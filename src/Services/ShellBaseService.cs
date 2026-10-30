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
        public const string BuildInfoFile = "shell-base-build.json";
        public const string DefaultVanillaFolder = @"C:\Program Files (x86)\Super Smash Flash 2 Beta";
        /// <summary>sha256 of <c>SSF2.swf</c> in the 64-bit installer the patches were captured on.</summary>
        public const string Vanilla1401Sha256 = "9afefb865f540eba5c26549f47c6892cdfbb1802ac93aa55723462c469eaacf4";

        public sealed record VanillaBuild(string Id, string Label, string Sha256);

        public static readonly VanillaBuild[] KnownBuilds =
        {
            new("installer64", "64-bit installer", "9afefb865f540eba5c26549f47c6892cdfbb1802ac93aa55723462c469eaacf4"),
            new("portable64", "64-bit portable", "d37bfe824779757d135333e45e9ceeea9eece1ad055094ca18f7b13fb5fe8bea"),
            new("installer32", "32-bit installer", "0f13237a19bc6a7f63acb8e7371f3ce8420516292099c279061f4ad84f488fdc"),
            new("portable32", "32-bit portable", "ebf175c88bc37b46b0c1a34c368eae6d529d0a50f7e8ada85fdefd874f5b17c5"),
        };

        public static VanillaBuild? BuildForHash(string? sha) =>
            KnownBuilds.FirstOrDefault(b => string.Equals(b.Sha256, sha, StringComparison.OrdinalIgnoreCase));

        public enum VanillaCheck { Missing, WrongVersion, Ok }

        /// <summary>Which known 1.4.0.1 build <paramref name="path"/> is, or null.</summary>
        public static VanillaBuild? Identify(string? path)
        {
            if (!IsVanillaFolder(path))
                return null;
            try
            {
                return BuildForHash(Sha256File(Path.Combine(path!, "SSF2.swf")));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[setup] vanilla identify failed: " + ex.Message);
                return null;
            }
        }

        /// <summary>Whether <paramref name="path"/> holds a known SSF2 Beta 1.4.0.1 build, by the <c>SSF2.swf</c> hash.</summary>
        public static VanillaCheck CheckVanilla(string? path)
        {
            if (!IsVanillaFolder(path))
                return VanillaCheck.Missing;
            var build = Identify(path);
            DebugLogger.Log($"[setup] vanilla check ok={(build != null ? 1 : 0)} id={build?.Id ?? "none"} path={path}");
            return build != null ? VanillaCheck.Ok : VanillaCheck.WrongVersion;
        }

        public sealed record ReleaseAsset(long Id, string Name, long Size);

        public sealed record Release(string Version, ReleaseAsset Core, ReleaseAsset Assets, string AssetsId);

        public static string CacheDirectory => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SSF2ModManager", "shellbase");

        /// <summary>Latest release with both .ssfpatch files, or null when there is none.</summary>
        public static async Task<Release?> GetLatestAsync(
            bool includePrereleases = false,
            CancellationToken ct = default)
        {
            using var http = CreateClient("application/vnd.github+json");
            using var doc = await GitHubReleaseChannel.FetchNewestDocumentAsync(http, Repo, includePrereleases, ct);
            if (doc == null)
            {
                DebugLogger.Log($"[shellbase] latest release missing token={(Token() != null ? 1 : 0)} prerelease={(includePrereleases ? 1 : 0)}");
                return null;
            }
            return ParseRelease(doc.RootElement);
        }

        public static Release? ParseRelease(JsonElement root)
        {
            ReleaseAsset? core = null, assets = null;
            if (!root.TryGetProperty("assets", out var assetArr) || assetArr.ValueKind != JsonValueKind.Array)
            {
                DebugLogger.Log("[shellbase] latest release missing assets array");
                return null;
            }
            foreach (var a in assetArr.EnumerateArray())
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

        /// <summary>A Shell built from source (version "dev") is maintained by hand, never rebuilt from a release.
        /// <c>expansions/decompiled</c> is a Make Mods folder and does not mean that.</summary>
        public static bool IsDevShell(string shellRoot) =>
            string.Equals(InstalledVersion(shellRoot), "dev", StringComparison.OrdinalIgnoreCase);

        public static bool IsVanillaFolder(string? path) =>
            !string.IsNullOrWhiteSpace(path) && File.Exists(Path.Combine(path, "SSF2.swf"));

        /// <summary>Download, build, and copy the result into <paramref name="shellRoot"/>. Existing mods, state and
        /// expansions stay, because the build output only overwrites the files it contains.</summary>
        public static async Task BuildAsync(Release release, string vanillaRoot, string shellRoot,
            IProgress<string> progress, CancellationToken ct = default, bool allowVanillaMismatch = false)
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

            progress.Report(SetupService.Say(40, "Making sure the tools are installed."));
            await Next2FlashSetup.EnsureAsync(ct, includePrereleases: new SettingsService().GetExperimentalBranch());
            var out_ = Path.Combine(CacheDirectory, "out");
            if (Directory.Exists(out_))
                Directory.Delete(out_, recursive: true);

            progress.Report(SetupService.Say(48, "Building your copy of the game. This takes about a minute."));
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
            if (allowVanillaMismatch)
                psi.ArgumentList.Add("--allow-vanilla-mismatch");
            using var proc = Process.Start(psi) ?? throw new InvalidOperationException("Could not start python.");
            string? last = null;
            var buildPct = 48;
            void OnLine(string? line)
            {
                if (string.IsNullOrWhiteSpace(line)) return;
                last = line;
                DebugLogger.Log(line.StartsWith("[shellbase]") ? line : "[shellbase] " + line);
                var friendly = FriendlyBuildLine(line);
                if (friendly == null) return;
                buildPct = Math.Min(78, buildPct + 4);
                progress.Report(SetupService.Say(buildPct, friendly));
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

            progress.Report(SetupService.Say(80, "Putting the game in its folder."));
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

        private static string? FriendlyBuildLine(string line)
        {
            if (line.Contains("vanilla base", StringComparison.OrdinalIgnoreCase))
                return "Reading your SSF2 install.";
            if (line.Contains("engine2 source", StringComparison.OrdinalIgnoreCase))
                return "Adding the shell's changes to the game.";
            if (line.Contains("engine2.swf", StringComparison.OrdinalIgnoreCase))
                return "Building the game.";
            if (line.Contains("AIR runtime", StringComparison.OrdinalIgnoreCase))
                return "Copying what the game needs to start.";
            if (line.Contains("shell files", StringComparison.OrdinalIgnoreCase))
                return "Copying the shell.";
            if (line.Contains("data copied", StringComparison.OrdinalIgnoreCase))
                return "Copying characters, stages, and music.";
            if (line.Contains("mappings", StringComparison.OrdinalIgnoreCase))
                return "Reading the character and stage list.";
            if (line.Contains("build OK", StringComparison.OrdinalIgnoreCase))
                return "The game files are ready.";
            return null;
        }

        private static async Task DownloadIfMissingAsync(ReleaseAsset asset, string path, IProgress<string> progress,
            CancellationToken ct)
        {
            if (File.Exists(path) && new FileInfo(path).Length == asset.Size)
            {
                DebugLogger.Log($"[shellbase] cached {asset.Name}");
                return;
            }
            progress.Report(SetupService.Say(12, "Downloading SSF2 Shell. This file is large."));
            using var http = CreateClient("application/octet-stream");
            http.Timeout = TimeSpan.FromMinutes(30);
            using var response = await http.GetAsync(
                $"https://api.github.com/repos/{Repo}/releases/assets/{asset.Id}", HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var tmp = path + ".part";
            var total = response.Content.Headers.ContentLength ?? asset.Size;
            await using (var stream = await response.Content.ReadAsStreamAsync(ct))
            await using (var file = File.Create(tmp))
            {
                var buf = new byte[1024 * 128];
                long read = 0;
                int n;
                while ((n = await stream.ReadAsync(buf, ct)) > 0)
                {
                    await file.WriteAsync(buf.AsMemory(0, n), ct);
                    read += n;
                    var pct = total > 0 ? 12 + (int)(24.0 * read / total) : 20;
                    progress.Report(SetupService.Say(pct, "Downloading SSF2 Shell… " + (read / (1024 * 1024)) + " MB"));
                }
            }
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
