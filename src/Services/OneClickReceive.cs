using System.IO;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json.Linq;
using SharpCompress.Archives;
using SharpCompress.Common;

namespace SSF2ModManager.Services
{
    public enum OneClickKind
    {
        Pack,
        Stage,
        Character,
        Song
    }

    public sealed class OneClickItem
    {
        public OneClickKind Kind { get; init; }
        public string RelativePath { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string? Id { get; init; }
        public string Note { get; set; } = "";
        public bool Selected { get; set; } = true;

        public string Label
        {
            get
            {
                var name = string.IsNullOrWhiteSpace(Id) ? Path.GetFileName(RelativePath) : Id;
                var note = string.IsNullOrWhiteSpace(Note) ? "" : " — " + Note;
                return Kind + ": " + name + note;
            }
        }
    }

    public sealed class OneClickReceipt
    {
        public string ExtractedRoot { get; init; } = "";
        public bool DeleteExtractOnCleanup { get; init; }
        public bool IsPack { get; set; }
        public string? PackRoot { get; set; }
        public List<OneClickItem> Items { get; init; } = new();
        public int LeftOutNineB { get; set; }
        public int LeftOutOtherVersions { get; set; }
        public int LeftOutUnknown { get; set; }

        public void Cleanup()
        {
            if (!DeleteExtractOnCleanup || string.IsNullOrWhiteSpace(ExtractedRoot))
                return;
            try { Directory.Delete(ExtractedRoot, true); } catch { }
        }
    }

    /// <summary>
    /// Classifies a GameBanana download and installs the compatible files into the shell.
    /// </summary>
    public static class OneClickReceive
    {
        public readonly struct DownloadProgress
        {
            public long Received { get; init; }
            public long Total { get; init; }
        }

        /// <summary>GameBanana <c>/dl/{fileId}</c> is the file. <c>/mmdl/</c> is the 1-click page.</summary>
        public static string DirectDownloadUrl(string archiveUrl)
        {
            if (GameBananaUrlHelper.TryExtractFileId(archiveUrl, out var id) && id > 0)
                return "https://gamebanana.com/dl/" + id.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return archiveUrl;
        }

        public static string FormatDownload(long received, long total)
        {
            var got = Megabytes(received);
            if (total > 0)
            {
                var pct = (int)Math.Clamp(received * 100.0 / total, 0, 100);
                return $"Downloading… {pct}% ({got} / {Megabytes(total)})";
            }
            return "Downloading… " + got;
        }

        private static string Megabytes(long bytes) =>
            (bytes / (1024.0 * 1024.0)).ToString("0.0", System.Globalization.CultureInfo.InvariantCulture) + " MB";

        public static async Task<string> DownloadAsync(
            string archiveUrl,
            IProgress<DownloadProgress>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var url = DirectDownloadUrl(archiveUrl);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", AppInfo.UserAgent);
            http.Timeout = TimeSpan.FromMinutes(10);
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? -1;
            DebugLogger.Log($"[oneclick] download total={total}");
            progress?.Report(new DownloadProgress { Received = 0, Total = total });
            var temp = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            var partial = Path.Combine(temp, "download.bin");
            await using (var stream = await response.Content.ReadAsStreamAsync(cancellationToken))
            await using (var file = File.Create(partial))
            {
                var buffer = new byte[81920];
                long received = 0;
                int read;
                while ((read = await stream.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await file.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                    received += read;
                    progress?.Report(new DownloadProgress { Received = received, Total = total });
                }
            }

            var ext = ExtensionFromMagic(partial);
            var named = Path.Combine(temp, "download" + ext);
            File.Move(partial, named);
            return named;
        }

        public static OneClickReceipt ClassifyArchive(string archivePath)
        {
            var temp = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                using var archive = ArchiveFactory.OpenArchive(archivePath);
                foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
                {
                    var key = entry.Key?.Replace('/', Path.DirectorySeparatorChar) ?? "";
                    if (string.IsNullOrWhiteSpace(key))
                        continue;
                    var dest = Path.GetFullPath(Path.Combine(temp, key));
                    if (!dest.StartsWith(Path.GetFullPath(temp), StringComparison.OrdinalIgnoreCase))
                        continue;
                    var dir = Path.GetDirectoryName(dest);
                    if (dir != null)
                        Directory.CreateDirectory(dir);
                    entry.WriteToFile(dest, new ExtractionOptions { Overwrite = true });
                }
            }
            catch
            {
                try { Directory.Delete(temp, true); } catch { }
                throw;
            }

            var receipt = ClassifyDirectory(temp);
            return new OneClickReceipt
            {
                ExtractedRoot = temp,
                DeleteExtractOnCleanup = true,
                IsPack = receipt.IsPack,
                PackRoot = receipt.PackRoot,
                Items = receipt.Items,
                LeftOutNineB = receipt.LeftOutNineB,
                LeftOutOtherVersions = receipt.LeftOutOtherVersions,
                LeftOutUnknown = receipt.LeftOutUnknown
            };
        }

        public static OneClickReceipt ClassifyDirectory(string extractedRoot)
        {
            var receipt = new OneClickReceipt { ExtractedRoot = extractedRoot };
            if (!Directory.Exists(extractedRoot))
                return receipt;

            var manifest = FindManifest(extractedRoot);
            if (manifest != null)
            {
                receipt.IsPack = true;
                receipt.PackRoot = Path.GetDirectoryName(manifest);
                receipt.Items.Add(new OneClickItem
                {
                    Kind = OneClickKind.Pack,
                    RelativePath = "ssf2mm-pack.json",
                    FullPath = manifest,
                    Id = "ssf2mm-pack",
                    Note = "Mod Manager 2.0 pack",
                    Selected = true
                });
                DebugLogger.Log("[oneclick] kind=pack id=ssf2mm-pack selected=1");
                return receipt;
            }

            var files = Directory.GetFiles(extractedRoot, "*", SearchOption.AllDirectories)
                .Where(IsSwfName)
                .Select(path => (Path: path, Rel: Path.GetRelativePath(extractedRoot, path)))
                .ToList();

            var hasSandbox = files.Any(f => Segments(f.Rel).Any(IsSandboxSegment));
            var kept = new List<(string Path, string Rel)>();
            foreach (var file in files)
            {
                var nine = Segments(file.Rel).Any(IsNineBSegment);
                var sandbox = Segments(file.Rel).Any(IsSandboxSegment);
                if (hasSandbox && !sandbox)
                {
                    if (nine)
                        receipt.LeftOutNineB++;
                    else
                        receipt.LeftOutOtherVersions++;
                    continue;
                }
                if (!hasSandbox && nine)
                {
                    receipt.LeftOutNineB++;
                    continue;
                }
                kept.Add(file);
            }

            foreach (var file in kept)
            {
                byte[] raw;
                try
                {
                    raw = File.ReadAllBytes(file.Path);
                }
                catch
                {
                    receipt.LeftOutUnknown++;
                    continue;
                }
                if (SwfPackage.IsTimelineDocument(SwfPackage.DocumentClass(raw)))
                {
                    receipt.LeftOutNineB++;
                    continue;
                }
                var item = ClassifyFile(file.Path, file.Rel);
                if (item == null)
                {
                    receipt.LeftOutUnknown++;
                    continue;
                }
                receipt.Items.Add(item);
                DebugLogger.Log($"[oneclick] kind={item.Kind.ToString().ToLowerInvariant()} id={item.Id} selected={(item.Selected ? 1 : 0)}");
            }

            PreferOwnSlot(receipt.Items);
            return receipt;
        }

        /// <summary>
        /// A taken song id becomes that id plus the next free number, starting at 2.
        /// Ids that are free are omitted from the map.
        /// </summary>
        public static Dictionary<string, string> PlanSongIds(IEnumerable<string> bundled, IEnumerable<string> taken)
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in taken ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    used.Add(id.Trim());
            }
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in bundled ?? Array.Empty<string>())
            {
                var id = (raw ?? "").Trim();
                if (!id.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                    continue;
                var assigned = ShellCharacterCloneService.NextFreeLinkage(used, id);
                used.Add(assigned);
                if (!assigned.Equals(id, StringComparison.OrdinalIgnoreCase))
                    map[id] = assigned;
            }
            return map;
        }

        public static async Task InstallAsync(
            string shellRoot,
            OneClickReceipt receipt,
            IReadOnlyList<OneClickItem> chosen,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(shellRoot) || !Directory.Exists(shellRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            var selected = (chosen ?? Array.Empty<OneClickItem>()).Where(i => i.Selected).ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("Nothing was selected.");

            if (selected.Any(i => i.Kind == OneClickKind.Pack))
            {
                var root = receipt.PackRoot ?? receipt.ExtractedRoot;
                progress?.Report("Installing pack…");
                var placed = ModPackageService.DeployExtracted(shellRoot, root, includeSingleGenericMod: true);
                DebugLogger.Log($"[oneclick] installed stages=0 characters=0 songs=0 packs={placed.Count}");
                return;
            }

            var songs = selected.Where(i => i.Kind == OneClickKind.Song).ToList();
            var stages = selected.Where(i => i.Kind == OneClickKind.Stage).ToList();
            var characters = selected.Where(i => i.Kind == OneClickKind.Character).ToList();

            var taken = TakenSongIds(shellRoot);
            var map = PlanSongIds(songs.Select(s => s.Id ?? ""), taken);
            var stagePaths = new Dictionary<OneClickItem, string>();
            if (map.Count > 0)
            {
                var clone = new CharacterCloneService();
                foreach (var stage in stages)
                {
                    var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(stage.FullPath, cancellationToken));
                    var pairs = map
                        .Where(kv => IndexOfAscii(bytes, kv.Key) >= 0)
                        .Select(kv => (kv.Key, kv.Value))
                        .ToList();
                    if (pairs.Count == 0)
                    {
                        stagePaths[stage] = stage.FullPath;
                        continue;
                    }
                    progress?.Report("Pointing the stage at the imported songs…");
                    var work = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-music-" + Guid.NewGuid().ToString("N"));
                    stagePaths[stage] = await clone.RewritePairsAsync(stage.FullPath, work, pairs, cancellationToken);
                }
            }

            var mappingsEdit = new MappingsEditService();
            JObject? mappings = null;
            try
            {
                mappings = await mappingsEdit.LoadDocumentAsync(shellRoot, exportIfMissing: false, cancellationToken: cancellationToken);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[oneclick] mappings skip " + ex.Message);
            }

            foreach (var song in songs)
            {
                var id = song.Id ?? "";
                if (map.TryGetValue(id, out var assigned))
                    id = assigned;
                progress?.Report("Importing song " + id + "…");
                await PlaceSongAsync(shellRoot, song.FullPath, id, mappings, cancellationToken);
            }
            if (mappings != null && songs.Count > 0)
            {
                try
                {
                    await mappingsEdit.CommitAsync(shellRoot, mappings, null, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log("[oneclick] mappings commit skip " + ex.Message);
                    await mappingsEdit.SaveDocumentAsync(shellRoot, mappings, cancellationToken);
                }
            }

            var stageImport = new ShellStageCloneService();
            foreach (var stage in stages)
            {
                progress?.Report("Importing stage " + (stage.Id ?? stage.RelativePath) + "…");
                var path = stagePaths.TryGetValue(stage, out var rewritten) ? rewritten : stage.FullPath;
                await stageImport.ImportIntoShellAsync(
                    shellRoot, path, ShellStageCloneService.PreviewBesideSwf(path),
                    progress, cancellationToken, skipFla: true);
            }

            var characterImport = new ShellCharacterCloneService();
            foreach (var character in characters)
            {
                progress?.Report("Importing character " + (character.Id ?? character.RelativePath) + "…");
                await characterImport.ImportIntoShellAsync(
                    shellRoot, character.FullPath, progress, cancellationToken, skipFla: true);
            }

            DebugLogger.Log($"[oneclick] installed stages={stages.Count} characters={characters.Count} songs={songs.Count}");
        }

        private static async Task PlaceSongAsync(
            string shellRoot, string sourcePath, string trackId, JObject? mappings, CancellationToken cancellationToken)
        {
            var sound = Path.Combine(shellRoot, "data", "sound");
            Directory.CreateDirectory(sound);
            var dest = Path.Combine(sound, trackId + ".swf");
            var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(sourcePath, cancellationToken));
            await File.WriteAllBytesAsync(dest, bytes, cancellationToken);

            if (mappings != null)
            {
                var guid = ReadSongGuid(bytes) ?? MappingsEditService.AllocateUniqueGuid(mappings);
                MappingsEditService.UpsertEntry(mappings, "music", trackId, trackId + ".swf", name: trackId, guid: guid);
            }

            try
            {
                ContentPackService.EnsureRoot(shellRoot);
                var index = ContentPackService.LoadIndex(shellRoot);
                ContentPackService.RegisterMusicPack(index, trackId);
                ContentPackService.SaveIndex(shellRoot, index);
                ContentPackService.SavePack(shellRoot, new ContentPackInfo
                {
                    PackId = trackId,
                    Type = ContentPackType.Music,
                    Name = trackId,
                    Guid = mappings?["music"]?[trackId]?["guid"]?.ToString() ?? Guid.NewGuid().ToString(),
                    Slot = -1,
                    PlayableId = trackId,
                    ContentRelativePath = trackId + ".swf"
                });
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[oneclick] song pack skip " + ex.Message);
            }
        }

        private static HashSet<string> TakenSongIds(string shellRoot)
        {
            var taken = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var sound = Path.Combine(shellRoot, "data", "sound");
            if (!Directory.Exists(sound))
                return taken;
            foreach (var file in Directory.GetFiles(sound))
            {
                var name = Path.GetFileNameWithoutExtension(file);
                if (name.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                    taken.Add(name);
            }
            return taken;
        }

        private static void PreferOwnSlot(List<OneClickItem> items)
        {
            var stages = items.Where(i => i.Kind == OneClickKind.Stage).ToList();
            var own = stages.Where(s => IsOwnSlot(s.RelativePath)).ToList();
            var over = stages.Where(s => IsOverSlot(s.RelativePath)).ToList();
            if (own.Count == 0 || over.Count == 0)
                return;
            foreach (var item in over)
            {
                item.Selected = false;
                item.Note = "Replaces a stock stage";
            }
            foreach (var item in own)
                item.Note = string.IsNullOrWhiteSpace(item.Note) ? "Own slot" : item.Note;
        }

        private static OneClickItem? ClassifyFile(string path, string relative)
        {
            byte[] raw;
            try { raw = File.ReadAllBytes(path); }
            catch { return null; }
            byte[] swf;
            try { swf = SwfPackage.ToSwf(raw); }
            catch { return null; }

            var document = SwfPackage.DocumentClass(raw);
            var (stageId, _) = ShellStageCloneService.ReadStageImportIdentity(swf);
            var stageClip = HasStageClip(swf);
            if (SwfPackage.IsStage(swf) || stageClip)
            {
                return new OneClickItem
                {
                    Kind = OneClickKind.Stage,
                    RelativePath = relative,
                    FullPath = path,
                    Id = stageId,
                    Selected = true
                };
            }

            if (SwfPackage.IsCharacter(swf) && (SwfPackage.IsBetaDocument(document) || document == null || SwfPackage.IsTimelineDocument(document)))
            {
                var (linkage, _) = CharacterCloneService.ReadImportIdentity(swf);
                return new OneClickItem
                {
                    Kind = OneClickKind.Character,
                    RelativePath = relative,
                    FullPath = path,
                    Id = linkage,
                    Selected = true
                };
            }

            if (SwfPackage.IsBetaDocument(document) || Path.GetFileName(path).StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
            {
                var songId = SongId(path, swf);
                if (songId != null)
                {
                    return new OneClickItem
                    {
                        Kind = OneClickKind.Song,
                        RelativePath = relative,
                        FullPath = path,
                        Id = songId,
                        Selected = true
                    };
                }
            }

            return null;
        }

        private static bool HasStageClip(byte[] swf)
        {
            foreach (var text in AsciiStrings(swf))
            {
                if (text.Equals("stage_effects", StringComparison.Ordinal))
                    continue;
                if (text.StartsWith("stage_", StringComparison.Ordinal)
                    && text.Length > "stage_".Length
                    && CharacterCloneService.IsPlainLinkage(text["stage_".Length..]))
                    return true;
            }
            return false;
        }

        private static string? SongId(string path, byte[] swf)
        {
            var file = Path.GetFileNameWithoutExtension(path);
            if (file.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)
                && CharacterCloneService.IsPlainLinkage(file))
                return file;
            foreach (var text in AsciiStrings(swf))
            {
                if (text.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase)
                    && CharacterCloneService.IsPlainLinkage(text))
                    return text;
            }
            return null;
        }

        private static IEnumerable<string> AsciiStrings(byte[] bytes)
        {
            var i = 0;
            while (i < bytes.Length)
            {
                if (bytes[i] >= 32 && bytes[i] < 127)
                {
                    var start = i;
                    while (i < bytes.Length && bytes[i] >= 32 && bytes[i] < 127)
                        i++;
                    if (i - start >= 2 && i - start <= 80)
                        yield return Encoding.ASCII.GetString(bytes, start, i - start);
                }
                else
                {
                    i++;
                }
            }
        }

        private static int IndexOfAscii(byte[] haystack, string needle)
        {
            var bytes = Encoding.ASCII.GetBytes(needle);
            for (var i = 0; i <= haystack.Length - bytes.Length; i++)
            {
                if (haystack.AsSpan(i, bytes.Length).SequenceEqual(bytes))
                    return i;
            }
            return -1;
        }

        private static string? FindManifest(string extractedRoot)
        {
            var direct = Path.Combine(extractedRoot, "ssf2mm-pack.json");
            var path = File.Exists(direct)
                ? direct
                : Directory.GetFiles(extractedRoot, "ssf2mm-pack.json", SearchOption.AllDirectories).FirstOrDefault();
            if (path == null)
                return null;
            try
            {
                var obj = JObject.Parse(File.ReadAllText(path));
                return obj["format"]?.ToString() == "ssf2mm-pack" ? path : null;
            }
            catch
            {
                return null;
            }
        }

        private static bool IsSwfName(string path)
        {
            var ext = Path.GetExtension(path);
            return ext.Equals(".swf", StringComparison.OrdinalIgnoreCase)
                || ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> Segments(string relative) =>
            relative.Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries);

        private static bool IsSandboxSegment(string segment) =>
            segment.Contains("sandbox", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The guid stored after the ABC key <c>guid</c>. The game checks this against mappings.
        /// </summary>
        public static string? ReadSongGuid(byte[] swf)
        {
            if (swf == null || swf.Length < 8)
                return null;
            var key = Encoding.ASCII.GetBytes("guid");
            for (var i = 0; i < swf.Length - key.Length - 2; i++)
            {
                if (swf[i] != key.Length)
                    continue;
                if (!swf.AsSpan(i + 1, key.Length).SequenceEqual(key))
                    continue;
                var p = i + 1 + key.Length;
                if (p >= swf.Length)
                    continue;
                var len = swf[p];
                if (len == 0 || len >= 128 || p + 1 + len > swf.Length)
                    continue;
                var text = Encoding.ASCII.GetString(swf, p + 1, len);
                if (text.All(c => c >= 32 && c < 127))
                    return text;
            }
            return null;
        }

        private static bool IsNineBSegment(string segment)
        {
            var n = new string(segment.Where(char.IsLetterOrDigit).ToArray()).ToLowerInvariant();
            return n is "9b" or "v9b" or "09b" or "v09b";
        }

        private static bool IsOwnSlot(string relative) =>
            Segments(relative).Any(s =>
                s.Contains("own", StringComparison.OrdinalIgnoreCase)
                && s.Contains("slot", StringComparison.OrdinalIgnoreCase));

        private static bool IsOverSlot(string relative) =>
            Segments(relative).Any(s => s.StartsWith("over", StringComparison.OrdinalIgnoreCase));

        private static string ExtensionFromMagic(string path)
        {
            var head = new byte[8];
            using var stream = File.OpenRead(path);
            var n = stream.Read(head, 0, head.Length);
            if (n >= 2 && head[0] == (byte)'P' && head[1] == (byte)'K')
                return ".zip";
            if (n >= 4 && head[0] == (byte)'R' && head[1] == (byte)'a' && head[2] == (byte)'r')
                return ".rar";
            if (n >= 6 && head[0] == (byte)'7' && head[1] == (byte)'z')
                return ".7z";
            return ".bin";
        }
    }
}
