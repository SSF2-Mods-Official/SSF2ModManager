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
    public class CachedSongInfo
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public string Artist { get; set; } = "";
        public string SwfPath { get; set; } = "";
        public int LoopMs { get; set; }
        /// <summary>True after <c>musicLoop</c> was read. Stops the next launch from opening every file.</summary>
        public bool LoopChecked { get; set; }
        public string? PreviewMp3Path { get; set; }

        public string DisplayLabel
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Name) ? Id : Name;
                if (string.IsNullOrWhiteSpace(Artist)) return name;
                return $"{name} — {Artist}";
            }
        }
    }

    public class CachedStagePlaylist
    {
        public string StageId { get; set; } = "";
        public string StageName { get; set; } = "";
        public List<string> TrackIds { get; set; } = new();
    }

    public class UserMusicPlaylist
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        /// <summary>custom or series.</summary>
        public string Kind { get; set; } = "custom";
        public List<string> TrackIds { get; set; } = new();
    }

    public class MusicLibraryCache
    {
        public const int CurrentSchema = 4;
        public string InstallRoot { get; set; } = "";
        public DateTime BuiltUtc { get; set; }
        public int SchemaVersion { get; set; }
        /// <summary>True after a full binary enrichment pass finished.</summary>
        public bool EnrichmentComplete { get; set; }
        public List<CachedSongInfo> Songs { get; set; } = new();
        public List<CachedStagePlaylist> StagePlaylists { get; set; } = new();
        public List<UserMusicPlaylist> UserPlaylists { get; set; } = new();
        public string? LastPlayedSongId { get; set; }
        public List<string> LastPlayedQueueIds { get; set; } = new();
    }

    public readonly struct MusicCacheProgress
    {
        public MusicCacheProgress(string message, int done, int total, MusicLibraryCache? snapshot = null)
        {
            Message = message;
            Done = done;
            Total = total;
            Snapshot = snapshot;
        }

        public string Message { get; }
        public int Done { get; }
        public int Total { get; }
        public MusicLibraryCache? Snapshot { get; }
        public double Percent => Total <= 0 ? 0 : Math.Clamp(100.0 * Done / Total, 0, 100);
    }

    /// <summary>
    /// AppData cache of music metadata, stage playlists, and user playlists.
    /// Uses fast SWF string-pool scanning (no FFDec) so builds finish in seconds.
    /// </summary>
    public class MusicLibraryCacheService
    {
        private readonly MusicEditService _music;
        private readonly JpexsCliService _jpexs;
        private readonly MappingsEditService _mappings;

        public MusicLibraryCacheService(
            MusicEditService? music = null,
            JpexsCliService? jpexs = null,
            MappingsEditService? mappings = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _mappings = mappings ?? new MappingsEditService(_jpexs);
            _music = music ?? new MusicEditService(_jpexs, _mappings);
        }

        /// <summary>Test hook — when set, Load/Save use this path instead of AppData.</summary>
        public static string? CacheFilePathOverride { get; set; }

        public static string CacheFilePath =>
            CacheFilePathOverride ?? Path.Combine(AppPaths.AppDataDir, "music-library-cache.json");
        public static string PreviewCacheDir
        {
            get
            {
                var d = Path.Combine(AppPaths.AppDataDir, "music-previews");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public MusicLibraryCache? Load(string installRoot)
        {
            if (!File.Exists(CacheFilePath)) return null;
            try
            {
                var cache = JsonConvert.DeserializeObject<MusicLibraryCache>(File.ReadAllText(CacheFilePath));
                if (cache == null) return null;
                if (!string.Equals(
                        Path.GetFullPath(cache.InstallRoot.TrimEnd('\\')),
                        Path.GetFullPath(installRoot.TrimEnd('\\')),
                        StringComparison.OrdinalIgnoreCase))
                    return null;
                cache.UserPlaylists ??= new List<UserMusicPlaylist>();
                cache.LastPlayedQueueIds ??= new List<string>();
                return cache;
            }
            catch { return null; }
        }

        public void Save(MusicLibraryCache cache)
        {
            cache.BuiltUtc = DateTime.UtcNow;
            File.WriteAllText(CacheFilePath, JsonConvert.SerializeObject(cache, Formatting.Indented));
        }

        /// <summary>
        /// Instant song list for the launcher: AppData cache, else Shell <c>music_player/list.json</c>,
        /// else the bundled stock seed. Never exports mappings or scans SWF binaries.
        /// </summary>
        public MusicLibraryCache EnsureFastLibrary(string installRoot)
        {
            var existing = Load(installRoot);
            if (existing?.Songs is { Count: > 0 })
            {
                var named = ApplyStockNames(existing.Songs, installRoot);
                var paths = PreferPlainSwfPaths(existing.Songs, installRoot);
                var loops = ApplyLoopMsFromDisk(existing.Songs);
                var before = existing.Songs.Count;
                AppendExpansionSongs(installRoot, existing.Songs, existing.Songs);
                if (named || paths || loops || existing.Songs.Count != before)
                {
                    existing.Songs = existing.Songs
                        .OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToList();
                    Save(existing);
                }
                return existing;
            }

            var stock = LoadStockSeedEntries(installRoot);
            var onDisk = IndexSoundFiles(installRoot);
            var previous = existing?.Songs ?? new List<CachedSongInfo>();
            var songs = new List<CachedSongInfo>(stock.Count);
            foreach (var entry in stock)
            {
                if (!onDisk.TryGetValue(entry.Id, out var swf))
                    continue;
                var prev = previous.FirstOrDefault(s =>
                    s.Id.Equals(entry.Id, StringComparison.OrdinalIgnoreCase));
                songs.Add(new CachedSongInfo
                {
                    Id = entry.Id,
                    Name = entry.Name,
                    Artist = string.IsNullOrWhiteSpace(entry.Artist) ? (prev?.Artist ?? "") : entry.Artist,
                    SwfPath = swf,
                    LoopMs = entry.LoopMs > 0 ? entry.LoopMs : (prev?.LoopMs ?? 0),
                    PreviewMp3Path = prev?.PreviewMp3Path is { } p && File.Exists(p) ? p : null
                });
            }

            AppendExpansionSongs(installRoot, songs, previous);
            ApplyLoopMsFromDisk(songs);

            var cache = new MusicLibraryCache
            {
                InstallRoot = installRoot,
                Songs = songs.OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToList(),
                StagePlaylists = existing?.StagePlaylists ?? new List<CachedStagePlaylist>(),
                UserPlaylists = existing?.UserPlaylists ?? new List<UserMusicPlaylist>(),
                LastPlayedSongId = existing?.LastPlayedSongId,
                LastPlayedQueueIds = existing?.LastPlayedQueueIds ?? new List<string>(),
                // Song list is ready; skip the heavy SWF enrichment pass for the mini player.
                EnrichmentComplete = true,
                SchemaVersion = MusicLibraryCache.CurrentSchema
            };
            Save(cache);
            return cache;
        }

        /// <summary>
        /// Instant seed from mappings only (names + paths). Safe to show in UI immediately.
        /// </summary>
        public MusicLibraryCache SeedFromMappings(string installRoot, JObject mappingsDoc, MusicLibraryCache? existing = null)
        {
            var cache = existing ?? Load(installRoot) ?? new MusicLibraryCache { InstallRoot = installRoot };
            cache.InstallRoot = installRoot;

            var songs = new List<CachedSongInfo>();
            if (mappingsDoc["music"] is JObject musicObj)
            {
                foreach (var prop in musicObj.Properties())
                {
                    if (prop.Value is not JObject entry) continue;
                    var id = prop.Name;
                    // Skip mappings entries with no on-disk SWF.
                    var swf = MappingsEditService.ResolveEntrySwfPath(installRoot, mappingsDoc, "music", id);
                    if (swf == null) continue;

                    var prev = cache.Songs.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                    var mappedName = entry["name"]?.ToString();
                    // Mappings display name wins — SWF string pools often keep template leftovers (e.g. "Battlefield").
                    string name;
                    if (!string.IsNullOrWhiteSpace(mappedName) && !LooksLikeRawId(mappedName!, id))
                        name = mappedName!;
                    else if (!string.IsNullOrWhiteSpace(prev?.Name) && !LooksLikeRawId(prev!.Name, id))
                        name = prev.Name;
                    else
                        name = mappedName ?? prev?.Name ?? id;

                    songs.Add(new CachedSongInfo
                    {
                        Id = id,
                        Name = name,
                        Artist = prev?.Artist ?? "",
                        SwfPath = swf,
                        LoopMs = prev?.LoopMs ?? 0,
                        PreviewMp3Path = prev?.PreviewMp3Path is { } p && File.Exists(p) ? p : null
                    });
                }
            }

            // Expansion / content-pack tracks that are on disk but not (yet) in mappings.
            AppendExpansionSongs(installRoot, songs, cache.Songs);

            cache.Songs = songs.OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToList();

            var stages = new List<CachedStagePlaylist>();
            if (mappingsDoc["stage"] is JObject stageObj)
            {
                foreach (var prop in stageObj.Properties())
                {
                    if (prop.Value is not JObject entry) continue;
                    var stageId = prop.Name;
                    // Same rule as songs: skip mappings entries with no on-disk SWF.
                    if (MusicEditService.ResolveStageSwfPath(installRoot, mappingsDoc, stageId) == null)
                        continue;

                    var prev = cache.StagePlaylists.FirstOrDefault(s =>
                        s.StageId.Equals(stageId, StringComparison.OrdinalIgnoreCase));
                    stages.Add(new CachedStagePlaylist
                    {
                        StageId = stageId,
                        StageName = entry["name"]?.ToString() ?? stageId,
                        TrackIds = prev?.TrackIds?.ToList() ?? new List<string>()
                    });
                }
            }
            cache.StagePlaylists = stages.OrderBy(s => s.StageName, StringComparer.OrdinalIgnoreCase).ToList();
            return cache;
        }

        public async Task<MusicLibraryCache> BuildOrRefreshAsync(
            string installRoot,
            JObject mappingsDoc,
            bool forceFull = false,
            IProgress<MusicCacheProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var previous = Load(installRoot);
            // Keep prior metadata/previews/user playlists; forceFull just re-scans every SWF.
            var cache = SeedFromMappings(installRoot, mappingsDoc, previous);
            if (forceFull)
            {
                foreach (var st in cache.StagePlaylists)
                    st.TrackIds = new List<string>();
                cache.EnrichmentComplete = false;
            }

            // Work units: songs needing meta + stages needing playlist scan
            var songWork = cache.Songs
                .Where(s => File.Exists(s.SwfPath) &&
                            (forceFull || NeedsMetadata(s)))
                .ToList();
            var stageWork = cache.StagePlaylists
                .Select(st =>
                {
                    var path = MusicEditService.ResolveStageSwfPath(installRoot, mappingsDoc, st.StageId);
                    return (Stage: st, Path: path);
                })
                .Where(x => !string.IsNullOrWhiteSpace(x.Path) && File.Exists(x.Path!) &&
                            (forceFull || x.Stage.TrackIds.Count == 0))
                .ToList();

            var total = songWork.Count + stageWork.Count;
            if (total == 0)
            {
                cache.EnrichmentComplete = true;
                cache.SchemaVersion = MusicLibraryCache.CurrentSchema;
                Save(cache);
                progress?.Report(new MusicCacheProgress("Music cache ready.", 0, 0, CloneSnapshot(cache)));
                return cache;
            }

            var done = 0;
            var knownIds = new HashSet<string>(cache.Songs.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);
            progress?.Report(new MusicCacheProgress($"Caching 0/{total}…", 0, total, CloneSnapshot(cache)));

            // Parallel binary scans — vastly faster than per-file FFDec.
            await Parallel.ForEachAsync(
                songWork,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                    CancellationToken = cancellationToken
                },
                (song, ct) =>
                {
                    var mappingsName = song.Name; // seeded from mappings already
                    try
                    {
                        if (SwfQuickScan.TryReadMusicMeta(song.SwfPath, out var name, out var artist, out var loop))
                        {
                            // Only take SWF title when mappings didn't give a real name.
                            if (LooksLikeRawId(mappingsName, song.Id) && !string.IsNullOrWhiteSpace(name))
                                song.Name = name;
                            if (!string.IsNullOrWhiteSpace(artist)) song.Artist = artist;
                            if (loop > 0) song.LoopMs = loop;
                        }
                    }
                    catch { /* keep seed */ }

                    if (LooksLikeRawId(song.Name, song.Id))
                        song.Name = SwfQuickScan.HumanizeTrackId(song.Id);

                    var n = Interlocked.Increment(ref done);
                    if (n == total || n % 4 == 0 || n <= 3)
                    {
                        progress?.Report(new MusicCacheProgress(
                            $"Caching songs {n}/{total}…", n, total, CloneSnapshot(cache)));
                    }
                    return ValueTask.CompletedTask;
                });

            await Parallel.ForEachAsync(
                stageWork,
                new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(2, Environment.ProcessorCount),
                    CancellationToken = cancellationToken
                },
                (item, ct) =>
                {
                    try
                    {
                        var tracks = SwfQuickScan.ReadStagePlaylistIds(item.Path!, knownIds);
                        if (tracks.Count > 0)
                            item.Stage.TrackIds = tracks;
                    }
                    catch { /* keep previous */ }

                    var n = Interlocked.Increment(ref done);
                    if (n == total || n % 4 == 0 || n <= 3)
                    {
                        progress?.Report(new MusicCacheProgress(
                            $"Caching stages {n}/{total}…", n, total, CloneSnapshot(cache)));
                    }
                    return ValueTask.CompletedTask;
                });

            foreach (var song in cache.Songs)
            {
                if (LooksLikeRawId(song.Name, song.Id))
                    song.Name = SwfQuickScan.HumanizeTrackId(song.Id);
            }
            cache.Songs = cache.Songs.OrderBy(s => s.DisplayLabel, StringComparer.OrdinalIgnoreCase).ToList();
            cache.StagePlaylists = cache.StagePlaylists.OrderBy(s => s.StageName, StringComparer.OrdinalIgnoreCase).ToList();
            cache.EnrichmentComplete = true;
            cache.SchemaVersion = MusicLibraryCache.CurrentSchema;
            Save(cache);
            progress?.Report(new MusicCacheProgress(
                $"Music cache ready — {cache.Songs.Count} songs, {cache.StagePlaylists.Count} stages.",
                total, total, CloneSnapshot(cache)));
            return cache;
        }

        /// <summary>
        /// Adds music content packs and loose <c>data/sound/bgm_*</c> files that mappings missed
        /// (fresh GameBanana installs, packs not yet committed into mappings.swf).
        /// </summary>
        private static void AppendExpansionSongs(
            string installRoot,
            List<CachedSongInfo> songs,
            List<CachedSongInfo> previous)
        {
            var seen = new HashSet<string>(songs.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

            void TryAdd(string id, string? displayName, string? swfHint)
            {
                if (string.IsNullOrWhiteSpace(id) || !seen.Add(id))
                    return;
                var swf = ResolveMusicSwfOnDisk(installRoot, id, swfHint);
                if (swf == null)
                    return;
                var prev = previous.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
                var name = !string.IsNullOrWhiteSpace(displayName) && !LooksLikeRawId(displayName!, id)
                    ? displayName!
                    : prev?.Name is { Length: > 0 } pn && !LooksLikeRawId(pn, id)
                        ? pn
                        : (displayName ?? id);
                songs.Add(new CachedSongInfo
                {
                    Id = id,
                    Name = name,
                    Artist = prev?.Artist ?? "",
                    SwfPath = swf,
                    LoopMs = prev?.LoopMs ?? 0,
                    PreviewMp3Path = prev?.PreviewMp3Path is { } p && File.Exists(p) ? p : null
                });
            }

            try
            {
                ContentPackService.EnsureRoot(installRoot);
                var index = ContentPackService.LoadIndex(installRoot);
                foreach (var packId in index.Packs)
                {
                    var pack = ContentPackService.LoadPack(installRoot, packId);
                    if (pack == null || pack.Type != ContentPackType.Music)
                        continue;
                    TryAdd(pack.PackId, pack.Name, pack.ContentRelativePath);
                }

                // Pack folders that never made it into index.json yet.
                var packsDir = ContentPackService.PacksDir(installRoot);
                if (Directory.Exists(packsDir))
                {
                    foreach (var dir in Directory.GetDirectories(packsDir))
                    {
                        var pack = ContentPackService.LoadPack(installRoot, Path.GetFileName(dir));
                        if (pack == null || pack.Type != ContentPackType.Music)
                            continue;
                        TryAdd(pack.PackId, pack.Name, pack.ContentRelativePath);
                    }
                }
            }
            catch { /* packs optional */ }

            var soundDir = Path.Combine(installRoot, "data", "sound");
            if (!Directory.Exists(soundDir))
                return;
            foreach (var path in Directory.EnumerateFiles(soundDir, "bgm_*.*")
                         .Where(p => p.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)
                                     || p.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase)))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                TryAdd(id, id, path);
            }
        }

        private static string? ResolveMusicSwfOnDisk(string installRoot, string id, string? hint)
        {
            var candidates = new List<string>();
            if (!string.IsNullOrWhiteSpace(hint))
            {
                var h = hint!.Replace('/', Path.DirectorySeparatorChar);
                if (Path.IsPathRooted(h))
                    candidates.Add(h);
                else
                {
                    candidates.Add(Path.Combine(installRoot, "data", "sound", Path.GetFileName(h)));
                    candidates.Add(Path.Combine(installRoot, h));
                    candidates.Add(ContentPackService.ContentFullPath(installRoot, id));
                }
            }

            candidates.Add(Path.Combine(installRoot, "data", "sound", id + ".swf"));
            candidates.Add(Path.Combine(installRoot, "data", "sound", id + ".ssf"));
            candidates.Add(ContentPackService.ContentFullPath(installRoot, id));
            candidates.Add(Path.Combine(ContentPackService.PackDir(installRoot, id), id + ".swf"));

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path))
                    return path;
                if (path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                {
                    var ssf = path[..^4] + ".ssf";
                    if (File.Exists(ssf)) return ssf;
                }
                else if (path.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                {
                    var swf = path[..^4] + ".swf";
                    if (File.Exists(swf)) return swf;
                }
            }
            return null;
        }

        private sealed class StockSeedEntry
        {
            public string Id { get; set; } = "";
            public string Name { get; set; } = "";
            public string Artist { get; set; } = "";
            public int LoopMs { get; set; }
        }

        private sealed class StockSeedFile
        {
            public List<StockSeedEntry> Songs { get; set; } = new();
        }

        /// <summary>Prefer the Shell's jukebox list, then the bundled stock seed.</summary>
        private static List<StockSeedEntry> LoadStockSeedEntries(string installRoot)
        {
            var shellList = Path.Combine(
                ShellWorkspaceService.Ssf2mmDirectory(installRoot), "music_player", "list.json");
            var fromShell = TryParseMusicPlayerList(shellList);
            if (fromShell.Count > 0)
                return fromShell;

            foreach (var path in StockSeedFileCandidates())
            {
                var fromFile = TryParseStockSeedFile(path);
                if (fromFile.Count > 0)
                    return fromFile;
            }

            return TryParseStockSeedJson(ReadEmbeddedStockSeedJson());
        }

        private static IEnumerable<string> StockSeedFileCandidates()
        {
            var baseDir = AppContext.BaseDirectory;
            yield return Path.Combine(baseDir, "runtime", "music-library-stock.json");
            yield return Path.Combine(baseDir, "Assets", "music-library-stock.json");
            yield return Path.Combine(baseDir, "music-library-stock.json");
        }

        private static string? ReadEmbeddedStockSeedJson()
        {
            var asm = typeof(MusicLibraryCacheService).Assembly;
            foreach (var name in asm.GetManifestResourceNames())
            {
                if (!name.EndsWith("music-library-stock.json", StringComparison.OrdinalIgnoreCase))
                    continue;
                using var stream = asm.GetManifestResourceStream(name);
                if (stream == null) continue;
                using var reader = new StreamReader(stream);
                return reader.ReadToEnd();
            }
            return null;
        }

        private static List<StockSeedEntry> TryParseStockSeedFile(string path)
        {
            if (!File.Exists(path)) return new List<StockSeedEntry>();
            try { return TryParseStockSeedJson(File.ReadAllText(path)); }
            catch { return new List<StockSeedEntry>(); }
        }

        private static List<StockSeedEntry> TryParseStockSeedJson(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return new List<StockSeedEntry>();
            try
            {
                var file = JsonConvert.DeserializeObject<StockSeedFile>(json);
                return file?.Songs?
                    .Where(s => !string.IsNullOrWhiteSpace(s.Id))
                    .ToList() ?? new List<StockSeedEntry>();
            }
            catch { return new List<StockSeedEntry>(); }
        }

        private static List<StockSeedEntry> TryParseMusicPlayerList(string path)
        {
            if (!File.Exists(path)) return new List<StockSeedEntry>();
            try
            {
                var doc = JObject.Parse(File.ReadAllText(path));
                if (doc["tracks"] is not JObject tracks) return new List<StockSeedEntry>();
                var list = new List<StockSeedEntry>();
                foreach (var prop in tracks.Properties())
                {
                    if (prop.Value is not JObject t) continue;
                    var title = t["title"]?.ToString() ?? "";
                    var artist = t["artist"]?.ToString() ?? "";
                    if (artist is "PLACEHOLDER" or "TODO" or "??????")
                        artist = "";
                    var name = !string.IsNullOrWhiteSpace(title) && !title.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)
                        ? title
                        : prop.Name;
                    _ = int.TryParse(t["loopMs"]?.ToString(), out var loop);
                    list.Add(new StockSeedEntry
                    {
                        Id = prop.Name,
                        Name = name,
                        Artist = artist,
                        LoopMs = loop
                    });
                }
                return list;
            }
            catch { return new List<StockSeedEntry>(); }
        }

        /// <summary>One directory pass — id → path under data/sound (.swf preferred over .ssf).</summary>
        private static Dictionary<string, string> IndexSoundFiles(string installRoot)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var soundDir = Path.Combine(installRoot, "data", "sound");
            if (!Directory.Exists(soundDir)) return map;
            foreach (var path in Directory.EnumerateFiles(soundDir))
            {
                var isSwf = path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase);
                var isSsf = path.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase);
                if (!isSwf && !isSsf) continue;
                var id = Path.GetFileNameWithoutExtension(path);
                if (!map.TryGetValue(id, out var existing))
                {
                    map[id] = path;
                    continue;
                }
                // Prefer a plain .swf when both exist.
                if (isSwf && existing.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                    map[id] = path;
            }
            return map;
        }

        /// <summary>Fill raw ids / missing artists from the stock jukebox seed.</summary>
        private static bool ApplyStockNames(List<CachedSongInfo> songs, string installRoot)
        {
            var stock = LoadStockSeedEntries(installRoot);
            if (stock.Count == 0) return false;
            var byId = stock.ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var changed = false;
            foreach (var song in songs)
            {
                if (!byId.TryGetValue(song.Id, out var entry))
                    continue;
                if (LooksLikeRawId(song.Name, song.Id)
                    && !string.IsNullOrWhiteSpace(entry.Name)
                    && !LooksLikeRawId(entry.Name, song.Id))
                {
                    song.Name = entry.Name;
                    changed = true;
                }
                if (string.IsNullOrWhiteSpace(song.Artist) && !string.IsNullOrWhiteSpace(entry.Artist))
                {
                    song.Artist = entry.Artist;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>Fill missing LoopMs from SWF <c>musicLoop</c> (cheap string-pool scan).</summary>
        private static bool ApplyLoopMsFromDisk(List<CachedSongInfo> songs)
        {
            var changed = false;
            foreach (var song in songs)
            {
                if (song.LoopMs > 0 || song.LoopChecked) continue;
                if (string.IsNullOrWhiteSpace(song.SwfPath) || !File.Exists(song.SwfPath))
                    continue;
                try
                {
                    if (SwfQuickScan.TryReadMusicMeta(song.SwfPath, out _, out _, out var loop) && loop > 0)
                    {
                        song.LoopMs = loop;
                        changed = true;
                    }
                }
                catch { /* keep 0 */ }
                song.LoopChecked = true;
                changed = true;
            }
            return changed;
        }

        /// <summary>Point at sibling .swf when the cache still holds a .ssf path.</summary>
        private static bool PreferPlainSwfPaths(List<CachedSongInfo> songs, string installRoot)
        {
            var onDisk = IndexSoundFiles(installRoot);
            var changed = false;
            foreach (var song in songs)
            {
                if (onDisk.TryGetValue(song.Id, out var preferred)
                    && !string.Equals(song.SwfPath, preferred, StringComparison.OrdinalIgnoreCase))
                {
                    song.SwfPath = preferred;
                    changed = true;
                }
            }
            return changed;
        }

        private static bool LooksLikeRawId(string name, string id) =>
            string.IsNullOrWhiteSpace(name) ||
            name.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase);

        /// <summary>Only re-scan SWFs when the display name is still a raw id — empty artist is fine.</summary>
        private static bool NeedsMetadata(CachedSongInfo s) =>
            LooksLikeRawId(s.Name, s.Id);

        private static MusicLibraryCache CloneSnapshot(MusicLibraryCache cache)
        {
            // Shallow-ish copy for UI binding (lists recreated so ItemsSource refresh is safe).
            return new MusicLibraryCache
            {
                InstallRoot = cache.InstallRoot,
                BuiltUtc = cache.BuiltUtc,
                Songs = cache.Songs.ToList(),
                StagePlaylists = cache.StagePlaylists
                    .Select(s => new CachedStagePlaylist
                    {
                        StageId = s.StageId,
                        StageName = s.StageName,
                        TrackIds = s.TrackIds.ToList()
                    }).ToList(),
                UserPlaylists = cache.UserPlaylists,
                LastPlayedSongId = cache.LastPlayedSongId,
                LastPlayedQueueIds = cache.LastPlayedQueueIds.ToList()
            };
        }

        public async Task<string?> EnsurePreviewMp3Async(
            CachedSongInfo song,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!string.IsNullOrWhiteSpace(song.PreviewMp3Path) && File.Exists(song.PreviewMp3Path))
                return song.PreviewMp3Path;
            if (string.IsNullOrWhiteSpace(song.SwfPath) || !File.Exists(song.SwfPath))
                return null;

            progress?.Report($"Extracting audio: {song.DisplayLabel}…");
            var dest = Path.Combine(PreviewCacheDir, MenuSlotService.SanitizeId(song.Id) + ".mp3");
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-prev-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                // Unwrap packaged .ssf → plain FWS/CWS for both fast extract and FFDec.
                var exportPath = SwfPackage.EnsurePlainSwf(song.SwfPath, work);

                // Fast path: pull DefineSound MP3 in-process (ms). FFDec is the slow fallback.
                try
                {
                    BgmPreviewService.ExtractMp3(exportPath, dest);
                    if (File.Exists(dest) && new FileInfo(dest).Length > 64)
                    {
                        song.PreviewMp3Path = dest;
                        return dest;
                    }
                }
                catch
                {
                    try { if (File.Exists(dest)) File.Delete(dest); } catch { /* ignore */ }
                }

                cancellationToken.ThrowIfCancellationRequested();
                await _jpexs.ExportAsync("", exportPath, work, "sound", cancellationToken);
                var mp3 = Directory.GetFiles(work, "*.mp3", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (mp3 == null) return null;
                File.Copy(mp3, dest, overwrite: true);
                song.PreviewMp3Path = dest;
                return dest;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }
    }
}
