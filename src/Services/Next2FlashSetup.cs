using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Finds a Next2Flash checkout or release, or downloads the latest GitHub release.
    /// JPEXS, decompile, and asconfig go through <c>n2f.py</c>.
    /// </summary>
    public static class Next2FlashSetup
    {
        private const string ReleaseApi =
            "https://api.github.com/repos/SSF2-Mods-Official/Next2Flash/releases/latest";

        public static string InstallDirectory
        {
            get
            {
                var saved = AppPaths.SavedNext2FlashFolder();
                if (!string.IsNullOrWhiteSpace(saved))
                    return saved;
                var shell = ShellWorkspaceService.FindShellFolder();
                var parent = string.IsNullOrWhiteSpace(shell) ? null : Directory.GetParent(shell)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent))
                    return Path.Combine(parent, "Next2Flash");
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "SSF2ModManager",
                    "Next2Flash");
            }
        }

        /// <summary>Download the latest release when Next2Flash is missing, or when <paramref name="update"/> is set and
        /// the install is the downloaded copy. A source checkout is never replaced.</summary>
        public static async Task EnsureAsync(CancellationToken cancellationToken = default, bool update = false)
        {
            var root = AppPaths.Next2FlashRoot;
            if (root != null)
            {
                if (!update)
                    return;
                if (Directory.Exists(Path.Combine(root, ".git")) || File.Exists(Path.Combine(root, "app", "n2f.py")))
                    throw new InvalidOperationException(
                        "This Next2Flash is a source checkout (" + root + "). Update it with git pull.");
            }

            var target = root ?? InstallDirectory;
            DebugLogger.Log($"[Next2Flash] downloading the latest release update={(update ? 1 : 0)} target={target}");
            Directory.CreateDirectory(target);
            string? zipUrl = await FindZipAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(zipUrl))
            {
                throw new InvalidOperationException(
                    "Next2Flash is not installed, and the GitHub release has no zip yet. " +
                    "Install it from https://github.com/SSF2-Mods-Official/Next2Flash/releases");
            }

            var zipPath = Path.Combine(Path.GetTempPath(), "Next2Flash-release.zip");
            using (var http = new HttpClient())
            {
                http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager");
                http.Timeout = TimeSpan.FromMinutes(20);
                using var response = await http.GetAsync(zipUrl, cancellationToken);
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(zipPath);
                await stream.CopyToAsync(file, cancellationToken);
            }

            if (Directory.Exists(target))
            {
                var occupied = Directory.EnumerateFileSystemEntries(target).Any();
                if (occupied && !AppPaths.IsNext2FlashRoot(target))
                {
                    throw new InvalidOperationException(
                        "The Next2Flash folder is not empty: " + target);
                }
                Directory.Delete(target, recursive: true);
            }
            Directory.CreateDirectory(target);
            ZipFile.ExtractToDirectory(zipPath, target);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            if (AppPaths.Next2FlashRoot == null)
            {
                throw new InvalidOperationException(
                    "Downloaded Next2Flash, but n2f.py was not in the zip. " +
                    "See https://github.com/SSF2-Mods-Official/Next2Flash/releases");
            }
            DebugLogger.Log("[Next2Flash] installed at " + AppPaths.Next2FlashRoot);
        }

        public static ProcessStartInfo CreateRaw(string argumentTail, bool redirectInput)
        {
            var root = AppPaths.Next2FlashRoot
                ?? throw new InvalidOperationException("Next2Flash is not installed.");
            string python;
            string script;
            var releasePython = Path.Combine(root, "runtime", "resources", "python", "python.exe");
            var releaseScript = Path.Combine(root, "runtime", "resources", "app", "n2f.py");
            if (File.Exists(releasePython) && File.Exists(releaseScript))
            {
                python = releasePython;
                script = releaseScript;
            }
            else
            {
                script = Path.Combine(root, "app", "n2f.py");
                python = FindPython()
                    ?? throw new InvalidOperationException(
                        "Next2Flash is a source checkout and python.exe was not found on PATH.");
            }

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
            string python;
            string script;
            var releasePython = Path.Combine(root, "runtime", "resources", "python", "python.exe");
            var releaseScript = Path.Combine(root, "runtime", "resources", "app", "n2f.py");
            if (File.Exists(releasePython) && File.Exists(releaseScript))
            {
                python = releasePython;
                script = releaseScript;
            }
            else
            {
                script = Path.Combine(root, "app", "n2f.py");
                python = FindPython()
                    ?? throw new InvalidOperationException(
                        "Next2Flash is a source checkout and python.exe was not found on PATH.");
            }

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

        /// <summary>Python that runs Next2Flash: the release's bundled python, else python on PATH.</summary>
        public static string PythonExe()
        {
            var root = AppPaths.Next2FlashRoot
                ?? throw new InvalidOperationException("Next2Flash is not installed.");
            var releasePython = Path.Combine(root, "runtime", "resources", "python", "python.exe");
            if (File.Exists(releasePython))
                return releasePython;
            return FindPython()
                ?? throw new InvalidOperationException("Next2Flash is a source checkout and python.exe was not found on PATH.");
        }

        private static string? FindPython()
        {
            var path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
            {
                var exe = Path.Combine(dir.Trim(), "python.exe");
                if (File.Exists(exe))
                    return exe;
            }
            return null;
        }

        private static async Task<string?> FindZipAsync(CancellationToken cancellationToken)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager");
            using var response = await http.GetAsync(ReleaseApi, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!doc.RootElement.TryGetProperty("assets", out var assets))
                return null;
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (!name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    continue;
                return asset.GetProperty("browser_download_url").GetString();
            }
            return null;
        }
    }
}
