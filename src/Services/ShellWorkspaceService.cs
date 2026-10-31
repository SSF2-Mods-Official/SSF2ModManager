using System;
using System.IO;
using System.Linq;
using SSF2ModManager.Models;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Locates the playable SSF2 Shell game folder and the optional shell-dev workspace.
    /// </summary>
    public static class ShellWorkspaceService
    {
        public const string VersionNickname = "SSF2 Shell";
        public const string VersionName = "SSF2 Shell";
        public const string GameExeName = "Super Smash Flash 2 Beta.exe";
        public const string DevFolderName = "shell-dev";

        public static readonly string[] GameExeNames =
        {
            GameExeName,
            "SSF2.exe",
            "Super Smash Flash 2.exe",
            "ssf2.exe"
        };

        /// <summary>Playable game install (exe + data), not the AS3MXML project.</summary>
        public static bool IsShellFolder(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;

            // New layout: SSF2 Shell is the former build/ tree
            if (Directory.Exists(Path.Combine(path, "data")) && FindGameExecutable(path) != null)
                return true;

            // Legacy: asconfig + nested build/
            return File.Exists(Path.Combine(path, "asconfig.json"))
                   && Directory.Exists(Path.Combine(path, "build"));
        }

        public static bool IsDevWorkspace(string? path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return false;
            return File.Exists(Path.Combine(path, "asconfig.json"))
                   && Directory.Exists(Path.Combine(path, "tools"));
        }

        public static string? FindShellFolder()
        {
            var saved = ShellFromInstallRoot(AppPaths.SavedLauncherInstallFolder());
            if (saved != null)
                return saved;

            foreach (var cand in EnumerateManagerCandidates())
            {
                var shell = Path.Combine(cand, "SSF2 Shell");
                if (IsShellFolder(shell)) return shell;
            }

            // Legacy nested zip layout walk
            var dir = new DirectoryInfo(AppPaths.ExeDirectory);
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var cand = Path.Combine(dir.FullName, "SSF2 Shell");
                if (IsShellFolder(cand)) return cand;
                var zipLayout = Path.Combine(dir.FullName, "SSF2 Mod Manager", "SSF2 Shell");
                if (IsShellFolder(zipLayout)) return zipLayout;
                dir = dir.Parent;
            }

            return null;
        }

        public static string? FindDevWorkspace()
        {
            // shell-dev lives with the modding tools, not inside the game folder.
            foreach (var cand in EnumerateManagerCandidates())
            {
                var runtimeDev = Path.Combine(cand, "runtime", DevFolderName);
                if (IsDevWorkspace(runtimeDev)) return runtimeDev;
            }

            // Older layouts kept shell-dev inside the game folder.
            var shell = FindShellFolder();
            if (shell != null)
            {
                var underExpansions = Path.Combine(shell, "expansions", DevFolderName);
                if (IsDevWorkspace(underExpansions)) return underExpansions;
                var nested = Path.Combine(shell, DevFolderName);
                if (IsDevWorkspace(nested)) return nested;
            }

            foreach (var cand in EnumerateManagerCandidates())
            {
                var underExpansions = Path.Combine(cand, "SSF2 Shell", "expansions", DevFolderName);
                if (IsDevWorkspace(underExpansions)) return underExpansions;
                var nested = Path.Combine(cand, "SSF2 Shell", DevFolderName);
                if (IsDevWorkspace(nested)) return nested;
                var sibling = Path.Combine(cand, DevFolderName);
                if (IsDevWorkspace(sibling)) return sibling;
            }

            var dir = new DirectoryInfo(AppPaths.ExeDirectory);
            for (var i = 0; i < 8 && dir != null; i++)
            {
                var underExpansions = Path.Combine(dir.FullName, "SSF2 Shell", "expansions", DevFolderName);
                if (IsDevWorkspace(underExpansions)) return underExpansions;
                var nested = Path.Combine(dir.FullName, "SSF2 Shell", DevFolderName);
                if (IsDevWorkspace(nested)) return nested;
                var zipNested = Path.Combine(dir.FullName, "SSF2 Mod Manager", "SSF2 Shell", "expansions", DevFolderName);
                if (IsDevWorkspace(zipNested)) return zipNested;
                var zipLegacy = Path.Combine(dir.FullName, "SSF2 Mod Manager", "SSF2 Shell", DevFolderName);
                if (IsDevWorkspace(zipLegacy)) return zipLegacy;
                dir = dir.Parent;
            }

            return null;
        }

        private static string? ShellFromInstallRoot(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            if (IsShellFolder(path))
                return Path.GetFullPath(path);
            var nested = Path.Combine(path, VersionName);
            if (IsShellFolder(nested))
                return Path.GetFullPath(nested);
            return null;
        }

        private static IEnumerable<string> EnumerateManagerCandidates()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var baseDir in new[] { AppPaths.ExeDirectory, AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(baseDir)) continue;
                var trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (seen.Add(trimmed))
                    yield return trimmed;
            }
        }

        /// <summary>Install root for mods / game launch (= SSF2 Shell, formerly SSF2 Shell/build).</summary>
        public static string? FindBuildFolder()
        {
            var shell = FindShellFolder();
            if (shell == null) return null;

            // Legacy: still have nested build/
            var nested = Path.Combine(shell, "build");
            if (Directory.Exists(nested) && FindGameExecutable(nested) != null)
                return nested;

            return shell;
        }

        public static string? FindGameExecutable(string? installRoot = null)
        {
            var root = string.IsNullOrWhiteSpace(installRoot) ? FindBuildFolder() : installRoot;
            if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
                return null;

            foreach (var name in GameExeNames)
            {
                var direct = Path.Combine(root, name);
                if (File.Exists(direct)) return direct;
            }

            foreach (var name in GameExeNames)
            {
                try
                {
                    var found = Directory.GetFiles(root, name, SearchOption.AllDirectories);
                    if (found.Length > 0) return found[0];
                }
                catch { /* ignore */ }
            }

            return FindFallbackExe(root);
        }

        public static string? FindFallbackExe(string installRoot)
        {
            try
            {
                var exeFiles = Directory.GetFiles(installRoot, "*.exe", SearchOption.AllDirectories);
                return exeFiles.FirstOrDefault(p =>
                {
                    var rel = Path.GetRelativePath(installRoot, p);
                    return rel.IndexOf("Adobe AIR", StringComparison.OrdinalIgnoreCase) < 0
                           && rel.IndexOf("META-INF", StringComparison.OrdinalIgnoreCase) < 0;
                });
            }
            catch
            {
                return null;
            }
        }

        public static string ExpansionsRoot(string installRoot) =>
            Path.Combine(installRoot, "expansions");

        /// <summary>Drop-in character packs: <c>expansions/character/</c> (was <c>data/character/expansions/</c>).</summary>
        public static string ExpansionsDirectory(string installRoot) =>
            CharacterExpansionsDirectory(installRoot);

        public static string CharacterExpansionsDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "character");

        public static string StageExpansionsDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "stages");

        public static string ModsDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "mods");

        public static string DecompiledExpansionsDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "decompiled");

        public static string Ssf2mmDirectory(string installRoot) =>
            Path.Combine(installRoot, "data", "ssf2mm");

        /// <summary>Older runtime folder. Reads still find files here until the next write.</summary>
        public static string LegacyStateDirectory(string installRoot) =>
            Path.Combine(Ssf2mmDirectory(installRoot), "state");

        /// <summary>Runtime files the game and manager rewrite (boot, launch, log). Kept out of <c>data/</c>.</summary>
        public static string StateDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "state");

        public static string GameLogPath(string installRoot) =>
            Path.Combine(StateDirectory(installRoot), "game.log");

        /// <summary>Creates <c>expansions/state</c>. Does not read or write <c>data/</c>.</summary>
        public static void EnsureRuntimeState(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return;
            Directory.CreateDirectory(StateDirectory(installRoot));
        }

        /// <summary>The player's own data (controller profiles, playlists, menu music, song cache). Moves with the expansions folder.</summary>
        public static string UserStateDirectory(string installRoot) =>
            Path.Combine(ExpansionsRoot(installRoot), "user");

        /// <summary>Player file under <c>expansions/user</c>. <c>data/</c> is not consulted.</summary>
        public static string UserStateFile(string installRoot, string fileName) =>
            Path.Combine(UserStateDirectory(installRoot), fileName);

        /// <summary>Source for the built-in packs. Compiling writes <c>data/ssf2mm/&lt;id&gt;/mod.swf</c>.</summary>
        public static string BuiltInModSourceDirectory(string installRoot) =>
            Path.Combine(installRoot, "src", "mods");

        public static string EngineDirectory(string installRoot) =>
            Path.Combine(installRoot, "data", "engine");

        public static string TemplateDirectory(string installRoot) =>
            Path.Combine(CharacterExpansionsDirectory(installRoot), "template");

        /// <summary>Stock character SWFs only (<c>data/character/</c>).</summary>
        public static string CharacterDirectory(string installRoot) =>
            Path.Combine(installRoot, "data", "character");

        /// <summary>
        /// Ensures top-level <c>expansions/</c> and moves legacy <c>data/…</c> mod trees when needed.
        /// </summary>
        public static void EnsureExpansionsLayout(string? installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return;

            Directory.CreateDirectory(ExpansionsRoot(installRoot));

            TryMigrateFolder(
                Path.Combine(installRoot, "data", "character", "expansions"),
                CharacterExpansionsDirectory(installRoot));
            TryMigrateFolder(
                Path.Combine(installRoot, "data", "mods"),
                ModsDirectory(installRoot));
            TryMigrateFolder(
                Path.Combine(installRoot, "expansions", "engine"),
                EngineDirectory(installRoot));
            TryMigrateFolder(
                Path.Combine(installRoot, "expansions", "ssf2mm"),
                Ssf2mmDirectory(installRoot));
            TryMigrateEssentials(
                Path.Combine(installRoot, "expansions", "essentials"),
                Ssf2mmDirectory(installRoot));

            Directory.CreateDirectory(CharacterExpansionsDirectory(installRoot));
            Directory.CreateDirectory(ModsDirectory(installRoot));
            Directory.CreateDirectory(Ssf2mmDirectory(installRoot));
            Directory.CreateDirectory(EngineDirectory(installRoot));
        }

        private static void TryMigrateEssentials(string legacyPath, string ssf2mmPath)
        {
            if (!Directory.Exists(legacyPath))
                return;
            Directory.CreateDirectory(ssf2mmPath);
            foreach (var child in Directory.GetDirectories(legacyPath))
            {
                var name = Path.GetFileName(child);
                if (string.IsNullOrWhiteSpace(name))
                    continue;
                var dest = Path.Combine(ssf2mmPath, name);
                if (Directory.Exists(dest))
                    continue;
                try
                {
                    Directory.Move(child, dest);
                }
                catch
                {
                    // Leave the pack in place if a file is locked.
                }
            }
        }

        private static void TryMigrateFolder(string legacyPath, string newPath)
        {
            if (!Directory.Exists(legacyPath))
                return;
            if (Directory.Exists(newPath))
                return; // prefer new; do not merge

            try
            {
                var parent = Path.GetDirectoryName(newPath);
                if (!string.IsNullOrWhiteSpace(parent))
                    Directory.CreateDirectory(parent);
                Directory.Move(legacyPath, newPath);
            }
            catch
            {
                // Leave legacy in place if move fails (locked files, etc.).
            }
        }

        public static void EnsureRegistered(ModManagerService mods)
        {
            if (mods == null) throw new ArgumentNullException(nameof(mods));
            var build = FindBuildFolder();
            if (string.IsNullOrWhiteSpace(build)) return;

            EnsureExpansionsLayout(build);

            var existing = mods.Versions.FirstOrDefault(v =>
                PathsEqual(v.FolderPath, build)
                || string.Equals(v.Nickname, VersionNickname, StringComparison.OrdinalIgnoreCase)
                || IsLegacyBuildPath(v.FolderPath, build));

            if (existing == null)
            {
                mods.AddVersion(VersionName, build, VersionNickname);
            }
            else if (!PathsEqual(existing.FolderPath, build))
            {
                mods.ChangeVersionPath(existing, build);
            }

            mods.ActiveVersion = VersionNickname;
        }

        /// <summary>True if entry still points at ...\SSF2 Shell\build after flatten.</summary>
        private static bool IsLegacyBuildPath(string? entryPath, string currentInstall)
        {
            if (string.IsNullOrWhiteSpace(entryPath)) return false;
            try
            {
                var full = Path.GetFullPath(entryPath.TrimEnd('\\', '/'));
                var legacy = Path.GetFullPath(Path.Combine(currentInstall, "build"));
                if (string.Equals(full, legacy, StringComparison.OrdinalIgnoreCase))
                    return true;
                // entry was ...\SSF2 Shell\build and current is ...\SSF2 Shell
                var parent = Directory.GetParent(full);
                return parent != null
                       && PathsEqual(parent.FullName, currentInstall)
                       && string.Equals(Path.GetFileName(full), "build", StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static bool PathsEqual(string? a, string? b)
        {
            if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) return false;
            try
            {
                return string.Equals(
                    Path.GetFullPath(a.TrimEnd('\\', '/')),
                    Path.GetFullPath(b.TrimEnd('\\', '/')),
                    StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static bool IsShellVersion(SSF2VersionEntry? entry) =>
            entry != null && (
                string.Equals(entry.Nickname, VersionNickname, StringComparison.OrdinalIgnoreCase)
                || PathsEqual(entry.FolderPath, FindBuildFolder()));
    }
}
