/*
 * SSF2 Mod Manager — instance/keyframe tracker (JSFL only)
 *
 * Does NOT rename instances. Dumps every instance + every keyframe, then diffs.
 *
 * 1. Open the FLA (saved to disk).
 * 2. Run this → GUIDE-ids-before.tsv
 * 3. Run the wizard / JSFL you want to test. Save.
 * 4. Run this again → GUIDE-ids-after.tsv + GUIDE-ids-diff.txt
 *
 * Diff:
 *   FRAME  fingerprint stayed, frame index changed
 *   POS    same lib+name+frame, x/y changed
 *   MISS   fingerprint gone
 *   KEY    layer keyframe index/duration/empty-count changed
 */

function main() {
    try { fl.showIdleMessage(false); } catch (e0) {}
    try { run(); }
    catch (e) {
        fl.trace("ssf2mm_id_track CRASH: " + e);
        alert("JSFL error:\n" + e);
    }
    try { fl.showIdleMessage(true); } catch (e1) {}
}

function run() {
    var doc = fl.getDocumentDOM();
    if (!doc) {
        alert("Open a FLA first.");
        return;
    }
    if (!doc.pathURI) {
        alert("Save the FLA to disk first.");
        return;
    }
    var dir = doc.pathURI.substring(0, doc.pathURI.lastIndexOf("/"));
    var beforeUri = dir + "/GUIDE-ids-before.tsv";
    var afterUri = dir + "/GUIDE-ids-after.tsv";
    var diffUri = dir + "/GUIDE-ids-diff.txt";
    var dump = dumpDoc(doc);

    if (!FLfile.exists(beforeUri)) {
        FLfile.write(beforeUri, dump);
        alert("Wrote GUIDE-ids-before.tsv (" + dump.split("\n").length + " lines).\n\nDid not change the FLA.\nRun your re-org, save, then run this JSFL again.");
        return;
    }

    FLfile.write(afterUri, dump);
    var report = diffTsv(FLfile.read(beforeUri), dump);
    FLfile.write(diffUri, report);
    alert("Wrote GUIDE-ids-after.tsv and GUIDE-ids-diff.txt\n\n" + report.split("\n").slice(0, 14).join("\n"));
}

function dumpDoc(doc) {
    var lines = ["kind\ttimeline\tlayer\tlayerIndex\tlayerType\tframe\tduration\tname\tlib\tx\ty\tempty"];
    eachTimeline(doc, function (tl, label) {
        var L, layer, f, fr, e, el, lib, nm, dur, empty, i;
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

function parseRows(text) {
    var inst = [];
    var keys = [];
    var rows = String(text || "").split("\n");
    var i, p;
    for (i = 0; i < rows.length; i++) {
        if (!rows[i] || rows[i].indexOf("kind\t") == 0)
            continue;
        p = rows[i].split("\t");
        if (p.length < 12)
            continue;
        if (p[0] == "INST") {
            inst.push({
                fp: p[8] + "|" + p[7] + "|" + p[9] + "|" + p[10],
                tl: p[1], layer: p[2], frame: p[5], name: p[7], lib: p[8], x: p[9], y: p[10]
            });
        } else if (p[0] == "KEY") {
            keys.push({
                k: p[1] + "|" + p[2] + "|" + p[5],
                tl: p[1], layer: p[2], frame: p[5], dur: p[6], empty: p[11]
            });
        }
    }
    return { inst: inst, keys: keys };
}

function groupFrames(insts) {
    var m = {};
    var i, fp, row;
    for (i = 0; i < insts.length; i++) {
        row = insts[i];
        fp = row.fp;
        if (!m[fp])
            m[fp] = [];
        m[fp].push(Number(row.frame));
        m[fp].tl = row.tl;
        m[fp].lib = row.lib;
        m[fp].layer = row.layer;
    }
    for (fp in m)
        m[fp].sort(function (a, b) { return a - b; });
    return m;
}

function diffTsv(beforeText, afterText) {
    var b = parseRows(beforeText);
    var a = parseRows(afterText);
    var bg = groupFrames(b.inst);
    var ag = groupFrames(a.inst);
    var miss = 0, frameCh = 0, extra = 0;
    var lines = [];
    var fp, i, same, bf, af;
    for (fp in bg) {
        if (!ag[fp]) {
            miss++;
            if (miss <= 40)
                lines.push("MISS\t" + bg[fp].tl + "\t" + bg[fp].layer + "\t" + bg[fp].lib + "\tframes=" + bg[fp].join(","));
            continue;
        }
        bf = bg[fp].join(",");
        af = ag[fp].join(",");
        if (bf != af) {
            frameCh++;
            if (frameCh <= 50)
                lines.push("FRAME\t" + bg[fp].tl + "\t" + bg[fp].layer + "\t" + bg[fp].lib + "\t" + bf + " -> " + af);
        }
    }
    for (fp in ag) {
        if (!bg[fp])
            extra++;
    }
    var keyMiss = 0, keyDur = 0, keyEmpty = 0;
    var bkeys = {};
    for (i = 0; i < b.keys.length; i++)
        bkeys[b.keys[i].k] = b.keys[i];
    var akeys = {};
    for (i = 0; i < a.keys.length; i++)
        akeys[a.keys[i].k] = a.keys[i];
    var k, bk, ak;
    for (k in bkeys) {
        bk = bkeys[k];
        ak = akeys[k];
        if (!ak) {
            keyMiss++;
            if (keyMiss <= 40)
                lines.push("KEYGONE\t" + bk.tl + "\t" + bk.layer + "\tf" + bk.frame + "\tdur=" + bk.dur + "\tempty=" + bk.empty);
            continue;
        }
        if (bk.dur != ak.dur) {
            keyDur++;
            if (keyDur <= 30)
                lines.push("KEYDUR\t" + bk.tl + "\t" + bk.layer + "\tf" + bk.frame + "\t" + bk.dur + " -> " + ak.dur);
        }
        if (bk.empty != ak.empty) {
            keyEmpty++;
            if (keyEmpty <= 30)
                lines.push("KEYEMPTY\t" + bk.tl + "\t" + bk.layer + "\tf" + bk.frame + "\tempty " + bk.empty + " -> " + ak.empty);
        }
    }
    return (
        "instBefore\t" + b.inst.length + "\n" +
        "instAfter\t" + a.inst.length + "\n" +
        "fpMissing\t" + miss + "\n" +
        "fpExtra\t" + extra + "\n" +
        "frameChanged\t" + frameCh + "\n" +
        "keyGone\t" + keyMiss + "\n" +
        "keyDurChanged\t" + keyDur + "\n" +
        "emptyKeyChanged\t" + keyEmpty + "\n\n" +
        lines.join("\n") + "\n"
    );
}

main();
