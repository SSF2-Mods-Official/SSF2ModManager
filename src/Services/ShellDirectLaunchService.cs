using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Writes a one-shot launch command that the Shell <c>manager_bridge</c> pack reads after boot
    /// so Mod Manager can open Character Select / Stage Select without patching engine2.
    /// </summary>
    public static class ShellDirectLaunchService
    {
        public const string RelativePath = @"data\ssf2mm\state\launch.json";
        public const string ScreenHome = "home";
        public const string ScreenCss = "css";
        public const string ScreenSss = "sss";
        public const string ScreenSssEdit = "sssedit";
        public const string ScreenJukebox = "jukebox";
        public const string ScreenCssEdit = "cssedit";
        public const string ScreenVaultCostumes = "vault";

        public static string GetLaunchFilePath(string installRoot) =>
            Path.Combine(installRoot, RelativePath);

        public static void WriteCommand(string installRoot, string screen, IDictionary<string, object?>? extra = null)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                throw new ArgumentException("Install root is required.", nameof(installRoot));
            var path = GetLaunchFilePath(installRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var payload = new Dictionary<string, object?>
            {
                ["screen"] = string.IsNullOrWhiteSpace(screen) ? ScreenHome : screen.Trim().ToLowerInvariant(),
                ["writtenUtc"] = DateTime.UtcNow.ToString("o")
            };
            if (extra != null)
            {
                foreach (var pair in extra)
                    payload[pair.Key] = pair.Value;
            }
            File.WriteAllText(path, JsonConvert.SerializeObject(payload, Formatting.Indented));
        }

        public static void ClearCommand(string installRoot)
        {
            try
            {
                var path = GetLaunchFilePath(installRoot);
                if (File.Exists(path)) File.Delete(path);
            }
            catch { /* ignore */ }
        }

        public static Process? Launch(string installRoot, string? screen = null, IDictionary<string, object?>? extra = null)
        {
            if (!string.IsNullOrWhiteSpace(screen)
                && !string.Equals(screen, ScreenHome, StringComparison.OrdinalIgnoreCase))
            {
                WriteCommand(installRoot, screen, extra);
            }
            else
            {
                ClearCommand(installRoot);
            }

            if (ShellConfigureLaunchService.IsGameRunning())
            {
                var live = string.IsNullOrWhiteSpace(screen) ? ScreenHome : screen.Trim().ToLowerInvariant();
                if (!string.Equals(live, ScreenHome, StringComparison.OrdinalIgnoreCase))
                {
                    var payload = new JObject
                    {
                        ["op"] = "applyLaunch",
                        ["screen"] = live
                    };
                    if (extra != null)
                    {
                        foreach (var pair in extra)
                        {
                            if (pair.Value != null)
                                payload[pair.Key] = JToken.FromObject(pair.Value);
                        }
                    }
                    ShellConfigureLaunchService.WriteCmd(installRoot, payload);
                }
                ShellConfigureLaunchService.FocusGameWindow();
                return null;
            }

            var exe = ShellWorkspaceService.FindGameExecutable(installRoot)
                      ?? throw new FileNotFoundException(
                          "Could not find Super Smash Flash 2 Beta.exe in the SSF2 Shell build folder.",
                          installRoot);

            try { CreditsService.Update(installRoot); } catch { }
            try
            {
                var dropped = MappingsEditService.DropMissingFiles(installRoot);
                if (dropped.Count > 0)
                    DebugLogger.Log("[mappings] dropped missing " + string.Join(",", dropped));
            }
            catch { }

            return Process.Start(new ProcessStartInfo(exe)
            {
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exe) ?? installRoot
            });
        }
    }
}
