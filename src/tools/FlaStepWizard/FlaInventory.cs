using System.Globalization;
using System.Text.Json;
using System.Xml.Linq;
using MergeFlaLayers;

namespace FlaStepWizard;

/// <summary>
/// Per-movieclip counts so wizard steps can be audited for missing or
/// misplaced instances (boxes, unnamed FX, bitmaps).
/// </summary>
public static class FlaInventory
{
    public sealed class Clip
    {
        public string Id { get; set; } = "";
        public List<string> Layers { get; set; } = new();
        public int MaxFrame { get; set; }
        public Dictionary<string, int> Names { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> Libs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BoxFamilies { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, int> BoxPlacements { get; set; } = new(StringComparer.Ordinal);
        public int Unnamed { get; set; }
        public int Bitmaps { get; set; }
    }

    public sealed class Report
    {
        public Dictionary<string, Clip> Clips { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    public static Report FromFla(string flaPath)
    {
        var report = new Report();
        var entries = FlaZip.Read(flaPath);
        foreach (var kv in entries)
        {
            if (!kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            if (kv.Value == null || kv.Value.Length == 0)
                continue;
            var xml = FlaZip.Utf8(kv.Value);
            if (xml.IndexOf("DOMLayer", StringComparison.Ordinal) < 0)
                continue;
            var doc = FlaZip.Parse(xml);
            if (doc?.Root == null)
                continue;
            var n = 0;
            foreach (var layers in doc.Descendants().Where(e => e.Name.LocalName == "layers"))
            {
                n++;
                var timeline = layers.Ancestors().FirstOrDefault(e => e.Name.LocalName == "DOMTimeline");
                var tName = (string?)timeline?.Attribute("name") ?? "";
                var id = kv.Key.Replace('\\', '/');
                if (!string.IsNullOrEmpty(tName) && n > 1)
                    id += "#" + tName;
                report.Clips[id] = FromLayers(id, layers);
            }
        }
        return report;
    }

    public static Clip FromLayers(string id, XElement layers)
    {
        var clip = new Clip { Id = id };
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var lname = (string?)layer.Attribute("name") ?? "";
            clip.Layers.Add(lname);
            var fam = FlaBoxLayerMerge.MapFamily(lname);
            if (fam != null)
                Add(clip.BoxFamilies, fam + "Layers", 1);
            foreach (var frame in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                var i = ParseInt(frame.Attribute("index"));
                var d = ParseInt(frame.Attribute("duration"));
                if (d < 1)
                    d = 1;
                clip.MaxFrame = Math.Max(clip.MaxFrame, i + d);
                foreach (var el in frame.Descendants())
                {
                    var tag = el.Name.LocalName;
                    if (tag == "DOMBitmapInstance")
                    {
                        clip.Bitmaps += d;
                        Add(clip.Libs, Leaf((string?)el.Attribute("libraryItemName") ?? ""), d);
                        continue;
                    }
                    if (tag != "DOMSymbolInstance")
                        continue;
                    var instName = (string?)el.Attribute("name") ?? "";
                    var lib = Leaf((string?)el.Attribute("libraryItemName") ?? "");
                    if (!string.IsNullOrEmpty(lib))
                        Add(clip.Libs, lib, d);
                    if (string.IsNullOrEmpty(instName))
                    {
                        clip.Unnamed += d;
                        continue;
                    }
                    Add(clip.Names, instName, d);
                    var box = FlaBoxLayerMerge.MapFamily(instName);
                    if (box == null)
                        continue;
                    Add(clip.BoxFamilies, box, d);
                    var tx = "";
                    var ty = "";
                    var m = el.Descendants().FirstOrDefault(e => e.Name.LocalName == "Matrix");
                    if (m != null)
                    {
                        tx = (string?)m.Attribute("tx") ?? "";
                        ty = (string?)m.Attribute("ty") ?? "";
                    }
                    var place = instName + "|" + lib + "|" + tx + "|" + ty;
                    for (var f = i; f < i + d; f++)
                        Add(clip.BoxPlacements, f.ToString(CultureInfo.InvariantCulture) + "|" + place);
                }
            }
        }
        ApplyHeldTails(clip, layers);
        return clip;
    }

    /// <summary>
    /// Animate keeps drawing a layer's last filled key until the clip ends.
    /// Count those frames as occupied so a short AttackBox (keys 6-8 on a
    /// 16-frame clip) diffs as extra boxes on 9-end.
    /// </summary>
    private static void ApplyHeldTails(Clip clip, XElement layers)
    {
        if (clip.MaxFrame <= 0)
            return;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var type = (string?)layer.Attribute("layerType") ?? "";
            if (type.Equals("folder", StringComparison.OrdinalIgnoreCase))
                continue;
            XElement? last = null;
            var lastEnd = 0;
            foreach (var frame in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                var i = ParseInt(frame.Attribute("index"));
                var d = ParseInt(frame.Attribute("duration"));
                if (d < 1)
                    d = 1;
                if (last == null || i >= FrameIndexOf(last))
                {
                    last = frame;
                    lastEnd = i + d;
                }
            }
            if (last == null || lastEnd >= clip.MaxFrame)
                continue;
            var boxes = last.Descendants().Where(e =>
            {
                if (e.Name.LocalName != "DOMSymbolInstance")
                    return false;
                return FlaBoxLayerMerge.MapFamily((string?)e.Attribute("name")) != null;
            }).ToList();
            if (boxes.Count == 0)
                continue;
            var extra = clip.MaxFrame - lastEnd;
            foreach (var el in boxes)
            {
                var instName = (string?)el.Attribute("name") ?? "";
                var lib = Leaf((string?)el.Attribute("libraryItemName") ?? "");
                var box = FlaBoxLayerMerge.MapFamily(instName);
                if (box == null)
                    continue;
                Add(clip.Names, instName, extra);
                Add(clip.BoxFamilies, box, extra);
                var tx = "";
                var ty = "";
                var m = el.Descendants().FirstOrDefault(e => e.Name.LocalName == "Matrix");
                if (m != null)
                {
                    tx = (string?)m.Attribute("tx") ?? "";
                    ty = (string?)m.Attribute("ty") ?? "";
                }
                var place = instName + "|" + lib + "|" + tx + "|" + ty;
                for (var f = lastEnd; f < clip.MaxFrame; f++)
                    Add(clip.BoxPlacements, f.ToString(CultureInfo.InvariantCulture) + "|" + place);
            }
        }
    }

    private static int FrameIndexOf(XElement frame) => ParseInt(frame.Attribute("index"));

    public static List<string> Diff(Report before, Report after)
    {
        var issues = new List<string>();
        foreach (var kv in before.Clips)
        {
            if (!after.Clips.TryGetValue(kv.Key, out var b))
            {
                issues.Add("missing clip " + kv.Key);
                continue;
            }
            var a = kv.Value;
            foreach (var lib in a.Libs)
            {
                b.Libs.TryGetValue(lib.Key, out var n);
                if (n < lib.Value)
                    issues.Add(kv.Key + " lib " + lib.Key + " " + lib.Value + " -> " + n);
            }
            foreach (var name in a.Names)
            {
                if (FlaBoxLayerMerge.MapFamily(name.Key) == null)
                    continue;
                b.Names.TryGetValue(name.Key, out var n);
                if (n < name.Value)
                    issues.Add(kv.Key + " name " + name.Key + " " + name.Value + " -> " + n);
            }
            foreach (var fam in a.BoxFamilies)
            {
                if (fam.Key.EndsWith("Layers", StringComparison.Ordinal))
                    continue;
                b.BoxFamilies.TryGetValue(fam.Key, out var n);
                if (n != fam.Value)
                    issues.Add(kv.Key + " family " + fam.Key + " " + fam.Value + " -> " + n);
            }
            foreach (var lib in b.Libs)
            {
                a.Libs.TryGetValue(lib.Key, out var n);
                if (n < lib.Value)
                    issues.Add(kv.Key + " extra lib " + lib.Key + " " + n + " -> " + lib.Value);
            }
            foreach (var name in b.Names)
            {
                if (FlaBoxLayerMerge.MapFamily(name.Key) == null)
                    continue;
                a.Names.TryGetValue(name.Key, out var n);
                if (n < name.Value)
                    issues.Add(kv.Key + " extra name " + name.Key + " " + n + " -> " + name.Value);
            }
            foreach (var place in a.BoxPlacements)
            {
                b.BoxPlacements.TryGetValue(place.Key, out var n);
                if (n < place.Value)
                    issues.Add(kv.Key + " missing box " + place.Key + " " + place.Value + " -> " + n);
            }
            foreach (var place in b.BoxPlacements)
            {
                a.BoxPlacements.TryGetValue(place.Key, out var n);
                if (n < place.Value)
                    issues.Add(kv.Key + " extra box " + place.Key + " " + n + " -> " + place.Value);
            }
        }
        return issues;
    }

    public static string ToJson(Report report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });

    public static Report FromJson(string json) =>
        JsonSerializer.Deserialize<Report>(json) ?? new Report();

    private static void Add(Dictionary<string, int> map, string key, int n = 1)
    {
        if (string.IsNullOrEmpty(key))
            return;
        map.TryGetValue(key, out var cur);
        map[key] = cur + n;
    }

    private static string Leaf(string path)
    {
        var n = (path ?? "").Replace('\\', '/');
        var slash = n.LastIndexOf('/');
        return slash >= 0 ? n[(slash + 1)..] : n;
    }

    private static int ParseInt(XAttribute? attr)
    {
        int.TryParse((string?)attr ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var n);
        return n;
    }
}
