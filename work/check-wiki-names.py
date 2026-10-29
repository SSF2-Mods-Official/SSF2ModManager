import pathlib
import re

root = pathlib.Path(r"wiki/Dev Wiki")
shell = pathlib.Path(r"C:\Users\glwex\Documents\GitHub\SSF2 Shell Base")
mgr = pathlib.Path(r"C:\Users\glwex\Documents\GitHub\SSF2ModManager")
blobs = []
for base in (shell, mgr / "src"):
    if not base.exists():
        print("MISSING", base)
        continue
    for p in base.rglob("*"):
        if p.suffix.lower() not in {".as", ".cs", ".xaml"}:
            continue
        if "work" in p.parts or "node_modules" in p.parts:
            continue
        try:
            blobs.append(p.read_text(encoding="utf-8", errors="ignore"))
        except Exception:
            pass
print("files", len(blobs))
pat = re.compile(r"^\| `([^`]+)`", re.M)
names = []
for md in sorted(root.glob("*.md")):
    text = md.read_text(encoding="utf-8")
    for m in pat.finditer(text):
        cell = m.group(1)
        for part in re.split(r",\s*", cell):
            part = part.strip()
            if not part or part.startswith("src/") or " " in part or "/" in part:
                continue
            if not re.match(r"^[A-Za-z_][A-Za-z0-9_\.]*$", part):
                continue
            names.append((md.name, part.split(".")[-1], part))
missing = []
for page, short, full in names:
    if not any(short in body for body in blobs):
        missing.append(page + " :: " + full)
print("checked", len(names))
print("missing", len(missing))
for line in missing:
    print(line)
