using Microsoft.Win32;
using System;
using System.Diagnostics;

namespace SSF2ModManager.Services
{
    public sealed class ProtocolInstallRequest
    {
        public string ArchiveUrl { get; init; } = string.Empty;
        /// <summary>GameBanana ItemType / ModelName (e.g. Mod, Sound, Tool). Not the submission category.</summary>
        public string ModType { get; init; } = string.Empty;
        public int? ModId { get; init; }
        public string RawUrl { get; init; } = string.Empty;
    }

    public static class ProtocolService
    {
        public const string Scheme = "ssf2mm";

        public static bool TryParse(string? url, out ProtocolInstallRequest request)
        {
            request = new ProtocolInstallRequest();
            if (string.IsNullOrWhiteSpace(url)) return false;

            var trimmed = url.Trim();
            if (!trimmed.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase)) return false;

            // ssf2mm:https://...  or  ssf2mm://https//...  (GameBanana 1-click variants)
            var payload = trimmed[(Scheme.Length + 1)..].TrimStart('/');
            payload = GameBananaUrlHelper.NormalizeSchemeTypos(payload);

            var parts = payload.Split(',');
            if (parts.Length == 0) return false;

            var archive = GameBananaUrlHelper.NormalizeSchemeTypos(parts[0].Trim());
            if (!archive.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                && !archive.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                return false;

            int? modId = null;
            if (parts.Length >= 3 && int.TryParse(parts[2].Trim(), out var id))
                modId = id;

            request = new ProtocolInstallRequest
            {
                RawUrl = trimmed,
                ArchiveUrl = archive,
                ModType = parts.Length >= 2 ? parts[1].Trim() : string.Empty,
                ModId = modId
            };
            return true;
        }

        public static string GetExePath() =>
            Process.GetCurrentProcess().MainModule?.FileName ?? string.Empty;

        public static void Register()
        {
            var exePath = GetExePath();
            if (string.IsNullOrEmpty(exePath)) return;

            using var key = Registry.CurrentUser.CreateSubKey($@"Software\Classes\{Scheme}");
            if (key == null) return;
            key.SetValue("", "URL:SSF2 Mod Manager Protocol");
            key.SetValue("URL Protocol", "");
            using var defaultIcon = key.CreateSubKey("DefaultIcon");
            defaultIcon?.SetValue("", exePath + ",1");
            using var command = key.CreateSubKey(@"shell\open\command");
            command?.SetValue("", $"\"{exePath}\" \"%1\"");
        }

        public static void Unregister()
        {
            try { Registry.CurrentUser.DeleteSubKeyTree($@"Software\Classes\{Scheme}", false); }
            catch { }
        }

        public static bool IsRegistered()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
                if (key?.GetValue("") is not string command) return false;
                var exe = GetExePath();
                return !string.IsNullOrEmpty(exe) && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        public static string GetRegistrationStatus()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey($@"Software\Classes\{Scheme}\shell\open\command");
                var command = key?.GetValue("") as string;
                if (string.IsNullOrWhiteSpace(command))
                    return "Not registered";

                var exe = GetExePath();
                var ok = !string.IsNullOrEmpty(exe) && command.Contains(exe, StringComparison.OrdinalIgnoreCase);
                return ok
                    ? $"Registered (points to current executable)\n{command}"
                    : $"Registered (different executable)\n{command}";
            }
            catch (Exception ex)
            {
                return $"Registration check failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Builds a test URL for manual verification. Does not perform an install.
        /// </summary>
        public static string BuildTestUrl(string archiveUrl, string modType = "Mod", int modId = 0) =>
            modId > 0
                ? $"{Scheme}:{archiveUrl},{modType},{modId}"
                : $"{Scheme}:{archiveUrl},{modType}";
    }
}
