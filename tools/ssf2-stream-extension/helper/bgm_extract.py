#!/usr/bin/env python3
"""Extract MP3 payload from an SSF2 bgm_*.swf for in-panel HTML5 preview."""

from __future__ import annotations

import argparse
import struct
import sys
import zlib
from pathlib import Path


def _swf_body(data: bytes) -> bytes:
    sig = data[:3]
    if sig == b"FWS":
        return data[8:]
    if sig == b"CWS":
        return zlib.decompress(data[8:])
    if sig == b"ZWS":
        raise ValueError("LZMA SWF (ZWS) not supported")
    raise ValueError(f"Not a SWF ({sig!r})")


def _find_mp3(body: bytes) -> bytes | None:
    # Prefer DefineSound (tag 14) MP3 payloads when we can parse tags.
    i = 0
    n = len(body)
    # Skip RECT at start of SWF body
    if n < 2:
        return None
    nbits = body[0] >> 3
    rect_bits = 5 + nbits * 4
    rect_bytes = (rect_bits + 7) // 8
    i = rect_bytes + 4  # frame rate + frame count
    while i + 2 <= n:
        code_len = body[i] | (body[i + 1] << 8)
        tag = code_len >> 6
        length = code_len & 0x3F
        i += 2
        if length == 0x3F:
            if i + 4 > n:
                break
            length = struct.unpack_from("<I", body, i)[0]
            i += 4
        payload = body[i : i + length]
        i += length
        if tag == 0:  # End
            break
        if tag == 14 and len(payload) >= 7:  # DefineSound
            # id(2) + flags(1) + sampleCount(4) [+ seek(2) for MP3]
            fmt = (payload[2] >> 4) & 0x0F
            if fmt == 2:  # MP3
                # skip id, flags, sampleCount, seekSamples
                mp3 = payload[2 + 1 + 4 + 2 :]
                if len(mp3) > 64 and (mp3[0] == 0xFF or mp3[:3] == b"ID3"):
                    return mp3
    # Fallback: first MPEG frame sync in file body
    for magic in (b"\xff\xfb", b"\xff\xfa", b"\xff\xf3", b"\xff\xf2", b"ID3"):
        at = body.find(magic)
        if at >= 0:
            return body[at:]
    return None


def extract(swf_path: Path, out_path: Path) -> int:
    data = swf_path.read_bytes()
    body = _swf_body(data)
    mp3 = _find_mp3(body)
    if not mp3:
        raise RuntimeError(f"No MP3 found in {swf_path.name}")
    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(mp3)
    return len(mp3)


def main() -> int:
    ap = argparse.ArgumentParser()
    ap.add_argument("swf")
    ap.add_argument("-o", "--out", required=True)
    args = ap.parse_args()
    try:
        n = extract(Path(args.swf), Path(args.out))
        print(f"OK:{n}")
        return 0
    except Exception as e:
        print(f"ERR:{e}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
