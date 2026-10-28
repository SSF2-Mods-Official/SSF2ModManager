using System.IO;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// What the first-run setup still needs: Next2Flash, a vanilla SSF2 Beta 1.4.0.1 folder, a built SSF2 Shell,
    /// and optionally an editor for debugging.
    /// </summary>
    public static class SetupService
    {
        public sealed record Status(
            string? Next2Flash,
            string? Vanilla,
            ShellBaseService.VanillaCheck VanillaCheck,
            string? Shell,
            string? ShellVersion,
            bool DevShell,
            string? Editor);

        /// <summary>Where a new SSF2 Shell is built: beside the Mod Manager exe.</summary>
        public static string DefaultShellTarget() =>
            Path.Combine(AppPaths.ExeDirectory, ShellWorkspaceService.VersionName);

        /// <summary>The saved vanilla folder, else the default install path, else null.</summary>
        public static string? FindVanilla(string? saved)
        {
            if (ShellBaseService.IsVanillaFolder(saved))
                return saved;
            return ShellBaseService.IsVanillaFolder(ShellBaseService.DefaultVanillaFolder)
                ? ShellBaseService.DefaultVanillaFolder
                : null;
        }

        public static Status Read(SettingsService settings)
        {
            Next2FlashSetup.MigrateLegacy();
            var vanilla = FindVanilla(settings.GetVanillaSsf2Folder());
            var shell = ShellWorkspaceService.FindShellFolder();
            var status = new Status(
                AppPaths.Next2FlashRoot,
                vanilla,
                ShellBaseService.CheckVanilla(vanilla),
                shell,
                shell == null ? null : ShellBaseService.InstalledVersion(shell),
                shell != null && ShellBaseService.IsDevShell(shell),
                ModdingToolchainService.ResolveEditorCli());
            DebugLogger.Log($"[setup] status n2f={(status.Next2Flash != null ? 1 : 0)} vanilla={status.VanillaCheck} " +
                $"shell={(status.Shell != null ? 1 : 0)} version={status.ShellVersion ?? "none"} dev={(status.DevShell ? 1 : 0)} " +
                $"editor={(status.Editor != null ? 1 : 0)}");
            return status;
        }

        /// <summary>Open setup at launch when the game cannot be played yet.</summary>
        public static bool IsNeeded(Status status) =>
            status.Next2Flash == null || status.Shell == null;
    }
}
