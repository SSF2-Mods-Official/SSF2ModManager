import zipfile
import re
from pathlib import Path

p = Path(r"SSF2 Mod Manager/SSF2 Shell/build/data/character/expansions/cloney/FLA/cloney.fla.bak")
with zipfile.ZipFile(p) as z:
    # pick a USED bitmap from A_up_tilt: Bitmap 1252.png
    pat = re.compile(r'libraryItemName="Bitmap 1252\.png"')
    for e in z.infolist():
        fn = e.filename.replace("\\", "/")
        if not fn.startswith("LIBRARY/") or not fn.endswith(".xml"):
            continue
        xml = z.read(e).decode("utf-8", "replace")
        if not pat.search(xml):
            continue
        i = xml.find('name="')
        name = xml[i + 6 : xml.find('"', i + 6)] if i >= 0 else "?"
        print("USED Bitmap 1252 parent", fn, name)

    # count how many dump bitmaps (unused) have linkage
    dom = z.read("DOMDocument.xml").decode("utf-8", "replace")
    items = re.findall(
        r'<DOMBitmapItem\b([^>]*)>',
        dom,
    )
    print("bitmap tags", len(items))
