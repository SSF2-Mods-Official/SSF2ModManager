using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Builds SSF2 Shell from the latest SSF2 Shell Base release: a core <c>shell-base-&lt;version&gt;.ssfpatch</c>
    /// and an assets <c>shell-assets-&lt;id&gt;.ssfpatch</c> (VC_redist, video), unzipped into one folder and run
    /// with <c>python tools/shellbase.py build</c> against the user's vanilla SSF2 Beta 1.4.0.1.
    /// Requests send a GitHub token when one is available, which raises the API rate limit.
    /// </summary>
    public static class ShellBaseService
    {
        public const string Repo = "SSF2-Mods-Official/SSF2-Shell-Base";
        private const string LatestApi = "https://api.github.com/repos/" + Repo + "/releases/latest";
        public const string BuildInfoFile = "shell-base-build.json";
        public const string DefaultVanillaFolder = @"C:\Program Files (x86)\Super Smash Flash 2 Beta";
        /// <summary>sha256 of <c>SSF2.swf</c> in SSF2 Beta 1.4.0.1, the only version the patches apply to.</summary>
        public const string Vanilla1401Sha256 = "9afefb865f540eba5c26549f47c6892cdfbb1802ac93aa55723462c469eaacf4";

        public enum VanillaCheck { Missing, WrongVersion, Ok }

        /// <summary>Whether <paramref name="path"/> holds SSF2 Beta 1.4.0.1, by the <c>SSF2.swf</c> hash.</summary>
        public static VanillaCheck CheckVanilla(string? path)
        {
            if (!IsVanillaFolder(path))
                return VanillaCheck.Missing;
            try
            {
                var ok = Sha256File(Path.Combine(path!, "SSF2.swf")) == Vanilla1401Sha256;
                DebugLogger.Log($"[setup] vanilla check ok={(ok ? 1 : 0)} path={path}");
                return ok ? VanillaCheck.Ok : VanillaCheck.WrongVersion;
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[setup] vanilla check failed: " + ex.Message);
                return VanillaCheck.Missing;
            }
        }

        public sealed record ReleaseAsset(long Id, string Name, long Size);

        public sealed record Release(string Version, ReleaseAsset Core, ReleaseAsset Assets, string AssetsId);

        public static string CacheDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSF2ModManager", "shellbase");

        /// <summary>Latest release with both .ssfpatch files, or null when there is none.</summary>
        public static async Task<Release?> GetLatestAsync(CancellationToken ct = default)
        {
            using var http = CreateClient("application/vnd.github+json");
            using var response = await http.GetAsync(LatestApi, ct);
            if (!response.IsSuccessStatusCode)
            {
                DebugLogger.Log($"[shellbase] latest release HTTP {(int)response.StatusCode} token={(Token() != null ? 1 : 0)}");
                return null;
            }
            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);
            ReleaseAsset? core = null, assets = null;
            foreach (var a in doc.RootElement.GetProperty("assets").EnumerateArray())
            {
                var asset = new ReleaseAsset(a.GetProperty("id").GetInt64(), a.GetProperty("name").GetString() ?? "",
                    a.GetProperty("size").GetInt64());
                if (asset.Name.StartsWith("shell-base-", StringComparison.OrdinalIgnoreCase) && asset.Name.EndsWith(".ssfpatch"))
                    core = asset;
                else if (asset.Name.StartsWith("shell-assets-", StringComparison.OrdinalIgnoreCase) && asset.Name.EndsWith(".ssfpatch"))
                    assets = asset;
            }
            if (core == null || assets == null)
            {
                DebugLogger.Log($"[shellbase] latest release missing ssfpatch core={(core != null ? 1 : 0)} assets={(assets != null ? 1 : 0)}");
                return null;
            }
            var version = core.Name["shell-base-".Length..^".ssfpatch".Length];
            var assetsId = assets.Name["shell-assets-".Length..^".ssfpatch".Length];
            return new Release(version, core, assets, assetsId);
        }

        /// <summary>Version written by the last build into the Shell folder, or null.</summary>
        public static string? InstalledVersion(string shellRoot)
        {
            try
            {
                var path = Path.Combine(shellRoot, BuildInfoFile);
                if (!File.Exists(path))
                    return null;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                return doc.RootElement.TryGetProperty("version", out var v) ? v.GetString() : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>A Shell dev tree (decompiled engine present, or built from source as version "dev") is maintained
        /// by hand, never rebuilt from a release.</summary>
        public static bool IsDevShell(string shellRoot) =>
            Directory.Exists(Path.Combine(shellRoot, "expansions", "decompiled"))
            || string.Equals(InstalledVersion(shellRoot), "dev", StringComparison.OrdinalIgnoreCase);

        public static bool IsVanillaFolder(string? path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "SSF2.swf"));

        /// <summary>Download, build, and copy the result into <paramref name="shellRoot"/>. Existing mods, state and
        /// expansions stay, because the build output only overwrites the files it contains.</summary>
        public static async Task BuildAsync(Release release, string vanillaRoot, string shellRoot,
            IProgress<string> progress, CancellationToken ct = default)
        {
            Directory.CreateDirectory(CacheDirectory);
            var corePath = Path.Combine(CacheDirectory, release.Core.Name);
            var assetsPath = Path.Combine(CacheDirectory, release.Assets.Name);
            await DownloadIfMissingAsync(release.Core, corePath, progress, ct);
            await DownloadIfMissingAsync(release.Assets, assetsPath, progress, ct);

            var pkg = Path.Combine(CacheDirectory, "pkg-" + release.Version);
            if (Directory.Exists(pkg))
                Directory.Delete(pkg, recursive: true);
            ZipFile.ExtractToDirectory(corePath, pkg);
            ZipFile.ExtractToDirectory(assetsPath, pkg);

            using (var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(pkg, "ssfpatch.json"))))
            {
                var want = manifest.RootElement.GetProperty("vanilla_ssf2_sha256").GetString();
                var got = Sha256File(Path.Combine(vanillaRoot, "SSF2.swf"));
                DebugLogger.Log($"[shellbase] vanilla sha256 match={(got == want ? 1 : 0)}");
                if (got != want)
                    throw new InvalidOperationException(
                        "That folder is not SSF2 Beta 1.4.0.1 (SSF2.swf does not match). Pick the vanilla SSF2 Beta 1.4.0.1 install.");
            }

            progress.Report("Checking Next2Flash…");
            await Next2FlashSetup.EnsureAsync(ct);
            var out_ = Path.Combine(CacheDirectory, "out");
            if (Directory.Exists(out_))
                Directory.Delete(out_, recursive: true);

            progress.Report("Building SSF2 Shell (about a minute)…");
            var psi = new ProcessStartInfo
            {
                FileName = Next2FlashSetup.PythonExe(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = pkg,
            };
            foreach (var arg in new[] { Path.Combine(pkg, "tools", "shellbase.py"), "build", "--vanilla", vanillaRoot,
                         "--n2f", AppPaths.Next2FlashRoot!, "--out", out_ })
                psi.ArgumentList.Add(arg);
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start python.");
            string? last = null;
            void OnLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                last = line;
                DebugLogger.Log(line.StartsWith("[shellbase]") ? line : "[shellbase] " + line);
                if (line.StartsWith("[shellbase]"))
                    progress.Report(line["[shellbase] ".Length..]);
            }
            proc.OutputDataReceived += (_, e) => OnLine(e.Data);
            proc.ErrorDataReceived += (_, e) => OnLine(e.Data);
            proc.BeginOutputReadLine();
            proc.BeginErrorReadLine();
            await proc.WaitForExitAsync(ct);
            if (proc.ExitCode != 0)
            {
                DebugLogger.Log($"[shellbase] build fail exit={proc.ExitCode}");
                throw new InvalidOperationException("SSF2 Shell build failed: " + (last ?? "exit " + proc.ExitCode));
            }

            progress.Report("Installing SSF2 Shell…");
            var copied = CopyTree(out_, shellRoot);
            DebugLogger.Log($"[shellbase] build ok version={release.Version} assets={release.AssetsId} files={copied} -> {shellRoot}");
            try { Directory.Delete(pkg, recursive: true); Directory.Delete(out_, recursive: true); } catch { }
            var removed = PruneCache(CacheDirectory, release.Core.Name, release.Assets.Name);
            DebugLogger.Log($"[shellbase] cache pruned removed={removed}");
        }

        /// <summary>Delete cached release files other than <paramref name="keep"/>. Returns how many were deleted.</summary>
        public static int PruneCache(string cacheDir, params string[] keep)
        {
            var removed = 0;
            foreach (var pattern in new[] { "shell-base-*.ssfpatch", "shell-assets-*.ssfpatch" })
            {
                foreach (var f in Directory.EnumerateFiles(cacheDir, pattern))
                {
                    if (Array.IndexOf(keep, Path.GetFileName(f)) >= 0)
                        continue;
                    try
                    {
                        File.Delete(f);
                        removed++;
                    }
                    catch (IOException) { }
                    catch (UnauthorizedAccessException) { }
                }
            }
            return removed;
        }

        private static async Task DownloadIfMissingAsync(ReleaseAsset asset, string path, IProgress<string> progress,
            CancellationToken ct)
        {
            if (File.Exists(path) && new FileInfo(path).Length == asset.Size)
            {
                DebugLogger.Log($"[shellbase] cached {asset.Name}");
                return;
            }
            progress.Report($"Downloading {asset.Name} ({asset.Size / (1024 * 1024)} MB)…");
            using var http = CreateClient("application/octet-stream");
            http.Timeout = TimeSpan.FromMinutes(30);
            using var response = await http.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases/assets/{asset.Id}", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var tmp = path + ".part";
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(tmp))
                await stream.CopyToAsync(file, ct);
            File.Move(tmp, path, overwrite: true);
            DebugLogger.Log($"[shellbase] downloaded {asset.Name} bytes={new FileInfo(path).Length}");
        }

        private static HttpClient CreateClient(string accept)
        {
            var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager");
            http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue(accept));
            var token = Token();
            if (token != null)
                http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            return http;
        }

        /// <summary>Optional GitHub token from SSF2MM_GITHUB_TOKEN, GITHUB_TOKEN or GH_TOKEN.</summary>
        private static string? Token()
        {
            foreach (var name in new[] { "SSF2MM_GITHUB_TOKEN", "GITHUB_TOKEN", "GH_TOKEN" })
            {
                var v = Environment.GetEnvironmentVariable(name);
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return null;
        }

        private static string Sha256File(string path)
        {
            using var sha = SHA256.Create();
            using var f = File.OpenRead(path);
            return Convert.ToHexString(sha.ComputeHash(f)).ToLowerInvariant();
        }

        private static int CopyTree(string src, string dest)
        {
            var n = 0;
            foreach (var f in Directory.EnumerateFiles(src, "*", SearchOption.AllDirectories))
            {
                var target = Path.Combine(dest, Path.GetRelativePath(src, f));
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                File.Copy(f, target, overwrite: true);
                n++;
            }
            return n;
        }
    }
}
