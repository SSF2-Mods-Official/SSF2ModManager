using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Compares a shell folder to the file list carried in <c>SSF2.swf</c>, and scans
    /// expansion <c>mod.swf</c> files that are not already trusted.
    /// </summary>
    public static class BuildTrust
    {
        public const string MarkerVanilla = "SSF2MM-VANILLA";
        public const string MarkerShell = "SSF2MM-SHELL";
        public const string DevFileName = "dev-build";

        public sealed class Report
        {
            public bool Dev { get; init; }
            public bool BooterOk { get; init; }
            public List<string> HashCaution { get; } = new();
            public List<string> DoNotRun { get; } = new();
        }

        public static bool DevSkip() =>
            File.Exists(Path.Combine(AppPaths.AppDataDir, DevFileName));

        public static Report Check(string installRoot)
        {
            var dev = DevSkip();
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
            {
                var missing = new Report { Dev = dev, BooterOk = false };
                missing.HashCaution.Add("missing");
                return missing;
            }

            var swfPath = Path.Combine(installRoot, "SSF2.swf");
            var booterExists = File.Exists(swfPath);
            var booterHash = booterExists ? HashFile(swfPath) : "";
            var report = new Report
            {
                Dev = dev,
                BooterOk = booterExists && (dev || string.Equals(booterHash, ShellSwfGuard.BooterSha256, StringComparison.OrdinalIgnoreCase))
            };
            if (!report.BooterOk)
                report.HashCaution.Add("SSF2.swf");

            var lists = LoadLists(swfPath, dev);
            if (!dev)
                CompareList(installRoot, lists.Shell, report);
            ScanExpansions(installRoot, report);
            DebugLogger.Log($"[shell] trust booter={(report.BooterOk ? 1 : 0)} caution={report.HashCaution.Count} block={report.DoNotRun.Count} dev={(dev ? 1 : 0)}");
            return report;
        }

        public static (Dictionary<string, string> Vanilla, Dictionary<string, string> Shell) Parse(string text)
        {
            var vanilla = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var shell = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var which = vanilla;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0)
                    continue;
                if (line == MarkerVanilla)
                {
                    which = vanilla;
                    continue;
                }
                if (line == MarkerShell)
                {
                    which = shell;
                    continue;
                }
                var space = line.LastIndexOf(' ');
                if (space <= 0)
                    continue;
                which[line[..space].Replace('\\', '/')] = line[(space + 1)..].ToLowerInvariant();
            }
            return (vanilla, shell);
        }

        public static string? Kind(string relative)
        {
            var rel = relative.Replace('\\', '/');
            if (rel.Equals("SSF2.swf", StringComparison.OrdinalIgnoreCase)
                || rel.Equals("shell-base-build.json", StringComparison.OrdinalIgnoreCase))
                return null;
            if (rel.StartsWith("expansions/", StringComparison.OrdinalIgnoreCase))
                return null;
            if (rel.Equals("data/ssf2mm/game.log", StringComparison.OrdinalIgnoreCase)
                || rel.StartsWith("data/ssf2mm/state/", StringComparison.OrdinalIgnoreCase))
                return null;
            if (IsVanilla(rel))
                return "vanilla";
            return "shell";
        }

        public static bool IsVanillaFile(string rel) => IsVanilla(rel);

        public static List<string> VanillaCautionPaths(Report report)
        {
            var list = new List<string>();
            foreach (var path in report.HashCaution)
            {
                if (IsVanilla(path))
                    list.Add(path.Replace('\\', '/'));
            }
            list.Sort(StringComparer.OrdinalIgnoreCase);
            return list;
        }

        private static bool IsVanilla(string rel) =>
            rel.StartsWith("Adobe AIR/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("data/character/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("data/stage/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("data/menu/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("data/modes/", StringComparison.OrdinalIgnoreCase)
            || rel.StartsWith("data/sound/", StringComparison.OrdinalIgnoreCase)
            || (rel.StartsWith("data/misc/", StringComparison.OrdinalIgnoreCase) && !IsMappings(rel));

        private static bool IsMappings(string rel) =>
            rel.Contains("/mappings.", StringComparison.OrdinalIgnoreCase);

        private static (Dictionary<string, string> Vanilla, Dictionary<string, string> Shell) LoadLists(string swfPath, bool dev)
        {
            if (!dev && File.Exists(swfPath))
            {
                try
                {
                    var text = Encoding.UTF8.GetString(SwfApiScan.Decompress(File.ReadAllBytes(swfPath)));
                    var at = text.IndexOf(MarkerVanilla, StringComparison.Ordinal);
                    if (at >= 0)
                        return Parse(text[at..]);
                }
                catch
                {
                    /* fall through to the copy shipped with the manager */
                }
            }
            return Parse(ReadShippedLists());
        }

        private static string ReadShippedLists()
        {
            var asm = typeof(BuildTrust).Assembly;
            var name = asm.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("build-file-lists.txt", StringComparison.OrdinalIgnoreCase));
            if (name == null)
                return "";
            using var stream = asm.GetManifestResourceStream(name);
            if (stream == null)
                return "";
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static void CompareList(string root, Dictionary<string, string> expected, Report report)
        {
            foreach (var pair in expected)
            {
                var path = Path.Combine(root, pair.Key.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(path))
                {
                    report.HashCaution.Add(pair.Key);
                    continue;
                }
                var length = new FileInfo(path).Length;
                var unix = BuildTrustStore.UnixSeconds(path);
                if (BuildTrustStore.TryGet(root, pair.Key, out var cached)
                    && cached.Length == length
                    && Math.Abs(cached.UnixSeconds - unix) <= 2
                    && string.Equals(cached.Sha256, pair.Value, StringComparison.OrdinalIgnoreCase))
                    continue;
                var hash = BuildTrustStore.Sha256(path);
                if (!string.Equals(hash, pair.Value, StringComparison.OrdinalIgnoreCase))
                {
                    report.HashCaution.Add(pair.Key);
                    continue;
                }
                BuildTrustStore.Remember(root, pair.Key, hash, length, unix);
            }
        }

        private static void ScanExpansions(string root, Report report)
        {
            var expansions = Path.Combine(root, "expansions");
            if (!Directory.Exists(expansions))
                return;
            foreach (var file in Directory.EnumerateFiles(expansions, "mod.swf", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(root, file).Replace('\\', '/');
                if (rel.Contains("/ignore/", StringComparison.OrdinalIgnoreCase)
                    || rel.Contains("/_template/", StringComparison.OrdinalIgnoreCase))
                    continue;
                var length = new FileInfo(file).Length;
                var unix = BuildTrustStore.UnixSeconds(file);
                var hash = BuildTrustStore.Sha256(file);
                if (BuildTrustStore.TryGet(root, rel, out var cached)
                    && cached.Length == length
                    && string.Equals(cached.Sha256, hash, StringComparison.OrdinalIgnoreCase)
                    && string.Equals(cached.Rules, SwfApiScan.RulesId, StringComparison.Ordinal))
                    continue;
                var hits = SwfApiScan.RefusedIn(file);
                if (hits.Count > 0)
                {
                    report.DoNotRun.Add(rel + " " + string.Join(",", hits));
                    continue;
                }
                BuildTrustStore.Remember(root, rel, hash, length, unix, SwfApiScan.RulesId);
            }
        }

        private static string HashFile(string path)
        {
            try
            {
                using var sha = SHA256.Create();
                using var file = File.OpenRead(path);
                return Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant();
            }
            catch
            {
                return "";
            }
        }
    }
}
