using System.Collections.Generic;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// GameBanana mod ids a pack needs. A link uses the number after <c>mods/</c>.
    /// A token that is only digits is an id.
    /// </summary>
    public static class ModDependencies
    {
        public static int[] Parse(string? text)
        {
            var seen = new List<int>();
            if (string.IsNullOrWhiteSpace(text))
                return [];
            foreach (var token in Regex.Split(text.Trim(), @"[\s,;]+"))
            {
                if (token.Length == 0)
                    continue;
                var link = Regex.Match(token, @"mods/(\d+)", RegexOptions.IgnoreCase);
                var raw = link.Success ? link.Groups[1].Value : token;
                if (!Regex.IsMatch(raw, @"^\d+$"))
                    continue;
                if (!int.TryParse(raw, out var id) || id <= 0 || seen.Contains(id))
                    continue;
                seen.Add(id);
            }
            return seen.ToArray();
        }

        public static int[] Read(JObject? info)
        {
            if (info?["dependencies"] is not JArray array)
                return [];
            var seen = new List<int>();
            foreach (var item in array)
            {
                if (!int.TryParse(item?.ToString(), out var id) || id <= 0 || seen.Contains(id))
                    continue;
                seen.Add(id);
            }
            return seen.ToArray();
        }

        public static void Write(JObject info, int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                info.Remove("dependencies");
                return;
            }
            info["dependencies"] = new JArray(ids);
        }

        public static string Format(int[] ids) =>
            ids == null || ids.Length == 0 ? "" : string.Join(", ", ids);
    }
}
