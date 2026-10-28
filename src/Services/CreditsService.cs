using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Maintains Credits.txt in the SSF2 Shell folder from expansions/*/info.json
    /// so shared mod packs always credit creators.
    /// </summary>
    public static class CreditsService
    {
        public const string FileName = "Credits.txt";

        private static readonly HashSet<string> SkipSections = new(StringComparer.OrdinalIgnoreCase)
        {
            "engine", "ssf2mm"
        };

        private static readonly HashSet<string> SkipFolders = new(StringComparer.OrdinalIgnoreCase)
        {
            "decompiled", "resources", "_template", "template"
        };

        public static string CreditsPath(string shellRoot) =>
            Path.Combine(shellRoot, FileName);

        /// <summary>
        /// Scan expansions and rewrite Credits.txt. Restores missing auto-entries;
        /// preserves a trailing "## Notes" section if present.
        /// </summary>
        public static void Update(string? shellRoot)
        {
            if (string.IsNullOrWhiteSpace(shellRoot) || !Directory.Exists(shellRoot))
                return;

            try
            {
                var entries = Scan(shellRoot);
                var path = CreditsPath(shellRoot);
                var notes = StandingCredits(ReadManualNotes(path));
                Write(path, entries, notes);
            }
            catch (Exception ex)
            {
                try { DebugLogger.Error("CreditsService.Update failed", ex); } catch { }
            }
        }

        /// <summary>Update using the resolved SSF2 Shell / build folder.</summary>
        public static void UpdateForActiveShell()
        {
            Update(ShellWorkspaceService.FindBuildFolder() ?? ShellWorkspaceService.FindShellFolder());
        }

        public static List<CreditEntry> Scan(string shellRoot)
        {
            var list = new List<CreditEntry>();
            var expansions = Path.Combine(shellRoot, "expansions");
            if (!Directory.Exists(expansions))
                return list;

            foreach (var sectionDir in Directory.GetDirectories(expansions))
            {
                var section = Path.GetFileName(sectionDir);
                if (SkipSections.Contains(section))
                    continue;

                foreach (var packDir in Directory.GetDirectories(sectionDir))
                {
                    var folder = Path.GetFileName(packDir);
                    if (SkipFolders.Contains(folder))
                        continue;

                    // Nested group folders (e.g. character packs must have assets)
                    if (section.Equals("character", StringComparison.OrdinalIgnoreCase))
                    {
                        var hasSwf = Directory.GetFiles(packDir, "*.swf", SearchOption.TopDirectoryOnly).Length > 0
                                     || File.Exists(Path.Combine(packDir, "assets.swf"))
                                     || File.Exists(Path.Combine(packDir, "info.json"));
                        if (!hasSwf && !File.Exists(Path.Combine(packDir, "info.json")))
                            continue;
                    }

                    var entry = ReadPack(shellRoot, packDir, section, folder);
                    if (entry != null)
                        list.Add(entry);
                }
            }

            var builtin = Path.Combine(shellRoot, "data", "ssf2mm");
            if (Directory.Exists(builtin))
            {
                foreach (var packDir in Directory.GetDirectories(builtin))
                {
                    var folder = Path.GetFileName(packDir);
                    if (SkipFolders.Contains(folder))
                        continue;
                    var entry = ReadPack(shellRoot, packDir, "essentials", folder);
                    if (entry != null)
                        list.Add(entry);
                }
            }

            return list
                .OrderBy(e => e.Section, StringComparer.OrdinalIgnoreCase)
                .ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static CreditEntry? ReadPack(string shellRoot, string packDir, string section, string folder)
        {
            var infoPath = Path.Combine(packDir, "info.json");
            var modPath = Path.Combine(packDir, "mod.json");
            JObject? info = null;
            JObject? mod = null;

            if (File.Exists(infoPath))
            {
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch { info = null; }
            }
            if (File.Exists(modPath))
            {
                try { mod = JObject.Parse(File.ReadAllText(modPath)); }
                catch { mod = null; }
            }

            if (info == null && mod == null)
                return null;

            var name = FirstNonEmpty(
                info?["name"]?.ToString(),
                mod?["name"]?.ToString(),
                folder) ?? folder;

            var creator = FirstNonEmpty(
                info?["creator"]?.ToString(),
                info?["author"]?.ToString(),
                mod?["author"]?.ToString(),
                mod?["creator"]?.ToString(),
                "Unknown");

            var type = FirstNonEmpty(
                info?["type"]?.ToString(),
                section) ?? section;

            var rel = Path.GetRelativePath(shellRoot, packDir).Replace('\\', '/');
            var enabled = true;
            var enabledToken = info?["enabled"];
            if (enabledToken != null && enabledToken.Type != JTokenType.Null)
            {
                try { enabled = enabledToken.Value<bool>(); }
                catch { enabled = true; }
            }

            return new CreditEntry
            {
                RelativePath = rel,
                Name = name.Trim(),
                Creator = (creator ?? "Unknown").Trim(),
                Type = type.Trim(),
                Section = section,
                Enabled = enabled
            };
        }

        private static string? FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return null;
        }

        /// <summary>IDK and the original Palette Maker stay in Notes, which a launch rewrite keeps.</summary>
        public static string StandingCredits(string notes)
        {
            const string idk = "- IDK — David (stariwinkle)";
            const string palette = "- Palette Maker (original) — VAL";
            var text = notes ?? "";
            if (text.Contains("David (stariwinkle)", StringComparison.Ordinal)
                && text.Contains("Palette Maker (original)", StringComparison.Ordinal))
                return text;
            var block = idk + Environment.NewLine + palette + Environment.NewLine + Environment.NewLine;
            var trimmed = text.TrimStart('\r', '\n');
            if (trimmed.StartsWith("# Add freeform", StringComparison.Ordinal))
                return block + trimmed;
            if (trimmed.Length == 0)
                return block + "# Add freeform credits below this line; they are preserved across updates.";
            return block + text.TrimStart('\r', '\n');
        }

        private static string ReadManualNotes(string path)
        {
            if (!File.Exists(path))
                return "";

            try
            {
                var text = File.ReadAllText(path);
                var marker = "## Notes";
                var idx = text.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                if (idx < 0)
                    return "";
                return text.Substring(idx + marker.Length).TrimStart('\r', '\n');
            }
            catch
            {
                return "";
            }
        }

        private static void Write(string path, List<CreditEntry> entries, string notes)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# SSF2 Shell — Mod Credits");
            sb.AppendLine("# Auto-maintained by SSF2 Mod Manager. Verified again when the game launches (manager_bridge).");
            sb.AppendLine("# Removing an entry for an installed mod will restore it on the next update/launch.");
            sb.AppendLine("# Format: Name — Creator  [type]  (relative path)");
            sb.AppendLine();

            string? lastSection = null;
            foreach (var e in entries)
            {
                if (!string.Equals(lastSection, e.Section, StringComparison.OrdinalIgnoreCase))
                {
                    if (lastSection != null)
                        sb.AppendLine();
                    sb.AppendLine("## " + SectionTitle(e.Section));
                    lastSection = e.Section;
                }

                var creator = string.IsNullOrWhiteSpace(e.Creator) ? "Unknown" : e.Creator;
                sb.AppendLine($"- {e.Name} — {creator}  [{e.Type}]  ({e.RelativePath})");
            }

            sb.AppendLine();
            sb.AppendLine("## Notes");
            if (!string.IsNullOrWhiteSpace(notes))
                sb.AppendLine(notes.TrimEnd() + Environment.NewLine);
            else
                sb.AppendLine("# Add freeform credits below this line; they are preserved across updates.");

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static string SectionTitle(string section) => section.ToLowerInvariant() switch
        {
            "essentials" => "Essentials",
            "mods" => "Mods",
            "character" => "Characters",
            _ => char.ToUpperInvariant(section[0]) + section.Substring(1)
        };
    }

    public sealed class CreditEntry
    {
        public string RelativePath { get; set; } = "";
        public string Name { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Type { get; set; } = "";
        public string Section { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }
}
