using System.Text.RegularExpressions;
using SSF2ModManager.Models;

namespace SSF2ModManager.Services
{
    /// <summary>Download Mods list rules: which filter kind a GameBanana mod belongs to, and whether it mentions 0.9b.</summary>
    public static class BrowseModFilter
    {
        private static readonly Regex NineB = new(@"(?<![a-z0-9])9\s?b(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex Modpack = new(@"\bmod\s*-?\s*packs?\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>Name, version and description (<c>_sText</c>, once details have loaded). Any 9B mention hides the mod.</summary>
        public static bool MentionsNineB(GameBananaMod mod) =>
            MentionsNineB(mod.Name + " " + mod.Version + " " + PlainText(mod.FullText) + " " + mod.Description);

        public static bool MentionsNineB(string text) => NineB.IsMatch(text);

        public static bool MentionsModpack(GameBananaMod mod) =>
            MentionsModpack(mod.Name + " " + mod.Version + " " + PlainText(mod.FullText) + " " + mod.Description);

        public static bool MentionsModpack(string text) => Modpack.IsMatch(text ?? "");

        /// <summary>A listed download whose name or url ends in .exe or .json.</summary>
        public static bool HasBlockedDownload(GameBananaMod mod)
        {
            if (mod.Files == null)
                return false;
            foreach (var file in mod.Files)
            {
                if (IsExeOrJson(file.FileName) || IsExeOrJson(file.DownloadUrl))
                    return true;
            }
            return false;
        }

        public static bool IsUnsupported(GameBananaMod mod) =>
            MentionsNineB(mod) || MentionsModpack(mod) || HasBlockedDownload(mod);

        /// <summary>A mod whose listed files are not an .ssfe package. Hidden until the browse box is checked.</summary>
        public static bool IsSemiSupported(GameBananaMod mod)
        {
            if (IsUnsupported(mod) || mod.Files == null || mod.Files.Count == 0)
                return false;
            foreach (var file in mod.Files)
            {
                if (EndsWithSsfe(file.FileName) || EndsWithSsfe(file.DownloadUrl))
                    return false;
            }
            return true;
        }

        private static bool EndsWithSsfe(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var path = value.Split('?', '#')[0].Trim();
            return path.EndsWith(".ssfe", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsExeOrJson(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return false;
            var path = value.Split('?', '#')[0].Trim();
            return path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase);
        }

        public static string PlainText(string? html) =>
            string.IsNullOrEmpty(html)
                ? ""
                : System.Net.WebUtility.HtmlDecode(Regex.Replace(Regex.Replace(html, "<[^>]+>", " "), @"\s+", " ")).Trim();
        public static string KindFor(string? rootCategory) => (rootCategory ?? "").Trim().ToLowerInvariant() switch
        {
            "characters" => "character",
            "maps" => "stage",
            "sounds" or "music" => "music",
            _ => "mod"
        };
    }
}
