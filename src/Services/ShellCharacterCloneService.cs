using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public class ShellRosterCharacter
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SwfPath { get; set; } = "";
        public string Label =>
            string.IsNullOrWhiteSpace(DisplayName) ? Id : $"{DisplayName}  ({Id})";
    }

    public class ShellCharacterCloneResult
    {
        public string ExpansionFolder { get; set; } = "";
        public string FighterSwfPath { get; set; } = "";
        public string? FlaPath { get; set; }
        public CharacterCloneResult? Clone { get; set; }
        public List<string> Notes { get; set; } = new();
    }

    public sealed class ImportCloneAssetChoice
    {
        public string Path { get; init; } = "";
        public string FileName { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string SourceId { get; init; } = "";
        public bool KeepOldAssets { get; set; }
    }

    /// <summary>
    /// Clones an in-game Shell fighter into <c>expansions/character/{id}</c>
    /// using JPEXS only for identifier rename + FLA export.
    /// </summary>
    public class ShellCharacterCloneService
    {
        public const string TemplateDonorId = "buu";

        private readonly CharacterCloneService _clone;
        private readonly JpexsCliService _jpexs;
        private readonly MappingsEditService _mappings;

        public ShellCharacterCloneService(
            CharacterCloneService? clone = null,
            JpexsCliService? jpexs = null,
            MappingsEditService? mappings = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _clone = clone ?? new CharacterCloneService(_jpexs);
            _mappings = mappings ?? new MappingsEditService(_jpexs);
        }

        public static List<ShellRosterCharacter> ListPlayableCharacters(string installRoot, JObject mappings)
        {
            var list = new List<ShellRosterCharacter>();
            if (string.IsNullOrWhiteSpace(installRoot) || mappings == null)
                return list;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in MappingsEditService.GetCssRows(mappings).SelectMany(r => r))
            {
                if (!TryAddRosterCharacter(installRoot, mappings, id, seen, list))
                    continue;
            }

            if (mappings["character"] is JObject cat)
            {
                foreach (var prop in cat.Properties())
                    TryAddRosterCharacter(installRoot, mappings, prop.Name, seen, list);
            }

            var charDir = ShellWorkspaceService.CharacterDirectory(installRoot);
            if (Directory.Exists(charDir))
            {
                foreach (var swf in Directory.GetFiles(charDir, "*.swf").Concat(Directory.GetFiles(charDir, "*.ssf")))
                {
                    var id = Path.GetFileNameWithoutExtension(swf);
                    if (!seen.Add(id)) continue;
                    if (MappingsEditService.IsPlaceholderId(id)) continue;
                    var name = mappings["character"]?[id]?["name"]?.ToString();
                    list.Add(new ShellRosterCharacter
                    {
                        Id = id,
                        DisplayName = string.IsNullOrWhiteSpace(name) ? id : name!,
                        SwfPath = swf
                    });
                }
            }

            return list
                .OrderBy(c => c.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(c => c.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool TryAddRosterCharacter(
            string installRoot,
            JObject mappings,
            string id,
            HashSet<string> seen,
            List<ShellRosterCharacter> list)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (MappingsEditService.IsPlaceholderId(id)) return false;
            if (id.Equals("random", StringComparison.OrdinalIgnoreCase)) return false;
            if (!seen.Add(id)) return false;

            var swf = MappingsEditService.ResolveEntrySwfPath(installRoot, mappings, "character", id);
            if (string.IsNullOrWhiteSpace(swf) || !File.Exists(swf))
                return false;

            var name = mappings["character"]?[id]?["name"]?.ToString();
            if (string.IsNullOrWhiteSpace(name)) name = id;
            list.Add(new ShellRosterCharacter
            {
                Id = id,
                DisplayName = name!,
                SwfPath = swf
            });
            return true;
        }

        public async Task<JObject> LoadMappingsAsync(string installRoot, CancellationToken cancellationToken = default) =>
            await _mappings.LoadDocumentAsync(installRoot, exportIfMissing: false, cancellationToken: cancellationToken);

        /// <summary>
        /// Folder name shown on disk (spaces kept). Real Expansions derives the
        /// fighter id with <c>folder.name.replace(/[^a-zA-Z0-9_]/g, "_")</c>.
        /// </summary>
        public static string ExpansionFolderName(string? displayName, string characterName)
        {
            var raw = !string.IsNullOrWhiteSpace(displayName) ? displayName : characterName;
            return CharacterCloneService.SanitizeExpansionFolderName(raw);
        }

        /// <summary>Real Expansions game id from an expansion folder name.</summary>
        public static string ExpansionGameId(string folderName)
        {
            var id = Regex.Replace(folderName ?? "", @"[^a-zA-Z0-9_]", "_");
            if (string.IsNullOrWhiteSpace(id) || id.All(c => c == '_'))
                throw new ArgumentException("Character name did not produce a usable id.");
            if (char.IsDigit(id[0])) id = "c" + id;
            return id;
        }

        /// <summary>Stock fighters live in data/character. Only those have a costumes block to copy.</summary>
        public static bool IsDataCharacterFile(string sourceSwf)
        {
            var dir = Path.GetDirectoryName(Path.GetFullPath(sourceSwf ?? ""));
            while (!string.IsNullOrEmpty(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (Path.GetFileName(dir).Equals("character", StringComparison.OrdinalIgnoreCase)
                    && parent != null
                    && Path.GetFileName(parent).Equals("data", StringComparison.OrdinalIgnoreCase))
                    return true;
                dir = parent;
            }
            return false;
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
                var folder = !string.IsNullOrWhiteSpace(linkageId)
                    ? CharacterCloneService.SanitizeExpansionFolderName(linkageId)
                    : ExpansionFolderName(displayName, linkageId);
                newId = ExpansionGameId(folder);
            }
            catch
            {
                return null;
            }

            foreach (var fighter in roster ?? Array.Empty<ShellRosterCharacter>())
            {
                if (string.IsNullOrWhiteSpace(fighter.Id)) continue;
                if (!string.IsNullOrWhiteSpace(ignoreFolderId)
                    && fighter.Id.Equals(ignoreFolderId, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (fighter.Id.Equals(newId, StringComparison.OrdinalIgnoreCase))
                    return $"Cannot use — '{newId}' is already taken by {fighter.DisplayName}.";
                if (!string.IsNullOrWhiteSpace(displayName)
                    && displayName.Equals(fighter.DisplayName, StringComparison.OrdinalIgnoreCase))
                    return $"Cannot use — '{fighter.DisplayName}' is already taken.";
            }

            return null;
        }

        public async Task<ShellCharacterCloneResult> CloneIntoShellAsync(
            string installRoot,
            string sourceId,
            string characterName,
            string displayName,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? folderOverride = null,
            bool skipFla = false,
            bool keepOldAssets = false)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");

            var folderName = !string.IsNullOrWhiteSpace(folderOverride)
                ? CharacterCloneService.SanitizeExpansionFolderName(folderOverride)
                : ExpansionFolderName(displayName, characterName);
            var newId = ExpansionGameId(folderName);
            var shown = string.IsNullOrWhiteSpace(displayName) ? folderName : displayName.Trim();
            sourceId = (sourceId ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourceId))
                throw new ArgumentException("Pick a character already in the game to clone.", nameof(sourceId));
            if (sourceId.Equals(newId, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("New character name must differ from the source character.");

            progress?.Report("Loading roster…");
            var mappings = await LoadMappingsAsync(installRoot, cancellationToken);
            var source = ListPlayableCharacters(installRoot, mappings)
                .FirstOrDefault(c => c.Id.Equals(sourceId, StringComparison.OrdinalIgnoreCase))
                ?? throw new FileNotFoundException(
                    $"Source character '{sourceId}' is not on disk in this Shell build.");

            var expansions = ShellWorkspaceService.ExpansionsDirectory(installRoot);
            Directory.CreateDirectory(expansions);
            var destFolder = Path.Combine(expansions, folderName);
            if (Directory.Exists(destFolder))
                TryDeleteDirectory(destFolder);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-shell-clone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Renaming {source.Id} → {newId} with JPEXS…");
                var clone = await _clone.CloneAsync(new CharacterCloneRequest
                {
                    SourcePath = source.SwfPath,
                    OldName = source.Id,
                    NewName = newId,
                    OutputDirectory = work,
                    JpexsPath = jpexsPath,
                    DisplayName = shown,
                    TakenNames = CollectTakenSymbolNames(installRoot)
                }, progress, cancellationToken);

                progress?.Report("Copying expansion template…");
                var folder = InstallExpansionFromTemplate(
                    ShellWorkspaceService.TemplateDirectory(installRoot),
                    destFolder,
                    newId,
                    shown,
                    clone.OutputSwfPath,
                    keepOldAssets ? sourceId : null);
                var fighter = Path.Combine(folder, newId + ".swf");
                // #region agent log
                DebugAgentLog.Write("B", "ShellCharacterCloneService.cs:before-fla", "Fighter SWF before FLA export", DebugAgentLog.ScanSwf(fighter));
                var fighterWriteBeforeFla = File.Exists(fighter) ? File.GetLastWriteTimeUtc(fighter) : DateTime.MinValue;
                // #endregion

                var result = new ShellCharacterCloneResult
                {
                    ExpansionFolder = folder,
                    FighterSwfPath = fighter,
                    Clone = clone
                };
                result.Notes.AddRange(clone.Notes);
                result.Notes.Add($"Expansion folder: {folder}");
                if (keepOldAssets)
                    result.Notes.Add($"Keeping {sourceId}'s stock, costumes, and Classic / All-Star art.");

                progress?.Report("Writing costumes.json…");
                await LivePackRuntimeService.WriteLiveCostumesJsonAsync(
                    installRoot, sourceId, newId, folder, jpexsPath, progress, cancellationToken);

                if (!skipFla)
                {
                    var flaDir = Path.Combine(folder, "FLA");
                    Directory.CreateDirectory(flaDir);
                    var flaSw = Stopwatch.StartNew();
                    progress?.Report($"Exporting FLA ({newId}.fla)…");
                    await _jpexs.ExportFlaAsync(
                        jpexsPath ?? "", fighter, flaDir, "cs6", cancellationToken,
                        outputBaseName: newId, progress: progress, characterPretty: true);
                    var fla = Path.Combine(flaDir, newId + ".fla");
                    if (!File.Exists(fla) && !Directory.Exists(fla))
                        fla = Path.Combine(flaDir, "FLA1", newId + ".fla");
                    if (!File.Exists(fla) && !Directory.Exists(fla))
                    {
                        throw new InvalidOperationException(
                            $"FLA export did not produce {newId}.fla under {flaDir}.");
                    }
                    result.FlaPath = fla;
                    result.Notes.Add($"FLA: {fla} ({flaSw.Elapsed.TotalSeconds:0.0}s)");
                    DebugAgentLog.Write("A", "ShellCharacterCloneService.cs:after-fla", "Fighter SWF after FLA export", new
                    {
                        swf = DebugAgentLog.ScanSwf(fighter),
                        swfWriteChanged = File.Exists(fighter) && File.GetLastWriteTimeUtc(fighter) != fighterWriteBeforeFla,
                        fla = DebugAgentLog.ScanFlaAndLeftovers(fla, flaDir)
                    });
                    progress?.Report($"FLA export done ({flaSw.Elapsed.TotalSeconds:0.0}s)…");
                }

                progress?.Report("Done.");
                try { CreditsService.Update(installRoot); } catch { }
                return result;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
            }
        }

        public static string? CloneInsteadMessage(IEnumerable<ShellRosterCharacter> roster, string linkage)
        {
            var hit = roster.FirstOrDefault(c => c.Id.Equals(linkage, StringComparison.OrdinalIgnoreCase));
            if (hit == null)
                return null;
            var shown = string.IsNullOrWhiteSpace(hit.DisplayName) ? hit.Id : hit.DisplayName;
            return $"This file uses the linkage \"{linkage}\", which is already in the game ({shown}). Clone that character instead of importing this file.";
        }

        /// <summary>
        /// Paths chosen in the character import dialog, in that order. A blank path is dropped. The same path twice is kept once.
        /// </summary>
        public static List<string> SelectedImportPaths(IEnumerable<string>? picked)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var raw in picked ?? Array.Empty<string>())
            {
                var path = (raw ?? "").Trim();
                if (path.Length == 0)
                    continue;
                if (!seen.Add(path))
                    continue;
                list.Add(path);
            }
            return list;
        }

        /// <summary>
        /// Next free game id. <c>donkeykong</c> taken becomes <c>donkeykong2</c>, then <c>donkeykong3</c>.
        /// </summary>
        public static string NextFreeLinkage(IEnumerable<string> taken, string linkage)
        {
            linkage = (linkage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(linkage))
                throw new ArgumentException("Linkage is required.", nameof(linkage));
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in taken ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    used.Add(id.Trim());
            }
            if (!used.Contains(linkage))
                return linkage;
            for (var n = 2; n < 10000; n++)
            {
                var candidate = linkage + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!used.Contains(candidate))
                    return candidate;
            }
            throw new InvalidOperationException($"No free linkage for '{linkage}'.");
        }

        /// <summary>
        /// The playable id whose art a file is a clone of. A free linkage that is not
        /// a numbered copy of a playable id is not a clone.
        /// </summary>
        public static string? CloneAssetSource(string? linkage, IEnumerable<string>? taken)
        {
            linkage = (linkage ?? "").Trim();
            if (string.IsNullOrWhiteSpace(linkage))
                return null;
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var id in taken ?? Array.Empty<string>())
            {
                if (!string.IsNullOrWhiteSpace(id))
                    used.Add(id.Trim());
            }
            if (used.Contains(linkage))
                return used.First(id => id.Equals(linkage, StringComparison.OrdinalIgnoreCase));
            var match = Regex.Match(linkage, @"^(.*?)(\d+)$");
            if (!match.Success)
                return null;
            var baseId = match.Groups[1].Value;
            if (string.IsNullOrEmpty(baseId))
                return null;
            if (!int.TryParse(match.Groups[2].Value, out var n) || n < 2)
                return null;
            foreach (var id in used)
            {
                if (id.Equals(baseId, StringComparison.OrdinalIgnoreCase))
                    return id;
            }
            return null;
        }

        public static List<ImportCloneAssetChoice> CloneAssetChoices(
            IEnumerable<ImportCloneAssetChoice> files,
            IEnumerable<string> taken)
        {
            var list = new List<ImportCloneAssetChoice>();
            foreach (var file in files ?? Array.Empty<ImportCloneAssetChoice>())
            {
                var source = CloneAssetSource(file.SourceId, taken);
                if (string.IsNullOrWhiteSpace(source))
                    continue;
                list.Add(new ImportCloneAssetChoice
                {
                    Path = file.Path,
                    FileName = string.IsNullOrWhiteSpace(file.FileName) ? Path.GetFileName(file.Path) : file.FileName,
                    DisplayName = string.IsNullOrWhiteSpace(file.DisplayName) ? source : file.DisplayName,
                    SourceId = source
                });
            }
            return list;
        }

        public static void ApplyOldAssets(JObject meta, string? oldAssetId)
        {
            if (meta == null || string.IsNullOrWhiteSpace(oldAssetId))
                return;
            meta["oldassets"] = true;
            meta["oldassetid"] = oldAssetId.Trim();
        }

        public static List<string> TakenLinkageIds(string installRoot, JObject mappings)
        {
            var ids = new List<string>();
            foreach (var fighter in ListPlayableCharacters(installRoot, mappings))
            {
                if (!string.IsNullOrWhiteSpace(fighter.Id))
                    ids.Add(fighter.Id);
            }
            var expansions = ShellWorkspaceService.ExpansionsDirectory(installRoot);
            if (!Directory.Exists(expansions))
                return ids;
            foreach (var dir in Directory.GetDirectories(expansions))
            {
                var name = Path.GetFileName(dir);
                if (name.Equals("template", StringComparison.OrdinalIgnoreCase))
                    continue;
                try
                {
                    ids.Add(ExpansionGameId(name));
                }
                catch (ArgumentException)
                {
                }
            }
            return ids;
        }

        /// <summary>
        /// Symbol-class names exported by stock characters and other expansions.
        /// A clone renames a class in the new file when that exact name is already here.
        /// </summary>
        public static HashSet<string> CollectTakenSymbolNames(string installRoot)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                return names;

            AddSymbolFiles(names, ShellWorkspaceService.CharacterDirectory(installRoot));
            var expansions = ShellWorkspaceService.CharacterExpansionsDirectory(installRoot);
            if (!Directory.Exists(expansions))
                return names;

            foreach (var dir in Directory.GetDirectories(expansions))
            {
                if (Path.GetFileName(dir).Equals("template", StringComparison.OrdinalIgnoreCase))
                    continue;
                AddSymbolFiles(names, dir);
            }
            return names;
        }

        private static void AddSymbolFiles(HashSet<string> names, string directory)
        {
            if (!Directory.Exists(directory))
                return;
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                var ext = Path.GetExtension(path);
                if (!ext.Equals(".swf", StringComparison.OrdinalIgnoreCase)
                    && !ext.Equals(".ssf", StringComparison.OrdinalIgnoreCase))
                    continue;
                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(path);
                }
                catch (IOException)
                {
                    continue;
                }
                foreach (var name in SwfPackage.SymbolClassNames(bytes))
                {
                    if (string.IsNullOrEmpty(name))
                        continue;
                    names.Add(name);
                    var cut = Math.Max(name.LastIndexOf('.'), name.LastIndexOf(':'));
                    if (cut >= 0 && cut + 1 < name.Length)
                        names.Add(name[(cut + 1)..]);
                }
            }
        }

        public async Task<ShellCharacterCloneResult> ImportIntoShellAsync(
            string installRoot,
            string sourceSwf,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool skipFla = false,
            bool keepOldAssets = false)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || !Directory.Exists(installRoot))
                throw new DirectoryNotFoundException("SSF2 Shell build folder was not found.");
            if (string.IsNullOrWhiteSpace(sourceSwf) || !File.Exists(sourceSwf))
                throw new FileNotFoundException("Character file was not found.", sourceSwf);

            progress?.Report("Reading character…");
            var swfBytes = SwfPackage.ToSwf(await File.ReadAllBytesAsync(sourceSwf, cancellationToken));
            if (SwfPackage.IsStage(swfBytes))
                throw new InvalidDataException("That file is a stage. Use Import a stage.");
            var (newId, shown) = CharacterCloneService.ReadImportIdentity(swfBytes);
            if (!CharacterCloneService.IsPlainLinkage(newId) || string.IsNullOrWhiteSpace(shown))
                throw new InvalidDataException("Could not read a character linkage from that file.");

            var mappings = await LoadMappingsAsync(installRoot, cancellationToken);
            var sourceLinkage = newId!;
            var taken = TakenLinkageIds(installRoot, mappings);
            var assetSource = CloneAssetSource(sourceLinkage, taken);
            var assigned = NextFreeLinkage(taken, sourceLinkage);

            var expansions = ShellWorkspaceService.ExpansionsDirectory(installRoot);
            Directory.CreateDirectory(expansions);
            var destFolder = Path.Combine(expansions, assigned);
            if (Directory.Exists(destFolder))
                TryDeleteDirectory(destFolder);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-char-import-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var prepared = Path.Combine(work, "prepared.swf");
                await File.WriteAllBytesAsync(prepared, swfBytes, cancellationToken);
                var produced = prepared;
                if (!assigned.Equals(sourceLinkage, StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report($"Linkage {sourceLinkage} is taken. Importing as {assigned}…");
                    var clone = await _clone.CloneAsync(new CharacterCloneRequest
                    {
                        SourcePath = prepared,
                        OldName = sourceLinkage,
                        NewName = assigned,
                        OutputDirectory = work,
                        DisplayName = shown,
                        TakenNames = CollectTakenSymbolNames(installRoot)
                    }, progress, cancellationToken);
                    produced = clone.OutputSwfPath;
                }
                else
                {
                    progress?.Report("Copying character…");
                }
                newId = assigned;

                progress?.Report("Copying expansion template…");
                var useOld = keepOldAssets && !string.IsNullOrWhiteSpace(assetSource);
                var folder = InstallExpansionFromTemplate(
                    ShellWorkspaceService.TemplateDirectory(installRoot),
                    destFolder,
                    newId!,
                    shown!,
                    produced,
                    useOld ? assetSource : null);
                var fighter = Path.Combine(folder, newId + ".swf");
                var result = new ShellCharacterCloneResult
                {
                    ExpansionFolder = folder,
                    FighterSwfPath = fighter
                };
                result.Notes.Add($"Expansion folder: {folder}");
                if (!assigned.Equals(sourceLinkage, StringComparison.OrdinalIgnoreCase))
                    result.Notes.Add($"Linkage {sourceLinkage} was taken. Imported as {assigned}. Display name stays \"{shown}\".");
                if (useOld)
                    result.Notes.Add($"Keeping {assetSource}'s stock, costumes, and Classic / All-Star art.");

                if (IsDataCharacterFile(sourceSwf))
                {
                    progress?.Report("Writing costumes.json…");
                    await LivePackRuntimeService.WriteLiveCostumesJsonAsync(
                        installRoot, sourceLinkage, newId!, folder, null, progress, cancellationToken);
                }

                if (!skipFla)
                {
                    var flaDir = Path.Combine(folder, "FLA");
                    Directory.CreateDirectory(flaDir);
                    progress?.Report($"Exporting FLA ({newId}.fla)…");
                    await _jpexs.ExportFlaAsync(
                        "", fighter, flaDir, "cs6", cancellationToken,
                        outputBaseName: newId, progress: progress, characterPretty: true);
                    var fla = Path.Combine(flaDir, newId + ".fla");
                    if (!File.Exists(fla) && !Directory.Exists(fla))
                        fla = Path.Combine(flaDir, "FLA1", newId + ".fla");
                    if (!File.Exists(fla) && !Directory.Exists(fla))
                    {
                        throw new InvalidOperationException(
                            $"FLA export did not produce {newId}.fla under {flaDir}.");
                    }
                    result.FlaPath = fla;
                }
                progress?.Report("Done.");
                try { CreditsService.Update(installRoot); } catch { }
                return result;
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { }
            }
        }

        /// <summary>
        /// Copies the Shell expansion template, swaps in the cloned fighter, and
        /// renames <c>assets_buu</c> files to the new character id.
        /// </summary>
        public static string InstallExpansionFromTemplate(
            string? templateDir,
            string destFolder,
            string characterId,
            string displayName,
            string clonedSwfPath,
            string? oldAssetId = null)
        {
            if (string.IsNullOrWhiteSpace(destFolder))
                throw new ArgumentException("Destination folder is required.", nameof(destFolder));
            if (string.IsNullOrWhiteSpace(clonedSwfPath) || !File.Exists(clonedSwfPath))
                throw new FileNotFoundException("Cloned character SWF not found.", clonedSwfPath);

            var id = ExpansionGameId(characterId);
            var shown = string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim();

            if (Directory.Exists(destFolder))
                TryDeleteDirectory(destFolder);

            if (!string.IsNullOrWhiteSpace(templateDir) && Directory.Exists(templateDir))
                CopyDirectory(templateDir, destFolder);
            else
                Directory.CreateDirectory(destFolder);

            foreach (var swf in Directory.GetFiles(destFolder, "*.swf"))
            {
                var stem = Path.GetFileNameWithoutExtension(swf);
                if (stem.Equals("assets", StringComparison.OrdinalIgnoreCase)
                    || stem.StartsWith("assets_", StringComparison.OrdinalIgnoreCase))
                    continue;
                File.Delete(swf);
            }

            var destSwf = Path.Combine(destFolder, id + ".swf");
            File.Copy(clonedSwfPath, destSwf, overwrite: true);

            RenameTemplateAssets(destFolder, TemplateDonorId, id);

            var namedAssets = Path.Combine(destFolder, "assets_" + id + ".swf");
            var genericAssets = Path.Combine(destFolder, "assets.swf");
            if (File.Exists(namedAssets) && !File.Exists(genericAssets))
                File.Copy(namedAssets, genericAssets);
            if (!File.Exists(genericAssets))
            {
                var expansionsRoot = Path.GetDirectoryName(destFolder);
                foreach (var donor in new[] { "template", "buu", "hero" })
                {
                    if (string.IsNullOrEmpty(expansionsRoot)) break;
                    var donorAssets = Path.Combine(expansionsRoot, donor, "assets.swf");
                    if (!File.Exists(donorAssets)) continue;
                    File.Copy(donorAssets, genericAssets);
                    break;
                }
            }

            var metaPath = Path.Combine(destFolder, "css_meta.json");
            var meta = new JObject
            {
                ["displayName"] = shown,
                ["id"] = id
            };
            if (File.Exists(metaPath))
            {
                try
                {
                    var existing = JObject.Parse(File.ReadAllText(metaPath));
                    existing["displayName"] = shown;
                    existing["id"] = id;
                    meta = existing;
                }
                catch { /* rewrite below */ }
            }
            ApplyOldAssets(meta, oldAssetId);
            File.WriteAllText(metaPath, meta.ToString(Formatting.Indented));

            Directory.CreateDirectory(Path.Combine(destFolder, "FLA"));
            return destFolder;
        }

        public static void RenameTemplateAssets(string folder, string oldId, string newId)
        {
            if (!Directory.Exists(folder) || string.IsNullOrWhiteSpace(oldId) || string.IsNullOrWhiteSpace(newId))
                return;
            if (oldId.Equals(newId, StringComparison.OrdinalIgnoreCase))
                return;

            var prefix = "assets_" + oldId;
            foreach (var file in Directory.GetFiles(folder))
            {
                var name = Path.GetFileName(file);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rest = name[prefix.Length..];
                var dest = Path.Combine(folder, "assets_" + newId + rest);
                if (File.Exists(dest)) File.Delete(dest);
                File.Move(file, dest);
            }

            foreach (var dir in Directory.GetDirectories(folder))
            {
                var name = Path.GetFileName(dir);
                if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    continue;
                var rest = name[prefix.Length..];
                var dest = Path.Combine(Path.GetDirectoryName(dir)!, "assets_" + newId + rest);
                if (Directory.Exists(dest)) Directory.Delete(dest, recursive: true);
                Directory.Move(dir, dest);
            }
        }

        public static void CopyDirectory(string source, string dest)
        {
            Directory.CreateDirectory(dest);
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(dest, Path.GetFileName(file)), overwrite: true);
            foreach (var dir in Directory.GetDirectories(source))
                CopyDirectory(dir, Path.Combine(dest, Path.GetFileName(dir)));
        }

        public static void TryDeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
                return;
            foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
            {
                try
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                    File.Delete(file);
                }
                catch { /* locked */ }
            }

            foreach (var dir in Directory.EnumerateDirectories(path, "*", SearchOption.AllDirectories)
                         .OrderByDescending(d => d.Length))
            {
                try { Directory.Delete(dir, recursive: false); } catch { /* locked */ }
            }

            try { Directory.Delete(path, recursive: true); } catch { /* locked */ }
        }
    }
}
