"""Stamp ssf2mmId on instances and diff cloney-step0..3 for frame/pos/dup drift."""
from __future__ import annotations

import re
import zipfile
from collections import defaultdict
from pathlib import Path
from xml.etree import ElementTree as ET

INST_OPEN = re.compile(
    r"<(DOM(?:Bitmap|Symbol)Instance)\b([^>]*?)(/?)>",
    re.IGNORECASE,
)


def local(tag: str) -> str:
    return tag.split("}")[-1]


def stamp_xml(xml: str, start: int) -> tuple[str, int]:
    n = start

    def repl(m: re.Match) -> str:
        nonlocal n
        body = m.group(2)
        if re.search(r"\bssf2mmId=", body, re.I):
            return m.group(0)
        n += 1
        return f"<{m.group(1)}{body} ssf2mmId=\"{n}\"{m.group(3)}>"

    return INST_OPEN.sub(repl, xml), n


def stamp_fla(path: Path) -> int:
    entries: dict[str, bytes] = {}
    n = 0
    with zipfile.ZipFile(path, "r") as z:
        for info in z.infolist():
            data = z.read(info)
            key = info.filename.replace("\\", "/")
            if key.endswith(".xml"):
                text = data.decode("utf-8", "replace")
                if "Instance" in text:
                    text, n = stamp_xml(text, n)
                    data = text.encode("utf-8")
            entries[key] = data
    tmp = path.with_suffix(".stamping")
    with zipfile.ZipFile(tmp, "w", compression=zipfile.ZIP_DEFLATED) as z:
        for k, data in entries.items():
            z.writestr(k, data)
    tmp.replace(path)
    return n


def dump_fla(path: Path) -> dict[str, dict]:
    out: dict[str, dict] = {}
    with zipfile.ZipFile(path) as z:
        for name in z.namelist():
            nn = name.replace("\\", "/")
            if not nn.endswith(".xml"):
                continue
            raw = z.read(name)
            if b"ssf2mmId" not in raw:
                continue
            try:
                root = ET.fromstring(raw)
            except ET.ParseError:
                continue
            for layer in root.iter():
                if local(layer.tag) != "DOMLayer":
                    continue
                lname = layer.attrib.get("name", "")
                for frame in layer.iter():
                    if local(frame.tag) != "DOMFrame":
                        continue
                    idx = int(frame.attrib.get("index") or 0)
                    dur = int(frame.attrib.get("duration") or 1)
                    for el in frame.iter():
                        ln = local(el.tag)
                        if ln not in ("DOMBitmapInstance", "DOMSymbolInstance"):
                            continue
                        iid = el.attrib.get("ssf2mmId")
                        if not iid:
                            continue
                        tx = ty = "0"
                        for m in el.iter():
                            if local(m.tag) == "Matrix":
                                tx = m.attrib.get("tx", "0")
                                ty = m.attrib.get("ty", "0")
                                break
                        out[iid] = {
                            "file": nn,
                            "layer": lname,
                            "frame": idx,
                            "dur": dur,
                            "name": el.attrib.get("name", ""),
                            "lib": (el.attrib.get("libraryItemName") or "").split("/")[-1],
                            "tx": tx,
                            "ty": ty,
                        }
    return out


def count_id_freq(path: Path) -> dict[str, int]:
    freq: dict[str, int] = defaultdict(int)
    with zipfile.ZipFile(path) as z:
        for name in z.namelist():
            if not name.endswith(".xml"):
                continue
            text = z.read(name).decode("utf-8", "replace")
            for m in re.finditer(r'\bssf2mmId="(\d+)"', text):
                freq[m.group(1)] += 1
    return freq


def empty_keys(path: Path) -> dict[tuple[str, str, int], tuple[int, int]]:
    """(file, layer, index) -> (duration, instCount)"""
    keys: dict[tuple[str, str, int], tuple[int, int]] = {}
    with zipfile.ZipFile(path) as z:
        for name in z.namelist():
            nn = name.replace("\\", "/")
            if not nn.endswith(".xml"):
                continue
            raw = z.read(name)
            if b"DOMFrame" not in raw:
                continue
            try:
                root = ET.fromstring(raw)
            except ET.ParseError:
                continue
            for layer in root.iter():
                if local(layer.tag) != "DOMLayer":
                    continue
                lname = layer.attrib.get("name", "")
                for frame in layer.iter():
                    if local(frame.tag) != "DOMFrame":
                        continue
                    idx = int(frame.attrib.get("index") or 0)
                    dur = int(frame.attrib.get("duration") or 1)
                    ninst = sum(
                        1
                        for el in frame.iter()
                        if local(el.tag) in ("DOMBitmapInstance", "DOMSymbolInstance")
                    )
                    keys[(nn, lname, idx)] = (dur, ninst)
    return keys


def near(a: str, b: str) -> bool:
    try:
        return abs(float(a) - float(b)) < 0.05
    except ValueError:
        return a == b


def in_b(row: dict) -> bool:
    f = row["file"].replace("\\", "/")
    return f.endswith("/B/B.xml") or f.endswith("Symbol 1671.xml")


def diff_pair(label: str, a: dict, b: dict, freq_b: dict[str, int], limit: int = 40) -> str:
    lines = [f"==== {label}"]
    miss = frame = pos = 0
    dups = [(k, c) for k, c in freq_b.items() if c > 1]
    samples: list[str] = []
    for iid, row in a.items():
        oth = b.get(iid)
        if not oth:
            miss += 1
            if len(samples) < limit:
                samples.append(f"  MISS id={iid} {row['file']} {row['layer']} f{row['frame']} {row['lib']}")
            continue
        if row["frame"] != oth["frame"]:
            frame += 1
            if len(samples) < limit:
                samples.append(
                    f"  FRAME id={iid} {row['lib']} {row['layer']} f{row['frame']}->{oth['frame']} nowLayer={oth['layer']}"
                )
        if not near(row["tx"], oth["tx"]) or not near(row["ty"], oth["ty"]):
            pos += 1
            if len(samples) < limit:
                samples.append(
                    f"  POS id={iid} {row['lib']} f{row['frame']} ({row['tx']},{row['ty']})->({oth['tx']},{oth['ty']})"
                )
    extra = sum(1 for iid in b if iid not in a)
    lines.append(
        f"idsA={len(a)} idsB={len(b)} missing={miss} extra={extra} "
        f"frameChanged={frame} posChanged={pos} duplicatedIds={len(dups)}"
    )
    if dups[:10]:
        lines.append("  dup ids " + ", ".join(f"{k}x{c}" for k, c in dups[:10]))
    lines.extend(samples)
    lines.append("")
    return "\n".join(lines)


def empty_diff(label: str, ka, kb, limit: int = 25) -> str:
    gone = filled = dur = 0
    samples: list[str] = []
    for key, (adur, an) in ka.items():
        if an != 0:
            continue
        if key not in kb:
            gone += 1
            if len(samples) < limit:
                samples.append(f"  KEYGONE {key[0]} {key[1]} f{key[2]} dur={adur}")
            continue
        bdur, bn = kb[key]
        if bn > 0:
            filled += 1
            if len(samples) < limit:
                samples.append(f"  KEYFILLED {key[0]} {key[1]} f{key[2]}")
        elif adur != bdur:
            dur += 1
    return f"==== {label}\nemptyGone={gone} emptyFilled={filled} emptyDurChanged={dur}\n" + "\n".join(samples) + "\n"


def main() -> None:
    base = Path(r"SSF2 Mod Manager/SSF2 Shell/build/data/character/expansions/cloney/FLA")
    steps = [base / f"cloney-step{i}.fla" for i in range(4)]
    dumps = [dump_fla(p) for p in steps]
    freqs = [count_id_freq(p) for p in steps]
    keys = [empty_keys(p) for p in steps]
    parts = [
        f"stamped ids step0={len(dumps[0])} step1={len(dumps[1])} step2={len(dumps[2])} step3={len(dumps[3])}",
        diff_pair("step0 -> step1 organize", dumps[0], dumps[1], freqs[1]),
        diff_pair("step1 -> step2 names+relocate", dumps[1], dumps[2], freqs[2]),
        diff_pair("step2 -> step3 folders", dumps[2], dumps[3], freqs[3]),
        empty_diff("empty keys step1 -> step2", keys[1], keys[2]),
        empty_diff("empty keys step2 -> step3", keys[2], keys[3]),
        diff_pair("B clip step1 -> step2", {i: r for i, r in dumps[1].items() if in_b(r)}, {i: r for i, r in dumps[2].items() if in_b(r)}, freqs[2], 60),
        diff_pair("B clip step2 -> step3", {i: r for i, r in dumps[2].items() if in_b(r)}, {i: r for i, r in dumps[3].items() if in_b(r)}, freqs[3], 60),
    ]
    text = "\n".join(parts)
    out = base / "GUIDE-ids-diff.txt"
    out.write_text(text, encoding="utf-8")
    print(text)
    print("wrote", out)


if __name__ == "__main__":
    main()
