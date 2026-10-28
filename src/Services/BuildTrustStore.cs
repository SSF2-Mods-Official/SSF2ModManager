using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Hashes the manager has already accepted. Stored outside the build so a mod
    /// folder cannot replace the list. An entry counts only while the file's size,
    /// write time, and sha256 still match.
    /// </summary>
    public static class BuildTrustStore
    {
        public static string? FilePath { get; set; }

        public static string TrustPath =>
            FilePath ?? Path.Combine(AppPaths.AppDataDir, "build-trust.json");

        public readonly struct Entry
        {
            public Entry(string sha256, long length, long unixSeconds, string rules)
            {
                Sha256 = sha256;
                Length = length;
                UnixSeconds = unixSeconds;
                Rules = rules;
            }

            public string Sha256 { get; }
            public long Length { get; }
            public long UnixSeconds { get; }
            public string Rules { get; }
        }

        public static bool TryGet(string buildRoot, string relative, out Entry entry)
        {
            entry = default;
            var files = Read();
            if (files[Key(buildRoot, relative)] is not JObject row)
                return false;
            entry = new Entry(
                row.Value<string>("sha256") ?? "",
                row.Value<long?>("length") ?? -1,
                row.Value<long?>("unix") ?? -1,
                row.Value<string>("rules") ?? "");
            return entry.Sha256.Length > 0;
        }

        public static void Remember(string buildRoot, string relative, string sha256, long length, long unixSeconds, string? rules = null)
        {
            var root = Read();
            var row = new JObject
            {
                ["sha256"] = sha256,
                ["length"] = length,
                ["unix"] = unixSeconds
            };
            if (!string.IsNullOrEmpty(rules))
                row["rules"] = rules;
            root[Key(buildRoot, relative)] = row;
            var path = TrustPath;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, new JObject { ["files"] = root }.ToString(Newtonsoft.Json.Formatting.Indented));
        }

        public static string Sha256(string path)
        {
            using var sha = SHA256.Create();
            using var file = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(file)).ToLowerInvariant();
        }

        public static long UnixSeconds(string path) =>
            new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeSeconds();

        private static JObject Read()
        {
            try
            {
                if (!File.Exists(TrustPath))
                    return new JObject();
                return JObject.Parse(File.ReadAllText(TrustPath))["files"] as JObject ?? new JObject();
            }
            catch
            {
                return new JObject();
            }
        }

        private static string Key(string buildRoot, string relative) =>
            Path.GetFullPath(buildRoot).TrimEnd('\\') + "|" + relative.Replace('\\', '/');
    }
}
