/*
 * SSF2 Mod Manager — merge HitBox / Item / AttackBox / HurtBox layers
 *
 * Run AFTER ssf2mm_fla_cleanup.jsfl (names/folders first).
 * Opens the sibling .fla if needed, merges box-family layers on EVERY
 * movie clip + Scene 1, saves, leaves the FLA open.
 *
 * Copies ONE frame at a time (copyFrames(f, f) → pasteFrames(f)).
 * Skips clips that already have a single layer per family.
 */

function main() {
    try { fl.showIdleMessage(false); } catch (e0) {}
    try {
        runMerge();
    } finally {
        try { fl.showIdleMessage(true); } catch (e1) {}
    }
}

function runMerge() {
    var doc = fl.getDocumentDOM();
    if (!doc) {
        var script = String(fl.scriptURI || "");
        var scriptDir = script ? script.substring(0, script.lastIndexOf("/")) : "";
        var dirs = [scriptDir];
        if (scriptDir.substring(scriptDir.length - 6).toLowerCase() == "/tools")
            dirs.push(scriptDir.substring(0, scriptDir.lastIndexOf("/")));
        var d, files, i, uri;
        for (d = 0; d < dirs.length && !doc; d++) {
            files = FLfile.listFolder(dirs[d] + "/*.fla", "files") || [];
            for (i = 0; i < files.length; i++) {
                if (/^RECOVER_/i.test(files[i]))
                    continue;
                uri = dirs[d] + "/" + files[i];
                try { doc = fl.openDocument(uri); } catch (eOpen) { doc = null; }
                if (doc)
                    break;
            }
        }
    }
    if (!doc) {
        alert("Open the FLA first, or put this script in FLA1/tools next to the .fla.");
        return;
    }
    fl.outputPanel.clear();
    fl.trace("ssf2mm_fla_merge_boxes: " + doc.name);

    var lib = doc.library;
    var n = 0;
    var mergedClips = 0;
    var i, it;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip")
            continue;
        n++;
        try {
            if (mergeBoxFamiliesOnTimeline(it.timeline))
                mergedClips++;
        } catch (eClip) {
            fl.trace("merge skip " + it.name + ": " + eClip);
        }
        if ((n % 25) == 0)
            fl.trace("merge clips scanned=" + n + " merged=" + mergedClips);
    }
    try { mergeBoxFamiliesOnTimeline(doc.getTimeline()); } catch (eScene) {}
    fl.trace("merge boxes done. clips scanned=" + n + " merged=" + mergedClips);
    try {
        doc.save();
        fl.trace("saved");
    } catch (eSave) {
        fl.trace("save failed: " + eSave);
        alert("Merge finished but Save failed. Use File → Save.");
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

function mergeBoxFamiliesOnTimeline(tl) {
    if (!tl || !tl.layers)
        return false;
    var families = {};
    var L, n, fam, i, idxs, dest, need = false;
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
        return false;
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
    return true;
}

main();
