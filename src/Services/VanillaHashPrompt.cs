using System;
using System.IO;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// A vanilla hash miss names the files and asks once. Continue is remembered for that folder,
    /// or for that set of built-shell paths at Play.
    /// </summary>
    public static class VanillaHashPrompt
    {
        public const string Prefix = "vanilla hash mismatch:";

        public static string? FilesFrom(string? message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return null;
            var at = message.IndexOf(Prefix, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return null;
            var files = message[(at + Prefix.Length)..].Trim();
            var nl = files.IndexOfAny(new[] { '\r', '\n' });
            if (nl >= 0)
                files = files[..nl].Trim();
            return files.Length == 0 ? null : files;
        }

        public static string Norm(string path) =>
            Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        public static bool Accepted(SettingsService settings, string vanillaFolder)
        {
            var saved = settings.GetVanillaMismatchFolder();
            return !string.IsNullOrWhiteSpace(saved)
                && string.Equals(Norm(saved), Norm(vanillaFolder), StringComparison.OrdinalIgnoreCase);
        }

        public static void Remember(SettingsService settings, string vanillaFolder) =>
            settings.SetVanillaMismatchFolder(Norm(vanillaFolder));

        public static async System.Threading.Tasks.Task BuildAsync(
            ShellBaseService.Release release, string vanilla, string shell, IProgress<string> progress,
            SettingsService settings, Func<string, bool> ask, System.Threading.CancellationToken ct = default)
        {
            var allow = Accepted(settings, vanilla);
            try
            {
                await ShellBaseService.BuildAsync(release, vanilla, shell, progress, ct, allow);
            }
            catch (InvalidOperationException ex) when (FilesFrom(ex.Message) is string files && !allow)
            {
                DebugLogger.Log("[setup] vanilla mismatch ask " + files);
                if (!ask(Question(files)))
                    throw new InvalidOperationException("Build cancelled.");
                Remember(settings, vanilla);
                await ShellBaseService.BuildAsync(release, vanilla, shell, progress, ct, true);
            }
        }

        public static string Question(string files) =>
            "These vanilla files are not SSF2 Beta 1.4.0.1:\n\n" + files
            + "\n\nContinue anyway? This is asked once for this folder.";

        public static string PlayKey(System.Collections.Generic.IReadOnlyList<string> paths) =>
            string.Join("\n", paths);

        public static string PlayQuestion(System.Collections.Generic.IReadOnlyList<string> paths) =>
            "These vanilla files in the Shell do not match:\n\n" + string.Join("\n", paths)
            + "\n\nContinue anyway? This is asked once for this set.";

        /// <summary>True when Play should start. A new set of vanilla cautions asks <paramref name="ask"/>.</summary>
        public static bool ConfirmPlay(SettingsService settings, string shell, Func<string, bool> ask)
        {
            var paths = BuildTrust.VanillaCautionPaths(BuildTrust.Check(shell));
            if (paths.Count == 0)
                return true;
            var key = PlayKey(paths);
            if (string.Equals(settings.GetPlayCautionKey(), key, StringComparison.OrdinalIgnoreCase))
                return true;
            if (!ask(PlayQuestion(paths)))
                return false;
            settings.SetPlayCautionKey(key);
            return true;
        }
    }
}
