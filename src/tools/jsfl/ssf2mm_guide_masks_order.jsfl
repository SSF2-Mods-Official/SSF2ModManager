/*
 * SSF2 Mod Manager — Animate guide for masks + leftover layer order
 *
 * Conservative step 3:
 *   - do not collect leftover Layer N into Effects
 *   - do not touch mask / masked / guide
 *   - wrap *effect* names in place only
 *   - leftover overlay keys must keep their frame indexes
 *
 * Writes GUIDE-step3-masks-order.fla + .txt next to the character FLA.
 */

function main() {
    try { fl.showIdleMessage(false); } catch (eIdle) {}
    try {
        runGuide();
    } catch (e) {
        fl.trace("ssf2mm_guide_masks_order CRASH: " + e);
        alert("JSFL error:\n" + e);
    }
    try { fl.showIdleMessage(true); } catch (eIdle2) {}
}

function runGuide() {
    fl.outputPanel.clear();
    var dir = outputDir();
    if (!dir) {
        alert("Could not find a folder to write the GUIDE fla.");
        return;
    }
    fl.trace("ssf2mm_guide_masks_order → " + dir);

    var doc = null;
    try { doc = fl.createDocument("timeline"); } catch (e0) {
        try { doc = fl.createDocument(); } catch (e1) {
            alert("createDocument failed: " + e1);
            return;
        }
    }
    if (!doc) {
        alert("createDocument returned null");
        return;
    }

    try { doc.library.addNewItem("movie clip", "KaiokenLike"); } catch (eAdd0) {
        fl.trace("add KaiokenLike: " + eAdd0);
    }
    try { doc.library.addNewItem("movie clip", "BairLike"); } catch (eAdd1) {
        fl.trace("add BairLike: " + eAdd1);
    }

    var kaiBefore = "";
    var kaiAfter = "";
    var bairBefore = "";
    var bairAfter = "";

    try { doc.library.editItem("KaiokenLike"); } catch (eEd0) {
        fl.trace("edit KaiokenLike: " + eEd0);
    }
    setupKaioken(doc.getTimeline());
    kaiBefore = dumpTimeline(doc.getTimeline(), "KaiokenLike BEFORE");
    applyConservativeStep3(doc.getTimeline(), "cloney");
    kaiAfter = dumpTimeline(doc.getTimeline(), "KaiokenLike AFTER conservative step 3");
    try { doc.exitEditMode(); } catch (eEx0) {}

    try { doc.library.editItem("BairLike"); } catch (eEd1) {
        fl.trace("edit BairLike: " + eEd1);
    }
    setupBair(doc.getTimeline());
    bairBefore = dumpTimeline(doc.getTimeline(), "BairLike BEFORE");
    applyConservativeStep3(doc.getTimeline(), "cloney");
    bairAfter = dumpTimeline(doc.getTimeline(), "BairLike AFTER conservative step 3");
    try { doc.exitEditMode(); } catch (eEx1) {}

    var txt = [
        "# GUIDE-step3-masks-order",
        "# Conservative step 3: NEVER collect leftover Layer N into Effects.",
        "# NEVER touch mask/masked/guide. Wrap *effect* names in place only.",
        "# Overlay keys must keep firstKey (Layer 6=10, Layer 4=8, Layer 1=0).",
        "",
        kaiBefore, "", kaiAfter, "", bairBefore, "", bairAfter, ""
    ].join("\n");
    FLfile.write(dir + "/GUIDE-step3-masks-order.txt", txt);

    var uri = dir + "/GUIDE-step3-masks-order.fla";
    var ok = saveAndClose(doc, uri);
    fl.trace("wrote " + uri + " ok=" + ok);
    alert(
        "Wrote GUIDE-step3-masks-order.fla + .txt\n\n" +
        "KaiokenLike: mask stack must stay intact.\n" +
        "BairLike: leftover Layer N stay in place; firstKey must not move.\n\n" +
        "Leave those files for C# to match."
    );
}

function outputDir() {
    var doc = fl.getDocumentDOM();
    if (doc && doc.pathURI)
        return doc.pathURI.substring(0, doc.pathURI.lastIndexOf("/"));
    var script = String(fl.scriptURI || "");
    if (!script)
        return "";
    var scriptDir = script.substring(0, script.lastIndexOf("/"));
    if (scriptDir.substring(scriptDir.length - 6).toLowerCase() == "/tools")
        return scriptDir.substring(0, scriptDir.lastIndexOf("/"));
    return scriptDir;
}

function setupKaioken(tl) {
    rebuildTimeline(tl, ["Actions", "Layer 9", "Layer 6", "Layer 5", "Mask", "Masked", "Layer 1"]);
    var mask = indexOf(tl, "Mask");
    var masked = indexOf(tl, "Masked");
    if (mask < 0 || masked < 0) {
        fl.trace("Kaioken layers missing mask=" + mask + " masked=" + masked);
        return;
    }
    try { tl.layers[mask].layerType = "mask"; } catch (e0) {
        fl.trace("set mask: " + e0);
    }
    try { tl.layers[masked].parentLayer = tl.layers[mask]; } catch (e1) {
        fl.trace("parent masked: " + e1);
    }
}

function setupBair(tl) {
    rebuildTimeline(tl, ["Actions", "Layer 6", "HitBox", "spark effects", "Layer 4", "cloney", "Item", "Layer 1"]);
    try { tl.insertFrames(12, true, 0); } catch (eFr) {
        fl.trace("insertFrames: " + eFr);
    }
    keyAt(tl, "Layer 6", 10);
    keyAt(tl, "Layer 4", 8);
    keyAt(tl, "Layer 1", 0);
}

function keyAt(tl, name, frame) {
    var L = indexOf(tl, name);
    if (L < 0)
        return;
    try { tl.setSelectedLayers(L, true); } catch (e0) {}
    try { tl.currentFrame = frame; } catch (e1) {}
    try { tl.convertToKeyframes(frame); } catch (e2) {
        try { tl.insertKeyframe(); } catch (e3) {
            fl.trace("keyAt " + name + " f" + frame + ": " + e3);
        }
    }
}

function applyConservativeStep3(tl, art) {
    wrapConsecutiveEffectRuns(tl);
    lockExceptArt(tl, art);
}

function wrapConsecutiveEffectRuns(tl) {
    var runs = [];
    var run = [];
    var L, layer, n, type;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        type = String(layer.layerType || "normal");
        n = String(layer.name || "");
        if (type == "folder" || type == "mask" || type == "masked" || type == "guide" || type == "guided") {
            if (run.length) { runs.push(run); run = []; }
            continue;
        }
        if (isEffectName(n))
            run.push(L);
        else if (run.length) {
            runs.push(run);
            run = [];
        }
    }
    if (run.length)
        runs.push(run);
    var r;
    for (r = runs.length - 1; r >= 0; r--)
        parkRunInFolder(tl, runs[r], "Effects");
}

function isEffectName(n) {
    if (/^effects$/i.test(n))
        return false;
    return /effect/i.test(n);
}

function parkRunInFolder(tl, indices, folderName) {
    if (!indices.length)
        return;
    var names = [];
    var i, folderIdx, ci, dest;
    for (i = 0; i < indices.length; i++) {
        names.push(String(tl.layers[indices[i]].name));
        try { tl.layers[indices[i]].locked = false; } catch (e0) {}
    }
    try { tl.setSelectedLayers(indexOf(tl, names[0]), true); } catch (e1) {}
    try { tl.addNewLayer(folderName, "folder", false); } catch (e2) {
        fl.trace("add folder: " + e2);
        return;
    }
    folderIdx = indexOf(tl, folderName);
    ci = indexOf(tl, names[0]);
    if (folderIdx > ci && folderIdx >= 0 && ci >= 0) {
        try { tl.reorderLayer(folderIdx, ci); } catch (e3) {}
    }
    for (i = 0; i < names.length; i++) {
        folderIdx = indexOf(tl, folderName);
        ci = indexOf(tl, names[i]);
        if (folderIdx < 0 || ci < 0)
            continue;
        dest = folderIdx + 1 + i;
        if (ci != dest) {
            try { tl.reorderLayer(ci, dest); } catch (e4) {}
        }
        folderIdx = indexOf(tl, folderName);
        ci = indexOf(tl, names[i]);
        if (folderIdx < 0 || ci < 0)
            continue;
        try { tl.layers[ci].parentLayer = tl.layers[folderIdx]; } catch (e5) {
            fl.trace("parent " + names[i] + ": " + e5);
        }
    }
}

function lockExceptArt(tl, art) {
    var L, layer, n;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        n = String(layer.name || "");
        if (n.toLowerCase() == String(art).toLowerCase()) {
            try { layer.locked = false; } catch (e0) {}
            continue;
        }
        try { layer.locked = true; } catch (e1) {}
    }
}

function rebuildTimeline(tl, names) {
    while (tl.layerCount > 1) {
        try { tl.deleteLayer(tl.layerCount - 1); } catch (e0) { break; }
    }
    try { tl.layers[0].name = names[0]; } catch (e1) {}
    var i;
    for (i = 1; i < names.length; i++) {
        try { tl.addNewLayer(names[i], "normal", false); } catch (e2) {}
    }
    for (i = 0; i < names.length && i < tl.layerCount; i++) {
        try { tl.layers[i].name = names[i]; } catch (e3) {}
        try { tl.layers[i].locked = false; } catch (e4) {}
    }
}

function dumpTimeline(tl, label) {
    var lines = [];
    var L, layer, parent, pidx;
    lines.push("timeline\t" + label);
    lines.push("index\tname\tlayerType\tlocked\tparent\tparentIndex\tfirstKey");
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        parent = "";
        pidx = "";
        try {
            if (layer.parentLayer) {
                parent = String(layer.parentLayer.name || "");
                pidx = String(indexOfLayer(tl, layer.parentLayer));
            }
        } catch (e) {}
        lines.push(
            L + "\t" +
            String(layer.name || "") + "\t" +
            String(layer.layerType || "") + "\t" +
            (layer.locked ? "true" : "false") + "\t" +
            parent + "\t" +
            pidx + "\t" +
            firstKey(layer)
        );
    }
    return lines.join("\n");
}

function firstKey(layer) {
    var f, fr;
    try {
        for (f = 0; f < layer.frameCount; f++) {
            fr = layer.frames[f];
            if (fr && fr.startFrame == f && fr.elements && fr.elements.length)
                return String(f);
        }
    } catch (e) {}
    return "";
}

function indexOf(tl, name) {
    var L;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name) == name)
            return L;
    }
    return -1;
}

function indexOfLayer(tl, layer) {
    var L;
    for (L = 0; L < tl.layerCount; L++) {
        if (tl.layers[L] == layer)
            return L;
    }
    return -1;
}

function saveAndClose(doc, uri) {
    try {
        fl.saveDocument(doc, uri);
    } catch (e0) {
        fl.trace("saveDocument " + uri + ": " + e0);
        try { doc.save(); } catch (e1) { return false; }
    }
    try { fl.closeDocument(doc, false); } catch (e2) {}
    return true;
}

main();
