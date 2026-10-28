using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Menu blurbs from <c>event_mode.swf</c>, and <c>info.json</c> descriptions for expansion packs.
    /// </summary>
    public static class EventMenuText
    {
        public static string Description(string shellRoot, EventSourceChoice source)
        {
            if (source == null)
                return "";
            var pack = PackDescription(source.Path);
            if (pack != null)
                return pack;
            var id = IdOf(source.ClassName);
            if (id.Length == 0 || string.IsNullOrWhiteSpace(shellRoot))
                return "";
            var path = Path.Combine(shellRoot, "data", "modes", "event_mode.swf");
            if (!File.Exists(path))
                return "";
            return DescriptionForId(File.ReadAllBytes(path), id);
        }

        public static string DescriptionForId(byte[] data, string id)
        {
            if (data == null || string.IsNullOrWhiteSpace(id))
                return "";
            var strings = ReadStrings(data);
            for (var i = 0; i < strings.Count; i++)
            {
                if (!string.Equals(strings[i], id, StringComparison.Ordinal))
                    continue;
                var n = i + 1;
                if (n < strings.Count && IsTitle(strings[n]))
                    n++;
                if (n < strings.Count && strings[n] == "description")
                    n++;
                if (n < strings.Count && IsBlurb(strings[n]))
                    return strings[n];
            }
            return "";
        }

        private static string? PackDescription(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !path.EndsWith(".as", StringComparison.OrdinalIgnoreCase))
                return null;
            var src = Directory.GetParent(path);
            var pack = src?.Parent?.FullName;
            if (string.IsNullOrWhiteSpace(pack))
                return "";
            var infoPath = Path.Combine(pack, "info.json");
            if (!File.Exists(infoPath))
                return "";
            try
            {
                var info = JObject.Parse(File.ReadAllText(infoPath));
                return info["description"]?.ToString() ?? "";
            }
            catch
            {
                return "";
            }
        }

        private static string IdOf(string className)
        {
            if (string.IsNullOrWhiteSpace(className))
                return "";
            var match = System.Text.RegularExpressions.Regex.Match(
                className,
                @"^EventMode\d+(?:Special)?_([A-Za-z][A-Za-z0-9]*)$");
            return match.Success ? match.Groups[1].Value : "";
        }

        private static bool IsTitle(string text)
        {
            if (text.StartsWith("All-Star", StringComparison.Ordinal))
                return true;
            var dot = text.IndexOf(". ", StringComparison.Ordinal);
            return dot > 0 && dot < 4 && char.IsDigit(text[0]);
        }

        private static bool IsBlurb(string text)
        {
            return text.Contains(' ') && !IsTitle(text);
        }

        private static List<string> ReadStrings(byte[] data)
        {
            var utf8 = new UTF8Encoding(false, true);
            var strings = new List<string>();
            var i = 0;
            while (i < data.Length)
            {
                var start = i;
                var n = ReadU30(data, ref i);
                if (n is < 3 or > 400 || i + n > data.Length)
                {
                    i = start + 1;
                    continue;
                }
                try
                {
                    var text = utf8.GetString(data, i, n);
                    if (text.Length >= 3 && IsPrintable(text))
                    {
                        strings.Add(text);
                        i += n;
                        continue;
                    }
                }
                catch (DecoderFallbackException)
                {
                    /* not a string */
                }
                i = start + 1;
            }
            return strings;
        }

        private static int ReadU30(byte[] data, ref int i)
        {
            var result = 0;
            for (var shift = 0; shift <= 28; shift += 7)
            {
                if (i >= data.Length)
                    return -1;
                var b = data[i++];
                result |= (b & 0x7f) << shift;
                if ((b & 0x80) == 0)
                    return result;
            }
            return -1;
        }

        private static bool IsPrintable(string text)
        {
            foreach (var c in text)
            {
                if (char.IsControl(c))
                    return false;
            }
            return true;
        }
    }
}
