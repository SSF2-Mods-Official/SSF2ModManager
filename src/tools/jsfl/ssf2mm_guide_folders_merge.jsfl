/*
 * SSF2 Mod Manager — Animate guides for wizard steps 3 and 4
 *
 * Does NOT guess zip XML. It uses Layer.parentLayer, reorderLayer, locked,
 * copyFrames/pasteFrames, then File→Save so the .fla is Animate's format.
 *
 * Run Command with any FLA folder context (or from FLA/tools).
 * Writes next to the character FLA (or next to this script):
 *
 *   GUIDE-step3-folders.fla + .txt
 *     Scene 1: Ground folder, Effects folder, cloney at top, lock except art
 *     PoseClip: leftover Layer → Effect 1, same folders/lock (art not moved)
 *
 *   GUIDE-step4-merge.fla + .txt
 *     PoseMerge: two HitBox + two Item layers, then merge via copyFrames
 *
 * After it finishes, leave those GUIDE files. The zip XML inside them is
 * what C# must match for steps 3 and 4.
 */

function main() {
    try { fl.showIdleMessage(false); } catch (eIdle) {}
    try {
        runGuides();
    } finally {
        try { fl.showIdleMessage(true); } catch (eIdle2) {}
    }
}

function runGuides() {
    fl.outputPanel.clear();
    var dir = outputDir();
    if (!dir) {
        alert("Could not find a folder to write GUIDE flas.");
        return;
    }
    fl.trace("ssf2mm_guide_folders_merge → " + dir);

    var step3 = writeStep3Guide(dir);
    var step4 = writeStep4Guide(dir);

    alert(
        "Wrote Animate guides:\n\n" +
        (step3 ? "  GUIDE-step3-folders.fla + .txt\n" : "  step 3 FAILED\n") +
        (step4 ? "  GUIDE-step4-merge.fla + .txt\n" : "  step 4 FAILED\n") +
        "\nKeep those files. Zip XML in them is the spec for wizard steps 3–4."
    );
}

function outputDir() {
    var doc = fl.getDocumentDOM();
    if (doc && doc.pathURI) {
        var d = doc.pathURI.substring(0, doc.pathURI.lastIndexOf("/"));
        if (d)
            return d;
    }
    var script = String(fl.scriptURI || "");
    if (!script)
        return "";
    var scriptDir = script.substring(0, script.lastIndexOf("/"));
    if (scriptDir.substring(scriptDir.length - 6).toLowerCase() == "/tools")
        return scriptDir.substring(0, scriptDir.lastIndexOf("/"));
    return scriptDir;
}

function writeStep3Guide(dir) {
    var doc = null;
    try { doc = fl.createDocument("timeline"); } catch (e0) {
        fl.trace("createDocument step3: " + e0);
        return false;
    }
    if (!doc)
        return false;

    var scene = doc.getTimeline();
    rebuildTimeline(scene, ["Actions", "HitBox", "ground", "cloney", "spark effects", "Item"]);
    paintLayer(doc, scene, indexOf(scene, "HitBox"), "#FF00FF", 10, 10);
    paintLayer(doc, scene, indexOf(scene, "ground"), "#00AA00", 40, 80);
    paintLayer(doc, scene, indexOf(scene, "cloney"), "#CC0000", 70, 30);
    paintLayer(doc, scene, indexOf(scene, "spark effects"), "#8888FF", 120, 20);
    paintLayer(doc, scene, indexOf(scene, "Item"), "#FF8800", 20, 120);

    applyStep3(scene, "cloney", true);

    try { doc.library.addNewItem("movie clip", "PoseClip"); } catch (eAdd) {
        fl.trace("add PoseClip: " + eAdd);
    }
    try { doc.library.editItem("PoseClip"); } catch (eEd) {
        fl.trace("edit PoseClip: " + eEd);
    }
    var pose = doc.getTimeline();
    rebuildTimeline(pose, ["Actions", "HitBox", "Layer 5", "cloney", "Item", "ground"]);
    paintLayer(doc, pose, indexOf(pose, "HitBox"), "#FF00FF", 10, 10);
    paintLayer(doc, pose, indexOf(pose, "Layer 5"), "#AAAAAA", 90, 10);
    paintLayer(doc, pose, indexOf(pose, "cloney"), "#CC0000", 70, 30);
    paintLayer(doc, pose, indexOf(pose, "Item"), "#FF8800", 20, 120);
    paintLayer(doc, pose, indexOf(pose, "ground"), "#00AA00", 40, 80);
    applyStep3(pose, "cloney", false);
    try { doc.exitEditMode(); } catch (eEx) {}

    var txt = [];
    txt.push("# GUIDE-step3-folders — JSFL after folders/lock");
    txt.push("# Scene 1: art to top, Ground + Effects folders, lock except cloney");
    txt.push("# PoseClip: Layer 5 → Effect 1, folders/lock, art stays in place");
    txt.push("");
    txt.push(dumpTimeline(doc.getTimeline(), "scene:Scene 1"));
    try {
        doc.library.editItem("PoseClip");
        txt.push("");
        txt.push(dumpTimeline(doc.getTimeline(), "lib:PoseClip"));
        doc.exitEditMode();
    } catch (eDump) {
        txt.push("PoseClip dump failed: " + eDump);
    }

    var uri = dir + "/GUIDE-step3-folders.fla";
    FLfile.write(dir + "/GUIDE-step3-folders.txt", txt.join("\n") + "\n");
    var ok = saveAndClose(doc, uri);
    fl.trace("step3 " + (ok ? "saved " : "FAILED ") + uri);
    return ok;
}

function writeStep4Guide(dir) {
    var doc = null;
    try { doc = fl.createDocument("timeline"); } catch (e0) {
        fl.trace("createDocument step4: " + e0);
        return false;
    }
    if (!doc)
        return false;

    try { doc.library.addNewItem("movie clip", "PoseMerge"); } catch (eAdd) {}
    try { doc.library.editItem("PoseMerge"); } catch (eEd) {}
    var tl = doc.getTimeline();
    rebuildTimeline(tl, ["Actions", "HitBox", "HitBox", "Item", "Item", "cloney"]);
    try { tl.insertFrames(12, true, 0); } catch (eFr) {}

    var hitA = firstIndexNamed(tl, "HitBox");
    var hitB = lastIndexNamed(tl, "HitBox");
    var itemA = firstIndexNamed(tl, "Item");
    var itemB = lastIndexNamed(tl, "Item");

    paintLayerAtFrame(doc, tl, hitA, 0, "#FF00FF", 10, 10);
    paintLayerAtFrame(doc, tl, hitB, 5, "#AA00AA", 90, 10);
    paintLayerAtFrame(doc, tl, itemA, 0, "#FF8800", 10, 90);
    paintLayerAtFrame(doc, tl, itemB, 8, "#CC6600", 90, 90);
    paintLayer(doc, tl, indexOf(tl, "cloney"), "#CC0000", 50, 40);

    var before = dumpTimeline(tl, "lib:PoseMerge BEFORE merge") + "\n" + dumpKeyframes(tl);

    mergeBoxFamiliesOnTimeline(tl);

    var after = dumpTimeline(tl, "lib:PoseMerge AFTER merge") + "\n" + dumpKeyframes(tl);
    try { doc.exitEditMode(); } catch (eEx) {}

    FLfile.write(
        dir + "/GUIDE-step4-merge.txt",
        "# GUIDE-step4-merge — copyFrames merge of two HitBox + two Item\n" +
        "# HitBox/Item should each be one layer after merge; outline on those layers.\n\n" +
        before + "\n\n" + after + "\n"
    );

    var uri = dir + "/GUIDE-step4-merge.fla";
    var ok = saveAndClose(doc, uri);
    fl.trace("step4 " + (ok ? "saved " : "FAILED ") + uri);
    return ok;
}

function applyStep3(tl, art, isScene) {
    // Folders last. Reorder/rename first so parentLayerIndex stays valid.
    if (!isScene)
        collectEffects(tl, art);
    if (isScene)
        moveArtToTop(tl, art);
    wrapGround(tl);
    wrapEffects(tl);
    lockExceptArt(tl, art);
}

function wrapGround(tl) {
    var idxs = [];
    var L, n;
    for (L = 0; L < tl.layerCount; L++) {
        if (tl.layers[L].layerType == "folder")
            continue;
        n = String(tl.layers[L].name || "").toLowerCase();
        if (n.indexOf("ground") >= 0)
            idxs.push(L);
    }
    parkInFolder(tl, "Ground", idxs, "reference", true);
}

function collectEffects(tl, art) {
    var L, layer, n, type, i;
    var leftovers = [];
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        type = String(layer.layerType || "");
        if (type == "folder")
            continue;
        n = String(layer.name || "");
        if (boxFamilyFromName(n))
            continue;
        if (n.toLowerCase() == String(art).toLowerCase())
            continue;
        if (/^effect/i.test(n) || n.toLowerCase() == "ground" || /^reference /i.test(n))
            continue;
        if (/^actions$/i.test(n) || /^labels layer$/i.test(n))
            continue;
        if (/effect/i.test(n) || /^(Layer(\s+\d+)?|Layer_\d+|Symbol\s+\d+)$/i.test(n))
            leftovers.push(layer);
    }
    for (i = 0; i < leftovers.length; i++) {
        try { leftovers[i].name = "Effect " + (i + 1); } catch (e) {}
    }
}

function wrapEffects(tl) {
    var L, n, type;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].layerType || "") == "folder" &&
            /^effects$/i.test(String(tl.layers[L].name || "")))
            return;
    }
    var idxs = [];
    for (L = 0; L < tl.layerCount; L++) {
        type = String(tl.layers[L].layerType || "");
        if (type == "folder")
            continue;
        n = String(tl.layers[L].name || "");
        if (/effect/i.test(n))
            idxs.push(L);
    }
    parkInFolder(tl, "Effects", idxs, "", false);
}

function parkInFolder(tl, folderName, indices, renamePrefix, lockFolder) {
    if (!indices.length)
        return;
    indices.sort(function (a, b) { return a - b; });
    var names = [];
    var i, layer, ci, folderIdx, dest;
    for (i = 0; i < indices.length; i++) {
        layer = tl.layers[indices[i]];
        if (renamePrefix)
            try { layer.name = renamePrefix + " " + (i + 1); } catch (e0) {}
        names.push(String(layer.name));
        try { layer.locked = false; } catch (e1) {}
    }
    folderIdx = folderIndex(tl, folderName);
    if (folderIdx < 0) {
        try {
            tl.setSelectedLayers(indexOf(tl, names[0]), true);
            folderIdx = tl.addNewLayer(folderName, "folder", false);
        } catch (e2) {
            try { folderIdx = tl.addNewLayer(folderName, "folder"); } catch (e3) { folderIdx = -1; }
        }
        folderIdx = folderIndex(tl, folderName);
        ci = indexOf(tl, names[0]);
        if (folderIdx >= 0 && ci >= 0 && folderIdx > ci) {
            try { tl.reorderLayer(folderIdx, ci); } catch (eTop) {}
            folderIdx = folderIndex(tl, folderName);
        }
    }
    if (folderIdx < 0)
        return;
    try { tl.layers[folderIdx].locked = false; } catch (eU) {}
    for (i = 0; i < names.length; i++) {
        folderIdx = folderIndex(tl, folderName);
        ci = indexOf(tl, names[i]);
        if (ci < 0 || folderIdx < 0)
            continue;
        dest = folderIdx + 1 + i;
        if (ci != dest) {
            try { tl.reorderLayer(ci, dest); } catch (eR) {
                fl.trace("reorderLayer " + names[i] + " -> " + dest + ": " + eR);
            }
        }
        folderIdx = folderIndex(tl, folderName);
        ci = indexOf(tl, names[i]);
        if (ci < 0 || folderIdx < 0)
            continue;
        try {
            tl.layers[ci].parentLayer = tl.layers[folderIdx];
            fl.trace("parent " + names[i] + " @" + ci + " -> " + folderName + " @" + folderIdx +
                " now=" + (tl.layers[ci].parentLayer ? tl.layers[ci].parentLayer.name : "null"));
        } catch (e4) {
            fl.trace("parentLayer failed " + folderName + " " + e4);
        }
    }
    if (lockFolder) {
        try { tl.layers[folderIndex(tl, folderName)].locked = true; } catch (e5) {}
    }
    for (i = 0; i < names.length; i++) {
        try { tl.layers[indexOf(tl, names[i])].locked = true; } catch (e6) {}
    }
}

function folderIndex(tl, folderName) {
    var L;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name) == folderName && tl.layers[L].layerType == "folder")
            return L;
    }
    return -1;
}

function moveArtToTop(tl, art) {
    var L, idx = -1;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name || "").toLowerCase() == String(art).toLowerCase()) {
            idx = L;
            break;
        }
    }
    if (idx > 0) {
        try { tl.reorderLayer(idx, 0); } catch (e) {
            fl.trace("reorderLayer art: " + e);
        }
    }
}

function lockExceptArt(tl, art) {
    var L, layer, isArt;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        isArt = String(layer.name || "").toLowerCase() == String(art).toLowerCase();
        try { layer.locked = !isArt; } catch (e) {}
    }
}

function rebuildTimeline(tl, names) {
    while (tl.layerCount > 1) {
        try { tl.deleteLayer(tl.layerCount - 1); } catch (e0) { break; }
    }
    try { tl.layers[0].name = names[0]; } catch (e1) {}
    var i;
    for (i = 1; i < names.length; i++) {
        try { tl.addNewLayer(names[i], "normal", false); } catch (e2) {
            try { tl.addNewLayer(names[i]); } catch (e3) {}
        }
    }
    for (i = 0; i < names.length && i < tl.layerCount; i++) {
        try { tl.layers[i].name = names[i]; } catch (e4) {}
    }
}

function paintLayer(doc, tl, index, color, x, y) {
    paintLayerAtFrame(doc, tl, index, 0, color, x, y);
}

function paintLayerAtFrame(doc, tl, index, frame, color, x, y) {
    if (index < 0)
        return;
    try { tl.setSelectedLayers(index, true); } catch (e0) {}
    try { tl.currentLayer = index; } catch (e1) {}
    try { tl.currentFrame = frame; } catch (e2) {}
    if (frame > 0) {
        try { tl.convertToKeyframes(frame, frame); } catch (e3) {
            try { tl.insertKeyframe(frame); } catch (e4) {}
        }
    }
    try { doc.selectNone(); } catch (e5) {}
    try { doc.setFillColor(color); } catch (e6) {}
    try {
        doc.addNewRectangle({ left: x, top: y, right: x + 60, bottom: y + 40 }, 0);
    } catch (e7) {
        fl.trace("rect failed: " + e7);
    }
}

function compactName(name) {
    return String(name || "").toLowerCase().split("_").join("").split(" ").join("");
}

function boxFamilyFromName(name) {
    var n = compactName(name).replace(/[0-9]+/g, "");
    if (!n)
        return "";
    if (n.indexOf("itembox") >= 0 || n == "item")
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
    try { tl.deleteLayer(srcIndex); } catch (eDel) {}
}

function collectBoxFamilies(tl) {
    var families = {};
    var L, n, fam;
    for (L = 0; L < tl.layerCount; L++) {
        n = String(tl.layers[L].name || "");
        fam = boxFamilyFromName(n);
        if (!fam)
            continue;
        if (!families[fam])
            families[fam] = [];
        families[fam].push(L);
    }
    return families;
}

function mergeBoxFamiliesOnTimeline(tl) {
    if (!tl || !tl.layers)
        return false;
    var any = false;
    var progressed = true;
    var fam, idxs, dest;
    while (progressed) {
        progressed = false;
        var families = collectBoxFamilies(tl);
        for (fam in families) {
            idxs = families[fam];
            if (idxs.length < 2)
                continue;
            dest = idxs[0];
            try { tl.layers[dest].locked = false; } catch (eD) {}
            overlayMergeLayer(tl, dest, idxs[1]);
            dest = firstIndexNamed(tl, String(tl.layers[dest].name || fam));
            try { tl.layers[dest].name = fam; } catch (eName) {}
            if (fam != "Item") {
                try { tl.layers[dest].outline = true; } catch (eOut) {}
            }
            any = true;
            progressed = true;
            break;
        }
    }
    return any;
}

function dumpTimeline(tl, label) {
    var lines = [];
    var L, layer, parent;
    lines.push("timeline\t" + label);
    lines.push("index\tname\tlayerType\tlocked\toutline\tparent");
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        parent = "";
        try { if (layer.parentLayer) parent = String(layer.parentLayer.name || ""); } catch (e) {}
        lines.push(
            L + "\t" +
            String(layer.name || "") + "\t" +
            String(layer.layerType || "") + "\t" +
            (layer.locked ? "true" : "false") + "\t" +
            (layer.outline ? "true" : "false") + "\t" +
            parent
        );
    }
    return lines.join("\n");
}

function dumpKeyframes(tl) {
    var lines = ["keyframes\tlayer\tindex\tduration\telements"];
    var L, layer, f, fr, dur;
    for (L = 0; L < tl.layerCount; L++) {
        layer = tl.layers[L];
        for (f = 0; f < layer.frameCount; f++) {
            fr = layer.frames[f];
            if (!fr || fr.startFrame != f)
                continue;
            dur = 1;
            try { dur = fr.duration; } catch (e) {}
            lines.push(
                String(layer.name || "") + "\t" +
                f + "\t" +
                dur + "\t" +
                (fr.elements ? fr.elements.length : 0)
            );
        }
    }
    return lines.join("\n");
}

function indexOf(tl, name) {
    var L;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name) == name)
            return L;
    }
    return -1;
}

function firstIndexNamed(tl, name) {
    return indexOf(tl, name);
}

function lastIndexNamed(tl, name) {
    var L, idx = -1;
    for (L = 0; L < tl.layerCount; L++) {
        if (String(tl.layers[L].name) == name)
            idx = L;
    }
    return idx;
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
