using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Fast SWF string-pool scanning (no FFDec). Reads ABC length-prefixed UTF-8 strings
    /// from the tail of FWS/CWS files — enough for music metadata and stage playlist ids.
    /// </summary>
    public static class SwfQuickScan
    {
        public static byte[]? ReadPayloadTail(string swfPath, int maxTailBytes = 1024 * 1024)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath)) return null;
            try
            {
                using var fs = File.OpenRead(swfPath);
                if (fs.Length < 8) return null;
                var hdr = new byte[3];
                _ = fs.Read(hdr, 0, 3);
                var sig = Encoding.ASCII.GetString(hdr);

                if (sig == "FWS")
                {
                    var take = (int)Math.Min(fs.Length, maxTailBytes);
                    fs.Seek(fs.Length - take, SeekOrigin.Begin);
                    var buf = new byte[take];
                    _ = fs.Read(buf, 0, take);
                    return buf;
                }

                if (sig == "CWS")
                {
                    fs.Seek(0, SeekOrigin.Begin);
                    var compressed = new byte[fs.Length];
                    _ = fs.Read(compressed, 0, compressed.Length);
                    using var ms = new MemoryStream(compressed, 8, compressed.Length - 8);
                    using var zlib = new ZLibStream(ms, CompressionMode.Decompress);
                    using var outMs = new MemoryStream();
                    zlib.CopyTo(outMs);
                    var full = outMs.ToArray();
                    if (full.Length <= maxTailBytes) return full;
                    var tail = new byte[maxTailBytes];
                    Buffer.BlockCopy(full, full.Length - maxTailBytes, tail, 0, maxTailBytes);
                    return tail;
                }

                var rawTake = (int)Math.Min(fs.Length, maxTailBytes);
                fs.Seek(fs.Length - rawTake, SeekOrigin.Begin);
                var raw = new byte[rawTake];
                _ = fs.Read(raw, 0, rawTake);
                return raw;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>ABC U30 + UTF-8 string extraction (allows Pokémon-style non-ASCII titles).</summary>
        public static List<string> ExtractLengthPrefixedStrings(ReadOnlySpan<byte> data, int minLen = 2, int maxLen = 180)
        {
            var list = new List<string>(256);
            for (var i = 0; i < data.Length - 1; i++)
            {
                if (!TryReadU30(data, i, out var len, out var lenBytes)) continue;
                if (len < minLen || len > maxLen) continue;
                var bodyStart = i + lenBytes;
                if (bodyStart + len > data.Length) continue;
                var body = data.Slice(bodyStart, len);
                if (!LooksLikeUtf8Text(body)) continue;
                string s;
                try { s = Encoding.UTF8.GetString(body); }
                catch { continue; }
                if (!IsPlausibleString(s)) continue;
                list.Add(s);
                i = bodyStart + len - 1;
            }
            return list;
        }

        private static bool TryReadU30(ReadOnlySpan<byte> data, int index, out int value, out int consumed)
        {
            value = 0;
            consumed = 0;
            var shift = 0;
            for (var i = 0; i < 5 && index + i < data.Length; i++)
            {
                var b = data[index + i];
                value |= (b & 0x7F) << shift;
                consumed++;
                if ((b & 0x80) == 0)
                    return value >= 0;
                shift += 7;
            }
            return false;
        }

        private static bool LooksLikeUtf8Text(ReadOnlySpan<byte> body)
        {
            var i = 0;
            var printable = 0;
            while (i < body.Length)
            {
                var b = body[i];
                if (b <= 0x7F)
                {
                    if (b < 9 || (b > 13 && b < 32)) return false;
                    if (b >= 32) printable++;
                    i++;
                    continue;
                }
                // 2-byte UTF-8
                if ((b & 0xE0) == 0xC0)
                {
                    if (i + 1 >= body.Length || (body[i + 1] & 0xC0) != 0x80) return false;
                    printable++;
                    i += 2;
                    continue;
                }
                // 3-byte UTF-8
                if ((b & 0xF0) == 0xE0)
                {
                    if (i + 2 >= body.Length || (body[i + 1] & 0xC0) != 0x80 || (body[i + 2] & 0xC0) != 0x80)
                        return false;
                    printable++;
                    i += 3;
                    continue;
                }
                return false;
            }
            return printable >= 2;
        }

        private static bool IsPlausibleString(string s)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 2) return false;
            var letterOrDigit = 0;
            foreach (var ch in s)
            {
                if (char.IsLetterOrDigit(ch)) { letterOrDigit++; continue; }
                if (char.IsWhiteSpace(ch) || ch is '_' or '.' or '/' or ':' or '-' or '\'' or '&'
                    or '!' or '?' or ',' or '(' or ')' or '[' or ']' or '+' or '#' or '*'
                    or '/' or '\\' or '"' or ';' or '%' or '@' or '~' or '—')
                    continue;
                // Allow other letters (accented) via Unicode categories
                if (char.GetUnicodeCategory(ch) is
                    System.Globalization.UnicodeCategory.LowercaseLetter or
                    System.Globalization.UnicodeCategory.UppercaseLetter or
                    System.Globalization.UnicodeCategory.TitlecaseLetter or
                    System.Globalization.UnicodeCategory.OtherLetter or
                    System.Globalization.UnicodeCategory.ModifierLetter or
                    System.Globalization.UnicodeCategory.NonSpacingMark)
                {
                    letterOrDigit++;
                    continue;
                }
                return false;
            }
            return letterOrDigit >= 1;
        }

        public static bool TryReadMusicMeta(string swfPath, out string songName, out string artist, out int loopMs)
        {
            songName = "";
            artist = "";
            loopMs = 0;
            var tail = ReadPayloadTail(swfPath, 1024 * 1024);
            if (tail == null) return false;
            var strings = ExtractLengthPrefixedStrings(tail);

            // Prefer id3 / songName / artist cluster (matches SSF2 music Main constants).
            for (var i = 0; i < strings.Count; i++)
            {
                if (!strings[i].Equals("songName", StringComparison.Ordinal)) continue;
                if (i + 1 >= strings.Count) continue;
                var maybeName = strings[i + 1];
                if (maybeName.Equals("artist", StringComparison.OrdinalIgnoreCase) ||
                    maybeName.Equals("id3", StringComparison.OrdinalIgnoreCase) ||
                    maybeName.Equals("musicLoop", StringComparison.OrdinalIgnoreCase))
                    continue;
                songName = maybeName;
                if (i + 2 < strings.Count && strings[i + 2].Equals("artist", StringComparison.Ordinal) &&
                    i + 3 < strings.Count)
                {
                    artist = strings[i + 3];
                }
                break;
            }

            if (string.IsNullOrWhiteSpace(artist))
            {
                for (var i = 0; i < strings.Count; i++)
                {
                    if (!strings[i].Equals("artist", StringComparison.Ordinal) || i + 1 >= strings.Count) continue;
                    var v = strings[i + 1];
                    if (v.Equals("songName", StringComparison.OrdinalIgnoreCase) ||
                        v.Equals("id3", StringComparison.OrdinalIgnoreCase) ||
                        v.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                        continue;
                    artist = v;
                    break;
                }
            }

            return !string.IsNullOrWhiteSpace(songName) || !string.IsNullOrWhiteSpace(artist);
        }

        public static List<string> ReadStagePlaylistIds(string swfPath, ISet<string>? knownMusicIds = null)
        {
            var tail = ReadPayloadTail(swfPath, 1024 * 1024);
            if (tail == null) return new List<string>();
            var strings = ExtractLengthPrefixedStrings(tail);
            var fromCluster = new List<string>();
            for (var i = 0; i < strings.Count; i++)
            {
                if (!strings[i].Equals("music", StringComparison.Ordinal)) continue;
                if (i == 0 || !strings[i - 1].Equals("sounds", StringComparison.Ordinal)) continue;
                for (var j = i + 1; j < strings.Count; j++)
                {
                    var s = strings[j];
                    if (IsMusicTrackId(s, knownMusicIds)) fromCluster.Add(s);
                    else break;
                }
                if (fromCluster.Count > 0) break;
            }

            var all = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var s in fromCluster)
            {
                if (seen.Add(s)) all.Add(s);
            }
            foreach (var s in strings)
            {
                if (!IsMusicTrackId(s, knownMusicIds)) continue;
                if (seen.Add(s)) all.Add(s);
            }
            return all;
        }

        /// <summary>Turn bgm_fooBar into "Foo Bar" when real metadata is missing.</summary>
        public static string HumanizeTrackId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return id;
            var s = id;
            if (s.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                s = s[4..];
            var sb = new StringBuilder(s.Length + 8);
            for (var i = 0; i < s.Length; i++)
            {
                var c = s[i];
                if (c is '_' or '-')
                {
                    sb.Append(' ');
                    continue;
                }
                if (i > 0 && char.IsUpper(c) && char.IsLower(s[i - 1]))
                    sb.Append(' ');
                sb.Append(c);
            }
            var words = sb.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i < words.Length; i++)
            {
                var w = words[i];
                if (w.Length == 0) continue;
                words[i] = char.ToUpperInvariant(w[0]) + (w.Length > 1 ? w[1..] : "");
            }
            return string.Join(' ', words);
        }

        private static bool IsMusicTrackId(string s, ISet<string>? known)
        {
            if (string.IsNullOrWhiteSpace(s) || s.Length < 4) return false;
            if (s.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)) return true;
            if (known != null && known.Contains(s)) return true;
            return false;
        }

        /// <summary>
        /// Best-effort in-place rewrite of songName/artist UTF-8 strings in an FWS SWF constant pool.
        /// Used after template clones where FFDec -replace leaves "Battlefield" etc. behind.
        /// </summary>
        public static bool TryRewriteMusicId3Strings(string swfPath, string songName, string artist)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath)) return false;
            if (string.IsNullOrWhiteSpace(songName) && string.IsNullOrWhiteSpace(artist)) return false;
            try
            {
                var bytes = File.ReadAllBytes(swfPath);
                if (bytes.Length < 16 || bytes[0] != (byte)'F' || bytes[1] != (byte)'W' || bytes[2] != (byte)'S')
                    return false;

                var changed = false;
                if (!string.IsNullOrWhiteSpace(songName))
                    changed |= RewriteAfterKey(bytes, "songName", songName!);
                if (!string.IsNullOrWhiteSpace(artist))
                    changed |= RewriteAfterKey(bytes, "artist", artist!);

                if (!changed) return false;
                File.WriteAllBytes(swfPath, bytes);
                return true;
            }
            catch { return false; }
        }

        private static bool RewriteAfterKey(byte[] data, string key, string newValue)
        {
            var keyBytes = Encoding.ASCII.GetBytes(key);
            for (var i = 0; i < data.Length - keyBytes.Length - 2; i++)
            {
                // length-prefixed key
                if (data[i] != keyBytes.Length) continue;
                var match = true;
                for (var k = 0; k < keyBytes.Length; k++)
                {
                    if (data[i + 1 + k] != keyBytes[k]) { match = false; break; }
                }
                if (!match) continue;

                var valueLenPos = i + 1 + keyBytes.Length;
                if (valueLenPos >= data.Length) continue;
                var oldLen = data[valueLenPos];
                if (oldLen < 1 || oldLen > 180 || valueLenPos + 1 + oldLen > data.Length) continue;

                var utf8 = Encoding.UTF8.GetBytes(newValue);
                if (utf8.Length == 0 || utf8.Length > 180) return false;

                if (utf8.Length <= oldLen)
                {
                    // Fit in existing slot; pad with spaces if shorter.
                    data[valueLenPos] = (byte)oldLen;
                    for (var j = 0; j < oldLen; j++)
                        data[valueLenPos + 1 + j] = j < utf8.Length ? utf8[j] : (byte)' ';
                    return true;
                }

                // Longer than old: only rewrite if we can shrink a nearby pad — skip for safety.
            }
            return false;
        }
    }
}
