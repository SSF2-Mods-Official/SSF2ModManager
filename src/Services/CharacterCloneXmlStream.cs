using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Disk-backed SWF XML rewrite so a full-character clone does not load a
    /// multi-hundred-MB UTF-16 string (and several copies of it) into RAM.
    /// </summary>
    public static class CharacterCloneXmlStream
    {
        private const int ChunkChars = 4 * 1024 * 1024;
        private const int ScanOverlapChars = 256 * 1024;

        private static readonly string[] IdentityScanKeys =
        {
            "statsName", "displayName", "linkage_id", "linkage_id2", "linkage_id_special",
            "finalSmashCutin", "thumbnail", "seriesIcon", "classAPI", "guid"
        };

        public static string BuildPlanXml(string xmlPath)
        {
            var identity = new CharacterCloneService.CharacterIdentityNames();
            var symbols = new List<string>();
            var extClasses = new HashSet<string>(StringComparer.Ordinal);
            ForEachWindow(xmlPath, ScanOverlapChars, window =>
            {
                if (WindowMightContainIdentity(window))
                    identity = MergeIdentity(identity, CharacterCloneService.ParseIdentityFieldsFromXml(window));
                if (window.Contains("SymbolClassTag", StringComparison.Ordinal))
                    symbols.AddRange(CharacterCloneService.ParseSymbolClassNamesFromXml(window));
                if (window.Contains("Ext</item>", StringComparison.Ordinal))
                {
                    foreach (Match m in Regex.Matches(window, @"<item>([A-Za-z][A-Za-z0-9_]*Ext)</item>"))
                    {
                        var name = m.Groups[1].Value;
                        if (!name.Contains('.', StringComparison.Ordinal))
                            extClasses.Add(name);
                    }
                }
            });

            var unique = symbols
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.Ordinal)
                .ToList();
            return SynthesizePlanXml(identity, unique, extClasses);
        }

        public static int RewriteIdentifierPairsToFile(
            string inputPath,
            string outputPath,
            IReadOnlyList<(string From, string To)> pairs)
        {
            if (string.IsNullOrWhiteSpace(inputPath) || !File.Exists(inputPath))
                throw new FileNotFoundException("SWF XML not found.", inputPath);

            var usable = (pairs ?? Array.Empty<(string, string)>())
                .Where(p => !string.IsNullOrEmpty(p.From) && p.From != p.To)
                .GroupBy(p => p.From, StringComparer.Ordinal)
                .Select(g => g.First())
                .OrderByDescending(p => p.From.Length)
                .ToList();

            if (usable.Count == 0)
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                return 0;
            }

            var maxFrom = usable.Max(p => p.From.Length);
            var overlapKeep = maxFrom + 8;
            var byFirst = usable
                .GroupBy(p => p.From[0])
                .ToDictionary(g => g.Key, g => g.ToList());
            var firstChars = byFirst.Keys.ToArray();
            var seekChars = firstChars.Concat(new[] { '<' }).Distinct().ToArray();

            using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), 1 << 20);
            var buf = new char[ChunkChars];
            var carry = "";
            char? lookbehind = null;
            var hits = 0;

            while (true)
            {
                var n = reader.Read(buf, 0, buf.Length);
                var done = n <= 0;
                var chunk = n > 0 ? new string(buf, 0, n) : "";
                var window = carry + chunk;
                if (window.Length == 0)
                    break;

                var emitLimit = done ? window.Length : Math.Max(0, window.Length - overlapKeep);
                var pos = 0;
                while (pos < emitLimit)
                {
                    if (lookbehind is char prev && IsAsciiAlnum(prev))
                    {
                        var run = pos;
                        while (run < emitLimit && IsAsciiAlnum(window[run]))
                            run++;
                        if (run > pos)
                        {
                            writer.Write(window.AsSpan(pos, run - pos));
                            lookbehind = window[run - 1];
                            pos = run;
                            continue;
                        }
                    }

                    if (TrySkipBulkyXmlTag(window, pos, emitLimit, done, writer, ref lookbehind, out var bulkyEnd))
                    {
                        pos = bulkyEnd;
                        continue;
                    }

                    if (IsHexDigit(window[pos]))
                    {
                        var run = pos;
                        while (run < emitLimit && IsHexDigit(window[run]))
                            run++;
                        if (run - pos >= 48)
                        {
                            writer.Write(window.AsSpan(pos, run - pos));
                            lookbehind = window[run - 1];
                            pos = run;
                            continue;
                        }
                    }

                    var matched = false;
                    if (byFirst.TryGetValue(window[pos], out var cands))
                    {
                        foreach (var p in cands)
                        {
                            if (pos + p.From.Length > window.Length)
                                continue;
                            if (!CharacterCloneService.IdentifierMatchesAt(
                                    window.AsSpan(), pos, p.From, pos == 0 ? lookbehind : null))
                                continue;
                            writer.Write(p.To);
                            hits++;
                            lookbehind = p.To.Length > 0 ? p.To[^1] : lookbehind;
                            pos += p.From.Length;
                            matched = true;
                            break;
                        }
                    }

                    if (!matched)
                    {
                        var remain = emitLimit - pos;
                        if (remain <= 1)
                        {
                            writer.Write(window[pos]);
                            lookbehind = window[pos];
                            pos++;
                            continue;
                        }

                        var rel = window.AsSpan(pos + 1, remain - 1).IndexOfAny(seekChars);
                        var next = rel < 0 ? emitLimit : pos + 1 + rel;
                        writer.Write(window.AsSpan(pos, next - pos));
                        lookbehind = window[next - 1];
                        pos = next;
                    }
                }

                carry = pos < window.Length ? window[pos..] : "";
                if (done)
                    break;
            }

            return hits;
        }

        private static readonly string[] BulkyXmlTagOpens =
        {
            "<item type=\"StraightEdgeRecord\"",
            "<item type=\"CurvedEdgeRecord\"",
            "<item type=\"StyleChangeRecord\"",
            "<item type=\"EndShapeRecord\""
        };

        private static bool TrySkipBulkyXmlTag(
            string window,
            int pos,
            int emitLimit,
            bool done,
            StreamWriter writer,
            ref char? lookbehind,
            out int newPos)
        {
            newPos = pos;
            if (window[pos] != '<')
                return false;

            string? open = null;
            foreach (var candidate in BulkyXmlTagOpens)
            {
                if (pos + candidate.Length <= window.Length &&
                    window.AsSpan(pos, candidate.Length).Equals(candidate.AsSpan(), StringComparison.Ordinal))
                {
                    open = candidate;
                    break;
                }
            }

            if (open == null)
                return false;

            var searchFrom = pos + open.Length;
            var gt = window.IndexOf('>', searchFrom);
            if (gt < 0 || gt >= emitLimit)
            {
                if (!done)
                {
                    newPos = pos;
                    return false;
                }

                writer.Write(window.AsSpan(pos, emitLimit - pos));
                lookbehind = window[emitLimit - 1];
                newPos = emitLimit;
                return true;
            }

            var end = gt + 1;
            writer.Write(window.AsSpan(pos, end - pos));
            lookbehind = window[end - 1];
            newPos = end;
            return true;
        }

        /// <summary>
        /// One pass over the rewritten XML for display name + resource GUID (replaces
        /// separate ApplyDisplayNameInFile / ApplyGuidInFile / literal GUID sweeps).
        /// </summary>
        public static (int DisplayNameHits, int GuidHits) ApplyCloneMetadataInFile(
            string path,
            string? newDisplayName,
            string? newGuid)
        {
            if (string.IsNullOrWhiteSpace(newDisplayName) && string.IsNullOrWhiteSpace(newGuid))
                return (0, 0);

            var display = string.IsNullOrWhiteSpace(newDisplayName) ? null : newDisplayName.Trim();
            var guid = string.IsNullOrWhiteSpace(newGuid) ? null : newGuid.Trim();
            var oldGuid = guid != null ? TryReadExistingGuid(path) : null;
            var tmp = path + ".meta.tmp";
            var displayHits = 0;
            var guidHits = 0;

            var displayOpen = display != null ? "<item>displayName</item>" : null;
            var guidOpen = guid != null ? "<item>guid</item>" : null;
            var guidPattern = guid != null
                ? new Regex(
                    @"register\s*\(\s*[""']guid[""']\s*,\s*[""']([^""']+)[""']\s*\)",
                    RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)
                : null;
            var maxKeyLen = Math.Max(displayOpen?.Length ?? 0, guidOpen?.Length ?? 0);
            var overlap = Math.Max(maxKeyLen + 512, 1024);

            using (var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true))
            using (var writer = new StreamWriter(tmp, false, new UTF8Encoding(false), 1 << 20))
            {
                var buf = new char[ChunkChars];
                var carry = "";

                while (true)
                {
                    var n = reader.Read(buf, 0, buf.Length);
                    var done = n <= 0;
                    var window = carry + (n > 0 ? new string(buf, 0, n) : "");
                    if (window.Length == 0)
                        break;

                    var emitLimit = done ? window.Length : Math.Max(0, window.Length - overlap);
                    var pos = 0;
                    while (pos < emitLimit)
                    {
                        var nextSpecial = FindNextMetadataEdit(
                            window, pos, emitLimit, displayOpen, guidOpen, guidPattern, oldGuid, guid);
                        if (nextSpecial > pos)
                        {
                            writer.Write(window.AsSpan(pos, nextSpecial - pos));
                            pos = nextSpecial;
                        }

                        if (pos >= emitLimit)
                            break;

                        if (displayOpen != null &&
                            StartsWithIgnoreCase(window, pos, displayOpen))
                        {
                            pos += RewritePoolItemAt(
                                window, pos, displayOpen, XmlEscape(display!), writer, ref emitLimit, done, ref displayHits);
                            continue;
                        }

                        if (guidOpen != null &&
                            StartsWithIgnoreCase(window, pos, guidOpen))
                        {
                            pos += RewritePoolItemAt(
                                window, pos, guidOpen, guid!, writer, ref emitLimit, done, ref guidHits,
                                value => CharacterCloneService.IsDisplayNameGuardKey(value) ||
                                         value.Equals(guid!, StringComparison.OrdinalIgnoreCase));
                            continue;
                        }

                        if (guidPattern != null)
                        {
                            var regMatch = guidPattern.Match(window, pos);
                            if (regMatch.Success && regMatch.Index == pos)
                            {
                                var current = regMatch.Groups[1].Value;
                                if (current.Equals(guid!, StringComparison.OrdinalIgnoreCase))
                                    writer.Write(regMatch.Value);
                                else
                                {
                                    writer.Write("register(\"guid\",\"" + guid + "\")");
                                    guidHits++;
                                }

                                pos += regMatch.Length;
                                continue;
                            }
                        }

                        if (oldGuid != null && guid != null &&
                            StartsWithIgnoreCase(window, pos, oldGuid))
                        {
                            writer.Write(guid);
                            guidHits++;
                            pos += oldGuid.Length;
                            continue;
                        }

                        writer.Write(window[pos]);
                        pos++;
                    }

                    carry = pos < window.Length ? window[pos..] : "";
                    if (done)
                        break;
                }
            }

            ReplaceFile(tmp, path);
            return (displayHits, guidHits);
        }

        private static int FindNextMetadataEdit(
            string window,
            int pos,
            int emitLimit,
            string? displayOpen,
            string? guidOpen,
            Regex? guidPattern,
            string? oldGuid,
            string? newGuid)
        {
            var next = emitLimit;
            if (displayOpen != null)
            {
                var idx = window.IndexOf(displayOpen, pos, emitLimit - pos, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    next = Math.Min(next, idx);
            }

            if (guidOpen != null)
            {
                var idx = window.IndexOf(guidOpen, pos, emitLimit - pos, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    next = Math.Min(next, idx);
            }

            if (guidPattern != null)
            {
                var m = guidPattern.Match(window, pos);
                if (m.Success && m.Index < emitLimit)
                    next = Math.Min(next, m.Index);
            }

            if (oldGuid != null && newGuid != null)
            {
                var idx = window.IndexOf(oldGuid, pos, emitLimit - pos, StringComparison.OrdinalIgnoreCase);
                if (idx >= 0)
                    next = Math.Min(next, idx);
            }

            return next;
        }

        private static bool StartsWithIgnoreCase(string text, int index, string prefix) =>
            index + prefix.Length <= text.Length &&
            text.AsSpan(index, prefix.Length).Equals(prefix.AsSpan(), StringComparison.OrdinalIgnoreCase);

        private static string? TryReadExistingGuid(string path)
        {
            string? found = null;
            var registerPattern = new Regex(
                @"register\s*\(\s*[""']guid[""']\s*,\s*[""']([^""']+)[""']\s*\)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            ForEachWindow(path, ScanOverlapChars, window =>
            {
                if (found != null)
                    return;
                if (!window.Contains("guid", StringComparison.Ordinal))
                    return;

                var m = registerPattern.Match(window);
                if (m.Success)
                {
                    found = m.Groups[1].Value;
                    return;
                }

                var open = "<item>guid</item>";
                var idx = window.IndexOf(open, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return;
                const string itemOpen = "<item>";
                const string itemClose = "</item>";
                var valueStart = window.IndexOf(itemOpen, idx + open.Length, StringComparison.OrdinalIgnoreCase);
                if (valueStart < 0)
                    return;
                valueStart += itemOpen.Length;
                var valueEnd = window.IndexOf(itemClose, valueStart, StringComparison.OrdinalIgnoreCase);
                if (valueEnd <= valueStart)
                    return;
                var value = window[valueStart..valueEnd];
                if (!CharacterCloneService.IsDisplayNameGuardKey(value))
                    found = value;
            });
            return found;
        }

        private static int RewritePoolItemAt(
            string window,
            int pos,
            string open,
            string newValue,
            StreamWriter writer,
            ref int emitLimit,
            bool done,
            ref int hits,
            Func<string, bool>? keepExisting = null)
        {
            const string itemOpen = "<item>";
            const string itemClose = "</item>";
            writer.Write(window.AsSpan(pos, open.Length));
            var cursor = pos + open.Length;
            var wsEnd = cursor;
            while (wsEnd < window.Length && char.IsWhiteSpace(window[wsEnd]))
                wsEnd++;
            if (wsEnd + itemOpen.Length + itemClose.Length > window.Length && !done)
            {
                emitLimit = pos;
                return 0;
            }

            writer.Write(window.AsSpan(cursor, wsEnd - cursor));
            if (wsEnd + itemOpen.Length > window.Length ||
                !window.AsSpan(wsEnd, itemOpen.Length).Equals(itemOpen.AsSpan(), StringComparison.OrdinalIgnoreCase))
                return open.Length;

            writer.Write(window.AsSpan(wsEnd, itemOpen.Length));
            var valueStart = wsEnd + itemOpen.Length;
            var valueEnd = window.IndexOf(itemClose, valueStart, StringComparison.OrdinalIgnoreCase);
            if (valueEnd < 0)
            {
                emitLimit = pos;
                return 0;
            }

            var current = window[valueStart..valueEnd];
            if (keepExisting != null && keepExisting(current))
                writer.Write(current);
            else
            {
                writer.Write(newValue);
                hits++;
            }

            writer.Write(window.AsSpan(valueEnd, itemClose.Length));
            return valueEnd + itemClose.Length - pos;
        }

        private static bool WindowMightContainIdentity(string window)
        {
            foreach (var key in IdentityScanKeys)
            {
                if (window.Contains(key, StringComparison.Ordinal))
                    return true;
            }

            return false;
        }

        private static bool IsAsciiAlnum(char c) =>
            (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');

        private static bool IsHexDigit(char c) =>
            (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');

        public static int ApplyDisplayNameInFile(string path, string newDisplayName)
        {
            if (string.IsNullOrWhiteSpace(newDisplayName))
                return 0;
            var tmp = path + ".dn.tmp";
            var hits = ReplacePoolItemValue(
                path, tmp, "displayName", XmlEscape(newDisplayName),
                skip: CharacterCloneService.IsDisplayNameGuardKey);
            ReplaceFile(tmp, path);
            return hits;
        }

        public static int ApplyGuidInFile(string path, string newGuid)
        {
            if (string.IsNullOrWhiteSpace(newGuid))
                return 0;
            var guid = newGuid.Trim();
            string? oldGuid = null;
            var hits = 0;

            var tmp = path + ".guid.tmp";
            hits += ReplacePoolItemValue(path, tmp, "guid", guid, value =>
            {
                if (CharacterCloneService.IsDisplayNameGuardKey(value))
                    return true;
                if (string.IsNullOrEmpty(oldGuid) && !value.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    oldGuid = value;
                return value.Equals(guid, StringComparison.OrdinalIgnoreCase);
            });
            ReplaceFile(tmp, path);

            var tmp2 = path + ".guid2.tmp";
            hits += ReplaceRegisterGuid(path, tmp2, guid, ref oldGuid);
            ReplaceFile(tmp2, path);

            if (!string.IsNullOrEmpty(oldGuid) &&
                !oldGuid.Equals(guid, StringComparison.OrdinalIgnoreCase))
            {
                var tmp3 = path + ".guid3.tmp";
                hits += ReplaceLiteral(path, tmp3, oldGuid, guid);
                ReplaceFile(tmp3, path);
            }

            return hits;
        }

        public static bool FileContains(string path, string needle)
        {
            if (string.IsNullOrEmpty(needle) || !File.Exists(path))
                return false;
            var found = false;
            ForEachWindow(path, Math.Max(needle.Length * 2, 256), window =>
            {
                if (window.Contains(needle, StringComparison.Ordinal))
                    found = true;
            });
            return found;
        }

        public static List<string> ParseSymbolClassNamesFromFile(string xmlPath)
        {
            var names = new List<string>();
            ForEachWindow(xmlPath, ScanOverlapChars, window =>
                names.AddRange(CharacterCloneService.ParseSymbolClassNamesFromXml(window)));
            return names
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Distinct(StringComparer.Ordinal)
                .ToList();
        }

        public static void ForEachWindow(string path, int overlap, Action<string> onWindow)
        {
            using var reader = new StreamReader(path, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            var buf = new char[ChunkChars];
            var carry = "";
            while (true)
            {
                var n = reader.Read(buf, 0, buf.Length);
                if (n <= 0)
                {
                    if (carry.Length > 0)
                        onWindow(carry);
                    break;
                }

                var window = carry + new string(buf, 0, n);
                onWindow(window);
                carry = window.Length <= overlap ? window : window[(window.Length - overlap)..];
            }
        }

        private static int ReplacePoolItemValue(
            string inputPath,
            string outputPath,
            string key,
            string newValue,
            Func<string, bool>? skip)
        {
            var open = "<item>" + key + "</item>";
            const string itemOpen = "<item>";
            const string itemClose = "</item>";
            var overlap = open.Length + 256;
            using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
            var buf = new char[ChunkChars];
            var carry = "";
            var hits = 0;

            while (true)
            {
                var n = reader.Read(buf, 0, buf.Length);
                var done = n <= 0;
                var window = carry + (n > 0 ? new string(buf, 0, n) : "");
                if (window.Length == 0)
                    break;

                var emitLimit = done ? window.Length : Math.Max(0, window.Length - overlap);
                var pos = 0;
                while (pos < emitLimit)
                {
                    var idx = window.IndexOf(open, pos, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0 || idx >= emitLimit)
                    {
                        writer.Write(window.AsSpan(pos, emitLimit - pos));
                        pos = emitLimit;
                        break;
                    }

                    writer.Write(window.AsSpan(pos, idx - pos));
                    writer.Write(window.AsSpan(idx, open.Length));
                    var cursor = idx + open.Length;
                    var wsEnd = cursor;
                    while (wsEnd < window.Length && char.IsWhiteSpace(window[wsEnd]))
                        wsEnd++;
                    if (wsEnd + itemOpen.Length + itemClose.Length > window.Length && !done)
                    {
                        pos = idx;
                        emitLimit = idx;
                        break;
                    }

                    writer.Write(window.AsSpan(cursor, wsEnd - cursor));
                    if (wsEnd + itemOpen.Length > window.Length ||
                        !window.AsSpan(wsEnd, itemOpen.Length).Equals(itemOpen.AsSpan(), StringComparison.OrdinalIgnoreCase))
                    {
                        pos = wsEnd;
                        continue;
                    }

                    writer.Write(window.AsSpan(wsEnd, itemOpen.Length));
                    var valueStart = wsEnd + itemOpen.Length;
                    var valueEnd = window.IndexOf(itemClose, valueStart, StringComparison.OrdinalIgnoreCase);
                    if (valueEnd < 0)
                    {
                        pos = idx;
                        emitLimit = idx;
                        break;
                    }

                    var current = window[valueStart..valueEnd];
                    if (skip != null && skip(current))
                    {
                        writer.Write(current);
                    }
                    else
                    {
                        writer.Write(newValue);
                        hits++;
                    }

                    writer.Write(window.AsSpan(valueEnd, itemClose.Length));
                    pos = valueEnd + itemClose.Length;
                }

                carry = pos < window.Length ? window[pos..] : "";
                if (done)
                    break;
            }

            return hits;
        }

        private static int ReplaceRegisterGuid(string inputPath, string outputPath, string guid, ref string? oldGuid)
        {
            var pattern = new Regex(
                @"register\s*\(\s*[""']guid[""']\s*,\s*[""']([^""']+)[""']\s*\)",
                RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
            var overlap = 128;
            using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
            var buf = new char[ChunkChars];
            var carry = "";
            var hits = 0;
            var capturedOld = oldGuid;

            while (true)
            {
                var n = reader.Read(buf, 0, buf.Length);
                var done = n <= 0;
                var window = carry + (n > 0 ? new string(buf, 0, n) : "");
                if (window.Length == 0)
                    break;

                var emitLimit = done ? window.Length : Math.Max(0, window.Length - overlap);
                var pos = 0;
                while (pos < emitLimit)
                {
                    var m = pattern.Match(window, pos);
                    if (!m.Success || m.Index >= emitLimit)
                    {
                        writer.Write(window.AsSpan(pos, emitLimit - pos));
                        pos = emitLimit;
                        break;
                    }

                    writer.Write(window.AsSpan(pos, m.Index - pos));
                    var current = m.Groups[1].Value;
                    if (string.IsNullOrEmpty(capturedOld) && !current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                        capturedOld = current;
                    if (current.Equals(guid, StringComparison.OrdinalIgnoreCase))
                    {
                        writer.Write(m.Value);
                    }
                    else
                    {
                        writer.Write("register(\"guid\",\"" + guid + "\")");
                        hits++;
                    }

                    pos = m.Index + m.Length;
                }

                carry = pos < window.Length ? window[pos..] : "";
                if (done)
                    break;
            }

            oldGuid = capturedOld;
            return hits;
        }

        private static int ReplaceLiteral(string inputPath, string outputPath, string from, string to)
        {
            if (string.IsNullOrEmpty(from) || from == to)
            {
                File.Copy(inputPath, outputPath, overwrite: true);
                return 0;
            }

            var overlap = from.Length + 8;
            using var reader = new StreamReader(inputPath, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
            using var writer = new StreamWriter(outputPath, false, new UTF8Encoding(false));
            var buf = new char[ChunkChars];
            var carry = "";
            var hits = 0;

            while (true)
            {
                var n = reader.Read(buf, 0, buf.Length);
                var done = n <= 0;
                var window = carry + (n > 0 ? new string(buf, 0, n) : "");
                if (window.Length == 0)
                    break;

                var emitLimit = done ? window.Length : Math.Max(0, window.Length - overlap);
                var pos = 0;
                while (pos < emitLimit)
                {
                    var idx = window.IndexOf(from, pos, StringComparison.OrdinalIgnoreCase);
                    if (idx < 0 || idx >= emitLimit)
                    {
                        writer.Write(window.AsSpan(pos, emitLimit - pos));
                        pos = emitLimit;
                        break;
                    }

                    writer.Write(window.AsSpan(pos, idx - pos));
                    writer.Write(to);
                    hits++;
                    pos = idx + from.Length;
                }

                carry = pos < window.Length ? window[pos..] : "";
                if (done)
                    break;
            }

            return hits;
        }

        private static CharacterCloneService.CharacterIdentityNames MergeIdentity(
            CharacterCloneService.CharacterIdentityNames current,
            CharacterCloneService.CharacterIdentityNames next) =>
            new()
            {
                StatsName = current.StatsName ?? next.StatsName,
                DisplayName = current.DisplayName ?? next.DisplayName,
                LinkageId = current.LinkageId ?? next.LinkageId,
                LinkageId2 = current.LinkageId2 ?? next.LinkageId2,
                LinkageIdSpecial = current.LinkageIdSpecial ?? next.LinkageIdSpecial,
                FinalSmashCutin = current.FinalSmashCutin ?? next.FinalSmashCutin,
                Thumbnail = current.Thumbnail ?? next.Thumbnail,
                SeriesIcon = current.SeriesIcon ?? next.SeriesIcon,
                ClassApi = current.ClassApi ?? next.ClassApi
            };

        private static string SynthesizePlanXml(
            CharacterCloneService.CharacterIdentityNames identity,
            IReadOnlyList<string> symbolNames,
            IEnumerable<string>? extClassNames = null)
        {
            var sb = new StringBuilder();
            void Item(string key, string? value)
            {
                if (string.IsNullOrWhiteSpace(value)) return;
                sb.Append("<item>").Append(key).Append("</item><item>")
                    .Append(XmlEscape(value)).Append("</item>");
            }

            Item("statsName", identity.StatsName);
            Item("displayName", identity.DisplayName);
            Item("linkage_id", identity.LinkageId);
            Item("linkage_id2", identity.LinkageId2);
            Item("linkage_id_special", identity.LinkageIdSpecial);
            Item("finalSmashCutin", identity.FinalSmashCutin);
            Item("thumbnail", identity.Thumbnail);
            Item("seriesIcon", identity.SeriesIcon);
            Item("classAPI", identity.ClassApi);
            if (extClassNames != null)
            {
                foreach (var ext in extClassNames)
                    sb.Append("<item>").Append(XmlEscape(ext)).Append("</item>");
            }
            sb.Append("<item type=\"SymbolClassTag\"><names>");
            foreach (var name in symbolNames)
                sb.Append("<item>").Append(XmlEscape(name)).Append("</item>");
            sb.Append("</names></item>");
            return sb.ToString();
        }

        private static string XmlEscape(string value) =>
            value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static void ReplaceFile(string tmp, string dest)
        {
            if (!File.Exists(tmp))
                return;
            File.Copy(tmp, dest, overwrite: true);
            try { File.Delete(tmp); } catch { /* ignore */ }
        }
    }
}
