using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Builds the palette sprite cache on this machine. The files stay in Documents.
    /// A character pack is not modified.
    /// </summary>
    public static class PaletteCacheWarmService
    {
        public const string SignalName = "palette-cache-warm.signal";
        public const string MarkerName = "palette-cache-warm.done";

        public static string MarkerPath(string shell) =>
            Path.Combine(shell, "expansions", "state", MarkerName);

        public static string SignalPath(string shell) =>
            Path.Combine(shell, "expansions", "state", SignalName);

        public static bool IsDone(string shell) =>
            !string.IsNullOrWhiteSpace(shell) && File.Exists(MarkerPath(shell));

        /// <summary>True after palette_maker and manager_bridge have been compiled with the warm.</summary>
        public static bool ShellHasCacheWarm(string shell)
        {
            if (string.IsNullOrWhiteSpace(shell))
                return false;
            var bridge = Path.Combine(shell, "data", "ssf2mm", "manager_bridge", "mod.swf");
            var palette = Path.Combine(shell, "data", "ssf2mm", "palette_maker", "mod.swf");
            return FileContains(bridge, "cachewarm") && FileContains(palette, "palette-cache-warm.signal");
        }

        private static bool FileContains(string path, string text)
        {
            if (!File.Exists(path))
                return false;
            var bytes = File.ReadAllBytes(path);
            var needle = System.Text.Encoding.ASCII.GetBytes(text);
            if (needle.Length == 0 || bytes.Length < needle.Length)
                return false;
            for (var i = 0; i <= bytes.Length - needle.Length; i++)
            {
                var match = true;
                for (var j = 0; j < needle.Length; j++)
                {
                    if (bytes[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match)
                    return true;
            }
            return false;
        }

        /// <summary>The character id stored on the pack, or the folder name.</summary>
        public static string CharacterId(string packDir)
        {
            var info = Path.Combine(packDir, "info.json");
            if (File.Exists(info))
            {
                try
                {
                    var id = JObject.Parse(File.ReadAllText(info))["id"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(id))
                        return id.Trim();
                }
                catch
                {
                    /* folder name */
                }
            }
            return new DirectoryInfo(packDir).Name;
        }

        /// <summary>
        /// Launches the shell in cache-warm mode and waits until it writes the signal.
        /// Returns false when there is no game exe or the game is already running.
        /// <paramref name="character"/> empty warms the roster and writes the marker.
        /// </summary>
        public static async Task<bool> WarmAsync(
            string shell,
            string? character,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(shell) || !ShellHasCacheWarm(shell))
                return false;
            if (ShellWorkspaceService.FindGameExecutable(shell) == null)
                return false;
            if (ShellConfigureLaunchService.IsGameRunning())
            {
                DebugLogger.Log("[palette-cache] skip warm, game running");
                return false;
            }

            var signal = SignalPath(shell);
            try
            {
                if (File.Exists(signal))
                    File.Delete(signal);
            }
            catch
            {
                /* the wait still notices a new write */
            }

            var extra = new Dictionary<string, object?>
            {
                ["vaultAction"] = "cachewarm"
            };
            if (!string.IsNullOrWhiteSpace(character))
                extra["vaultCacheChar"] = character.Trim();

            progress?.Report(string.IsNullOrWhiteSpace(character)
                ? "Caching costumes…"
                : "Caching " + character.Trim() + "…");
            DebugLogger.Log("[palette-cache] warm start char=" + (character ?? "all"));
            ShellDirectLaunchService.Launch(shell, ShellDirectLaunchService.ScreenVaultCostumes, extra);

            var deadline = DateTime.UtcNow.AddMinutes(20);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (File.Exists(signal))
                {
                    if (string.IsNullOrWhiteSpace(character))
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(MarkerPath(shell))!);
                        File.WriteAllText(MarkerPath(shell), "ok");
                    }
                    try { File.Delete(signal); } catch { /* marker is the record */ }
                    DebugLogger.Log("[palette-cache] warm done char=" + (character ?? "all"));
                    return true;
                }
                await Task.Delay(500, cancellationToken);
            }
            DebugLogger.Log("[palette-cache] warm timed out char=" + (character ?? "all"));
            return false;
        }

        /// <summary>Warms each installed character pack. Does not write into the pack folder.</summary>
        public static async Task WarmInstalledAsync(
            string shell,
            IEnumerable<DeployedPack> placed,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            foreach (var pack in placed.Where(p => p.Kind.Equals("character", StringComparison.OrdinalIgnoreCase)))
            {
                if (string.IsNullOrWhiteSpace(pack.Dest) || !Directory.Exists(pack.Dest))
                    continue;
                await WarmAsync(shell, CharacterId(pack.Dest), progress, cancellationToken);
            }
        }
    }
}
