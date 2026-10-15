using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Checks GitHub Releases and downloads newer Mod Manager assets into the install folder.
    /// </summary>
    public static class LauncherUpdateService
    {
        private static readonly HttpClient Http = CreateClient();

        public const string ReleasesApiLatest =
            "https://api.github.com/repos/SSF2-Mods-Official/SSF2ModManager/releases/latest";

        public const string ReleasesLatestPage =
            "https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/latest";

        private static HttpClient CreateClient()
        {
            var c = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
            c.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            c.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            return c;
        }

        public sealed class ReleaseInfo
        {
            public string TagName { get; init; } = "";
            public string Name { get; init; } = "";
            public string HtmlUrl { get; init; } = "";
            public List<ReleaseAsset> Assets { get; init; } = new();
        }

        public sealed class ReleaseAsset
        {
            public string Name { get; init; } = "";
            public string BrowserDownloadUrl { get; init; } = "";
            public long Size { get; init; }
        }

        public sealed class UpdateResult
        {
            public bool Ok { get; init; }
            public bool UpToDate { get; init; }
            public string Message { get; init; } = "";
            public string? RemoteVersion { get; init; }
            public string? DownloadedPath { get; init; }
        }

        public static async Task<ReleaseInfo?> FetchLatestAsync(CancellationToken ct = default)
        {
            using var resp = await Http.GetAsync(ReleasesApiLatest, ct);
            if (!resp.IsSuccessStatusCode) return null;
            await using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            var root = doc.RootElement;
            var info = new ReleaseInfo
            {
                TagName = root.TryGetProperty("tag_name", out var t) ? t.GetString() ?? "" : "",
                Name = root.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                HtmlUrl = root.TryGetProperty("html_url", out var u) ? u.GetString() ?? ReleasesLatestPage : ReleasesLatestPage,
            };
            if (root.TryGetProperty("assets", out var assets) && assets.ValueKind == JsonValueKind.Array)
            {
                foreach (var a in assets.EnumerateArray())
                {
                    var name = a.TryGetProperty("name", out var an) ? an.GetString() ?? "" : "";
                    var url = a.TryGetProperty("browser_download_url", out var au) ? au.GetString() ?? "" : "";
                    var size = a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0;
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(url)) continue;
                    if (name.Contains("source", StringComparison.OrdinalIgnoreCase)) continue;
                    info.Assets.Add(new ReleaseAsset { Name = name, BrowserDownloadUrl = url, Size = size });
                }
            }
            return info;
        }

        public static string NormalizeVersion(string? tag) =>
            (tag ?? "").Trim().TrimStart('v', 'V');

        /// <summary>
        /// Check and optionally download/extract release zip assets into <paramref name="installRoot"/>.
        /// </summary>
        public static async Task<UpdateResult> CheckAndUpdateAsync(
            string installRoot,
            bool download,
            IProgress<(string status, double? fraction)>? progress = null,
            CancellationToken ct = default)
        {
            progress?.Report(("Checking for updates…", null));
            ReleaseInfo? release;
            try
            {
                release = await FetchLatestAsync(ct);
            }
            catch (Exception ex)
            {
                return new UpdateResult { Ok = false, Message = "Could not reach GitHub: " + ex.Message };
            }

            if (release == null || string.IsNullOrWhiteSpace(release.TagName))
                return new UpdateResult { Ok = false, Message = "No release info from GitHub." };

            var remote = NormalizeVersion(release.TagName);
            var local = NormalizeVersion(AppInfo.Version);
            if (AppInfo.IsUpToDate(local, remote))
            {
                progress?.Report(("Up to date", 1));
                return new UpdateResult
                {
                    Ok = true,
                    UpToDate = true,
                    RemoteVersion = remote,
                    Message = $"Up to date ({AppInfo.DisplayVersion})"
                };
            }

            if (!download)
            {
                progress?.Report(($"Update available: v{remote}", null));
                return new UpdateResult
                {
                    Ok = true,
                    UpToDate = false,
                    RemoteVersion = remote,
                    Message = $"Update available: v{remote}"
                };
            }

            var zips = release.Assets
                .Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (zips.Count == 0)
            {
                // No zip — open releases page instead of failing silently
                try
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(release.HtmlUrl)
                    {
                        UseShellExecute = true
                    });
                }
                catch { /* ignore */ }
                return new UpdateResult
                {
                    Ok = true,
                    UpToDate = false,
                    RemoteVersion = remote,
                    Message = $"Update v{remote} available — opened releases page (no zip asset)."
                };
            }

            Directory.CreateDirectory(installRoot);
            var updatesDir = Path.Combine(installRoot, "updates");
            Directory.CreateDirectory(updatesDir);

            string? lastPath = null;
            for (var i = 0; i < zips.Count; i++)
            {
                var asset = zips[i];
                var dest = Path.Combine(updatesDir, asset.Name);
                var needDownload = !File.Exists(dest) || new FileInfo(dest).Length != asset.Size;
                if (needDownload)
                {
                    progress?.Report(($"Downloading {asset.Name}…", (double)i / zips.Count));
                    await DownloadFileAsync(asset.BrowserDownloadUrl, dest, progress, i, zips.Count, ct);
                }
                else
                {
                    progress?.Report(($"Already have {asset.Name}", (double)(i + 1) / zips.Count));
                }

                progress?.Report(($"Extracting {asset.Name}…", (double)(i + 0.5) / zips.Count));
                TryExtractZip(dest, installRoot);
                lastPath = dest;
            }

            progress?.Report(("Update ready", 1));
            return new UpdateResult
            {
                Ok = true,
                UpToDate = false,
                RemoteVersion = remote,
                DownloadedPath = lastPath,
                Message = $"Downloaded v{remote}. Restart the launcher if the exe was updated."
            };
        }

        private static async Task DownloadFileAsync(
            string url,
            string destPath,
            IProgress<(string status, double? fraction)>? progress,
            int index,
            int total,
            CancellationToken ct)
        {
            using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            resp.EnsureSuccessStatusCode();
            var totalBytes = resp.Content.Headers.ContentLength ?? -1;
            await using var input = await resp.Content.ReadAsStreamAsync(ct);
            var tmp = destPath + ".partial";
            await using (var output = File.Create(tmp))
            {
                var buffer = new byte[81920];
                long read = 0;
                int n;
                while ((n = await input.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, n), ct);
                    read += n;
                    if (totalBytes > 0)
                    {
                        var local = (double)read / totalBytes;
                        var overall = (index + local) / total;
                        progress?.Report(($"Downloading… {read / 1024 / 1024} MB", overall));
                    }
                }
            }
            if (File.Exists(destPath)) File.Delete(destPath);
            File.Move(tmp, destPath);
        }

        private static void TryExtractZip(string zipPath, string installRoot)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue; // directory
                var relative = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                // Strip a single top-level folder if the zip wraps everything
                relative = StripSingleRoot(relative, zip);
                var dest = Path.GetFullPath(Path.Combine(installRoot, relative));
                if (!dest.StartsWith(Path.GetFullPath(installRoot), StringComparison.OrdinalIgnoreCase))
                    continue;
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                try
                {
                    entry.ExtractToFile(dest, overwrite: true);
                }
                catch (IOException)
                {
                    // Locked exe — write sidecar for next launch
                    try { entry.ExtractToFile(dest + ".new", overwrite: true); } catch { /* ignore */ }
                }
            }
        }

        private static string StripSingleRoot(string relative, ZipArchive zip)
        {
            var roots = zip.Entries
                .Select(e => e.FullName.Replace('\\', '/').Split('/')[0])
                .Where(s => !string.IsNullOrEmpty(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (roots.Count != 1) return relative;
            var root = roots[0] + Path.DirectorySeparatorChar;
            var norm = relative.Replace('/', Path.DirectorySeparatorChar);
            if (norm.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                return norm.Substring(root.Length);
            return relative;
        }
    }
}
