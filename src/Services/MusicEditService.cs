using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public class StagePlaylistInfo
    {
        public string StageId { get; set; } = "";
        public string StageSwfPath { get; set; } = "";
        public List<string> TrackIds { get; set; } = new();
    }

    /// <summary>
    /// Create music SWFs from MP3 + edit stage Main register("music", [...]) playlists.
    /// </summary>
    public class MusicEditService
    {
        private readonly JpexsCliService _jpexs;
        private readonly MappingsEditService _mappings;
        private readonly CharacterCloneService _clone;

        public MusicEditService(
            JpexsCliService? jpexs = null,
            MappingsEditService? mappings = null,
            CharacterCloneService? clone = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _mappings = mappings ?? new MappingsEditService(_jpexs);
            _clone = clone ?? new CharacterCloneService(_jpexs);
        }

        public static string? FindSoundFolder(string installRoot)
        {
            var p = Path.Combine(installRoot, "data", "sound");
            return Directory.Exists(p) ? p : null;
        }

        public static string? PickTemplateMusicSwf(string installRoot)
        {
            var sound = FindSoundFolder(installRoot);
            if (sound == null) return null;
            foreach (var prefer in new[] { "bgm_battlefield.swf", "bgm_menu.swf", "bgm_results.swf" })
            {
                var full = Path.Combine(sound, prefer);
                if (File.Exists(full)) return full;
            }
            return Directory.GetFiles(sound, "bgm_*.swf").OrderBy(f => new FileInfo(f).Length).FirstOrDefault();
        }

        public static string? ResolveStageSwfPath(string installRoot, JObject mappingsDoc, string stageId) =>
            MappingsEditService.ResolveEntrySwfPath(installRoot, mappingsDoc, "stage", stageId);

        public static string? ResolveCharacterSwfPath(string installRoot, JObject mappingsDoc, string characterId) =>
            MappingsEditService.ResolveEntrySwfPath(installRoot, mappingsDoc, "character", characterId);

        public async Task<StagePlaylistInfo> LoadStagePlaylistAsync(
            string installRoot,
            JObject mappingsDoc,
            string stageId,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var full = ResolveStageSwfPath(installRoot, mappingsDoc, stageId)
                ?? throw new FileNotFoundException($"Stage SWF not found for '{stageId}'.");
            progress?.Report($"Reading playlist: {stageId}…");
            var tracks = await ReadPlaylistAsync(full, jpexsPath, cancellationToken);
            return new StagePlaylistInfo
            {
                StageId = stageId,
                StageSwfPath = full,
                TrackIds = tracks
            };
        }

        public async Task<List<string>> ReadPlaylistAsync(
            string stageSwfPath,
            string? jpexsPath = null,
            CancellationToken cancellationToken = default)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-playlist-r-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await _jpexs.ExportAsync(jpexsPath ?? "", stageSwfPath, work, "script", cancellationToken);
                var mainAs = FindPlaylistMainScript(work)
                    ?? throw new InvalidOperationException("Stage Main.as not found.");
                return ParseMusicRegister(await File.ReadAllTextAsync(mainAs, cancellationToken));
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public async Task WritePlaylistAsync(
            string stageSwfPath,
            IReadOnlyList<string> trackIds,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-playlist-w-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Updating playlist in {Path.GetFileName(stageSwfPath)}…");
                EnsureBackup(stageSwfPath);

                // Export into work root so FFDec creates work/scripts/Main.as (same as ReadPlaylistAsync).
                await _jpexs.ExportAsync(jpexsPath ?? "", stageSwfPath, work, "script", cancellationToken);
                var mainAs = FindPlaylistMainScript(work)
                    ?? throw new InvalidOperationException("Stage Main.as not found for playlist edit.");
                var text = await File.ReadAllTextAsync(mainAs, cancellationToken);
                var arrayLiteral = BuildMusicArrayLiteral(trackIds);
                var rewritten = RewriteMusicRegisterInText(text, arrayLiteral);
                if (rewritten.Equals(text, StringComparison.Ordinal))
                    throw new InvalidOperationException(
                        $"Could not locate register(\"music\", […]) playlist in {Path.GetFileName(mainAs)}.");

                // Sanity: rewritten playlist must parse back to the same ids.
                var parsed = ParseMusicRegister(rewritten);
                if (parsed.Count != trackIds.Count ||
                    !parsed.SequenceEqual(trackIds, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Playlist rewrite validation failed (wrote {parsed.Count} ids, expected {trackIds.Count}).");
                }

                await File.WriteAllTextAsync(mainAs, rewritten, cancellationToken);
                // Surgical -replace of Main only. Bulk -importScript / xml2swf corrupts stage SWFs (match crashes).
                var outSwf = Path.Combine(work, "stage-out.swf");
                await _jpexs.ReplaceAsync(jpexsPath ?? "", stageSwfPath, outSwf, "Main", mainAs, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("FFDec -replace Main did not produce an output stage SWF.");
                File.Copy(outSwf, stageSwfPath, overwrite: true);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Read existing playlist, append track if missing, write back (never replaces whole list blindly).
        /// </summary>
        public async Task AppendTrackToStageAsync(
            string installRoot,
            JObject mappingsDoc,
            string stageId,
            string trackId,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            // Content-pack runtime: sidecar playlists.json only — never mutate stage Main.
            if (SlotPoolService.IsReady(installRoot) || File.Exists(ContentPackService.IndexPath(installRoot)))
            {
                progress?.Report($"Attaching '{trackId}' to stage '{stageId}' via playlists.json (no stage SWF rewrite)…");
                ContentPackService.AttachTrackToStagePlaylist(installRoot, stageId, trackId);
                var pack = ContentPackService.LoadPack(installRoot, trackId);
                if (pack != null && pack.Type == ContentPackType.Music)
                {
                    if (!pack.AttachToStages.Contains(stageId, StringComparer.OrdinalIgnoreCase))
                        pack.AttachToStages.Add(MenuSlotService.SanitizeId(stageId));
                    ContentPackService.SavePack(installRoot, pack);
                }
                var packs = new ContentPackService();
                await packs.CommitPacksAsync(installRoot, mappingsDoc, jpexsPath, progress, cancellationToken);
                return;
            }

            var path = ResolveStageSwfPath(installRoot, mappingsDoc, stageId)
                ?? throw new FileNotFoundException($"Stage SWF not found for '{stageId}'.");
            progress?.Report($"Reading current playlist for {stageId}…");
            var tracks = await ReadPlaylistAsync(path, jpexsPath, cancellationToken);
            if (tracks.Count == 0)
            {
                progress?.Report(
                    $"Warning: no tracks parsed from {stageId}; refusing to overwrite with a single song. " +
                    "Open Manage playlists and set the list manually.");
                throw new InvalidOperationException(
                    $"Could not read existing playlist for '{stageId}'. " +
                    "Refusing to replace it with only the new track. Use Manage stage playlists to edit safely.");
            }

            if (!tracks.Contains(trackId, StringComparer.OrdinalIgnoreCase))
                tracks.Add(trackId);
            await WritePlaylistAsync(path, tracks, jpexsPath, progress, cancellationToken);
        }

        /// <summary>
        /// Clone a template music SWF, rewrite ids/loop/id3 via scripts, replace sound (resampled to 44.1kHz).
        /// </summary>
        public async Task<string> CreateMusicFromMp3Async(
            string installRoot,
            string mp3Path,
            string trackId,
            int musicLoopMs,
            string songName,
            string artist,
            JObject mappingsDoc,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            int trimStartMs = 0,
            int? trimEndMs = null)
        {
            if (!File.Exists(mp3Path))
                throw new FileNotFoundException("MP3 not found.", mp3Path);

            trackId = MenuSlotService.SanitizeId(trackId);
            if (!trackId.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                trackId = "bgm_" + trackId;

            var template = PickTemplateMusicSwf(installRoot)
                ?? throw new FileNotFoundException("No template bgm_*.swf found under data/sound.");
            var soundDir = FindSoundFolder(installRoot)!;
            var outPath = Path.Combine(soundDir, trackId + ".swf");

            var templateBase = Path.GetFileNameWithoutExtension(template);
            progress?.Report($"Cloning music template {Path.GetFileName(template)} → {trackId}…");

            var workOut = Path.Combine(Path.GetTempPath(), "ssf2mm-music-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workOut);
            try
            {
                // Must match mappings.json — leaving the template GUID crashes the game (duplicate with stock track).
                var guid = MappingsEditService.AllocateUniqueGuid(mappingsDoc);

                var cloneResult = await _clone.CloneAsync(new CharacterCloneRequest
                {
                    SourcePath = template,
                    OldName = templateBase,
                    NewName = trackId,
                    OutputDirectory = workOut,
                    JpexsPath = jpexsPath,
                    Guid = guid
                }, progress, cancellationToken);

                var cloned = cloneResult.OutputSwfPath;

                progress?.Report("Setting loop point, GUID, and song info…");
                var withMeta = await PatchMusicScriptsAsync(
                    cloned, trackId, guid, musicLoopMs, songName, artist, jpexsPath, workOut, cancellationToken);

                progress?.Report("Preparing audio (trim + 44.1 kHz for Flash)…");
                var preparedMp3 = await PrepareMp3ForFlashAsync(
                    mp3Path, workOut, progress, cancellationToken, trimStartMs, trimEndMs);

                progress?.Report("Replacing embedded sound with uploaded MP3…");
                var withSound = await ReplaceSoundWithMp3Async(
                    withMeta, preparedMp3, jpexsPath, workOut, cancellationToken);

                // Sound replace can leave template id3 strings in the ABC pool — rewrite them in-place.
                SwfQuickScan.TryRewriteMusicId3Strings(withSound, songName, artist);

                if (File.Exists(outPath))
                    EnsureBackup(outPath);
                File.Copy(withSound, outPath, overwrite: true);

                MappingsEditService.UpsertEntry(
                    mappingsDoc, "music", trackId, trackId + ".swf",
                    name: string.IsNullOrWhiteSpace(songName) ? trackId : songName,
                    guid: guid);

                // Also register as a content pack so boot traces see music OK.
                try
                {
                    RegisterMusicContentPack(installRoot, trackId, songName, guid, outPath);
                }
                catch (Exception ex)
                {
                    progress?.Report("Warning: music pack register: " + ex.Message);
                }

                progress?.Report($"Wrote {outPath}");
                return outPath;
            }
            finally
            {
                try { Directory.Delete(workOut, true); } catch { /* ignore */ }
            }
        }

        public async Task<List<StagePlaylistInfo>> LoadAllStagePlaylistsAsync(
            string installRoot,
            JObject mappingsDoc,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var list = new List<StagePlaylistInfo>();
            if (mappingsDoc["stage"] is not JObject stages) return list;
            foreach (var prop in stages.Properties())
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    list.Add(await LoadStagePlaylistAsync(
                        installRoot, mappingsDoc, prop.Name, jpexsPath, progress, cancellationToken));
                }
                catch
                {
                    var path = ResolveStageSwfPath(installRoot, mappingsDoc, prop.Name);
                    if (path == null) continue;
                    list.Add(new StagePlaylistInfo
                    {
                        StageId = prop.Name,
                        StageSwfPath = path,
                        TrackIds = new List<string>()
                    });
                }
            }
            return list.OrderBy(s => s.StageId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public static List<string> ParseMusicRegister(string text)
        {
            var ids = new List<string>();
            // register("music", [ ... ]);  — not register("music", "bgm_x") on music SWFs
            var m = Regex.Match(
                text,
                @"register\s*\(\s*[""']music[""']\s*,\s*\[(?<body>[\s\S]*?)\]\s*\)",
                RegexOptions.IgnoreCase);
            if (!m.Success) return ids;

            // FFDec export uses {"id":"bgm_x"}; source often uses {id: "bgm_x"}
            foreach (Match idM in Regex.Matches(
                         m.Groups["body"].Value,
                         @"(?:[""']id[""']|id)\s*:\s*[""'](?<id>[^""']+)[""']",
                         RegexOptions.IgnoreCase))
            {
                var id = idM.Groups["id"].Value.Trim();
                if (id.Length > 0) ids.Add(id);
            }
            return ids;
        }

        /// <summary>
        /// Compact JSON-ish array matching FFDec-exported stage Main.as style.
        /// </summary>
        public static string BuildMusicArrayLiteral(IReadOnlyList<string> trackIds)
        {
            if (trackIds.Count == 0) return "[]";
            var parts = trackIds.Select(id => $"{{\"id\":\"{EscapeAs(id)}\"}}");
            return "[" + string.Join(",", parts) + "]";
        }

        public static string RewriteMusicRegisterInText(string text, string arrayLiteral)
        {
            var rx = new Regex(
                @"register\s*\(\s*[""']music[""']\s*,\s*\[[\s\S]*?\]\s*\)",
                RegexOptions.IgnoreCase);
            if (!rx.IsMatch(text)) return text;
            return rx.Replace(text, $"register(\"music\",{arrayLiteral})", 1);
        }

        public static string PatchMusicMetadataInText(
            string text, string trackId, string guid, int musicLoopMs, string songName, string artist)
        {
            var loop = Math.Max(0, musicLoopMs);
            var name = EscapeAs(songName ?? "");
            var art = EscapeAs(artist ?? "");
            var id = EscapeAs(trackId ?? "");
            var g = EscapeAs(guid ?? "");

            text = Regex.Replace(
                text,
                @"register\s*\(\s*[""']id[""']\s*,\s*[""'][^""']+[""']\s*\)",
                $"register(\"id\",\"{id}\")",
                RegexOptions.IgnoreCase);

            text = Regex.Replace(
                text,
                @"register\s*\(\s*[""']guid[""']\s*,\s*[""'][^""']+[""']\s*\)",
                $"register(\"guid\",\"{g}\")",
                RegexOptions.IgnoreCase);

            text = Regex.Replace(
                text,
                @"register\s*\(\s*[""']musicLoop[""']\s*,\s*\d+\s*\)",
                $"register(\"musicLoop\",{loop})",
                RegexOptions.IgnoreCase);

            // Music SWF uses register("music", "bgm_id") — string, not array.
            text = Regex.Replace(
                text,
                @"register\s*\(\s*[""']music[""']\s*,\s*[""'][^""']+[""']\s*\)",
                $"register(\"music\",\"{id}\")",
                RegexOptions.IgnoreCase);

            // Quoted-key style from FFDec: {"songName":"X","artist":"Y"}
            text = Regex.Replace(
                text,
                @"register\s*\(\s*[""']id3[""']\s*,\s*\{[\s\S]*?\}\s*\)",
                $"register(\"id3\",{{\n            \"songName\":\"{name}\",\n            \"artist\":\"{art}\"\n         }})",
                RegexOptions.IgnoreCase);

            return text;
        }

        private async Task<string> PatchMusicScriptsAsync(
            string swfPath,
            string trackId,
            string guid,
            int musicLoopMs,
            string songName,
            string artist,
            string? jpexsPath,
            string workRoot,
            CancellationToken cancellationToken)
        {
            var exportRoot = Path.Combine(workRoot, "meta-scripts");
            Directory.CreateDirectory(exportRoot);
            await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, exportRoot, "script", cancellationToken);
            var mainAs = FindMainScript(exportRoot)
                ?? throw new InvalidOperationException("Music Main.as not found.");
            var text = await File.ReadAllTextAsync(mainAs, cancellationToken);
            var next = PatchMusicMetadataInText(text, trackId, guid, musicLoopMs, songName, artist);
            if (next.Equals(text, StringComparison.Ordinal))
                throw new InvalidOperationException("Could not patch music metadata in music Main.as.");

            if (!next.Contains($"register(\"guid\",\"{guid}\")", StringComparison.Ordinal))
                throw new InvalidOperationException("guid was not updated in music Main.as.");

            if (!next.Contains($"register(\"musicLoop\",{Math.Max(0, musicLoopMs)})", StringComparison.Ordinal) &&
                musicLoopMs > 0)
            {
                throw new InvalidOperationException("musicLoop was not updated in music Main.as.");
            }

            await File.WriteAllTextAsync(mainAs, next, cancellationToken);
            // Surgical -replace Main only (same reason as stage playlists — avoid bulk importScript).
            var outSwf = Path.Combine(workRoot, "meta.swf");
            await _jpexs.ReplaceAsync(jpexsPath ?? "", swfPath, outSwf, "Main", mainAs, cancellationToken);
            if (!File.Exists(outSwf))
                throw new InvalidOperationException("FFDec -replace Main did not produce an output music SWF.");
            return outSwf;
        }

        private async Task<string> ReplaceSoundWithMp3Async(
            string swfPath,
            string mp3Path,
            string? jpexsPath,
            string workRoot,
            CancellationToken cancellationToken)
        {
            var exportDir = Path.Combine(workRoot, "sound-export");
            Directory.CreateDirectory(exportDir);
            await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, exportDir, "sound", cancellationToken);
            var sounds = Directory.GetFiles(exportDir, "*.*", SearchOption.AllDirectories)
                .Where(f =>
                {
                    var e = Path.GetExtension(f).ToLowerInvariant();
                    return e is ".mp3" or ".wav" or ".flv";
                })
                .OrderByDescending(f => new FileInfo(f).Length)
                .ToList();
            if (sounds.Count == 0)
                throw new InvalidOperationException("No embedded sound found in music template SWF.");

            var target = sounds[0];
            var idMatch = Regex.Match(Path.GetFileName(target), @"^(\d+)");
            if (!idMatch.Success)
                throw new InvalidOperationException($"Could not parse sound character id from '{Path.GetFileName(target)}'.");

            var outSwf = Path.Combine(workRoot, "sound-replaced.swf");
            await _jpexs.ReplaceAsync(
                jpexsPath ?? "", swfPath, outSwf, idMatch.Groups[1].Value, mp3Path, cancellationToken);
            if (!File.Exists(outSwf))
                throw new InvalidOperationException("FFDec sound replace did not produce an output SWF.");

            // Sanity: replaced SWF should not stay within a few bytes of the template after a multi-MB import.
            var inLen = new FileInfo(swfPath).Length;
            var outLen = new FileInfo(outSwf).Length;
            var mp3Len = new FileInfo(mp3Path).Length;
            if (mp3Len > 200_000 && Math.Abs(outLen - inLen) < 64)
            {
                throw new InvalidOperationException(
                    "Sound replace appears to have failed (output size unchanged). " +
                    "Flash only accepts MP3 at 11025 / 22050 / 44100 Hz — try converting the file to 44.1 kHz.");
            }

            return outSwf;
        }

        /// <summary>
        /// Trim (optional) + resample to 44.1 kHz stereo MP3 for Flash sound replace.
        /// Requires the bundled <c>runtime/ffmpeg</c> copy.
        /// </summary>
        public static async Task<string> PrepareMp3ForFlashAsync(
            string mp3Path,
            string workRoot,
            IProgress<string>? progress,
            CancellationToken cancellationToken,
            int trimStartMs = 0,
            int? trimEndMs = null)
        {
            Directory.CreateDirectory(workRoot);
            // Copy to a safe ASCII temp path (spaces/# break some tooling).
            var safeIn = Path.Combine(workRoot, "input" + Path.GetExtension(mp3Path));
            if (string.IsNullOrWhiteSpace(Path.GetExtension(safeIn)))
                safeIn += ".mp3";
            File.Copy(mp3Path, safeIn, overwrite: true);
            var safeOut = Path.Combine(workRoot, "flash44100.mp3");

            var ffmpeg = FindFfmpeg()
                ?? throw new InvalidOperationException(
                    "ffmpeg was not found. The bundled copy under 'runtime\\ffmpeg\\' is missing. " +
                    "Reinstall SSF2 Mod Manager or run scripts\\deps\\fetch-native-tools.ps1 " +
                    "(Flash needs 44.1 kHz MP3 — 48 kHz imports fail).");

            if (trimStartMs < 0) trimStartMs = 0;
            if (trimEndMs is { } end && end > 0 && end <= trimStartMs)
                throw new ArgumentException("Trim end must be greater than trim start.", nameof(trimEndMs));

            var startSec = trimStartMs / 1000.0;
            var args = new StringBuilder();
            args.Append("-y ");
            if (trimStartMs > 0)
                args.Append(System.Globalization.CultureInfo.InvariantCulture, $"-ss {startSec:0.###} ");
            args.Append(System.Globalization.CultureInfo.InvariantCulture, $"-i \"{safeIn}\" ");
            if (trimEndMs is { } trimEnd && trimEnd > 0)
            {
                var durSec = (trimEnd - trimStartMs) / 1000.0;
                args.Append(System.Globalization.CultureInfo.InvariantCulture, $"-t {durSec:0.###} ");
            }
            args.Append("-ar 44100 -ac 2 -c:a libmp3lame -q:a 2 ");
            args.Append(System.Globalization.CultureInfo.InvariantCulture, $"\"{safeOut}\"");

            progress?.Report(trimStartMs > 0 || trimEndMs is > 0
                ? "Trimming and resampling MP3 to 44.1 kHz…"
                : "Resampling MP3 to 44.1 kHz for Flash…");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpeg,
                Arguments = args.ToString(),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var proc = Process.Start(psi)
                ?? throw new InvalidOperationException("Failed to start ffmpeg.");
            var stderr = await proc.StandardError.ReadToEndAsync(cancellationToken);
            await proc.WaitForExitAsync(cancellationToken);
            if (proc.ExitCode != 0 || !File.Exists(safeOut))
            {
                throw new InvalidOperationException(
                    "ffmpeg failed to prepare MP3 for Flash.\n" + stderr.Trim());
            }
            return safeOut;
        }

        /// <summary>Bundled <c>runtime/ffmpeg</c> only. PATH / WinGet installs are ignored on purpose.</summary>
        public static string? FindFfmpeg() => AppPaths.FfmpegExe;

        public static bool HasBundledFfmpeg() => AppPaths.FfmpegExe != null;

        private static string? FindPlaylistMainScript(string root)
        {
            // Prefer a script that contains the playlist array form register("music", [ ... ]).
            var asFiles = Directory.GetFiles(root, "*.as", SearchOption.AllDirectories);
            foreach (var f in asFiles.OrderBy(f => Path.GetFileName(f).Equals("Main.as", StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                                     .ThenBy(f => f.Length))
            {
                try
                {
                    var t = File.ReadAllText(f);
                    if (Regex.IsMatch(t, @"register\s*\(\s*[""']music[""']\s*,\s*\[", RegexOptions.IgnoreCase))
                        return f;
                }
                catch { /* ignore */ }
            }
            return FindMainScript(root);
        }

        private static string? FindMainScript(string root) =>
            Directory.GetFiles(root, "Main.as", SearchOption.AllDirectories)
                .OrderBy(f => f.Length)
                .FirstOrDefault()
            ?? Directory.GetFiles(root, "*.as", SearchOption.AllDirectories)
                .FirstOrDefault(f =>
                {
                    try
                    {
                        var t = File.ReadAllText(f);
                        return t.Contains("register(\"music\"", StringComparison.Ordinal)
                               || t.Contains("register('music'", StringComparison.Ordinal);
                    }
                    catch { return false; }
                });

        private static void RegisterMusicContentPack(
            string installRoot,
            string trackId,
            string songName,
            string guid,
            string soundSwfPath)
        {
            ContentPackService.EnsureRoot(installRoot);
            var index = ContentPackService.LoadIndex(installRoot);
            ContentPackService.RegisterMusicPack(index, trackId);
            ContentPackService.SaveIndex(installRoot, index);

            // Keep the playable SWF in data/sound/; pack.json only registers metadata for boot traces.
            var pack = new ContentPackInfo
            {
                PackId = trackId,
                Type = ContentPackType.Music,
                Name = string.IsNullOrWhiteSpace(songName) ? trackId : songName,
                Guid = guid,
                Slot = -1,
                PlayableId = trackId,
                ContentRelativePath = trackId + ".swf"
            };
            ContentPackService.SavePack(installRoot, pack);

            // Optional mirror into pack folder for tooling / exists checks that look at packs/*/content.swf.
            try
            {
                var mirror = ContentPackService.ContentFullPath(installRoot, trackId);
                Directory.CreateDirectory(Path.GetDirectoryName(mirror)!);
                if (File.Exists(soundSwfPath))
                    File.Copy(soundSwfPath, mirror, overwrite: true);
            }
            catch
            {
                /* ignore mirror failures */
            }
        }

        private static void EnsureBackup(string path)
        {
            var bak = path + ".ssf2mm.bak";
            if (!File.Exists(bak) && File.Exists(path))
                File.Copy(path, bak);
        }

        private static string EscapeAs(string s) =>
            (s ?? "").Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal);
    }
}
