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
        Song,
        Alt
    }

    public sealed class OneClickItem
    {
        public OneClickKind Kind { get; init; }
        public string RelativePath { get; init; } = "";
        public string FullPath { get; init; } = "";
        public string? Id { get; init; }
        public string Note { get; set; } = "";
        public bool Selected { get; set; } = true;
        /// <summary>The name stored in the file (stage or character); null for songs.</summary>
        public string? OriginalName { get; set; }
        /// <summary>The name the user confirmed. A stage or character file is rewritten when it differs from <see cref="OriginalName"/>.</summary>
        public string? DisplayName { get; set; }
        /// <summary>A stage preview the user picked (PNG or JPG); saved as stagepreview.png.</summary>
        public string? PreviewPath { get; set; }
        /// <summary>An alt's files: the resource id each one registers, and its path in the download.</summary>
        public List<(string ResourceId, string Path)> Files { get; init; } = new();

        public string Label
        {
            get
            {
                if (Kind == OneClickKind.Alt)
                    return $"Alt of {Id}: {Path.GetFileName(RelativePath)} ({Files.Count} file(s))";
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
        /// <summary>Files under a <c>Vanilla</c> folder (the build already has them).</summary>
        public int LeftOutVanilla { get; set; }
        /// <summary>Files under an <c>Announcer</c> folder (global sounds, not a character).</summary>
        public int LeftOutAnnouncer { get; set; }

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
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                await using var file = File.Create(partial);
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
            catch
            {
                try { Directory.Delete(temp, true); } catch { }
                throw;
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

        /// <summary>Where downloads wait, e.g. C:\Users\x\AppData\Local\Temp\ssf2mm-oneclick-….</summary>
        public static string TempFolderPattern => Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-…");

        /// <summary>Says where the download is held and that unused files are deleted.</summary>
        public static string TempNote(bool alreadyDeleted) => alreadyDeleted
            ? $"The download was held in a temporary folder ({TempFolderPattern}) and has been deleted."
            : $"The download is held in a temporary folder ({TempFolderPattern}). Anything you leave unchecked, and the download itself, is deleted when you install or cancel.";

        /// <summary>The text above the confirm list.</summary>
        public static string ReceiptIntro(OneClickReceipt receipt)
        {
            var lines = new List<string> { "Choose what to install into SSF2 Shell." };
            if (receipt.Items.Any(i => !i.Selected && i.Note == "Replaces a built-in stage"))
                lines.Add("This mod also includes a version that replaces a built-in stage. It starts unchecked so your stage list stays as it is.");
            if (receipt.LeftOutNineB > 0)
                lines.Add($"{receipt.LeftOutNineB} file(s) made for SSF2 0.9b were skipped because SSF2 Shell can't load them.");
            if (receipt.LeftOutOtherVersions > 0)
                lines.Add($"{receipt.LeftOutOtherVersions} file(s) for other SSF2 versions were skipped. The Sandbox version is listed instead.");
            if (receipt.Items.Any(i => i.Kind == OneClickKind.Alt))
                lines.Add("Alts load in place of a character you already have. On Character Select, put your token on the character and press jump to switch between the original and each alt.");
            if (receipt.LeftOutVanilla > 0)
                lines.Add($"{receipt.LeftOutVanilla} file(s) in the mod's Vanilla folder were skipped because SSF2 Shell already has them.");
            if (receipt.LeftOutAnnouncer > 0)
                lines.Add($"{receipt.LeftOutAnnouncer} announcer/sound file(s) were skipped.");
            lines.Add(TempNote(false));
            return string.Join("\n\n", lines);
        }

        /// <summary>The error shown instead of an empty confirm list, or null when something can be installed.</summary>
        public static string? NothingToInstallMessage(OneClickReceipt receipt, string? modName = null)
        {
            if (receipt.IsPack || receipt.Items.Count > 0)
                return null;
            var name = string.IsNullOrWhiteSpace(modName) ? "This mod" : modName;
            var lines = new List<string> { name + " can't be installed in SSF2 Shell. None of its files are supported.", "" };
            if (receipt.LeftOutNineB > 0)
                lines.Add($"• {receipt.LeftOutNineB} file(s) are made for SSF2 0.9b, which SSF2 Shell can't load.");
            if (receipt.LeftOutUnknown > 0)
                lines.Add($"• {receipt.LeftOutUnknown} file(s) aren't a character, stage or song the manager recognizes.");
            if (receipt.LeftOutNineB == 0 && receipt.LeftOutUnknown == 0)
                lines.Add("• The download has no SSF2 game files (.ssf or .swf).");
            lines.Add("");
            lines.Add("SSF2 Shell is built on SSF2 Beta 1.4.0.1. Check the mod's GameBanana page for a Beta 1.4 version.");
            lines.Add("");
            lines.Add(TempNote(true));
            DebugLogger.Log($"[oneclick] nothing to install nineb={receipt.LeftOutNineB} unknown={receipt.LeftOutUnknown}");
            return string.Join("\n", lines);
        }

        public sealed class AltPlan
        {
            public List<(string Folder, string CharacterId, List<(string ResourceId, string Path)> Files)> Alts { get; } = new();
            public int Vanilla { get; set; }
            public int Announcer { get; set; }
            /// <summary>Paths that belong to an alt, the Vanilla folder, or an announcer folder.</summary>
            public HashSet<string> Claimed { get; } = new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Groups game files by folder. A folder holding a character whose registered id the build already has is one alt;
        /// every file in that folder with a registered id is part of it. Vanilla and Announcer folders are left out.
        /// </summary>
        public static AltPlan PlanAlts(
            IEnumerable<(string RelativePath, string? RegisterId, bool IsCharacter)> files,
            ISet<string> takenCharacterIds)
        {
            var plan = new AltPlan();
            var rest = new List<(string RelativePath, string? RegisterId, bool IsCharacter)>();
            foreach (var f in files)
            {
                var parts = f.RelativePath.Replace('\\', '/').Split('/');
                var dirs = parts.Take(parts.Length - 1).ToList();
                if (dirs.Any(d => d.Equals("Vanilla", StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Vanilla++;
                    plan.Claimed.Add(f.RelativePath);
                    continue;
                }
                if (dirs.Any(d => d.Contains("Announcer", StringComparison.OrdinalIgnoreCase)))
                {
                    plan.Announcer++;
                    plan.Claimed.Add(f.RelativePath);
                    continue;
                }
                rest.Add(f);
            }
            foreach (var group in rest.GroupBy(f => Path.GetDirectoryName(f.RelativePath.Replace('/', Path.DirectorySeparatorChar)) ?? "",
                         StringComparer.OrdinalIgnoreCase))
            {
                var owner = group.FirstOrDefault(f => f.IsCharacter && f.RegisterId != null && takenCharacterIds.Contains(f.RegisterId));
                if (owner.RegisterId == null)
                    continue;
                var members = group.Where(f => !string.IsNullOrWhiteSpace(f.RegisterId)).ToList();
                plan.Alts.Add((group.Key, owner.RegisterId, members.Select(f => (f.RegisterId!, f.RelativePath)).ToList()));
                foreach (var f in group)
                    plan.Claimed.Add(f.RelativePath);
            }
            return plan;
        }

        /// <summary>Character ids in the build: mappings <c>character</c> keys and <c>expansions/character</c> folders.</summary>
        public static async Task<HashSet<string>> TakenCharacterIdsAsync(string shellRoot, CancellationToken cancellationToken = default)
        {
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var mappings = await new MappingsEditService().LoadDocumentAsync(shellRoot, exportIfMissing: false, cancellationToken: cancellationToken);
                if (mappings?["character"] is JObject characters)
                    foreach (var prop in characters.Properties())
                        ids.Add(prop.Name);
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[oneclick] alts mappings skip " + ex.Message);
            }
            var expansions = ShellWorkspaceService.CharacterExpansionsDirectory(shellRoot);
            if (Directory.Exists(expansions))
                foreach (var dir in Directory.GetDirectories(expansions))
                    ids.Add(Path.GetFileName(dir));
            return ids;
        }

        /// <summary>Turns files for characters the build already has into alts (see <see cref="PlanAlts"/>).</summary>
        public static void ClassifyAlts(OneClickReceipt receipt, ISet<string> takenCharacterIds)
        {
            if (receipt.IsPack || string.IsNullOrWhiteSpace(receipt.ExtractedRoot) || !Directory.Exists(receipt.ExtractedRoot))
                return;
            var scanned = new List<(string RelativePath, string? RegisterId, bool IsCharacter)>();
            foreach (var path in Directory.GetFiles(receipt.ExtractedRoot, "*.*", SearchOption.AllDirectories)
                         .Where(p => p.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase) || p.EndsWith(".swf", StringComparison.OrdinalIgnoreCase)))
            {
                string? id = null;
                var character = false;
                try
                {
                    var swf = SwfPackage.ToSwf(File.ReadAllBytes(path));
                    id = SwfPackage.RegisterCalls(swf).FirstOrDefault(r => r.Key == "id").Value;
                    character = SwfPackage.IsCharacter(swf);
                }
                catch (Exception ex)
                {
                    DebugLogger.Log($"[oneclick] alts read skip {Path.GetFileName(path)} {ex.Message}");
                }
                scanned.Add((Path.GetRelativePath(receipt.ExtractedRoot, path), id, character));
            }
            var plan = PlanAlts(scanned, takenCharacterIds);
            if (plan.Claimed.Count == 0)
                return;
            receipt.Items.RemoveAll(i => plan.Claimed.Contains(i.RelativePath));
            foreach (var (folder, characterId, files) in plan.Alts)
            {
                receipt.Items.Add(new OneClickItem
                {
                    Kind = OneClickKind.Alt,
                    Id = characterId,
                    RelativePath = folder,
                    FullPath = Path.Combine(receipt.ExtractedRoot, folder),
                    Selected = true,
                    OriginalName = Path.GetFileName(folder),
                    Files = files.Select(f => (f.ResourceId, Path.Combine(receipt.ExtractedRoot, f.Path))).ToList()
                });
            }
            receipt.LeftOutVanilla += plan.Vanilla;
            receipt.LeftOutAnnouncer += plan.Announcer;
            DebugLogger.Log($"[oneclick] alts groups={plan.Alts.Count} vanilla={plan.Vanilla} announcer={plan.Announcer}");
        }

        /// <summary>The error shown before download when GameBanana's file list has no SSF2 game files.</summary>
        public static string NothingInFileListMessage(IReadOnlyList<string> paths, string? modName = null)
        {
            var name = string.IsNullOrWhiteSpace(modName) ? "This mod" : modName;
            var shown = paths.Take(8).Select(p => "• " + p).ToList();
            if (paths.Count > shown.Count)
                shown.Add($"• … and {paths.Count - shown.Count} more");
            return name + " can't be installed in SSF2 Shell. GameBanana lists no SSF2 game files (.ssf or .swf) in it:\n\n"
                + string.Join("\n", shown)
                + "\n\nNothing was downloaded.";
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
            CancellationToken cancellationToken = default,
            int gameBananaModId = 0,
            int gameBananaFileId = 0,
            string? modName = null)
        {
            if (string.IsNullOrWhiteSpace(shellRoot) || !Directory.Exists(shellRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            var selected = (chosen ?? Array.Empty<OneClickItem>()).Where(i => i.Selected).ToList();
            if (selected.Count == 0)
                throw new InvalidOperationException("Nothing was selected.");
            var installed = new List<(OneClickKind Kind, string Id)>();

            if (selected.Any(i => i.Kind == OneClickKind.Pack))
            {
                var root = receipt.PackRoot ?? receipt.ExtractedRoot;
                progress?.Report("Installing pack…");
                var placed = ModPackageService.DeployExtracted(shellRoot, root, includeSingleGenericMod: true);
                foreach (var pack in placed)
                {
                    StampGameBananaId(pack.Dest, gameBananaModId);
                    installed.Add((OneClickKind.Pack, Path.GetRelativePath(ShellWorkspaceService.ExpansionsRoot(shellRoot), pack.Dest)));
                }
                GameBananaInstalls.Record(shellRoot, gameBananaModId, gameBananaFileId, modName, installed);
                DebugLogger.Log($"[oneclick] installed stages=0 characters=0 songs=0 packs={placed.Count}");
                return;
            }

            var songs = selected.Where(i => i.Kind == OneClickKind.Song).ToList();
            var stages = selected.Where(i => i.Kind == OneClickKind.Stage).ToList();
            var characters = selected.Where(i => i.Kind == OneClickKind.Character).ToList();
            var alts = selected.Where(i => i.Kind == OneClickKind.Alt).ToList();

            foreach (var alt in alts)
            {
                var name = alt.DisplayName == null
                    ? CharacterAltsService.NameFromFolder(alt.OriginalName ?? "", alt.Id)
                    : string.IsNullOrWhiteSpace(alt.DisplayName) ? null : alt.DisplayName.Trim();
                progress?.Report($"Adding alt {name ?? alt.Id}…");
                var folder = CharacterAltsService.Install(shellRoot, gameBananaModId, alt.Id!, name, alt.Files, alt.OriginalName);
                installed.Add((OneClickKind.Alt, folder));
            }

            var taken = TakenSongIds(shellRoot);
            var map = PlanSongIds(songs.Select(s => s.Id ?? ""), taken);
            var rewritten = new Dictionary<OneClickItem, string>();
            var clone = new CharacterCloneService();
            foreach (var item in stages.Concat(characters))
            {
                var pairs = new List<(string From, string To)>();
                if (item.Kind == OneClickKind.Stage && map.Count > 0)
                {
                    var bytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(item.FullPath, cancellationToken));
                    pairs.AddRange(map.Where(kv => IndexOfAscii(bytes, kv.Key) >= 0).Select(kv => (kv.Key, kv.Value)));
                }
                var rename = NamePair(item);
                if (rename != null)
                    pairs.Add(rename.Value);
                else if (item.Kind == OneClickKind.Character && !string.IsNullOrWhiteSpace(item.DisplayName)
                    && !item.DisplayName.Equals(item.OriginalName, StringComparison.Ordinal))
                    DebugLogger.Log($"[oneclick] rename skip {item.Kind} id={item.Id} reason=no-display-name-in-file stored={item.OriginalName}");
                if (pairs.Count == 0)
                    continue;
                progress?.Report(rename != null
                    ? $"Naming {item.Id} \"{rename.Value.To}\"…"
                    : "Pointing the stage at the imported songs…");
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-rewrite-" + Guid.NewGuid().ToString("N"));
                rewritten[item] = await clone.RewritePairsAsync(item.FullPath, work, pairs, cancellationToken);
                DebugLogger.Log($"[oneclick] rewrite {item.Kind} id={item.Id} pairs={pairs.Count} rename={(rename != null ? 1 : 0)}");
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
                installed.Add((OneClickKind.Song, id));
            }

            var stageImport = new ShellStageCloneService();
            var named = 0;
            foreach (var stage in stages)
            {
                progress?.Report("Importing stage " + (stage.Id ?? stage.RelativePath) + "…");
                var path = rewritten.TryGetValue(stage, out var swapped) ? swapped : stage.FullPath;
                var dest = await stageImport.ImportIntoShellAsync(
                    shellRoot, path, ShellStageCloneService.PreviewBesideSwf(stage.FullPath),
                    progress, cancellationToken, skipFla: true);
                SavePreview(stage, dest);
                StampGameBananaId(dest, gameBananaModId);
                var stageId = Path.GetFileName(dest);
                if (!string.IsNullOrWhiteSpace(stage.DisplayName))
                {
                    SetInfoName(dest, stage.DisplayName!);
                    if (mappings != null)
                    {
                        NameStage(mappings, stageId, stage.DisplayName!, stage.FullPath);
                        named++;
                    }
                }
                installed.Add((OneClickKind.Stage, stageId));
            }

            if (mappings != null && (songs.Count > 0 || named > 0))
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

            var characterImport = new ShellCharacterCloneService();
            foreach (var character in characters)
            {
                progress?.Report("Importing character " + (character.Id ?? character.RelativePath) + "…");
                var path = rewritten.TryGetValue(character, out var swapped) ? swapped : character.FullPath;
                var result = await characterImport.ImportIntoShellAsync(
                    shellRoot, path, progress, cancellationToken, skipFla: true);
                StampGameBananaId(result.ExpansionFolder, gameBananaModId);
                installed.Add((OneClickKind.Character, Path.GetFileName(result.ExpansionFolder)));
            }

            foreach (var copy in rewritten.Values)
            {
                try { Directory.Delete(Path.GetDirectoryName(copy)!, true); } catch { }
            }
            GameBananaInstalls.Record(shellRoot, gameBananaModId, gameBananaFileId, modName, installed);
            DebugLogger.Log($"[oneclick] installed stages={stages.Count} characters={characters.Count} songs={songs.Count} alts={alts.Count}");
        }

        /// <summary>
        /// (stored name, confirmed name) for a character whose file stores a display name the user changed.
        /// Stages are named through mappings (<see cref="NameStage"/>). A stored name equal to the id is the
        /// linkage, and rewriting it would rename the id.
        /// </summary>
        public static (string From, string To)? NamePair(OneClickItem item)
        {
            if (item.Kind != OneClickKind.Character)
                return null;
            var from = item.OriginalName?.Trim();
            var to = item.DisplayName?.Trim();
            if (string.IsNullOrEmpty(from) || string.IsNullOrEmpty(to) || from.Equals(to, StringComparison.Ordinal))
                return null;
            if (!string.IsNullOrEmpty(item.Id) && from.Equals(item.Id, StringComparison.Ordinal))
                return null;
            return (from, to);
        }

        /// <summary>
        /// Sets <c>stage[id].name</c>, which <c>StageSelectMenu.displayName</c> reads before it falls back to the id.
        /// A new entry points at <c>expansions/stages/&lt;id&gt;/&lt;id&gt;.swf</c> with the guid the stage file registers.
        /// </summary>
        public static void NameStage(JObject mappings, string stageId, string name, string? sourceFile = null)
        {
            var existing = (mappings["stage"] as JObject)?[stageId] as JObject;
            if (existing != null)
            {
                existing["name"] = name;
                DebugLogger.Log($"[oneclick] stage name id={stageId} entry=existing");
                return;
            }
            string? guid = null;
            if (!string.IsNullOrWhiteSpace(sourceFile) && File.Exists(sourceFile))
            {
                try { guid = SwfPackage.RegisterCalls(File.ReadAllBytes(sourceFile)).FirstOrDefault(r => r.Key == "guid").Value; }
                catch { }
            }
            MappingsEditService.UpsertEntry(mappings, "stage", stageId, $"expansions/stages/{stageId}/{stageId}.swf", name: name, guid: guid);
            DebugLogger.Log($"[oneclick] stage name id={stageId} entry=new guid={(guid != null ? 1 : 0)}");
        }

        private static void SetInfoName(string folder, string name)
        {
            var path = Path.Combine(folder, "info.json");
            try
            {
                var info = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
                info["name"] = name;
                File.WriteAllText(path, info.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[oneclick] info name skip " + ex.Message);
            }
        }

        /// <summary>Adds <c>"gamebanana": modId</c> to the folder's info.json.</summary>
        public static void StampGameBananaId(string? folder, int modId)
        {
            if (modId <= 0 || string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
                return;
            var path = Path.Combine(folder, "info.json");
            try
            {
                var info = File.Exists(path) ? JObject.Parse(File.ReadAllText(path)) : new JObject();
                info["gamebanana"] = modId;
                File.WriteAllText(path, info.ToString(Newtonsoft.Json.Formatting.Indented));
            }
            catch (Exception ex)
            {
                DebugLogger.Log("[oneclick] info stamp skip " + ex.Message);
            }
        }

        private static void SavePreview(OneClickItem stage, string dest)
        {
            var from = stage.PreviewPath;
            var kind = string.IsNullOrWhiteSpace(from) ? "none"
                : from.Contains(Path.DirectorySeparatorChar + GameBananaShotsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ? "gamebanana" : "archive";
            DebugLogger.Log($"[oneclick] preview stage={stage.Id} from={kind}");
            if (string.IsNullOrWhiteSpace(from) || !File.Exists(from))
                return;
            var target = Path.Combine(dest, "stagepreview.png");
            if (from.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(from, target, overwrite: true);
                return;
            }
            using var input = File.OpenRead(from);
            var decoder = System.Windows.Media.Imaging.BitmapDecoder.Create(
                input, System.Windows.Media.Imaging.BitmapCreateOptions.None, System.Windows.Media.Imaging.BitmapCacheOption.OnLoad);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(decoder.Frames[0]);
            using var output = File.Create(target);
            encoder.Save(output);
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
                item.Note = "Replaces a built-in stage";
            }
            foreach (var item in own)
                item.Note = string.IsNullOrWhiteSpace(item.Note) ? "Adds a new stage" : item.Note;
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
            var registers = SwfPackage.RegisterCalls(swf);
            if (registers.Count > 0)
            {
                if (document != null && !SwfPackage.IsBetaDocument(document) && !SwfPackage.IsTimelineDocument(document))
                    return null;
                var id = registers.FirstOrDefault(r => r.Key == "id").Value;
                var kind = registers.Any(r => r.Key == "stage") ? OneClickKind.Stage
                    : registers.Any(r => r.Key == "music") ? OneClickKind.Song
                    : SwfPackage.IsCharacter(swf) ? OneClickKind.Character
                    : (OneClickKind?)null;
                if (kind == null || string.IsNullOrWhiteSpace(id))
                    return null;
                return new OneClickItem
                {
                    Kind = kind.Value, RelativePath = relative, FullPath = path, Id = id, Selected = true,
                    OriginalName = StoredName(kind.Value, swf)
                };
            }

            var (stageId, stageName) = ShellStageCloneService.ReadStageImportIdentity(swf);
            var stageClip = HasStageClip(swf);
            if (SwfPackage.IsStage(swf) || stageClip)
            {
                return new OneClickItem
                {
                    Kind = OneClickKind.Stage,
                    RelativePath = relative,
                    FullPath = path,
                    Id = stageId,
                    Selected = true,
                    OriginalName = stageName
                };
            }

            if (SwfPackage.IsCharacter(swf) && (SwfPackage.IsBetaDocument(document) || document == null || SwfPackage.IsTimelineDocument(document)))
            {
                var (linkage, shown) = CharacterCloneService.ReadImportIdentity(swf);
                return new OneClickItem
                {
                    Kind = OneClickKind.Character,
                    RelativePath = relative,
                    FullPath = path,
                    Id = linkage,
                    Selected = true,
                    OriginalName = shown
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

        private static string? StoredName(OneClickKind kind, byte[] swf)
        {
            try
            {
                return kind switch
                {
                    OneClickKind.Stage => ShellStageCloneService.ReadStageImportIdentity(swf).DisplayName,
                    OneClickKind.Character => CharacterCloneService.ReadImportIdentity(swf).Item2,
                    _ => null
                };
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The name box's start value. One stage or one character in the download takes the GameBanana mod name;
        /// otherwise the name the file stores; a song keeps its id.
        /// </summary>
        public static string DefaultDisplayName(OneClickReceipt receipt, OneClickItem item, string? modName)
        {
            if (item.Kind == OneClickKind.Song)
                return item.Id ?? Path.GetFileNameWithoutExtension(item.RelativePath);
            if (item.Kind == OneClickKind.Alt)
                return CharacterAltsService.NameFromFolder(item.OriginalName ?? "", item.Id) ?? "";
            var alone = receipt.Items.Count(i => i.Kind == item.Kind && i.Selected) == 1 && item.Selected;
            if (alone && !string.IsNullOrWhiteSpace(modName))
                return modName.Trim();
            if (!string.IsNullOrWhiteSpace(item.OriginalName))
                return item.OriginalName!;
            return item.Id ?? Path.GetFileNameWithoutExtension(item.RelativePath);
        }

        /// <summary>PNGs in the download, archive ones first, then the GameBanana screenshots saved under <c>_gamebanana</c>.</summary>
        public static List<string> PreviewCandidates(OneClickReceipt receipt)
        {
            if (string.IsNullOrWhiteSpace(receipt.ExtractedRoot) || !Directory.Exists(receipt.ExtractedRoot))
                return new List<string>();
            return Directory.GetFiles(receipt.ExtractedRoot, "*.*", SearchOption.AllDirectories)
                .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                         || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                .OrderBy(f => IsGameBananaShot(receipt, f) ? 1 : 0)
                .ThenBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public const string GameBananaShotsFolder = "_gamebanana";

        public static bool IsGameBananaShot(OneClickReceipt receipt, string path) =>
            Path.GetRelativePath(receipt.ExtractedRoot, path)
                .StartsWith(GameBananaShotsFolder + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// False when GameBanana's file list is non-empty and names nothing the manager can install.
        /// </summary>
        public static bool RawListHasGameFiles(IReadOnlyList<string> paths)
        {
            if (paths.Count == 0)
                return true;
            return paths.Any(p =>
            {
                var ext = Path.GetExtension(p);
                return ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".swf", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".zip", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".rar", StringComparison.OrdinalIgnoreCase)
                    || ext.Equals(".7z", StringComparison.OrdinalIgnoreCase)
                    || Path.GetFileName(p).Equals("ssf2mm-pack.json", StringComparison.OrdinalIgnoreCase);
            });
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
