using System.Text.RegularExpressions;
using SSF2ModManager.Models;

namespace SSF2ModManager.Services
{
    /// <summary>Download Mods list rules: which filter kind a GameBanana mod belongs to, and whether it is 0.9b-only.</summary>
    public static class BrowseModFilter
    {
        private static readonly Regex NineB = new(@"(?<![a-z0-9])9\s?b(?![a-z0-9])", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex BetaOrSandbox = new(@"\b(beta|sandbox)\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>The feed has no description, so name and version are all there is to read.</summary>
        public static bool IsNineBOnly(GameBananaMod mod) => IsNineBOnly(mod.Name + " " + mod.Version);

        public static bool IsNineBOnly(string text) =>
            NineB.IsMatch(text) && !BetaOrSandbox.IsMatch(text);

        public static string KindFor(string? rootCategory) => (rootCategory ?? "").Trim().ToLowerInvariant() switch
        {
            "characters" => "character",
            "maps" => "stage",
            "sounds" or "music" => "music",
            _ => "mod"
        };
    }
}
