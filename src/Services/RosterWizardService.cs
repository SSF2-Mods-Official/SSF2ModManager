using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public enum CharacterWizardMode
    {
        Clone,
        New,
        Edit
    }

    public class CharacterWizardRequest
    {
        public CharacterWizardMode Mode { get; set; } = CharacterWizardMode.Clone;
        public string SourceSwfPath { get; set; } = "";
        public string Id { get; set; } = "";
        /// <summary>Existing install id when Mode=Clone (donor fighter) or Mode=Edit.</summary>
        public string DonorId { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string SeriesIcon { get; set; } = "smash_symbol";
        /// <summary>When true, clone <see cref="SeriesSymbolTemplate"/> into <see cref="SeriesIcon"/>.</summary>
        public bool CreateNewSeriesSymbol { get; set; }
        public string SeriesSymbolTemplate { get; set; } = "smash_symbol";
        public string? SeriesSymbolPng { get; set; }
        public string? StockPng { get; set; }
        public string? PaPng { get; set; }
        public string? CharSelectPng { get; set; }
        public string? CssChipPng { get; set; }
        /// <summary>Slot key → flattened PNG path (select, pa, stock, congrats, …).</summary>
        public Dictionary<string, string> SlotPngs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
        public string DonorChipId { get; set; } = "sandbag";
        public int CssRowIndex { get; set; } = -1; // -1 = last row
        public bool NewCssRow { get; set; }
        public bool AddToRandom { get; set; } = true;
        public bool CloneIfExists { get; set; } = true;
        /// <summary>Write <c>data/character/{DisplayName}/</c> package (SWF, assets, FLA, costumes).</summary>
        public bool WritePackageFolder { get; set; } = true;
        public bool ExportFla { get; set; } = true;
    }

    public class CharacterUpdateItem
    {
        public string PackageFolder { get; set; } = "";
        public bool UpdateAssets { get; set; } = true;
        public bool UpdateSwf { get; set; } = true;
    }

    public class StageWizardRequest
    {
        public string SourceSwfPath { get; set; } = "";
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string? StageSelectPng { get; set; }
        public string DonorChipId { get; set; } = "battlefield";
        public int SssRowIndex { get; set; } = -1;
        public bool NewSssRow { get; set; }
        public bool TrainingOnly { get; set; }
        public bool AddToRandom { get; set; } = true;
        public bool CloneIfExists { get; set; } = true;
    }

    public class WizardResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string ContentId { get; set; } = "";
        public string? Warning { get; set; }
    }

    /// <summary>
    /// Guided character/stage registration: clone if needed, menu chips, CSS/SSS insert, Commit.
    /// </summary>
    public class RosterWizardService
    {
        public const string DefaultNewUiDonorId = "mario";

        private readonly JpexsCliService _jpexs;
        private readonly MappingsEditService _mappings;
        private readonly MenuSlotService _menuSlots;
        private readonly CharacterCloneService _clone;
        private readonly CharacterAssetService _assets;
        private readonly CharacterPackageService _packages;

        public RosterWizardService(
            JpexsCliService? jpexs = null,
            MappingsEditService? mappings = null,
            MenuSlotService? menuSlots = null,
            CharacterCloneService? clone = null,
            CharacterAssetService? assets = null,
            CharacterPackageService? packages = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _mappings = mappings ?? new MappingsEditService(_jpexs);
            _menuSlots = menuSlots ?? new MenuSlotService(_jpexs);
            _clone = clone ?? new CharacterCloneService(_jpexs);
            _assets = assets ?? new CharacterAssetService(_jpexs, _menuSlots);
            _packages = packages ?? new CharacterPackageService(_jpexs, _assets, _clone);
        }

        /// <summary>
        /// Unified Clone / New / Edit character wizard commit.
        /// </summary>
        public async Task<WizardResult> RunCharacterWizardAsync(
            string installRoot,
            JObject doc,
            CharacterWizardRequest req,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            WizardStepProgress? steps = null)
        {
            var id = MenuSlotService.SanitizeId(req.Id);
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("Character id is required.");

            // Content-pack path: one-time slot pool + pack files only (no host inject on add).
            if (SlotPoolService.IsReady(installRoot) || GamePrepareService.IsPrepared(installRoot))
            {
                await EnsurePackRuntimeAsync(installRoot, jpexsPath, progress, cancellationToken);
                if (SlotPoolService.IsReady(installRoot))
                    return await RunCharacterWizardViaPackAsync(
                        installRoot, doc, req, jpexsPath, progress, cancellationToken, steps);
            }

            var displayName = string.IsNullOrWhiteSpace(req.DisplayName) ? id : req.DisplayName.Trim();
            var charDir = CharacterPackageService.CharacterDataDir(installRoot);
            Directory.CreateDirectory(charDir);

            // Live SWF lives in the package folder when packaging is enabled.
            var packageFolder = CharacterPackageService.PackageFolderPath(installRoot, displayName);
            Directory.CreateDirectory(packageFolder);
            var destSwf = req.WritePackageFolder
                ? Path.Combine(packageFolder, id + ".swf")
                : Path.Combine(charDir, id + ".swf");
            var mappingsSwfRel = req.WritePackageFolder
                ? CharacterPackageService.MappingsRelativeSwfPath(displayName, id)
                : id + ".swf";

            var donorId = MenuSlotService.SanitizeId(
                string.IsNullOrWhiteSpace(req.DonorId) ? req.DonorChipId : req.DonorId);
            if (string.IsNullOrWhiteSpace(donorId))
                donorId = DefaultNewUiDonorId;

            string? resourceGuid = null;
            string? warn = null;

            var seriesIcon = string.IsNullOrWhiteSpace(req.SeriesIcon) ? "smash_symbol" : req.SeriesIcon.Trim();
            if (req.CreateNewSeriesSymbol && string.IsNullOrWhiteSpace(req.SeriesIcon))
                seriesIcon = id + "_symbol";
            var arts = new Dictionary<string, string>(req.SlotPngs, StringComparer.OrdinalIgnoreCase);
            void Put(string key, string? path)
            {
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path) && !arts.ContainsKey(key))
                    arts[key] = path!;
            }
            Put("select", req.CssChipPng);
            Put("pa", req.PaPng ?? req.CharSelectPng);
            Put("charselect", req.CharSelectPng ?? req.PaPng);
            Put("stock", req.StockPng);
            CharacterAssetService.FanOutSharedPaSlots(arts);

            async Task EnsureUiAsync()
            {
                progress?.Report($"Ensuring UI slots from donor '{donorId}'…");
                try
                {
                    await _assets.EnsureUiSlotsFromDonorAsync(
                        installRoot, id, donorId, jpexsPath, progress, cancellationToken,
                        displayName: displayName,
                        seriesIcon: seriesIcon,
                        createNewSeriesSymbol: req.CreateNewSeriesSymbol,
                        seriesSymbolTemplate: string.IsNullOrWhiteSpace(req.SeriesSymbolTemplate)
                            ? "smash_symbol"
                            : req.SeriesSymbolTemplate,
                        seriesSymbolPng: req.SeriesSymbolPng,
                        slotPngs: arts.Count > 0 ? arts : null);
                    if (req.CreateNewSeriesSymbol)
                        seriesIcon = string.IsNullOrWhiteSpace(req.SeriesIcon) ? id + "_symbol" : req.SeriesIcon.Trim();
                }
                catch (Exception ex) when (
                    ex.Message.Contains("select script", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("_select", StringComparison.OrdinalIgnoreCase)
                    || ex.Message.Contains("UI slot clone failed", StringComparison.OrdinalIgnoreCase))
                {
                    // CSS chip script failures must abort — otherwise the character is invisible on CSS.
                    throw;
                }
                catch (Exception ex)
                {
                    progress?.Report("UI slot ensure warning: " + ex.Message);
                }
            }

            void UpsertMappingsDoc()
            {
                if (req.Mode != CharacterWizardMode.Edit)
                {
                    MappingsEditService.UpsertEntry(
                        doc, "character", id, mappingsSwfRel,
                        name: displayName,
                        seriesIcon: seriesIcon,
                        guid: resourceGuid);

                    var rows = MappingsEditService.GetCssRows(doc);
                    var alreadyOnCss = rows.Any(r => r.Any(c => c.Equals(id, StringComparison.OrdinalIgnoreCase)));
                    if (!alreadyOnCss)
                    {
                        if (req.NewCssRow || rows.Count == 0)
                            rows.Add(new List<string> { id });
                        else
                        {
                            var row = req.CssRowIndex < 0 ? rows.Count - 1 : Math.Clamp(req.CssRowIndex, 0, rows.Count - 1);
                            MappingsEditService.AppendToRow(rows, row, id);
                        }
                        MappingsEditService.SetCssRows(doc, rows);
                        warn = MappingsEditService.FitSelectLayout(doc, isCss: true, rows);
                    }

                    if (req.AddToRandom)
                        MappingsEditService.AddToRandomPool(doc, "random_characters", "characters", id);
                }
                else if (!string.IsNullOrWhiteSpace(req.DisplayName) && doc["character"]?[id] is JObject entry)
                {
                    entry["name"] = displayName;
                    entry["file"] = mappingsSwfRel;
                }
            }

            async Task CommitMappingsAsync()
            {
                progress?.Report("Saving and applying to game…");
                await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            }

            async Task WritePackageAsync()
            {
                if (!req.WritePackageFolder || !File.Exists(destSwf)) return;
                progress?.Report("Writing character package folder…");
                await _packages.CreateOrUpdatePackageAsync(
                    installRoot, id, displayName, donorId, seriesIcon, destSwf, arts,
                    req.SeriesSymbolPng, jpexsPath, progress, cancellationToken, req.ExportFla);
            }

            string ResolveDonorSwf()
            {
                // Prefer package folder, then flat {id}.swf
                var pkg = CharacterPackageService.ScanPackages(installRoot)
                    .FirstOrDefault(p => p.Manifest.Id.Equals(donorId, StringComparison.OrdinalIgnoreCase));
                if (pkg != null && pkg.SwfExists) return pkg.SwfPath;
                var flat = Path.Combine(charDir, donorId + ".swf");
                return flat;
            }

            if (req.Mode == CharacterWizardMode.Edit)
            {
                if (!File.Exists(destSwf))
                {
                    var flat = Path.Combine(charDir, id + ".swf");
                    if (File.Exists(flat)) destSwf = flat;
                    else throw new FileNotFoundException("Character SWF not found for edit.", destSwf);
                }
                resourceGuid = doc["character"]?[id]?["guid"]?.ToString();
                progress?.Report($"Editing existing character '{id}'…");
                await EnsureUiAsync();
                await WritePackageAsync();
                UpsertMappingsDoc();
                await CommitMappingsAsync();
            }
            else if (req.Mode == CharacterWizardMode.New)
            {
                donorId = DefaultNewUiDonorId;
                req.DonorChipId = donorId;
                resourceGuid = MappingsEditService.AllocateUniqueGuid(doc);

                var marioSwf = ResolveDonorSwf();
                var source = !string.IsNullOrWhiteSpace(req.SourceSwfPath) && File.Exists(req.SourceSwfPath)
                    ? req.SourceSwfPath
                    : marioSwf;
                if (!File.Exists(source))
                    throw new FileNotFoundException(
                        "New mode needs Mario's character SWF (or another source SWF) in the install.", source);

                progress?.Report($"Creating new character '{id}' from {Path.GetFileName(source)}…");
                await ClonePackToDestAsync(
                    source, Path.GetFileNameWithoutExtension(source), id, destSwf,
                    displayName, resourceGuid, jpexsPath, progress, cancellationToken);
                await EnsureUiAsync();
                await WritePackageAsync();
                UpsertMappingsDoc();
                await CommitMappingsAsync();
            }
            else // Clone
            {
                var source = req.SourceSwfPath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                {
                    source = ResolveDonorSwf();
                    if (!File.Exists(source))
                        throw new FileNotFoundException("Clone source SWF not found.", source);
                }

                // SWF rewrite name comes from the source file stem when a custom pack is browsed.
                // DonorId is UI-art donor only (chips / PA / stock), not the rewrite source.
                var sourceStem = MenuSlotService.SanitizeId(Path.GetFileNameWithoutExtension(source));
                var customSource = !string.IsNullOrWhiteSpace(req.SourceSwfPath)
                                   && File.Exists(req.SourceSwfPath);
                var oldName = customSource ? sourceStem : donorId;
                if (string.IsNullOrWhiteSpace(oldName))
                    oldName = sourceStem;
                req.DonorChipId = donorId;

                // Same-id import (e.g. knuckles.ssf → id knuckles): copy + GUID patch, no rename clone.
                var sameIdImport = oldName.Equals(id, StringComparison.OrdinalIgnoreCase);
                var existingGuid = doc["character"]?[id]?["guid"]?.ToString()?.Trim();
                resourceGuid = !string.IsNullOrWhiteSpace(existingGuid)
                    ? existingGuid!
                    : MappingsEditService.AllocateUniqueGuid(doc);

                if (sameIdImport)
                {
                    steps?.BeginStep(0, $"Importing unique pack '{id}'…");
                    progress?.Report($"Same-id import '{id}' (copy + GUID patch, no rename)…");
                    Directory.CreateDirectory(Path.GetDirectoryName(destSwf)!);
                    CharacterPackageService.CopyFileWithRetry(source, destSwf);
                    await _clone.PatchGuidInSwfAsync(
                        destSwf, resourceGuid, jpexsPath, progress, cancellationToken);
                    steps?.CompleteStep();
                }
                else
                {
                    steps?.BeginStep(0, $"Cloning '{oldName}' → '{id}'…");
                    progress?.Report($"Cloning '{oldName}' → '{id}'…");
                    await ClonePackToDestAsync(
                        source, oldName, id, destSwf,
                        displayName, resourceGuid, jpexsPath, progress, cancellationToken);
                    steps?.CompleteStep();
                }

                steps?.BeginStep(1, "Installing UI art + scripts…");
                await EnsureUiAsync();
                steps?.CompleteStep();

                steps?.BeginStep(2, "Writing package folder + FLA…");
                await WritePackageAsync();
                steps?.CompleteStep();

                steps?.BeginStep(3, "Saving mappings…");
                UpsertMappingsDoc();
                await CommitMappingsAsync();
                steps?.CompleteStep();
            }

            var verb = req.Mode switch
            {
                CharacterWizardMode.Edit => "updated",
                CharacterWizardMode.New => "created",
                _ => "cloned"
            };
            return new WizardResult
            {
                Success = true,
                ContentId = id,
                Message = $"Character '{id}' {verb} (UI art + mappings + package).",
                Warning = warn
            };
        }

        /// <summary>
        /// Apply package-folder assets and/or SWF back into the install for selected characters.
        /// </summary>
        public async Task<WizardResult> UpdateCharactersAsync(
            string installRoot,
            JObject doc,
            IReadOnlyList<CharacterUpdateItem> items,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (items == null || items.Count == 0)
                throw new ArgumentException("No characters selected to update.");

            var updated = 0;
            foreach (var item in items)
            {
                var manifest = CharacterPackageService.TryReadManifest(item.PackageFolder);
                if (manifest == null || string.IsNullOrWhiteSpace(manifest.Id))
                {
                    progress?.Report("Skip (no character.json): " + item.PackageFolder);
                    continue;
                }

                var id = MenuSlotService.SanitizeId(manifest.Id);
                var pkg = new CharacterPackageInfo
                {
                    FolderPath = item.PackageFolder,
                    FolderName = Path.GetFileName(item.PackageFolder),
                    Manifest = manifest
                };
                progress?.Report($"Updating '{manifest.DisplayName}' ({id})…");

                if (item.UpdateSwf)
                {
                    if (!pkg.SwfExists)
                        throw new FileNotFoundException("Package SWF missing.", pkg.SwfPath);
                    var dest = pkg.SwfPath; // already the live path
                    progress?.Report($"SWF ready at {CharacterPackageService.MappingsRelativeSwfPath(manifest.DisplayName, id)}");
                    // Ensure mappings point at package path.
                    var rel = CharacterPackageService.MappingsRelativeSwfPath(manifest.DisplayName, id);
                    if (doc["character"]?[id] is JObject entry)
                        entry["file"] = rel;
                    else
                    {
                        MappingsEditService.UpsertEntry(
                            doc, "character", id, rel,
                            name: manifest.DisplayName,
                            seriesIcon: manifest.SeriesIcon);
                    }
                    _ = dest;
                }

                if (item.UpdateAssets)
                {
                    var arts = CharacterPackageService.LoadAssetsFromFolder(item.PackageFolder);
                    CharacterAssetService.FanOutSharedPaSlots(arts);
                    // Drop non-host keys
                    arts.Remove("symbol");
                    arts.Remove("pa"); // fan-out already filled cssp/charselect/…
                    if (arts.Count > 0)
                    {
                        progress?.Report($"Applying {arts.Count} asset(s)…");
                        await _assets.ApplySlotArtsAsync(
                            installRoot, id, arts, jpexsPath, progress, cancellationToken);
                    }
                    var symbol = Path.Combine(item.PackageFolder, "assets", "symbol.png");
                    if (!File.Exists(symbol))
                        symbol = Path.Combine(item.PackageFolder, "symbol.png"); // legacy
                    if (File.Exists(symbol) && !string.IsNullOrWhiteSpace(manifest.SeriesIcon)
                        && !manifest.SeriesIcon.Equals("smash_symbol", StringComparison.OrdinalIgnoreCase))
                    {
                        var misc2 = MenuSlotService.FindMisc2(installRoot);
                        if (misc2 != null)
                        {
                            try
                            {
                                await _menuSlots.ReplaceArtForLinkageAsync(
                                    misc2, manifest.SeriesIcon, symbol, jpexsPath, null, progress, cancellationToken);
                            }
                            catch (Exception ex)
                            {
                                progress?.Report("Symbol update warning: " + ex.Message);
                            }
                        }
                    }
                }

                updated++;
            }

            progress?.Report("Saving mappings…");
            await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            return new WizardResult
            {
                Success = true,
                ContentId = updated.ToString(),
                Message = $"Updated {updated} character package(s)."
            };
        }

        /// <summary>Legacy entry point — maps to Clone/New style add.</summary>
        public Task<WizardResult> AddCharacterAsync(
            string installRoot,
            JObject doc,
            CharacterWizardRequest req,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (req.Mode == CharacterWizardMode.Edit)
                req.Mode = CharacterWizardMode.Clone;
            if (string.IsNullOrWhiteSpace(req.DonorChipId))
                req.DonorChipId = "sandbag";
            if (string.IsNullOrWhiteSpace(req.DonorId))
                req.DonorId = req.DonorChipId;
            return RunCharacterWizardAsync(installRoot, doc, req, jpexsPath, progress, cancellationToken);
        }

        private async Task ClonePackToDestAsync(
            string sourcePath,
            string oldName,
            string newId,
            string destSwf,
            string? displayName,
            string resourceGuid,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-charwiz-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var result = await _clone.CloneAsync(new CharacterCloneRequest
                {
                    SourcePath = sourcePath,
                    OldName = oldName,
                    NewName = newId,
                    OutputDirectory = work,
                    DisplayName = string.IsNullOrWhiteSpace(displayName) ? null : displayName,
                    JpexsPath = jpexsPath,
                    Guid = resourceGuid
                }, progress, cancellationToken);
                CharacterPackageService.CopyFileWithRetry(result.OutputSwfPath, destSwf);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public async Task<WizardResult> AddStageAsync(
            string installRoot,
            JObject doc,
            StageWizardRequest req,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (GamePrepareService.IsPrepared(installRoot))
            {
                await EnsurePackRuntimeAsync(installRoot, jpexsPath, progress, cancellationToken);
                if (SlotPoolService.IsReady(installRoot))
                    return await AddStageViaPackAsync(installRoot, doc, req, jpexsPath, progress, cancellationToken);
            }

            var id = MenuSlotService.SanitizeId(req.Id);
            if (string.IsNullOrWhiteSpace(req.SourceSwfPath) || !File.Exists(req.SourceSwfPath))
                throw new FileNotFoundException("Stage SWF not found.", req.SourceSwfPath);

            var stageDir = Path.Combine(installRoot, "data", "stage");
            Directory.CreateDirectory(stageDir);
            var destSwf = Path.Combine(stageDir, id + ".swf");
            var sourceBase = Path.GetFileNameWithoutExtension(req.SourceSwfPath);

            var needsClone = req.CloneIfExists &&
                             (!sourceBase.Equals(id, StringComparison.OrdinalIgnoreCase) ||
                              !PathsEqual(req.SourceSwfPath, destSwf) ||
                              File.Exists(destSwf));

            var stageGuid = MappingsEditService.AllocateUniqueGuid(doc);

            if (needsClone)
            {
                progress?.Report($"Cloning stage pack to {id}…");
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-stagewiz-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    var result = await _clone.CloneAsync(new CharacterCloneRequest
                    {
                        SourcePath = req.SourceSwfPath,
                        OldName = sourceBase,
                        NewName = id,
                        OutputDirectory = work,
                        DisplayName = string.IsNullOrWhiteSpace(req.DisplayName) ? null : req.DisplayName,
                        JpexsPath = jpexsPath,
                        Guid = stageGuid
                    }, progress, cancellationToken);
                    File.Copy(result.OutputSwfPath, destSwf, overwrite: true);
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch { /* ignore */ }
                }
            }
            else if (!PathsEqual(req.SourceSwfPath, destSwf))
            {
                File.Copy(req.SourceSwfPath, destSwf, overwrite: true);
                await _clone.PatchGuidInSwfAsync(destSwf, stageGuid, jpexsPath, progress, cancellationToken);
            }
            else
            {
                await _clone.PatchGuidInSwfAsync(destSwf, stageGuid, jpexsPath, progress, cancellationToken);
            }

            progress?.Report("Creating stage-select chip…");
            var menu = MenuSlotService.FindMenuStages(installRoot)
                ?? throw new FileNotFoundException("menu_stages.swf not found.");
            var symbols = await _menuSlots.LoadSymbolIndexAsync(installRoot, jpexsPath, progress, cancellationToken);
            if (!symbols.StageSelect.Contains(id))
            {
                await _menuSlots.CloneSelectSymbolAsync(
                    menu, req.DonorChipId, id, true, req.StageSelectPng,
                    jpexsPath, progress, cancellationToken);
            }

            MappingsEditService.UpsertEntry(
                doc, "stage", id, id + ".swf",
                name: string.IsNullOrWhiteSpace(req.DisplayName) ? id : req.DisplayName,
                guid: stageGuid,
                trainingOnly: req.TrainingOnly);

            var rows = MappingsEditService.GetSssNormalRows(doc);
            if (req.NewSssRow || rows.Count == 0)
                rows.Add(new List<string> { id });
            else
            {
                var row = req.SssRowIndex < 0 ? rows.Count - 1 : Math.Clamp(req.SssRowIndex, 0, rows.Count - 1);
                MappingsEditService.AppendToRow(rows, row, id);
            }
            MappingsEditService.SetSssNormalRows(doc, rows);
            var warn = MappingsEditService.FitSelectLayout(doc, isCss: false, rows);

            if (req.AddToRandom)
                MappingsEditService.AddToRandomPool(doc, "random_stages", "stages", id);

            progress?.Report("Saving and applying to game…");
            await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);

            return new WizardResult
            {
                Success = true,
                ContentId = id,
                Message = $"Stage '{id}' added to SSS and mappings.",
                Warning = warn
            };
        }

        private static async Task EnsurePackRuntimeAsync(
            string installRoot,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            if (SlotPoolService.IsReady(installRoot)) return;
            progress?.Report("Enabling content-pack runtime (character slot pool only)…");
            var prep = new GamePrepareService();
            await prep.EnsureContentPackRuntimeAsync(
                installRoot, jpexsPath, progress, cancellationToken,
                characterSlots: SlotPoolService.DefaultCharacterSlots,
                stageSlots: 0);
        }

        private async Task<WizardResult> RunCharacterWizardViaPackAsync(
            string installRoot,
            JObject doc,
            CharacterWizardRequest req,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken,
            WizardStepProgress? steps)
        {
            var id = MenuSlotService.SanitizeId(req.Id);
            var displayName = string.IsNullOrWhiteSpace(req.DisplayName) ? id : req.DisplayName.Trim();
            var donorId = MenuSlotService.SanitizeId(
                string.IsNullOrWhiteSpace(req.DonorId) ? req.DonorChipId : req.DonorId);
            if (string.IsNullOrWhiteSpace(donorId))
                donorId = DefaultNewUiDonorId;

            var charDir = CharacterPackageService.CharacterDataDir(installRoot);
            Directory.CreateDirectory(charDir);
            string ResolveDonorSwf()
            {
                var pkg = CharacterPackageService.ScanPackages(installRoot)
                    .FirstOrDefault(p => p.Manifest.Id.Equals(donorId, StringComparison.OrdinalIgnoreCase));
                if (pkg != null && pkg.SwfExists) return pkg.SwfPath;
                return Path.Combine(charDir, donorId + ".swf");
            }

            string source;
            string oldName;
            if (req.Mode == CharacterWizardMode.New)
            {
                donorId = DefaultNewUiDonorId;
                source = !string.IsNullOrWhiteSpace(req.SourceSwfPath) && File.Exists(req.SourceSwfPath)
                    ? req.SourceSwfPath
                    : ResolveDonorSwf();
                oldName = Path.GetFileNameWithoutExtension(source);
            }
            else
            {
                source = req.SourceSwfPath;
                if (string.IsNullOrWhiteSpace(source) || !File.Exists(source))
                    source = ResolveDonorSwf();
                var sourceStem = MenuSlotService.SanitizeId(Path.GetFileNameWithoutExtension(source));
                var customSource = !string.IsNullOrWhiteSpace(req.SourceSwfPath) && File.Exists(req.SourceSwfPath);
                oldName = customSource ? sourceStem : donorId;
                if (string.IsNullOrWhiteSpace(oldName))
                    oldName = sourceStem;
            }
            if (!File.Exists(source))
                throw new FileNotFoundException("Character source SWF not found.", source);

            var resourceGuid = MappingsEditService.AllocateUniqueGuid(doc);
            var packDir = ContentPackService.PackDir(installRoot, id);
            Directory.CreateDirectory(packDir);
            Directory.CreateDirectory(Path.Combine(packDir, "assets"));
            var contentSwf = ContentPackService.ContentFullPath(installRoot, id);

            progress?.Report($"Content pack: cloning fighter → {Path.GetFileName(contentSwf)}…");
            steps?.BeginStep(0, $"Pack clone '{id}'…");
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pack-char-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                if (oldName.Equals(id, StringComparison.OrdinalIgnoreCase))
                {
                    File.Copy(source, contentSwf, overwrite: true);
                    await _clone.PatchGuidInSwfAsync(contentSwf, resourceGuid, jpexsPath, progress, cancellationToken);
                }
                else
                {
                    var cloneResult = await _clone.CloneAsync(new CharacterCloneRequest
                    {
                        SourcePath = source,
                        OldName = oldName,
                        NewName = id,
                        OutputDirectory = work,
                        DisplayName = displayName,
                        JpexsPath = jpexsPath,
                        Guid = resourceGuid
                    }, progress, cancellationToken);
                    File.Copy(cloneResult.OutputSwfPath, contentSwf, overwrite: true);
                }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }

            // Inject-once: only write pack folder files. Host SWFs are never mutated on add.
            var seriesIcon = string.IsNullOrWhiteSpace(req.SeriesIcon) ? "smash_symbol" : req.SeriesIcon.Trim();
            if (req.CreateNewSeriesSymbol && string.IsNullOrWhiteSpace(req.SeriesIcon))
                seriesIcon = id + "_symbol";

            progress?.Report("Seeding pack assets/ from donor (live edit sources)…");
            steps?.BeginStep(1, "Seed pack assets…");
            try
            {
                await _packages.ExportDonorAssetFolderAsync(
                    installRoot, donorId, packDir, jpexsPath, progress, cancellationToken,
                    seriesIcon: seriesIcon);
            }
            catch (Exception ex)
            {
                progress?.Report("Donor asset seed warning: " + ex.Message);
            }

            void CopyAsset(string key, string? path)
            {
                if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;
                var ext = Path.GetExtension(path);
                if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
                var fileName = key.Equals("select", StringComparison.OrdinalIgnoreCase)
                               || key.Equals("pa", StringComparison.OrdinalIgnoreCase)
                               || key.Equals("stock", StringComparison.OrdinalIgnoreCase)
                    ? key.ToLowerInvariant() + (ext.Equals(".jpg", StringComparison.OrdinalIgnoreCase) || ext.Equals(".jpeg", StringComparison.OrdinalIgnoreCase) ? ext.ToLowerInvariant() : ".png")
                    : key + ext;
                if (fileName.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    fileName = Path.ChangeExtension(fileName, ".jpg");
                var dest = Path.Combine(packDir, "assets", fileName);
                File.Copy(path!, dest, overwrite: true);
            }
            CopyAsset("select", req.CssChipPng);
            CopyAsset("pa", req.PaPng ?? req.CharSelectPng);
            CopyAsset("stock", req.StockPng);
            foreach (var kv in req.SlotPngs)
                CopyAsset(kv.Key, kv.Value);
            if (!string.IsNullOrWhiteSpace(req.SeriesSymbolPng) && File.Exists(req.SeriesSymbolPng))
                File.Copy(req.SeriesSymbolPng, Path.Combine(packDir, "assets", "symbol.png"), overwrite: true);
            steps?.CompleteStep();

            progress?.Report("Writing live costumes.json…");
            steps?.BeginStep(2, "Write costumes.json…");
            await LivePackRuntimeService.WriteLiveCostumesJsonAsync(
                installRoot, donorId, id, packDir, jpexsPath, progress, cancellationToken);
            steps?.CompleteStep();

            var index = ContentPackService.LoadIndex(installRoot);
            var slot = ContentPackService.ClaimCharacterSlot(index, id);
            var rosterId = SlotPoolService.CharacterSlotId(slot);

            var pack = new ContentPackInfo
            {
                PackId = id,
                Type = ContentPackType.Character,
                Name = displayName,
                Guid = resourceGuid,
                Slot = slot,
                PlayableId = id,
                ContentRelativePath = ContentPackService.ContentFileRelativeFromData(id),
                SeriesIcon = seriesIcon
            };
            ContentPackService.SavePack(installRoot, pack);
            ContentPackService.SaveIndex(installRoot, index);

            progress?.Report($"Claimed pool slot {rosterId} for '{id}' (files only — no host SWF edits)…");
            steps?.BeginStep(3, "Commit pack mappings + live.json…");
            var packs = new ContentPackService();
            await packs.CommitPacksAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            steps?.CompleteStep();

            if (req.AddToRandom)
            {
                MappingsEditService.AddToRandomPool(doc, "random_characters", "characters", id);
                await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            }

            return new WizardResult
            {
                Success = true,
                ContentId = id,
                Message =
                    $"Character '{id}' added via content pack (slot {rosterId}). " +
                    "Pack folder only — CSS/PA/stock/costumes load live from assets/ + costumes.json."
            };
        }

        private async Task<WizardResult> AddStageViaPackAsync(
            string installRoot,
            JObject doc,
            StageWizardRequest req,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            var id = MenuSlotService.SanitizeId(req.Id);
            if (string.IsNullOrWhiteSpace(req.SourceSwfPath) || !File.Exists(req.SourceSwfPath))
                throw new FileNotFoundException("Stage SWF not found.", req.SourceSwfPath);

            var displayName = string.IsNullOrWhiteSpace(req.DisplayName) ? id : req.DisplayName.Trim();
            var sourceBase = Path.GetFileNameWithoutExtension(req.SourceSwfPath);
            var stageGuid = MappingsEditService.AllocateUniqueGuid(doc);
            var packDir = ContentPackService.PackDir(installRoot, id);
            Directory.CreateDirectory(packDir);
            Directory.CreateDirectory(Path.Combine(packDir, "assets"));
            var contentSwf = ContentPackService.ContentFullPath(installRoot, id);

            progress?.Report($"Content pack: cloning stage → {Path.GetFileName(contentSwf)} (no menu_stages inject)…");
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pack-stage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var needsClone = req.CloneIfExists &&
                                 !sourceBase.Equals(id, StringComparison.OrdinalIgnoreCase);
                if (needsClone)
                {
                    var result = await _clone.CloneAsync(new CharacterCloneRequest
                    {
                        SourcePath = req.SourceSwfPath,
                        OldName = sourceBase,
                        NewName = id,
                        OutputDirectory = work,
                        DisplayName = displayName,
                        JpexsPath = jpexsPath,
                        Guid = stageGuid
                    }, progress, cancellationToken);
                    File.Copy(result.OutputSwfPath, contentSwf, overwrite: true);
                }
                else
                {
                    File.Copy(req.SourceSwfPath, contentSwf, overwrite: true);
                    await _clone.PatchGuidInSwfAsync(contentSwf, stageGuid, jpexsPath, progress, cancellationToken);
                }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }

            if (!string.IsNullOrWhiteSpace(req.StageSelectPng) && File.Exists(req.StageSelectPng))
            {
                File.Copy(
                    req.StageSelectPng!,
                    Path.Combine(packDir, "assets", "stage_select" + Path.GetExtension(req.StageSelectPng)),
                    overwrite: true);
            }

            var index = ContentPackService.LoadIndex(installRoot);
            var slot = ContentPackService.ClaimStageSlot(index, id);
            var pack = new ContentPackInfo
            {
                PackId = id,
                Type = ContentPackType.Stage,
                Name = displayName,
                Guid = stageGuid,
                Slot = slot,
                PlayableId = id,
                ContentRelativePath = ContentPackService.ContentFileRelativeFromData(id)
            };
            ContentPackService.SavePack(installRoot, pack);
            ContentPackService.SaveIndex(installRoot, index);

            var packs = new ContentPackService();
            await packs.CommitPacksAsync(installRoot, doc, jpexsPath, progress, cancellationToken);

            if (req.AddToRandom)
            {
                MappingsEditService.AddToRandomPool(doc, "random_stages", "stages", id);
                await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            }

            return new WizardResult
            {
                Success = true,
                ContentId = id,
                Message =
                    $"Stage '{id}' added via content pack (slot {SlotPoolService.StageSlotId(slot)}). " +
                    "Stock menu_stages was not rewritten."
            };
        }

        private static bool PathsEqual(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);
    }
}
