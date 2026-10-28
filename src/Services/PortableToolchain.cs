using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Portable Python and JRE for Next2Flash / FFDec. Players never install either from PATH.
    /// </summary>
    public static class PortableToolchain
    {
        private const string PythonVersion = "3.13.3";
        private static readonly string PythonEmbedUrl =
            $"https://www.python.org/ftp/python/{PythonVersion}/python-{PythonVersion}-embed-amd64.zip";
        private const string TemurinJreApi =
            "https://api.adoptium.net/v3/binary/latest/17/ga/windows/x64/jre/hotspot/normal/eclipse?project=jdk";

        /// <summary>True when a bundled python.exe is on disk (N2F release, dist/python, or manager runtime).</summary>
        public static bool HasPython() => ResolvePythonExe() != null;

        /// <summary>True when a bundled java.exe is on disk.</summary>
        public static bool HasJre() => AppPaths.JreJavaExe != null || ResolveManagerJre() != null;

        public static string? ResolvePythonExe()
        {
            var n2f = AppPaths.Next2FlashRoot;
            if (!string.IsNullOrWhiteSpace(n2f))
            {
                foreach (var rel in new[]
                         {
                             Path.Combine("runtime", "resources", "python", "python.exe"),
                             Path.Combine("dist", "python", "python.exe"),
                         })
                {
                    var p = Path.Combine(n2f, rel);
                    if (File.Exists(p))
                        return p;
                }
            }

            return AppPaths.FindUnderSidecar("python", "python.exe");
        }

        public static string? ResolveManagerJre() =>
            AppPaths.FindUnderSidecar("jre", "bin", "java.exe");

        /// <summary>
        /// After Next2Flash is present, make sure portable Python and JRE exist.
        /// Downloads into the N2F tree when possible; otherwise into the manager <c>runtime</c> folder.
        /// </summary>
        public static async Task EnsureAsync(CancellationToken cancellationToken = default)
        {
            var n2f = AppPaths.Next2FlashRoot
                      ?? throw new InvalidOperationException("Next2Flash is not installed.");

            if (ResolvePythonExe() == null)
            {
                DebugLogger.Log("[portable] python missing — fetching embed " + PythonVersion);
                await EnsurePythonAsync(n2f, cancellationToken);
            }
            else
            {
                DebugLogger.Log("[portable] python ready " + ResolvePythonExe());
            }

            if (AppPaths.JreJavaExe == null && ResolveManagerJre() == null)
            {
                DebugLogger.Log("[portable] jre missing — fetching Temurin 17");
                await EnsureJreAsync(n2f, cancellationToken);
            }
            else
            {
                DebugLogger.Log("[portable] jre ready " + (AppPaths.JreJavaExe ?? ResolveManagerJre()));
            }

            if (ResolvePythonExe() == null)
            {
                throw new InvalidOperationException(
                    "Portable Python is still missing after download. Re-run Setup → Download Next2Flash.");
            }

            if (AppPaths.JreJavaExe == null && ResolveManagerJre() == null)
            {
                throw new InvalidOperationException(
                    "Portable JRE is still missing after download. Re-run Setup → Download Next2Flash.");
            }
        }

        private static async Task EnsurePythonAsync(string n2fRoot, CancellationToken cancellationToken)
        {
            // Prefer the release layout so CreateN2f finds it without PATH.
            var releasePy = Path.Combine(n2fRoot, "runtime", "resources", "python");
            var distPy = Path.Combine(n2fRoot, "dist", "python");
            var target = Directory.Exists(Path.Combine(n2fRoot, "runtime", "resources"))
                ? releasePy
                : distPy;

            Directory.CreateDirectory(target);
            var zipPath = Path.Combine(Path.GetTempPath(), $"python-{PythonVersion}-embed.zip");
            await DownloadAsync(PythonEmbedUrl, zipPath, cancellationToken);
            ExtractZipOverwrite(zipPath, target);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            // Enable site-packages for pip / N2F deps (same as Next2Flash publish.bat).
            var pth = Directory.GetFiles(target, "python*._pth").FirstOrDefault();
            if (pth != null)
            {
                var zipName = Path.GetFileNameWithoutExtension(pth) + ".zip";
                await File.WriteAllLinesAsync(pth, new[] { zipName, ".", @"Lib\site-packages", "import site" }, cancellationToken);
            }

            var python = Path.Combine(target, "python.exe");
            if (!File.Exists(python))
                throw new InvalidOperationException("Embed Python zip did not contain python.exe.");

            var requirements = Path.Combine(n2fRoot, "app", "requirements.txt");
            if (!File.Exists(requirements))
                requirements = Path.Combine(n2fRoot, "runtime", "resources", "app", "requirements.txt");
            if (File.Exists(requirements))
            {
                var getPip = Path.Combine(target, "get-pip.py");
                await DownloadAsync("https://bootstrap.pypa.io/get-pip.py", getPip, cancellationToken);
                await RunAsync(python, $"\"{getPip}\" --no-warn-script-location", target, cancellationToken);
                await RunAsync(python, $"-m pip install --no-warn-script-location -r \"{requirements}\"", target, cancellationToken);
                try { File.Delete(getPip); } catch { /* ignore */ }
            }

            DebugLogger.Log("[portable] python ready " + python);
        }

        private static async Task EnsureJreAsync(string n2fRoot, CancellationToken cancellationToken)
        {
            var releaseJre = Path.Combine(n2fRoot, "runtime", "resources", "toolchain", "jre");
            var sourceJre = Path.Combine(n2fRoot, "toolchain", "jre");
            var target = Directory.Exists(Path.Combine(n2fRoot, "runtime", "resources", "toolchain"))
                         || Directory.Exists(Path.Combine(n2fRoot, "runtime", "resources"))
                ? releaseJre
                : Directory.Exists(Path.Combine(n2fRoot, "toolchain"))
                    ? sourceJre
                    : Path.Combine(AppPaths.RuntimeDirectory, "jre");

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            var zipPath = Path.Combine(Path.GetTempPath(), "temurin-jre-17.zip");
            await DownloadAsync(TemurinJreApi, zipPath, cancellationToken);

            var extract = Path.Combine(Path.GetTempPath(), "temurin-jre-extract-" + Guid.NewGuid().ToString("N"));
            ExtractZipOverwrite(zipPath, extract);
            try { File.Delete(zipPath); } catch { /* ignore */ }

            var java = Directory.GetFiles(extract, "java.exe", SearchOption.AllDirectories)
                .FirstOrDefault(p => p.Replace('\\', '/').Contains("/bin/java.exe", StringComparison.OrdinalIgnoreCase));
            if (java == null)
                throw new InvalidOperationException("Temurin zip did not contain bin\\java.exe.");

            var jreHome = Directory.GetParent(Path.GetDirectoryName(java)!)!.FullName;
            if (Directory.Exists(target))
                Directory.Delete(target, recursive: true);
            CopyDirectory(jreHome, target);
            try { Directory.Delete(extract, recursive: true); } catch { /* ignore */ }

            var bundled = Path.Combine(target, "bin", "java.exe");
            if (!File.Exists(bundled))
                throw new InvalidOperationException("Portable JRE copy failed.");
            DebugLogger.Log("[portable] jre ready " + bundled);
        }

        private static async Task DownloadAsync(string url, string dest, CancellationToken cancellationToken)
        {
            using var http = new HttpClient();
            http.DefaultRequestHeaders.UserAgent.ParseAdd("SSF2ModManager");
            http.Timeout = TimeSpan.FromMinutes(20);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var file = File.Create(dest);
            await stream.CopyToAsync(file, cancellationToken);
        }

        private static void ExtractZipOverwrite(string zipPath, string dest)
        {
            Directory.CreateDirectory(dest);
            using var zip = ZipFile.OpenRead(zipPath);
            foreach (var entry in zip.Entries)
            {
                var name = entry.FullName.Replace('/', Path.DirectorySeparatorChar);
                if (string.IsNullOrEmpty(entry.Name) || name.EndsWith(Path.DirectorySeparatorChar))
                {
                    Directory.CreateDirectory(Path.Combine(dest, name));
                    continue;
                }
                var outPath = Path.Combine(dest, name);
                var parent = Path.GetDirectoryName(outPath);
                if (!string.IsNullOrEmpty(parent))
                    Directory.CreateDirectory(parent);
                entry.ExtractToFile(outPath, overwrite: true);
            }
        }

        private static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var dir in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, dir);
                Directory.CreateDirectory(Path.Combine(dest, rel));
            }
            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                var rel = Path.GetRelativePath(source, file);
                File.Copy(file, Path.Combine(dest, rel), overwrite: true);
            }
        }

        private static async Task RunAsync(string fileName, string args, string workDir, CancellationToken cancellationToken)
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                WorkingDirectory = workDir,
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Could not start " + fileName);
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            if (proc.ExitCode != 0)
                throw new InvalidOperationException($"Command failed ({proc.ExitCode}): {fileName} {args}\n{stderr}".Trim());
        }
    }
}
