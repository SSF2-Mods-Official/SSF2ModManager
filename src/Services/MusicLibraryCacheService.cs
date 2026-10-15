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

        public static string CacheFilePath => Path.Combine(AppPaths.AppDataDir, "music-library-cache.json");
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

        private static bool LooksLikeRawId(string name, string id) =>
            string.IsNullOrWhiteSpace(name) ||
            name.Equals(id, StringComparison.OrdinalIgnoreCase) ||
            name.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase);

        private static bool NeedsMetadata(CachedSongInfo s) =>
            string.IsNullOrWhiteSpace(s.Artist) || LooksLikeRawId(s.Name, s.Id);

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
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-prev-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await _jpexs.ExportAsync("", song.SwfPath, work, "sound", cancellationToken);
                var mp3 = Directory.GetFiles(work, "*.mp3", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault();
                if (mp3 == null) return null;
                var dest = Path.Combine(PreviewCacheDir, MenuSlotService.SanitizeId(song.Id) + ".mp3");
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
