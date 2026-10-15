using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using MergeFlaLayers;

namespace FlaStepWizard;

public static class FlaCleanupSteps
{
    public sealed class Result
    {
        public bool Ok { get; init; }
        public string Message { get; init; } = "";
    }


    public static Result Organize(string flaPath)
    {
        var tsvPath = Path.Combine(Path.GetDirectoryName(flaPath) ?? "", "tools", "ssf2mm_organize.txt");
        if (!File.Exists(tsvPath))
            return Ok("No tools/ssf2mm_organize.txt — skipped.");
        return Organize(flaPath, File.ReadAllText(tsvPath));
    }

    /// <summary>Apply an in-memory organize blueprint. Does not read or write <c>tools/</c>.</summary>
    public static Result Organize(string flaPath, string organizeTsv)
    {
        var tsv = organizeTsv ?? "";
        if (tsv.IndexOf('\t') < 0)
            return Ok("Organize blueprint empty — skipped.");
        var map = ParseOrganizeMap(tsv);
        if (map.Count == 0)
            return Ok("Organize blueprint empty — skipped.");
        var entries = FlaZip.Read(flaPath);
        var n = 0;
        foreach (var key in entries.Keys.ToList())
        {
            if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var xml = FlaZip.Utf8(entries[key]);
            var next = ApplyOrganizeXml(xml, map);
            if (string.Equals(next, xml, StringComparison.Ordinal))
                continue;
            entries[key] = FlaZip.Bytes(next);
            n++;
        }
        foreach (var key in entries.Keys.ToList())
        {
            if (!key.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var xml = FlaZip.Utf8(entries[key]);
            var next = InjectLibraryFolders(xml, map.Values);
            if (!string.Equals(next, xml, StringComparison.Ordinal))
            {
                entries[key] = FlaZip.Bytes(next);
                n++;
            }
        }
        n += RelocateLibraryFiles(entries, map);
        FlaZip.Write(flaPath, entries);
        return Ok("Updated " + n + " XML file(s) from organize blueprint.");
    }

    /// <summary>
    /// Library-only organize rewrite: item opening <c>name</c>, <c>libraryItemName</c>,
    /// and <c>soundName</c>. Clip <c>DOMTimeline name</c> becomes the dest leaf (no slashes).
    /// </summary>
    public static string ApplyOrganizeXml(string xml, string tsv) =>
        ApplyOrganizeXml(xml, ParseOrganizeMap(tsv ?? ""));

    public static string ApplyOrganizeXml(string xml, IReadOnlyDictionary<string, string> map)
    {
        if (string.IsNullOrEmpty(xml) || map == null || map.Count == 0)
            return xml ?? "";
        var timelineRenames = new List<(string Old, string DestLeaf)>();
        var next = LibraryItemOpenNameRegex.Replace(xml, m =>
        {
            var old = m.Groups["n"].Value;
            var dest = ResolveOrganizeDest(map, old);
            if (dest.Equals(old, StringComparison.Ordinal))
                return m.Value;
            if (m.Groups["tag"].Value.Equals("DOMSymbolItem", StringComparison.OrdinalIgnoreCase))
                timelineRenames.Add((old, Leaf(dest)));
            return "<" + m.Groups["tag"].Value + m.Groups["rest"].Value + "name=\"" + dest + "\"";
        });
        next = Regex.Replace(
            next,
            @"\blibraryItemName=""(?<n>[^""]+)""",
            m => "libraryItemName=\"" + ResolveOrganizeDest(map, m.Groups["n"].Value) + "\"",
            RegexOptions.CultureInvariant);
        next = Regex.Replace(
            next,
            @"\bsoundName=""(?<n>[^""]+)""",
            m => "soundName=\"" + ResolveOrganizeDest(map, m.Groups["n"].Value) + "\"",
            RegexOptions.CultureInvariant);
        next = RewriteMappedAttr(next, "href", map);
        next = RewriteMappedAttr(next, "bitmapPath", map);
        foreach (var (old, destLeaf) in timelineRenames)
        {
            if (string.IsNullOrEmpty(destLeaf) || destLeaf.IndexOf('/') >= 0)
                continue;
            foreach (var prev in new[] { old, Leaf(old) }.Distinct(StringComparer.Ordinal))
            {
                if (prev.Equals(destLeaf, StringComparison.Ordinal) ||
                    prev.Equals("Scene 1", StringComparison.OrdinalIgnoreCase))
                    continue;
                var rx = new Regex(
                    @"<DOMTimeline\b(?<rest>[^>]*?)\bname=""" + Regex.Escape(prev) + @"""",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
                next = rx.Replace(next, m => "<DOMTimeline" + m.Groups["rest"].Value + "name=\"" + destLeaf + "\"", 1);
            }
        }
        return next;
    }

    public static string InjectLibraryFolders(string xml, IEnumerable<string> destNames)
    {
        if (string.IsNullOrEmpty(xml) || destNames == null)
            return xml ?? "";
        var folders = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var dest in destNames)
        {
            var n = (dest ?? "").Replace('\\', '/').Trim('/');
            var slash = n.LastIndexOf('/');
            if (slash <= 0)
                continue;
            var folder = n[..slash];
            while (!string.IsNullOrEmpty(folder))
            {
                folders.Add(folder);
                var p = folder.LastIndexOf('/');
                folder = p <= 0 ? "" : folder[..p];
            }
        }
        xml = CollapseLibraryFolders(xml);
        if (folders.Count == 0)
            return xml;
        var block = new StringBuilder();
        block.Append("<folders>\n");
        foreach (var folder in folders.OrderBy(f => f.Count(c => c == '/')).ThenBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            block.Append("    <DOMFolderItem name=\"")
                .Append(folder.Replace("&", "&amp;").Replace("\"", "&quot;"))
                .Append("\" isExpanded=\"false\"/>\n");
        }
        block.Append("  </folders>\n  <symbols>");
        if (xml.IndexOf("<folders>", StringComparison.OrdinalIgnoreCase) >= 0)
            return xml;
        var withFolders = Regex.Replace(
            xml,
            @"<symbols>",
            block.ToString(),
            RegexOptions.IgnoreCase,
            TimeSpan.FromSeconds(2));
        return withFolders;
    }

    public static string CollapseLibraryFolders(string xml)
    {
        if (string.IsNullOrEmpty(xml))
            return xml ?? "";
        return Regex.Replace(
            xml,
            @"(<DOMFolderItem\b[^>]*\bisExpanded="")true("")",
            "$1false$2",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    /// <summary>
    /// Moves LIBRARY zip entries so file path matches the organized name, and
    /// Animate no longer lists leftover Symbol N items at the library root.
    /// </summary>
    public static int RelocateLibraryFiles(Dictionary<string, byte[]> entries, IReadOnlyDictionary<string, string> map)
    {
        if (entries == null || map == null || map.Count == 0)
            return 0;
        var moves = new List<(string OldKey, string NewKey)>();
        foreach (var key in entries.Keys)
        {
            if (!key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase))
                continue;
            var rest = key["LIBRARY/".Length..].Trim('/');
            if (string.IsNullOrEmpty(rest) || rest.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                IsAudioMedia(rest))
                continue;
            var dest = ResolveOrganizeDest(map, rest);
            if (dest.Equals(rest, StringComparison.Ordinal))
                continue;
            var neu = "LIBRARY/" + WithSourceExtension(rest, dest).Replace('\\', '/').Trim('/');
            if (neu.Equals(key, StringComparison.OrdinalIgnoreCase))
                continue;
            moves.Add((key, neu));
        }
        var n = 0;
        foreach (var (oldKey, newKey) in moves.OrderByDescending(m => m.OldKey.Length))
        {
            if (!entries.ContainsKey(oldKey) || entries.ContainsKey(newKey))
                continue;
            entries[newKey] = entries[oldKey];
            entries.Remove(oldKey);
            n++;
        }
        return n;
    }

    private static string RewriteMappedAttr(string xml, string attr, IReadOnlyDictionary<string, string> map)
    {
        return Regex.Replace(
            xml,
            @"\b" + attr + @"=""(?<n>[^""]+)""",
            m =>
            {
                var cur = m.Groups["n"].Value;
                if (cur.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) ||
                    cur.StartsWith("M ", StringComparison.Ordinal) ||
                    IsAudioMedia(cur))
                    return m.Value;
                var dest = ResolveOrganizeDest(map, cur);
                if (dest.Equals(cur, StringComparison.Ordinal))
                    return m.Value;
                return attr + "=\"" + WithSourceExtension(cur, dest) + "\"";
            },
            RegexOptions.CultureInvariant);
    }

    private static string WithSourceExtension(string source, string dest)
    {
        var ext = Path.GetExtension(source);
        if (string.IsNullOrEmpty(ext))
            return dest;
        if (dest.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            return dest;
        var destExt = Path.GetExtension(dest);
        if (!string.IsNullOrEmpty(destExt) &&
            !destExt.Equals(ext, StringComparison.OrdinalIgnoreCase))
            return dest[..^destExt.Length] + ext;
        if (dest.IndexOf('.', dest.LastIndexOf('/') + 1) >= 0)
            return dest;
        return dest + ext;
    }

    public static Result RenameHost(string flaPath)
    {
        var display = ReadDisplayName(flaPath);
        if (string.IsNullOrWhiteSpace(display))
            return Fail("No display name (tools/ssf2mm_displayName.txt or pack folder).");
        var entries = FlaZip.Read(flaPath);
        if (!TryDom(entries, out _))
            return Fail("DOMDocument.xml missing.");
        var oldName = FindHostLibraryName(entries, display);
        if (string.IsNullOrEmpty(oldName))
            return Fail("Could not find host clip (no stance instances).");
        if (oldName.Equals(display, StringComparison.Ordinal))
            return Ok("Host already named " + display + ".");
        foreach (var key in entries.Keys.ToList())
        {
            if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var xml = FlaZip.Utf8(entries[key]);
            var next = xml.Replace("libraryItemName=\"" + oldName + "\"", "libraryItemName=\"" + display + "\"");
            next = Regex.Replace(next, @"\bname=""" + Regex.Escape(oldName) + @"""", "name=\"" + display + "\"");
            if (!string.Equals(next, xml, StringComparison.Ordinal))
                entries[key] = FlaZip.Bytes(next);
        }
        var oldEntry = entries.Keys.FirstOrDefault(k =>
            k.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) &&
            (k.EndsWith("/" + Leaf(oldName) + ".xml", StringComparison.OrdinalIgnoreCase) ||
             k.Equals("LIBRARY/" + Leaf(oldName) + ".xml", StringComparison.OrdinalIgnoreCase)));
        if (oldEntry != null)
        {
            var slash = oldEntry.Replace('\\', '/').LastIndexOf('/');
            var neu = (slash >= 0 ? oldEntry[..(slash + 1)] : "LIBRARY/") + display + ".xml";
            if (!oldEntry.Equals(neu, StringComparison.OrdinalIgnoreCase))
            {
                entries[neu] = entries[oldEntry];
                entries.Remove(oldEntry);
            }
        }
        FlaZip.Write(flaPath, entries);
        return Ok("Host " + oldName + " → " + display + ".");
    }

    private static readonly Regex StanceInstanceNameRegex = new(
        @"<DOMSymbolInstance\b[^>]*\bname=""stance""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static string? FindHostLibraryName(Dictionary<string, byte[]> entries, string display)
    {
        string? bestName = null;
        var bestCount = 0;
        foreach (var kv in entries)
        {
            if (!kv.Key.StartsWith("LIBRARY/", StringComparison.OrdinalIgnoreCase) ||
                !kv.Key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var xml = FlaZip.Utf8(kv.Value);
            var named = LibraryItemOpenNameRegex.Match(xml);
            if (!named.Success ||
                !named.Groups["tag"].Value.Equals("DOMSymbolItem", StringComparison.OrdinalIgnoreCase))
                continue;
            var name = named.Groups["n"].Value;
            if (Leaf(name).Equals(display, StringComparison.OrdinalIgnoreCase))
                return name;
            var stances = StanceInstanceNameRegex.Matches(xml).Count;
            if (stances > bestCount)
            {
                bestCount = stances;
                bestName = name;
            }
        }
        return bestName;
    }

    public static Result PruneEmpty(string flaPath)
    {
        var n = EditTimelines(flaPath, (layers, _) =>
        {
            var removed = 0;
            var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (layers.Elements().Count(e => e.Name.LocalName == "DOMLayer") <= 1)
                    break;
                if (!IsEmptyPrunable(list[i]))
                    continue;
                var idx = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(list[i]);
                list[i].Remove();
                removed++;
                if (idx >= 0)
                    AdjustParent(layers, idx);
            }
            return removed;
        });
        return Ok("Removed " + n + " empty layer(s).");
    }

    public static Result NamesAndOutlines(string flaPath) =>
        NamesAndOutlines(flaPath, ReadDisplayName(flaPath));

    public static Result NamesAndOutlines(string flaPath, string displayName, bool characterPretty = true)
    {
        var art = string.IsNullOrWhiteSpace(displayName) ? "stance" : displayName.Trim();
        var n = EditTimelines(flaPath, (layers, isDoc) =>
        {
            RenameLayers(layers, art);
            // Character-only: peel/move hitbox leftovers and outline box layers.
            if (characterPretty)
            {
                RelocateBoxLayerContents(layers, art);
                PeelStrayBoxes(layers, art);
            }
            DropStackedUnnamedLeftovers(layers);
            ComplementaryMergeAdjacent(layers, art);
            TrimTrailingEmptyFrames(layers);
            if (characterPretty)
                Outline(layers, art);
            return 1;
        });
        return Ok("Renamed/outlined layers on " + n + " timeline(s).");
    }

    public static Result FoldersAndLock(string flaPath) =>
        FoldersAndLock(flaPath, ReadDisplayName(flaPath));

    public static Result FoldersAndLock(string flaPath, string displayName, bool characterPretty = true)
    {
        var art = string.IsNullOrWhiteSpace(displayName) ? "stance" : displayName.Trim();
        var n = EditTimelines(flaPath, (layers, isDoc) =>
        {
            ApplyFoldersAndLock(layers, art, isDoc, characterPretty);
            return 1;
        });
        CollapseFoldersInFla(flaPath);
        return Ok("Ground/Effects/lock applied on " + n + " timeline(s).");
    }

    public static Result Merge(string flaPath)
    {
        var r = FlaBoxLayerMerge.Run(flaPath);
        return new Result { Ok = r.Ok, Message = r.Message };
    }

    /// <summary>Merge box layers without writing a <c>.bak</c>. Display name is unused (merge is family-based).</summary>
    public static Result Merge(string flaPath, string displayName)
    {
        _ = displayName;
        var r = FlaBoxLayerMerge.Run(flaPath, writeBackup: false);
        return new Result { Ok = r.Ok, Message = r.Message };
    }

    /// <summary>
    /// Character clips only: host stance layers go in Ground; other clips get
    /// Ground/Effects folders. Scene 1 keeps its original stack (no flatten).
    /// Stages skip this — Ground/Effects/stance lock is character-specific.
    /// </summary>
    public static void ApplyFoldersAndLock(XElement layers, string art, bool isDoc, bool characterPretty = true)
    {
        if (isDoc)
        {
            SplitLabelsFromActions(layers);
            UnlockAll(layers);
            return;
        }
        if (!characterPretty)
            return;
        SplitLabelsFromActions(layers);
        if (IsHostClip(layers, art))
        {
            WrapHostGround(layers, art);
            RefreshFolderParents(layers);
            LockExceptArt(layers, art);
            return;
        }
        WrapGround(layers);
        WrapEffects(layers);
        RefreshFolderParents(layers);
        LockExceptArt(layers, art);
    }

    private static void UnlockAll(XElement layers)
    {
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
            layer.Attribute("locked")?.Remove();
    }

    public static int MergeBoxLayers(XElement layers) => FlaBoxLayerMerge.MergeLayers(layers);

    public static string ReadDisplayName(string flaPath)
    {
        var dir = Path.GetDirectoryName(flaPath) ?? "";
        var txt = Path.Combine(dir, "tools", "ssf2mm_displayName.txt");
        if (File.Exists(txt))
        {
            var t = File.ReadAllLines(txt)[0].Trim();
            if (!string.IsNullOrWhiteSpace(t))
                return t;
        }
        var pack = Path.GetDirectoryName(dir);
        if (!string.IsNullOrEmpty(pack))
        {
            var json = Path.Combine(pack, "character.json");
            if (File.Exists(json))
            {
                var m = Regex.Match(File.ReadAllText(json), "\"(?:DisplayName|displayName)\"\\s*:\\s*\"([^\"]+)\"");
                if (m.Success)
                    return m.Groups[1].Value.Trim();
            }
            var folder = Path.GetFileName(pack);
            if (!string.IsNullOrWhiteSpace(folder) && !folder.Equals("FLA", StringComparison.OrdinalIgnoreCase))
                return folder;
        }
        return Path.GetFileNameWithoutExtension(flaPath) ?? "";
    }

    private static Result Ok(string m) => new() { Ok = true, Message = m };
    private static Result Fail(string m) => new() { Ok = false, Message = m };


    private static readonly Regex LibraryItemOpenNameRegex = new(
        @"<(?<tag>DOMSymbolItem|DOMBitmapItem|DOMSoundItem|DOMFontItem|DOMVideoItem)\b(?<rest>[^>]*?)\bname=""(?<n>[^""]+)""",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static Dictionary<string, string> ParseOrganizeMap(string tsv)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(tsv))
            return map;
        foreach (var raw in tsv.Split('\n'))
        {
            var parts = raw.TrimEnd('\r').Split('\t');
            if (parts.Length < 2 || string.IsNullOrWhiteSpace(parts[1]))
                continue;
            var folder = parts[0].Trim().Replace('\\', '/').Trim('/');
            var item = parts[1].Trim().Replace('\\', '/');
            var srcLeaf = Leaf(item);
            var destLeaf = parts.Length >= 3 && !string.IsNullOrWhiteSpace(parts[2])
                ? Leaf(parts[2].Trim())
                : srcLeaf;
            destLeaf = KeepMediaExtension(srcLeaf, destLeaf);
            var dest = string.IsNullOrEmpty(folder) ? destLeaf : folder + "/" + destLeaf;
            // Sounds: rename the library item into Audio/, but keep href + wav
            // at LIBRARY/soundN.wav. Moving/rewriting sound href made Animate
            // drop every DOMSoundItem. Do not add the stem key (sound1) or
            // leftover .flv files collide with the wav dest.
            if (IsAudioMedia(srcLeaf) || IsAudioMedia(destLeaf) || IsAudioMedia(dest))
            {
                map[item] = dest;
                map[srcLeaf] = dest;
                continue;
            }
            AddOrganizeKey(map, item, dest);
            AddOrganizeKey(map, srcLeaf, dest);
        }
        return map;
    }

    private static void AddOrganizeKey(Dictionary<string, string> map, string key, string dest)
    {
        if (string.IsNullOrEmpty(key))
            return;
        map[key] = dest;
        var leaf = Leaf(key);
        map[leaf] = dest;
        var stem = Path.GetFileNameWithoutExtension(leaf);
        if (!string.IsNullOrEmpty(stem))
            map[stem] = dest;
    }

    private static bool IsAudioMedia(string path)
    {
        var ext = Path.GetExtension(path ?? "");
        return ext.Equals(".wav", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mp3", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".flv", StringComparison.OrdinalIgnoreCase) ||
               ext.Equals(".mpa", StringComparison.OrdinalIgnoreCase);
    }

    public static string KeepMediaExtension(string sourceLeaf, string destLeaf)
    {
        var srcExt = Path.GetExtension(sourceLeaf);
        if (string.IsNullOrEmpty(srcExt))
            return destLeaf;
        var destName = Leaf(destLeaf);
        if (!string.IsNullOrEmpty(Path.GetExtension(destName)))
            return destLeaf;
        return destLeaf + srcExt;
    }

    private static string ResolveOrganizeDest(IReadOnlyDictionary<string, string> map, string current)
    {
        if (map.TryGetValue(current, out var dest))
            return dest;
        var leaf = Leaf(current);
        if (map.TryGetValue(leaf, out dest))
            return dest;
        var stem = Path.GetFileNameWithoutExtension(leaf);
        if (!string.IsNullOrEmpty(stem) && map.TryGetValue(stem, out dest))
        {
            var ext = Path.GetExtension(leaf);
            if (!string.IsNullOrEmpty(ext) && dest.IndexOf('.', dest.LastIndexOf('/') + 1) < 0)
                return dest + ext;
            return dest;
        }
        return current;
    }

    private static bool TryDom(Dictionary<string, byte[]> entries, out string xml)
    {
        foreach (var key in new[] { "DOMDocument.xml", "DOMDocument.xml" })
        {
            if (entries.TryGetValue(key, out var bytes))
            {
                xml = FlaZip.Utf8(bytes);
                return true;
            }
        }
        xml = "";
        return false;
    }

    private static int EditTimelines(string flaPath, Func<XElement, bool, int> edit)
    {
        var entries = FlaZip.Read(flaPath);
        var total = 0;
        foreach (var key in entries.Keys.ToList())
        {
            if (!key.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                continue;
            var xml = FlaZip.Utf8(entries[key]);
            if (xml.IndexOf("DOMLayer", StringComparison.Ordinal) < 0)
                continue;
            var doc = FlaZip.Parse(xml);
            if (doc == null)
                continue;
            var isDoc = doc.Root?.Name.LocalName == "DOMDocument";
            var n = 0;
            foreach (var layers in doc.Descendants().Where(e => e.Name.LocalName == "layers").ToList())
                n += edit(layers, isDoc);
            if (n == 0)
                continue;
            entries[key] = FlaZip.Bytes(FlaZip.Save(doc));
            total += n;
        }
        FlaZip.Write(flaPath, entries);
        return total;
    }

    public static void RenameLayers(XElement layers, string art)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var artLayer = FindStanceLayer(layers) ?? PickArtLayer(layers);
        if (artLayer != null)
        {
            artLayer.SetAttributeValue("name", art);
            used.Add(art);
        }
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (ReferenceEquals(layer, artLayer))
                continue;
            var name = (string?)layer.Attribute("name") ?? "";
            if (IsScript(name))
            {
                layer.SetAttributeValue("name", "Actions");
                used.Add("Actions");
                continue;
            }
            if (name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
            {
                layer.SetAttributeValue("name", "Labels Layer");
                used.Add("Labels Layer");
                continue;
            }
            var inst = FirstInstance(layer);
            var nameFam = FlaBoxLayerMerge.MapFamily(name);
            var fam = FlaBoxLayerMerge.MapFamily(inst) ?? nameFam;
            string want;
            if (nameFam != null && LayerHasBoxInstance(layer))
            {
                // Keep Item/HitBox/AttackBox so Relocate can peel leftover art
                // off the family layer instead of renaming it to FalseSora N.
                want = nameFam;
            }
            else if (fam != null && LayerHasBoxInstance(layer) && LayerHasNonBoxContent(layer))
            {
                var nonBox = FirstNamedNonBox(layer);
                if (!string.IsNullOrEmpty(nonBox))
                    want = Unique(used, nonBox);
                else if (LayerHasArtGraphics(layer))
                    want = Unique(used, art);
                else
                {
                    used.Add(name);
                    continue;
                }
            }
            else if (fam != null && !LayerLooksLikeCharacterArt(layer))
                want = fam;
            else if (LayerHasArtGraphics(layer))
                want = Unique(used, art);
            else if (!string.IsNullOrEmpty(inst) && FlaBoxLayerMerge.MapFamily(inst) == null)
                want = Unique(used, inst);
            else
            {
                var leaf = FirstClipLeaf(layer);
                if (!string.IsNullOrEmpty(leaf) && !Regex.IsMatch(leaf, @"^Symbol\s*\d+$", RegexOptions.IgnoreCase))
                    want = Unique(used, leaf);
                else if (LayerHasArtGraphics(layer))
                    want = Unique(used, art);
                else
                {
                    used.Add(name);
                    continue;
                }
            }
            layer.SetAttributeValue("name", want);
            used.Add(want);
        }
    }

    /// <summary>
    /// Character-only: peel bitmaps and shapes off box-named layers onto a
    /// sibling art layer (FalseSora / FalseSora 2) so charge FX and body
    /// sprites are not locked inside HitBox/Item.
    /// </summary>
    public static void RelocateBoxLayerContents(XElement layers, string art)
    {
        if (layers == null || string.IsNullOrWhiteSpace(art))
            return;
        var ns = layers.Name.Namespace;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList())
        {
            var name = (string?)layer.Attribute("name") ?? "";
            if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                continue;
            if (FlaBoxLayerMerge.MapFamily(name) == null)
                continue;
            XElement? dest = null;
            foreach (var frame in LayerFrames(layer).ToList())
            {
                if (HasTween(frame))
                    continue;
                var elements = FrameElements(frame);
                if (elements == null)
                    continue;
                var graphics = elements.Elements().Where(IsGraphicElement).ToList();
                if (graphics.Count == 0)
                    continue;
                dest ??= InsertArtSiblingLayer(layers, layer, art, ns);
                if (PlaceElementsAt(dest, FrameIndex(frame), FrameDuration(frame), graphics, ns,
                        createIfMissing: true, fillEmpty: true, addToFilled: true))
                    foreach (var el in graphics) el.Remove();
            }
        }
    }

    private static bool IsGraphicElement(XElement el) =>
        el.Name.LocalName is "DOMBitmapInstance" or "DOMShape" or "DOMStaticText" or "DOMDynamicText";

    private static XElement InsertArtSiblingLayer(XElement layers, XElement after, string art, XNamespace ns)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
            used.Add((string?)layer.Attribute("name") ?? "");
        var dest = new XElement(ns + "DOMLayer",
            new XAttribute("name", Unique(used, art)),
            new XElement(ns + "frames"));
        after.AddAfterSelf(dest);
        return dest;
    }

    /// <summary>
    /// Move box-named clips off non-box layers (including the display-name
    /// art layer) onto a family layer so item/hit/attack stay on their own layer.
    /// </summary>
    public static void PeelStrayBoxes(XElement layers, string art)
    {
        if (layers == null)
            return;
        var ns = layers.Name.Namespace;
        var peel = new Dictionary<string, XElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList())
        {
            var name = (string?)layer.Attribute("name") ?? "";
            if (IsProtectedLayer(layer) || IsScript(name) ||
                name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                continue;
            if (FlaBoxLayerMerge.MapFamily(name) != null)
                continue;
            foreach (var frame in LayerFrames(layer).ToList())
            {
                if (HasTween(frame))
                    continue;
                var elements = FrameElements(frame);
                if (elements == null)
                    continue;
                var byFamily = new Dictionary<string, List<XElement>>(StringComparer.OrdinalIgnoreCase);
                foreach (var el in elements.Elements().ToList())
                {
                    if (el.Name.LocalName != "DOMSymbolInstance")
                        continue;
                    var fam = FlaBoxLayerMerge.MapFamily((string?)el.Attribute("name"));
                    if (fam == null)
                        continue;
                    if (!byFamily.TryGetValue(fam, out var list))
                    {
                        list = new List<XElement>();
                        byFamily[fam] = list;
                    }
                    list.Add(el);
                }
                if (byFamily.Count == 0)
                    continue;
                var index = FrameIndex(frame);
                var duration = FrameDuration(frame);
                foreach (var kv in byFamily)
                {
                    if (!peel.TryGetValue(kv.Key, out var dest))
                    {
                        dest = layers.Elements().FirstOrDefault(l =>
                                   l.Name.LocalName == "DOMLayer" &&
                                   string.Equals((string?)l.Attribute("name"), kv.Key,
                                       StringComparison.OrdinalIgnoreCase))
                               ?? InsertBoxFamilyLayer(layers, kv.Key);
                        peel[kv.Key] = dest;
                    }
                    if (PlaceElementsAt(dest, index, duration, kv.Value, ns,
                            createIfMissing: true, fillEmpty: true, addToFilled: true))
                        foreach (var el in kv.Value) el.Remove();
                }
            }
        }
    }

    /// <summary>
    /// Unnamed-only leftover layers: drop an instance only when another non-box
    /// layer already has the same library item on overlapping frames. Unique
    /// overlays stay. Empty leftover layers are deleted.
    /// </summary>
    public static void DropStackedUnnamedLeftovers(XElement layers)
    {
        if (layers == null)
            return;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList())
        {
            var name = (string?)layer.Attribute("name") ?? "";
            if (IsProtectedLayer(layer) || IsScript(name) ||
                name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                continue;
            if (FlaBoxLayerMerge.MapFamily(name) != null)
                continue;
            if (!IsUnnamedOnlyLayer(layer))
                continue;
            foreach (var frame in LayerFrames(layer).ToList())
            {
                var els = FrameElements(frame);
                if (els == null)
                    continue;
                var start = FrameIndex(frame);
                var dur = FrameDuration(frame);
                foreach (var el in els.Elements().ToList())
                {
                    if (el.Name.LocalName != "DOMSymbolInstance")
                        continue;
                    var lib = (string?)el.Attribute("libraryItemName") ?? "";
                    if (string.IsNullOrEmpty(lib))
                        continue;
                    if (OtherNonBoxHasSamePlacement(layers, layer, start, dur, el))
                        el.Remove();
                }
            }
            if (!layer.Descendants().Any(e =>
                    e.Name.LocalName is "DOMSymbolInstance" or "DOMBitmapInstance"
                        or "DOMStaticText" or "DOMDynamicText"))
            {
                var idx = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(layer);
                layer.Remove();
                if (idx >= 0)
                    AdjustParent(layers, idx);
            }
        }
    }

    private static bool IsUnnamedOnlyLayer(XElement layer)
    {
        var any = false;
        foreach (var el in layer.Descendants())
        {
            var tag = el.Name.LocalName;
            if (tag is "DOMBitmapInstance" or "DOMStaticText" or "DOMDynamicText")
                return false;
            if (tag != "DOMSymbolInstance")
                continue;
            any = true;
            if (!string.IsNullOrEmpty((string?)el.Attribute("name")))
                return false;
        }
        return any;
    }

    private static bool OtherNonBoxHasSamePlacement(XElement layers, XElement self, int start, int dur, XElement inst)
    {
        var end = start + Math.Max(1, dur);
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (ReferenceEquals(layer, self))
                continue;
            var name = (string?)layer.Attribute("name") ?? "";
            if (IsProtectedLayer(layer) || IsScript(name) ||
                name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                continue;
            if (FlaBoxLayerMerge.MapFamily(name) != null)
                continue;
            foreach (var frame in LayerFrames(layer))
            {
                var i = FrameIndex(frame);
                var frameEnd = i + FrameDuration(frame);
                if (i >= end || start >= frameEnd)
                    continue;
                if (FrameElements(frame)?.Elements().Any(el => FlaBoxLayerMerge.SameGraphic(el, inst)) == true)
                    return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Drop empty keys/holds that sit after the last occupied frame on the
    /// timeline. Empty keys in the middle are kept. Occupied = instance,
    /// shape, text, script, sound, or frame label.
    /// </summary>
    public static void TrimTrailingEmptyFrames(XElement layers)
    {
        if (layers == null)
            return;
        var last = 0;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var frame in LayerFrames(layer))
            {
                if (!FrameIsOccupied(frame))
                    continue;
                last = Math.Max(last, FrameIndex(frame) + FrameDuration(frame));
            }
        }
        if (last <= 0)
            return;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var frame in LayerFrames(layer).ToList())
            {
                var i = FrameIndex(frame);
                var d = FrameDuration(frame);
                if (i >= last)
                {
                    frame.Remove();
                    continue;
                }
                if (i + d <= last)
                    continue;
                var span = last - i;
                if (span <= 1)
                    frame.Attribute("duration")?.Remove();
                else
                    frame.SetAttributeValue("duration", span.ToString(CultureInfo.InvariantCulture));
            }
        }
    }

    private static bool FrameIsOccupied(XElement frame)
    {
        if (!string.IsNullOrEmpty((string?)frame.Attribute("name")))
            return true;
        if (!string.IsNullOrEmpty((string?)frame.Attribute("soundName")))
            return true;
        if (HasTween(frame))
            return true;
        if (frame.Descendants().Any(e => e.Name.LocalName == "script"))
            return true;
        return FrameHasArt(frame);
    }

    /// <summary>
    /// Merge adjacent leftover layers whose filled frames do not overlap.
    /// Sparse overlay goes onto the denser layer. Box/art/mask layers are skipped.
    /// </summary>
    public static void ComplementaryMergeAdjacent(XElement layers, string art)
    {
        if (layers == null)
            return;
        var ns = layers.Name.Namespace;
        var guard = 0;
        while (guard++ < 500)
        {
            var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
            var merged = false;
            for (var i = 0; i < list.Count - 1; i++)
            {
                var a = list[i];
                var b = list[i + 1];
                if (!CanComplementaryMerge(a, art) || !CanComplementaryMerge(b, art))
                    continue;
                var fa = FlaBoxLayerMerge.MapFamily((string?)a.Attribute("name"));
                var fb = FlaBoxLayerMerge.MapFamily((string?)b.Attribute("name"));
                if (!string.Equals(fa, fb, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (LayerHasTween(a) || LayerHasTween(b))
                    continue;
                var filledA = FilledSpan(a);
                var filledB = FilledSpan(b);
                if (filledA == 0 && filledB == 0)
                    continue;
                if (FilledRangesOverlap(a, b))
                    continue;
                var dest = filledA >= filledB ? a : b;
                var src = ReferenceEquals(dest, a) ? b : a;
                OverlayComplementary(dest, src, ns);
                var srcAt = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(src);
                src.Remove();
                if (srcAt >= 0)
                    AdjustParent(layers, srcAt);
                merged = true;
                break;
            }
            if (!merged)
                break;
        }
    }

    private static bool CanComplementaryMerge(XElement layer, string art)
    {
        var name = (string?)layer.Attribute("name") ?? "";
        if (IsArtLayerName(name, art))
            return false;
        if (IsProtectedLayer(layer) || IsScript(name) ||
            name.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
            return false;
        var fam = FlaBoxLayerMerge.MapFamily(name);
        if (fam == null)
            return true;
        return fam.Equals("AttackBox", StringComparison.OrdinalIgnoreCase) ||
               fam.Equals("HandBox", StringComparison.OrdinalIgnoreCase);
    }

    private static bool LayerHasTween(XElement layer) =>
        LayerFrames(layer).Any(HasTween);

    private static int FilledSpan(XElement layer)
    {
        var n = 0;
        foreach (var frame in LayerFrames(layer))
        {
            if (!FrameHasArt(frame))
                continue;
            n += FrameDuration(frame);
        }
        return n;
    }

    private static bool FilledRangesOverlap(XElement a, XElement b)
    {
        var occupied = new HashSet<int>();
        foreach (var frame in LayerFrames(a))
        {
            if (!FrameHasArt(frame))
                continue;
            var i = FrameIndex(frame);
            var end = i + FrameDuration(frame);
            for (var f = i; f < end; f++)
                occupied.Add(f);
        }
        foreach (var frame in LayerFrames(b))
        {
            if (!FrameHasArt(frame))
                continue;
            var i = FrameIndex(frame);
            var end = i + FrameDuration(frame);
            for (var f = i; f < end; f++)
            {
                if (occupied.Contains(f))
                    return true;
            }
        }
        return false;
    }

    private static void OverlayComplementary(XElement dest, XElement src, XNamespace ns)
    {
        foreach (var frame in LayerFrames(src).ToList())
        {
            if (HasTween(frame) || !FrameHasArt(frame))
                continue;
            var elements = FrameElements(frame);
            if (elements == null)
                continue;
            var moved = elements.Elements().ToList();
            if (moved.Count == 0)
                continue;
            PlaceElementsAt(dest, FrameIndex(frame), FrameDuration(frame), moved, ns,
                createIfMissing: true, fillEmpty: true, addToFilled: false);
        }
    }

    private static XElement InsertBoxFamilyLayer(XElement layers, string family)
    {
        var ns = layers.Name.Namespace;
        var neu = new XElement(ns + "DOMLayer",
            new XAttribute("name", family),
            new XElement(ns + "frames"));
        var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
        var insertAt = list.FindIndex(l =>
            string.Equals(FlaBoxLayerMerge.MapFamily((string?)l.Attribute("name")), family, StringComparison.OrdinalIgnoreCase));
        if (insertAt < 0)
            insertAt = list.FindIndex(l => FlaBoxLayerMerge.MapFamily((string?)l.Attribute("name")) != null);
        if (insertAt < 0)
            insertAt = 0;
        if (insertAt >= list.Count)
            layers.Add(neu);
        else
            list[insertAt].AddBeforeSelf(neu);
        var at = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(neu);
        if (at >= 0)
        {
            foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
            {
                var raw = (string?)layer.Attribute("parentLayerIndex");
                if (!int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parent) ||
                    parent < at)
                    continue;
                layer.SetAttributeValue("parentLayerIndex", parent + 1);
            }
        }
        return neu;
    }

    private static bool PlaceElementsAt(XElement destLayer, int index, int duration, List<XElement> moved,
        XNamespace ns, bool createIfMissing, bool fillEmpty, bool addToFilled, bool prepend = false)
    {
        if (moved.Count == 0)
            return false;
        var framesEl = destLayer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (framesEl == null)
        {
            if (!createIfMissing)
                return false;
            framesEl = new XElement(ns + "frames");
            destLayer.Add(framesEl);
        }
        var exact = FindFrameAt(framesEl, index);
        var cover = FindCoveringFrame(framesEl, index);
        XElement? dest = null;
        if (exact != null)
        {
            if (HasTween(exact))
                return false;
            var filled = FrameHasInstances(exact);
            if (filled && !addToFilled)
                return false;
            if (!filled && !fillEmpty)
                return false;
            dest = exact;
        }
        else if (cover != null)
        {
            if (HasTween(cover))
                return false;
            var filled = FrameHasInstances(cover);
            if (filled && !addToFilled && !fillEmpty)
                return false;
            if (!filled && !fillEmpty)
                return false;
            SplitFrameAt(cover, index);
            dest = FindFrameAt(framesEl, index);
            if (dest == null)
                return false;
            if (FrameHasInstances(dest) && !addToFilled)
                return false;
            if (!FrameHasInstances(dest) && !fillEmpty)
                return false;
        }
        else
        {
            if (!createIfMissing)
                return false;
            var gap = GapToNextFrame(framesEl, index);
            var dur = Math.Max(1, duration);
            if (gap < int.MaxValue)
                dur = Math.Min(dur, gap);
            dest = new XElement(ns + "DOMFrame",
                new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("duration", dur.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("keyMode", "9728"));
            InsertFrameSorted(framesEl, dest);
        }
        var span = Math.Max(1, duration);
        span = Math.Min(span, FrameDuration(dest));
        var next = NextFrame(framesEl, index);
        if (next != null)
            span = Math.Min(span, FrameIndex(next) - index);
        if (span < 1)
            span = 1;
        if (FrameDuration(dest) > span)
            SplitFrameAt(dest, index + span);
        var els = FrameElements(dest) ?? NewElements(dest, ns);
        if (prepend)
        {
            for (var i = moved.Count - 1; i >= 0; i--)
                els.AddFirst(new XElement(moved[i]));
        }
        else
        {
            foreach (var el in moved)
                els.Add(new XElement(el));
        }
        return true;
    }

    private static XElement? NextFrame(XElement framesEl, int index)
    {
        XElement? next = null;
        var best = int.MaxValue;
        foreach (var f in framesEl.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            var i = FrameIndex(f);
            if (i > index && i < best)
            {
                best = i;
                next = f;
            }
        }
        return next;
    }

    private static int GapToNextFrame(XElement framesEl, int index)
    {
        var next = NextFrame(framesEl, index);
        return next == null ? int.MaxValue : Math.Max(1, FrameIndex(next) - index);
    }

    private static void InsertFrameSorted(XElement framesEl, XElement neu)
    {
        var idx = FrameIndex(neu);
        XElement? after = null;
        foreach (var f in framesEl.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            if (FrameIndex(f) < idx)
                after = f;
            else
                break;
        }
        if (after == null)
            framesEl.AddFirst(neu);
        else
            after.AddAfterSelf(neu);
    }

    private static XElement? FindContentLayer(XElement layers, string name) =>
        layers.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DOMLayer" &&
            string.Equals((string?)e.Attribute("name"), name, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals((string?)e.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase));

    private static IEnumerable<XElement> LayerFrames(XElement layer)
    {
        var frames = layer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (frames == null)
            yield break;
        foreach (var f in frames.Elements().Where(e => e.Name.LocalName == "DOMFrame"))
            yield return f;
    }

    private static XElement? FrameElements(XElement frame) =>
        frame.Elements().FirstOrDefault(e => e.Name.LocalName == "elements");

    private static int FrameIndex(XElement frame) =>
        int.TryParse((string?)frame.Attribute("index"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : 0;

    private static int FrameDuration(XElement frame) =>
        int.TryParse((string?)frame.Attribute("duration"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) && n > 0 ? n : 1;

    private static bool HasTween(XElement frame)
    {
        var tw = (string?)frame.Attribute("tweenType");
        return !string.IsNullOrEmpty(tw) && !tw.Equals("none", StringComparison.OrdinalIgnoreCase);
    }

    private static bool PlaceOnFilledArtKey(XElement destLayer, int index, List<XElement> moved, XNamespace ns)
    {
        if (moved.Count == 0)
            return false;
        var framesEl = destLayer.Elements().FirstOrDefault(e => e.Name.LocalName == "frames");
        if (framesEl == null)
            return false;
        var exact = FindFrameAt(framesEl, index);
        var cover = FindCoveringFrame(framesEl, index);
        XElement? dest = null;
        if (exact != null)
        {
            if (HasTween(exact) || !FrameHasInstances(exact))
                return false;
            dest = exact;
        }
        else if (cover != null)
        {
            if (HasTween(cover) || !FrameHasInstances(cover))
                return false;
            SplitFrameAt(cover, index);
            dest = FindFrameAt(framesEl, index);
            if (dest == null)
                return false;
            FrameElements(dest)?.Elements().Remove();
        }
        else
            return false;
        var els = FrameElements(dest) ?? NewElements(dest, ns);
        var placed = false;
        foreach (var el in moved)
        {
            if (els.Elements().Any(e => FlaBoxLayerMerge.SamePlacement(e, el)))
                continue;
            els.Add(new XElement(el));
            placed = true;
        }
        return placed;
    }

    private static bool FrameHasInstances(XElement frame) =>
        FrameElements(frame)?.Elements().Any(e =>
            e.Name.LocalName is "DOMBitmapInstance" or "DOMSymbolInstance" or "DOMShape") == true;

    private static XElement? FindFrameAt(XElement framesEl, int index) =>
        framesEl.Elements().FirstOrDefault(f =>
            f.Name.LocalName == "DOMFrame" && FrameIndex(f) == index);

    private static XElement? FindCoveringFrame(XElement framesEl, int index) =>
        framesEl.Elements().FirstOrDefault(f =>
        {
            if (f.Name.LocalName != "DOMFrame")
                return false;
            var i = FrameIndex(f);
            return i < index && i + FrameDuration(f) > index;
        });

    private static XElement NewElements(XElement frame, XNamespace ns)
    {
        var els = new XElement(ns + "elements");
        frame.Add(els);
        return els;
    }

    private static void SplitFrameAt(XElement frame, int at)
    {
        var start = FrameIndex(frame);
        var dur = FrameDuration(frame);
        if (at <= start || at >= start + dur)
            return;
        var tail = new XElement(frame);
        frame.SetAttributeValue("duration", (at - start).ToString(CultureInfo.InvariantCulture));
        tail.SetAttributeValue("index", at.ToString(CultureInfo.InvariantCulture));
        tail.SetAttributeValue("duration", (start + dur - at).ToString(CultureInfo.InvariantCulture));
        frame.AddAfterSelf(tail);
    }

    /// <summary>
    /// Animate needs both outline="true" and useOutlineView="true" (see GUIDE-animate-truth.fla).
    /// </summary>
    public static void Outline(XElement layers, string art)
    {
        if (layers == null)
            return;
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                continue;
            var layerName = (string?)layer.Attribute("name") ?? "";
            if (layerName.Equals(art, StringComparison.OrdinalIgnoreCase))
            {
                layer.Attribute("outline")?.Remove();
                layer.Attribute("useOutlineView")?.Remove();
                continue;
            }
            if (!IsOutlineBoxLayer(layer))
            {
                layer.Attribute("outline")?.Remove();
                layer.Attribute("useOutlineView")?.Remove();
                continue;
            }
            SetOutlined(layer, true);
        }
    }

    private static bool IsOutlineBoxLayer(XElement layer)
    {
        var name = (string?)layer.Attribute("name") ?? "";
        if (IsItemBoxLabel(name))
            return false;
        var fam = FlaBoxLayerMerge.MapFamily(name);
        if (fam != null && !fam.Equals("Item", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance"))
        {
            var instName = (string?)inst.Attribute("name") ?? "";
            if (IsItemBoxLabel(instName))
                continue;
            fam = FlaBoxLayerMerge.MapFamily(instName);
            if (fam != null && !fam.Equals("Item", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void SetOutlined(XElement layer, bool on)
    {
        if (on)
        {
            layer.SetAttributeValue("outline", "true");
            layer.SetAttributeValue("useOutlineView", "true");
        }
        else
        {
            layer.Attribute("outline")?.Remove();
            layer.Attribute("useOutlineView")?.Remove();
        }
    }

    private static bool IsItemBoxLabel(string name)
    {
        var n = Regex.Replace(name ?? "", @"[\s*_]+", "", RegexOptions.CultureInvariant);
        return n.Equals("item", StringComparison.OrdinalIgnoreCase) ||
               n.IndexOf("itembox", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void CollapseFoldersInFla(string flaPath)
    {
        var entries = FlaZip.Read(flaPath);
        if (!TryDom(entries, out var xml))
            return;
        var next = CollapseLibraryFolders(xml);
        if (string.Equals(next, xml, StringComparison.Ordinal))
            return;
        foreach (var key in entries.Keys.ToList())
        {
            if (key.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase) ||
                key.Equals("DOMDocument.xml", StringComparison.OrdinalIgnoreCase))
            {
                entries[key] = FlaZip.Bytes(next);
                break;
            }
        }
        FlaZip.Write(flaPath, entries);
    }

    /// <summary>
    /// Only the character library item (name == display name) is the host.
    /// Projectile/revival/attack clips often contain a nested instance named
    /// <c>stance</c>; treating those as host outlines their VFX as Ground.
    /// Bare test timelines with no parent symbol still use the stance instance.
    /// </summary>
    private static bool IsHostClip(XElement layers, string art)
    {
        var clipName = ClipLibraryName(layers);
        if (!string.IsNullOrEmpty(clipName))
            return clipName.Equals(art, StringComparison.OrdinalIgnoreCase);
        return layers.Descendants().Any(e =>
            e.Name.LocalName == "DOMSymbolInstance" &&
            string.Equals((string?)e.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase));
    }

    private static string? ClipLibraryName(XElement layers)
    {
        for (var p = layers.Parent; p != null; p = p.Parent)
        {
            var n = p.Name.LocalName;
            if (n is "DOMSymbolItem" or "DOMDocument")
                return (string?)p.Attribute("name");
        }
        return null;
    }

    private static void FlattenScene1(XElement layers, string art)
    {
        var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
        var dest = FindContentLayer(layers, art) ?? FindStanceLayer(layers) ?? PickArtLayer(layers)
            ?? list.FirstOrDefault(l =>
                !string.Equals((string?)l.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase));
        if (dest == null)
            return;
        dest.SetAttributeValue("name", art);
        dest.Attribute("locked")?.Remove();
        dest.Attribute("parentLayerIndex")?.Remove();
        dest.Attribute("layerType")?.Remove();
        SetOutlined(dest, false);
        var ns = layers.Name.Namespace;
        var destAt = list.IndexOf(dest);
        for (var i = destAt + 1; i < list.Count; i++)
            OverlayScene1Layer(dest, list[i], ns, prepend: true);
        for (var i = destAt - 1; i >= 0; i--)
            OverlayScene1Layer(dest, list[i], ns, prepend: false);
        foreach (var layer in list)
        {
            if (!ReferenceEquals(layer, dest))
                layer.Remove();
        }
        dest.Remove();
        layers.AddFirst(dest);
    }

    private static void OverlayScene1Layer(XElement dest, XElement src, XNamespace ns, bool prepend)
    {
        if (string.Equals((string?)src.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
            return;
        foreach (var frame in LayerFrames(src).ToList())
        {
            var elements = FrameElements(frame);
            if (elements == null)
                continue;
            var moved = elements.Elements().ToList();
            if (moved.Count == 0)
                continue;
            PlaceElementsAt(dest, FrameIndex(frame), FrameDuration(frame), moved, ns,
                createIfMissing: true, fillEmpty: true, addToFilled: true, prepend);
        }
    }

    private static void SplitLabelsFromActions(XElement layers)
    {
        var actions = layers.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DOMLayer" &&
            string.Equals((string?)e.Attribute("name"), "Actions", StringComparison.OrdinalIgnoreCase));
        if (actions == null)
            return;
        if (layers.Elements().Any(e =>
            e.Name.LocalName == "DOMLayer" &&
            ((string?)e.Attribute("name") ?? "").StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase)))
            return;
        var labeled = LayerFrames(actions)
            .Where(f => !string.IsNullOrEmpty((string?)f.Attribute("name")))
            .ToList();
        if (labeled.Count == 0)
            return;
        var ns = layers.Name.Namespace;
        var marks = labeled
            .Select(f => (Frame: f, Index: FrameIndex(f), Name: (string?)f.Attribute("name") ?? ""))
            .Where(m => m.Name.Length > 0)
            .OrderBy(m => m.Index)
            .ToList();
        var end = LayerFrames(actions).Select(f => FrameIndex(f) + FrameDuration(f)).DefaultIfEmpty(0).Max();
        var framesEl = new XElement(ns + "frames");
        var cursor = 0;
        for (var i = 0; i < marks.Count; i++)
        {
            if (marks[i].Index < cursor)
                continue;
            if (marks[i].Index > cursor)
                framesEl.Add(LabelHold(ns, cursor, marks[i].Index - cursor, null));
            var next = i + 1 < marks.Count ? marks[i + 1].Index : Math.Max(end, marks[i].Index + 1);
            var span = Math.Max(1, next - marks[i].Index);
            framesEl.Add(LabelHold(ns, marks[i].Index, span, marks[i].Name));
            cursor = marks[i].Index + span;
            marks[i].Frame.Attribute("name")?.Remove();
            marks[i].Frame.Attribute("labelType")?.Remove();
            marks[i].Frame.Attribute("bookmark")?.Remove();
        }
        var labels = new XElement(ns + "DOMLayer",
            new XAttribute("name", "Labels Layer"),
            framesEl);
        actions.AddBeforeSelf(labels);
    }

    /// <summary>
    /// A contiguous Labels-layer key. Animate ignores a later <c>index</c> when the
    /// previous key does not cover the gap, so labels pile onto one frame.
    /// </summary>
    private static XElement LabelHold(XNamespace ns, int index, int duration, string? name)
    {
        var frame = new XElement(ns + "DOMFrame",
            new XAttribute("index", index.ToString(CultureInfo.InvariantCulture)),
            new XAttribute("keyMode", "9728"));
        if (duration > 1)
            frame.SetAttributeValue("duration", duration.ToString(CultureInfo.InvariantCulture));
        if (!string.IsNullOrEmpty(name))
        {
            frame.SetAttributeValue("name", name);
            frame.SetAttributeValue("labelType", "name");
        }
        frame.Add(new XElement(ns + "elements"));
        return frame;
    }

    private static void WrapHostGround(XElement layers, string art)
    {
        var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
        var artLayer = FindContentLayer(layers, art) ?? FindStanceLayer(layers);
        if (artLayer == null)
            return;
        var artAt = list.IndexOf(artLayer);
        if (artAt < 0)
            return;
        var ground = new List<XElement>();
        for (var i = artAt + 1; i < list.Count; i++)
        {
            var layer = list[i];
            if (string.Equals((string?)layer.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                break;
            var n = (string?)layer.Attribute("name") ?? "";
            if (IsScript(n) || n.StartsWith("Labels Layer", StringComparison.OrdinalIgnoreCase))
                continue;
            ground.Add(layer);
        }
        if (ground.Count == 0)
            return;
        foreach (var layer in ground)
            SetOutlined(layer, true);
        ParkInFolder(layers, "Ground", ground, renamePrefix: null, lockFolder: true);
    }

    private static void WrapGround(XElement layers)
    {
        var ground = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").Where(l =>
        {
            if (string.Equals((string?)l.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                return false;
            var n = (string?)l.Attribute("name") ?? "";
            if (n.StartsWith("reference ", StringComparison.OrdinalIgnoreCase))
                return true;
            return n.IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (FirstInstance(l) ?? "").IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   (FirstClipLeaf(l) ?? "").IndexOf("ground", StringComparison.OrdinalIgnoreCase) >= 0;
        }).ToList();
        ParkInFolder(layers, "Ground", ground, "reference", lockFolder: true);
    }

    private static void WrapEffects(XElement layers)
    {
        var effects = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").Where(l =>
        {
            var n = (string?)l.Attribute("name") ?? "";
            if (IsProtectedLayer(l))
                return false;
            return n.StartsWith("Effect", StringComparison.OrdinalIgnoreCase) ||
                   n.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0;
        }).ToList();
        ParkInFolder(layers, "Effects", effects, renamePrefix: null, lockFolder: true);
    }

    private static bool IsProtectedLayer(XElement layer)
    {
        var type = ((string?)layer.Attribute("layerType") ?? "").ToLowerInvariant();
        if (type is "mask" or "masked" or "guide" or "guided" or "folder")
            return true;
        return !string.IsNullOrEmpty((string?)layer.Attribute("parentLayerIndex"));
    }

    private static void ParkInFolder(XElement layers, string folderName, List<XElement> children, string? renamePrefix, bool lockFolder)
    {
        if (children.Count == 0)
            return;
        var ns = layers.Name.Namespace;
        var folder = layers.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DOMLayer" &&
            string.Equals((string?)e.Attribute("name"), folderName, StringComparison.OrdinalIgnoreCase) &&
            string.Equals((string?)e.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase));
        if (folder == null)
        {
            folder = new XElement(ns + "DOMLayer",
                new XAttribute("name", folderName),
                new XAttribute("layerType", "folder"),
                new XAttribute("autoNamed", "false"));
            if (folderName.Equals("Ground", StringComparison.OrdinalIgnoreCase))
                folder.SetAttributeValue("open", "false");
            children[0].AddBeforeSelf(folder);
        }
        for (var i = children.Count - 1; i >= 0; i--)
        {
            children[i].Remove();
            folder.AddAfterSelf(children[i]);
        }
        if (!string.IsNullOrEmpty(renamePrefix))
        {
            for (var i = 0; i < children.Count; i++)
            {
                children[i].SetAttributeValue("name", renamePrefix + " " + (i + 1));
                children[i].SetAttributeValue("autoNamed", "false");
            }
        }
        var folderIndex = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList().IndexOf(folder);
        foreach (var child in children)
        {
            child.SetAttributeValue("parentLayerIndex", folderIndex);
            child.SetAttributeValue("locked", "true");
        }
        if (lockFolder)
            folder.SetAttributeValue("locked", "true");
        if (folderName.Equals("Ground", StringComparison.OrdinalIgnoreCase))
            folder.SetAttributeValue("open", "false");
    }

    private static void RefreshFolderParents(XElement layers)
    {
        var list = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer").ToList();
        for (var i = 0; i < list.Count; i++)
        {
            if (!string.Equals((string?)list[i].Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                continue;
            var folderName = (string?)list[i].Attribute("name") ?? "";
            for (var j = i + 1; j < list.Count; j++)
            {
                if (string.Equals((string?)list[j].Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase))
                    break;
                var cn = (string?)list[j].Attribute("name") ?? "";
                if (!BelongsInFolder(folderName, cn))
                    break;
                list[j].SetAttributeValue("parentLayerIndex", i);
            }
        }
    }

    private static bool BelongsInFolder(string folderName, string childName)
    {
        if (folderName.Equals("Ground", StringComparison.OrdinalIgnoreCase))
            return childName.StartsWith("reference ", StringComparison.OrdinalIgnoreCase);
        if (folderName.Equals("Effects", StringComparison.OrdinalIgnoreCase))
            return childName.StartsWith("Effect", StringComparison.OrdinalIgnoreCase) ||
                   childName.IndexOf("effect", StringComparison.OrdinalIgnoreCase) >= 0;
        return false;
    }

    private static void MoveArtToTop(XElement layers, string art)
    {
        var el = layers.Elements().FirstOrDefault(e =>
            e.Name.LocalName == "DOMLayer" &&
            (string.Equals((string?)e.Attribute("name"), art, StringComparison.OrdinalIgnoreCase) ||
             string.Equals((string?)e.Attribute("name"), "stance", StringComparison.OrdinalIgnoreCase)));
        if (el == null)
            return;
        el.Remove();
        layers.AddFirst(el);
    }

    private static void LockExceptArt(XElement layers, string art)
    {
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var name = (string?)layer.Attribute("name") ?? "";
            var isArt = IsArtLayerName(name, art);
            if (isArt)
                layer.Attribute("locked")?.Remove();
            else
                layer.SetAttributeValue("locked", "true");
        }
    }

    private static bool IsEmptyPrunable(XElement layer)
    {
        var type = (string?)layer.Attribute("layerType") ?? "normal";
        if (type is "folder" or "guide" or "guided" or "mask" or "masked")
            return false;
        var name = (string?)layer.Attribute("name") ?? "";
        if (IsScript(name) || name.IndexOf("label", StringComparison.OrdinalIgnoreCase) >= 0)
            return false;
        foreach (var f in layer.Descendants().Where(e => e.Name.LocalName == "DOMFrame"))
        {
            if (!string.IsNullOrEmpty((string?)f.Attribute("name")))
                return false;
            if (f.Descendants().Any(e => e.Name.LocalName == "script"))
                return false;
            if (!string.IsNullOrEmpty((string?)f.Attribute("soundName")))
                return false;
            if (FrameHasArt(f))
                return false;
        }
        return true;
    }

    private static bool FrameHasArt(XElement frame) =>
        frame.Descendants().Any(e =>
            e.Name.LocalName is "DOMSymbolInstance" or "DOMBitmapInstance" or "DOMShape"
                or "DOMStaticText" or "DOMDynamicText" or "Edge");

    private static bool IsScript(string name) =>
        name.IndexOf("script", StringComparison.OrdinalIgnoreCase) >= 0 ||
        name.Equals("Actions", StringComparison.OrdinalIgnoreCase) ||
        name.Equals("Action", StringComparison.OrdinalIgnoreCase);

    private static XElement? FindStanceLayer(XElement layers) =>
        layers.Elements().FirstOrDefault(e =>
        {
            if (e.Name.LocalName != "DOMLayer")
                return false;
            var name = (string?)e.Attribute("name") ?? "";
            if (name.Equals("stance", StringComparison.OrdinalIgnoreCase))
                return true;
            return string.Equals(FirstInstance(e), "stance", StringComparison.OrdinalIgnoreCase);
        });

    private static XElement? PickArtLayer(XElement layers)
    {
        var list = layers.Elements().Where(e =>
            e.Name.LocalName == "DOMLayer" &&
            !string.Equals((string?)e.Attribute("layerType"), "folder", StringComparison.OrdinalIgnoreCase) &&
            FlaBoxLayerMerge.MapFamily((string?)e.Attribute("name")) == null).ToList();
        XElement? best = null;
        var bestScore = 0;
        foreach (var layer in list)
        {
            var score = ArtScore(layer);
            if (score < 1)
                continue;
            var layer1 = IsLayer1Name((string?)layer.Attribute("name"));
            var bestIsLayer1 = best != null && IsLayer1Name((string?)best.Attribute("name"));
            if (score > bestScore ||
                (score == bestScore && (layer1 || best == null || !bestIsLayer1)))
            {
                bestScore = score;
                best = layer;
            }
        }
        return best;
    }

    private static bool IsLayer1Name(string? name) =>
        (name ?? "").Equals("Layer 1", StringComparison.OrdinalIgnoreCase);

    private static int ArtScore(XElement layer)
    {
        var anyBox = false;
        var anyNonBox = false;
        var bitmapFrames = 0;
        var symbolFrames = 0;
        foreach (var frame in LayerFrames(layer))
        {
            var els = FrameElements(frame);
            if (els == null)
                continue;
            var hasBitmap = false;
            var hasSymbol = false;
            foreach (var el in els.Elements())
            {
                var tag = el.Name.LocalName;
                if (tag is "DOMBitmapInstance" or "DOMShape")
                {
                    anyNonBox = true;
                    hasBitmap = true;
                }
                else if (tag == "DOMSymbolInstance")
                {
                    if (FlaBoxLayerMerge.MapFamily((string?)el.Attribute("name")) == null)
                    {
                        anyNonBox = true;
                        hasSymbol = true;
                    }
                    else
                        anyBox = true;
                }
            }
            if (hasBitmap)
                bitmapFrames++;
            else if (hasSymbol)
                symbolFrames++;
        }
        if (anyBox && !anyNonBox)
            return 0;
        // Bitmaps/shapes are the character body. A 2-frame sprite layer must beat
        // a long FX clip so Layer 3 (Sora smash) wins over aurabase / item leftovers.
        return bitmapFrames > 0 ? bitmapFrames * 1000 + symbolFrames : symbolFrames;
    }

    private static bool LayerHasArtGraphics(XElement layer) =>
        layer.Descendants().Any(e => e.Name.LocalName is "DOMBitmapInstance" or "DOMShape");

    private static bool IsArtLayerName(string name, string art)
    {
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(art))
            return false;
        if (name.Equals(art, StringComparison.OrdinalIgnoreCase))
            return true;
        return Regex.IsMatch(name, "^" + Regex.Escape(art) + @" \d+$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static bool LayerLooksLikeCharacterArt(XElement layer)
    {
        var zero = LayerFrames(layer).FirstOrDefault(f => FrameIndex(f) == 0);
        if (zero == null)
            return false;
        var els = FrameElements(zero);
        if (els == null)
            return false;
        foreach (var el in els.Elements())
        {
            var tag = el.Name.LocalName;
            if (tag == "DOMBitmapInstance")
                return true;
            if (tag != "DOMSymbolInstance")
                continue;
            if (FlaBoxLayerMerge.MapFamily((string?)el.Attribute("name")) == null)
                return true;
        }
        return false;
    }

    private static bool LayerHasBoxInstance(XElement layer) =>
        layer.Descendants().Any(e =>
            e.Name.LocalName == "DOMSymbolInstance" &&
            FlaBoxLayerMerge.MapFamily((string?)e.Attribute("name")) != null);

    private static bool LayerHasNonBoxContent(XElement layer)
    {
        foreach (var el in layer.Descendants())
        {
            var tag = el.Name.LocalName;
            if (tag is "DOMBitmapInstance" or "DOMShape")
                return true;
            if (tag != "DOMSymbolInstance")
                continue;
            if (FlaBoxLayerMerge.MapFamily((string?)el.Attribute("name")) == null)
                return true;
        }
        return false;
    }

    private static string? FirstNamedNonBox(XElement layer)
    {
        foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance"))
        {
            var n = (string?)inst.Attribute("name");
            if (string.IsNullOrEmpty(n))
                continue;
            if (FlaBoxLayerMerge.MapFamily(n) == null)
                return n;
        }
        return null;
    }

    private static string? FirstInstance(XElement layer)
    {
        foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName is "DOMSymbolInstance" or "DOMBitmapInstance"))
        {
            var n = (string?)inst.Attribute("name");
            if (!string.IsNullOrEmpty(n))
                return n;
        }
        return null;
    }

    private static string? FirstClipLeaf(XElement layer)
    {
        foreach (var inst in layer.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance"))
        {
            var lib = (string?)inst.Attribute("libraryItemName");
            if (!string.IsNullOrEmpty(lib))
                return Leaf(lib);
        }
        return null;
    }

    private static string Unique(HashSet<string> used, string wanted)
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

    private static string Leaf(string name)
    {
        var n = (name ?? "").Replace('\\', '/');
        var slash = n.LastIndexOf('/');
        return slash >= 0 ? n[(slash + 1)..] : n;
    }

    private static void AdjustParent(XElement layers, int removed)
    {
        foreach (var layer in layers.Elements().Where(e => e.Name.LocalName == "DOMLayer"))
        {
            var parent = (string?)layer.Attribute("parentLayerIndex");
            if (string.IsNullOrEmpty(parent))
                continue;
            if (!int.TryParse(parent, NumberStyles.Integer, CultureInfo.InvariantCulture, out var p))
                continue;
            if (p == removed)
                layer.Attribute("parentLayerIndex")?.Remove();
            else if (p > removed)
                layer.SetAttributeValue("parentLayerIndex", p - 1);
        }
    }
}
