using System;
using System.Collections.Generic;
using System.IO;

namespace SSF2ModManager.Services
{
    public static class AppPaths
    {
        /// <summary>
        /// Directory containing SSF2ModManager.exe (works with PublishSingleFile extract dirs).
        /// Release layout: exe + README + SSF2 Shell + runtime\ (sidecars).
        /// </summary>
        public static string ExeDirectory
        {
            get
            {
                try
                {
                    var processPath = Environment.ProcessPath;
                    if (!string.IsNullOrWhiteSpace(processPath))
                    {
                        var dir = Path.GetDirectoryName(processPath);
                        if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
                            return dir;
                    }
                }
                catch { /* ignore */ }

                return AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        /// <summary>Preferred sidecars folder: <c>{ExeDirectory}\runtime</c>.</summary>
        public static string RuntimeDirectory => Path.Combine(ExeDirectory, "runtime");

        /// <summary>
        /// Content / tool roots to search: <c>runtime\</c> first, then legacy release-root,
        /// then single-file extract dir (and its <c>runtime\</c>).
        /// </summary>
        public static IEnumerable<string> EnumerateSidecarRoots()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var baseDir in new[] { ExeDirectory, AppContext.BaseDirectory })
            {
                if (string.IsNullOrWhiteSpace(baseDir)) continue;
                var trimmed = baseDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var runtime = Path.Combine(trimmed, "runtime");
                if (seen.Add(runtime))
                    yield return runtime;
                if (seen.Add(trimmed))
                    yield return trimmed;
            }
        }

        /// <summary>
        /// First existing <paramref name="relativePath"/> under a sidecar root, or null.
        /// </summary>
        public static string? FindUnderSidecar(params string[] relativePathParts)
        {
            if (relativePathParts == null || relativePathParts.Length == 0)
                return null;
            var rel = Path.Combine(relativePathParts);
            foreach (var root in EnumerateSidecarRoots())
            {
                var full = Path.Combine(root, rel);
                if (File.Exists(full) || Directory.Exists(full))
                    return full;
            }
            return null;
        }

        /// <summary>Content base for Languages / News / Themes (runtime preferred).</summary>
        public static string ContentDirectory
        {
            get
            {
                foreach (var root in EnumerateSidecarRoots())
                {
                    if (Directory.Exists(Path.Combine(root, "Languages")) ||
                        Directory.Exists(Path.Combine(root, "News")) ||
                        Directory.Exists(Path.Combine(root, "Themes")))
                        return root;
                }

                var runtime = RuntimeDirectory;
                if (Directory.Exists(runtime))
                    return runtime;

                return ExeDirectory;
            }
        }

        /// <summary>
        /// Next2Flash repo that owns FFDec, Flex lite, and the JRE.
        /// Set <c>NEXT2FLASH_HOME</c> to the repo root, or keep the repo as a
        /// sibling of this one (Documents\GitHub\Next2Flash).
        /// </summary>
        public static string? Next2FlashRoot => ProbeNext2FlashRoot();

        public static string? FfdecDirectory =>
            Next2FlashDirIfFile(Path.Combine("toolchain", "ffdec"), "ffdec.jar")
            ?? Next2FlashDirIfFile(Path.Combine("runtime", "resources", "toolchain", "ffdec"), "ffdec.jar")
            ?? FindUnderSidecar("ffdec");

        public static string? AflexLiteDirectory =>
            Next2FlashDirIfFile(Path.Combine("toolchain", "aflex_lite"), Path.Combine("bin", "mxmlc.bat"))
            ?? Next2FlashDirIfFile(Path.Combine("runtime", "resources", "toolchain", "aflex_lite"), Path.Combine("bin", "mxmlc.bat"))
            ?? FindUnderSidecar("aflex_lite");
        public static string? ExtensionsDirectory => FindUnderSidecar("extensions");
        public static string? ToolsDirectory => FindUnderSidecar("tools");
        public static string? JsflDirectory => FindUnderSidecar("jsfl");

        public static string? JreJavaExe =>
            Next2FlashFile("toolchain", "jre", "bin", "java.exe")
            ?? Next2FlashFile("runtime", "resources", "toolchain", "jre", "bin", "java.exe")
            ?? FindUnderSidecar("jre", "bin", "java.exe");

        public static string? FfmpegExe
        {
            get
            {
                return FindUnderSidecar("ffmpeg", "ffmpeg.exe")
                    ?? FindUnderSidecar("ffmpeg", "bin", "ffmpeg.exe");
            }
        }

        public static string AppDataDir
        {
            get
            {
                var dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "SSF2ModManager");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }

        public static string SettingsFile => Path.Combine(AppDataDir, "user-settings.json");
        public static string DebugLogFile => Path.Combine(AppDataDir, "ssf2mm-debug.log");
        public static string ModDatabaseFile => Path.Combine(AppDataDir, "mods.json");

        /// <summary>Bundled news articles under runtime (or legacy root).</summary>
        public static bool IsNext2FlashRoot(string? path) => LooksLikeNext2Flash(path);

        public static string? SavedNext2FlashFolder() => ResolveStored(ReadSetting("Next2FlashFolder"));

        /// <summary>Launcher install folder saved on this PC. Empty when unset or missing.</summary>
        public static string? SavedLauncherInstallFolder() => ResolveStored(ReadSetting("LauncherInstallFolder"));

        /// <summary>A path inside the exe folder is stored relative, so moving that folder keeps it.</summary>
        public static string? StorePath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var full = Path.GetFullPath(path.Trim());
            var exe = Path.GetFullPath(ExeDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (full.StartsWith(exe, StringComparison.OrdinalIgnoreCase))
                return full.Substring(exe.Length);
            return full;
        }

        public static string? ResolveStored(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return null;
            var full = Path.IsPathRooted(path)
                ? path
                : Path.Combine(ExeDirectory, path);
            full = Path.GetFullPath(full);
            return Directory.Exists(full) || File.Exists(full) ? full : null;
        }

        private static string? ReadSetting(string name)
        {
            try
            {
                if (!File.Exists(SettingsFile))
                    return null;
                var txt = File.ReadAllText(SettingsFile);
                var node = Newtonsoft.Json.Linq.JObject.Parse(txt);
                var path = node[name]?.ToString();
                return string.IsNullOrWhiteSpace(path) ? null : path.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string? ProbeNext2FlashRoot()
        {
            var saved = SavedNext2FlashFolder();
            if (LooksLikeNext2Flash(saved))
                return Path.GetFullPath(saved!);

            var env = Environment.GetEnvironmentVariable("NEXT2FLASH_HOME");
            if (LooksLikeNext2Flash(env))
                return Path.GetFullPath(env!);

            var dir = ExeDirectory;
            for (var i = 0; i < 10 && !string.IsNullOrWhiteSpace(dir); i++)
            {
                if (LooksLikeNext2Flash(dir))
                    return Path.GetFullPath(dir);
                var parent = Directory.GetParent(dir)?.FullName;
                if (!string.IsNullOrWhiteSpace(parent))
                {
                    var sibling = Path.Combine(parent, "Next2Flash");
                    if (LooksLikeNext2Flash(sibling))
                        return Path.GetFullPath(sibling);
                }
                if (string.IsNullOrWhiteSpace(parent) || parent == dir)
                    break;
                dir = parent;
            }

            var docs = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "GitHub",
                "Next2Flash");
            if (LooksLikeNext2Flash(docs))
                return Path.GetFullPath(docs);

            var besideExe = Path.Combine(ExeDirectory, "Next2Flash");
            if (LooksLikeNext2Flash(besideExe))
                return Path.GetFullPath(besideExe);

            var downloaded = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SSF2ModManager",
                "Next2Flash");
            if (LooksLikeNext2Flash(downloaded))
                return Path.GetFullPath(downloaded);
            var nested = FindNestedInstall(downloaded);
            if (nested != null)
                return nested;
            return null;
        }

        private static string? FindNestedInstall(string downloaded)
        {
            if (!Directory.Exists(downloaded))
                return null;
            foreach (var dir in Directory.GetDirectories(downloaded))
            {
                if (LooksLikeNext2Flash(dir))
                    return Path.GetFullPath(dir);
            }
            return null;
        }

        private static bool LooksLikeNext2Flash(string? path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return false;
            var dev = File.Exists(Path.Combine(path, "app", "n2f.py"))
                && File.Exists(Path.Combine(path, "toolchain", "ffdec", "ffdec.jar"));
            var release = File.Exists(Path.Combine(path, "runtime", "resources", "app", "n2f.py"))
                && File.Exists(Path.Combine(path, "runtime", "resources", "toolchain", "ffdec", "ffdec.jar"));
            return dev || release;
        }

        private static string? Next2FlashDirIfFile(string relativeDir, string markerFile)
        {
            var root = Next2FlashRoot;
            if (root == null)
                return null;
            var dir = Path.Combine(root, relativeDir);
            return File.Exists(Path.Combine(dir, markerFile)) ? dir : null;
        }

        private static string? Next2FlashFile(params string[] relativeParts)
        {
            var root = Next2FlashRoot;
            if (root == null || relativeParts == null || relativeParts.Length == 0)
                return null;
            var full = root;
            foreach (var part in relativeParts)
                full = Path.Combine(full, part);
            return File.Exists(full) ? full : null;
        }

        public static string NewsFolder => Path.Combine(ContentDirectory, "News");

        /// <summary>Remote news articles synced from GitHub and cached locally.</summary>
        public static string NewsCacheFolder
        {
            get
            {
                var dir = Path.Combine(AppDataDir, "News");
                Directory.CreateDirectory(dir);
                return dir;
            }
        }
    }
}
