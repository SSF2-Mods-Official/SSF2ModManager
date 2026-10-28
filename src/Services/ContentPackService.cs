using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public enum ContentPackType
    {
        Character,
        Stage,
        Music
    }

    public sealed class ContentPackInfo
    {
        public string PackId { get; set; } = "";
        public ContentPackType Type { get; set; }
        public string Name { get; set; } = "";
        public string Guid { get; set; } = "";
        public int Slot { get; set; } = -1;
        public string PlayableId { get; set; } = "";
        public string ContentRelativePath { get; set; } = "";
        public string? SeriesIcon { get; set; }
        public List<string> AttachToStages { get; set; } = new();
        public string RosterSlotId =>
            Type == ContentPackType.Stage
                ? SlotPoolService.StageSlotId(Slot)
                : SlotPoolService.CharacterSlotId(Slot);
    }

    public sealed class ContentPackIndex
    {
        public int CharacterSlotCount { get; set; } = SlotPoolService.DefaultCharacterSlots;
        public int StageSlotCount { get; set; } = SlotPoolService.DefaultStageSlots;
        public Dictionary<string, string> CharacterSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> StageSlots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public List<string> Packs { get; set; } = new();
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }

    public sealed class ContentPackPlaylists
    {
        public Dictionary<string, List<string>> Stages { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Content packs live in <c>expansions/packs/&lt;id&gt;</c>; the registry (<c>index.json</c>, <c>live.json</c>)
    /// stays in <c>data/ssf2mm</c>. Adds never mutate stock menu hosts.
    /// </summary>
    public sealed class ContentPackService
    {
        public const string RootFolderName = "ssf2mm";
        public const string IndexFileName = "index.json";
        public const string PlaylistsFileName = "playlists.json";
        public const string PacksFolderName = "packs";
        public const string PackJsonFileName = "pack.json";
        public const string ContentSwfFileName = "content.swf";
        public const string PacksFileFieldPrefix = "expansions/packs/";
        private const string LegacyFileFieldPrefix = "ssf2mm/packs/";

        public static string RootDir(string installRoot) =>
            ShellWorkspaceService.Ssf2mmDirectory(installRoot);

        public static string IndexPath(string installRoot) =>
            Path.Combine(RootDir(installRoot), IndexFileName);

        public static string PlaylistsPath(string installRoot) =>
            Path.Combine(RootDir(installRoot), PlaylistsFileName);

        public static string PacksDir(string installRoot) =>
            Path.Combine(ShellWorkspaceService.ExpansionsRoot(installRoot), PacksFolderName);

        public static string LegacyPacksDir(string installRoot) =>
            Path.Combine(RootDir(installRoot), PacksFolderName);

        public static string PackDir(string installRoot, string packId) =>
            Path.Combine(PacksDir(installRoot), MenuSlotService.SanitizeId(packId));

        public static void EnsureRoot(string installRoot)
        {
            Directory.CreateDirectory(RootDir(installRoot));
            Directory.CreateDirectory(PacksDir(installRoot));
            MigrateLegacyPacks(installRoot);
        }

        /// <summary>Moves <c>data/ssf2mm/packs/&lt;id&gt;</c> into <c>expansions/packs</c> and rewrites old file fields.</summary>
        public static int MigrateLegacyPacks(string installRoot)
        {
            var legacy = LegacyPacksDir(installRoot);
            if (!Directory.Exists(legacy))
                return 0;
            var moved = 0;
            foreach (var dir in Directory.GetDirectories(legacy))
            {
                var dest = Path.Combine(PacksDir(installRoot), Path.GetFileName(dir));
                if (Directory.Exists(dest))
                    continue;
                Directory.Move(dir, dest);
                var json = Path.Combine(dest, PackJsonFileName);
                if (File.Exists(json))
                {
                    var text = File.ReadAllText(json);
                    if (text.Contains(LegacyFileFieldPrefix, StringComparison.OrdinalIgnoreCase))
                        File.WriteAllText(json, text.Replace(LegacyFileFieldPrefix, PacksFileFieldPrefix, StringComparison.OrdinalIgnoreCase));
                }
                moved++;
            }
            if (!Directory.EnumerateFileSystemEntries(legacy).Any())
                Directory.Delete(legacy);
            return moved;
        }

        public static ContentPackIndex LoadIndex(string installRoot)
        {
            EnsureRoot(installRoot);
            var path = IndexPath(installRoot);
            if (!File.Exists(path))
            {
                var fresh = new ContentPackIndex();
                SaveIndex(installRoot, fresh);
                return fresh;
            }
            try
            {
                var idx = JsonConvert.DeserializeObject<ContentPackIndex>(File.ReadAllText(path))
                          ?? new ContentPackIndex();
                idx.CharacterSlots ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                idx.StageSlots ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                idx.Packs ??= new List<string>();
                return idx;
            }
            catch
            {
                return new ContentPackIndex();
            }
        }

        public static void SaveIndex(string installRoot, ContentPackIndex index)
        {
            EnsureRoot(installRoot);
            index.UpdatedAt = DateTimeOffset.UtcNow;
            File.WriteAllText(
                IndexPath(installRoot),
                JsonConvert.SerializeObject(index, Formatting.Indented));
        }

        public static ContentPackPlaylists LoadPlaylists(string installRoot)
        {
            EnsureRoot(installRoot);
            var path = PlaylistsPath(installRoot);
            if (!File.Exists(path))
                return new ContentPackPlaylists();
            try
            {
                return JsonConvert.DeserializeObject<ContentPackPlaylists>(File.ReadAllText(path))
                       ?? new ContentPackPlaylists();
            }
            catch
            {
                return new ContentPackPlaylists();
            }
        }

        public static void SavePlaylists(string installRoot, ContentPackPlaylists playlists)
        {
            EnsureRoot(installRoot);
            File.WriteAllText(
                PlaylistsPath(installRoot),
                JsonConvert.SerializeObject(playlists, Formatting.Indented));
        }

        public static ContentPackInfo? LoadPack(string installRoot, string packId)
        {
            var dir = PackDir(installRoot, packId);
            var jsonPath = Path.Combine(dir, PackJsonFileName);
            if (!File.Exists(jsonPath)) return null;
            try
            {
                return JsonConvert.DeserializeObject<ContentPackInfo>(File.ReadAllText(jsonPath));
            }
            catch
            {
                return null;
            }
        }

        public static void SavePack(string installRoot, ContentPackInfo pack)
        {
            pack.PackId = MenuSlotService.SanitizeId(pack.PackId);
            var dir = PackDir(installRoot, pack.PackId);
            Directory.CreateDirectory(dir);
            Directory.CreateDirectory(Path.Combine(dir, "assets"));
            File.WriteAllText(
                Path.Combine(dir, PackJsonFileName),
                JsonConvert.SerializeObject(pack, Formatting.Indented));
        }

        public static int ClaimCharacterSlot(ContentPackIndex index, string packId)
        {
            packId = MenuSlotService.SanitizeId(packId);
            foreach (var kv in index.CharacterSlots)
            {
                if (kv.Value.Equals(packId, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(kv.Key, out var existing))
                    return existing;
            }
            for (var i = 0; i < index.CharacterSlotCount; i++)
            {
                var key = i.ToString();
                if (!index.CharacterSlots.ContainsKey(key))
                {
                    index.CharacterSlots[key] = packId;
                    if (!index.Packs.Contains(packId, StringComparer.OrdinalIgnoreCase))
                        index.Packs.Add(packId);
                    return i;
                }
            }
            throw new InvalidOperationException(
                $"No free character slots (0..{index.CharacterSlotCount - 1}). Expand the slot pool.");
        }

        public static int ClaimStageSlot(ContentPackIndex index, string packId)
        {
            packId = MenuSlotService.SanitizeId(packId);
            foreach (var kv in index.StageSlots)
            {
                if (kv.Value.Equals(packId, StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(kv.Key, out var existing))
                    return existing;
            }
            for (var i = 0; i < index.StageSlotCount; i++)
            {
                var key = i.ToString();
                if (!index.StageSlots.ContainsKey(key))
                {
                    index.StageSlots[key] = packId;
                    if (!index.Packs.Contains(packId, StringComparer.OrdinalIgnoreCase))
                        index.Packs.Add(packId);
                    return i;
                }
            }
            throw new InvalidOperationException(
                $"No free stage slots (0..{index.StageSlotCount - 1}). Expand the slot pool.");
        }

        public static void RegisterMusicPack(ContentPackIndex index, string packId)
        {
            packId = MenuSlotService.SanitizeId(packId);
            if (!index.Packs.Contains(packId, StringComparer.OrdinalIgnoreCase))
                index.Packs.Add(packId);
        }

        public static void AttachTrackToStagePlaylist(string installRoot, string stageId, string trackId)
        {
            stageId = MenuSlotService.SanitizeId(stageId);
            trackId = MenuSlotService.SanitizeId(trackId);
            var lists = LoadPlaylists(installRoot);
            if (!lists.Stages.TryGetValue(stageId, out var tracks) || tracks == null)
            {
                tracks = new List<string>();
                lists.Stages[stageId] = tracks;
            }
            if (!tracks.Contains(trackId, StringComparer.OrdinalIgnoreCase))
                tracks.Add(trackId);
            SavePlaylists(installRoot, lists);
        }

        /// <summary>
        /// Relative path from <c>data/</c> for mappings <c>file</c> field.
        /// </summary>
        public static string ContentFileRelativeFromData(string packId) =>
            $"{PacksFileFieldPrefix}{MenuSlotService.SanitizeId(packId)}/{ContentSwfFileName}";

        public static string ContentFullPath(string installRoot, string packId) =>
            Path.Combine(PackDir(installRoot, packId), ContentSwfFileName);

        /// <summary>
        /// Merges pack registry into mappings document (roster entries + CSS/SSS rows).
        /// Does not touch menu host SWFs.
        /// </summary>
        public static void ApplyPacksToMappings(string installRoot, JObject doc)
        {
            var index = LoadIndex(installRoot);
            var meta = new JObject();
            var packArr = new JArray();

            foreach (var packId in index.Packs.ToList())
            {
                var pack = LoadPack(installRoot, packId);
                if (pack == null) continue;

                var contentRel = string.IsNullOrWhiteSpace(pack.ContentRelativePath)
                    ? ContentFileRelativeFromData(pack.PackId)
                    : pack.ContentRelativePath.Replace('\\', '/');
                // Resolve on-disk path the same way mappings do (category-aware).
                var categoryHint = pack.Type switch
                {
                    ContentPackType.Stage => "stage",
                    ContentPackType.Music => "music",
                    _ => "character"
                };
                var contentFull = Path.Combine(
                    installRoot, "data",
                    DatMappingService.BuildDataRelativeSwfPath(categoryHint, contentRel));
                if (!File.Exists(contentFull))
                    contentFull = ContentFullPath(installRoot, pack.PackId);
                var exists = File.Exists(contentFull);

                if (pack.Type == ContentPackType.Character && pack.Slot >= 0)
                {
                    var rosterId = SlotPoolService.CharacterSlotId(pack.Slot);
                    var playable = string.IsNullOrWhiteSpace(pack.PlayableId) ? pack.PackId : pack.PlayableId;
                    MappingsEditService.UpsertEntry(
                        doc, "character", rosterId, contentRel,
                        name: string.IsNullOrWhiteSpace(pack.Name) ? pack.PackId : pack.Name,
                        seriesIcon: pack.SeriesIcon,
                        guid: string.IsNullOrWhiteSpace(pack.Guid) ? null : pack.Guid);
                    // Playable id entry (fighter load / stats) when different from roster slot id.
                    if (!playable.Equals(rosterId, StringComparison.OrdinalIgnoreCase))
                    {
                        MappingsEditService.UpsertEntry(
                            doc, "character", playable, contentRel,
                            name: string.IsNullOrWhiteSpace(pack.Name) ? playable : pack.Name,
                            seriesIcon: pack.SeriesIcon,
                            guid: string.IsNullOrWhiteSpace(pack.Guid) ? null : pack.Guid);
                    }
                    EnsureIdInCssRows(doc, rosterId);
                    packArr.Add(new JObject
                    {
                        ["type"] = "character",
                        ["id"] = pack.PackId,
                        ["name"] = string.IsNullOrWhiteSpace(pack.Name) ? pack.PackId : pack.Name,
                        ["slot"] = pack.Slot,
                        ["rosterId"] = rosterId,
                        ["playableId"] = playable,
                        ["file"] = contentRel,
                        ["exists"] = exists
                    });
                }
                else if (pack.Type == ContentPackType.Stage && pack.Slot >= 0)
                {
                    var rosterId = SlotPoolService.StageSlotId(pack.Slot);
                    MappingsEditService.UpsertEntry(
                        doc, "stage", rosterId, contentRel,
                        name: string.IsNullOrWhiteSpace(pack.Name) ? pack.PackId : pack.Name,
                        guid: string.IsNullOrWhiteSpace(pack.Guid) ? null : pack.Guid);
                    var playable = string.IsNullOrWhiteSpace(pack.PlayableId) ? pack.PackId : pack.PlayableId;
                    if (!playable.Equals(rosterId, StringComparison.OrdinalIgnoreCase))
                    {
                        MappingsEditService.UpsertEntry(
                            doc, "stage", playable, contentRel,
                            name: string.IsNullOrWhiteSpace(pack.Name) ? playable : pack.Name,
                            guid: string.IsNullOrWhiteSpace(pack.Guid) ? null : pack.Guid);
                    }
                    EnsureIdInSssRows(doc, rosterId);
                    packArr.Add(new JObject
                    {
                        ["type"] = "stage",
                        ["id"] = pack.PackId,
                        ["slot"] = pack.Slot,
                        ["rosterId"] = rosterId,
                        ["playableId"] = playable,
                        ["file"] = contentRel,
                        ["exists"] = exists
                    });
                }
                else if (pack.Type == ContentPackType.Music)
                {
                    MappingsEditService.UpsertEntry(
                        doc, "music", pack.PackId, contentRel,
                        name: string.IsNullOrWhiteSpace(pack.Name) ? pack.PackId : pack.Name,
                        guid: string.IsNullOrWhiteSpace(pack.Guid) ? null : pack.Guid);
                    packArr.Add(new JObject
                    {
                        ["type"] = "music",
                        ["id"] = pack.PackId,
                        ["file"] = contentRel,
                        ["exists"] = exists,
                        ["attachToStages"] = new JArray(pack.AttachToStages ?? new List<string>())
                    });
                }
            }

            var playlists = LoadPlaylists(installRoot);
            var plObj = new JObject();
            foreach (var kv in playlists.Stages)
                plObj[kv.Key] = new JArray(kv.Value);

            meta["packs"] = packArr;
            meta["playlists"] = plObj;
            meta["characterSlotCount"] = index.CharacterSlotCount;
            meta["stageSlotCount"] = index.StageSlotCount;
            doc["ssf2mm"] = meta;

            // Sidecar for inject-once runtime (SSF2API reads this; no host SWF mutation on add).
            try
            {
                EnsureRoot(installRoot);
                var live = new JObject
                {
                    ["packs"] = packArr.DeepClone(),
                    ["updatedAt"] = DateTimeOffset.UtcNow.ToString("o")
                };
                File.WriteAllText(
                    Path.Combine(RootDir(installRoot), "live.json"),
                    live.ToString(Formatting.Indented));
            }
            catch
            {
                /* non-fatal */
            }
        }

        private static void EnsureIdInCssRows(JObject doc, string rosterId)
        {
            var rows = MappingsEditService.GetCssRows(doc);
            if (rows.SelectMany(r => r).Any(id => id.Equals(rosterId, StringComparison.OrdinalIgnoreCase)))
                return;
            if (rows.Count == 0)
                rows.Add(new List<string>());
            // Append to last row if room, else new row.
            var last = rows[^1];
            if (last.Count >= MappingsEditService.CssMaxColsPerRow)
            {
                if (rows.Count >= MappingsEditService.CssMaxRows)
                    throw new InvalidOperationException("CSS is full; cannot place pack slot.");
                last = new List<string>();
                rows.Add(last);
            }
            last.Add(rosterId);
            MappingsEditService.SetCssRows(doc, rows);
        }

        private static void EnsureIdInSssRows(JObject doc, string rosterId)
        {
            var rows = MappingsEditService.GetSssNormalRows(doc);
            if (rows.SelectMany(r => r).Any(id => id.Equals(rosterId, StringComparison.OrdinalIgnoreCase)))
                return;
            if (rows.Count == 0)
                rows.Add(new List<string>());
            rows[^1].Add(rosterId);
            MappingsEditService.SetSssNormalRows(doc, rows);
        }

        public async Task CommitPacksAsync(
            string installRoot,
            JObject doc,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("Merging content packs into mappings…");
            ApplyPacksToMappings(installRoot, doc);
            var mappings = new MappingsEditService();
            await mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
        }
    }
}
