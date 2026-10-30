using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Finds a Next2Flash checkout or release, or downloads the latest GitHub release.
    /// JPEXS, decompile, and asconfig go through <c>n2f.py</c> with portable Python only.
    /// </summary>
    public static class Next2FlashSetup
    {
        public const string Repo = "SSF2-Mods-Official/Next2Flash";

        public static string InstallDirectory
        {
            get
            {
                var saved = AppPaths.SavedNext2FlashFolder();
                if (!string.IsNullOrWhiteSpace(saved))
                    return saved;
                return Path.Combine(AppPaths.ExeDirectory, "Next2Flash");
            }
        }

        public static string LegacyAppDataDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "SSF2ModManager",
            "Next2Flash");

        public static bool IsSourceCheckout(string root) =>
            Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, "app", "n2f.py"));

        public static string? ReadReleaseTag(string root)
        {
            var path = Path.Combine(root, "release-tag.txt");
            if (!File.Exists(path))
                return null;
            var tag = File.ReadAllText(path).Trim();
            return tag.Length == 0 ? null : tag;
        }

        public static void WriteReleaseTag(string root, string tag)
        {
            Directory.CreateDirectory(root);
            File.WriteAllText(Path.Combine(root, "release-tag.txt"), tag.Trim());
        }

        /// <summary>Copy files whose size differs. Returns copied, skipped.</summary>
        public static (int Copied, int Skipped) CopyChanged(string source, string dest)
        {
            var copied = 0;
            var skipped = 0;
            foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dest, Path.GetRelativePath(source, file));
                if (File.Exists(target) && new FileInfo(target).Length == new FileInfo(file).Length)
                {
                    skipped++;
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(file, target, overwrite: true);
                copied++;
            }
            return (copied, skipped);
        }

        /// <summary>Download the latest release when Next2Flash is missing, or when <paramref name="update"/> is set and
        /// the install is the downloaded copy. A source checkout is never replaced. An update copies only files whose
        /// size changed and writes <c>release-tag.txt</c>. Always ensures portable Python + JRE.</summary>
        /// <summary>Copies an old AppData install next to the exe when that folder is empty.</summary>
        public static void MigrateLegacy()
        {
            var dest = Path.Combine(AppPaths.ExeDirectory, "Next2Flash");
            if (AppPaths.IsNext2FlashRoot(dest) || (Directory.Exists(dest) && IsSourceCheckout(dest)))
                return;
            var legacy = LegacyAppDataDirectory;
            if (!AppPaths.IsNext2FlashRoot(legacy) || IsSourceCheckout(legacy))
                return;
            var counts = CopyChanged(legacy, dest);
            var tag = ReadReleaseTag(legacy);
            if (tag != null)
                WriteReleaseTag(dest, tag);
            DebugLogger.Log($"[n2f] moved from AppData copied={counts.Copied} to {dest}");
        }

        public static async Task EnsureAsync(CancellationToken cancellationToken = default, bool update = false,
            IProgress<string>? progress = null, bool includePrereleases = false)
        {
            MigrateLegacy();
            var root = AppPaths.Next2FlashRoot;
            if (root != null)
            {
                if (update)
                {
                    if (IsSourceCheckout(root))
                        throw new InvalidOperationException(
                            "This Next2Flash is a source checkout (" + root + "). Update it with git pull.");
                    await DownloadReleaseAsync(root, cancellationToken, progress, includePrereleases);
                }
            }
            else
            {
                await DownloadReleaseAsync(InstallDirectory, cancellationToken, progress, includePrereleases);
            }

            if (AppPaths.Next2FlashRoot == null)
            {
                throw new InvalidOperationException(
                    "Next2Flash is not installed (n2f.py missing). " +
                    "See https://github.com/SSF2-Mods-Official/Next2Flash/releases");
            }

            await PortableToolchain.EnsureAsync(cancellationToken);
        }

        private static async Task DownloadReleaseAsync(string target, CancellationToken cancellationToken,
            IProgress<string>? progress = null, bool includePrereleases = false)
        {
            DebugLogger.Log($"[Next2Flash] downloading the latest release target={target} prerelease={(includePrereleases ? 1 : 0)}");
            var remote = await FindReleaseAsync(includePrereleases, cancellationToken);
            if (remote == null)
            {
                throw new InvalidOperationException(
                    "Next2Flash is not installed, and the GitHub release has no zip yet. " +
                    "Install it from https://github.com/SSF2-Mods-Official/Next2Flash/releases");
            }

            if (AppPaths.IsNext2FlashRoot(target)
                && string.Equals(ReadReleaseTag(target), remote.Tag, StringComparison.OrdinalIgnoreCase))
            {
                DebugLogger.Log("[n2f] update copied=0 skipped=0");
                return;
            }

            if (Directory.Exists(target))
            {
                var occupied = Directory.EnumerateFileSystemEntries(target).Any();
                if (occupied && !AppPaths.IsNext2FlashRoot(target))
                {
                    throw new InvalidOperationException(
                        "The Next2Flash folder is not empty: " + target);
                }
            }

            var zipPath = Path.Combine(Path.GetTempPath(), "Next2Flash-release.zip");
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager");
                http.Timeout = TimeSpan.FromMinutes(20);
                using var response = await http.GetAsync(remote.ZipUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength ?? 0;
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(zipPath);
                var buf = new byte[1024 * 128];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buf, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), cancellationToken);
                    read += n;
                    var pct = total > 0 ? 5 + (int)(70.0 * read / total) : 10;
                    progress?.Report(SetupService.Say(pct, "Downloading the tools… " + (read / (1024 * 1024)) + " MB"));
                }
            }

            var unpack = Path.Combine(Path.GetTempPath(), "Next2Flash-unpack-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(unpack);
            try
            {
                progress?.Report(SetupService.Say(80, "Unpacking the tools."));
                ZipFile.ExtractToDirectory(zipPath, unpack);
                var source = ReleaseRoot(unpack);
                var counts = CopyChanged(source, target);
                WriteReleaseTag(target, remote.Tag);
                DebugLogger.Log($"[n2f] update copied={counts.Copied} skipped={counts.Skipped}");
            }
            finally
            {
                try { Directory.Delete(unpack, true); } catch { /* temp */ }
                try { File.Delete(zipPath); } catch { /* ignore */ }
            }

            if (!AppPaths.IsNext2FlashRoot(target) && AppPaths.Next2FlashRoot == null)
            {
                throw new InvalidOperationException(
                    "Downloaded Next2Flash, but n2f.py was not in the zip. " +
                    "See https://github.com/SSF2-Mods-Official/Next2Flash/releases");
            }
            DebugLogger.Log("[Next2Flash] installed at " + (AppPaths.Next2FlashRoot ?? target));
        }

        private static string ReleaseRoot(string unpack)
        {
            if (AppPaths.IsNext2FlashRoot(unpack))
                return unpack;
            var dirs = Directory.GetDirectories(unpack);
            if (dirs.Length == 1 && AppPaths.IsNext2FlashRoot(dirs[0]))
                return dirs[0];
            return unpack;
        }

        private sealed record RemoteRelease(string Tag, string ZipUrl);

        public static ProcessStartInfo CreateRaw(string argumentTail, bool redirectInput)
        {
            var root = AppPaths.Next2FlashRoot
                ?? throw new InvalidOperationException("Next2Flash is not installed.");
            var (python, script) = ResolvePythonAndScript(root);
            return new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"\"{script}\" {argumentTail}",
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
        }

        public static ProcessStartInfo CreateN2f(bool redirectInput, params string[] args)
        {
            var root = AppPaths.Next2FlashRoot
                ?? throw new InvalidOperationException("Next2Flash is not installed.");
            var (python, script) = ResolvePythonAndScript(root);
            var psi = new ProcessStartInfo
            {
                FileName = python,
                UseShellExecute = false,
                RedirectStandardInput = redirectInput,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = root,
            };
            psi.ArgumentList.Add(script);
            foreach (var arg in args)
                psi.ArgumentList.Add(arg);
            return psi;
        }

        /// <summary>Portable Python only (Next2Flash release, dist/python, or manager runtime). Never PATH.</summary>
        public static string PythonExe() =>
            PortableToolchain.ResolvePythonExe()
            ?? throw new InvalidOperationException(
                "Portable Python is missing. Open Setup and download Next2Flash (it includes Python), " +
                "or wait for the manager to finish fetching the embed runtime.");

        private static (string Python, string Script) ResolvePythonAndScript(string root)
        {
            var releaseScript = Path.Combine(root, "runtime", "resources", "app", "n2f.py");
            var sourceScript = Path.Combine(root, "app", "n2f.py");
            var script = File.Exists(releaseScript) ? releaseScript
                : File.Exists(sourceScript) ? sourceScript
                : throw new InvalidOperationException("n2f.py was not found under " + root);

            var python = PortableToolchain.ResolvePythonExe()
                ?? throw new InvalidOperationException(
                    "Portable Python is missing. Open Setup and download Next2Flash.");
            return (python, script);
        }

        /// <summary>True when the downloaded copy's tag is behind the latest release. A source checkout is not behind.</summary>
        public static async Task<(bool Behind, string Detail)> UpdateStatusAsync(
            bool includePrereleases = false,
            CancellationToken cancellationToken = default)
        {
            var root = AppPaths.Next2FlashRoot;
            if (root != null && IsSourceCheckout(root))
                return (false, "source");
            var remote = await FindReleaseAsync(includePrereleases, cancellationToken);
            if (remote == null)
                return (false, "none");
            if (root == null)
                return (true, remote.Tag);
            if (!string.Equals(ReadReleaseTag(root), remote.Tag, StringComparison.OrdinalIgnoreCase))
                return (true, remote.Tag);
            return (false, remote.Tag);
        }

        private static async Task<RemoteRelease?> FindReleaseAsync(
            bool includePrereleases,
            CancellationToken cancellationToken)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd(AppInfo.UserAgent);
            using var doc = await GitHubReleaseChannel.FetchNewestDocumentAsync(
                http, Repo, includePrereleases, cancellationToken);
            if (doc == null)
                return null;
            return ParseRemoteRelease(doc.RootElement);
        }

        private static RemoteRelease? ParseRemoteRelease(JsonElement root)
        {
            var tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            if (!root.TryGetProperty("assets", out var assets))
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                var url = asset.GetProperty("browser_download_url").GetString();
                if (!string.IsNullOrWhiteSpace(url))
                    return new RemoteRelease(tag, url);
            }
            return null;
        }
    }
}
