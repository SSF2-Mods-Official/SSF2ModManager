using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace MergeFlaLayers;

internal static class FlaBoxLayerMerge
{
    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
        public int XmlFilesChanged { get; init; }
        public int TimelinesMerged { get; init; }
        public int LayersRemoved { get; init; }
        public string? BackupPath { get; init; }
    }

    public static Result Run(string flaPath) => Run(flaPath, writeBackup: true);

    public static Result Run(string flaPath, bool writeBackup)
    {
        if (string.IsNullOrWhiteSpace(flaPath) || !File.Exists(flaPath))
            return new Result { Ok = false, Message = "FLA not found: " + flaPath };

        string? bak = null;
        try
        {
            if (writeBackup)
            {
                bak = flaPath + ".bak";
                if (!File.Exists(bak))
                    File.Copy(flaPath, bak, overwrite: false);
            }

            var entries = ReadZip(flaPath);
            var files = 0;
            var timelines = 0;
            var removed = 0;
            foreach (var key in entries.Keys.ToList())
            {
                if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    continue;
                var xml = Encoding.UTF8.GetString(entries[key]);
                if (xml.IndexOf("DOMLayer", StringComparison.Ordinal) < 0)
                    continue;
                var next = MergeXml(xml, out var t, out var r);
                if (t > 0 || r > 0 || !string.Equals(next, xml, StringComparison.Ordinal))
                {
                    entries[key] = Encoding.UTF8.GetBytes(next);
                    files++;
                }
                timelines += t;
                removed += r;
            }

            if (files == 0)
                return new Result
                {
                    Ok = true,
                    Message = "Nothing to merge (already one layer per box family).",
                    BackupPath = bak
                };

            WriteZipAtomic(flaPath, entries);
            using (var zip = ZipFile.OpenRead(flaPath))
            {
                if (zip.Entries.Count == 0)
                    throw new InvalidOperationException("Wrote an empty zip.");
            }

            return new Result
            {
                Ok = true,
                Message = "Merged box layers in " + files + " XML file(s): "
                    + timelines + " timeline(s), removed " + removed + " layer(s).",
                XmlFilesChanged = files,
                TimelinesMerged = timelines,
                LayersRemoved = removed,
                BackupPath = bak
            };
        }
        catch (Exception ex)
        {
            try
            {
                if (bak != null && File.Exists(bak))
                    File.Copy(bak, flaPath, overwrite: true);
            }
            catch { /* keep original error */ }
            return new Result { Ok = false, Message = ex.Message, BackupPath = bak };
        }
    }

    public static string MergeXml(string xml, out int timelinesMerged, out int layersRemoved)
    {
        timelinesMerged = 0;
        layersRemoved = 0;
        if (string.IsNullOrWhiteSpace(xml))
            return xml;
        XDocument doc;
        try { doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace); }
        catch { return xml; }

        var changed = false;
        foreach (var layers in doc.Descendants().Where(e => e.Name.LocalName == "layers").ToList())
        {
            var n = MergeLayers(layers, out var mutated);
            if (n <= 0 && !mutated)
                continue;
            timelinesMerged++;
            layersRemoved += n;
            changed = true;
        }
        if (!changed)
            return xml;
        return doc.Declaration == null
            ? doc.ToString(SaveOptions.DisableFormatting)
            : doc.Declaration + Environment.NewLine + doc.ToString(SaveOptions.DisableFormatting);
    }

    public static int MergeLayers(XElement layers) => MergeLayers(layers, out _);

    public static int MergeLayers(XElement layers, out bool mutated)
    {
        SplitNonFamilyContent(layers);
        var n = MergeFamilies(layers);
        var padded = PadBoxLayers(layers);
        var outlined = OutlineBoxFamilies(layers);
        mutated = n > 0 || padded || outlined > 0;
        return n;
    }

    /// <summary>
    /// Animate holds a layer's last key to the clip end, and a hole with no
    /// DOMFrame is not an empty key — publish then keeps one PlaceObject across
    /// later bursts. Empty lead-in, interior gaps, and tail matching the clip
    /// length force Place → Remove → Place.
    /// </summary>
    private static bool PadBoxLayers(XElement layers)
    {
        var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
        var end = 0;
        foreach (var layer in list)
        {
            var type = (string?)layer.Attribute("layerType") ?? "";
            if (type.Equals("folder", StringComparison.OrdinalIgnoreCase))
                continue;
            end = Math.Max(end, LayerEnd(layer));
        }
        if (end <= 0)
            return false;
        var changed = false;
        foreach (var layer in list)
        {
            if (FamilyOf(layer) == null)
                continue;
            var start = LayerEnd(layer);
            var first = FirstIndex(layer);
            EnsureLeadingEmpty(layer);
            if (FillInteriorGaps(layer))
                changed = true;
            StretchLayer(layer, end);
            if (first > 0 || LayerEnd(layer) != start)
                changed = true;
        }
        return changed;
    }

    /// <summary>
    /// Leftover family layers created at merge (AttackBox peeled off HitBox)
    /// never went through step-2 outline. Item stays filled.
    /// </summary>
    private static int OutlineBoxFamilies(XElement layers)
    {
        var n = 0;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var type = (string?)layer.Attribute("layerType") ?? "";
            if (type.Equals("folder", StringComparison.OrdinalIgnoreCase))
                continue;
            var fam = FamilyOf(layer);
            if (fam == null || fam.Equals("Item", StringComparison.OrdinalIgnoreCase))
                continue;
            var already = (string?)layer.Attribute("outline") == "true" &&
                         (string?)layer.Attribute("useOutlineView") == "true" &&
                         (string?)layer.Attribute("locked") == "true";
            if (already)
                continue;
            layer.SetAttributeValue("outline", "true");
            layer.SetAttributeValue("useOutlineView", "true");
            layer.SetAttributeValue("locked", "true");
            n++;
        }
        return n;
    }

    private static void EnsureLeadingEmpty(XElement layer)
    {
        var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (frames == null)
            return;
        var first = frames.Elements().Where(e => e.Name.LocalName == "DOMFrame")
            .OrderBy(FrameIndex).FirstOrDefault();
        if (first == null)
            return;
        var i = FrameIndex(first);
        if (i <= 0)
            return;
        var ns = layer.Name.Namespace;
        var empty = new XElement(ns + "DOMFrame",
            new XAttribute("index", "0"),
            new XElement(ns + "elements"));
        if (i > 1)
            empty.SetAttributeValue("duration", i.ToString(CultureInfo.InvariantCulture));
        InsertFrameSorted(frames, empty);
    }

    /// <summary>
    /// A missing DOMFrame between filled keys is not an empty keyframe. Insert
    /// (or extend) an empty span so publish emits RemoveObject before the next Place.
    /// </summary>
    private static bool FillInteriorGaps(XElement layer)
    {
        var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (frames == null)
            return false;
        var keys = frames.Elements().Where(e => e.Name.LocalName == "DOMFrame")
            .OrderBy(FrameIndex).ToList();
        var changed = false;
        var ns = layer.Name.Namespace;
        for (var i = 0; i < keys.Count - 1; i++)
        {
            var prev = keys[i];
            var next = keys[i + 1];
            var hole = FrameIndex(prev) + FrameDuration(prev);
            var nextStart = FrameIndex(next);
            if (nextStart <= hole)
                continue;
            var gap = nextStart - hole;
            if (!FrameDrawable(prev))
            {
                prev.SetAttributeValue("duration", (nextStart - FrameIndex(prev)).ToString(CultureInfo.InvariantCulture));
                changed = true;
                continue;
            }
            var empty = new XElement(ns + "DOMFrame",
                new XAttribute("index", hole.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "elements"));
            if (gap > 1)
                empty.SetAttributeValue("duration", gap.ToString(CultureInfo.InvariantCulture));
            InsertFrameSorted(frames, empty);
            changed = true;
        }
        return changed;
    }

    private static int MergeFamilies(XElement layers)
    {
        var removed = 0;
        var guard = 0;
        while (guard++ < 500)
        {
            var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var merged = false;
            for (var i = 0; i < list.Count; )
            {
                var fam = FamilyOf(list[i]);
                if (fam == null)
                {
                    i++;
                    continue;
                }
                var run = new List<XElement> { list[i] };
                var j = i + 1;
                while (j < list.Count &&
                       string.Equals(FamilyOf(list[j]), fam, StringComparison.OrdinalIgnoreCase))
                {
                    run.Add(list[j]);
                    j++;
                }
                if (run.Count > 1)
                {
                    removed += MergeFamilyRun(layers, fam, run);
                    merged = true;
                    break;
                }
                i = j;
            }
            if (!merged)
                break;
        }
        return removed;
    }

    private static int MergeFamilyRun(XElement layers, string family, List<XElement> run)
    {
        var dest = run
            .OrderBy(FirstIndex)
            .ThenByDescending(LayerEnd)
            .ThenBy(el => el.Descendants().Count(e => e.Name.LocalName == "DOMFrame"))
            .ThenBy(el => run.IndexOf(el))
            .First();
        if (HasTween(dest))
            dest = run.FirstOrDefault(l => !HasTween(l)) ?? dest;
        if (HasTween(dest))
            return 0;
        dest.SetAttributeValue("name", family);
        if (!family.Equals("Item", StringComparison.OrdinalIgnoreCase))
        {
            dest.SetAttributeValue("outline", "true");
            dest.SetAttributeValue("useOutlineView", "true");
        }
        var removed = 0;
        foreach (var src in run.Where(l => !ReferenceEquals(l, dest)).Reverse().ToList())
        {
            if (HasTween(src))
                continue;
            var srcIndex = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(src);
            if (HasDrawable(src))
                OverlayFrames(dest, src, family);
            StretchLayer(dest, LayerEnd(src));
            if (HasNonFamily(src, family))
            {
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var l in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
                    used.Add((string?)l.Attribute("name") ?? "");
                src.SetAttributeValue("name", UniqueName(used, "FX"));
                continue;
            }
            src.Remove();
            if (srcIndex >= 0)
                AdjustParentIndex(layers, srcIndex);
            removed++;
        }
        SortFrames(dest);
        return removed;
    }

    private static string? FamilyOf(XElement layer)
    {
        var type = (string?)layer.Attribute("layerType") ?? "";
        if (type.Equals("folder", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("mask", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("guide", StringComparison.OrdinalIgnoreCase) ||
            type.Equals("guided", StringComparison.OrdinalIgnoreCase))
            return null;
        var name = (string?)layer.Attribute("name") ?? "";
        if (name.StartsWith("Effects ", StringComparison.Ordinal))
            return null;
        // Name only. First-instance guessing treats art (body as itemBox) as Item.
        return MapFamily(name);
    }

    private static void SplitNonFamilyContent(XElement layers)
    {
        var ns = layers.Name.Namespace;
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
            used.Add((string?)layer.Attribute("name") ?? "");
        var leftovers = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        XElement? insertAfter = null;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList())
        {
            var fam = FamilyOf(layer);
            if (fam == null || !HasNonFamily(layer, fam))
                continue;
            insertAfter = layer;
            foreach (var frame in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame").ToList())
            {
                var els = frame.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");
                if (els == null)
                    continue;
                var groups = els.Elements().Where(e => IsNonFamily(e, fam))
                    .GroupBy(e => LeftoverBucket(e))
                    .ToList();
                foreach (var g in groups)
                {
                    if (g.Key == "Layer")
                    {
                        var keep = g.Where(el =>
                        {
                            var lib = (string?)el.Attribute("libraryItemName") ?? "";
                            if (!string.IsNullOrEmpty(lib) &&
                                OtherLayerHasSamePlacement(layers, layer, FrameIndex(frame), FrameDuration(frame), el))
                            {
                                el.Remove();
                                return false;
                            }
                            return true;
                        }).ToList();
                        if (keep.Count == 0)
                            continue;
                        if (!leftovers.TryGetValue("FX", out var fxLayer))
                        {
                            fxLayer = new XElement(ns + "DOMLayer",
                                new XAttribute("name", UniqueName(used, "FX")),
                                new XElement(ns + "frames"));
                            insertAfter.AddAfterSelf(fxLayer);
                            leftovers["FX"] = fxLayer;
                            insertAfter = fxLayer;
                        }
                        MoveInstances(frame, fxLayer, keep, ns);
                        continue;
                    }
                    if (!leftovers.TryGetValue(g.Key, out var leftover))
                    {
                        leftover = new XElement(ns + "DOMLayer",
                            new XAttribute("name", UniqueName(used, g.Key)),
                            new XElement(ns + "frames"));
                        insertAfter.AddAfterSelf(leftover);
                        leftovers[g.Key] = leftover;
                        insertAfter = leftover;
                    }
                    MoveInstances(frame, leftover, g.ToList(), ns);
                }
            }
        }
    }

    private static string LeftoverBucket(XElement el)
    {
        if (el.Name.LocalName != "DOMSymbolInstance")
            return "Layer";
        var fam = MapFamily((string?)el.Attribute("name"));
        return string.IsNullOrEmpty(fam) ? "Layer" : fam;
    }

    private static string UniqueName(HashSet<string> used, string wanted)
    {
        var baseName = string.IsNullOrWhiteSpace(wanted) ? "Layer" : wanted.Trim();
        if (used.Add(baseName))
            return baseName;
        for (var n = 2; n < 1000; n++)
        {
            var c = baseName + " " + n;
            if (used.Add(c))
                return c;
        }
        return baseName;
    }

    private static bool HasNonFamily(XElement layer, string family) =>
        layer.Descendants().Any(e => IsNonFamily(e, family));

    private static bool IsNonFamily(XElement el, string family)
    {
        var n = el.Name.LocalName;
        if (n is "DOMBitmapInstance" or "DOMStaticText" or "DOMDynamicText" or "Edge" or "DOMShape")
            return true;
        if (n != "DOMSymbolInstance")
            return false;
        var fam = MapFamily((string?)el.Attribute("name"));
        return !string.Equals(fam, family, StringComparison.OrdinalIgnoreCase);
    }

    private static void DropDuplicateUnnamed(XElement layers, XElement srcLayer, XElement srcFrame,
        List<XElement> moved)
    {
        var start = FrameIndex(srcFrame);
        var dur = FrameDuration(srcFrame);
        foreach (var el in moved)
        {
            if (OtherLayerHasSamePlacement(layers, srcLayer, start, dur, el))
                el.Remove();
        }
    }

    private static bool OtherLayerHasSamePlacement(XElement layers, XElement self, int start, int dur, XElement inst)
    {
        var end = start + Math.Max(1, dur);
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (ReferenceEquals(layer, self))
                continue;
            var name = (string?)layer.Attribute("name") ?? "";
            var type = (string?)layer.Attribute("layerType") ?? "";
            if (type.Equals("folder", StringComparison.OrdinalIgnoreCase))
                continue;
            if (name.Equals("Actions", StringComparison.OrdinalIgnoreCase) ||
                name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                continue;
            if (FamilyOf(layer) != null)
                continue;
            foreach (var f in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
            {
                var i = FrameIndex(f);
                var frameEnd = i + FrameDuration(f);
                if (i >= end || start >= frameEnd)
                    continue;
                var els = f.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");
                if (els?.Elements().Any(x => SameGraphic(x, inst)) == true)
                    return true;
            }
        }
        return false;
    }

    private static void MoveInstances(XElement srcFrame, XElement destLayer, List<XElement> moved, XNamespace ns)
    {
        if (moved.Count == 0)
            return;
        var destFrames = destLayer.Elements().First(e => e.Name.LocalName == "frames");
        OverlayInstances(destFrames, ns, FrameIndex(srcFrame), FrameDuration(srcFrame), moved);
        foreach (var el in moved)
            el.Remove();
    }

    internal static string? MapFamily(string? instanceName)
    {
        var n = Regex.Replace(instanceName ?? "", @"[\s*_]+", "", RegexOptions.CultureInvariant).ToLowerInvariant();
        n = Regex.Replace(n, @"\d+$", "");
        if (n.Length == 0)
            return null;
        if (n.Contains("itembox", StringComparison.Ordinal) || n == "item")
            return "Item";
        if (n.Contains("hitbox", StringComparison.Ordinal) || n == "hit")
            return "HitBox";
        if (n.Contains("attackbox", StringComparison.Ordinal) || n.Contains("atkbox", StringComparison.Ordinal))
            return "AttackBox";
        if (n.Contains("hurtbox", StringComparison.Ordinal))
            return "HurtBox";
        if (n.Contains("grabbox", StringComparison.Ordinal))
            return "GrabBox";
        if (n.Contains("handbox", StringComparison.Ordinal) || n == "hand")
            return "HandBox";
        if (n.EndsWith("box", StringComparison.Ordinal) && n.Length > 3)
            return char.ToUpperInvariant(n[0]) + n[1..^3] + "Box";
        return null;
    }

    private static bool HasTween(XElement layer)
    {
        foreach (var f in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            var tw = (string?)f.Attribute("tweenType");
            if (!string.IsNullOrEmpty(tw) && !tw.Equals("None", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool HasDrawable(XElement layer) =>
        layer.Descendants().Any(e => e.Name.LocalName == "DOMFrame" && FrameDrawable(e));

    private static bool FrameDrawable(XElement frame)
    {
        foreach (var el in frame.Descendants())
        {
            var n = el.Name.LocalName;
            if (n is "DOMSymbolInstance" or "DOMBitmapInstance" or "DOMStaticText" or "DOMDynamicText")
                return true;
            if (n == "Edge")
                return true;
        }
        return false;
    }

    private static void OverlayFrames(XElement dest, XElement src, string family)
    {
        var destFrames = dest.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        var srcFrames = src.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (srcFrames == null)
            return;
        if (destFrames == null)
        {
            dest.Add(new XElement(srcFrames));
            return;
        }
        var ns = dest.Name.Namespace;
        foreach (var sf in srcFrames.Elements().Where(e => e.Name.LocalName == "DOMFrame").ToList())
        {
            var idx = FrameIndex(sf);
            var srcDur = FrameDuration(sf);
            var srcEls = sf.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");
            if (srcEls == null)
                continue;
            var moved = srcEls.Elements().Where(e => !IsNonFamily(e, family)).ToList();
            if (moved.Count == 0)
                continue;
            OverlayInstances(destFrames, ns, idx, srcDur, moved);
        }
    }

    internal static bool SamePlacement(XElement a, XElement b)
    {
        if (!string.Equals((string?)a.Attribute("name") ?? "", (string?)b.Attribute("name") ?? "", StringComparison.Ordinal))
            return false;
        return SameGraphic(a, b);
    }

    internal static bool SameGraphic(XElement a, XElement b)
    {
        if (!string.Equals((string?)a.Attribute("libraryItemName") ?? "", (string?)b.Attribute("libraryItemName") ?? "", StringComparison.OrdinalIgnoreCase))
            return false;
        return PlacementKey(a) == PlacementKey(b);
    }

    internal static string PlacementKey(XElement el)
    {
        var m = el.Descendants().FirstOrDefault(e => e.Name.LocalName == "Matrix");
        if (m == null)
            return "";
        return string.Join(",",
            (string?)m.Attribute("tx") ?? "",
            (string?)m.Attribute("ty") ?? "",
            (string?)m.Attribute("a") ?? "",
            (string?)m.Attribute("b") ?? "",
            (string?)m.Attribute("c") ?? "",
            (string?)m.Attribute("d") ?? "");
    }

    private static bool HasSameInstance(XElement destEls, XElement src) =>
        destEls.Elements().Any(e => SamePlacement(e, src));

    private static void OverlayInstances(XElement destFrames, XNamespace ns, int idx, int srcDur, List<XElement> moved)
    {
        var end = idx + srcDur;
        var f = idx;
        while (f < end)
        {
            var df = EnsureKeyframe(destFrames, ns, f);
            var destDur = FrameDuration(df);
            var take = Math.Min(destDur, end - f);
            if (destDur > take)
                EnsureKeyframe(destFrames, ns, f + take);
            var destEls = df.Elements().FirstOrDefault(e => e.Name.LocalName == "elements")
                ?? new XElement(ns + "elements");
            if (destEls.Parent == null)
                df.AddFirst(destEls);
            foreach (var child in moved.AsEnumerable().Reverse())
            {
                if (HasSameInstance(destEls, child))
                    continue;
                destEls.AddFirst(new XElement(child));
            }
            f += take;
        }
    }

    private static void StretchLayer(XElement layer, int end)
    {
        if (end <= LayerEnd(layer))
            return;
        var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (frames == null)
            return;
        var last = frames.Elements().Where(e => e.Name.LocalName == "DOMFrame")
            .OrderBy(FrameIndex).LastOrDefault();
        if (last == null)
            return;
        var lastEnd = FrameIndex(last) + FrameDuration(last);
        if (end <= lastEnd)
            return;
        if (!FrameDrawable(last))
        {
            last.SetAttributeValue("duration", (end - FrameIndex(last)).ToString(CultureInfo.InvariantCulture));
            return;
        }
        var ns = layer.Name.Namespace;
        var empty = new XElement(ns + "DOMFrame",
            new XAttribute("index", lastEnd.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("duration", (end - lastEnd).ToString(CultureInfo.InvariantCulture)),
            new XElement(ns + "elements"));
        InsertFrameSorted(frames, empty);
    }

    private static int FirstIndex(XElement layer) =>
        layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame")
            .Select(FrameIndex).DefaultIfEmpty(int.MaxValue).Min();

    private static int LayerEnd(XElement layer) =>
        layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame")
            .Select(f => FrameIndex(f) + FrameDuration(f)).DefaultIfEmpty(0).Max();

    private static void SortFrames(XElement layer)
    {
        var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (frames == null)
            return;
        var list = frames.Elements().Where(e => e.Name.LocalName == "DOMFrame")
            .OrderBy(FrameIndex).ToList();
        foreach (var f in list)
            f.Remove();
        foreach (var f in list)
            frames.Add(f);
    }

    private static void InsertFrameSorted(XElement frames, XElement neu)
    {
        var idx = FrameIndex(neu);
        XElement? after = null;
        foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            if (FrameIndex(f) < idx)
                after = f;
            else
                break;
        }
        if (after == null)
            frames.AddFirst(neu);
        else
            after.AddAfterSelf(neu);
    }

    private static XElement EnsureKeyframe(XElement frames, XNamespace ns, int index)
    {
        var exact = frames.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DOMFrame" && FrameIndex(e) == index);
        if (exact != null)
            return exact;
        XElement? cover = null;
        foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            var idx = FrameIndex(f);
            if (index >= idx && index < idx + FrameDuration(f))
            {
                cover = f;
                break;
            }
        }
        if (cover == null)
        {
            var created = new XElement(ns + "DOMFrame",
                new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "elements"));
            InsertFrameSorted(frames, created);
            return created;
        }
        var start = FrameIndex(cover);
        var dur = FrameDuration(cover);
        var headLen = index - start;
        var tailLen = start + dur - index;
        cover.SetAttributeValue("duration", headLen);
        if (headLen <= 1)
            cover.Attribute("duration")?.Remove();
        var tail = new XElement(cover);
        tail.SetAttributeValue("index", index.ToString(CultureInfo.InvariantCulture));
        if (tailLen > 1)
            tail.SetAttributeValue("duration", tailLen.ToString(CultureInfo.InvariantCulture));
        else
            tail.Attribute("duration")?.Remove();
        cover.AddAfterSelf(tail);
        return tail;
    }

    private static int FrameIndex(XElement frame)
    {
        int.TryParse((string?)frame.Attribute("index") ?? "0", NumberStyles.Integer, CultureInfo.InvariantCulture, out var idx);
        return idx;
    }

    private static int FrameDuration(XElement frame)
    {
        if (!int.TryParse((string?)frame.Attribute("duration") ?? "1", NumberStyles.Integer, CultureInfo.InvariantCulture, out var dur) || dur < 1)
            return 1;
        return dur;
    }

    private static void AdjustParentIndex(XElement layers, int removedIndex)
    {
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var parent = (string?)layer.Attribute("parentLayerIndex");
            if (string.IsNullOrEmpty(parent))
                continue;
            if (!int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                continue;
            if (p == removedIndex)
                layer.Attribute("parentLayerIndex")?.Remove();
            else if (p > removedIndex)
                layer.SetAttributeValue("parentLayerIndex", p - 1);
        }
    }

    private static Dictionary<string, byte[]> ReadZip(string flaPath)
    {
        var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);
        using var zip = ZipFile.OpenRead(flaPath);
        foreach (var entry in zip.Entries)
        {
            if (string.IsNullOrEmpty(entry.Name))
                continue;
            using var stream = entry.Open();
            using var ms = new MemoryStream();
            stream.CopyTo(ms);
            map[entry.FullName.Replace('\\', '/')] = ms.ToArray();
        }
        return map;
    }

    private static void WriteZipAtomic(string flaPath, Dictionary<string, byte[]> entries)
    {
        var tmp = flaPath + ".tmp";
        if (File.Exists(tmp))
            File.Delete(tmp);
        using (var zip = ZipFile.Open(tmp, ZipArchiveMode.Create))
        {
            foreach (var kv in entries.OrderBy(e => e.Key, StringComparer.OrdinalIgnoreCase))
            {
                var created = zip.CreateEntry(kv.Key.Replace('\\', '/'), CompressionLevel.Fastest);
                using var stream = created.Open();
                stream.Write(kv.Value, 0, kv.Value.Length);
            }
        }
        File.Copy(tmp, flaPath, overwrite: true);
        try { File.Delete(tmp); } catch { /* ignore */ }
    }
}
