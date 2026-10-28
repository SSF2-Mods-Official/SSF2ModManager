/*
 * SSF2 Mod Manager — Animate ground-truth (JSFL)
 *
 * Zip XML outline="true" is ignored by Animate. This script uses the live
 * Layer API (layer.outline = true), which is what actually turns on outline.
 *
 * Run: Commands → Run Command… (or drag onto Animate).
 * If no FLA is open, opens the sibling .fla next to this script.
 *
 * Writes next to the FLA:
 *   GUIDE-animate-truth.fla  — 6 labeled layers (HitBox outlined, Item not, …)
 *   GUIDE-animate-truth.txt   — JSFL property dump of that guide
 *   jsfl-layers-before.txt    — open FLA, Animate's layer.outline BEFORE we touch it
 *   jsfl-layers-after.txt    — same, AFTER we set outline on *box* (not Item)
 *
 * Then File → Save the character FLA. HitBox should be hollow squares in the
 * timeline. Zip those txt/fla files if you want the C# wizard to match Animate.
 */

function main() {
    try { fl.showIdleMessage(false); } catch (eIdle) {}
    try {
        runTruth();
    } finally {
        try { fl.showIdleMessage(true); } catch (eIdle2) {}
    }
}

function runTruth() {
    fl.outputPanel.clear();
    var doc = ensureFlaDocument();
    if (!doc) {
        alert("Open the character FLA first, or put this script in that FLA's tools folder.");
        return;
    }
    if (!doc.pathURI) {
        alert("Save the FLA to disk first, then run this again.");
        return;
    }

    var flaDir = doc.pathURI.substring(0, doc.pathURI.lastIndexOf("/"));
    fl.trace("ssf2mm_animate_truth: " + doc.pathURI);

    FLfile.write(flaDir + "/jsfl-layers-before.txt", dumpAllTimelines(doc, "BEFORE jsfl outline"));

    var n = applyBoxOutlines(doc);
    fl.trace("set layer.outline=true on " + n + " box layer(s) (Item skipped)");

    FLfile.write(flaDir + "/jsfl-layers-after.txt", dumpAllTimelines(doc, "AFTER jsfl outline"));

    try { doc.save(); } catch (eSave) {
        fl.trace("save character FLA failed: " + eSave);
    }

    writeGuideFla(flaDir);

    alert(
        "JSFL outline applied on " + n + " box layers (not Item).\n\n" +
        "Wrote:\n" +
        "  jsfl-layers-before.txt\n" +
        "  jsfl-layers-after.txt\n" +
        "  GUIDE-animate-truth.fla + .txt\n\n" +
        "HitBox layers should now show as outlines in this FLA.\n" +
        "Keep GUIDE-animate-truth.fla — that is Animate's real format."
    );
}

function ensureFlaDocument() {
    var doc = fl.getDocumentDOM();
    if (doc)
        return doc;
    var uri = findSiblingFlaUri();
    if (!uri)
        return null;
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
    var d, i, files, prefer;
    for (d = 0; d < dirs.length; d++) {
        files = FLfile.listFolder(dirs[d] + "/*.fla", "files") || [];
        if (!files.length)
            continue;
        prefer = dirs[d].substring(dirs[d].lastIndexOf("/") + 1) + ".fla";
        for (i = 0; i < files.length; i++) {
            if (String(files[i]).toLowerCase() == prefer.toLowerCase() &&
                String(files[i]).toLowerCase().indexOf("guide-") < 0)
                return dirs[d] + "/" + files[i];
        }
        for (i = 0; i < files.length; i++) {
            if (String(files[i]).toLowerCase().indexOf("guide-") < 0)
                return dirs[d] + "/" + files[i];
        }
    }
    return "";
}

function applyBoxOutlines(doc) {
    var n = 0;
    eachTimeline(doc, function (tl) {
        n += applyBoxOutlinesOnTimeline(tl);
    });
    return n;
}

function applyBoxOutlinesOnTimeline(tl) {
    var n = 0;
    var L, layer, want, wasLocked;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        if (!layer || layer.layerType == "folder" || layer.layerType == "guide" || layer.layerType == "mask")
            continue;
        want = layerShouldOutline(layer);
        wasLocked = !!layer.locked;
        try { layer.locked = false; } catch (e0) {}
        try {
            layer.outline = want;
            if (want)
                n++;
        } catch (e1) {
            fl.trace("outline failed on " + layer.name + ": " + e1);
        }
        try { layer.locked = wasLocked; } catch (e2) {}
    }
    return n;
}

function layerShouldOutline(layer) {
    if (isItemLabel(layer.name))
        return false;
    if (hasBoxToken(layer.name))
        return true;
    return layerHasBoxInstance(layer);
}

function layerHasBoxInstance(layer) {
    var f, e, el, name, frames;
    try { frames = layer.frames; } catch (e0) { return false; }
    if (!frames)
        return false;
    for (f = 0; f < frames.length; f++) {
        if (!frames[f] || !frames[f].elements)
            continue;
        for (e = 0; e < frames[f].elements.length; e++) {
            el = frames[f].elements[e];
            name = String(el.name || "");
            if (isItemLabel(name))
                continue;
            if (hasBoxToken(name))
                return true;
        }
    }
    return false;
}

function hasBoxToken(name) {
    return String(name || "").toLowerCase().indexOf("box") >= 0;
}

function isItemLabel(name) {
    var n = String(name || "").toLowerCase().replace(/[\s*_]+/g, "");
    return n == "item" || n.indexOf("itembox") >= 0;
}

function eachTimeline(doc, fn) {
    var i, it, lib;
    try {
        for (i = 0; i < doc.timelines.length; i++)
            fn(doc.timelines[i], "scene:" + doc.timelines[i].name);
    } catch (e0) {}
    lib = doc.library;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic" && it.itemType != "button")
            continue;
        try {
            fn(it.timeline, "lib:" + itemName(it));
        } catch (e1) {}
    }
}

function itemName(it) {
    try { return String(it.name); } catch (e) { return "?"; }
}

function dumpAllTimelines(doc, title) {
    var lines = [];
    lines.push("# " + title);
    lines.push("# file\t" + doc.pathURI);
    lines.push("# Animate reads Layer.outline from its own object model.");
    lines.push("# If XML has outline=\"true\" but this dump says false, zip edits were ignored.");
    lines.push("timeline\tlayer\toutline\tlocked\tvisible\tcolor\tlayerType\twantBoxOutline");
    eachTimeline(doc, function (tl, label) {
        var L, layer, o;
        for (L = 0; L < tl.layerCount; L++) {
            layer = tl.layers[L];
            o = "false";
            try { o = layer.outline ? "true" : "false"; } catch (e) {}
            lines.push(
                label + "\t" +
                String(layer.name || "") + "\t" +
                o + "\t" +
                (layer.locked ? "true" : "false") + "\t" +
                (layer.visible ? "true" : "false") + "\t" +
                String(layer.color || "") + "\t" +
                String(layer.layerType || "") + "\t" +
                (layerShouldOutline(layer) ? "true" : "false")
            );
        }
    });
    return lines.join("\n") + "\n";
}

function writeGuideFla(flaDir) {
    var guideUri = flaDir + "/GUIDE-animate-truth.fla";
    var doc = null;
    try {
        doc = fl.createDocument("timeline");
    } catch (e0) {
        fl.trace("createDocument failed: " + e0);
        return;
    }
    if (!doc) {
        fl.trace("createDocument returned null");
        return;
    }

    var tl = doc.getTimeline();
    ensureLayerCount(tl, 6);

    setupGuideLayer(doc, tl, 0, "HitBox", true, "#FF4FFF", "#FF00FF");
    setupGuideLayer(doc, tl, 1, "HurtBox", true, "#4FFF4F", "#00FF00");
    setupGuideLayer(doc, tl, 2, "AttackBox", true, "#4F4FFF", "#0000FF");
    setupGuideLayer(doc, tl, 3, "Item", false, "#FF8000", "#FF8000");
    setupGuideLayer(doc, tl, 4, "cloney", false, "#FF0000", "#CC0000");
    setupGuideLayer(doc, tl, 5, "Effect 1", false, "#CCCCCC", "#999999");

    FLfile.write(flaDir + "/GUIDE-animate-truth.txt", dumpAllTimelines(doc, "GUIDE created by JSFL"));

    try {
        fl.saveDocument(doc, guideUri);
        fl.trace("wrote " + guideUri);
    } catch (e1) {
        fl.trace("save GUIDE failed: " + e1);
    }
}

function ensureLayerCount(tl, n) {
    while (tl.layerCount < n) {
        try { tl.addNewLayer("Layer " + (tl.layerCount + 1), "normal", false); } catch (e) { break; }
    }
}

function setupGuideLayer(doc, tl, index, name, outline, layerColor, fillColor) {
    var layer = tl.layers[index];
    try { layer.locked = false; } catch (e0) {}
    try { layer.visible = true; } catch (e1) {}
    try { layer.name = name; } catch (e2) {}
    try { layer.color = layerColor; } catch (e3) {}
    try { tl.setSelectedLayers(index, true); } catch (e4) {}
    try { tl.currentFrame = 0; } catch (e5) {}
    try { doc.selectNone(); } catch (e6) {}
    try {
        doc.setFillColor(fillColor);
        doc.addNewRectangle({ left: 40 + index * 8, top: 40, right: 200 + index * 8, bottom: 160 }, 0);
    } catch (e7) {
        fl.trace("rect on " + name + ": " + e7);
    }
    try { layer.outline = outline; } catch (e8) {
        fl.trace("outline on " + name + ": " + e8);
    }
}

main();
