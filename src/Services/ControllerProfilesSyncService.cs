using System.IO;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Syncs Configure Launch tags with the in-game controller_profiles / profiles.json nametags.
    /// <c>names</c> in that file is the only source of truth for available tags.
    /// "(none)" in the manager = Default / empty name in-game (CSS shows P1–P4).
    /// </summary>
    public static class ControllerProfilesSyncService
    {
        public const int NameMax = 15;
        public const string DefaultTag = "Default";

        public sealed class Snapshot
        {
            public List<string> Names { get; set; } = new();
            /// <summary>Slot 1–4 last selected tag; empty means Default / no nametag.</summary>
            public Dictionary<int, string> Last { get; set; } = new();
        }

        public static string? ProfilesPath(string installRoot)
        {
            if (string.IsNullOrWhiteSpace(installRoot))
                return null;
            var builtin = UserProfilesPath(installRoot)!;
            if (File.Exists(builtin))
                return builtin;
            foreach (var legacy in new[]
            {
                Path.Combine(ShellWorkspaceService.StateDirectory(installRoot), "controller_profiles.json"),
                Path.Combine(ShellWorkspaceService.LegacyStateDirectory(installRoot), "controller_profiles.json"),
                Path.Combine(installRoot, "expansions", "mods", "controller_profiles", "profiles.json"),
                Path.Combine(installRoot, "data", "ssf2mm", "controller_profiles", "profiles.json"),
                Path.Combine(installRoot, "expansions", "essentials", "controller_profiles", "profiles.json")
            })
            {
                if (File.Exists(legacy))
                    return legacy;
            }
            return builtin;
        }

        /// <summary>Where saves go: <c>expansions/user/controller_profiles.json</c>.</summary>
        public static string? UserProfilesPath(string installRoot) =>
            string.IsNullOrWhiteSpace(installRoot)
                ? null
                : Path.Combine(ShellWorkspaceService.UserStateDirectory(installRoot), "controller_profiles.json");

        public static Snapshot Load(string installRoot)
        {
            var snap = new Snapshot();
            var path = ProfilesPath(installRoot);
            if (path == null || !File.Exists(path))
                return snap;
            try
            {
                var root = JObject.Parse(File.ReadAllText(path));
                // Only the names array — do not invent tags from orphan profile keys.
                if (root["names"] is JArray names)
                {
                    foreach (var n in names)
                    {
                        var tag = NormalizeName(n?.ToString());
                        if (tag.Length == 0)
                            continue;
                        if (snap.Names.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                            continue;
                        snap.Names.Add(tag);
                    }
                }
                if (root["last"] is JObject last)
                {
                    for (var pid = 1; pid <= 4; pid++)
                    {
                        var raw = last[pid.ToString()]?.ToString() ?? "";
                        var tag = NormalizeName(raw);
                        if (tag.Length > 0 && !snap.Names.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                            tag = "";
                        snap.Last[pid] = tag;
                    }
                }
            }
            catch
            {
                // Corrupt / locked — leave empty snapshot.
            }
            return snap;
        }

        /// <summary>
        /// Replace launch tag lists with profiles.json only (wipe manager-only leftovers).
        /// </summary>
        public static void MergeIntoPrefs(string installRoot, LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            var snap = Load(installRoot);
            prefs.PlayerTags = snap.Names.ToList();
            for (var i = 0; i < 4; i++)
            {
                var pid = i + 1;
                var current = NormalizeName(prefs.Players[i].Tag);
                if (current.Length > 0 && snap.Names.Any(t => t.Equals(current, StringComparison.OrdinalIgnoreCase)))
                {
                    prefs.Players[i].Tag = snap.Names.First(t => t.Equals(current, StringComparison.OrdinalIgnoreCase));
                    continue;
                }
                if (snap.Last.TryGetValue(pid, out var last) && last.Length > 0)
                    prefs.Players[i].Tag = last;
                else
                    prefs.Players[i].Tag = "";
            }
        }

        public static bool AddName(string installRoot, string? raw)
        {
            var tag = NormalizeName(raw);
            if (tag.Length == 0)
                return false;
            var snap = Load(installRoot);
            if (snap.Names.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                return false;
            snap.Names.Add(tag);
            SaveSnapshot(installRoot, snap, ensureProfile: tag);
            return true;
        }

        public static bool DeleteName(string installRoot, string? raw)
        {
            var tag = NormalizeName(raw);
            if (tag.Length == 0)
                return false;
            var snap = Load(installRoot);
            var removed = snap.Names.RemoveAll(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)) > 0;
            if (!removed)
                return false;
            for (var pid = 1; pid <= 4; pid++)
            {
                if (snap.Last.TryGetValue(pid, out var last) && last.Equals(tag, StringComparison.OrdinalIgnoreCase))
                    snap.Last[pid] = "";
            }
            SaveSnapshot(installRoot, snap, removeProfile: tag);
            return true;
        }

        /// <summary>Write per-slot selection back; names stay whatever is in profiles.json.</summary>
        public static void WriteFromPrefs(string installRoot, LaunchConfigurePrefs prefs)
        {
            prefs.EnsureDefaults();
            var snap = Load(installRoot);
            // Keep file names as source of truth — only update last from prefs.
            for (var i = 0; i < 4; i++)
            {
                var tag = NormalizeName(prefs.Players[i].Tag);
                if (tag.Length > 0 && !snap.Names.Any(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase)))
                    tag = "";
                else if (tag.Length > 0)
                    tag = snap.Names.First(t => t.Equals(tag, StringComparison.OrdinalIgnoreCase));
                snap.Last[i + 1] = tag;
                prefs.Players[i].Tag = tag;
            }
            prefs.PlayerTags = snap.Names.ToList();
            SaveSnapshot(installRoot, snap);
        }

        public static string NormalizeName(string? raw)
        {
            var tag = (raw ?? "").Trim();
            if (tag.Equals(DefaultTag, StringComparison.OrdinalIgnoreCase))
                return "";
            if (tag.Length > NameMax)
                tag = tag[..NameMax];
            return tag;
        }

        private static void SaveSnapshot(
            string installRoot,
            Snapshot snap,
            string? ensureProfile = null,
            string? removeProfile = null)
        {
            var source = ProfilesPath(installRoot);
            var path = UserProfilesPath(installRoot);
            if (source == null || path == null)
                return;
            try
            {
                var dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                JObject root;
                if (File.Exists(source))
                {
                    try { root = JObject.Parse(File.ReadAllText(source)); }
                    catch { root = new JObject(); }
                }
                else
                    root = new JObject();

                var profiles = root["profiles"] as JObject ?? new JObject();
                if (!string.IsNullOrEmpty(ensureProfile) && profiles[ensureProfile] == null)
                    profiles[ensureProfile] = SeedProfile();
                if (!string.IsNullOrEmpty(removeProfile))
                    profiles.Remove(removeProfile);
                profiles.Remove(DefaultTag);

                // Drop orphan profile blobs not listed in names.
                var keep = new HashSet<string>(snap.Names, StringComparer.OrdinalIgnoreCase);
                foreach (var prop in profiles.Properties().ToList())
                {
                    if (!keep.Contains(prop.Name))
                        profiles.Remove(prop.Name);
                }

                var last = new JObject();
                for (var pid = 1; pid <= 4; pid++)
                {
                    snap.Last.TryGetValue(pid, out var tag);
                    tag = NormalizeName(tag);
                    if (tag.Length > 0 && !keep.Contains(tag))
                        tag = "";
                    last[pid.ToString()] = tag.Length == 0 ? DefaultTag : tag;
                }

                root["names"] = new JArray(snap.Names);
                root["profiles"] = profiles;
                root["last"] = last;
                File.WriteAllText(path, root.ToString(Newtonsoft.Json.Formatting.None));
            }
            catch
            {
                // Best-effort sync.
            }
        }

        private static JObject SeedProfile() => new()
        {
            ["keyboard"] = new JObject
            {
                ["controls"] = new JObject(),
                ["pad"] = new JObject()
            },
            ["pads"] = new JObject()
        };
    }
}
