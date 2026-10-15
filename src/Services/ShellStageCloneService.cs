using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Clones a stock stage into <c>expansions/stages/&lt;id&gt;</c> with a stagepreview.png.
    /// </summary>
    public class ShellStageCloneService
    {
        private readonly CharacterCloneService _clone;
        private readonly JpexsCliService _jpexs;
        private readonly MappingsEditService _mappings;

        public ShellStageCloneService(
            CharacterCloneService? clone = null,
            JpexsCliService? jpexs = null,
            MappingsEditService? mappings = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _clone = clone ?? new CharacterCloneService(_jpexs);
            _mappings = mappings ?? new MappingsEditService(_jpexs);
        }

        public static List<ShellRosterCharacter> ListPlayableStages(string installRoot, JObject mappings)
        {
            var list = new List<ShellRosterCharacter>();
            if (string.IsNullOrWhiteSpace(installRoot) || mappings["stage"] is not JObject cat)
                return list;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in cat.Properties())
            {
                var id = prop.Name;
                if (string.IsNullOrWhiteSpace(id)) continue;
                if (id.Equals("random", StringComparison.OrdinalIgnoreCase)) continue;
                if (id.Equals("xpstage", StringComparison.OrdinalIgnoreCase)) continue;
                if (MappingsEditService.IsPlaceholderId(id)) continue;
                if (!seen.Add(id)) continue;
                var swf = MappingsEditService.ResolveEntrySwfPath(installRoot, mappings, "stage", id);
                if (string.IsNullOrWhiteSpace(swf) || !File.Exists(swf)) continue;
                var name = prop.Value?["name"]?.ToString();
                list.Add(new ShellRosterCharacter
                {
                    Id = id,
                    DisplayName = string.IsNullOrWhiteSpace(name) ? id : name!,
                    SwfPath = swf
                });
            }

            var extra = ShellWorkspaceService.StageExpansionsDirectory(installRoot);
            if (Directory.Exists(extra))
            {
                foreach (var dir in Directory.GetDirectories(extra))
                {
                    var folder = Path.GetFileName(dir);
                    if (string.IsNullOrWhiteSpace(folder) || folder.StartsWith('_') || folder.StartsWith('.'))
                        continue;
                    var swf = Directory.GetFiles(dir, "*.swf").FirstOrDefault();
                    if (swf == null) continue;
                    var id = Path.GetFileNameWithoutExtension(swf);
                    if (!seen.Add(id)) continue;
                    list.Add(new ShellRosterCharacter
                    {
                        Id = id,
                        DisplayName = ReadStageName(dir, id),
                        SwfPath = swf
                    });
                }
            }

            return list
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public async Task<string> CloneIntoShellAsync(
            string installRoot,
            string sourceId,
            string displayName,
            string linkageId,
            string? previewPng,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool skipFla = false,
            bool keepSourcePreview = false)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            sourceId = (sourceId ?? "").Trim();
            var folderName = CharacterCloneService.SanitizeExpansionFolderName(
                string.IsNullOrWhiteSpace(linkageId) ? displayName : linkageId);
            var newId = ShellCharacterCloneService.ExpansionGameId(folderName);
            if (sourceId.Equals(newId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("New stage name must differ from the source stage.");

            progress?.Report("Loading stages…");
            var mappings = await _mappings.LoadDocumentAsync(installRoot, exportIfMissing: false, cancellationToken: cancellationToken);
            var source = ListPlayableStages(installRoot, mappings)
                .FirstOrDefault(s => s.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException($"Source stage '{sourceId}' was not found on disk.");

            var dest = Path.Combine(ShellWorkspaceService.StageExpansionsDirectory(installRoot), newId);
            var destFull = Path.GetFullPath(dest);
            var clash = ListPlayableStages(installRoot, mappings)
                .FirstOrDefault(s =>
                {
                    if (!s.Id.Equals(newId, StringComparison.OrdinalIgnoreCase)) return false;
                    var swfDir = Path.GetDirectoryName(s.SwfPath);
                    return string.IsNullOrWhiteSpace(swfDir)
                        || !Path.GetFullPath(swfDir).Equals(destFull, StringComparison.OrdinalIgnoreCase);
                });
            if (clash != null)
                throw new ArgumentException(
                    $"Stage id '{newId}' is already in the game ({clash.DisplayName}). Pick a different name.");

            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            Directory.CreateDirectory(dest);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-stage-clone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Renaming {source.Id} → {newId}…");
                var clone = await _clone.CloneAsync(new CharacterCloneRequest
                {
                    SourcePath = source.SwfPath,
                    OldName = source.Id,
                    NewName = newId,
                    OutputDirectory = work,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? newId : displayName.Trim(),
                    SetupForReFlash2 = false,
                    IsStageClone = true
                }, progress, cancellationToken);

                var swfDest = Path.Combine(dest, newId + ".swf");
                File.Copy(clone.OutputSwfPath, swfDest, overwrite: true);

                var previewDest = Path.Combine(dest, "stagepreview.png");
                var previewSource = keepSourcePreview ? PreviewBesideSwf(source.SwfPath) : previewPng;
                if (!string.IsNullOrWhiteSpace(previewSource) && File.Exists(previewSource))
                    File.Copy(previewSource, previewDest, overwrite: true);
                if (keepSourcePreview)
                    progress?.Report(string.IsNullOrWhiteSpace(previewSource)
                        ? $"No stagepreview.png beside {source.Id}. Leaving the preview off."
                        : $"Keeping {source.Id}'s preview.");

                var info = new JObject
                {
                    ["name"] = string.IsNullOrWhiteSpace(displayName) ? newId : displayName.Trim(),
                    ["id"] = newId,
                    ["type"] = "Stage",
                    ["enabled"] = true
                };
                await File.WriteAllTextAsync(Path.Combine(dest, "info.json"), info.ToString(Formatting.Indented), cancellationToken);

                if (!skipFla)
                {
                    var flaDir = Path.Combine(dest, "FLA");
                    Directory.CreateDirectory(flaDir);
                    progress?.Report("Exporting FLA…");
                    await _jpexs.ExportFlaAsync(
                        "", swfDest, flaDir, "cs6", cancellationToken,
                        outputBaseName: newId, progress: progress, characterPretty: false,
                        stageLibrary: true);
                }
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { }
            }

            progress?.Report("Done.");
            try { CreditsService.Update(installRoot); } catch { }
            return dest;
        }

        /// <summary>
        /// Stage id and display name stored in the SWF. The id is the first <c>stage_</c> clip
        /// other than <c>stage_effects</c>. A file with no stage clip uses the first plain <c>id</c>
        /// that is not a <c>bgm_</c> track, then a <c>_bg</c> name. Display name is <c>displayName</c>
        /// when that string sits between the record's <c>id</c> and <c>backgrounds</c>.
        /// Otherwise the display name is the id.
        /// </summary>
        public static (string? Linkage, string? DisplayName) ReadStageImportIdentity(byte[] swf)
        {
            var strings = new List<string>();
            var i = 0;
            var bytes = swf ?? Array.Empty<byte>();
            while (i < bytes.Length)
            {
                if (bytes[i] >= 32 && bytes[i] < 127)
                {
                    var start = i;
                    while (i < bytes.Length && bytes[i] >= 32 && bytes[i] < 127)
                        i++;
                    if (i - start >= 2 && i - start <= 80)
                        strings.Add(Encoding.ASCII.GetString(bytes, start, i - start));
                }
                else
                {
                    i++;
                }
            }

            string? looseDisplay = null;
            string? linkage = null;
            string? stageRoot = null;
            string? bgRoot = null;
            var backgroundsAt = -1;
            for (var n = 0; n < strings.Count; n++)
            {
                var text = strings[n];
                if (backgroundsAt < 0 && text == "backgrounds")
                    backgroundsAt = n;
                if (stageRoot == null
                    && !text.Equals("stage_effects", StringComparison.Ordinal)
                    && text.StartsWith("stage_", StringComparison.Ordinal)
                    && text.Length > "stage_".Length
                    && CharacterCloneService.IsPlainLinkage(text["stage_".Length..]))
                    stageRoot = text["stage_".Length..];
                if (bgRoot == null
                    && text.EndsWith("_bg", StringComparison.Ordinal)
                    && text.Length > 3
                    && CharacterCloneService.IsPlainLinkage(text[..^3]))
                    bgRoot = text[..^3];
                if (n + 1 >= strings.Count)
                    continue;
                var next = strings[n + 1];
                if (looseDisplay == null && text == "displayName" && !string.IsNullOrWhiteSpace(next))
                    looseDisplay = next.Trim();
                if (linkage == null
                    && text == "id"
                    && CharacterCloneService.IsPlainLinkage(next)
                    && !next.StartsWith("bgm_", StringComparison.OrdinalIgnoreCase))
                    linkage = next;
            }

            var recordId = StageRecordId(strings, backgroundsAt);
            string? recordDisplay = null;
            if (recordId >= 0)
            {
                for (var n = recordId; n < backgroundsAt; n++)
                {
                    if (strings[n] == "displayName" && !string.IsNullOrWhiteSpace(strings[n + 1]))
                    {
                        recordDisplay = strings[n + 1].Trim();
                        break;
                    }
                }
            }

            if (CharacterCloneService.IsPlainLinkage(stageRoot))
                linkage = stageRoot;
            else if (!CharacterCloneService.IsPlainLinkage(linkage))
                linkage = bgRoot;

            string? shown;
            if (!string.IsNullOrWhiteSpace(recordDisplay))
                shown = recordDisplay;
            else if (stageRoot == null && recordId < 0 && !string.IsNullOrWhiteSpace(looseDisplay))
                shown = looseDisplay;
            else
                shown = linkage;
            return (linkage, shown);
        }

        /// <summary>
        /// Index of the stage record's <c>id</c>, the string before <c>resources</c> or <c>movieclips</c>
        /// and before <c>backgrounds</c>. -1 when the file has no stage record.
        /// </summary>
        private static int StageRecordId(List<string> strings, int backgroundsAt)
        {
            if (backgroundsAt < 0)
                return -1;
            var marker = -1;
            for (var n = backgroundsAt - 1; n >= 0; n--)
            {
                if (strings[n] == "resources" || strings[n] == "movieclips")
                {
                    marker = n;
                    break;
                }
            }
            if (marker < 0)
                return -1;
            var floor = Math.Max(0, marker - 15);
            for (var n = marker; n >= floor; n--)
            {
                if (strings[n] == "id")
                    return n;
            }
            return -1;
        }

        /// <summary><c>stagepreview.png</c> in the same folder as the SWF, when that file exists.</summary>
        public static string? PreviewBesideSwf(string? swfPath)
        {
            if (string.IsNullOrWhiteSpace(swfPath))
                return null;
            var dir = Path.GetDirectoryName(swfPath);
            if (string.IsNullOrEmpty(dir))
                return null;
            var preview = Path.Combine(dir, "stagepreview.png");
            return File.Exists(preview) ? preview : null;
        }

        public static List<string> TakenStageIds(string installRoot, JObject mappings)
        {
            var ids = new List<string>();
            foreach (var stage in ListPlayableStages(installRoot, mappings))
            {
                if (!string.IsNullOrWhiteSpace(stage.Id))
                    ids.Add(stage.Id);
            }
            var extra = ShellWorkspaceService.StageExpansionsDirectory(installRoot);
            if (!Directory.Exists(extra))
                return ids;
            foreach (var dir in Directory.GetDirectories(extra))
            {
                var name = Path.GetFileName(dir);
                if (string.IsNullOrWhiteSpace(name) || name.StartsWith('_') || name.StartsWith('.'))
                    continue;
                try
                {
                    ids.Add(ShellCharacterCloneService.ExpansionGameId(name));
                }
                catch (ArgumentException)
                {
                }
            }
            return ids;
        }

        public async Task<string> ImportIntoShellAsync(
            string installRoot,
            string sourceSwf,
            string? previewPng,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool skipFla = false,
            bool keepSourcePreview = false)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            if (string.IsNullOrWhiteSpace(sourceSwf) || !File.Exists(sourceSwf))
                throw new FileNotFoundException("Stage file was not found.", sourceSwf);

            progress?.Report("Reading stage…");
            var swfBytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(sourceSwf, cancellationToken));
            if (SwfPackage.IsCharacter(swfBytes))
                throw new InvalidDataException("That file is a character. Use Import a character.");
            var (sourceLinkage, shown) = ReadStageImportIdentity(swfBytes);
            if (!CharacterCloneService.IsPlainLinkage(sourceLinkage) || string.IsNullOrWhiteSpace(shown))
                throw new InvalidDataException("Could not read a stage id from that file.");

            progress?.Report("Loading stages…");
            var mappings = await _mappings.LoadDocumentAsync(installRoot, exportIfMissing: false, cancellationToken: cancellationToken);
            var taken = TakenStageIds(installRoot, mappings);
            var assetSource = ShellCharacterCloneService.CloneAssetSource(sourceLinkage, taken);
            var assigned = ShellCharacterCloneService.NextFreeLinkage(taken, sourceLinkage!);
            var newId = ShellCharacterCloneService.ExpansionGameId(assigned);

            var dest = Path.Combine(ShellWorkspaceService.StageExpansionsDirectory(installRoot), newId);
            if (Directory.Exists(dest))
                Directory.Delete(dest, recursive: true);
            Directory.CreateDirectory(dest);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-stage-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var prepared = Path.Combine(work, "prepared.swf");
                await File.WriteAllBytesAsync(prepared, swfBytes, cancellationToken);
                string produced;
                if (assigned.Equals(sourceLinkage, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report("Copying stage…");
                    produced = prepared;
                }
                else
                {
                    progress?.Report($"Id {sourceLinkage} is taken. Importing as {assigned}…");
                    var clone = await _clone.CloneAsync(new CharacterCloneRequest
                    {
                        SourcePath = prepared,
                        OldName = sourceLinkage!,
                        NewName = assigned,
                        OutputDirectory = work,
                        DisplayName = shown!,
                        SetupForReFlash2 = false,
                        IsStageClone = true
                    }, progress, cancellationToken);
                    produced = clone.OutputSwfPath;
                }

                var swfDest = Path.Combine(dest, newId + ".swf");
                File.Copy(produced, swfDest, overwrite: true);

                string? previewSource = null;
                if (keepSourcePreview && !string.IsNullOrWhiteSpace(assetSource))
                {
                    var donor = ListPlayableStages(installRoot, mappings)
                        .FirstOrDefault(s => s.Id.Equals(assetSource, StringComparison.OrdinalIgnoreCase));
                    previewSource = PreviewBesideSwf(donor?.SwfPath);
                    progress?.Report(string.IsNullOrWhiteSpace(previewSource)
                        ? $"No stagepreview.png beside {assetSource}. Leaving the preview off."
                        : $"Keeping {assetSource}'s preview.");
                }
                else
                {
                    previewSource = PreviewBesideSwf(sourceSwf) ?? previewPng;
                }
                if (!string.IsNullOrWhiteSpace(previewSource) && File.Exists(previewSource))
                    File.Copy(previewSource, Path.Combine(dest, "stagepreview.png"), overwrite: true);

                var info = new JObject
                {
                    ["name"] = shown,
                    ["id"] = newId,
                    ["type"] = "Stage",
                    ["enabled"] = true
                };
                await File.WriteAllTextAsync(Path.Combine(dest, "info.json"), info.ToString(Formatting.Indented), cancellationToken);

                if (!skipFla)
                {
                    var flaDir = Path.Combine(dest, "FLA");
                    Directory.CreateDirectory(flaDir);
                    progress?.Report("Exporting FLA…");
                    await _jpexs.ExportFlaAsync(
                        "", swfDest, flaDir, "cs6", cancellationToken,
                        outputBaseName: newId, progress: progress, characterPretty: false,
                        stageLibrary: true);
                }
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { }
            }

            progress?.Report("Done.");
            try { CreditsService.Update(installRoot); } catch { }
            return dest;
        }

        public static string? TakenNameMessage(
            IEnumerable<ShellRosterCharacter> roster,
            string displayName,
            string linkageId,
            string? ignoreFolderId = null)
        {
            displayName = (displayName ?? "").Trim();
            linkageId = (linkageId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(displayName) && string.IsNullOrWhiteSpace(linkageId))
                return null;
            string newId;
            try
            {
                var folder = CharacterCloneService.SanitizeExpansionFolderName(
                    string.IsNullOrWhiteSpace(linkageId) ? displayName : linkageId);
                newId = ShellCharacterCloneService.ExpansionGameId(folder);
            }
            catch
            {
                return null;
            }

            foreach (var stage in roster ?? Array.Empty<ShellRosterCharacter>())
            {
                if (string.IsNullOrWhiteSpace(stage.Id)) continue;
                if (!string.IsNullOrWhiteSpace(ignoreFolderId)
                    && stage.Id.Equals(ignoreFolderId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (stage.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))
                    return $"Cannot use — '{newId}' is already taken by {stage.DisplayName}.";
                if (!string.IsNullOrWhiteSpace(displayName)
                    && displayName.Equals(stage.DisplayName, StringComparison.OrdinalIgnoreCase))
                    return $"Cannot use — '{stage.DisplayName}' is already taken.";
            }

            return null;
        }

        private static string ReadStageName(string dir, string fallback)
        {
            try
            {
                var info = Path.Combine(dir, "info.json");
                if (!File.Exists(info)) return fallback;
                var obj = JObject.Parse(File.ReadAllText(info));
                var name = obj["name"]?.ToString();
                return string.IsNullOrWhiteSpace(name) ? fallback : name!;
            }
            catch
            {
                return fallback;
            }
        }
    }
}
