#!/usr/bin/env python3
"""SSF2 MJPEG stream server — different approach from postMessage frame piping.

- Serves multipart MJPEG at http://127.0.0.1:<port>/stream.mjpg
- Captures the real AIR window at full client resolution (DPI-aware)
- View ADL off: park off-screen (no layered alpha — that yielded white frames).
  AIR often paints only a corner of a large client; crop to content (dark UI included).
  Stream native crop size (browser scales); optional max long-edge cap for smooth encode.
- Optional /focus brings the game to the foreground for play
"""

from __future__ import annotations

import argparse
import ctypes
import ctypes.wintypes as wt
import io
import json
import os
import struct
import sys
import threading
import time
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Iterable, Optional

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)

try:
    ctypes.windll.shcore.SetProcessDpiAwareness(2)
except Exception:
    try:
        user32.SetProcessDPIAware()
    except Exception:
        pass

PW_RENDERFULLCONTENT = 0x00000002
PW_CLIENTONLY = 0x00000001
SRCCOPY = 0x00CC0020
BI_RGB = 0
DIB_RGB_COLORS = 0
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
SW_RESTORE = 9
SW_SHOW = 5
INPUT_KEYBOARD = 1
KEYEVENTF_KEYUP = 0x0002
WM_MOUSEMOVE = 0x0200
WM_LBUTTONDOWN = 0x0201
WM_LBUTTONUP = 0x0202
WM_RBUTTONDOWN = 0x0204
WM_RBUTTONUP = 0x0205
WM_KEYDOWN = 0x0100
WM_KEYUP = 0x0101
WM_ACTIVATE = 0x0006
WM_SETFOCUS = 0x0007
WA_CLICKACTIVE = 2
MK_LBUTTON = 0x0001
MK_RBUTTON = 0x0002
GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_APPWINDOW = 0x00040000
WS_EX_LAYERED = 0x00080000
WS_EX_TRANSPARENT = 0x00000020
LWA_ALPHA = 0x00000002
DWMWA_CLOAK = 13
SWP_NOSIZE = 0x0001
SWP_NOZORDER = 0x0004
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
SWP_FRAMECHANGED = 0x0020
HWND_BOTTOM = 1
OFFSCREEN_X = -12800
OFFSCREEN_Y = -12800

try:
    dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True)
except Exception:
    dwmapi = None


class BITMAPINFOHEADER(ctypes.Structure):
    _fields_ = [
        ("biSize", wt.DWORD),
        ("biWidth", wt.LONG),
        ("biHeight", wt.LONG),
        ("biPlanes", wt.WORD),
        ("biBitCount", wt.WORD),
        ("biCompression", wt.DWORD),
        ("biSizeImage", wt.DWORD),
        ("biXPelsPerMeter", wt.LONG),
        ("biYPelsPerMeter", wt.LONG),
        ("biClrUsed", wt.DWORD),
        ("biClrImportant", wt.DWORD),
    ]


class BITMAPINFO(ctypes.Structure):
    _fields_ = [("bmiHeader", BITMAPINFOHEADER), ("bmiColors", wt.DWORD * 3)]


class KEYBDINPUT(ctypes.Structure):
    _fields_ = [
        ("wVk", wt.WORD),
        ("wScan", wt.WORD),
        ("dwFlags", wt.DWORD),
        ("time", wt.DWORD),
        ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong)),
    ]


class MOUSEINPUT(ctypes.Structure):
    _fields_ = [
        ("dx", wt.LONG),
        ("dy", wt.LONG),
        ("mouseData", wt.DWORD),
        ("dwFlags", wt.DWORD),
        ("time", wt.DWORD),
        ("dwExtraInfo", ctypes.POINTER(ctypes.c_ulong)),
    ]


class HARDWAREINPUT(ctypes.Structure):
    _fields_ = [("uMsg", wt.DWORD), ("wParamL", wt.WORD), ("wParamH", wt.WORD)]


class INPUT_UNION(ctypes.Union):
    _fields_ = [("ki", KEYBDINPUT), ("mi", MOUSEINPUT), ("hi", HARDWAREINPUT)]


class INPUT(ctypes.Structure):
    _fields_ = [("type", wt.DWORD), ("union", INPUT_UNION)]


user32.EnumWindows.argtypes = [ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM), wt.LPARAM]
user32.GetWindowTextW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.GetWindowTextLengthW.argtypes = [wt.HWND]
user32.GetClassNameW.argtypes = [wt.HWND, wt.LPWSTR, ctypes.c_int]
user32.IsWindowVisible.argtypes = [wt.HWND]
user32.IsWindow.argtypes = [wt.HWND]
user32.IsWindow.restype = wt.BOOL
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.c_void_p]
user32.GetWindowThreadProcessId.restype = wt.DWORD
user32.GetClientRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.PrintWindow.argtypes = [wt.HWND, wt.HDC, wt.UINT]
user32.GetDC.argtypes = [wt.HWND]
user32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
user32.ClientToScreen.argtypes = [wt.HWND, ctypes.POINTER(wt.POINT)]
user32.SetForegroundWindow.argtypes = [wt.HWND]
user32.ShowWindow.argtypes = [wt.HWND, ctypes.c_int]
user32.BringWindowToTop.argtypes = [wt.HWND]
user32.MapVirtualKeyW.argtypes = [wt.UINT, wt.UINT]
user32.MapVirtualKeyW.restype = wt.UINT
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.SendMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.SendInput.argtypes = [wt.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
user32.AttachThreadInput.argtypes = [wt.DWORD, wt.DWORD, wt.BOOL]
user32.GetForegroundWindow.restype = wt.HWND
user32.GetWindowLongW.argtypes = [wt.HWND, ctypes.c_int]
user32.SetWindowLongW.argtypes = [wt.HWND, ctypes.c_int, ctypes.c_long]
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.SetLayeredWindowAttributes.argtypes = [wt.HWND, ctypes.c_uint, ctypes.c_byte, wt.DWORD]
user32.SetLayeredWindowAttributes.restype = wt.BOOL
kernel32.GetCurrentThreadId.restype = wt.DWORD
kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
gdi32.CreateCompatibleDC.argtypes = [wt.HDC]
gdi32.CreateCompatibleBitmap.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int]
gdi32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
gdi32.BitBlt.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.HDC, ctypes.c_int, ctypes.c_int, wt.DWORD]
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.POINTER(BITMAPINFO), wt.UINT]
gdi32.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wt.HDC]


def eprint(*args: object) -> None:
    print(*args, file=sys.stderr, flush=True)


def process_image_name(pid: int) -> str:
    handle = kernel32.OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, False, pid)
    if not handle:
        return ""
    try:
        size = wt.DWORD(260)
        buf = ctypes.create_unicode_buffer(260)
        if kernel32.QueryFullProcessImageNameW(handle, 0, buf, ctypes.byref(size)):
            return buf.value
        return ""
    finally:
        kernel32.CloseHandle(handle)


def window_title(hwnd: int) -> str:
    length = user32.GetWindowTextLengthW(hwnd)
    if length <= 0:
        return ""
    buf = ctypes.create_unicode_buffer(length + 1)
    user32.GetWindowTextW(hwnd, buf, length + 1)
    return buf.value


def window_class(hwnd: int) -> str:
    buf = ctypes.create_unicode_buffer(256)
    user32.GetClassNameW(hwnd, buf, 256)
    return buf.value


def find_window(process_names: Iterable[str], title_hints: Iterable[str]) -> Optional[int]:
    names = {n.lower().removesuffix(".exe") for n in process_names}
    hints = [h.lower() for h in title_hints if h]
    air_classes = ("apolloruntimecontentwindow", "airapp", "adobeair")
    found: list[tuple[int, int, int]] = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def enum_proc(hwnd: int, _lparam: int) -> bool:
        pid = wt.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        path = process_image_name(pid.value)
        base = path.rsplit("\\", 1)[-1].lower().removesuffix(".exe") if path else ""
        title_l = window_title(hwnd).lower()
        class_l = window_class(hwnd).lower()
        if "cursor" in title_l or "visual studio code" in title_l:
            return True
        score = 0
        if base in names:
            score += 10
        if any(h in title_l for h in hints):
            score += 5
        if any(c in class_l for c in air_classes):
            score += 8
        # Allow cloaked windows when matching by process (View ADL off).
        if score < 10 and not user32.IsWindowVisible(hwnd):
            return True
        if score <= 0:
            return True
        rect = wt.RECT()
        user32.GetClientRect(hwnd, ctypes.byref(rect))
        area = max(0, rect.right - rect.left) * max(0, rect.bottom - rect.top)
        if area >= 100 * 100 or score >= 10:
            found.append((score, max(area, 1), int(hwnd)))
        return True

    user32.EnumWindows(enum_proc, 0)
    if not found:
        return None
    found.sort(key=lambda t: (t[0], t[1]), reverse=True)
    return found[0][2]


def crop_bgra(bgra: bytes, src_w: int, src_h: int, x: int, y: int, w: int, h: int) -> Optional[bytes]:
    if w <= 0 or h <= 0 or x < 0 or y < 0 or x + w > src_w or y + h > src_h:
        return None
    row = w * 4
    out = bytearray(h * row)
    for r in range(h):
        s = ((y + r) * src_w + x) * 4
        d = r * row
        out[d : d + row] = bgra[s : s + row]
    return bytes(out)


def is_mostly_black(bgra: bytes, sample_step: int = 64) -> bool:
    if not bgra:
        return True
    total = 0
    bright = 0
    for i in range(0, len(bgra) - 3, sample_step * 4):
        total += 1
        if bgra[i] + bgra[i + 1] + bgra[i + 2] > 24:
            bright += 1
    return total > 0 and bright / total < 0.01


def is_mostly_blank(bgra: bytes, sample_step: int = 64) -> bool:
    """True if frame is empty black or washed-out white (bad PrintWindow of invisible HWND)."""
    if not bgra:
        return True
    if is_mostly_black(bgra, sample_step):
        return True
    total = 0
    near_white = 0
    for i in range(0, len(bgra) - 3, sample_step * 4):
        total += 1
        if bgra[i] > 240 and bgra[i + 1] > 240 and bgra[i + 2] > 240:
            near_white += 1
    return total > 0 and near_white / total > 0.92


def content_bbox_bgra(
    bgra: bytes, width: int, height: int, thresh: int = 14
) -> Optional[tuple[int, int, int, int]]:
    """Bounding box of painted stage pixels. None if empty or already nearly full-frame.

    SSF2 UI uses very dark blues — expand edges with a low threshold so the
    title bar / top chrome is not treated as letterbox and cropped away.
    """
    if width < 8 or height < 8 or not bgra:
        return None
    step = max(1, min(width, height) // 180)
    min_x, min_y = width, height
    max_x, max_y = -1, -1
    for y in range(0, height, step):
        row = y * width * 4
        for x in range(0, width, step):
            i = row + x * 4
            if bgra[i] + bgra[i + 1] + bgra[i + 2] > thresh:
                if x < min_x:
                    min_x = x
                if y < min_y:
                    min_y = y
                if x > max_x:
                    max_x = x
                if y > max_y:
                    max_y = y
    if max_x < 0:
        return None

    def row_lit(y: int, t: int) -> bool:
        row = y * width * 4
        xs = max(1, width // 120)
        for x in range(0, width, xs):
            i = row + x * 4
            if bgra[i] + bgra[i + 1] + bgra[i + 2] > t:
                return True
        return False

    def col_lit(x: int, y0: int, y1: int, t: int) -> bool:
        ys = max(1, (y1 - y0 + 1) // 120)
        for y in range(y0, y1 + 1, ys):
            i = (y * width + x) * 4
            if bgra[i] + bgra[i + 1] + bgra[i + 2] > t:
                return True
        return False

    # Grow into dark UI (not pure letterbox) before padding.
    edge_t = 8
    while min_y > 0 and row_lit(min_y - 1, edge_t):
        min_y -= 1
    while max_y + 1 < height and row_lit(max_y + 1, edge_t):
        max_y += 1
    while min_x > 0 and col_lit(min_x - 1, min_y, max_y, edge_t):
        min_x -= 1
    while max_x + 1 < width and col_lit(max_x + 1, min_y, max_y, edge_t):
        max_x += 1

    cw0, ch0 = max_x - min_x + 1, max_y - min_y + 1
    # Small pad only — large pad shifted the stream vs Flash stage (mouse drift).
    pad = max(4, step)
    x0 = max(0, min_x - pad)
    y0 = max(0, min_y - pad)
    x1 = min(width - 1, max_x + pad)
    y1 = min(height - 1, max_y + pad)
    cw, ch = x1 - x0 + 1, y1 - y0 + 1
    if cw >= int(width * 0.90) and ch >= int(height * 0.90):
        return None
    if cw < 64 or ch < 64:
        return None
    return x0, y0, cw, ch


def printwindow_client(hwnd: int) -> Optional[tuple[int, int, bytes]]:
    """PrintWindow client area (preferred) or full HWND cropped to client."""
    wr, cr = wt.RECT(), wt.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(wr)):
        return None
    if not user32.GetClientRect(hwnd, ctypes.byref(cr)):
        return None
    pt = wt.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    win_w, win_h = wr.right - wr.left, wr.bottom - wr.top
    cw, ch = cr.right - cr.left, cr.bottom - cr.top
    if win_w <= 0 or win_h <= 0 or cw <= 0 or ch <= 0:
        return None
    ox, oy = pt.x - wr.left, pt.y - wr.top

    hwnd_dc = user32.GetDC(hwnd)
    mem_dc = gdi32.CreateCompatibleDC(hwnd_dc)

    def grab(bw: int, bh: int, flags: int) -> Optional[bytes]:
        bmp = gdi32.CreateCompatibleBitmap(hwnd_dc, bw, bh)
        old = gdi32.SelectObject(mem_dc, bmp)
        ok = user32.PrintWindow(hwnd, mem_dc, flags)
        if not ok and flags:
            ok = user32.PrintWindow(hwnd, mem_dc, flags & ~PW_RENDERFULLCONTENT)
        bmi = BITMAPINFO()
        ctypes.memset(ctypes.byref(bmi), 0, ctypes.sizeof(bmi))
        bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.bmiHeader.biWidth = bw
        bmi.bmiHeader.biHeight = -bh
        bmi.bmiHeader.biPlanes = 1
        bmi.bmiHeader.biBitCount = 32
        bmi.bmiHeader.biCompression = BI_RGB
        buf = (ctypes.c_ubyte * (bw * bh * 4))()
        got = gdi32.GetDIBits(mem_dc, bmp, 0, bh, buf, ctypes.byref(bmi), DIB_RGB_COLORS) if ok else 0
        gdi32.SelectObject(mem_dc, old)
        gdi32.DeleteObject(bmp)
        return bytes(buf) if got else None

    # Client-only is cheaper and avoids chrome; fall back to full window + crop.
    client = grab(cw, ch, PW_RENDERFULLCONTENT | PW_CLIENTONLY) or grab(cw, ch, PW_CLIENTONLY)
    if client is not None:
        gdi32.DeleteDC(mem_dc)
        user32.ReleaseDC(hwnd, hwnd_dc)
        return cw, ch, client

    full = grab(win_w, win_h, PW_RENDERFULLCONTENT) or grab(win_w, win_h, 0)
    gdi32.DeleteDC(mem_dc)
    user32.ReleaseDC(hwnd, hwnd_dc)
    if full is None:
        return None
    cropped = crop_bgra(full, win_w, win_h, ox, oy, cw, ch)
    if cropped is None:
        # Never stream title-bar chrome — that skews mouse Y vs client coords.
        return None
    return cw, ch, cropped


def capture_client(hwnd: int) -> Optional[tuple[int, int, bytes]]:
    wr, cr = wt.RECT(), wt.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(wr)):
        return None
    if not user32.GetClientRect(hwnd, ctypes.byref(cr)):
        return None
    pt = wt.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    cw, ch = cr.right - cr.left, cr.bottom - cr.top
    if cw <= 0 or ch <= 0:
        return None

    def grab_screen_client() -> Optional[tuple[int, int, bytes]]:
        screen = user32.GetDC(0)
        mem_dc = gdi32.CreateCompatibleDC(screen)
        bmp = gdi32.CreateCompatibleBitmap(screen, cw, ch)
        old = gdi32.SelectObject(mem_dc, bmp)
        gdi32.BitBlt(mem_dc, 0, 0, cw, ch, screen, pt.x, pt.y, SRCCOPY)
        bmi = BITMAPINFO()
        ctypes.memset(ctypes.byref(bmi), 0, ctypes.sizeof(bmi))
        bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
        bmi.bmiHeader.biWidth = cw
        bmi.bmiHeader.biHeight = -ch
        bmi.bmiHeader.biPlanes = 1
        bmi.bmiHeader.biBitCount = 32
        bmi.bmiHeader.biCompression = BI_RGB
        buf2 = (ctypes.c_ubyte * (cw * ch * 4))()
        got2 = gdi32.GetDIBits(mem_dc, bmp, 0, ch, buf2, ctypes.byref(bmi), DIB_RGB_COLORS)
        gdi32.SelectObject(mem_dc, old)
        gdi32.DeleteObject(bmp)
        gdi32.DeleteDC(mem_dc)
        user32.ReleaseDC(0, screen)
        if not got2:
            return None
        return cw, ch, bytes(buf2)

    iconic = bool(user32.IsIconic(hwnd))
    visible = bool(user32.IsWindowVisible(hwnd)) and not iconic
    parked = STATE.parked or (not STATE.view_adl)
    # Parked / cloaked / minimized: PrintWindow only (BitBlt would be empty/wrong).
    use_print = parked or iconic or not visible

    if not use_print:
        grabbed = grab_screen_client()
        if grabbed and not is_mostly_black(grabbed[2]):
            return grabbed

    frame = printwindow_client(hwnd)
    if frame and is_mostly_black(frame[2]) and parked and not iconic:
        time.sleep(0.02)
        frame = printwindow_client(hwnd) or frame
    return frame


def upscale_bgra_to(
    width: int, height: int, bgra: bytes, want_w: int, want_h: int
) -> tuple[int, int, bytes]:
    """Nearest-neighbor resize (up or down) to an exact size."""
    if want_w <= 0 or want_h <= 0 or (width == want_w and height == want_h):
        return width, height, bgra
    from PIL import Image

    img = Image.frombuffer("RGBA", (width, height), bgra, "raw", "BGRA", 0, 1)
    scaled = img.resize((want_w, want_h), Image.Resampling.BILINEAR)
    out = scaled.tobytes("raw", "BGRA")
    return want_w, want_h, out


def letterbox_bgra_to(
    width: int, height: int, bgra: bytes, want_w: int, want_h: int
) -> tuple[int, int, bytes]:
    """Fit content into want_w×want_h with black bars (no stretch)."""
    if want_w <= 0 or want_h <= 0:
        return width, height, bgra
    if width == want_w and height == want_h:
        return width, height, bgra
    from PIL import Image

    img = Image.frombuffer("RGBA", (width, height), bgra, "raw", "BGRA", 0, 1)
    scale = min(want_w / float(width), want_h / float(height))
    nw = max(1, int(round(width * scale)))
    nh = max(1, int(round(height * scale)))
    scaled = img.resize((nw, nh), Image.Resampling.BILINEAR)
    canvas = Image.new("RGBA", (want_w, want_h), (0, 0, 0, 255))
    ox = (want_w - nw) // 2
    oy = (want_h - nh) // 2
    canvas.paste(scaled, (ox, oy))
    out = canvas.tobytes("raw", "BGRA")
    return want_w, want_h, out


def parked_target_size() -> tuple[int, int]:
    """Fixed encode size while View ADL is off / parked."""
    if STATE.saved_client and STATE.saved_client[0] >= 64 and STATE.saved_client[1] >= 64:
        return STATE.saved_client
    sw, sh = STATE.stage_size
    if sw >= 64 and sh >= 64:
        return (sw, sh)
    return (640, 360)


def fit_long_edge(
    width: int, height: int, bgra: bytes, max_long: int
) -> tuple[int, int, bytes]:
    """Downscale so max(w,h) <= max_long. Browser scales the rest."""
    if max_long <= 0:
        return width, height, bgra
    long = max(width, height)
    if long <= max_long:
        return width, height, bgra
    scale = max_long / float(long)
    nw = max(1, int(round(width * scale)))
    nh = max(1, int(round(height * scale)))
    return upscale_bgra_to(width, height, bgra, nw, nh)


def encode_jpeg(width: int, height: int, bgra: bytes, quality: int) -> bytes:
    from PIL import Image

    img = Image.frombuffer("RGBA", (width, height), bgra, "raw", "BGRA", 0, 1).convert("RGB")
    out = io.BytesIO()
    img.save(out, format="JPEG", quality=quality, subsampling=0, optimize=False)
    return out.getvalue()


def solid_black_bgra(width: int, height: int) -> bytes:
    """BGRA opaque black for blank-frame replacement."""
    w = max(1, int(width))
    h = max(1, int(height))
    row = bytes([0, 0, 0, 255]) * w
    return row * h


_BLACK_JPEG_CACHE: dict[tuple[int, int, int], bytes] = {}


def encode_black_jpeg(width: int, height: int, quality: int) -> bytes:
    key = (max(1, int(width)), max(1, int(height)), int(quality))
    cached = _BLACK_JPEG_CACHE.get(key)
    if cached:
        return cached
    jpeg = encode_jpeg(key[0], key[1], solid_black_bgra(key[0], key[1]), key[2])
    if len(_BLACK_JPEG_CACHE) > 8:
        _BLACK_JPEG_CACHE.clear()
    _BLACK_JPEG_CACHE[key] = jpeg
    return jpeg


class Shared:
    def __init__(self) -> None:
        self.lock = threading.Lock()
        self.jpeg: bytes = b""
        self.bounds = (0, 0)
        self.hwnd: Optional[int] = None
        self.alive = True
        self.status = "waiting"
        self.input_forward = False
        self.view_adl = True
        self.parked = False
        # Only park when the extension launched this game (Debug / Launch SSF2).
        self.owned = False
        self.keep_active = False  # play-focus: force foreground for gamepad while parked
        self.saved_rect: Optional[tuple[int, int, int, int]] = None  # x, y, w, h
        self.saved_client: Optional[tuple[int, int]] = None  # client w, h
        self.saved_exstyle: Optional[int] = None
        # Legacy; cleared when not cropped. hit_rect always matches encoded pixels.
        self.paint_crop: Optional[tuple[int, int, int, int]] = None  # x, y, w, h
        # Client-space rect matching the pixels encoded in the current JPEG (pre long-edge fit).
        self.hit_rect: Optional[tuple[int, int, int, int]] = None
        # Absolute path to data/ssf2mm/cursor.json (mod writes hand cursor state).
        self.cursor_file: str = ""
        # Game stage size for SHOW_ALL letterbox mouse mapping when parked+cropped.
        self.stage_size: tuple[int, int] = (640, 360)


STATE = Shared()


def load_stage_size_from_bridge() -> None:
    """Optional stage.json from mod: {"width":640,"height":360}."""
    path = STATE.cursor_file
    if not path:
        return
    stage_path = os.path.join(os.path.dirname(path), "stage.json")
    if not os.path.isfile(stage_path):
        return
    try:
        with open(stage_path, "r", encoding="utf-8") as f:
            data = json.load(f)
        w = int(data.get("width") or 0)
        h = int(data.get("height") or 0)
        if w >= 64 and h >= 64:
            STATE.stage_size = (w, h)
    except Exception:
        pass


def map_norm_letterbox(nx: float, ny: float, cw: int, ch: int, sw: int, sh: int) -> tuple[int, int]:
    """Map stream-normalized coords (stage content) into client via SHOW_ALL letterbox."""
    if cw < 1 or ch < 1:
        return 0, 0
    if sw < 1 or sh < 1:
        return int(nx * max(0, cw - 1)), int(ny * max(0, ch - 1))
    scale = min(cw / float(sw), ch / float(sh))
    vw = sw * scale
    vh = sh * scale
    ox = (cw - vw) * 0.5
    oy = (ch - vh) * 0.5
    x = int(ox + nx * max(0.0, vw - 1.0))
    y = int(oy + ny * max(0.0, vh - 1.0))
    return max(0, min(cw - 1, x)), max(0, min(ch - 1, y))


def write_pointer_json(nx: float, ny: float, x: int, y: int) -> None:
    path = STATE.cursor_file
    if not path:
        return
    ptr = os.path.join(os.path.dirname(path), "pointer.json")
    try:
        with open(ptr, "w", encoding="utf-8") as f:
            json.dump({"nx": nx, "ny": ny, "x": x, "y": y}, f, separators=(",", ":"))
    except Exception:
        pass


def cloak_window(hwnd: int, cloak: bool) -> None:
    if not dwmapi:
        return
    try:
        val = ctypes.c_int(1 if cloak else 0)
        dwmapi.DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ctypes.byref(val), ctypes.sizeof(val))
    except Exception:
        pass


def _read_window_rect(hwnd: int) -> Optional[tuple[int, int, int, int]]:
    wr = wt.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(wr)):
        return None
    return wr.left, wr.top, wr.right - wr.left, wr.bottom - wr.top


def _read_client_size(hwnd: int) -> Optional[tuple[int, int]]:
    cr = wt.RECT()
    if not user32.GetClientRect(hwnd, ctypes.byref(cr)):
        return None
    w, h = cr.right - cr.left, cr.bottom - cr.top
    if w <= 0 or h <= 0:
        return None
    return w, h


def _is_on_screen_rect(rect: tuple[int, int, int, int]) -> bool:
    x, y, w, h = rect
    return x > -5000 and y > -5000 and w > 64 and h > 64


def _remember_home(hwnd: int) -> None:
    rect = _read_window_rect(hwnd)
    if rect and _is_on_screen_rect(rect):
        STATE.saved_rect = rect
    client = _read_client_size(hwnd)
    if client:
        STATE.saved_client = client
    if STATE.saved_exstyle is None:
        try:
            STATE.saved_exstyle = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        except Exception:
            STATE.saved_exstyle = None


def park_game_window(hwnd: int) -> None:
    """Hide by parking off-screen at the same outer size (AIR keeps painting).

    Layered alpha=0 made PrintWindow return white — do not use it for hide.
    Stream size is preserved by upscaling captures to saved_client.
    Never park windows we did not launch (standalone exe / external attach).
    """
    if not STATE.owned:
        eprint("VIEW:skip park (not owned)")
        return
    if user32.IsIconic(hwnd):
        user32.ShowWindow(hwnd, SW_RESTORE)
    user32.ShowWindow(hwnd, SW_SHOW)
    _remember_home(hwnd)
    # Clear any previous layered/transparent hide from older builds.
    try:
        ex = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        if STATE.saved_exstyle is None:
            STATE.saved_exstyle = ex
        cleared = ex & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT)
        if cleared != ex:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, cleared)
    except Exception:
        pass
    cloak_window(hwnd, False)
    rect = STATE.saved_rect or _read_window_rect(hwnd) or (80, 80, 960, 540)
    _x, _y, w, h = rect
    w, h = max(320, w), max(240, h)
    user32.SetWindowPos(
        hwnd,
        HWND_BOTTOM,
        OFFSCREEN_X,
        OFFSCREEN_Y,
        w,
        h,
        SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_FRAMECHANGED,
    )
    STATE.parked = True
    STATE.view_adl = False
    STATE.paint_crop = None
    STATE.hit_rect = None
    eprint("VIEW:0")


def unpark_game_window(hwnd: int) -> None:
    cloak_window(hwnd, False)
    try:
        ex = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
        if STATE.saved_exstyle is not None:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, STATE.saved_exstyle)
        else:
            user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex & ~(WS_EX_LAYERED | WS_EX_TRANSPARENT))
    except Exception:
        pass
    user32.ShowWindow(hwnd, SW_RESTORE)
    user32.ShowWindow(hwnd, SW_SHOW)
    if STATE.saved_rect and _is_on_screen_rect(STATE.saved_rect):
        x, y, w, h = STATE.saved_rect
    else:
        x, y = 80, 80
        cur = _read_window_rect(hwnd)
        w, h = (max(320, cur[2]), max(240, cur[3])) if cur else (960, 540)
    user32.SetWindowPos(
        hwnd,
        0,
        x,
        y,
        w,
        h,
        SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED,
    )
    STATE.parked = False
    STATE.view_adl = True
    STATE.paint_crop = None
    STATE.hit_rect = None
    eprint("VIEW:1")


def cleanup_tracked_window() -> None:
    """Unpark so the game is not left invisible after the helper exits."""
    hwnd = STATE.hwnd
    if hwnd and user32.IsWindow(hwnd):
        if STATE.parked or not STATE.view_adl:
            unpark_game_window(hwnd)
        else:
            cloak_window(hwnd, False)


def restore_game_window() -> bool:
    hwnd = STATE.hwnd or find_window(
        ["adl", "Super Smash Flash 2 Beta"],
        ["Super Smash Flash", "SSF2", "adl"],
    )
    if not hwnd or not user32.IsWindow(hwnd):
        return False
    STATE.hwnd = hwnd
    unpark_game_window(hwnd)
    return True


def set_view_adl(visible: bool) -> None:
    """Apply View ADL. External (unowned) windows are never parked or treated as hidden."""
    hwnd = STATE.hwnd
    if not STATE.owned:
        STATE.view_adl = True
        if hwnd and user32.IsWindow(hwnd) and STATE.parked:
            unpark_game_window(hwnd)
        return
    if not hwnd or not user32.IsWindow(hwnd):
        STATE.view_adl = bool(visible)
        return
    if visible:
        unpark_game_window(hwnd)
    else:
        park_game_window(hwnd)


def _activate_for_input(hwnd: int) -> None:
    """Wake AIR input routing without bringing the window on-screen."""
    user32.SendMessageW(hwnd, WM_ACTIVATE, WA_CLICKACTIVE, 0)
    user32.SendMessageW(hwnd, WM_SETFOCUS, 0, 0)


def force_foreground_offscreen(hwnd: int) -> None:
    """Make the parked HWND foreground so AIR polls XInput/gamepad, keep it off-screen."""
    if not hwnd or not user32.IsWindow(hwnd):
        return
    fg = user32.GetForegroundWindow()
    if fg == hwnd:
        # Still re-assert activation; some pads drop when focus feels stale.
        _activate_for_input(hwnd)
        return
    pid = wt.DWORD()
    tid_fg = user32.GetWindowThreadProcessId(fg, ctypes.byref(pid)) if fg else 0
    tid_target = user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
    tid_this = kernel32.GetCurrentThreadId()
    attached_fg = False
    attached_tg = False
    try:
        if tid_fg and tid_fg != tid_this:
            attached_fg = bool(user32.AttachThreadInput(tid_this, tid_fg, True))
        if tid_target and tid_target != tid_this:
            attached_tg = bool(user32.AttachThreadInput(tid_this, tid_target, True))
        user32.ShowWindow(hwnd, SW_SHOW)
        user32.SetWindowPos(
            hwnd,
            0,
            OFFSCREEN_X,
            OFFSCREEN_Y,
            0,
            0,
            SWP_NOSIZE | SWP_NOZORDER | SWP_SHOWWINDOW,
        )
        user32.SetForegroundWindow(hwnd)
        user32.BringWindowToTop(hwnd)
        _activate_for_input(hwnd)
    except Exception:
        pass
    finally:
        if attached_tg:
            user32.AttachThreadInput(tid_this, tid_target, False)
        if attached_fg:
            user32.AttachThreadInput(tid_this, tid_fg, False)


def send_key(vk: int, down: bool) -> None:
    if not STATE.input_forward:
        return
    hwnd = STATE.hwnd
    # SendInput always hits the focused app (Cursor). When parked, post to the game HWND.
    if hwnd and (STATE.parked or not STATE.view_adl):
        _activate_for_input(hwnd)
        scan = user32.MapVirtualKeyW(vk, 0) & 0xFF
        lp = 1 | (scan << 16)
        if not down:
            lp |= (1 << 30) | (1 << 31)
        user32.PostMessageW(hwnd, WM_KEYDOWN if down else WM_KEYUP, vk, lp)
        return
    inp = INPUT()
    inp.type = INPUT_KEYBOARD
    inp.union.ki = KEYBDINPUT(vk, 0, 0 if down else KEYEVENTF_KEYUP, 0, None)
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def send_mouse(nx: float, ny: float, action: str, button: int) -> None:
    if not STATE.input_forward:
        return
    hwnd = STATE.hwnd
    if not hwnd:
        return
    if STATE.parked or not STATE.view_adl:
        _activate_for_input(hwnd)
    nx = max(0.0, min(1.0, nx))
    ny = max(0.0, min(1.0, ny))
    load_stage_size_from_bridge()

    rect = wt.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rect))
    client_w = max(0, rect.right - rect.left)
    client_h = max(0, rect.bottom - rect.top)

    parked = STATE.parked or (not STATE.view_adl)
    # Parked stream is cropped to stage pixels (often top-left in the buffer), but Flash
    # maps mouse through full-client SHOW_ALL letterboxing — use that, not paint_crop.
    if parked and STATE.paint_crop:
        sw, sh = STATE.stage_size
        x, y = map_norm_letterbox(nx, ny, client_w, client_h, sw, sh)
    elif STATE.hit_rect and STATE.hit_rect[2] > 0 and STATE.hit_rect[3] > 0:
        cx, cy, cw, ch = STATE.hit_rect
        x = cx + int(nx * max(0, cw - 1))
        y = cy + int(ny * max(0, ch - 1))
    else:
        x = int(nx * max(0, client_w - 1))
        y = int(ny * max(0, client_h - 1))

    write_pointer_json(nx, ny, x, y)
    lp = (y << 16) | (x & 0xFFFF)
    if action == "m":
        user32.SendMessageW(hwnd, WM_MOUSEMOVE, MK_LBUTTON if button == 0 else 0, lp)
        return
    if button == 0:
        down_m, up_m, mk = WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON
    else:
        down_m, up_m, mk = WM_RBUTTONDOWN, WM_RBUTTONUP, MK_RBUTTON
    if action == "d":
        user32.SendMessageW(hwnd, WM_MOUSEMOVE, 0, lp)
        user32.SendMessageW(hwnd, down_m, mk, lp)
    elif action == "u":
        user32.SendMessageW(hwnd, up_m, 0, lp)


def capture_loop(
    processes: list[str],
    titles: list[str],
    fps: float,
    quality: int,
    wait: float,
    max_long_edge: int,
) -> None:
    from PIL import Image  # noqa: F401

    target_fps = max(5.0, min(60.0, fps))
    target_dt = 1.0 / target_fps
    q = int(quality)
    q_min, q_max = 70, max(quality, 95)
    active_pulse = 0

    deadline = time.time() + wait
    hwnd: Optional[int] = None
    while STATE.alive and time.time() < deadline:
        hwnd = find_window(processes, titles)
        if hwnd:
            break
        STATE.status = "waiting"
        eprint("WAITING")
        time.sleep(0.35)
    if not hwnd:
        STATE.status = "missing"
        eprint("ERROR:SSF2 window not found - is the game running?")
        return

    STATE.hwnd = hwnd
    STATE.status = "streaming"
    eprint(f"HWND:{hwnd}")
    eprint(f"FPS_TARGET:{target_fps}")
    eprint(f"MAX_LONG:{max_long_edge}")

    while STATE.alive:
        loop_start = time.perf_counter()
        if not user32.IsWindow(hwnd):
            STATE.status = "closed"
            STATE.hwnd = None
            eprint("ERROR:window closed")
            hwnd = None
            while STATE.alive and hwnd is None:
                hwnd = find_window(processes, titles)
                STATE.status = "waiting"
                eprint("WAITING")
                time.sleep(0.5)
            if not hwnd:
                break
            # Reattached after close — treat as external; never park.
            STATE.owned = False
            STATE.hwnd = hwnd
            STATE.status = "streaming"
            eprint(f"HWND:{hwnd}")
            eprint("OWNED:0")
            eprint("VIEW:reattach external — View ADL forced on (unowned)")
            if STATE.parked or not STATE.view_adl:
                unpark_game_window(hwnd)
            continue

        frame = capture_client(hwnd)
        if frame is None:
            time.sleep(0.01)
            continue

        w, h, bgra = frame
        parked = STATE.parked or (not STATE.view_adl)
        # Only Play Focus steals OS foreground. Input-forward alone uses PostMessage
        # and must not yank focus from Cursor when View ADL is off.
        if parked and STATE.keep_active:
            active_pulse += 1
            # ~1 Hz @ 30fps — avoid thrashing Cursor focus every few frames.
            if active_pulse % 30 == 1:
                force_foreground_offscreen(hwnd)

        hit = (0, 0, w, h)
        if parked:
            # Crop to painted stage every frame so stream fills the panel; mouse uses same rect.
            bbox = content_bbox_bgra(bgra, w, h)
            if bbox:
                cx, cy, cw, ch = bbox
                if cx + cw <= w and cy + ch <= h:
                    cropped = crop_bgra(bgra, w, h, cx, cy, cw, ch)
                    if cropped:
                        STATE.paint_crop = (cx, cy, cw, ch)
                        hit = (cx, cy, cw, ch)
                        w, h, bgra = cw, ch, cropped
                    else:
                        STATE.paint_crop = None
                else:
                    STATE.paint_crop = None
            else:
                STATE.paint_crop = None
            # Keep JPEG / BOUNDS fixed so the webview does not resize with content.
            tw, th = parked_target_size()
            w, h, bgra = letterbox_bgra_to(w, h, bgra, tw, th)
        else:
            STATE.paint_crop = None

        STATE.hit_rect = hit

        if parked and is_mostly_blank(bgra):
            # Real black/blank screens must not keep the previous JPEG frozen.
            try:
                enc_w, enc_h, _ = fit_long_edge(w, h, bgra, max_long_edge)
                jpeg = encode_black_jpeg(enc_w, enc_h, q)
            except Exception as exc:  # noqa: BLE001
                eprint(f"ERROR:encode black {exc}")
                remaining = target_dt - (time.perf_counter() - loop_start)
                if remaining > 0:
                    time.sleep(remaining)
                continue
            with STATE.lock:
                STATE.jpeg = jpeg
                if STATE.bounds != (enc_w, enc_h):
                    STATE.bounds = (enc_w, enc_h)
                    eprint(f"BOUNDS:{enc_w},{enc_h}")
            remaining = target_dt - (time.perf_counter() - loop_start)
            if remaining > 0:
                time.sleep(remaining)
            continue

        w, h, bgra = fit_long_edge(w, h, bgra, max_long_edge)
        try:
            jpeg = encode_jpeg(w, h, bgra, q)
        except Exception as exc:  # noqa: BLE001
            eprint(f"ERROR:encode {exc}")
            time.sleep(0.01)
            continue
        with STATE.lock:
            STATE.jpeg = jpeg
            if STATE.bounds != (w, h):
                STATE.bounds = (w, h)
                eprint(f"BOUNDS:{w},{h}")
            if not parked and w >= 64 and h >= 64:
                STATE.saved_client = (w, h)

        elapsed = time.perf_counter() - loop_start
        # Prefer holding FPS; only drop quality when clearly behind.
        if elapsed > target_dt * 1.05:
            nq = max(q_min, q - 2)
            if nq != q:
                q = nq
                eprint(f"QUALITY:{q}")
        elif elapsed < target_dt * 0.55 and q < q_max:
            q = min(q_max, q + 1)
            eprint(f"QUALITY:{q}")

        remaining = target_dt - (time.perf_counter() - loop_start)
        if remaining > 0:
            time.sleep(remaining)


def set_bridge_dir(bridge_dir: str) -> None:
    """Point at build/data/ssf2mm so we can read cursor.json from the mod."""
    d = (bridge_dir or "").strip()
    if not d:
        STATE.cursor_file = ""
        return
    STATE.cursor_file = os.path.join(os.path.normpath(d), "cursor.json")
    eprint(f"CURSOR_FILE:{STATE.cursor_file}")
    load_stage_size_from_bridge()


def read_cursor_payload() -> dict:
    path = STATE.cursor_file
    if not path or not os.path.isfile(path):
        return {"hand": False}
    try:
        with open(path, "r", encoding="utf-8") as f:
            data = json.load(f)
        if not isinstance(data, dict):
            return {"hand": False}
        return {"hand": bool(data.get("hand")), "utc": data.get("utc") or ""}
    except Exception:
        return {"hand": False}


class Handler(BaseHTTPRequestHandler):
    def log_message(self, fmt: str, *args: object) -> None:
        return

    def _cors(self) -> None:
        self.send_header("Access-Control-Allow-Origin", "*")
        self.send_header("Access-Control-Allow-Methods", "GET, POST, OPTIONS")
        self.send_header("Access-Control-Allow-Headers", "Content-Type")
        self.send_header("Cache-Control", "no-store")

    def do_OPTIONS(self) -> None:  # noqa: N802
        self.send_response(204)
        self._cors()
        self.end_headers()

    def do_GET(self) -> None:  # noqa: N802
        if self.path.startswith("/health"):
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self._cors()
            self.end_headers()
            body = json.dumps(
                {
                    "status": STATE.status,
                    "bounds": STATE.bounds,
                    "hwnd": STATE.hwnd,
                    "inputForward": STATE.input_forward,
                    "viewAdl": STATE.view_adl,
                }
            ).encode()
            self.wfile.write(body)
            return

        if self.path.startswith("/cursor"):
            payload = read_cursor_payload()
            body = json.dumps(payload).encode()
            self.send_response(200)
            self.send_header("Content-Type", "application/json")
            self.send_header("Content-Length", str(len(body)))
            self._cors()
            self.end_headers()
            self.wfile.write(body)
            return

        if self.path.startswith("/stream"):
            self.send_response(200)
            self.send_header("Content-Type", "multipart/x-mixed-replace; boundary=frame")
            self._cors()
            self.end_headers()
            try:
                while STATE.alive:
                    with STATE.lock:
                        jpeg = STATE.jpeg
                    if jpeg:
                        self.wfile.write(b"--frame\r\n")
                        self.wfile.write(b"Content-Type: image/jpeg\r\n")
                        self.wfile.write(f"Content-Length: {len(jpeg)}\r\n\r\n".encode())
                        self.wfile.write(jpeg)
                        self.wfile.write(b"\r\n")
                        self.wfile.flush()
                    time.sleep(0.01)
            except (BrokenPipeError, ConnectionResetError, ConnectionAbortedError):
                return
            return

        if self.path == "/" or self.path.startswith("/view"):
            html = """<!DOCTYPE html><html><body style="margin:0;background:#000">
<img id="s" style="width:100%;height:100vh;object-fit:contain;image-rendering:pixelated" />
<script>document.getElementById('s').src='/stream.mjpg?'+Date.now();</script>
</body></html>"""
            data = html.encode()
            self.send_response(200)
            self.send_header("Content-Type", "text/html; charset=utf-8")
            self.send_header("Content-Length", str(len(data)))
            self._cors()
            self.end_headers()
            self.wfile.write(data)
            return

        self.send_response(404)
        self.end_headers()

    def do_POST(self) -> None:  # noqa: N802
        length = int(self.headers.get("Content-Length", "0"))
        raw = self.rfile.read(length) if length else b"{}"
        try:
            msg = json.loads(raw.decode("utf-8"))
        except Exception:
            msg = {}
        kind = (msg.get("type") or "").lower()
        try:
            if kind == "config":
                if "owned" in msg:
                    was_owned = STATE.owned
                    STATE.owned = bool(msg["owned"])
                    eprint(f"OWNED:{1 if STATE.owned else 0}")
                    if was_owned and not STATE.owned and STATE.hwnd and user32.IsWindow(STATE.hwnd):
                        unpark_game_window(STATE.hwnd)
                if "inputForward" in msg:
                    STATE.input_forward = bool(msg["inputForward"])
                    eprint(f"INPUT:{1 if STATE.input_forward else 0}")
                if "viewAdl" in msg:
                    set_view_adl(bool(msg["viewAdl"]))
                if "keepActive" in msg:
                    STATE.keep_active = bool(msg["keepActive"])
                    eprint(f"ACTIVE:{1 if STATE.keep_active else 0}")
                    if STATE.keep_active and STATE.hwnd and (STATE.parked or not STATE.view_adl):
                        force_foreground_offscreen(STATE.hwnd)
                if "bridgeDir" in msg and msg["bridgeDir"]:
                    set_bridge_dir(str(msg["bridgeDir"]))
            elif kind == "restore":
                restore_game_window()
            elif kind == "key":
                send_key(int(msg["vk"]), bool(msg.get("down", True)))
            elif kind == "mouse":
                send_mouse(
                    float(msg["nx"]),
                    float(msg["ny"]),
                    str(msg.get("action", "m")),
                    int(msg.get("button", 0)),
                )
        except Exception as exc:  # noqa: BLE001
            eprint(f"ERROR:input {exc}")
        self.send_response(204)
        self._cors()
        self.end_headers()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--port", type=int, default=0)
    parser.add_argument("--fps", type=float, default=30)
    parser.add_argument("--quality", type=int, default=92)
    parser.add_argument("--wait", type=float, default=60)
    parser.add_argument(
        "--max-long-edge",
        type=int,
        default=1280,
        help="Downscale stream frames so max(w,h) <= this (0 = no cap). Keeps quality high without lag.",
    )
    parser.add_argument("--process", action="append", default=[])
    parser.add_argument("--title", action="append", default=[])
    parser.add_argument(
        "--bridge-dir",
        default="",
        help="Path to data/ssf2mm (reads cursor.json for hand cursor).",
    )
    args = parser.parse_args()

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        eprint("ERROR:Pillow required: pip install Pillow")
        return 2

    if args.bridge_dir:
        set_bridge_dir(args.bridge_dir)

    processes = args.process or ["adl", "Super Smash Flash 2 Beta"]
    titles = args.title or ["Super Smash Flash", "SSF2", "adl"]

    server = ThreadingHTTPServer(("127.0.0.1", args.port), Handler)
    port = server.server_address[1]
    eprint(f"PORT:{port}")
    eprint(f"URL:http://127.0.0.1:{port}/stream.mjpg")

    t = threading.Thread(
        target=capture_loop,
        args=(processes, titles, args.fps, args.quality, args.wait, args.max_long_edge),
        daemon=True,
    )
    t.start()

    try:
        server.serve_forever(poll_interval=0.3)
    except KeyboardInterrupt:
        pass
    finally:
        STATE.alive = False
        cleanup_tracked_window()
        server.shutdown()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
