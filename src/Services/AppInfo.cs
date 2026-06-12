using System.Reflection;

namespace SSF2ModManager.Services
{
    public static class AppInfo
    {
        public const string Version = "1.0.2";
        public const string DisplayVersion = "v1.0.2";
        public const string ProductName = "SSF2 Mod Manager";
        public const string UserAgent = "SSF2ModManager/1.0.2";
        public const string GitHubRepo = "https://github.com/SSF2-Mods-Official/SSF2ModManager";
        public const string VersionCheckUrl = "https://raw.githubusercontent.com/SSF2-Mods-Official/SSF2ModManager/main/version.txt";
        public const string NewsIndexUrl = "https://raw.githubusercontent.com/SSF2-Mods-Official/SSF2ModManager/main/news-index.json";
        public const string NewsRawBaseUrl = "https://raw.githubusercontent.com/SSF2-Mods-Official/SSF2ModManager/main/src/News";

        public static string GetAssemblyVersion()
        {
            return Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? Version + ".0";
        }

        /// <summary>
        /// Compares semantic versions (1.0.0 vs 1.0.0.0 are equal).
        /// Returns 0 if equal, negative if local is older, positive if local is newer.
        /// </summary>
        public static int CompareVersions(string? local, string? remote)
        {
            var localParts = ParseVersionParts(local);
            var remoteParts = ParseVersionParts(remote);
            var len = Math.Max(localParts.Length, remoteParts.Length);
            for (var i = 0; i < len; i++)
            {
                var l = i < localParts.Length ? localParts[i] : 0;
                var r = i < remoteParts.Length ? remoteParts[i] : 0;
                if (l != r) return l.CompareTo(r);
            }
            return 0;
        }

        public static bool IsUpToDate(string? local, string? remote) => CompareVersions(local, remote) >= 0;

        private static int[] ParseVersionParts(string? version)
        {
            if (string.IsNullOrWhiteSpace(version)) return Array.Empty<int>();
            var cleaned = version.Trim().TrimStart('v', 'V');
            var main = cleaned.Split('-', '+')[0];
            return main.Split('.')
                .Select(p => int.TryParse(p, out var n) ? n : 0)
                .ToArray();
        }
    }
}
