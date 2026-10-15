#!/usr/bin/env python3
"""Capture an SSF2 / AIR window and stream JPEG frames to stdout.

Protocol:
  stderr: text status lines (BOUNDS:w,h / HWND:n / HIDDEN:0|1 / ERROR:... / WAITING)
  stdout: repeating [u32 big-endian length][jpeg bytes]
  stdin:  lines KEY d|u <vk> | MOUSE d|u|m <nx> <ny> [btn] | FOCUS | QUIT
"""

from __future__ import annotations

import argparse
import atexit
import ctypes
import ctypes.wintypes as wt
import io
import struct
import sys
import threading
import time
from typing import Iterable, Optional

user32 = ctypes.WinDLL("user32", use_last_error=True)
gdi32 = ctypes.WinDLL("gdi32", use_last_error=True)
kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
dwmapi = ctypes.WinDLL("dwmapi", use_last_error=True)


def enable_dpi_awareness() -> None:
    """Capture at real pixel size so the stream is not soft/blurry on scaled displays."""
    try:
        ctypes.windll.shcore.SetProcessDpiAwareness(2)  # PROCESS_PER_MONITOR_DPI_AWARE
        return
    except Exception:  # noqa: BLE001
        pass
    try:
        user32.SetProcessDPIAware()
    except Exception:  # noqa: BLE001
        pass


enable_dpi_awareness()

PW_RENDERFULLCONTENT = 0x00000002
SRCCOPY = 0x00CC0020
BI_RGB = 0
DIB_RGB_COLORS = 0
INPUT_KEYBOARD = 1
INPUT_MOUSE = 0
KEYEVENTF_KEYUP = 0x0002
MOUSEEVENTF_MOVE = 0x0001
MOUSEEVENTF_ABSOLUTE = 0x8000
MOUSEEVENTF_LEFTDOWN = 0x0002
MOUSEEVENTF_LEFTUP = 0x0004
MOUSEEVENTF_RIGHTDOWN = 0x0008
MOUSEEVENTF_RIGHTUP = 0x0010
SW_SHOW = 5
PROCESS_QUERY_LIMITED_INFORMATION = 0x1000
GWL_EXSTYLE = -20
WS_EX_TOOLWINDOW = 0x00000080
WS_EX_APPWINDOW = 0x00040000
HWND_BOTTOM = 1
SWP_NOSIZE = 0x0001
SWP_NOZORDER = 0x0004
SWP_NOACTIVATE = 0x0010
SWP_SHOWWINDOW = 0x0040
SWP_FRAMECHANGED = 0x0020
WM_MOUSEMOVE = 0x0200
WM_LBUTTONDOWN = 0x0201
WM_LBUTTONUP = 0x0202
WM_RBUTTONDOWN = 0x0204
WM_RBUTTONUP = 0x0205
MK_LBUTTON = 0x0001
MK_RBUTTON = 0x0002
DWMWA_CLOAK = 13
OFFSCREEN_X = -12800
OFFSCREEN_Y = -12800


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
user32.GetWindowRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.SetWindowPos.argtypes = [wt.HWND, wt.HWND, ctypes.c_int, ctypes.c_int, ctypes.c_int, ctypes.c_int, wt.UINT]
user32.GetWindowLongW.argtypes = [wt.HWND, ctypes.c_int]
user32.SetWindowLongW.argtypes = [wt.HWND, ctypes.c_int, ctypes.c_long]
user32.GetForegroundWindow.restype = wt.HWND
user32.AttachThreadInput.argtypes = [wt.DWORD, wt.DWORD, wt.BOOL]
user32.BringWindowToTop.argtypes = [wt.HWND]
user32.PostMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.SendMessageW.argtypes = [wt.HWND, wt.UINT, wt.WPARAM, wt.LPARAM]
user32.IsWindowVisible.argtypes = [wt.HWND]
user32.IsWindow.argtypes = [wt.HWND]
user32.IsWindow.restype = wt.BOOL
user32.GetWindowThreadProcessId.argtypes = [wt.HWND, ctypes.c_void_p]
user32.GetWindowThreadProcessId.restype = wt.DWORD
user32.GetClientRect.argtypes = [wt.HWND, ctypes.POINTER(wt.RECT)]
user32.PrintWindow.argtypes = [wt.HWND, wt.HDC, wt.UINT]
user32.GetDC.argtypes = [wt.HWND]
user32.ReleaseDC.argtypes = [wt.HWND, wt.HDC]
user32.ClientToScreen.argtypes = [wt.HWND, ctypes.POINTER(wt.POINT)]
user32.SetForegroundWindow.argtypes = [wt.HWND]
user32.ShowWindow.argtypes = [wt.HWND, ctypes.c_int]
user32.SendInput.argtypes = [wt.UINT, ctypes.POINTER(INPUT), ctypes.c_int]
user32.GetSystemMetrics.argtypes = [ctypes.c_int]

gdi32.CreateCompatibleDC.argtypes = [wt.HDC]
gdi32.CreateCompatibleBitmap.argtypes = [wt.HDC, ctypes.c_int, ctypes.c_int]
gdi32.SelectObject.argtypes = [wt.HDC, wt.HGDIOBJ]
gdi32.BitBlt.argtypes = [
    wt.HDC,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    ctypes.c_int,
    wt.HDC,
    ctypes.c_int,
    ctypes.c_int,
    wt.DWORD,
]
gdi32.GetDIBits.argtypes = [wt.HDC, wt.HBITMAP, wt.UINT, wt.UINT, ctypes.c_void_p, ctypes.POINTER(BITMAPINFO), wt.UINT]
gdi32.DeleteObject.argtypes = [wt.HGDIOBJ]
gdi32.DeleteDC.argtypes = [wt.HDC]

kernel32.OpenProcess.argtypes = [wt.DWORD, wt.BOOL, wt.DWORD]
kernel32.QueryFullProcessImageNameW.argtypes = [wt.HANDLE, wt.DWORD, wt.LPWSTR, ctypes.POINTER(wt.DWORD)]
kernel32.CloseHandle.argtypes = [wt.HANDLE]
kernel32.GetCurrentThreadId.restype = wt.DWORD

try:
    dwmapi.DwmSetWindowAttribute.argtypes = [wt.HWND, ctypes.c_uint, ctypes.c_void_p, ctypes.c_uint]
except Exception:  # noqa: BLE001
    pass


class WindowState:
    def __init__(self) -> None:
        self.rect = (0, 0, 800, 600)
        self.exstyle = 0
        self.hidden = False


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
    air_classes = ("apolloruntimecontentwindow", "airapp", "gfh_", "adobeair")
    found: list[tuple[int, int, int]] = []

    @ctypes.WINFUNCTYPE(wt.BOOL, wt.HWND, wt.LPARAM)
    def enum_proc(hwnd: int, _lparam: int) -> bool:
        if not user32.IsWindowVisible(hwnd):
            return True
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
        if score > 0:
            rect = wt.RECT()
            user32.GetClientRect(hwnd, ctypes.byref(rect))
            area = max(0, rect.right - rect.left) * max(0, rect.bottom - rect.top)
            if area >= 100 * 100:
                found.append((score, area, int(hwnd)))
        return True

    user32.EnumWindows(enum_proc, 0)
    if not found:
        return None
    found.sort(key=lambda t: (t[0], t[1]), reverse=True)
    return found[0][2]


def save_window_state(hwnd: int) -> WindowState:
    state = WindowState()
    rect = wt.RECT()
    user32.GetWindowRect(hwnd, ctypes.byref(rect))
    state.rect = (rect.left, rect.top, rect.right - rect.left, rect.bottom - rect.top)
    state.exstyle = user32.GetWindowLongW(hwnd, GWL_EXSTYLE)
    return state


def cloak_window(hwnd: int, cloak: bool) -> None:
    try:
        val = ctypes.c_int(1 if cloak else 0)
        dwmapi.DwmSetWindowAttribute(hwnd, DWMWA_CLOAK, ctypes.byref(val), ctypes.sizeof(val))
    except Exception:  # noqa: BLE001
        pass


def hide_game_window(hwnd: int, state: WindowState) -> None:
    """Park the AIR window off-screen / cloaked so only the IDE stream is visible."""
    ex = (state.exstyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW
    user32.SetWindowLongW(hwnd, GWL_EXSTYLE, ex)
    user32.SetWindowPos(
        hwnd,
        HWND_BOTTOM,
        OFFSCREEN_X,
        OFFSCREEN_Y,
        max(320, state.rect[2]),
        max(240, state.rect[3]),
        SWP_NOACTIVATE | SWP_FRAMECHANGED | SWP_SHOWWINDOW,
    )
    cloak_window(hwnd, True)
    state.hidden = True
    eprint("HIDDEN:1")


def restore_game_window(hwnd: int, state: WindowState) -> None:
    if not state.hidden or not user32.IsWindow(hwnd):
        return
    cloak_window(hwnd, False)
    user32.SetWindowLongW(hwnd, GWL_EXSTYLE, state.exstyle)
    x, y, w, h = state.rect
    if x < -1000 or y < -1000:
        x, y = 80, 80
    user32.SetWindowPos(hwnd, 0, x, y, w, h, SWP_NOZORDER | SWP_SHOWWINDOW | SWP_FRAMECHANGED)
    user32.ShowWindow(hwnd, SW_SHOW)
    state.hidden = False
    eprint("HIDDEN:0")


def force_foreground(hwnd: int, keep_offscreen: bool, state: Optional[WindowState]) -> None:
    fg = user32.GetForegroundWindow()
    tid_fg = user32.GetWindowThreadProcessId(fg, None) if fg else 0
    tid_target = user32.GetWindowThreadProcessId(hwnd, None)
    cur = kernel32.GetCurrentThreadId()
    if tid_fg and tid_fg != cur:
        user32.AttachThreadInput(cur, tid_fg, True)
    if tid_target and tid_target != cur:
        user32.AttachThreadInput(cur, tid_target, True)
    user32.BringWindowToTop(hwnd)
    user32.SetForegroundWindow(hwnd)
    if tid_fg and tid_fg != cur:
        user32.AttachThreadInput(cur, tid_fg, False)
    if tid_target and tid_target != cur:
        user32.AttachThreadInput(cur, tid_target, False)
    if keep_offscreen and state is not None:
        user32.SetWindowPos(
            hwnd,
            HWND_BOTTOM,
            OFFSCREEN_X,
            OFFSCREEN_Y,
            max(320, state.rect[2]),
            max(240, state.rect[3]),
            SWP_NOACTIVATE | SWP_SHOWWINDOW,
        )
        cloak_window(hwnd, True)


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


def _dib_from_dc(mem_dc: int, bmp: int, width: int, height: int) -> Optional[bytes]:
    bmi = BITMAPINFO()
    ctypes.memset(ctypes.byref(bmi), 0, ctypes.sizeof(bmi))
    bmi.bmiHeader.biSize = ctypes.sizeof(BITMAPINFOHEADER)
    bmi.bmiHeader.biWidth = width
    bmi.bmiHeader.biHeight = -height
    bmi.bmiHeader.biPlanes = 1
    bmi.bmiHeader.biBitCount = 32
    bmi.bmiHeader.biCompression = BI_RGB
    buf_size = width * height * 4
    buf = (ctypes.c_ubyte * buf_size)()
    got = gdi32.GetDIBits(mem_dc, bmp, 0, height, buf, ctypes.byref(bmi), DIB_RGB_COLORS)
    if not got:
        return None
    return bytes(buf)


def crop_bgra(bgra: bytes, src_w: int, src_h: int, x: int, y: int, w: int, h: int) -> Optional[bytes]:
    if w <= 0 or h <= 0 or x < 0 or y < 0 or x + w > src_w or y + h > src_h:
        return None
    row_bytes = w * 4
    out = bytearray(h * row_bytes)
    for row in range(h):
        src_off = ((y + row) * src_w + x) * 4
        dst_off = row * row_bytes
        out[dst_off : dst_off + row_bytes] = bgra[src_off : src_off + row_bytes]
    return bytes(out)


def client_crop_box(hwnd: int) -> Optional[tuple[int, int, int, int, int, int]]:
    """Return (win_w, win_h, crop_x, crop_y, client_w, client_h) in window bitmap coords."""
    wr = wt.RECT()
    cr = wt.RECT()
    if not user32.GetWindowRect(hwnd, ctypes.byref(wr)):
        return None
    if not user32.GetClientRect(hwnd, ctypes.byref(cr)):
        return None
    pt = wt.POINT(0, 0)
    if not user32.ClientToScreen(hwnd, ctypes.byref(pt)):
        return None
    win_w = wr.right - wr.left
    win_h = wr.bottom - wr.top
    client_w = cr.right - cr.left
    client_h = cr.bottom - cr.top
    crop_x = pt.x - wr.left
    crop_y = pt.y - wr.top
    if win_w <= 0 or win_h <= 0 or client_w <= 0 or client_h <= 0:
        return None
    if crop_x < 0 or crop_y < 0 or crop_x + client_w > win_w or crop_y + client_h > win_h:
        # DPI / weird chrome — fall back to full window
        return win_w, win_h, 0, 0, win_w, win_h
    return win_w, win_h, crop_x, crop_y, client_w, client_h


def capture_screen_bgra(hwnd: int) -> Optional[tuple[int, int, bytes]]:
    box = client_crop_box(hwnd)
    if box is None:
        return None
    _ww, _wh, _cx, _cy, client_w, client_h = box
    pt = wt.POINT(0, 0)
    user32.ClientToScreen(hwnd, ctypes.byref(pt))
    left, top = pt.x, pt.y
    screen_dc = user32.GetDC(0)
    mem_dc = gdi32.CreateCompatibleDC(screen_dc)
    bmp = gdi32.CreateCompatibleBitmap(screen_dc, client_w, client_h)
    old = gdi32.SelectObject(mem_dc, bmp)
    ok = gdi32.BitBlt(mem_dc, 0, 0, client_w, client_h, screen_dc, left, top, SRCCOPY)
    data = _dib_from_dc(mem_dc, bmp, client_w, client_h) if ok else None
    gdi32.SelectObject(mem_dc, old)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem_dc)
    user32.ReleaseDC(0, screen_dc)
    if data is None:
        return None
    return client_w, client_h, data


def capture_printwindow_bgra(hwnd: int) -> Optional[tuple[int, int, bytes]]:
    """PrintWindow the full HWND, then crop to client area (avoids title-bar + bottom clip)."""
    box = client_crop_box(hwnd)
    if box is None:
        return None
    win_w, win_h, crop_x, crop_y, client_w, client_h = box

    hwnd_dc = user32.GetDC(hwnd)
    if not hwnd_dc:
        return None
    mem_dc = gdi32.CreateCompatibleDC(hwnd_dc)
    bmp = gdi32.CreateCompatibleBitmap(hwnd_dc, win_w, win_h)
    old = gdi32.SelectObject(mem_dc, bmp)
    ok = user32.PrintWindow(hwnd, mem_dc, PW_RENDERFULLCONTENT)
    if not ok:
        ok = user32.PrintWindow(hwnd, mem_dc, 0)
    full = _dib_from_dc(mem_dc, bmp, win_w, win_h) if ok else None
    gdi32.SelectObject(mem_dc, old)
    gdi32.DeleteObject(bmp)
    gdi32.DeleteDC(mem_dc)
    user32.ReleaseDC(hwnd, hwnd_dc)
    if full is None:
        return None
    cropped = crop_bgra(full, win_w, win_h, crop_x, crop_y, client_w, client_h)
    if cropped is None:
        return win_w, win_h, full
    return client_w, client_h, cropped


def capture_frame(hwnd: int, hide_mode: bool) -> Optional[tuple[int, int, bytes]]:
    # Prefer PrintWindow (works for cloaked / off-screen when DWM redirects).
    frame = capture_printwindow_bgra(hwnd)
    if frame and not is_mostly_black(frame[2]):
        return frame
    if hide_mode:
        # Temporarily uncloak for one PrintWindow retry, then re-cloak.
        cloak_window(hwnd, False)
        frame = capture_printwindow_bgra(hwnd)
        cloak_window(hwnd, True)
        if frame and not is_mostly_black(frame[2]):
            return frame
        return frame  # may still be black; better than screen grab off-monitor
    if frame is None or is_mostly_black(frame[2]):
        return capture_screen_bgra(hwnd)
    return frame


def encode_frame(width: int, height: int, bgra: bytes, quality: int, fmt: str) -> bytes:
    from PIL import Image

    img = Image.frombuffer("RGBA", (width, height), bgra, "raw", "BGRA", 0, 1).convert("RGB")
    out = io.BytesIO()
    kind = (fmt or "webp").lower()
    if kind == "png":
        # Fast zlib level — PNG is large; use only if you need lossless.
        img.save(out, format="PNG", compress_level=1, optimize=False)
    elif kind == "jpeg" or kind == "jpg":
        img.save(out, format="JPEG", quality=quality, subsampling=0, optimize=False)
    else:
        # WebP: much better quality-per-byte than JPEG; method=0 = fastest encode (low lag).
        try:
            img.save(out, format="WEBP", quality=quality, method=0)
        except Exception:
            out = io.BytesIO()
            img.save(out, format="JPEG", quality=quality, subsampling=0, optimize=False)
    return out.getvalue()


def send_key(vk: int, down: bool) -> None:
    inp = INPUT()
    inp.type = INPUT_KEYBOARD
    inp.union.ki = KEYBDINPUT(vk, 0, 0 if down else KEYEVENTF_KEYUP, 0, None)
    user32.SendInput(1, ctypes.byref(inp), ctypes.sizeof(INPUT))


def send_mouse_client(hwnd: int, nx: float, ny: float, action: str, button: int = 0) -> None:
    """Deliver mouse in client coords (works while window is off-screen; keeps real cursor put)."""
    rect = wt.RECT()
    user32.GetClientRect(hwnd, ctypes.byref(rect))
    x = int(max(0.0, min(1.0, nx)) * max(0, rect.right - rect.left - 1))
    y = int(max(0.0, min(1.0, ny)) * max(0, rect.bottom - rect.top - 1))
    lp = (y << 16) | (x & 0xFFFF)
    if action == "m":
        # SendMessage is more reliable than Post for Flash/AIR hit-testing / dropdowns.
        user32.SendMessageW(hwnd, WM_MOUSEMOVE, MK_LBUTTON if button == 0 else 0, lp)
        return
    if button == 0:
        msg_down, msg_up, mk = WM_LBUTTONDOWN, WM_LBUTTONUP, MK_LBUTTON
    else:
        msg_down, msg_up, mk = WM_RBUTTONDOWN, WM_RBUTTONUP, MK_RBUTTON
    if action == "d":
        user32.SendMessageW(hwnd, WM_MOUSEMOVE, 0, lp)
        user32.SendMessageW(hwnd, msg_down, mk, lp)
    elif action == "u":
        user32.SendMessageW(hwnd, msg_up, 0, lp)


def stdin_loop(
    hwnd_holder: list[Optional[int]],
    stop: threading.Event,
    hide_mode: bool,
    state_holder: list[Optional[WindowState]],
) -> None:
    for raw in sys.stdin:
        line = raw.strip()
        if not line:
            continue
        parts = line.split()
        cmd = parts[0].upper()
        hwnd = hwnd_holder[0]
        state = state_holder[0]
        if cmd == "QUIT":
            stop.set()
            return
        if hwnd is None:
            continue
        try:
            if cmd == "FOCUS":
                force_foreground(hwnd, hide_mode, state)
                eprint("FOCUS:1")
            elif cmd == "KEY" and len(parts) >= 3:
                force_foreground(hwnd, hide_mode, state)
                send_key(int(parts[2]), parts[1].lower() == "d")
            elif cmd == "MOUSE" and len(parts) >= 4:
                btn = int(parts[4]) if len(parts) >= 5 else 0
                send_mouse_client(hwnd, float(parts[2]), float(parts[3]), parts[1].lower(), btn)
        except Exception as exc:  # noqa: BLE001
            eprint(f"ERROR:input {exc}")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fps", type=float, default=30)
    parser.add_argument("--quality", type=int, default=70)
    parser.add_argument("--wait", type=float, default=45)
    parser.add_argument("--hide-window", action="store_true")
    parser.add_argument("--format", default="webp", choices=("webp", "jpeg", "jpg", "png"))
    parser.add_argument("--process", action="append", default=[])
    parser.add_argument("--title", action="append", default=[])
    args = parser.parse_args()

    processes = args.process or ["adl", "Super Smash Flash 2 Beta"]
    titles = args.title or ["Super Smash Flash", "SSF2", "adl"]
    img_format = "jpeg" if args.format == "jpg" else args.format

    try:
        from PIL import Image  # noqa: F401
    except ImportError:
        eprint("ERROR:Pillow is required. Run: pip install -r helper/requirements.txt")
        return 2

    # Probe WebP support early so we can fall back before the game window hunt.
    if img_format == "webp":
        try:
            buf = io.BytesIO()
            Image.new("RGB", (2, 2), (0, 0, 0)).save(buf, format="WEBP", quality=80, method=0)
        except Exception:
            eprint("WARN:WebP unavailable, using JPEG")
            img_format = "jpeg"

    eprint(f"FORMAT:{img_format}")

    hwnd_holder: list[Optional[int]] = [None]
    state_holder: list[Optional[WindowState]] = [None]
    stop = threading.Event()
    threading.Thread(
        target=stdin_loop,
        args=(hwnd_holder, stop, args.hide_window, state_holder),
        daemon=True,
    ).start()

    deadline = time.time() + args.wait
    hwnd: Optional[int] = None
    while time.time() < deadline and not stop.is_set():
        hwnd = find_window(processes, titles)
        if hwnd:
            break
        eprint("WAITING")
        time.sleep(0.4)

    if not hwnd:
        eprint("ERROR:SSF2 window not found")
        return 1

    state = save_window_state(hwnd)
    state_holder[0] = state
    hwnd_holder[0] = hwnd
    eprint(f"HWND:{hwnd}")

    if args.hide_window:
        hide_game_window(hwnd, state)

    def _restore() -> None:
        if hwnd and state_holder[0]:
            restore_game_window(hwnd, state_holder[0])

    atexit.register(_restore)

    interval = 1.0 / max(5.0, min(60.0, args.fps))
    last_bounds = (0, 0)
    stdout = sys.stdout.buffer

    while not stop.is_set():
        if not user32.IsWindow(hwnd):
            eprint("ERROR:window closed")
            break
        frame = capture_frame(hwnd, args.hide_window)
        if frame is None:
            time.sleep(interval)
            continue
        width, height, bgra = frame
        if (width, height) != last_bounds:
            last_bounds = (width, height)
            eprint(f"BOUNDS:{width},{height}")
        try:
            jpeg = encode_frame(width, height, bgra, args.quality, img_format)
            stdout.write(struct.pack(">I", len(jpeg)))
            stdout.write(jpeg)
            stdout.flush()
        except BrokenPipeError:
            break
        except Exception as exc:  # noqa: BLE001
            eprint(f"ERROR:encode {exc}")
        time.sleep(interval)

    _restore()
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
