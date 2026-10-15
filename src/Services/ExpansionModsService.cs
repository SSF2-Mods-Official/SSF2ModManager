using System.IO;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public sealed class ModSettingEntry
    {
        public string Id { get; set; } = "";
        public string Label { get; set; } = "";
        public string Type { get; set; } = "string";
        public string Value { get; set; } = "";
        /// <summary>For dropdowns: explicit options, or empty when OptionsSource fills them.</summary>
        public List<string> Options { get; set; } = new();
        /// <summary>e.g. "stages" — Manage Mods populates Options from the install.</summary>
        public string OptionsSource { get; set; } = "";
    }

    public sealed class ExpansionModEntry
    {
        public string Section { get; set; } = "";
        public string FolderPath { get; set; } = "";
        public string FolderName { get; set; } = "";
        public string Name { get; set; } = "";
        public string Creator { get; set; } = "";
        public string Type { get; set; } = "";
        public string Description { get; set; } = "";
        public bool Enabled { get; set; } = true;
        /// <summary>When false, the Manage Mods UI hides the enable checkbox.</summary>
        public bool CanToggleEnabled { get; set; } = true;
        public string? PreviewPath { get; set; }
        public List<ModSettingEntry> Settings { get; set; } = new();
    }

    /// <summary>
    /// Reads and writes expansions/*/info.json for the Manage Mods window.
    /// enabled:false is what the game checks so a pack does not load.
    /// </summary>
    public static class ExpansionModsService
    {
        private static readonly HashSet<string> SkipNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "decompiled", "resources", "_template"
        };

        private static readonly HashSet<string> SkipSections = new(StringComparer.OrdinalIgnoreCase)
        {
            "engine", "ssf2mm"
        };

        private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase)
        {
            ["controller_profiles"] = "Controller profiles + nametags",
            ["cpu_nametag"] = "CPU Nametag",
            ["css_sprite_preview"] = "CSS Sprite Preview",
            ["center_player_hud"] = "Center Player HUDs",
            ["debug"] = "Debug Tools",
            ["expansions"] = "Expansions",
            ["ice_climbers_mode"] = "Ice Climber Mode",
            ["manager_bridge"] = "Manager Bridge",
            ["menu_background"] = "Menu Background",
            ["menu_editor"] = "Menu Editor",
            ["camera_background"] = "Camera Background",
            ["mod_menu"] = "Mod Menu",
            ["music_player"] = "Music Player",
            ["palette_maker"] = "Palette Maker",
            ["rumble"] = "Controller Rumble",
            ["tug_of_war"] = "Stock Steal (Tug of War)",
            ["window_scale"] = "Window Scale Hotkeys"
        };

        public static string ExpansionsRoot(string shellRoot) =>
            Path.Combine(shellRoot, "expansions");

        public static List<(string Section, List<ExpansionModEntry> Mods)> Load(string shellRoot)
        {
            var root = ExpansionsRoot(shellRoot);
            var sections = new List<(string, List<ExpansionModEntry>)>();
            if (!Directory.Exists(root) && !Directory.Exists(ShellWorkspaceService.Ssf2mmDirectory(shellRoot)))
                return sections;

            var toggleMods = new List<ExpansionModEntry>();
            var essentials = new List<ExpansionModEntry>();
            var characters = new List<ExpansionModEntry>();
            var sounds = new List<ExpansionModEntry>();
            var stages = new List<ExpansionModEntry>();
            var events = new List<ExpansionModEntry>();

            // Toggleable packs: expansions/mods/<id>/ with mod.json (or info.json fallback).
            CollectPackFolder(Path.Combine(root, "mods"), "mods", essentials: false, toggleMods, essentials);
            CollectSongPacks(Path.Combine(root, "songs", "packs"), sounds);
            CollectContentFolder(Path.Combine(root, "stages"), "stages", stages);
            CollectContentFolder(Path.Combine(root, "events"), "events", events);
            // Always-on boot packs live in data/ssf2mm next to the game log.
            CollectPackFolder(Path.Combine(shellRoot, "data", "ssf2mm"), "essentials", essentials: true, toggleMods, essentials);
            // Characters: only real packs (assets.swf), never nested folder groups.
            CollectCharacters(Path.Combine(root, "character"), characters);

            if (toggleMods.Count > 0)
                sections.Add(("Mods", toggleMods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            if (characters.Count > 0)
                sections.Add(("Characters", characters.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            var alts = CollectAlts(shellRoot);
            if (alts.Count > 0)
                sections.Add(("Alts", alts));
            sections.Add(("Sounds", sounds.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            sections.Add(("Stages", stages.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            sections.Add(("Events", events.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            if (essentials.Count > 0)
                sections.Add(("Essentials", essentials.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList()));
            sections.Add(("Intros", CollectIntros(shellRoot)));

            try { CreditsService.Update(shellRoot); } catch { }
            return sections;
        }

        /// <summary>Alts from expansions/alts/alts.json, numbered per character the way Character Select names them.</summary>
        private static List<ExpansionModEntry> CollectAlts(string shellRoot)
        {
            var list = new List<ExpansionModEntry>();
            foreach (var group in CharacterAltsService.List(shellRoot).GroupBy(a => a.CharacterId, StringComparer.OrdinalIgnoreCase))
            {
                var n = 0;
                foreach (var alt in group)
                {
                    n++;
                    list.Add(new ExpansionModEntry
                    {
                        Section = "alts",
                        FolderPath = alt.Folder,
                        FolderName = Path.GetFileName(alt.Folder),
                        Name = string.IsNullOrWhiteSpace(alt.Name) ? $"{alt.CharacterId} Alt {n}" : alt.Name!,
                        Type = "Alt of " + alt.CharacterId,
                        Description = "On Character Select, put your token on " + alt.CharacterId
                            + " and press jump to switch to this alt."
                            + (alt.GameBananaId > 0 ? " GameBanana mod " + alt.GameBananaId + "." : ""),
                        CanToggleEnabled = false,
                        Enabled = true
                    });
                }
            }
            return list.OrderBy(m => m.Type, StringComparer.OrdinalIgnoreCase).ThenBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static List<ExpansionModEntry> CollectIntros(string shellRoot)
        {
            var list = new List<ExpansionModEntry>();
            foreach (var intro in ShellConfigureLaunchService.ListIntroFiles(shellRoot))
            {
                list.Add(new ExpansionModEntry
                {
                    Section = "intros",
                    FolderPath = intro.Path,
                    FolderName = intro.Id,
                    Name = string.IsNullOrWhiteSpace(intro.Label) ? intro.Id : intro.Label,
                    Type = "Intro",
                    CanToggleEnabled = false,
                    Enabled = true
                });
            }
            return list;
        }

        private static void CollectPackFolder(
            string sectionDir,
            string section,
            bool essentials,
            List<ExpansionModEntry> toggleMods,
            List<ExpansionModEntry> essentialsList)
        {
            if (!Directory.Exists(sectionDir))
                return;
            foreach (var folder in Directory.GetDirectories(sectionDir)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('.') || name.StartsWith('_') || SkipNames.Contains(name))
                    continue;
                // Nested folders / XP slot groups without a pack marker are ignored.
                if (!IsModPackFolder(folder))
                    continue;
                var entry = EnsureInfo(folder, section, forceEssential: essentials);
                if (essentials || !entry.CanToggleEnabled)
                    essentialsList.Add(entry);
                else
                    toggleMods.Add(entry);
            }
        }

        private static void CollectSongPacks(string packsDir, List<ExpansionModEntry> toggleMods)
        {
            if (!Directory.Exists(packsDir))
                return;
            foreach (var folder in Directory.GetDirectories(packsDir)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('.') || name.StartsWith('_'))
                    continue;
                if (!File.Exists(Path.Combine(folder, "list.json")))
                    continue;
                toggleMods.Add(EnsureInfo(folder, "music", forceEssential: false));
            }
        }

        private static void CollectContentFolder(string dir, string section, List<ExpansionModEntry> into)
        {
            if (!Directory.Exists(dir))
                return;
            foreach (var folder in Directory.GetDirectories(dir)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('.') || name.StartsWith('_') || SkipNames.Contains(name))
                    continue;
                var hasInfo = File.Exists(Path.Combine(folder, "info.json"));
                var hasSwf = Directory.GetFiles(folder, "*.swf").Length > 0;
                if (!hasInfo && !hasSwf)
                    continue;
                into.Add(EnsureInfo(folder, section, forceEssential: false));
            }
        }

        private static void CollectCharacters(string characterRoot, List<ExpansionModEntry> characters)
        {
            if (!Directory.Exists(characterRoot))
                return;
            foreach (var folder in Directory.GetDirectories(characterRoot)
                         .OrderBy(d => Path.GetFileName(d), StringComparer.OrdinalIgnoreCase))
            {
                var name = Path.GetFileName(folder);
                if (name.StartsWith('.') || name.StartsWith('_') || SkipNames.Contains(name))
                    continue;
                if (!IsCharacterPackFolder(folder))
                    continue;
                characters.Add(EnsureInfo(folder, "character", forceEssential: false));
            }
        }

        private static bool IsModPackFolder(string folder) =>
            File.Exists(Path.Combine(folder, "mod.json"))
            || File.Exists(Path.Combine(folder, "mod.swf"));

        private static bool IsCharacterPackFolder(string folder) =>
            File.Exists(Path.Combine(folder, "assets.swf"))
            || Directory.GetFiles(folder, "*.ssf").Length > 0
            || Directory.GetFiles(folder, "*.swf")
                .Any(f => !Path.GetFileName(f).Equals("assets.swf", StringComparison.OrdinalIgnoreCase));

        public static void Save(ExpansionModEntry entry)
        {
            var builtIn = entry.Section.Equals("essentials", StringComparison.OrdinalIgnoreCase);
            var path = Path.Combine(entry.FolderPath, builtIn ? "mod.json" : "info.json");
            JObject obj;
            if (File.Exists(path))
            {
                try { obj = JObject.Parse(File.ReadAllText(path)); }
                catch { obj = new JObject(); }
            }
            else obj = new JObject();

            if (!entry.CanToggleEnabled)
                entry.Enabled = true;
            if (!builtIn)
            {
                obj["name"] = entry.Name;
                obj["creator"] = entry.Creator;
                obj["type"] = entry.Type;
                obj["description"] = entry.Description;
                obj["enabled"] = entry.Enabled;
            }
            var settings = new JArray();
            foreach (var s in entry.Settings)
            {
                var row = new JObject
                {
                    ["id"] = s.Id,
                    ["label"] = s.Label,
                    ["type"] = s.Type,
                    ["value"] = s.Value
                };
                if (!string.IsNullOrWhiteSpace(s.OptionsSource))
                    row["optionsSource"] = s.OptionsSource;
                if (s.Options is { Count: > 0 })
                    row["options"] = new JArray(s.Options);
                settings.Add(row);
            }
            if (!builtIn || settings.Count > 0 || obj["settings"] != null)
                obj["settings"] = settings;
            File.WriteAllText(path, obj.ToString(Newtonsoft.Json.Formatting.Indented));
            try { CreditsService.Update(ShellRootFromPack(entry.FolderPath)); } catch { }
        }

        public static void Delete(ExpansionModEntry entry)
        {
            if (entry.Section.Equals("alts", StringComparison.OrdinalIgnoreCase))
            {
                var expansions = entry.FolderPath;
                while (!string.IsNullOrEmpty(expansions) && !Path.GetFileName(expansions).Equals("expansions", StringComparison.OrdinalIgnoreCase))
                    expansions = Path.GetDirectoryName(expansions);
                if (string.IsNullOrEmpty(expansions))
                    throw new InvalidOperationException("Could not find the Shell folder for this alt.");
                CharacterAltsService.Remove(Path.GetDirectoryName(expansions)!, entry.FolderPath);
                return;
            }
            var root = ShellRootFromPack(entry.FolderPath);
            if (!string.IsNullOrWhiteSpace(root))
            {
                try
                {
                    var ids = new List<string> { entry.FolderName };
                    var infoPath = Path.Combine(entry.FolderPath, "info.json");
                    if (File.Exists(infoPath))
                    {
                        var infoId = JObject.Parse(File.ReadAllText(infoPath))["id"]?.ToString();
                        if (!string.IsNullOrWhiteSpace(infoId))
                            ids.Add(infoId);
                    }
                    var path = MappingsEditService.GetPlaintextPath(root);
                    if (File.Exists(path))
                    {
                        var doc = JObject.Parse(File.ReadAllText(path));
                        MappingsEditService.RemoveMappedIds(doc, ids);
                        File.Copy(path, path + ".bak", overwrite: true);
                        File.WriteAllText(path, doc.ToString(Newtonsoft.Json.Formatting.Indented));
                    }
                }
                catch { }
            }
            if (Directory.Exists(entry.FolderPath))
                Directory.Delete(entry.FolderPath, recursive: true);
            if (!string.IsNullOrWhiteSpace(root))
                try { CreditsService.Update(root); } catch { }
        }

        private static string? ShellRootFromPack(string packFolder)
        {
            try
            {
                var dir = packFolder;
                while (!string.IsNullOrEmpty(dir))
                {
                    if (string.Equals(Path.GetFileName(dir), "expansions", StringComparison.OrdinalIgnoreCase))
                        return Path.GetDirectoryName(dir);
                    dir = Path.GetDirectoryName(dir);
                }
                return null;
            }
            catch
            {
                return null;
            }
        }

        private static ExpansionModEntry EnsureInfo(string folder, string section, bool forceEssential)
        {
            var infoPath = Path.Combine(folder, "info.json");
            var modPath = Path.Combine(folder, "mod.json");
            JObject? mod = null;
            if (File.Exists(modPath))
            {
                try { mod = JObject.Parse(File.ReadAllText(modPath)); }
                catch { mod = null; }
            }

            if (section.Equals("essentials", StringComparison.OrdinalIgnoreCase))
                return BuiltInEntry(folder, mod ?? new JObject());

            JObject info;
            if (File.Exists(infoPath))
            {
                try { info = JObject.Parse(File.ReadAllText(infoPath)); }
                catch { info = new JObject(); }
            }
            else info = new JObject();

            var folderName = Path.GetFileName(folder);
            var isCharacter = section.Equals("character", StringComparison.OrdinalIgnoreCase);
            var isShellPack = section.Equals("mods", StringComparison.OrdinalIgnoreCase)
                              || section.Equals("essentials", StringComparison.OrdinalIgnoreCase);
            var alwaysOn = forceEssential || section.Equals("essentials", StringComparison.OrdinalIgnoreCase);
            var dirty = false;

            if (DisplayNames.TryGetValue(folderName, out var displayName))
            {
                if (!string.Equals(info["name"]?.ToString(), displayName, StringComparison.Ordinal))
                {
                    info["name"] = displayName;
                    dirty = true;
                }
            }
            else if (info["name"] == null || string.IsNullOrWhiteSpace(info["name"]?.ToString()))
            {
                info["name"] = mod?["name"]?.ToString() ?? folderName;
                dirty = true;
            }

            if (isShellPack)
            {
                if (!string.Equals(info["creator"]?.ToString(), "SSF2 Mod Manager", StringComparison.Ordinal))
                {
                    info["creator"] = "SSF2 Mod Manager";
                    dirty = true;
                }
            }
            else if (info["creator"] == null)
            {
                info["creator"] = mod?["author"]?.ToString() ?? mod?["creator"]?.ToString() ?? "";
                dirty = true;
            }

            if (info["type"] == null || string.IsNullOrWhiteSpace(info["type"]?.ToString()))
            {
                info["type"] = TypeFor(section, mod);
                dirty = true;
            }
            if (info["description"] == null)
            {
                info["description"] = mod?["description"]?.ToString() ?? "";
                dirty = true;
            }

            if (alwaysOn)
            {
                if (info["enabled"]?.Value<bool?>() != true)
                {
                    info["enabled"] = true;
                    dirty = true;
                }
            }
            else if (info["enabled"] == null)
            {
                info["enabled"] = true;
                dirty = true;
            }

            if (dirty || !File.Exists(infoPath))
                File.WriteAllText(infoPath, info.ToString(Newtonsoft.Json.Formatting.Indented));

            string? preview = null;
            if (isCharacter)
                preview = CharacterSelectPreviewService.TryGetCachedPreview(folder);
            if (string.IsNullOrWhiteSpace(preview))
            {
                var modPng = Path.Combine(folder, "mod.png");
                if (File.Exists(modPng))
                    preview = modPng;
            }

            return new ExpansionModEntry
            {
                Section = section,
                FolderPath = folder,
                FolderName = folderName,
                Name = info["name"]?.ToString() ?? folderName,
                Creator = info["creator"]?.ToString() ?? "",
                Type = info["type"]?.ToString() ?? section,
                Description = info["description"]?.ToString() ?? "",
                Enabled = info["enabled"]?.Value<bool?>() ?? true,
                CanToggleEnabled = !alwaysOn,
                PreviewPath = preview,
                Settings = ReadSettings(info["settings"] as JArray)
            };
        }

        /// <summary>
        /// Built-in packs in data/ssf2mm are always on and keep everything in mod.json.
        /// No info.json is written for them.
        /// </summary>
        private static ExpansionModEntry BuiltInEntry(string folder, JObject mod)
        {
            var folderName = Path.GetFileName(folder);
            var name = DisplayNames.TryGetValue(folderName, out var displayName)
                ? displayName
                : mod["name"]?.ToString() ?? folderName;
            var modPng = Path.Combine(folder, "mod.png");
            return new ExpansionModEntry
            {
                Section = "essentials",
                FolderPath = folder,
                FolderName = folderName,
                Name = name,
                Creator = "SSF2 Mod Manager",
                Type = "Essential",
                Description = mod["description"]?.ToString() ?? "",
                Enabled = true,
                CanToggleEnabled = false,
                PreviewPath = File.Exists(modPng) ? modPng : null,
                Settings = ReadSettings(mod["settings"] as JArray)
            };
        }

        private static List<ModSettingEntry> ReadSettings(JArray? settings)
        {
            var list = new List<ModSettingEntry>();
            if (settings == null)
                return list;
            foreach (var token in settings)
            {
                if (token is not JObject row)
                    continue;
                list.Add(new ModSettingEntry
                {
                    Id = row["id"]?.ToString() ?? "",
                    Label = row["label"]?.ToString() ?? row["id"]?.ToString() ?? "Setting",
                    Type = row["type"]?.ToString() ?? "string",
                    Value = row["value"]?.ToString() ?? "",
                    OptionsSource = row["optionsSource"]?.ToString() ?? "",
                    Options = ReadOptions(row["options"] as JArray)
                });
            }
            return list;
        }

        private static List<string> ReadOptions(JArray? options)
        {
            var list = new List<string>();
            if (options == null)
                return list;
            foreach (var token in options)
            {
                var s = token?.ToString();
                if (!string.IsNullOrWhiteSpace(s))
                    list.Add(s);
            }
            return list;
        }

        private static string TypeFor(string section, JObject? mod)
        {
            if (!string.IsNullOrWhiteSpace(mod?["type"]?.ToString()))
                return mod!["type"]!.ToString();
            return section.ToLowerInvariant() switch
            {
                "mods" => "Mod",
                "essentials" => "Essential",
                "character" => "Character",
                "music" => "Music Pack",
                _ => section
            };
        }
    }

    /// <summary>
    /// One-time FFDec export of linkage <c>select</c> → <c>select.png</c> beside assets.swf.
    /// Manage Mods reads the cache on the UI thread; missing previews are filled in the background.
    /// </summary>
    public static class CharacterSelectPreviewService
    {
        public const string PreviewFileName = "select.png";
        private const string LegacyPreviewFileName = "select_preview.png";
        private static readonly SemaphoreSlim Gate = new(1, 1);

        /// <summary>Cached preview only (no FFDec). Safe for UI thread.</summary>
        public static string? TryGetCachedPreview(string characterFolder)
        {
            if (string.IsNullOrWhiteSpace(characterFolder) || !Directory.Exists(characterFolder))
                return null;
            try
            {
                MigrateLegacyPreview(characterFolder);
                var preview = Path.Combine(characterFolder, PreviewFileName);
                if (File.Exists(preview) && new FileInfo(preview).Length > 64)
                {
                    TrimTransparentPadding(preview);
                    return preview;
                }
            }
            catch { /* ignore */ }
            return null;
        }

        public static bool NeedsExport(string characterFolder)
        {
            if (string.IsNullOrWhiteSpace(characterFolder) || !Directory.Exists(characterFolder))
                return false;
            var assets = Path.Combine(characterFolder, "assets.swf");
            if (!File.Exists(assets))
                return false;
            MigrateLegacyPreview(characterFolder);
            var preview = Path.Combine(characterFolder, PreviewFileName);
            try
            {
                if (File.Exists(preview)
                    && new FileInfo(preview).Length > 64
                    && File.GetLastWriteTimeUtc(preview) >= File.GetLastWriteTimeUtc(assets))
                    return false;
            }
            catch { return true; }
            return true;
        }

        public static async Task<string?> EnsurePreviewAsync(
            string characterFolder,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(characterFolder) || !Directory.Exists(characterFolder))
                return null;
            var assets = Path.Combine(characterFolder, "assets.swf");
            if (!File.Exists(assets))
                return null;

            MigrateLegacyPreview(characterFolder);
            var preview = Path.Combine(characterFolder, PreviewFileName);
            try
            {
                if (File.Exists(preview)
                    && File.GetLastWriteTimeUtc(preview) >= File.GetLastWriteTimeUtc(assets)
                    && new FileInfo(preview).Length > 64)
                    return preview;

                await Gate.WaitAsync(cancellationToken).ConfigureAwait(false);
                try
                {
                    // Re-check after lock (another job may have finished).
                    if (File.Exists(preview)
                        && File.GetLastWriteTimeUtc(preview) >= File.GetLastWriteTimeUtc(assets)
                        && new FileInfo(preview).Length > 64)
                        return preview;

                    if (!await TryExportSelectPngAsync(assets, preview, cancellationToken).ConfigureAwait(false))
                        return File.Exists(preview) ? preview : null;
                    return File.Exists(preview) ? preview : null;
                }
                finally
                {
                    Gate.Release();
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch
            {
                return File.Exists(preview) ? preview : null;
            }
        }

        /// <summary>
        /// Export missing/stale select.png for each folder. Invokes <paramref name="onReady"/>
        /// on the caller's sync context when provided (pass a UI dispatcher callback).
        /// </summary>
        public static async Task WarmPreviewsAsync(
            IEnumerable<string> characterFolders,
            Action<string, string>? onReady = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var pending = characterFolders
                .Where(NeedsExport)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (pending.Count == 0)
                return;

            progress?.Report($"Building {pending.Count} character preview(s)…");
            var done = 0;
            foreach (var folder in pending)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var path = await EnsurePreviewAsync(folder, cancellationToken).ConfigureAwait(false);
                done++;
                if (!string.IsNullOrWhiteSpace(path))
                    onReady?.Invoke(folder, path);
                progress?.Report($"Character previews {done}/{pending.Count}");
            }
            progress?.Report(pending.Count == 1
                ? "Character preview ready."
                : "Character previews ready.");
        }

        private static void MigrateLegacyPreview(string characterFolder)
        {
            var modern = Path.Combine(characterFolder, PreviewFileName);
            var legacy = Path.Combine(characterFolder, LegacyPreviewFileName);
            try
            {
                if (File.Exists(modern) || !File.Exists(legacy))
                    return;
                File.Copy(legacy, modern, overwrite: false);
            }
            catch { /* ignore */ }
        }

        private static async Task<bool> TryExportSelectPngAsync(
            string assetsSwf, string previewPath, CancellationToken cancellationToken)
        {
            var jpexs = new JpexsCliService();
            if (jpexs.ResolveExecutableOrBundled("") == null)
                return false;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-select-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var selectId = await ResolveSelectCharacterIdAsync(jpexs, assetsSwf, work, cancellationToken)
                    .ConfigureAwait(false);
                var spriteDir = Path.Combine(work, "sprite");
                Directory.CreateDirectory(spriteDir);

                // -selectid is fast; -selectclass "select" also dumps charselect etc.
                var args = selectId > 0
                    ? $"-selectid {selectId} -format sprite:png -export sprite \"{spriteDir}\" \"{assetsSwf}\""
                    : $"-selectclass select -format sprite:png -export sprite \"{spriteDir}\" \"{assetsSwf}\"";
                await jpexs.RunFfdecAsync("", args, cancellationToken, TimeSpan.FromSeconds(45))
                    .ConfigureAwait(false);

                var png = FindSelectPng(spriteDir);
                if (png == null || !File.Exists(png))
                    return false;
                File.Copy(png, previewPath, overwrite: true);
                TrimTransparentPadding(previewPath);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
            }
        }

        private static async Task<int> ResolveSelectCharacterIdAsync(
            JpexsCliService jpexs, string assetsSwf, string work, CancellationToken cancellationToken)
        {
            try
            {
                var symDir = Path.Combine(work, "sym");
                Directory.CreateDirectory(symDir);
                await jpexs.ExportAsync("", assetsSwf, symDir, "symbolClass", cancellationToken)
                    .ConfigureAwait(false);
                var csv = Directory.GetFiles(symDir, "*.csv", SearchOption.AllDirectories).FirstOrDefault();
                if (csv == null)
                    return 0;
                foreach (Match m in Regex.Matches(
                             await File.ReadAllTextAsync(csv, cancellationToken).ConfigureAwait(false),
                             @"^\s*(?<id>\d+);""?(?<n>[^"";\r\n]+)""?\s*$",
                             RegexOptions.Multiline))
                {
                    if (string.Equals(m.Groups["n"].Value.Trim(), "select", StringComparison.Ordinal))
                        return int.Parse(m.Groups["id"].Value);
                }
            }
            catch { /* fall through */ }
            return 0;
        }

        private static string? FindSelectPng(string spriteDir)
        {
            if (!Directory.Exists(spriteDir))
                return null;
            foreach (var dir in Directory.GetDirectories(spriteDir, "*", SearchOption.AllDirectories))
            {
                var name = Path.GetFileName(dir);
                if (name.EndsWith("_select", StringComparison.OrdinalIgnoreCase)
                    && name.IndexOf("charselect", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    var png = Directory.GetFiles(dir, "*.png").OrderBy(f => f).FirstOrDefault();
                    if (png != null)
                        return png;
                }
            }
            return Directory.GetFiles(spriteDir, "*.png", SearchOption.AllDirectories)
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
        }

        /// <summary>
        /// FFDec select sprites often sit on a large transparent canvas — crop to opaque
        /// content so Manage Mods thumbnails fill the card.
        /// </summary>
        public static void TrimTransparentPadding(string pngPath, byte alphaThreshold = 12)
        {
            if (string.IsNullOrWhiteSpace(pngPath) || !File.Exists(pngPath))
                return;
            try
            {
                byte[] bytes = File.ReadAllBytes(pngPath);
                using var inMs = new MemoryStream(bytes);
                using var src = new System.Drawing.Bitmap(inMs);
                var w = src.Width;
                var h = src.Height;
                if (w < 2 || h < 2)
                    return;

                var minX = w;
                var minY = h;
                var maxX = -1;
                var maxY = -1;
                for (var y = 0; y < h; y++)
                {
                    for (var x = 0; x < w; x++)
                    {
                        if (src.GetPixel(x, y).A <= alphaThreshold)
                            continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }
                if (maxX < minX || maxY < minY)
                    return;

                // Already tight enough (less than ~8% padding on any side).
                var padL = minX;
                var padT = minY;
                var padR = w - 1 - maxX;
                var padB = h - 1 - maxY;
                var maxPad = Math.Max(Math.Max(padL, padR), Math.Max(padT, padB));
                if (maxPad <= Math.Max(2, Math.Min(w, h) / 12))
                    return;

                var cw = maxX - minX + 1;
                var ch = maxY - minY + 1;
                using var cropped = src.Clone(
                    new System.Drawing.Rectangle(minX, minY, cw, ch),
                    src.PixelFormat);
                using var outMs = new MemoryStream();
                cropped.Save(outMs, System.Drawing.Imaging.ImageFormat.Png);
                File.WriteAllBytes(pngPath, outMs.ToArray());
            }
            catch { /* leave original */ }
        }
    }
}
