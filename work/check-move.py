import pathlib
import re

names_files = [
    "where-things-are.md",
    "news.md",
    "portable-toolchains.md",
    "launcher-shell.md",
    "built-in-mods.md",
    "performance.md",
    "match-frame.md",
    "cpu-ai.md",
    "resource-cache.md",
    "uncaught-errors.md",
    "profiler.md",
    "stream-debug.md",
    "stream-tab.md",
]
root = pathlib.Path(r"wiki/Private Wiki")
shell = pathlib.Path(r"C:\Users\glwex\Documents\GitHub\SSF2 Shell Base")
mgr = pathlib.Path(r"C:\Users\glwex\Documents\GitHub\SSF2ModManager\src")
blobs = []
for base in (shell / "shell" / "src", shell / "patches" / "engine2", shell / "tools", mgr):
    if not base.exists():
        print("MISSING", base)
        continue
    for p in base.rglob("*"):
        if p.suffix.lower() not in {".as", ".cs", ".xaml", ".patch", ".py", ".ps1"}:
            continue
        if "vanilla" in p.parts or "work" in p.parts:
            continue
        try:
            blobs.append(p.read_text(encoding="utf-8", errors="ignore"))
        except Exception:
            pass
print("files", len(blobs))
pat = re.compile(r"`([A-Za-z_][A-Za-z0-9_\.]{2,})`")
missing = []
checked = 0
for name in names_files:
    text = (root / name).read_text(encoding="utf-8")
    seen = set()
    for m in pat.finditer(text):
        full = m.group(1)
        if full in seen:
            continue
        seen.add(full)
        short = full.split(".")[-1]
        if short.lower() in {"true", "false", "null", "none", "ok", "json", "md", "swf", "as", "cs"}:
            continue
        if len(short) < 4:
            continue
        checked += 1
        if not any(short in body for body in blobs):
            missing.append(name + " :: " + full)
print("checked", checked)
print("missing", len(missing))
for line in missing:
    print(line)
