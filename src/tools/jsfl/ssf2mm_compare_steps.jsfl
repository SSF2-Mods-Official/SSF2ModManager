/*
 * Compare cloney-step0.fla .. cloney-step3.fla in this folder.
 * Dumps every instance + keyframe, then diffs consecutive steps for
 * moved frames, filled/removed empty keys, and duplicate fingerprints.
 *
 * Does not edit the FLAs. Open any doc in that folder (or none) and Run.
 */

function main() {
    try { fl.showIdleMessage(false); } catch (e0) {}
    try { run(); }
    catch (e) {
        fl.trace("ssf2mm_compare_steps CRASH: " + e);
        alert("JSFL error:\n" + e);
    }
    try { fl.showIdleMessage(true); } catch (e1) {}
}

function run() {
    var dir = folderUri();
    if (!dir) {
        alert("Could not find the FLA folder.");
        return;
    }
    fl.outputPanel.clear();
    fl.trace("compare steps in " + dir);
    var dumps = [];
    var n;
    for (n = 0; n <= 3; n++) {
        var uri = dir + "/cloney-step" + n + ".fla";
        if (!FLfile.exists(uri)) {
            alert("Missing " + uri);
            return;
        }
        var doc = fl.openDocument(uri);
        if (!doc) {
            alert("Could not open " + uri);
            return;
        }
        dumps[n] = dumpDoc(doc);
        FLfile.write(dir + "/GUIDE-ids-step" + n + ".tsv", dumps[n]);
        try { fl.closeDocument(doc, false); } catch (eC) {}
        fl.trace("dumped step " + n);
    }
    var parts = [];
    parts.push(diffTsv("step0 -> step1 organize", dumps[0], dumps[1]));
    parts.push(diffTsv("step1 -> step2 names", dumps[1], dumps[2]));
    parts.push(diffTsv("step2 -> step3 folders", dumps[2], dumps[3]));
    var report = parts.join("\n");
    FLfile.write(dir + "/GUIDE-ids-diff.txt", report);
    alert("Wrote GUIDE-ids-step0..3.tsv and GUIDE-ids-diff.txt\n\n" + report.split("\n").slice(0, 18).join("\n"));
}

function folderUri() {
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

function dumpDoc(doc) {
    var lines = ["kind\ttimeline\tlayer\tlayerIndex\tlayerType\tframe\tduration\tname\tlib\tx\ty\tempty"];
    eachTimeline(doc, function (tl, label) {
        var L, layer, f, fr, e, el, lib, nm, dur, empty;
        for (L = 0; L < tl.layerCount; L++) {
            layer = tl.layers[L];
            if (!layer.frames)
                continue;
            for (f = 0; f < layer.frameCount; f++) {
                fr = layer.frames[f];
                if (!fr || fr.startFrame != f)
                    continue;
                dur = 1;
                try { dur = fr.duration; } catch (eD) {}
                empty = (!fr.elements || fr.elements.length == 0) ? "1" : "0";
                lines.push(
                    "KEY\t" + label + "\t" + String(layer.name || "") + "\t" + L + "\t" +
                    String(layer.layerType || "normal") + "\t" + f + "\t" + dur + "\t\t\t\t\t" + empty
                );
                if (!fr.elements)
                    continue;
                for (e = 0; e < fr.elements.length; e++) {
                    el = fr.elements[e];
                    if (String(el.elementType || "") != "instance")
                        continue;
                    lib = "";
                    try { if (el.libraryItem) lib = String(el.libraryItem.name || ""); } catch (e0) {}
                    nm = String(el.name || "");
                    lines.push(
                        "INST\t" + label + "\t" + String(layer.name || "") + "\t" + L + "\t" +
                        String(layer.layerType || "normal") + "\t" + f + "\t" + dur + "\t" +
                        nm + "\t" + lib + "\t" + num(el.x) + "\t" + num(el.y) + "\t" + empty
                    );
                }
            }
        }
    });
    return lines.join("\n") + "\n";
}

function eachTimeline(doc, fn) {
    var i, it;
    try {
        for (i = 0; i < doc.timelines.length; i++)
            fn(doc.timelines[i], "scene:" + doc.timelines[i].name);
    } catch (e0) {}
    var lib = doc.library;
    for (i = 0; i < lib.items.length; i++) {
        it = lib.items[i];
        if (it.itemType != "movie clip" && it.itemType != "graphic" && it.itemType != "button")
            continue;
        try { fn(it.timeline, "lib:" + it.name); } catch (e1) {}
    }
}

function num(v) {
    var n = Number(v);
    if (isNaN(n))
        return "0";
    return String(Math.round(n * 100) / 100);
}

function parseInst(text) {
    var rows = [];
    var lines = String(text || "").split("\n");
    var i, p;
    for (i = 0; i < lines.length; i++) {
        if (lines[i].indexOf("INST\t") != 0)
            continue;
        p = lines[i].split("\t");
        if (p.length < 12)
            continue;
        rows.push({
            fp: p[8] + "|" + p[7] + "|" + p[9] + "|" + p[10],
            slot: p[1] + "|" + p[3] + "|" + p[5] + "|" + p[9] + "|" + p[10],
            tl: p[1], layer: p[2], li: p[3], frame: p[5], name: p[7], lib: p[8], x: p[9], y: p[10]
        });
    }
    return rows;
}

function parseKeys(text) {
    var rows = [];
    var lines = String(text || "").split("\n");
    var i, p;
    for (i = 0; i < lines.length; i++) {
        if (lines[i].indexOf("KEY\t") != 0)
            continue;
        p = lines[i].split("\t");
        if (p.length < 12)
            continue;
        rows.push({
            k: p[1] + "|" + p[3] + "|" + p[5],
            tl: p[1], layer: p[2], frame: p[5], dur: p[6], empty: p[11]
        });
    }
    return rows;
}

function fpFrames(rows) {
    var m = {};
    var i, fp;
    for (i = 0; i < rows.length; i++) {
        fp = rows[i].fp;
        if (!m[fp])
            m[fp] = { frames: [], tl: rows[i].tl, lib: rows[i].lib, layer: rows[i].layer, n: 0 };
        m[fp].frames.push(Number(rows[i].frame));
        m[fp].n++;
    }
    for (fp in m)
        m[fp].frames.sort(function (a, b) { return a - b; });
    return m;
}

function diffTsv(label, beforeText, afterText) {
    var bi = parseInst(beforeText);
    var ai = parseInst(afterText);
    var bg = fpFrames(bi);
    var ag = fpFrames(ai);
    var miss = 0, frameCh = 0, extra = 0, dups = 0;
    var lines = ["==== " + label];
    var fp, bf, af;
    for (fp in ag) {
        if (ag[fp].n > bg[fp] ? bg[fp].n : 0 && ag[fp].n > 1 && (!bg[fp] || ag[fp].n > bg[fp].n))
            dups++;
    }
    for (fp in bg) {
        if (!ag[fp]) {
            miss++;
            if (miss <= 20)
                lines.push("MISS\t" + bg[fp].tl + "\t" + bg[fp].layer + "\t" + bg[fp].lib + "\t" + bg[fp].frames.join(","));
            continue;
        }
        if (bg[fp].n < ag[fp].n) {
            dups++;
            if (dups <= 25)
                lines.push("DUP\t" + bg[fp].tl + "\t" + bg[fp].lib + "\t" + bg[fp].n + " -> " + ag[fp].n);
        }
        bf = bg[fp].frames.join(",");
        af = ag[fp].frames.join(",");
        if (bf != af) {
            frameCh++;
            if (frameCh <= 30)
                lines.push("FRAME\t" + bg[fp].tl + "\t" + bg[fp].lib + "\t" + bf + " -> " + af);
        }
    }
    for (fp in ag) {
        if (!bg[fp])
            extra++;
    }
    var bkeys = parseKeys(beforeText);
    var akeys = parseKeys(afterText);
    var bm = {}, am = {}, i, k;
    for (i = 0; i < bkeys.length; i++)
        bm[bkeys[i].k] = bkeys[i];
    for (i = 0; i < akeys.length; i++)
        am[akeys[i].k] = akeys[i];
    var keyGone = 0, keyFill = 0;
    for (k in bm) {
        if (bm[k].empty != "1")
            continue;
        if (!am[k]) {
            keyGone++;
            if (keyGone <= 20)
                lines.push("KEYGONE\t" + bm[k].tl + "\t" + bm[k].layer + "\tf" + bm[k].frame);
        } else if (am[k].empty != "1") {
            keyFill++;
            if (keyFill <= 20)
                lines.push("KEYFILL\t" + bm[k].tl + "\t" + bm[k].layer + "\tf" + bm[k].frame);
        }
    }
    lines.unshift(
        "inst " + bi.length + " -> " + ai.length +
        " miss=" + miss + " extra=" + extra + " frameChanged=" + frameCh +
        " dupFp=" + dups + " emptyGone=" + keyGone + " emptyFilled=" + keyFill
    );
    lines.unshift("==== " + label);
    return lines.join("\n") + "\n";
}

main();
