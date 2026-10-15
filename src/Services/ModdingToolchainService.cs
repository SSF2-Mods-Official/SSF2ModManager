using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Detects / installs VS Code + AS3MXML + SSF2 Stream, and points Shell settings at bundled jre/aflex_lite.
    /// </summary>
    public static class ModdingToolchainService
    {
        public const string As3mxmlExtensionId = "bowlerhatllc.vscode-as3mxml";
        public const string StreamExtensionId = "ssf2-shell.ssf2-stream";

        public sealed class Status
        {
            public string? ManagerRoot { get; init; }
            public string? ShellFolder { get; init; }
            public bool HasJre { get; init; }
            public bool HasAflexLite { get; init; }
            public bool HasAdl { get; init; }
            public bool HasAsconfigc { get; init; }
            public bool HasStreamVsix { get; init; }
            public string? VsCodePath { get; init; }
            public string? CursorPath { get; init; }
            public bool As3mxmlInstalled { get; init; }
            public bool StreamInstalled { get; init; }
            public string Summary { get; init; } = "";
        }

        public static string? FindManagerRoot()
        {
            var baseDir = AppPaths.ExeDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (File.Exists(Path.Combine(baseDir, "SSF2ModManager.exe")) ||
                Directory.Exists(Path.Combine(baseDir, "runtime", "jre")) ||
                Directory.Exists(Path.Combine(baseDir, "jre")) ||
                Directory.Exists(Path.Combine(baseDir, "SSF2 Shell")))
                return baseDir;

            var shell = ShellWorkspaceService.FindShellFolder();
            if (shell != null)
            {
                var parent = Directory.GetParent(shell);
                if (parent != null) return parent.FullName;
            }
            return baseDir;
        }

        public static string? ResolveJavaExe(string? managerRoot = null)
        {
            var bundled = AppPaths.JreJavaExe;
            if (bundled != null) return bundled;
            if (managerRoot != null)
            {
                foreach (var rel in new[]
                         {
                             Path.Combine("runtime", "jre", "bin", "java.exe"),
                             Path.Combine("jre", "bin", "java.exe")
                         })
                {
                    var path = Path.Combine(managerRoot, rel);
                    if (File.Exists(path)) return path;
                }
            }
            var env = Environment.GetEnvironmentVariable("SSF2_JAVA");
            if (!string.IsNullOrWhiteSpace(env) && File.Exists(env)) return env;
            return null;
        }

        public static string? ResolveAflexLite(string? managerRoot = null)
        {
            var lite = AppPaths.AflexLiteDirectory;
            if (lite != null) return lite;
            if (managerRoot == null) return null;
            foreach (var rel in new[] { Path.Combine("runtime", "aflex_lite"), "aflex_lite" })
            {
                var path = Path.Combine(managerRoot, rel);
                if (Directory.Exists(path)) return path;
            }
            return null;
        }

        public static string? ResolveStreamVsix(string? managerRoot = null)
        {
            var dir = AppPaths.ExtensionsDirectory;
            if (dir != null)
            {
                var vsix = Path.Combine(dir, "ssf2-stream.vsix");
                if (File.Exists(vsix)) return vsix;
            }
            if (managerRoot == null) return null;
            foreach (var rel in new[]
                     {
                         Path.Combine("runtime", "extensions", "ssf2-stream.vsix"),
                         Path.Combine("extensions", "ssf2-stream.vsix")
                     })
            {
                var path = Path.Combine(managerRoot, rel);
                if (File.Exists(path)) return path;
            }
            return null;
        }

        public static string? FindVsCodeCli()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Microsoft VS Code", "bin", "code.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    "Microsoft VS Code", "bin", "code.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Microsoft VS Code", "Code.exe"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return FindOnPath("code.cmd") ?? FindOnPath("code");
        }

        public static string? FindCursorCli()
        {
            var candidates = new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "cursor", "resources", "app", "bin", "cursor.cmd"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Programs", "Cursor", "resources", "app", "bin", "cursor.cmd"),
            };
            foreach (var c in candidates)
                if (File.Exists(c)) return c;
            return FindOnPath("cursor.cmd") ?? FindOnPath("cursor");
        }

        /// <summary>Editor chosen in Settings. vscode, cursor, or a command path.</summary>
        public static string? ResolveEditorCli()
        {
            var choice = new SettingsService().GetDebugIde();
            if (string.IsNullOrWhiteSpace(choice))
                return FindVsCodeCli() ?? FindCursorCli();
            if (choice.Equals("vscode", StringComparison.OrdinalIgnoreCase))
                return FindVsCodeCli();
            if (choice.Equals("cursor", StringComparison.OrdinalIgnoreCase))
                return FindCursorCli();
            if (File.Exists(choice))
                return choice;
            return FindVsCodeCli() ?? FindCursorCli();
        }

        private static string? FindOnPath(string name)
        {
            try
            {
                var path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
                {
                    var full = Path.Combine(dir.Trim('"'), name);
                    if (File.Exists(full)) return full;
                    if (!name.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase) &&
                        !name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        var cmd = full + ".cmd";
                        if (File.Exists(cmd)) return cmd;
                        var exe = full + ".exe";
                        if (File.Exists(exe)) return exe;
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        public static Status GetStatus()
        {
            var manager = FindManagerRoot();
            var shell = ShellWorkspaceService.FindShellFolder();
            var dev = ShellWorkspaceService.FindDevWorkspace();
            var java = ResolveJavaExe(manager);
            var lite = ResolveAflexLite(manager);
            var adl = lite != null && File.Exists(Path.Combine(lite, "bin", "adl.exe"));
            var asconfigc = dev != null && File.Exists(Path.Combine(dev, "tools", "asconfigc", "asconfigc.jar"));
            var vsix = ResolveStreamVsix(manager);
            var code = FindVsCodeCli();
            var cursor = FindCursorCli();
            var as3 = false;
            var stream = false;
            foreach (var cli in new[] { code, cursor }.Where(c => !string.IsNullOrEmpty(c)))
            {
                var list = RunCapture(cli!, "--list-extensions");
                if (list.Contains(As3mxmlExtensionId, StringComparison.OrdinalIgnoreCase)) as3 = true;
                if (list.Contains(StreamExtensionId, StringComparison.OrdinalIgnoreCase) ||
                    list.Contains("ssf2-stream", StringComparison.OrdinalIgnoreCase)) stream = true;
            }

            var lines = new List<string>
            {
                java != null ? "✓ Bundled JRE" : "✗ Bundled JRE missing (runtime\\jre\\bin\\java.exe)",
                lite != null ? "✓ aflex_lite (Next2Flash)" : "✗ aflex_lite missing — expected Next2Flash toolchain\\aflex_lite",
                adl ? "✓ adl.exe (F5)" : "✗ adl.exe missing inside Next2Flash aflex_lite",
                asconfigc ? "✓ asconfigc.jar" : "✗ asconfigc.jar missing under shell-dev\\tools\\asconfigc",
                vsix != null ? "✓ SSF2 Stream VSIX" : "✗ extensions\\ssf2-stream.vsix missing — run scripts\\deps\\package-ssf2-stream.ps1",
                code != null ? "✓ VS Code" : "○ VS Code not found",
                cursor != null ? "✓ Cursor" : "○ Cursor not found",
                as3 ? "✓ AS3MXML extension" : "○ AS3MXML not installed",
                stream ? "✓ SSF2 Stream extension" : "○ SSF2 Stream not installed",
            };

            return new Status
            {
                ManagerRoot = manager,
                ShellFolder = shell,
                HasJre = java != null,
                HasAflexLite = lite != null,
                HasAdl = adl,
                HasAsconfigc = asconfigc,
                HasStreamVsix = vsix != null,
                VsCodePath = code,
                CursorPath = cursor,
                As3mxmlInstalled = as3,
                StreamInstalled = stream,
                Summary = string.Join(Environment.NewLine, lines)
            };
        }

        public static async Task<(bool Ok, string Message)> InstallVsCodeAsync(IProgress<string>? progress = null)
        {
            progress?.Report("Installing VS Code via winget…");
            var (code, output) = await RunAsync("winget",
                "install -e --id Microsoft.VisualStudioCode --accept-package-agreements --accept-source-agreements --disable-interactivity");
            if (code == 0 || output.Contains("already installed", StringComparison.OrdinalIgnoreCase))
                return (true, "VS Code installed (or already present).");

            progress?.Report("winget failed; try installing VS Code from https://code.visualstudio.com");
            return (false, "winget install failed:\n" + Truncate(output, 800));
        }

        public static async Task<(bool Ok, string Message)> InstallExtensionsAsync(
            IProgress<string>? progress = null,
            bool skipAs3 = false,
            bool skipStream = false)
        {
            var cli = ResolveEditorCli();
            if (cli == null)
                return (false, "VS Code / Cursor CLI not found. Install VS Code first.");

            var sb = new StringBuilder();
            if (skipAs3)
            {
                sb.AppendLine("AS3MXML: already installed");
            }
            else
            {
                progress?.Report("Installing ActionScript & MXML…");
                var (c1, o1) = await RunAsync(cli, $"--install-extension {As3mxmlExtensionId} --force");
                sb.AppendLine(c1 == 0 ? "AS3MXML: OK" : "AS3MXML: " + Truncate(o1, 200));
            }

            if (skipStream && StreamVsixMatchesInstalled(cli))
            {
                sb.AppendLine("SSF2 Stream: already installed");
            }
            else
            {
                var vsix = ResolveStreamVsix();
                if (vsix != null)
                {
                    progress?.Report("Installing SSF2 Stream from VSIX…");
                    var (c2, o2) = await RunAsync(cli, $"--install-extension \"{vsix}\" --force");
                    sb.AppendLine(c2 == 0 ? "SSF2 Stream: OK" : "SSF2 Stream: " + Truncate(o2, 200));
                }
                else
                {
                    sb.AppendLine("SSF2 Stream: VSIX missing (extensions\\ssf2-stream.vsix)");
                }
            }

            return (true, sb.ToString().Trim());
        }

        /// <summary>True when the bundled VSIX is missing or not the version Cursor has installed.</summary>
        public static bool BundledStreamNeedsInstall()
        {
            var cli = ResolveEditorCli();
            if (cli == null)
                return false;
            return !StreamVsixMatchesInstalled(cli);
        }

        private static bool StreamVsixMatchesInstalled(string cli)
        {
            var vsix = ResolveStreamVsix();
            if (vsix == null)
                return true;
            var bundled = ReadVsixExtensionVersion(vsix);
            if (string.IsNullOrWhiteSpace(bundled))
                return false;
            var listed = RunCapture(cli, "--list-extensions --show-versions");
            return listed.Contains(StreamExtensionId + "@" + bundled, StringComparison.OrdinalIgnoreCase);
        }

        private static string? ReadVsixExtensionVersion(string vsixPath)
        {
            try
            {
                using var zip = ZipFile.OpenRead(vsixPath);
                var entry = zip.GetEntry("extension/package.json");
                if (entry == null)
                    return null;
                using var reader = new StreamReader(entry.Open());
                var node = JsonNode.Parse(reader.ReadToEnd()) as JsonObject;
                return node?["version"]?.GetValue<string>();
            }
            catch
            {
                return null;
            }
        }

        public static (bool Ok, string Message) WriteShellSettings()
        {
            var game = ShellWorkspaceService.FindShellFolder();
            var dev = ShellWorkspaceService.FindDevWorkspace() ?? game;
            var manager = FindManagerRoot();
            DebugLogger.Log($"[setup] shell settings workspace={dev ?? "none"} game={game ?? "none"}");
            if (dev == null)
                return (false, "Build SSF2 Shell first. Its folder holds the editor settings.");

            var java = ResolveJavaExe(manager);
            var sdk = ResolveAflexLite(manager);
            if (java == null || sdk == null)
                return (false, "Java or the AIR SDK was not found. Install Next2Flash first.");

            var settingsPath = Path.Combine(dev, ".vscode", "settings.json");
            Directory.CreateDirectory(Path.GetDirectoryName(settingsPath)!);

            JsonObject root;
            if (File.Exists(settingsPath))
            {
                try
                {
                    root = JsonNode.Parse(File.ReadAllText(settingsPath)) as JsonObject ?? new JsonObject();
                }
                catch
                {
                    root = new JsonObject();
                }
            }
            else root = new JsonObject();

            root["as3mxml.java.path"] = java;
            root["as3mxml.sdk.framework"] = sdk;
            root["ssf2Stream.debugFolder"] = game ?? dev;
            root["ssf2Stream.launchOnVscStartup"] = true;
            root["ssf2Stream.autoOpenOnDebug"] = true;

            var json = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json + Environment.NewLine);
            return (true, "Wrote " + settingsPath);
        }

        public static async Task<(bool Ok, string Message)> SetupModdingToolsAsync(IProgress<string>? progress = null)
        {
            var status = GetStatus();
            if (status.VsCodePath == null && status.CursorPath == null)
            {
                var (okVs, msgVs) = await InstallVsCodeAsync(progress);
                if (!okVs) return (false, msgVs);
            }

            var (okExt, msgExt) = await InstallExtensionsAsync(
                progress,
                skipAs3: status.As3mxmlInstalled,
                skipStream: status.StreamInstalled);
            progress?.Report(msgExt);
            var (okSet, msgSet) = WriteShellSettings();
            if (!okSet) return (false, msgSet);
            return (true, "Modding tools ready.\n" + msgExt + "\n" + msgSet);
        }

        public static (bool Ok, string Message) OpenShellInEditor()
        {
            var dev = ShellWorkspaceService.FindDevWorkspace() ?? ShellWorkspaceService.FindShellFolder();
            if (dev == null) return (false, "SSF2 Shell folder not found.");
            var cli = ResolveEditorCli();
            if (cli == null) return (false, "VS Code / Cursor not found. Pick one in Settings.");
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = $"\"{dev}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return (true, "Opening " + dev);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        /// <summary>
        /// Opens shell-dev in one editor window and runs an SSF2 Stream command there.
        /// A second process without -r used to open a welcome window beside shell-dev.
        /// </summary>
        public static async Task<(bool Ok, string Message)> OpenShellAndRunStreamCommandAsync(string commandId)
        {
            WriteShellSettings();
            var dev = ShellWorkspaceService.FindDevWorkspace() ?? ShellWorkspaceService.FindShellFolder();
            if (dev == null) return (false, "SSF2 Shell folder not found.");
            var cli = ResolveEditorCli();
            if (cli == null) return (false, "VS Code / Cursor not found. Pick one in Settings.");

            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = $"-r \"{dev}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }

            await Task.Delay(2500);
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = cli,
                    Arguments = $"-r --command {commandId}",
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                return (true, "Running " + commandId);
            }
            catch (Exception ex)
            {
                return (false, ex.Message);
            }
        }

        private static string RunCapture(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return "";
                var stdout = p.StandardOutput.ReadToEnd();
                var stderr = p.StandardError.ReadToEnd();
                p.WaitForExit(60000);
                return stdout + stderr;
            }
            catch { return ""; }
        }

        private static async Task<(int Code, string Output)> RunAsync(string fileName, string args)
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = args,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p == null) return (-1, "Failed to start " + fileName);
                var stdout = await p.StandardOutput.ReadToEndAsync();
                var stderr = await p.StandardError.ReadToEndAsync();
                await p.WaitForExitAsync();
                return (p.ExitCode, stdout + stderr);
            }
            catch (Exception ex)
            {
                return (-1, ex.Message);
            }
        }

        private static string Truncate(string s, int max) =>
            string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "…");
    }
}
