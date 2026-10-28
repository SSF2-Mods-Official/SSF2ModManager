using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public enum CharacterAssetTier
    {
        V1,
        V2,
        V3
    }

    public enum CharacterAssetHost
    {
        MenuCharacters,
        Misc2,
        Misc,
        ClassicMode,
        AllStarMode,
        CharacterSwf,
        Mappings
    }

    public class CharacterAssetSlotDef
    {
        public string Key { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public CharacterAssetHost Host { get; init; }
        public string LinkageSuffix { get; init; } = "";
        public string? LinkagePrefix { get; init; }
        public CharacterAssetTier Tier { get; init; } = CharacterAssetTier.V1;
        /// <summary>Slots the wizard can place art into (excludes costumes AS blocks).</summary>
        public bool IsEditable => Key is not ("costumes" or "win");

        public string LinkageFor(string characterId)
        {
            if (!string.IsNullOrEmpty(LinkagePrefix))
                return LinkagePrefix + characterId;
            if (string.IsNullOrEmpty(LinkageSuffix))
                return characterId;
            return characterId + LinkageSuffix;
        }
    }

    public class CharacterAssetSlotStatus
    {
        public CharacterAssetSlotDef Def { get; init; } = new();
        public bool Present { get; set; }
        public string? ExportedPngPath { get; set; }
        public string StatusLabel =>
            Present
                ? (Def.IsEditable ? "Ready" : "Detected")
                : (Def.IsEditable ? "Missing" : "Missing");
    }

    public class CharacterAssetScanResult
    {
        public string CharacterId { get; set; } = "";
        public List<CharacterAssetSlotStatus> Slots { get; set; } = new();
        public bool InMappings { get; set; }
        public bool SwfOnDisk { get; set; }
        public bool HasCostumeBlock { get; set; }
    }

    public class SlotExportInfo
    {
        public string SlotKey { get; set; } = "";
        public string? PngPath { get; set; }
        public string? ImageCharacterId { get; set; }
        public int PixelWidth { get; set; }
        public int PixelHeight { get; set; }
    }

    /// <summary>
    /// Inventory-driven character UI art: scan / export / ensure / replace slots.
    /// </summary>
    public class CharacterAssetService
    {
        private readonly JpexsCliService _jpexs;
        private readonly MenuSlotService _menuSlots;
        private readonly ConcurrentDictionary<string, CachedExport> _exportCache = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, CachedLinkageMap> _linkageCache = new(StringComparer.OrdinalIgnoreCase);

        private sealed class CachedExport
        {
            public required string Dir { get; init; }
            public long Length { get; init; }
            public DateTime MtimeUtc { get; init; }
        }

        private sealed class CachedLinkageMap
        {
            public required Dictionary<string, int> Map { get; init; }
            public long Length { get; init; }
            public DateTime MtimeUtc { get; init; }
        }

        public CharacterAssetService(JpexsCliService? jpexs = null, MenuSlotService? menuSlots = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _menuSlots = menuSlots ?? new MenuSlotService(_jpexs);
        }

        public static IReadOnlyList<CharacterAssetSlotDef> GetAssetManifest() => Manifest;

        public static IEnumerable<CharacterAssetSlotDef> GetEditableSlots() =>
            Manifest.Where(s => s.IsEditable);

        /// <summary>Slots loaded eagerly on the assets step (fast path).</summary>
        public static readonly string[] EagerPlaceholderSlotKeys = { "select", "cssp", "stock" };

        /// <summary>
        /// Wizard-facing preview slots (one shared PA instead of charselect/cssp/classic/trophy/allstar).
        /// </summary>
        public static readonly string[] WizardPreviewSlotKeys =
        {
            "select", "pa", "stock", "event", "congrats", "allstar_congrats"
        };

        /// <summary>Host slots that share the same PA art as <c>cssp</c>.</summary>
        public static readonly string[] SharedPaSlotKeys =
        {
            "charselect", "cssp", "classic", "classic_trophy", "allstar"
        };

        /// <summary>
        /// If <paramref name="arts"/> has a <c>pa</c> (or cssp/charselect) PNG, copy that path
        /// onto every shared PA slot key so one upload updates all PA linkages.
        /// </summary>
        public static void FanOutSharedPaSlots(IDictionary<string, string> arts)
        {
            if (arts == null) return;
            string? src = null;
            foreach (var key in new[] { "pa", "cssp", "charselect" })
            {
                if (arts.TryGetValue(key, out var path) && !string.IsNullOrWhiteSpace(path) && File.Exists(path))
                {
                    src = path;
                    break;
                }
            }
            if (src == null) return;
            foreach (var key in SharedPaSlotKeys)
            {
                if (!arts.ContainsKey(key))
                    arts[key] = src;
            }
        }

        private static string PlaceholderCacheRoot =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SSF2ModManager", "placeholder-cache");

        private static readonly CharacterAssetSlotDef[] Manifest =
        {
            new() { Key = "select", DisplayName = "CSS chip", Host = CharacterAssetHost.MenuCharacters,
                LinkageSuffix = "_select", Tier = CharacterAssetTier.V1 },
            new() { Key = "charselect", DisplayName = "Charselect PA", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_charselect", Tier = CharacterAssetTier.V1 },
            new() { Key = "stock", DisplayName = "Stock icon", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_stock", Tier = CharacterAssetTier.V1 },
            new() { Key = "cssp", DisplayName = "CSS portrait variant", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_cssp", Tier = CharacterAssetTier.V2 },
            new() { Key = "classic", DisplayName = "Classic PA", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_classic", Tier = CharacterAssetTier.V2 },
            new() { Key = "classic_trophy", DisplayName = "Classic trophy", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_classic_trophy", Tier = CharacterAssetTier.V2 },
            new() { Key = "allstar", DisplayName = "All-Star PA", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_allstar", Tier = CharacterAssetTier.V2 },
            new() { Key = "event", DisplayName = "Event PA", Host = CharacterAssetHost.Misc2,
                LinkageSuffix = "_event", Tier = CharacterAssetTier.V2 },
            new() { Key = "congrats", DisplayName = "Classic congrats", Host = CharacterAssetHost.ClassicMode,
                LinkagePrefix = "congrats_", Tier = CharacterAssetTier.V2 },
            new() { Key = "allstar_congrats", DisplayName = "All-Star congrats", Host = CharacterAssetHost.AllStarMode,
                LinkagePrefix = "allstar_congrats_", Tier = CharacterAssetTier.V2 },
            new() { Key = "costumes", DisplayName = "Costumes (misc)", Host = CharacterAssetHost.Misc,
                LinkageSuffix = "", Tier = CharacterAssetTier.V3 },
        };

        public async Task<CharacterAssetScanResult> ScanCharacterAsync(
            string installRoot,
            string characterId,
            JObject? mappingsDoc = null,
            string? jpexsPath = null,
            CancellationToken cancellationToken = default)
        {
            var id = MenuSlotService.SanitizeId(characterId);
            var result = new CharacterAssetScanResult { CharacterId = id };
            var charSwf = Path.Combine(installRoot, "data", "character", id + ".swf");
            result.SwfOnDisk = File.Exists(charSwf);
            result.InMappings = mappingsDoc?["character"]?[id] != null;

            var menuTask = SafeLinkageMap(MenuSlotService.FindMenuCharacters(installRoot), jpexsPath, cancellationToken);
            var misc2Task = SafeLinkageMap(MenuSlotService.FindMisc2(installRoot), jpexsPath, cancellationToken);
            var classicTask = SafeLinkageMap(FindModesFile(installRoot, "classic_mode.swf"), jpexsPath, cancellationToken);
            var allstarTask = SafeLinkageMap(FindModesFile(installRoot, "allstar_mode.swf"), jpexsPath, cancellationToken);
            await Task.WhenAll(menuTask, misc2Task, classicTask, allstarTask);

            var menuMap = menuTask.Result;
            var misc2Map = misc2Task.Result;
            var classicMap = classicTask.Result;
            var allstarMap = allstarTask.Result;

            result.HasCostumeBlock = await HasCostumeBlockAsync(installRoot, id, jpexsPath, cancellationToken);

            foreach (var def in Manifest)
            {
                var status = new CharacterAssetSlotStatus { Def = def };
                if (def.Key == "costumes")
                {
                    status.Present = result.HasCostumeBlock;
                    result.Slots.Add(status);
                    continue;
                }

                var link = def.LinkageFor(id);
                status.Present = def.Host switch
                {
                    CharacterAssetHost.MenuCharacters => menuMap.ContainsKey(link),
                    CharacterAssetHost.Misc2 => misc2Map.ContainsKey(link),
                    CharacterAssetHost.ClassicMode => classicMap.ContainsKey(link),
                    CharacterAssetHost.AllStarMode => allstarMap.ContainsKey(link),
                    _ => false
                };
                result.Slots.Add(status);
            }

            return result;
        }

        /// <summary>
        /// Exports placeholders via symbolClass + FFDec -selectid (not a full SWF dump).
        /// Hits AppData disk cache when the host SWF mtime is unchanged.
        /// </summary>
        public async Task<IReadOnlyDictionary<string, SlotExportInfo>> ExportSlotsBulkAsync(
            string installRoot,
            string characterId,
            IEnumerable<string> slotKeys,
            string outputDir,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var id = MenuSlotService.SanitizeId(characterId);
            Directory.CreateDirectory(outputDir);
            var keys = slotKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            var result = new Dictionary<string, SlotExportInfo>(StringComparer.OrdinalIgnoreCase);
            var installKey = HashInstallKey(installRoot);

            var byHost = keys
                .Select(k => Manifest.FirstOrDefault(m => m.Key.Equals(k, StringComparison.OrdinalIgnoreCase)))
                .Where(d => d != null && d!.Key is not ("costumes"))
                .Cast<CharacterAssetSlotDef>()
                .GroupBy(d => d.Host);

            var hostTasks = byHost.Select(async group =>
            {
                var host = group.Key;
                var swf = ResolveHostSwf(installRoot, host, id);
                if (swf == null || !File.Exists(swf))
                {
                    foreach (var def in group)
                        lock (result) result[def.Key] = new SlotExportInfo { SlotKey = def.Key };
                    return;
                }

                var missing = new List<CharacterAssetSlotDef>();
                foreach (var def in group)
                {
                    var cached = TryLoadDiskCachedPlaceholder(installKey, swf, id, def.Key, outputDir);
                    if (cached != null)
                    {
                        lock (result) result[def.Key] = cached;
                    }
                    else
                    {
                        missing.Add(def);
                    }
                }

                if (missing.Count == 0) return;

                progress?.Report($"Resolving symbols in {Path.GetFileName(swf)}…");
                var map = await GetLinkageMapCachedAsync(swf, jpexsPath, cancellationToken);
                var idBySlot = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var def in missing)
                {
                    var linkage = def.LinkageFor(id);
                    if (map.TryGetValue(linkage, out var spriteId))
                        idBySlot[def.Key] = spriteId;
                    else
                        lock (result) result[def.Key] = new SlotExportInfo { SlotKey = def.Key };
                }

                if (idBySlot.Count == 0) return;

                progress?.Report(
                    $"Exporting {idBySlot.Count} asset(s) from {Path.GetFileName(swf)}…");
                var exportDir = await ExportSelectedSpritesAsync(
                    swf, idBySlot.Values.Distinct(), jpexsPath, cancellationToken);

                foreach (var def in missing)
                {
                    if (!idBySlot.TryGetValue(def.Key, out var charId)) continue;
                    var linkage = def.LinkageFor(id);
                    var pngSrc = FindBestExportedPng(
                        exportDir, linkage, charId, preferFirstFrame: def.Key == "stock");
                    var info = new SlotExportInfo { SlotKey = def.Key };
                    if (pngSrc != null)
                    {
                        var dest = Path.Combine(outputDir, $"{id}_{def.Key}_placeholder.png");
                        File.Copy(pngSrc, dest, overwrite: true);
                        info.PngPath = dest;
                        info.ImageCharacterId = TryParseLeadingImageId(pngSrc) ?? charId.ToString();
                        TryReadPngSize(dest, out var w, out var h);
                        info.PixelWidth = w;
                        info.PixelHeight = h;
                        SaveDiskCachedPlaceholder(installKey, swf, id, def.Key, dest);
                    }
                    lock (result) result[def.Key] = info;
                }
            });

            await Task.WhenAll(hostTasks);
            return result;
        }

        public async Task<string?> ExportSlotAsync(
            string installRoot,
            string characterId,
            string slotKey,
            string outputPngPath,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var bulk = await ExportSlotsBulkAsync(
                installRoot, characterId, new[] { slotKey },
                Path.GetDirectoryName(outputPngPath) ?? Path.GetTempPath(),
                jpexsPath, progress, cancellationToken);
            if (!bulk.TryGetValue(slotKey, out var info) || string.IsNullOrWhiteSpace(info.PngPath))
                return null;
            if (!string.Equals(info.PngPath, outputPngPath, StringComparison.OrdinalIgnoreCase))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(outputPngPath)!);
                File.Copy(info.PngPath, outputPngPath, overwrite: true);
                return outputPngPath;
            }
            return info.PngPath;
        }

        /// <summary>
        /// Exports the CSS chip's visible fill bitmap (cropped), not the framed sprite with name bar.
        /// </summary>
        public async Task<string?> ExportSelectChipFillArtAsync(
            string installRoot,
            string characterId,
            string destPng,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var id = MenuSlotService.SanitizeId(characterId);
            var menu = MenuSlotService.FindMenuCharacters(installRoot);
            if (menu == null || !File.Exists(menu)) return null;

            var linkage = id + "_select";
            progress?.Report($"Exporting CSS chip fill art ({linkage})…");
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-select-fill-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var xmlPath = Path.Combine(work, "menu.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", menu, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var map = MenuSlotService.ParseSymbolClassMapFromXml(xml);
                if (!map.TryGetValue(linkage, out var spriteId))
                    return null;

                var bitsId = MenuSlotService.FindLargestNestedBitsIdInXml(xml, spriteId);
                if (bitsId == null || bitsId <= 0)
                    return null;

                var imgDir = Path.Combine(work, "img");
                Directory.CreateDirectory(imgDir);
                await _jpexs.ExportSelectedAsync(
                    jpexsPath ?? "", menu, imgDir, new[] { bitsId.Value }, "image", cancellationToken);
                var png = FindBestExportedPng(imgDir, linkage, bitsId.Value);
                if (png == null || !File.Exists(png))
                    return null;

                Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
                if (MenuSlotService.TryGetDefineBitsSize(xml, bitsId.Value.ToString(), out var bw, out var bh)
                    && bw > 0 && bh > 0
                    && MenuSlotService.TryComputeBitmapFillWindow(
                        xml, spriteId, bitsId.Value, bw, bh,
                        out var winX, out var winY, out var winW, out var winH)
                    && winW > 0 && winH > 0
                    && (winW < bw || winH < bh || winX > 0 || winY > 0))
                {
                    MenuSlotService.CropPng(png, destPng, winX, winY, winW, winH);
                }
                else
                {
                    File.Copy(png, destPng, overwrite: true);
                }
                return destPng;
            }
            catch (Exception ex)
            {
                progress?.Report("CSS chip fill export warning: " + ex.Message);
                return null;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>Exports a misc2 series-symbol linkage as PNG.</summary>
        public async Task<string?> ExportSeriesSymbolPngAsync(
            string installRoot,
            string seriesIcon,
            string destPng,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(seriesIcon)) return null;
            var misc2 = MenuSlotService.FindMisc2(installRoot);
            if (misc2 == null || !File.Exists(misc2)) return null;

            var name = seriesIcon.Trim();
            progress?.Report($"Exporting series symbol '{name}'…");
            try
            {
                var map = await GetLinkageMapCachedAsync(misc2, jpexsPath, cancellationToken);
                if (!map.TryGetValue(name, out var spriteId) || spriteId <= 0)
                    return null;

                var exportDir = await ExportSelectedSpritesAsync(
                    misc2, new[] { spriteId }, jpexsPath, cancellationToken);
                var png = FindBestExportedPng(exportDir, name, spriteId, preferFirstFrame: true);
                if (png == null || !File.Exists(png))
                {
                    // Fallback: nested bits as image.
                    var work = Path.Combine(Path.GetTempPath(), "ssf2mm-sym-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(work);
                    try
                    {
                        var xmlPath = Path.Combine(work, "misc2.xml");
                        await _jpexs.SwfToXmlAsync(jpexsPath ?? "", misc2, xmlPath, cancellationToken);
                        var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                        var bitsId = MenuSlotService.FindLargestNestedBitsIdInXml(xml, spriteId);
                        if (bitsId == null) return null;
                        var imgDir = Path.Combine(work, "img");
                        Directory.CreateDirectory(imgDir);
                        await _jpexs.ExportSelectedAsync(
                            jpexsPath ?? "", misc2, imgDir, new[] { bitsId.Value }, "image", cancellationToken);
                        png = FindBestExportedPng(imgDir, name, bitsId.Value);
                    }
                    finally
                    {
                        try { Directory.Delete(work, true); } catch { /* ignore */ }
                    }
                }

                if (png == null || !File.Exists(png)) return null;
                Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
                File.Copy(png, destPng, overwrite: true);
                return destPng;
            }
            catch (Exception ex)
            {
                progress?.Report("Series symbol export warning: " + ex.Message);
                return null;
            }
        }

        public async Task EnsureUiSlotsFromDonorAsync(
            string installRoot,
            string newId,
            string donorId,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? displayName = null,
            string? seriesIcon = null,
            bool createNewSeriesSymbol = false,
            string? seriesSymbolTemplate = null,
            string? seriesSymbolPng = null,
            IReadOnlyDictionary<string, string>? slotPngs = null,
            bool skipCssSelect = false)
        {
            newId = MenuSlotService.SanitizeId(newId);
            donorId = MenuSlotService.SanitizeId(donorId);

            var byHost = Manifest
                .Where(d => d.IsEditable)
                .Where(d => !(skipCssSelect && d.Key.Equals("select", StringComparison.OrdinalIgnoreCase)))
                .Select(d => new
                {
                    Def = d,
                    Swf = ResolveHostSwf(installRoot, d.Host, newId)
                })
                .Where(x => x.Swf != null)
                .GroupBy(x => x.Swf!, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var misc2Path = ResolveHostSwf(installRoot, CharacterAssetHost.Misc2);
            var deferredScripts = new ConcurrentDictionary<string, List<(string DonorLink, string NewLink)>>(
                StringComparer.OrdinalIgnoreCase);
            var cloneErrors = new ConcurrentBag<string>();
            var movieclips = new List<string>
            {
                newId + "_stock",
                newId + "_classic",
                newId + "_charselect"
            };
            var resolvedSeries = string.IsNullOrWhiteSpace(seriesIcon) ? null : seriesIcon.Trim();

            // Costumes on misc.swf from T0 — independent of menu/misc2 host XML.
            // Never restore misc.ssf2mm.bak here: that wiped previously cloned costume blocks
            // (e.g. knuckles) when a later clone (notmario) ran EnsureUi.
            var costumeTask = Task.Run(async () =>
            {
                try
                {
                    await EnsureCostumeBlockFromDonorAsync(
                        installRoot, newId, donorId, jpexsPath, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    // Missing costumes prevent match load — escalate like select scripts.
                    throw new InvalidOperationException(
                        $"Costume block ensure failed for '{newId}' from donor '{donorId}': {ex.Message}", ex);
                }
            }, cancellationToken);

            // Dual warm FFDec workers: host XML/art in parallel; inject scripts as each host
            // finishes so menu/classic/allstar don't wait on misc2.
            var hostGroups = byHost
                .OrderBy(g =>
                {
                    try { return new FileInfo(g.Key).Length; }
                    catch { return long.MaxValue; }
                })
                .ToList();

            await Task.WhenAll(hostGroups.Select(async group =>
            {
                var swf = group.Key;
                var clones = new List<(string DonorLink, string NewLink, bool InjectScript)>();
                foreach (var item in group)
                {
                    clones.Add((
                        item.Def.LinkageFor(donorId),
                        item.Def.LinkageFor(newId),
                        InjectScript: true));
                }

                if (group.Any(x => x.Def.Key == "stock"))
                {
                    // Always enqueue stock0; host pipeline skips if donor linkage is absent
                    // (avoids a pre-pipeline symbolClass export probe).
                    clones.Add((donorId + "_stock0", newId + "_stock0", InjectScript: true));
                }

                try
                {
                    var jobs = clones.Where(c => c.InjectScript).Select(c => (c.DonorLink, c.NewLink)).ToList();
                    deferredScripts[swf] = jobs;

                    List<string>? labelLinks = null;
                    if (!string.IsNullOrWhiteSpace(displayName))
                    {
                        // CSS chip label only — *_event labeling was a no-op (~4s on misc2).
                        labelLinks = new List<string>();
                        if (group.Any(x => x.Def.Key == "select"))
                            labelLinks.Add(newId + "_select");
                        if (labelLinks.Count == 0) labelLinks = null;
                    }

                    List<(string Linkage, string PngPath)>? reps = null;
                    if (slotPngs != null && slotPngs.Count > 0)
                    {
                        reps = new List<(string Linkage, string PngPath)>();
                        foreach (var item in group)
                        {
                            if (!slotPngs.TryGetValue(item.Def.Key, out var png)
                                || string.IsNullOrWhiteSpace(png) || !File.Exists(png))
                                continue;
                            reps.Add((item.Def.LinkageFor(newId), png));
                        }
                        if (group.Any(x => x.Def.Key == "stock")
                            && slotPngs.TryGetValue("stock", out var stockPng)
                            && File.Exists(stockPng))
                        {
                            var stock0 = newId + "_stock0";
                            if (!reps.Any(r => r.Linkage.Equals(stock0, StringComparison.OrdinalIgnoreCase)))
                                reps.Add((stock0, stockPng));
                        }
                        if (reps.Count == 0) reps = null;
                    }

                    await _menuSlots.CloneLabelsAndArtsPipelineAsync(
                        swf, clones, labelLinks, displayName, reps,
                        "characterID", jpexsPath, progress, cancellationToken);
                    InvalidateCachesFor(swf);

                    var isMisc2 = misc2Path != null
                                  && string.Equals(swf, misc2Path, StringComparison.OrdinalIgnoreCase);
                    var isMenu = string.Equals(
                        swf, ResolveHostSwf(installRoot, CharacterAssetHost.MenuCharacters),
                        StringComparison.OrdinalIgnoreCase);
                    // New series symbol must land in misc2 before its importScript register pass.
                    if (isMisc2 && createNewSeriesSymbol)
                        return;

                    try
                    {
                        await _menuSlots.InjectScriptsAndMisc2RegisterAsync(
                            swf, jobs, isMisc2 ? movieclips : null, "characterID",
                            jpexsPath, progress, cancellationToken);
                        InvalidateCachesFor(swf);
                    }
                    catch (Exception ex)
                    {
                        // Menu select scripts are required for the CSS chip to appear — never swallow.
                        if (isMenu)
                            throw;
                        progress?.Report($"Script inject warning ({Path.GetFileName(swf)}): {ex.Message}");
                    }

                    if (isMenu && group.Any(x => x.Def.Key == "select"))
                    {
                        await _menuSlots.EnsureSelectScriptAsync(
                            swf, donorId, newId, jpexsPath, progress, cancellationToken);
                        InvalidateCachesFor(swf);
                    }
                }
                catch (Exception ex)
                {
                    cloneErrors.Add($"{Path.GetFileName(swf)}: {ex.Message}");
                }
            }));
            if (!cloneErrors.IsEmpty)
                throw new InvalidOperationException(
                    "UI slot clone failed:\n" + string.Join("\n", cloneErrors));

            var misc2 = misc2Path;
            if (misc2 != null && createNewSeriesSymbol)
            {
                try
                {
                    var template = string.IsNullOrWhiteSpace(seriesSymbolTemplate)
                        ? "smash_symbol"
                        : seriesSymbolTemplate.Trim();
                    var newSym = string.IsNullOrWhiteSpace(resolvedSeries)
                        ? newId + "_symbol"
                        : resolvedSeries!;
                    await _menuSlots.CloneSeriesSymbolAsync(
                        misc2, template, newSym, seriesSymbolPng, jpexsPath, progress, cancellationToken);
                    InvalidateCachesFor(misc2);
                    resolvedSeries = newSym;
                    movieclips.Add(newSym);
                    await _menuSlots.RetargetEventSeriesSymbolAsync(
                        misc2, newId, template, resolvedSeries!, jpexsPath, progress, cancellationToken);
                    InvalidateCachesFor(misc2);
                }
                catch (Exception ex)
                {
                    progress?.Report("Series symbol warning: " + ex.Message);
                }

                if (deferredScripts.TryGetValue(misc2, out var misc2Jobs))
                {
                    try
                    {
                        await _menuSlots.InjectScriptsAndMisc2RegisterAsync(
                            misc2, misc2Jobs, movieclips, "characterID",
                            jpexsPath, progress, cancellationToken);
                        InvalidateCachesFor(misc2);
                    }
                    catch (Exception ex)
                    {
                        progress?.Report($"Script inject warning ({Path.GetFileName(misc2)}): {ex.Message}");
                    }
                }
            }

            await costumeTask;
        }

        /// <summary>
        /// Patches Misc2.as <c>register("resources").movieclips</c> with vanilla-style entries:
        /// <c>{id}_stock</c>, <c>{id}_classic</c>, <c>{id}_charselect</c>, plus any extras (new series symbol).
        /// </summary>
        public async Task RegisterMisc2ResourcesAsync(
            string misc2Swf,
            string characterId,
            IEnumerable<string>? extraMovieclips = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            characterId = MenuSlotService.SanitizeId(characterId);
            var names = new List<string>
            {
                characterId + "_stock",
                characterId + "_classic",
                characterId + "_charselect"
            };
            if (extraMovieclips != null)
                names.AddRange(extraMovieclips.Where(n => !string.IsNullOrWhiteSpace(n)));

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-misc2reg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var scripts = Path.Combine(work, "scripts");
                Directory.CreateDirectory(scripts);
                progress?.Report("Patching Misc2.as register(resources)…");
                await _jpexs.ExportAsync(jpexsPath ?? "", misc2Swf, scripts, "script", cancellationToken);
                var misc2As = Directory.GetFiles(scripts, "Misc2.as", SearchOption.AllDirectories).FirstOrDefault();
                if (misc2As == null)
                    throw new FileNotFoundException("Misc2.as not found in misc2 SWF.");

                var text = await File.ReadAllTextAsync(misc2As, cancellationToken);
                var next = MenuSlotService.AppendMisc2Movieclips(text, names);
                if (string.Equals(text, next, StringComparison.Ordinal))
                {
                    progress?.Report("Misc2 movieclips already registered.");
                    return;
                }

                await File.WriteAllTextAsync(misc2As, next, cancellationToken);
                var scriptsRoot = Path.GetDirectoryName(misc2As)!;
                // FFDec importScript wants the folder that contains the package tree.
                var importRoot = scriptsRoot;
                var outSwf = Path.Combine(work, "misc2-reg.swf");
                await _jpexs.ImportScriptAsync(jpexsPath ?? "", misc2Swf, outSwf, importRoot, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("Misc2 register script import produced no SWF.");
                var bak = misc2Swf + ".ssf2mm.bak";
                if (!File.Exists(bak)) File.Copy(misc2Swf, bak, overwrite: false);
                File.Copy(outSwf, misc2Swf, overwrite: true);
                progress?.Report(
                    "Registered in Misc2: " + string.Join(", ", names.Distinct(StringComparer.OrdinalIgnoreCase)));
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        private async Task<string?> DetectDonorSeriesIconAsync(
            string misc2Swf,
            string donorId,
            string? jpexsPath,
            CancellationToken cancellationToken)
        {
            try
            {
                var map = await GetLinkageMapCachedAsync(misc2Swf, jpexsPath, cancellationToken);
                if (!map.TryGetValue(donorId + "_event", out var eventId)) return null;
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-donorsym-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    var xmlPath = Path.Combine(work, "m.xml");
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", misc2Swf, xmlPath, cancellationToken);
                    var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                    // Find series-symbol PlaceObject under the donor event sprite.
                    var open = Regex.Match(
                        xml,
                        $@"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""{eventId}""[^>]*>",
                        RegexOptions.IgnoreCase);
                    if (!open.Success)
                    {
                        open = Regex.Match(
                            xml,
                            $@"<item\b[^>]*spriteId\s*=\s*""{eventId}""[^>]*type\s*=\s*""DefineSpriteTag""[^>]*>",
                            RegexOptions.IgnoreCase);
                    }
                    if (!open.Success) return null;

                    // Approximate sprite block: from open to next top-level close via characterId scan window.
                    var window = xml.Substring(open.Index, Math.Min(120_000, xml.Length - open.Index));
                    foreach (Match pm in Regex.Matches(window, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                    {
                        if (!int.TryParse(pm.Groups[1].Value, out var cid) || cid == eventId) continue;
                        var name = MenuSlotService.FindFirstLinkageNameForCharacterId(xml, cid);
                        if (name != null && MenuSlotService.IsSeriesSymbolLinkageName(name))
                            return name;
                    }
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        public async Task ApplySlotArtAsync(
            string installRoot,
            string characterId,
            string slotKey,
            string pngPath,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!File.Exists(pngPath))
                throw new FileNotFoundException("Art PNG not found.", pngPath);

            var def = Manifest.FirstOrDefault(s => s.Key.Equals(slotKey, StringComparison.OrdinalIgnoreCase))
                ?? throw new ArgumentException("Unknown slot: " + slotKey);
            if (!def.IsEditable)
                throw new InvalidOperationException($"Slot {slotKey} is not writable.");

            var id = MenuSlotService.SanitizeId(characterId);
            var swf = ResolveHostSwf(installRoot, def.Host, id)
                ?? throw new FileNotFoundException("Host SWF not found for " + def.Host);
            var linkage = def.LinkageFor(id);

            progress?.Report($"Replacing art in {linkage}…");
            InvalidateCachesFor(swf);
            await _menuSlots.ReplaceArtForLinkageAsync(
                swf, linkage, pngPath, jpexsPath, null, progress, cancellationToken);
            InvalidateCachesFor(swf);
            TryInvalidateDiskCacheForSwf(installRoot, swf);
            progress?.Report($"Updated {Path.GetFileName(swf)} / {linkage}.");
        }

        /// <summary>
        /// Apply slot PNGs in parallel across host SWFs (menu / misc2 / classic / allstar).
        /// Each host SWF is updated in one batched export/uniquify/replace pass.
        /// </summary>
        public async Task ApplySlotArtsAsync(
            string installRoot,
            string characterId,
            IReadOnlyDictionary<string, string> slotPngs,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var id = MenuSlotService.SanitizeId(characterId);
            var work = slotPngs
                .Where(kv => !string.IsNullOrWhiteSpace(kv.Value) && File.Exists(kv.Value))
                .Select(kv =>
                {
                    var def = Manifest.FirstOrDefault(m => m.Key.Equals(kv.Key, StringComparison.OrdinalIgnoreCase));
                    if (def == null || !def.IsEditable) return null;
                    var swf = ResolveHostSwf(installRoot, def.Host, id);
                    return swf == null ? null : new { Def = def, Png = kv.Value, Swf = swf };
                })
                .Where(x => x != null)
                .GroupBy(x => x!.Swf, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var errors = new ConcurrentBag<string>();
            await Task.WhenAll(work.Select(async group =>
            {
                var swf = group.Key;
                try
                {
                    InvalidateCachesFor(swf);
                    var reps = group
                        .Select(item => (item!.Def.LinkageFor(id), item.Png))
                        .ToList();
                    // Samus pattern: stock0 image linkage gets the same art as stock.
                    var stockItem = group.FirstOrDefault(i => i!.Def.Key.Equals("stock", StringComparison.OrdinalIgnoreCase));
                    if (stockItem != null)
                    {
                        var stock0 = id + "_stock0";
                        try
                        {
                            var map = await GetLinkageMapCachedAsync(swf, jpexsPath, cancellationToken);
                            if (map.ContainsKey(stock0) && !reps.Any(r => r.Item1.Equals(stock0, StringComparison.OrdinalIgnoreCase)))
                                reps.Add((stock0, stockItem.Png));
                        }
                        catch { /* ignore */ }
                    }
                    progress?.Report($"Applying {reps.Count} art slot(s) → {Path.GetFileName(swf)}…");
                    await _menuSlots.ReplaceArtsForLinkagesBatchAsync(
                        swf, reps, jpexsPath, progress, cancellationToken);
                    InvalidateCachesFor(swf);
                    TryInvalidateDiskCacheForSwf(installRoot, swf);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(swf)}: {ex.Message}");
                }
            }));

            if (!errors.IsEmpty)
                throw new InvalidOperationException("Art replace failed:\n" + string.Join("\n", errors));
        }

        public static string? ResolveHostSwf(string installRoot, CharacterAssetHost host, string? characterId = null) =>
            host switch
            {
                CharacterAssetHost.MenuCharacters => MenuSlotService.FindMenuCharacters(installRoot),
                CharacterAssetHost.Misc2 => MenuSlotService.FindMisc2(installRoot)
                    ?? FindDataFile(installRoot, "misc", "misc2.ssf"),
                CharacterAssetHost.Misc => FindDataFile(installRoot, "misc", "misc.swf")
                    ?? FindDataFile(installRoot, "misc", "misc.ssf"),
                CharacterAssetHost.ClassicMode => FindModesFile(installRoot, "classic_mode.swf"),
                CharacterAssetHost.AllStarMode => FindModesFile(installRoot, "allstar_mode.swf"),
                CharacterAssetHost.CharacterSwf when !string.IsNullOrWhiteSpace(characterId) =>
                    Path.Combine(installRoot, "data", "character", MenuSlotService.SanitizeId(characterId) + ".swf"),
                _ => null
            };

        /// <summary>Exact folder/file match for linkage (avoids select⊂charselect).</summary>
        public static bool NameMatchesLinkage(string name, string linkage)
        {
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(linkage)) return false;
            // Exact
            if (name.Equals(linkage, StringComparison.OrdinalIgnoreCase)) return true;
            // DefineSprite_123_linkage or ends with _linkage / .linkage
            if (name.EndsWith("_" + linkage, StringComparison.OrdinalIgnoreCase)) return true;
            if (name.EndsWith("." + linkage, StringComparison.OrdinalIgnoreCase)) return true;
            // DefineSprite_123_linkage (FFDec style) — token after last digit underscore group
            var m = Regex.Match(name, @"Define(?:Sprite|Bits(?:JPEG\d*|Lossless\d*)?)_\d+_(.+)$", RegexOptions.IgnoreCase);
            if (m.Success && m.Groups[1].Value.Equals(linkage, StringComparison.OrdinalIgnoreCase))
                return true;
            return false;
        }

        /// <summary>Picks stock=first frame; others=last substantive (≥500B), never largest-shadow heuristic.</summary>
        public static string? PickSpriteFramePng(IEnumerable<string> pngs, bool preferFirstFrame)
        {
            var list = pngs
                .Where(f => File.Exists(f) && new FileInfo(f).Length >= 64)
                .OrderBy(FrameNumber)
                .ToList();
            if (list.Count == 0) return null;
            if (preferFirstFrame) return list.First();
            var substantive = list.Where(f => new FileInfo(f).Length >= 500).ToList();
            return substantive.LastOrDefault() ?? list.Last();

            static int FrameNumber(string path) =>
                int.TryParse(Path.GetFileNameWithoutExtension(path), out var n) ? n : int.MaxValue;
        }

        /// <summary>
        /// Finds exported PNG for a linkage — sprite frames or direct DefineBits image exports.
        /// </summary>
        public static string? FindBestExportedPng(
            string exportRoot, string linkage, int characterId, bool preferFirstFrame = false)
        {
            var fromLink = FindExportedPngForLinkage(exportRoot, linkage, preferFirstFrame);
            if (fromLink != null) return fromLink;

            // Direct image export: 27.png / DefineBitsLossless_27_congrats_mario.png
            var byId = Directory.GetFiles(exportRoot, "*.*", SearchOption.AllDirectories)
                .Where(IsImageFile)
                .Where(f =>
                {
                    var name = Path.GetFileNameWithoutExtension(f);
                    if (name.Equals(characterId.ToString(), StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(characterId + "_", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith(characterId + ".", StringComparison.OrdinalIgnoreCase))
                        return true;
                    return Regex.IsMatch(
                        name,
                        $@"DefineBits(?:JPEG\d*|Lossless\d*)_{characterId}(?:_|$)",
                        RegexOptions.IgnoreCase);
                })
                .OrderByDescending(f => new FileInfo(f).Length)
                .FirstOrDefault();
            return byId;
        }

        private async Task EnsureCostumeBlockFromDonorAsync(
            string installRoot, string newId, string donorId,
            string? jpexsPath, IProgress<string>? progress, CancellationToken cancellationToken)
        {
            var misc = ResolveHostSwf(installRoot, CharacterAssetHost.Misc);
            if (misc == null) return;

            progress?.Report($"Cloning costume block {donorId} → {newId}…");
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-costume-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var scripts = Path.Combine(work, "scripts");
                Directory.CreateDirectory(scripts);
                // Single export: presence check + inject (no separate HasCostumeBlockAsync pass).
                await _jpexs.ExportAsync(jpexsPath ?? "", misc, scripts, "script", cancellationToken);
                var miscAs = Directory.GetFiles(scripts, "Misc.as", SearchOption.AllDirectories).FirstOrDefault()
                    ?? Directory.GetFiles(scripts, "*.as", SearchOption.AllDirectories)
                        .FirstOrDefault(f => File.ReadAllText(f).Contains("costume", StringComparison.OrdinalIgnoreCase));
                if (miscAs == null)
                    throw new FileNotFoundException("Misc.as not found in misc SWF.");

                var text = await File.ReadAllTextAsync(miscAs, cancellationToken);
                if (text.Contains($"[\"{newId}\"]", StringComparison.Ordinal) ||
                    text.Contains($"['{newId}']", StringComparison.Ordinal))
                {
                    progress?.Report($"Costume block for {newId} already present.");
                    return;
                }

                string? block = null;
                var costumeDonor = donorId;
                foreach (var tryDonor in new[] { donorId, "mario", "sonic", "sandbag" }
                             .Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    block = CharacterCloneService.ExtractCostumeBlock(text, tryDonor, newId);
                    if (!string.IsNullOrWhiteSpace(block))
                    {
                        costumeDonor = tryDonor;
                        break;
                    }
                }
                if (string.IsNullOrWhiteSpace(block))
                    throw new InvalidOperationException(
                        $"No costume block for donor '{donorId}' (also tried mario/sonic/sandbag).");

                // Append rewritten block after donor init area.
                var donorInit = new Regex(
                    $@"_loc1_\s*\[\s*[""']{Regex.Escape(costumeDonor)}[""']\s*\]\s*=\s*new\s+Array\s*\(\s*\)\s*;",
                    RegexOptions.CultureInvariant);
                var m = donorInit.Match(text);
                if (!m.Success)
                {
                    text = text.TrimEnd() + "\r\n" +
                           $"_loc1_[\"{newId}\"] = new Array();\r\n" + block + "\r\n";
                }
                else
                {
                    // Find end of donor block and insert after.
                    var nextInit = new Regex(
                        @"_loc1_\s*\[\s*[""']([^""']+)[""']\s*\]\s*=\s*new\s+Array\s*\(\s*\)\s*;",
                        RegexOptions.CultureInvariant);
                    var insertAt = text.Length;
                    foreach (Match nm in nextInit.Matches(text, m.Index + m.Length))
                    {
                        if (!string.Equals(nm.Groups[1].Value, costumeDonor, StringComparison.Ordinal))
                        {
                            insertAt = nm.Index;
                            break;
                        }
                    }
                    var inject = $"_loc1_[\"{newId}\"] = new Array();\r\n{block}\r\n";
                    text = text.Insert(insertAt, inject);
                }

                await File.WriteAllTextAsync(miscAs, text, cancellationToken);
                // Delta importScript — only Misc.as (full tree on misc.swf is very slow).
                var packageRoot = Path.GetDirectoryName(miscAs)!;
                var deltaRoot = Path.Combine(work, "delta");
                MenuSlotService.BuildDeltaScriptImportTree(
                    packageRoot, deltaRoot, new[] { Path.GetFileName(miscAs) });
                var outSwf = Path.Combine(work, "misc-out.swf");
                progress?.Report("importScript delta (Misc.as) into misc.swf…");
                await _jpexs.ImportScriptAsync(jpexsPath ?? "", misc, outSwf, deltaRoot, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("Costume script import produced no SWF.");
                var bak = misc + ".ssf2mm.bak";
                if (!File.Exists(bak)) File.Copy(misc, bak, overwrite: false);
                File.Copy(outSwf, misc, overwrite: true);
                progress?.Report($"Costume block cloned into {Path.GetFileName(misc)}.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        private async Task<bool> HasCostumeBlockAsync(
            string installRoot, string id, string? jpexsPath, CancellationToken cancellationToken)
        {
            var misc = ResolveHostSwf(installRoot, CharacterAssetHost.Misc);
            if (misc == null) return false;
            try
            {
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-costscan-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    await _jpexs.ExportAsync(jpexsPath ?? "", misc, work, "script", cancellationToken);
                    foreach (var asFile in Directory.GetFiles(work, "*.as", SearchOption.AllDirectories))
                    {
                        var t = await File.ReadAllTextAsync(asFile, cancellationToken);
                        if (t.Contains($"[\"{id}\"]", StringComparison.Ordinal) ||
                            t.Contains($"['{id}']", StringComparison.Ordinal))
                            return true;
                    }
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch { /* ignore */ }
                }
            }
            catch { /* ignore */ }
            return false;
        }

        private async Task<string> ExportSelectedSpritesAsync(
            string swfPath,
            IEnumerable<int> characterIds,
            string? jpexsPath,
            CancellationToken cancellationToken)
        {
            var ids = characterIds.Where(i => i > 0).Distinct().OrderBy(i => i).ToList();
            var cacheKey = swfPath + "|sel|" + string.Join(",", ids);
            var fi = new FileInfo(swfPath);
            if (_exportCache.TryGetValue(cacheKey, out var cached) &&
                cached.Length == fi.Length &&
                cached.MtimeUtc == fi.LastWriteTimeUtc &&
                Directory.Exists(cached.Dir))
            {
                return cached.Dir;
            }

            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-sel-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            await _jpexs.ExportSelectedAsync(
                jpexsPath ?? "", swfPath, dir, ids, "sprite,image", cancellationToken,
                "export.zoom=1.0,disableBitmapSmoothing=true,snapToPixels=true");
            _exportCache[cacheKey] = new CachedExport
            {
                Dir = dir,
                Length = fi.Length,
                MtimeUtc = fi.LastWriteTimeUtc
            };
            return dir;
        }

        private async Task<string?> FindImageIdFromSpriteXmlAsync(
            string swfPath,
            int spriteId,
            string? jpexsPath,
            string workRoot,
            CancellationToken cancellationToken)
        {
            try
            {
                var xmlPath = Path.Combine(workRoot, "imgid.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var spriteOpen = Regex.Match(
                    xml,
                    $@"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""{spriteId}""[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!spriteOpen.Success)
                {
                    spriteOpen = Regex.Match(
                        xml,
                        $@"<item\b[^>]*spriteId\s*=\s*""{spriteId}""[^>]*type\s*=\s*""DefineSpriteTag""[^>]*>",
                        RegexOptions.IgnoreCase);
                }
                if (!spriteOpen.Success) return null;

                // Scan PlaceObject characterIds inside a window after the sprite open tag.
                var window = xml.Substring(spriteOpen.Index, Math.Min(8000, xml.Length - spriteOpen.Index));
                foreach (Match m in Regex.Matches(window, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                {
                    var cid = m.Groups[1].Value;
                    if (cid == spriteId.ToString()) continue;
                    if (Regex.IsMatch(xml, $@"type\s*=\s*""DefineBits[^""]*""[^>]*characterId\s*=\s*""{cid}""",
                            RegexOptions.IgnoreCase)
                        || Regex.IsMatch(xml, $@"characterId\s*=\s*""{cid}""[^>]*type\s*=\s*""DefineBits",
                            RegexOptions.IgnoreCase))
                    {
                        return cid;
                    }
                }
            }
            catch { /* ignore */ }
            return null;
        }

        private static string HashInstallKey(string installRoot)
        {
            var norm = Path.GetFullPath(installRoot).TrimEnd('\\', '/').ToLowerInvariant();
            var hash = 0;
            foreach (var c in norm)
                hash = hash * 31 + c;
            return "i" + (hash & 0x7FFFFFFF).ToString("x");
        }

        private static string DiskCacheDir(string installKey, string swfPath)
        {
            var fi = new FileInfo(swfPath);
            var folder = $"{Path.GetFileName(swfPath)}-{fi.LastWriteTimeUtc.Ticks}-{fi.Length}";
            return Path.Combine(PlaceholderCacheRoot, installKey, folder);
        }

        private static string DiskCachePngPath(string installKey, string swfPath, string charId, string slotKey) =>
            Path.Combine(DiskCacheDir(installKey, swfPath), $"{charId}_{slotKey}.png");

        private static SlotExportInfo? TryLoadDiskCachedPlaceholder(
            string installKey, string swfPath, string charId, string slotKey, string outputDir)
        {
            try
            {
                var cached = DiskCachePngPath(installKey, swfPath, charId, slotKey);
                if (!File.Exists(cached)) return null;
                var dest = Path.Combine(outputDir, $"{charId}_{slotKey}_placeholder.png");
                Directory.CreateDirectory(outputDir);
                File.Copy(cached, dest, overwrite: true);
                var info = new SlotExportInfo { SlotKey = slotKey, PngPath = dest };
                TryReadPngSize(dest, out var w, out var h);
                info.PixelWidth = w;
                info.PixelHeight = h;
                return info;
            }
            catch { return null; }
        }

        private static void SaveDiskCachedPlaceholder(
            string installKey, string swfPath, string charId, string slotKey, string pngPath)
        {
            try
            {
                var dest = DiskCachePngPath(installKey, swfPath, charId, slotKey);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(pngPath, dest, overwrite: true);
            }
            catch { /* ignore */ }
        }

        private static void TryInvalidateDiskCacheForSwf(string installRoot, string swfPath)
        {
            try
            {
                // Next export uses a new mtime key automatically; also prune the old folder if present.
                var installKey = HashInstallKey(installRoot);
                var dir = DiskCacheDir(installKey, swfPath);
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
            catch { /* ignore */ }
        }

        private async Task<Dictionary<string, int>> GetLinkageMapCachedAsync(
            string swfPath, string? jpexsPath, CancellationToken cancellationToken)
        {
            var fi = new FileInfo(swfPath);
            if (_linkageCache.TryGetValue(swfPath, out var cached) &&
                cached.Length == fi.Length &&
                cached.MtimeUtc == fi.LastWriteTimeUtc)
            {
                return cached.Map;
            }
            var map = await _menuSlots.ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
            _linkageCache[swfPath] = new CachedLinkageMap
            {
                Map = map,
                Length = fi.Length,
                MtimeUtc = fi.LastWriteTimeUtc
            };
            return map;
        }

        private void InvalidateCachesFor(string swfPath)
        {
            foreach (var key in _exportCache.Keys.Where(k =>
                         k.StartsWith(swfPath, StringComparison.OrdinalIgnoreCase)).ToList())
                _exportCache.TryRemove(key, out _);
            _linkageCache.TryRemove(swfPath, out _);
        }

        private async Task<Dictionary<string, int>> SafeLinkageMap(
            string? swfPath, string? jpexsPath, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath))
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            try
            {
                return await GetLinkageMapCachedAsync(swfPath, jpexsPath, cancellationToken);
            }
            catch
            {
                return new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static string? FindModesFile(string installRoot, string fileName)
        {
            var preferred = Path.Combine(installRoot, "data", "modes", fileName);
            if (File.Exists(preferred)) return preferred;
            var data = Path.Combine(installRoot, "data");
            if (!Directory.Exists(data)) return null;
            return Directory.GetFiles(data, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string? FindDataFile(string installRoot, string category, string fileName)
        {
            var preferred = Path.Combine(installRoot, "data", category, fileName);
            if (File.Exists(preferred)) return preferred;
            var data = Path.Combine(installRoot, "data");
            if (!Directory.Exists(data)) return null;
            return Directory.GetFiles(data, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static string? FindExportedPngForLinkage(string exportRoot, string linkage, bool preferFirstFrame)
        {
            var dirs = Directory.GetDirectories(exportRoot, "*", SearchOption.AllDirectories)
                .Where(d => NameMatchesLinkage(Path.GetFileName(d), linkage))
                .ToList();

            foreach (var dir in dirs)
            {
                var pngs = Directory.GetFiles(dir, "*.png", SearchOption.AllDirectories);
                var picked = PickSpriteFramePng(pngs, preferFirstFrame);
                if (picked != null) return picked;
            }

            var files = Directory.GetFiles(exportRoot, "*.png", SearchOption.AllDirectories)
                .Where(f => NameMatchesLinkage(Path.GetFileNameWithoutExtension(f), linkage));
            return PickSpriteFramePng(files, preferFirstFrame);
        }

        private static string? FindImageCharacterIdNearPng(string exportRoot, string pngPath)
        {
            var id = TryParseLeadingImageId(pngPath);
            if (id != null) return id;
            var dir = Path.GetDirectoryName(pngPath);
            if (dir == null) return null;
            return Directory.GetFiles(dir, "*.*", SearchOption.TopDirectoryOnly)
                .Where(IsImageFile)
                .Select(TryParseLeadingImageId)
                .FirstOrDefault(x => x != null);
        }

        private static bool IsImageFile(string path)
        {
            var e = Path.GetExtension(path).ToLowerInvariant();
            return e is ".png" or ".jpg" or ".jpeg" or ".gif";
        }

        public static string? TryParseLeadingImageId(string path)
        {
            var name = Path.GetFileName(path);
            // 35.png / 645_congrats_artverify.png / DefineBitsLossless_35_….png
            var m = Regex.Match(name, @"^(\d+)(?:[._]|$)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            m = Regex.Match(name, @"DefineBits(?:JPEG\d*|Lossless\d*)_(\d+)", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        /// <summary>
        /// Bitmap character id from a DefineBits export name only — never sprite frame <c>1.png</c>.
        /// </summary>
        public static string? TryParseDefineBitsExportId(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            var name = Path.GetFileName(path);
            var m = Regex.Match(name, @"DefineBits(?:JPEG\d*|Lossless\d*)_(\d+)", RegexOptions.IgnoreCase);
            if (m.Success) return m.Groups[1].Value;
            // 645_congrats_….png — underscore form only (not 1.png / 35.jpg frame exports)
            m = Regex.Match(name, @"^(\d+)_.+", RegexOptions.IgnoreCase);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static void TryReadPngSize(string path, out int w, out int h)
        {
            w = h = 0;
            try
            {
                using var fs = File.OpenRead(path);
                using var img = System.Drawing.Image.FromStream(fs, false, false);
                w = img.Width;
                h = img.Height;
            }
            catch { /* ignore */ }
        }
    }
}
