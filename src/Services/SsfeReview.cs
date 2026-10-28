using System.IO;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public sealed class SsfeFile
    {
        public string RelativePath { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string Sha256 { get; init; } = "";
    }

    public sealed class SsfeReviewResult
    {
        public List<string> Errors { get; } = new();
        public List<SsfeFile> Files { get; } = new();
        public bool Ok => Errors.Count == 0;
    }

    /// <summary>
    /// Decides whether a mod folder may be sealed as an .ssfe. Runs only at package time.
    /// </summary>
    public static class SsfeReview
    {
        public static SsfeReviewResult Inspect(string folder)
        {
            var result = new SsfeReviewResult();
            if (!Directory.Exists(folder))
            {
                result.Errors.Add("Folder was not found.");
                return result;
            }

            var infoPath = Path.Combine(folder, "info.json");
            if (!File.Exists(infoPath))
            {
                result.Errors.Add("info.json is missing.");
            }
            else
            {
                try
                {
                    var info = JObject.Parse(File.ReadAllText(infoPath));
                    if (string.IsNullOrWhiteSpace(info["name"]?.ToString()))
                        result.Errors.Add("info.json needs a name.");
                    if (string.IsNullOrWhiteSpace(info["creator"]?.ToString()))
                        result.Errors.Add("info.json needs a creator.");
                }
                catch
                {
                    result.Errors.Add("info.json could not be read.");
                }
            }

            foreach (var file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(folder, file).Replace('\\', '/');
                if (Skip(rel))
                    continue;
                var ext = Path.GetExtension(rel);
                if (ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".rar", StringComparison.OrdinalIgnoreCase))
                {
                    result.Errors.Add("Nested archive: " + rel);
                    continue;
                }
                long ignored = 0;
                var count = 0;
                var blocked = ArchiveExtractGuard.Reject(rel, new FileInfo(file).Length, ref ignored, ref count);
                if (blocked != null)
                {
                    result.Errors.Add(blocked);
                    continue;
                }
                if (ext.Equals(".swf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        foreach (var api in SwfApiScan.RefusedIn(file))
                            result.Errors.Add(rel + " uses " + api + ".");
                    }
                    catch (Exception ex)
                    {
                        result.Errors.Add(rel + " could not be read (" + ex.Message + ").");
                    }
                }
                result.Files.Add(new SsfeFile
                {
                    RelativePath = rel,
                    FullPath = file,
                    Sha256 = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant()
                });
            }

            if (result.Files.Count == 0 && result.Errors.Count == 0)
                result.Errors.Add("Nothing to package.");
            return result;
        }

        /// <summary>Same files Package Mods leaves out, plus fixtures and rebuild scripts.</summary>
        public static bool Skip(string relative)
        {
            var norm = relative.Replace('/', '\\');
            if (norm.Contains("\\src\\", StringComparison.OrdinalIgnoreCase)
                || norm.StartsWith("src\\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (norm.Contains("\\fixture\\", StringComparison.OrdinalIgnoreCase)
                || norm.StartsWith("fixture\\", StringComparison.OrdinalIgnoreCase))
                return true;
            if (norm.Contains("\\decompiled\\", StringComparison.OrdinalIgnoreCase))
                return true;
            var name = Path.GetFileName(norm);
            if (name.Equals("build.bat", StringComparison.OrdinalIgnoreCase)
                || name.Equals("rebuild.bat", StringComparison.OrdinalIgnoreCase)
                || name.Equals("asconfig.json", StringComparison.OrdinalIgnoreCase))
                return true;
            return name.EndsWith(".ps1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
