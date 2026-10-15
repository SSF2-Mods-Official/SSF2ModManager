/*
 * SSF2 Mod Manager — inject _fla methods/fields onto frame 1 (Animate JSFL)
 *
 * Run after clone FLA export. Empty Sound/MovieClip .as stubs are already
 * deleted by the manager. This script moves remaining _fla class methods
 * and extra fields (self, xframe, …) onto the matching clip's frame 1,
 * then removes those .as files and the leftover {id}_fla folder.
 *
 * How to run (faster — do not open the FLA first):
 *   Animate.exe "…/FLA/tools/ssf2mm_fla_cleanup.jsfl"
 *   The script opens the sibling .fla, works, saves, and leaves it open.
 *
 *   Or: Commands → Run Command… with the FLA already open.
 *
 * Does:
 *   - Open the FLA next to this script if none is open
 *   - Read {id}_fla/*.as (then common/{id}_fla)
 *   - Prepend extra vars + methods to frame 1 of the linked movie clip
 *   - Delete the .as after a successful inject
 *   - Delete leftover {id}_fla folders
 *   - Apply tools/ssf2mm_organize.txt via newFolder + moveToFolder + renameItem
 *   - Delete truly empty non-mask/guide layers
 *   - Rename the root host clip to the friendly library name (Back Mage)
 *   - Rename layers (library clips and Scene 1) from instance name, else placed clip leaf, else bitmap layer N
 *   - Outline only when a majority of populated keyframes are *box* (not itemBox);
 *     append * only when some (not most) frames are *box*
 *   - Name text-only layers from the text contents
 *   - After rename, outline host layers below stance
 *   - Save (leaves the document open)
 *
 * Does not: rewrite other frames, turn auto-declare off, publish, or strip linkage.
 */

var displayNameForBitmaps = "";

function main() {
    try { fl.showIdleMessage(false); } catch (eIdle) {}
    try {
        runCleanup();
    } finally {
        try { fl.showIdleMessage(true); } catch (eIdle2) {}
    }
}

function runCleanup() {
    var doc = ensureFlaDocument();
    if (!doc) {
        fl.trace("ssf2mm_fla_cleanup: no document open.");
        alert("Could not find a FLA. Put this script in the FLA's tools folder, or open the FLA first.");
        return;
    }

    fl.outputPanel.clear();
    fl.trace("ssf2mm_fla_cleanup: " + doc.name);

    var flaUri = doc.pathURI;
    if (!flaUri) {
        alert("Save the FLA to disk first, then run this command again.");
        return;
    }

    var dir = flaUri.substring(0, flaUri.lastIndexOf("/"));
    var base = String(doc.name || "").replace(/\.fla$/i, "");
    var flaFolders = [
        dir + "/" + base + "_fla",
        dir + "/common/" + base + "_fla"
    ];
    var files = [];
    var flaFolder = "";
    var fi;
    for (fi = 0; fi < flaFolders.length; fi++) {
        if (FLfile.exists(flaFolders[fi])) {
            flaFolder = flaFolders[fi];
            files = FLfile.listFolder(flaFolder + "/*.as", "files") || [];
            if (files.length)
                break;
        }
    }

    var linkageIndex = buildLinkageIndex(doc.library);
    var injected = 0;
    var i;
    for (i = 0; i < files.length; i++) {
        var asUri = flaFolder + "/" + files[i];
        var src = FLfile.read(asUri);
        if (!src) continue;
        var className = parseClassName(src);
        if (!className) continue;
        var extraVars = extractExtraVars(src, className);
        var methods = extractExtraMethods(src, className);
        if (!extraVars && !methods) {
            FLfile.remove(asUri);
            fl.trace("removed stub " + files[i]);
            continue;
        }
        var pkg = parsePackageName(src);
        var linkage = pkg ? (pkg + "." + className) : className;
        var block = extraVars;
        if (methods)
            block += (block ? "\n\n" : "") + methods;
        if (!injectMethodsOnClip(doc, linkage, className, block, linkageIndex)) {
            fl.trace("no library clip for " + linkage);
            continue;
        }
        FLfile.remove(asUri);
        injected++;
        fl.trace("injected " + className);
    }

    if (flaFolder)
        removeFlaPackageFolder(flaFolder);
    removeFlaPackageFolder(dir + "/" + base + "_fla");
    removeFlaPackageFolder(dir + "/common/" + base + "_fla");

    try {
        applyOrganizeBlueprint(doc, dir);
    } catch (orgErr) {
        fl.trace("organize library: " + orgErr);
    }

    try {
        renameHostLibraryItem(doc);
    } catch (hostErr) {
        fl.trace("rename host: " + hostErr);
    }

    try {
        pruneEmptyLayers(doc);
    } catch (pruneErr) {
        fl.trace("prune empty layers: " + pruneErr);
    }

    var displayName = readDisplayName(dir, doc);
    displayNameForBitmaps = displayName;
    try {
        smartNameAndOutlineLayers(doc);
    } catch (boxErr) {
        fl.trace("layer names/outlines: " + boxErr);
    }
    try {
        applyDisplayNameLayerNames(doc, displayName);
        organizeGroundAndEffects(doc);
        lockAndRaiseDisplayName(doc, displayName);
        fl.trace("box merge skipped here — run ssf2mm_fla_merge_boxes.jsfl after this script");
    } catch (layErr) {
        fl.trace("layer folders/merge: " + layErr);
    }

    try {
        doc.save();
        fl.trace("ssf2mm_fla_cleanup: saved. injected=" + injected + " (FLA left open)");
    } catch (e) {
        fl.trace("save failed: " + e);
        alert("Inject finished but Save failed. Use File → Save.");
    }
}

function ensureFlaDocument() {
    var doc = fl.getDocumentDOM();
    if (doc)
        return doc;
    var uri = findSiblingFlaUri();
    if (!uri) {
        fl.trace("ssf2mm_fla_cleanup: no sibling .fla next to this script");
        return null;
    }
    fl.trace("ssf2mm_fla_cleanup: opening " + uri);
    try {
        return fl.openDocument(uri);
    } catch (e) {
        fl.trace("openDocument failed: " + e);
        return null;
    }
}

function findSiblingFlaUri() {
    var script = String(fl.scriptURI || "");
    if (!script)
        return "";
    var scriptDir = script.substring(0, script.lastIndexOf("/"));
    var dirs = [scriptDir];
    if (scriptDir.substring(scriptDir.length - 6).toLowerCase() == "/tools")
        dirs.push(scriptDir.substring(0, scriptDir.lastIndexOf("/")));
    var d;
    for (d = 0; d < dirs.length; d++) {
        var files = FLfile.listFolder(dirs[d] + "/*.fla", "files") || [];
        if (!files.length)
            continue;
        var prefer = dirs[d].substring(dirs[d].lastIndexOf("/") + 1) + ".fla";
        var i;
        for (i = 0; i < files.length; i++) {
            if (String(files[i]).toLowerCase() == prefer.toLowerCase())
                return dirs[d] + "/" + files[i];
        }
        return dirs[d] + "/" + files[0];
    }
    return "";
}

function parsePackageName(src) {
    var m = src.match(/package\s+([A-Za-z_][A-Za-z0-9_.]*)/);
    return m ? m[1] : "";
}

function parseClassName(src) {
    var m = src.match(/\bclass\s+([A-Za-z_][A-Za-z0-9_]*)/);
    return m ? m[1] : "";
}

function isDisplayType(typeName) {
    var t = String(typeName || "").replace(/\s/g, "");
    if (!t || t == "*" || t == "Object")
        return false;
    var simple = t.indexOf(".") >= 0 ? t.substring(t.lastIndexOf(".") + 1) : t;
    var skip = {
        MovieClip: 1, Sprite: 1, SimpleButton: 1, TextField: 1,
        Shape: 1, DisplayObject: 1, DisplayObjectContainer: 1,
        Bitmap: 1, Video: 1, MorphShape: 1, StaticText: 1, Button: 1
    };
    return !!skip[simple];
}

function extractExtraVars(src, className) {
    var out = [];
    var seen = {};
    var re = /\b(?:public|protected|private|internal)?\s*var\s+([A-Za-z_][A-Za-z0-9_]*)\s*(?::\s*([A-Za-z_][A-Za-z0-9_.*]*))?/g;
    var m;
    while ((m = re.exec(src)) != null) {
        var name = m[1];
        if (!name || name == className || seen[name])
            continue;
        if (isDisplayType(m[2]))
            continue;
        seen[name] = true;
        out.push("var " + name + ":*;");
    }
    return out.length ? out.join("\n") : "";
}

function extractExtraMethods(src, className) {
    var out = [];
    var idx = 0;
    while (true) {
        var fn = src.indexOf("function", idx);
        if (fn < 0) break;
        var rest = src.substring(fn);
        var hm = rest.match(/^function\s+(get\s+|set\s+)?([A-Za-z_][A-Za-z0-9_]*)\s*\(/);
        if (!hm) {
            idx = fn + 8;
            continue;
        }
        var name = hm[2];
        var headerEnd = fn + hm[0].length - 1;
        var parenEnd = matchParen(src, headerEnd);
        if (parenEnd < 0) {
            idx = fn + 8;
            continue;
        }
        var brace = src.indexOf("{", parenEnd);
        if (brace < 0) {
            idx = fn + 8;
            continue;
        }
        var bodyEnd = matchBrace(src, brace);
        if (bodyEnd < 0) {
            idx = fn + 8;
            continue;
        }
        idx = bodyEnd + 1;
        if (name == className)
            continue;
        var sig = src.substring(fn, brace);
        sig = sig.replace(/^\s*(public|protected|private|internal)\s+/g, "");
        sig = sig.replace(/\s*(static|override)\s+/g, " ");
        var body = src.substring(brace, bodyEnd + 1);
        out.push(sig + " " + body);
    }
    return out.length ? out.join("\n\n") : "";
}

function matchParen(src, openIdx) {
    var depth = 0;
    var i;
    for (i = openIdx; i < src.length; i++) {
        var c = src.charAt(i);
        if (c == "(") depth++;
        else if (c == ")") {
            depth--;
            if (depth == 0) return i;
        }
    }
    return -1;
}

function matchBrace(src, openIdx) {
    var depth = 0;
    var i;
    for (i = openIdx; i < src.length; i++) {
        var c = src.charAt(i);
        if (c == "{") depth++;
        else if (c == "}") {
            depth--;
            if (depth == 0) return i;
        }
    }
    return -1;
}

function buildLinkageIndex(lib) {
    var map = {};
    if (!lib || !lib.items)
        return map;
    var i;
    for (i = 0; i < lib.items.length; i++) {
        var it = lib.items[i];
        if (it.itemType != "movie clip")
            continue;
        var lc = String(it.linkageClassName || "");
        if (lc)
            map[lc] = it;
        var n = String(it.name || "");
        var slash = n.lastIndexOf("/");
        var leaf = slash >= 0 ? n.substring(slash + 1) : n;
        if (leaf && !map[leaf])
            map[leaf] = it;
    }
    return map;
}

function injectMethodsOnClip(doc, linkage, className, block, linkageIndex) {
    var lib = doc.library;
    var item = null;
    if (linkageIndex) {
        item = linkageIndex[linkage] || linkageIndex[className] || null;
    }
    if (!item) {
        var i;
        for (i = 0; i < lib.items.length; i++) {
            var it = lib.items[i];
            if (it.itemType != "movie clip")
                continue;
            var lc = String(it.linkageClassName || "");
            if (lc == linkage || lc == className) {
                item = it;
                break;
            }
        }
    }
    if (!item)
        return false;
    var itemName = String(item.name || "");
    var tl = null;
    try { tl = item.timeline; } catch (e1) {}
    if (tl) {
        try {
            prependFrame1Script(tl, block);
            return true;
        } catch (e2) {
            fl.trace("timeline inject failed " + itemName + ": " + e2);
        }
    }
    try {
        if (!lib.editItem(itemName))
            return false;
        prependFrame1Script(doc.getTimeline(), block);
        leaveAllEditModes(doc);
        return true;
    } catch (e) {
        fl.trace("inject failed " + itemName + ": " + e);
        return false;
    }
}

function prependFrame1Script(tl, block) {
    if (!tl || !tl.layers || tl.layers.length < 1)
        return;
    var layerIndex = 0;
    var i;
    for (i = 0; i < tl.layers.length; i++) {
        var ly = tl.layers[i];
        var t = String(ly.layerType || "");
        if (t == "folder" || t == "guide" || t == "mask")
            continue;
        var n = String(ly.name || "").toLowerCase();
        if (n.indexOf("script") >= 0 || n == "actions" || n == "action") {
            layerIndex = i;
            break;
        }
        if (layerIndex == 0)
            layerIndex = i;
    }
    tl.currentLayer = layerIndex;
    var layer = tl.layers[layerIndex];
    if (!layer.frames || layer.frames.length < 1)
        return;
    if (layer.frames[0].startFrame != 0) {
        try { tl.convertToKeyframes(0); } catch (e) {}
    }
    var existing = String(layer.frames[0].actionScript || "");
    if (block && existing.indexOf(block) >= 0)
        return;
    layer.frames[0].actionScript = block + (existing ? "\n" + existing : "");
}

function removeFlaPackageFolder(flaFolder) {
    if (!FLfile.exists(flaFolder))
        return;
    var leftover = FLfile.listFolder(flaFolder, "files") || [];
    var dirs = FLfile.listFolder(flaFolder, "directories") || [];
    var i;
    for (i = 0; i < leftover.length; i++)
        FLfile.remove(flaFolder + "/" + leftover[i]);
    for (i = 0; i < dirs.length; i++)
        removeFlaPackageFolder(flaFolder + "/" + dirs[i]);
    FLfile.remove(flaFolder);
    fl.trace("removed folder " + flaFolder);
}

function sanitizeFolder(name) {
    var s = String(name || "").replace(/[\\\/:*?"<>|]/g, "_");
    s = s.replace(/^\s+|\s+$/g, "");
    if (!s) s = "Unnamed";
    return s.charAt(0).toUpperCase() + s.substring(1);
}

function baseItemName(name) {
    var n = String(name || "");
    var slash = n.lastIndexOf("/");
    return slash >= 0 ? n.substring(slash + 1) : n;
}

function leaveAllEditModes(doc) {
    var n;
    for (n = 0; n < 8; n++) {
        try { doc.exitEditMode(); } catch (e) { break; }
    }
}

function compactName(name) {
    return String(name || "").toLowerCase().split("_").join("").split(" ").join("");
}

function boxFamilyFromName(name) {
    var n = compactName(name).replace(/[0-9]+/g, "");
    if (!n)
        return "";
    if (n.indexOf("itembox") >= 0)
        return "Item";
    if (n.indexOf("hitbox") >= 0 || n == "hit")
        return "HitBox";
    if (n.indexOf("attackbox") >= 0 || n.indexOf("atkbox") >= 0)
        return "AttackBox";
    if (n.indexOf("hurtbox") >= 0)
        return "HurtBox";
    if (n.indexOf("grabbox") >= 0)
        return "GrabBox";
    if (n.indexOf("touchbox") >= 0)
        return "TouchBox";
    return "";
}

function instanceNameHasBox(name) {
    var n = String(name || "").toLowerCase();
    if (compactName(n).indexOf("itembox") >= 0)
        return false;
    return n.indexOf("box") >= 0;
}

function nameLooksLikeItemBox(name) {
    return compactName(name).indexOf("itembox") >= 0;
}

function isGenericSymbolLeaf(name) {
    return /^symbol\s+\d+$/i.test(String(name || "").replace(/^\s+|\s+$/g, ""));
}

function layerIsSpecialType(layer) {
    if (!layer)
        return true;
    var t = String(layer.layerType || "normal");
    if (t == "folder" || t == "guide" || t == "guided" || t == "mask" || t == "masked")
        return true;
    var n = String(layer.name || "").toLowerCase();
    return n.indexOf("script") >= 0 || n.indexOf("label") >= 0 || n == "actions" || n == "action";
}

function isBitmapElement(el) {
    if (!el)
        return false;
    try {
        if (String(el.elementType || "") == "bitmap")
            return true;
        if (String(el.instanceType || "") == "bitmap")
            return true;
        if (el.libraryItem && String(el.libraryItem.itemType || "") == "bitmap")
            return true;
    } catch (e) {}
    return false;
}

function firstNamedInstance(layer) {
    var f, e, fr, els, el, nm;
    if (!layer || !layer.frames)
        return "";
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els)
            continue;
        for (e = 0; e < els.length; e++) {
            el = els[e];
            nm = String(el.name || "");
            if (nm)
                return nm;
        }
    }
    return "";
}

function isPlacedClipElement(el) {
    if (!el)
        return false;
    var t = String(el.instanceType || el.elementType || "");
    if (t == "symbol" || t == "movie clip" || t == "graphic" || t == "button")
        return true;
    try {
        var it = el.libraryItem;
        if (!it)
            return false;
        var k = String(it.itemType || "");
        return k == "movie clip" || k == "graphic" || k == "button";
    } catch (e) {}
    return false;
}

function firstPlacedClipLeaf(layer) {
    var f, e, fr, els, el, leaf;
    if (!layer || !layer.frames)
        return "";
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els)
            continue;
        for (e = 0; e < els.length; e++) {
            el = els[e];
            if (!isPlacedClipElement(el) || !el.libraryItem)
                continue;
            leaf = baseItemName(el.libraryItem.name);
            if (leaf && !isGenericSymbolLeaf(leaf))
                return leaf;
        }
    }
    return "";
}

function sanitizeLayerNameFromText(s) {
    var t = String(s || "").replace(/\r\n|\r|\n/g, " ");
    t = t.replace(/[\\\/:*?"<>|]/g, " ");
    t = t.replace(/\s+/g, " ").replace(/^\s+|\s+$/g, "");
    if (t.length > 40)
        t = t.substring(0, 40).replace(/^\s+|\s+$/g, "");
    return t;
}

function firstTextOnLayer(layer) {
    var f, e, fr, els, el, s;
    if (!layer || !layer.frames)
        return "";
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els)
            continue;
        for (e = 0; e < els.length; e++) {
            el = els[e];
            if (String(el.elementType || "") != "text")
                continue;
            s = "";
            try {
                if (el.getTextString)
                    s = el.getTextString();
            } catch (e1) {}
            if (!s) {
                try { s = el.contents; } catch (e2) {}
            }
            s = sanitizeLayerNameFromText(s);
            if (s)
                return s;
        }
    }
    return "";
}

function suggestedLayerName(layer) {
    var first = firstNamedInstance(layer);
    var fam = boxFamilyFromName(first) || boxFamilyFromName(layer && layer.name);
    if (fam)
        return fam;
    if (first)
        return first;
    var clip = firstPlacedClipLeaf(layer);
    if (clip)
        return clip;
    var text = firstTextOnLayer(layer);
    if (!text)
        return "";
    if (!/textbox$/i.test(text))
        return text + " textbox";
    return text;
}

function layerHasBitmap(layer) {
    var f, e, fr, els;
    if (!layer || !layer.frames)
        return false;
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els)
            continue;
        for (e = 0; e < els.length; e++) {
            if (isBitmapElement(els[e]))
                return true;
        }
    }
    return false;
}

function layerHasBoxInstance(layer) {
    var f, e, fr, els, el;
    if (!layer || !layer.frames)
        return false;
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els)
            continue;
        for (e = 0; e < els.length; e++) {
            el = els[e];
            if (instanceNameHasBox(el.name))
                return true;
        }
    }
    return false;
}

function keyframeHasBoxInstance(fr) {
    var e, els, el;
    if (!fr)
        return false;
    els = fr.elements;
    if (!els)
        return false;
    for (e = 0; e < els.length; e++) {
        el = els[e];
        if (instanceNameHasBox(el.name))
            return true;
    }
    return false;
}

function layerBoxKeyframeStats(layer) {
    var f, fr, els, populated = 0, boxKf = 0;
    if (!layer || !layer.frames)
        return { populated: 0, boxKf: 0 };
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        els = fr.elements;
        if (!els || els.length < 1)
            continue;
        populated++;
        if (keyframeHasBoxInstance(fr))
            boxKf++;
    }
    return { populated: populated, boxKf: boxKf };
}

function layerShouldOutlineFromMajority(layer) {
    var s = layerBoxKeyframeStats(layer);
    return s.populated > 0 && s.boxKf * 2 > s.populated;
}

function layerHasMinorityBoxFrames(layer) {
    var s = layerBoxKeyframeStats(layer);
    return s.boxKf > 0 && s.boxKf * 2 <= s.populated;
}

function uniqueLayerName(tl, wanted, skipIndex) {
    var name = wanted;
    var n = 2;
    while (true) {
        var taken = false;
        var L;
        for (L = 0; L < tl.layerCount; L++) {
            if (skipIndex >= 0 && L == skipIndex)
                continue;
            if (String(tl.layers[L].name || "") == name) {
                taken = true;
                break;
            }
        }
        if (!taken)
            return name;
        name = wanted + " " + n;
        n++;
    }
}

function layerIsEmptyPrunable(layer) {
    if (!layer)
        return false;
    var t = String(layer.layerType || "normal");
    if (t == "folder" || t == "guide" || t == "guided" || t == "mask" || t == "masked")
        return false;
    var n = String(layer.name || "").toLowerCase();
    if (n.indexOf("script") >= 0 || n.indexOf("label") >= 0 || n == "actions" || n == "action")
        return false;
    if (!layer.frames)
        return true;
    var f, fr, els;
    for (f = 0; f < layer.frameCount; f++) {
        fr = layer.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        if (String(fr.name || ""))
            return false;
        if (String(fr.actionScript || "").replace(/\s+/g, ""))
            return false;
        if (fr.soundName || fr.soundLibraryItem)
            return false;
        els = fr.elements;
        if (els && els.length > 0)
            return false;
    }
    return true;
}

function pruneEmptyLayersInTimeline(tl) {
    if (!tl || !tl.layers)
        return 0;
    var removed = 0;
    var L;
    for (L = tl.layerCount - 1; L >= 0; L--) {
        if (tl.layerCount <= 1)
            break;
        if (!layerIsEmptyPrunable(tl.layers[L]))
            continue;
        try {
            tl.deleteLayer(L);
            removed++;
        } catch (e) {}
    }
    return removed;
}

function pruneEmptyLayers(doc) {
    var removed = 0;
    var lib = doc.library;
    var i, it, tl;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic")
            continue;
        tl = null;
        try { tl = it.timeline; } catch (e0) { continue; }
        removed += pruneEmptyLayersInTimeline(tl);
    }
    try {
        removed += pruneEmptyLayersInTimeline(doc.getTimeline());
    } catch (e1) {}
    fl.trace("empty layers pruned=" + removed);
}

function flaIdentFromDoc(doc) {
    return String(doc.name || "").replace(/\.fla$/i, "");
}

function friendlyLibraryName(ident) {
    var s = String(ident || "").replace(/^\s+|\s+$/g, "");
    if (!s)
        return "Character";
    var parts = s.split(/[_\-\s]+/);
    var i, p, out = [];
    for (i = 0; i < parts.length; i++) {
        p = parts[i];
        if (!p)
            continue;
        out.push(p.charAt(0).toUpperCase() + p.substring(1));
    }
    return out.length ? out.join(" ") : "Character";
}

function identLinkage(ident) {
    return String(ident || "").replace(/^\s+|\s+$/g, "").replace(/[\s\-]+/g, "_");
}

function findHostLibraryItem(doc) {
    var ident = flaIdentFromDoc(doc);
    var linkage = identLinkage(ident);
    var lib = doc.library;
    var i, it, lc;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip")
            continue;
        lc = String(it.linkageClassName || "");
        if (lc && (lc == ident || lc == linkage || lc.toLowerCase() == linkage.toLowerCase()))
            return it;
    }
    try {
        var tl = doc.getTimeline();
        var L, f, e, fr, els, el;
        if (!tl || !tl.layers)
            return null;
        for (L = 0; L < tl.layerCount; L++) {
            for (f = 0; f < tl.layers[L].frameCount; f++) {
                fr = tl.layers[L].frames[f];
                if (!fr || fr.startFrame != f)
                    continue;
                els = fr.elements;
                if (!els)
                    continue;
                for (e = 0; e < els.length; e++) {
                    el = els[e];
                    if (el && el.libraryItem && el.libraryItem.itemType == "movie clip")
                        return el.libraryItem;
                }
            }
        }
    } catch (e1) {}
    return null;
}

function renameHostLibraryItem(doc) {
    leaveAllEditModes(doc);
    var host = findHostLibraryItem(doc);
    if (!host)
        return;
    var want = friendlyLibraryName(flaIdentFromDoc(doc));
    var path = String(host.name || "");
    var leaf = baseItemName(path);
    if (leaf == want)
        return;
    var folder = path.lastIndexOf("/") >= 0 ? path.substring(0, path.lastIndexOf("/")) : "";
    var dest = folder ? (folder + "/" + want) : want;
    var lib = doc.library;
    try { lib.selectNone(); } catch (e0) {}
    try {
        if (lib.selectItem(path) && lib.renameItem(want)) {
            fl.trace("renamed host " + path + " -> " + dest);
            return;
        }
    } catch (e1) {
        fl.trace("renameItem host failed: " + e1);
    }
    try {
        host.name = dest;
        fl.trace("set host.name " + dest);
    } catch (e2) {
        fl.trace("host rename failed: " + e2);
    }
}

function outlineHostLayersAfterStance(doc) {
    var host = findHostLibraryItem(doc);
    if (!host)
        return 0;
    var tl = null;
    try { tl = host.timeline; } catch (e0) { return 0; }
    if (!tl || !tl.layers)
        return 0;
    var stance = -1;
    var L, layer, n;
    for (L = 0; L < tl.layerCount; L++) {
        n = String(tl.layers[L].name || "").toLowerCase();
        if (n == "stance") {
            stance = L;
            break;
        }
    }
    if (stance < 0)
        return 0;
    var extra = 0;
    for (L = stance + 1; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        if (layerIsSpecialType(layer) || nameLooksLikeItemBox(layer.name))
            continue;
        try {
            if (!layer.outline) {
                layer.outline = true;
                extra++;
            }
        } catch (e1) {}
    }
    return extra;
}

function nameAndOutlineTimeline(tl) {
    var renamed = 0;
    var outlined = 0;
    if (!tl || !tl.layers)
        return { renamed: 0, outlined: 0 };
    var bitmapN = 1;
    var L, layer, first, want, outline;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        if (layerIsSpecialType(layer))
            continue;
        first = suggestedLayerName(layer);
        outline = layerShouldOutlineFromMajority(layer);
        if (first) {
            if (!outline && layerHasMinorityBoxFrames(layer) && String(first).toLowerCase() != "stance") {
                if (first.charAt(first.length - 1) != "*")
                    first = first + "*";
            }
            want = boxFamilyFromName(first) ? first : uniqueLayerName(tl, first, L);
        } else if (layerHasBitmap(layer)) {
            want = uniqueLayerName(tl, displayNameForBitmaps || ("bitmap layer " + bitmapN), L);
            bitmapN++;
        } else {
            want = "";
        }
        if (want && String(layer.name || "") != want) {
            try { layer.name = want; renamed++; } catch (e1) {}
        }
        if (outline) {
            try {
                layer.outline = true;
                outlined++;
            } catch (e2) {}
        }
    }
    return { renamed: renamed, outlined: outlined };
}

function smartNameAndOutlineLayers(doc) {
    var lib = doc.library;
    var outlined = 0;
    var renamed = 0;
    var i, it, tl, r;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic")
            continue;
        tl = null;
        try { tl = it.timeline; } catch (e0) { continue; }
        r = nameAndOutlineTimeline(tl);
        renamed += r.renamed;
        outlined += r.outlined;
    }
    try {
        r = nameAndOutlineTimeline(doc.getTimeline());
        renamed += r.renamed;
        outlined += r.outlined;
    } catch (e1) {}
    outlined += outlineHostLayersAfterStance(doc);
    fl.trace("layer names renamed=" + renamed + " outlined=" + outlined);
}

function findOrganizeTxt(flaDir) {
    var script = String(fl.scriptURI || "");
    var scriptDir = script ? script.substring(0, script.lastIndexOf("/")) : "";
    var cands = [];
    if (scriptDir)
        cands.push(scriptDir + "/ssf2mm_organize.txt");
    cands.push(flaDir + "/tools/ssf2mm_organize.txt");
    cands.push(flaDir + "/ssf2mm_organize.txt");
    var i;
    for (i = 0; i < cands.length; i++) {
        if (FLfile.exists(cands[i]))
            return cands[i];
    }
    return "";
}

function applyOrganizeBlueprint(doc, flaDir) {
    var uri = findOrganizeTxt(flaDir);
    if (!uri) {
        fl.trace("organize: no blueprint");
        return;
    }
    var text = FLfile.read(uri);
    if (!text || !/\t/.test(text)) {
        fl.trace("organize: skipped (empty blueprint)");
        return;
    }
    var lib = doc.library;
    var byName = {};
    var byLeaf = {};
    var i;

    function indexOnce() {
        byName = {};
        byLeaf = {};
        var k;
        for (k = 0; k < lib.items.length; k++) {
            var n = String(lib.items[k].name || "");
            if (!n) continue;
            byName[n] = true;
            var slash = n.lastIndexOf("/");
            var leaf = slash >= 0 ? n.substring(slash + 1) : n;
            if (!byLeaf[leaf])
                byLeaf[leaf] = n;
        }
    }

    function resolve(name) {
        if (byName[name])
            return name;
        var slash = name.lastIndexOf("/");
        var leaf = slash >= 0 ? name.substring(slash + 1) : name;
        return byLeaf[leaf] || "";
    }

    function leafOf(name) {
        var slash = name.lastIndexOf("/");
        return slash >= 0 ? name.substring(slash + 1) : name;
    }

    function parentFolder(name) {
        var slash = name.lastIndexOf("/");
        return slash >= 0 ? name.substring(0, slash) : "";
    }

    indexOnce();
    var lines = String(text).split(/\r\n|\n|\r/);
    var foldersMade = {};
    var f;
    for (i = 0; i < lines.length; i++) {
        var folderLine = String(lines[i]).replace(/^\s+|\s+$/g, "");
        if (!folderLine) continue;
        var fp = folderLine.split("\t");
        if (fp.length < 2 || !fp[0])
            continue;
        var folderPath = fp[0];
        if (foldersMade[folderPath])
            continue;
        foldersMade[folderPath] = true;
        if (!lib.itemExists(folderPath)) {
            try { lib.newFolder(folderPath); } catch (e1) {}
        }
    }

    var moved = 0;
    var renamed = 0;
    var skipped = 0;
    for (i = 0; i < lines.length; i++) {
        var line = String(lines[i]).replace(/^\s+|\s+$/g, "");
        if (!line) continue;
        var parts = line.split("\t");
        if (parts.length < 2 || !parts[1]) {
            skipped++;
            continue;
        }
        var folder = parts[0] || "";
        var item = resolve(parts[1]);
        if (!item) {
            skipped++;
            continue;
        }
        var destLeaf = (parts.length > 2 && parts[2]) ? parts[2] : leafOf(item);
        var dest = folder ? (folder + "/" + destLeaf) : destLeaf;
        if (item == dest) {
            moved++;
            continue;
        }
        var curLeaf = leafOf(item);
        if (parentFolder(item) != folder) {
            try {
                if (lib.moveToFolder(folder, item, false)) {
                    item = folder ? (folder + "/" + curLeaf) : curLeaf;
                    moved++;
                } else {
                    skipped++;
                    continue;
                }
            } catch (e2) {
                skipped++;
                continue;
            }
        } else {
            moved++;
        }
        if (curLeaf != destLeaf) {
            try {
                if (lib.selectItem(item) && lib.renameItem(destLeaf)) {
                    renamed++;
                    item = dest;
                }
            } catch (e3) {}
        }
        if ((i % 250) == 0 && i)
            fl.trace("organize " + i + "/" + lines.length);
    }
    try { lib.selectNone(); } catch (e4) {}
    fl.trace("organize moves=" + moved + " renamed=" + renamed + " skipped=" + skipped);
}

function readDisplayName(flaDir, doc) {
    var uri = flaDir + "/tools/ssf2mm_displayName.txt";
    if (FLfile.exists(uri)) {
        var t = FLfile.read(uri);
        if (t) {
            t = String(t).replace(/^\s+|\s+$/g, "").split(/\r?\n/)[0];
            if (t)
                return t;
        }
    }
    var host = findHostLibraryItem(doc);
    if (host) {
        var n = String(host.name || "");
        var slash = n.lastIndexOf("/");
        return slash >= 0 ? n.substring(slash + 1) : n;
    }
    return "stance";
}

function isSceneTimeline(doc, tl) {
    try {
        if (tl == doc.getTimeline())
            return true;
    } catch (e) {}
    var n = String(tl.name || "");
    return n == "Scene 1" || n == "Scene 1 ";
}

function overlayMergeLayer(tl, destIndex, srcIndex) {
    var src = tl.layers[srcIndex];
    var dest = tl.layers[destIndex];
    try { src.locked = false; dest.locked = false; } catch (eLock) {}
    var f, fr;
    for (f = 0; f < src.frameCount; f++) {
        fr = src.frames[f];
        if (!fr || fr.startFrame != f)
            continue;
        if (!fr.elements || !fr.elements.length)
            continue;
        try {
            tl.setSelectedLayers(srcIndex, true);
            tl.setSelectedFrames(f, f);
            tl.copyFrames(f, f);
            tl.setSelectedLayers(destIndex, true);
            tl.setSelectedFrames(f, f);
            tl.pasteFrames(f);
        } catch (e) {}
    }
    try { tl.deleteLayer(srcIndex); } catch (e2) {}
}

function mergeBoxFamiliesOnTimeline(tl) {
    var families = {};
    var L, n, fam, i, idxs, dest, k, need = false;
    for (L = 0; L < tl.layerCount; L++) {
        n = String(tl.layers[L].name || "");
        fam = boxFamilyFromName(n);
        if (!fam)
            continue;
        if (!families[fam])
            families[fam] = [];
        families[fam].push(L);
        if (families[fam].length >= 2)
            need = true;
    }
    if (!need)
        return;
    for (fam in families) {
        idxs = families[fam];
        if (idxs.length < 2)
            continue;
        dest = idxs[0];
        try { tl.layers[dest].locked = false; } catch (eD) {}
        for (i = idxs.length - 1; i >= 1; i--)
            overlayMergeLayer(tl, dest, idxs[i]);
        try { tl.layers[dest].name = fam; } catch (eName) {}
        try { tl.layers[dest].outline = true; } catch (eOut) {}
    }
}

function mergeBoxFamilies(doc) {
    // Do not copy/paste across the whole library — that hangs Animate
    // (hundreds of clips × HitBox layers). FLA2 merges those in C#.
    // JSFL only merges Scene 1 and the host clip.
    fl.trace("merge boxes: host + Scene 1 only (library clips: use FLA2)");
    var host = findHostLibraryItem(doc);
    if (host) {
        try { mergeBoxFamiliesOnTimeline(host.timeline); } catch (e0) {}
    }
    try { mergeBoxFamiliesOnTimeline(doc.getTimeline()); } catch (e1) {}
}

function isGroundLayer(layer) {
    var n = String(layer.name || "").toLowerCase();
    if (n.indexOf("ground") >= 0)
        return true;
    var inst = String(firstNamedInstance(layer) || "").toLowerCase();
    if (inst.indexOf("ground") >= 0)
        return true;
    var leaf = String(firstPlacedClipLeaf(layer) || "").toLowerCase();
    return leaf.indexOf("ground") >= 0;
}

function isGenericLeftoverLayer(layer) {
    var n = String(layer.name || "");
    if (layer.layerType == "folder" || layer.layerType == "mask" || layer.layerType == "guide")
        return false;
    return /^Layer(\s+\d+)?$/i.test(n) || /^Layer_\d+$/i.test(n) || /^Symbol\s+\d+$/i.test(n);
}

function parkLayersInFolder(tl, folderName, indices, renamePrefix) {
    if (!indices.length)
        return;
    var folderIdx = -1;
    var L;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name) == folderName && tl.layers[L].layerType == "folder") {
            folderIdx = L;
            break;
        }
    }
    if (folderIdx < 0) {
        try { folderIdx = tl.addNewLayer(folderName, "folder"); } catch (e0) { folderIdx = -1; }
    }
    var i, li, layer;
    for (i = 0; i < indices.length; i++) {
        li = indices[i];
        if (folderIdx >= 0 && li >= folderIdx)
            li++;
        layer = tl.layers[li];
        if (!layer)
            continue;
        try { layer.name = renamePrefix + " " + (i + 1); } catch (e1) {}
        try { layer.locked = true; } catch (e2) {}
        try { layer.parentLayer = tl.layers[folderIdx]; } catch (e3) {}
    }
    try { tl.layers[folderIdx].locked = true; } catch (e4) {}
}

function organizeGroundAndEffectsOnTimeline(tl, isScene) {
    var ground = [];
    var leftover = [];
    var L, layer;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        if (layer.layerType == "folder")
            continue;
        if (isGroundLayer(layer))
            ground.push(L);
        else if (!isScene && isGenericLeftoverLayer(layer))
            leftover.push(L);
    }
    parkLayersInFolder(tl, "Ground", ground, "reference");
    parkLayersInFolder(tl, "Effects", leftover, "Effect");
}

function organizeGroundAndEffects(doc) {
    var lib = doc.library;
    var i, it;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic")
            continue;
        try { organizeGroundAndEffectsOnTimeline(it.timeline, false); } catch (e0) {}
    }
    try { organizeGroundAndEffectsOnTimeline(doc.getTimeline(), true); } catch (e1) {}
}

function applyDisplayNameLayerNames(doc, displayName) {
    if (!displayName)
        return;
    var host = findHostLibraryItem(doc);
    function renameStance(tl) {
        var L, layer;
        for (L = 0; L < tl.layerCount; L++) {
            layer = tl.layers[L];
            var n = String(layer.name || "").toLowerCase();
            if (n == "stance" || n == String(displayName).toLowerCase()) {
                try { layer.name = displayName; } catch (e) {}
            }
        }
    }
    if (host) {
        try { renameStance(host.timeline); } catch (e0) {}
    }
    try { renameStance(doc.getTimeline()); } catch (e1) {}
}

function lockAndRaiseDisplayName(doc, displayName) {
    function apply(tl, raise) {
        var L, layer, art = -1;
        for (L = 0; L < tl.layerCount; L++) {
            layer = tl.layers[L];
            var isArt = String(layer.name || "") == displayName;
            if (isArt)
                art = L;
            try { layer.locked = !isArt; } catch (e0) {}
        }
        if (raise && art > 0) {
            try { tl.reorderLayer(art, 0); } catch (e1) {}
        }
    }
    var lib = doc.library;
    var i, it;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic")
            continue;
        try { apply(it.timeline, false); } catch (e2) {}
    }
    try { apply(doc.getTimeline(), true); } catch (e3) {}
}

main();
