using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace SSF2ModManager.Services
{
    public class MenuSymbolIndex
    {
        public HashSet<string> CharacterSelect { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> StageSelect { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> CharSelectPortraits { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> AllLinkages { get; } = new(StringComparer.OrdinalIgnoreCase);
        public string? MenuCharactersPath { get; set; }
        public string? MenuStagesPath { get; set; }
        public string? Misc2Path { get; set; }
    }

    public class MenuSlotCloneResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string? OutputSwfPath { get; set; }
        public int XmlTouches { get; set; }
    }

    /// <summary>
    /// Detects and clones CSS/SSS menu chip symbols ({id}_select / {id}_stage_select)
    /// and optional misc2 portraits ({id}_charselect).
    /// </summary>
    public class MenuSlotService
    {
        private readonly JpexsCliService _jpexs;

        public MenuSlotService(JpexsCliService? jpexs = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
        }

        public JpexsCliService Jpexs => _jpexs;

        public static string? FindMenuCharacters(string installRoot) =>
            FindDataFile(installRoot, "menu", "menu_characters.swf");

        public static string? FindMenuStages(string installRoot) =>
            FindDataFile(installRoot, "menu", "menu_stages.swf");

        public static string? FindMisc2(string installRoot) =>
            FindDataFile(installRoot, "misc", "misc2.swf");

        public async Task<MenuSymbolIndex> LoadSymbolIndexAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var index = new MenuSymbolIndex
            {
                MenuCharactersPath = FindMenuCharacters(installRoot),
                MenuStagesPath = FindMenuStages(installRoot),
                Misc2Path = FindMisc2(installRoot)
            };

            if (index.MenuCharactersPath != null)
            {
                progress?.Report("Scanning menu_characters symbolsâ€¦");
                foreach (var name in await ExportLinkageNamesAsync(
                             index.MenuCharactersPath, jpexsPath, cancellationToken))
                {
                    index.AllLinkages.Add(name);
                    if (IsCssSelectLinkage(name))
                        index.CharacterSelect.Add(name[..^"_select".Length]);
                }
            }

            if (index.MenuStagesPath != null)
            {
                progress?.Report("Scanning menu_stages symbolsâ€¦");
                foreach (var name in await ExportLinkageNamesAsync(
                             index.MenuStagesPath, jpexsPath, cancellationToken))
                {
                    index.AllLinkages.Add(name);
                    if (name.EndsWith("_stage_select", StringComparison.OrdinalIgnoreCase))
                        index.StageSelect.Add(name[..^"_stage_select".Length]);
                }
            }

            if (index.Misc2Path != null)
            {
                progress?.Report("Scanning misc2 symbolsâ€¦");
                foreach (var name in await ExportLinkageNamesAsync(
                             index.Misc2Path, jpexsPath, cancellationToken))
                {
                    index.AllLinkages.Add(name);
                    if (name.EndsWith("_charselect", StringComparison.OrdinalIgnoreCase))
                        index.CharSelectPortraits.Add(name[..^"_charselect".Length]);
                }
            }

            return index;
        }

        /// <summary>
        /// Duplicates a donor chip symbol to a new id inside menu_characters or menu_stages.
        /// </summary>
        public Task<MenuSlotCloneResult> CloneSelectSymbolAsync(
            string menuSwfPath,
            string donorId,
            string newId,
            bool isStage,
            string? replacementPngPath = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            CloneLinkageAsync(
                menuSwfPath,
                donorId,
                newId,
                isStage ? "_stage_select" : "_select",
                isStage ? "stageID" : "characterID",
                replacementPngPath,
                jpexsPath,
                progress,
                cancellationToken);

        /// <summary>
        /// Clones {donor}_charselect â†’ {newId}_charselect in misc2.swf.
        /// </summary>
        public Task<MenuSlotCloneResult> ClonePortraitSymbolAsync(
            string misc2Path,
            string donorId,
            string newId,
            string? replacementPngPath = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            CloneLinkageAsync(
                misc2Path, donorId, newId, "_charselect", "characterID",
                replacementPngPath, jpexsPath, progress, cancellationToken);

        public Task<MenuSlotCloneResult> CloneLinkageAsync(
            string swfPath,
            string donorId,
            string newId,
            string suffix,
            string idProp,
            string? replacementPngPath = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool injectScript = true)
        {
            donorId = donorId.Trim();
            newId = SanitizeId(newId);
            return CloneLinkageByNameAsync(
                swfPath, donorId + suffix, newId + suffix, idProp,
                replacementPngPath, jpexsPath, progress, cancellationToken, injectScript,
                donorIdForScript: donorId, newIdForScript: newId, suffixForScript: suffix);
        }

        /// <summary>
        /// Clones any donor linkage name to a new linkage name (supports prefix forms like congrats_id).
        /// </summary>
        public async Task<MenuSlotCloneResult> CloneLinkageByNameAsync(
            string swfPath,
            string donorLink,
            string newLink,
            string idProp = "characterID",
            string? replacementPngPath = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool injectScript = false,
            string? donorIdForScript = null,
            string? newIdForScript = null,
            string? suffixForScript = null)
        {
            var result = new MenuSlotCloneResult();
            donorLink = donorLink.Trim();
            newLink = newLink.Trim();
            if (string.IsNullOrWhiteSpace(donorLink) || string.IsNullOrWhiteSpace(newLink))
                throw new ArgumentException("Donor and new linkage names are required.");

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-linkclone-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Resolving donor linkage {donorLink}…");
                var linkages = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                if (linkages.ContainsKey(newLink))
                    throw new InvalidOperationException($"Linkage {newLink} already exists.");
                if (!linkages.TryGetValue(donorLink, out var donorCharId))
                    throw new InvalidOperationException($"Donor linkage {donorLink} not found in {Path.GetFileName(swfPath)}.");

                var donorId = donorIdForScript ?? GuessIdFromLinkage(donorLink);
                var newId = newIdForScript ?? GuessIdFromLinkage(newLink);
                var suffix = suffixForScript ?? GuessSuffixFromLinkage(newLink, newId);

                progress?.Report($"SWF → XML ({Path.GetFileName(swfPath)})…");
                var xmlPath = Path.Combine(work, "work.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);

                var newCharId = Math.Max(FindMaxSpriteId(xml), linkages.Values.DefaultIfEmpty(0).Max()) + 1;
                progress?.Report($"Duplicating spriteId {donorCharId} → {newCharId} as {newLink}…");
                var (xml2, touches) = DuplicateSpriteAndSymbolClass(
                    xml, donorCharId, newCharId, donorLink, newLink, donorId, newId, idProp);
                result.XmlTouches = touches;
                if (touches == 0)
                    throw new InvalidOperationException("No XML changes for linkage clone.");

                await File.WriteAllTextAsync(
                    xmlPath, xml2, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                var rebuilt = Path.Combine(work, "rebuilt.swf");
                progress?.Report("XML → SWF…");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, rebuilt, cancellationToken);

                if (!string.IsNullOrWhiteSpace(replacementPngPath) && File.Exists(replacementPngPath))
                {
                    progress?.Report($"Replacing art on character {newCharId}…");
                    rebuilt = await ReplaceArtForLinkageAsync(
                        rebuilt, newLink, replacementPngPath, jpexsPath, work, progress, cancellationToken);
                }

                if (injectScript)
                {
                    progress?.Report($"Importing ActionScript class {newLink} (dual-package)…");
                    rebuilt = await InjectLinkageScriptDualAsync(
                        rebuilt, donorLink, newLink, donorId, newId, idProp,
                        jpexsPath, work, progress, cancellationToken);
                }

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(rebuilt, swfPath, overwrite: true);

                result.Success = true;
                result.OutputSwfPath = swfPath;
                result.Message = $"Created {newLink} from {donorLink} in {Path.GetFileName(swfPath)}.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Ensures the linkage has a private DefineBits to -replace (duplicates shared images).
        /// Handles both sprite→nested image and direct image SymbolClass linkages (congrats_*).
        /// </summary>
        public async Task<string> EnsureUniqueImageForLinkageAsync(
            string swfPath,
            string linkage,
            string imageCharacterId,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (!int.TryParse(imageCharacterId, out var oldImgId))
                return imageCharacterId;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-uniqimg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var linkages = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                if (!linkages.TryGetValue(linkage, out var linkCharId))
                    return imageCharacterId;

                // Count how many linkages share this image id — if only ours, no need to clone.
                var sharers = linkages.Count(kv => kv.Value == oldImgId);
                if (sharers <= 1 && linkCharId == oldImgId)
                    return imageCharacterId;

                var xmlPath = Path.Combine(work, "work.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);

                var bitsOpen = Regex.Match(
                    xml,
                    $@"<item\b[^>]*type\s*=\s*""DefineBits[^""]*""[^>]*characterId\s*=\s*""{oldImgId}""[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!bitsOpen.Success)
                {
                    bitsOpen = Regex.Match(
                        xml,
                        $@"<item\b[^>]*characterId\s*=\s*""{oldImgId}""[^>]*type\s*=\s*""DefineBits[^""]*""[^>]*>",
                        RegexOptions.IgnoreCase);
                }
                if (!bitsOpen.Success)
                    return imageCharacterId;

                var bitsEnd = FindMatchingItemClose(xml, bitsOpen.Index);
                if (bitsEnd <= bitsOpen.Index)
                    return imageCharacterId;

                var newImgId = FindMaxSpriteId(xml) + 1;
                var bitsBlock = xml.Substring(bitsOpen.Index, bitsEnd - bitsOpen.Index);
                var clonedBits = RemapIdAttribute(bitsBlock, "characterId", oldImgId, newImgId);
                xml = xml.Insert(bitsEnd, "\n" + clonedBits + "\n");

                if (linkCharId == oldImgId)
                {
                    // Direct image linkage: point this SymbolClass name at the new bits id.
                    xml = RemapSymbolClassCharacterId(xml, linkage, oldImgId, newImgId);
                }
                else
                {
                    // Sprite linkage: remap PlaceObject refs inside that sprite only.
                    var spriteOpen = Regex.Match(
                        xml,
                        $@"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""{linkCharId}""[^>]*>",
                        RegexOptions.IgnoreCase);
                    if (!spriteOpen.Success)
                    {
                        spriteOpen = Regex.Match(
                            xml,
                            $@"<item\b[^>]*spriteId\s*=\s*""{linkCharId}""[^>]*type\s*=\s*""DefineSpriteTag""[^>]*>",
                            RegexOptions.IgnoreCase);
                    }
                    if (spriteOpen.Success)
                    {
                        var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
                        if (spriteEnd > spriteOpen.Index)
                        {
                            var spriteBlock = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
                            var remapped = RemapIdAttribute(spriteBlock, "characterId", oldImgId, newImgId);
                            xml = xml.Substring(0, spriteOpen.Index) + remapped + xml.Substring(spriteEnd);
                        }
                    }
                }

                await File.WriteAllTextAsync(
                    xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                var outSwf = Path.Combine(work, "uniq.swf");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, outSwf, cancellationToken);
                if (!File.Exists(outSwf))
                    return imageCharacterId;

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(outSwf, swfPath, overwrite: true);
                progress?.Report($"Uniqued image {oldImgId} → {newImgId} for {linkage}.");
                return newImgId.ToString();
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>Replace PNG into the image backing a linkage (unique-ifies first if shared).</summary>
        public Task<string> ReplaceArtForLinkageAsync(
            string swfPath,
            string linkage,
            string pngPath,
            string? jpexsPath,
            string? workRoot = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            ReplaceArtsForLinkagesBatchAsync(
                swfPath,
                new[] { (linkage, pngPath) },
                jpexsPath,
                progress,
                cancellationToken);

        /// <summary>
        /// Batch-replace art for many linkages in one SWF: one export, one uniquify XML pass,
        /// one multi -replace. Much faster than per-slot round trips.
        /// </summary>
        public async Task<string> ReplaceArtsForLinkagesBatchAsync(
            string swfPath,
            IReadOnlyList<(string Linkage, string PngPath)> replacements,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (replacements == null || replacements.Count == 0)
                return swfPath;

            var workRoot = Path.Combine(Path.GetTempPath(), "ssf2mm-repart-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
            try
            {
                progress?.Report($"Resolving {replacements.Count} linkage(s) in {Path.GetFileName(swfPath)}…");
                var map = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);

                var resolved = new List<(string Linkage, int CharId, string PngPath)>();
                foreach (var (linkage, png) in replacements)
                {
                    if (string.IsNullOrWhiteSpace(png) || !File.Exists(png))
                        throw new FileNotFoundException("Art PNG not found.", png);
                    if (!map.TryGetValue(linkage, out var charId))
                        throw new InvalidOperationException("Linkage not found: " + linkage);
                    resolved.Add((linkage, charId, png));
                }

                // Direct image linkages (classic/allstar congrats): SymbolClass id IS the bitmap id —
                // skip the expensive selective export for those.
                var imageTargets = new List<(string Linkage, string ImageId, string PngPath)>();
                var needsExport = new List<(string Linkage, int CharId, string PngPath)>();
                foreach (var (linkage, charId, png) in resolved)
                {
                    if (IsDirectImageLinkageName(linkage))
                        imageTargets.Add((linkage, charId.ToString(), png));
                    else
                        needsExport.Add((linkage, charId, png));
                }

                if (needsExport.Count > 0)
                {
                    // Sprite-frame exports are named 1.png — never treat those as bitmap chids.
                    // Resolve the real nested DefineBits id from XML (largest = PA, not series icon).
                    var xmlPathResolve = Path.Combine(workRoot, "resolve.xml");
                    await _jpexs.SwfToXmlAsync(
                        jpexsPath ?? "", swfPath, xmlPathResolve, cancellationToken);
                    var xmlResolve = await File.ReadAllTextAsync(xmlPathResolve, cancellationToken);

                    foreach (var (linkage, charId, png) in needsExport)
                    {
                        progress?.Report($"Resolving bitmap id for {linkage}…");
                        var imageId = FindLargestNestedBitsIdInXml(xmlResolve, charId)?.ToString()
                                      ?? (IsDefineBitsInXml(xmlResolve, charId) ? charId.ToString() : null);
                        if (imageId == null)
                        {
                            // Fallback: scan image exports for DefineBits* / {chid}_* names.
                            var exportDir = Path.Combine(workRoot, "export");
                            if (!Directory.Exists(exportDir))
                            {
                                Directory.CreateDirectory(exportDir);
                                progress?.Report(
                                    $"Exporting {needsExport.Count} asset(s) from {Path.GetFileName(swfPath)}…");
                                await _jpexs.ExportSelectedAsync(
                                    jpexsPath ?? "", swfPath, exportDir,
                                    needsExport.Select(r => r.CharId).Distinct(),
                                    "sprite,image", cancellationToken,
                                    "export.zoom=1.0,disableBitmapSmoothing=true,snapToPixels=true");
                            }
                            imageId = FindBitmapIdFromImageExports(exportDir, linkage, charId);
                        }
                        if (imageId == null)
                            throw new InvalidOperationException("No PNG/bitmap id found for " + linkage);
                        imageTargets.Add((linkage, imageId, png));
                    }
                }

                // One in-memory XML pass: uniquify + companions + matrix + stock→stock0, then one xml2swf.
                var remapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in imageTargets)
                    remapped[t.Linkage] = t.ImageId;

                var xmlPath = Path.Combine(workRoot, "resolve.xml");
                if (!File.Exists(xmlPath))
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var sizeXml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var xmlDirty = false;

                var needsUniquify = imageTargets.Any(t =>
                {
                    if (!int.TryParse(t.ImageId, out var imgId)) return false;
                    if (!map.TryGetValue(t.Linkage, out var linkCharId)) return true;
                    if (linkCharId == imgId)
                        return map.Count(kv => kv.Value == imgId) > 1;
                    return true;
                });

                if (needsUniquify)
                {
                    progress?.Report($"Uniquifying shared bitmaps in {Path.GetFileName(swfPath)}…");
                    var uniqNext = FindMaxSpriteId(sizeXml) + 1;
                    var (xml2, remap2, _) = UniquifyImagesInXml(
                        sizeXml, map, imageTargets.Select(t => (t.Linkage, t.ImageId)).ToList(), uniqNext);
                    remapped = remap2;
                    if (!string.Equals(sizeXml, xml2, StringComparison.Ordinal))
                    {
                        sizeXml = xml2;
                        xmlDirty = true;
                    }
                }

                // Prefer *_cssp as charselect top-layer art when both are in this batch.
                foreach (var t in imageTargets
                             .Where(x => x.Linkage.EndsWith("_charselect", StringComparison.OrdinalIgnoreCase)))
                {
                    var cssp = t.Linkage[..^"_charselect".Length] + "_cssp";
                    if (!map.TryGetValue(cssp, out var csspId) || !IsDefineBitsInXml(sizeXml, csspId))
                        continue;
                    if (!map.TryGetValue(t.Linkage, out var csSprite)) continue;
                    var placed = CollectPlacedBitsWithDepth(sizeXml, csSprite);
                    var top = placed.OrderByDescending(p => p.Depth).FirstOrDefault();
                    if (top.Id == 0 || top.Id == csspId) continue;
                    // Point highest-depth PlaceObject at cssp bits so art layer is the cssp bitmap.
                    var (retargeted, touched) = RemapHighestDepthBitsUnderSprite(
                        sizeXml, csSprite, csspId);
                    if (touched)
                    {
                        sizeXml = retargeted;
                        remapped[t.Linkage] = csspId.ToString();
                        xmlDirty = true;
                        progress?.Report($"Charselect {t.Linkage} art layer → {cssp}");
                    }
                }

                var companionRemap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var companionTouches = 0;
                var nextId = FindMaxSpriteId(sizeXml) + 1;
                foreach (var t in imageTargets)
                {
                    var imgId = remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId;
                    if (!map.TryGetValue(t.Linkage, out var spriteCharId)
                        || !int.TryParse(imgId, out var primaryId))
                        continue;
                    var companions = FindCompanionBitsIdsInXml(sizeXml, spriteCharId, primaryId);
                    if (companions.Count == 0) continue;
                    var privateIds = new List<string>();
                    foreach (var companionId in companions)
                    {
                        var (xmlNext, newId) = UniquifySingleBitsUnderSprite(
                            sizeXml, spriteCharId, companionId, ref nextId);
                        if (newId == null || string.Equals(sizeXml, xmlNext, StringComparison.Ordinal))
                        {
                            privateIds.Add(companionId.ToString());
                            continue;
                        }
                        sizeXml = xmlNext;
                        privateIds.Add(newId.Value.ToString());
                        companionTouches++;
                        xmlDirty = true;
                    }
                    companionRemap[t.Linkage] = privateIds;
                }
                if (companionTouches > 0)
                    progress?.Report($"Uniquified {companionTouches} companion shadow bitmap(s).");

                var (matrixXml, matrixTouches) = NormalizeBitmapMatricesForTargets(
                    sizeXml, map, imageTargets.Select(t => (
                        t.Linkage,
                        remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId)).ToList());
                if (matrixTouches > 0)
                {
                    sizeXml = matrixXml;
                    xmlDirty = true;
                    progress?.Report($"Normalized {matrixTouches} bitmap fill matrix(es).");
                }

                // Derive character id from any *_stock linkage in this batch.
                var stockLink = imageTargets.Select(t => t.Linkage)
                    .FirstOrDefault(l => l.EndsWith("_stock", StringComparison.OrdinalIgnoreCase)
                                         && !l.EndsWith("_stock0", StringComparison.OrdinalIgnoreCase));
                if (stockLink != null)
                {
                    var charId = stockLink[..^"_stock".Length];
                    var (stockXml, stockTouched) = RetargetStockSpriteToStock0InXml(sizeXml, map, charId);
                    if (stockTouched)
                    {
                        sizeXml = stockXml;
                        xmlDirty = true;
                        progress?.Report($"Retargeted {charId}_stock → {charId}_stock0");
                    }
                }

                if (xmlDirty)
                {
                    await File.WriteAllTextAsync(
                        xmlPath, sizeXml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                    var mutatedSwf = Path.Combine(workRoot, "mutated.swf");
                    await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, mutatedSwf, cancellationToken);
                    if (!File.Exists(mutatedSwf))
                        throw new InvalidOperationException("Failed to rebuild SWF after art XML mutations.");
                    var bakMut = swfPath + ".ssf2mm.bak";
                    if (!File.Exists(bakMut))
                        File.Copy(swfPath, bakMut, overwrite: false);
                    File.Copy(mutatedSwf, swfPath, overwrite: true);
                }

                var fitDir = Path.Combine(workRoot, "fit");
                Directory.CreateDirectory(fitDir);
                var replacePairs = new List<(string CharacterIdOrScriptName, string ImportDataFile)>();
                var replacedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var t in imageTargets)
                {
                    var imgId = remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId;
                    var fitted = Path.Combine(fitDir, imgId + ".png");
                    if (TryGetDefineBitsSize(sizeXml, imgId, out var tw, out var th) && tw > 0 && th > 0)
                        FitArtPngForLinkage(sizeXml, map, t.Linkage, imgId, t.PngPath, fitted, tw, th);
                    else
                        File.Copy(t.PngPath, fitted, overwrite: true);
                    replacePairs.Add((imgId, fitted));
                    replacedIds.Add(imgId);

                    // Same-size companion bits (charselect shadow) → black silhouette of the new art.
                    IEnumerable<string> companionIds = companionRemap.TryGetValue(t.Linkage, out var listed)
                        ? listed
                        : (map.TryGetValue(t.Linkage, out var spriteCharId)
                           && int.TryParse(imgId, out var primaryId)
                            ? FindCompanionBitsIdsInXml(sizeXml, spriteCharId, primaryId).Select(c => c.ToString())
                            : Array.Empty<string>());
                    foreach (var cid in companionIds)
                    {
                        if (!replacedIds.Add(cid)) continue;
                        if (!TryGetDefineBitsSize(sizeXml, cid, out var cw, out var ch)) continue;
                        var shadowPng = Path.Combine(fitDir, cid + "-shadow.png");
                        MakeShadowPngFromAlpha(t.PngPath, shadowPng, cw, ch);
                        replacePairs.Add((cid, shadowPng));
                        progress?.Report($"Shadow companion bits {cid} ← silhouette for {t.Linkage}");
                    }
                }

                var outSwf = Path.Combine(workRoot, "replaced.swf");
                progress?.Report($"Replacing {replacePairs.Count} image(s) in {Path.GetFileName(swfPath)}…");
                await _jpexs.ReplaceManyAsync(jpexsPath ?? "", swfPath, outSwf, replacePairs, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("FFDec -replace failed for " + Path.GetFileName(swfPath));

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(outSwf, swfPath, overwrite: true);
                progress?.Report($"Replaced art for {resolved.Count} linkage(s) in {Path.GetFileName(swfPath)}.");
                return swfPath;
            }
            finally
            {
                try { Directory.Delete(workRoot, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Clone many donor→new linkages in one SWF with a single SWF↔XML round trip.
        /// </summary>
        /// <param name="injectScripts">When false, only XML/SymbolClass is written (defer scripts).</param>
        /// <param name="misc2Movieclips">When set, patch Misc2.as movieclips in the same importScript pass.</param>
        public async Task CloneLinkagesBatchAsync(
            string swfPath,
            IReadOnlyList<(string DonorLink, string NewLink, bool InjectScript)> clones,
            string idProp = "characterID",
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool injectScripts = true,
            IReadOnlyList<string>? misc2Movieclips = null,
            string? scriptCharacterIdOverride = null,
            IReadOnlyDictionary<string, string>? scriptIdByNewLink = null,
            bool shallowClone = false,
            IReadOnlyDictionary<string, string>? labelByNewLink = null)
        {
            if (clones == null || clones.Count == 0) return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-linkclone-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report(
                    $"Batch-cloning {clones.Count} linkage(s) in {Path.GetFileName(swfPath)}" +
                    (shallowClone ? " (shallow / shared art)…" : "…"));
                var linkages = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                var pending = new List<(string DonorLink, string NewLink, int DonorCharId, bool Inject)>();
                foreach (var (donorLink, newLink, inject) in clones)
                {
                    var d = donorLink.Trim();
                    var n = newLink.Trim();
                    if (linkages.ContainsKey(n)) continue;
                    if (!linkages.TryGetValue(d, out var donorCharId))
                    {
                        progress?.Report($"Skip {n}: donor {d} missing.");
                        continue;
                    }
                    pending.Add((d, n, donorCharId, inject));
                }

                if (pending.Count == 0) return;

                var xmlPath = Path.Combine(work, "work.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var nextId = Math.Max(FindMaxSpriteId(xml), linkages.Values.DefaultIfEmpty(0).Max()) + 1;
                var touches = 0;
                var scriptJobs = new List<(string DonorLink, string NewLink, string DonorId, string NewId)>();
                var playableOverride = string.IsNullOrWhiteSpace(scriptCharacterIdOverride)
                    ? null
                    : SanitizeId(scriptCharacterIdOverride);

                if (shallowClone)
                {
                    progress?.Report($"Bulk shallow-clone {pending.Count} sprite(s)…");
                    var bulk = BulkShallowCloneSelectSprites(
                        xml, pending, ref nextId, playableOverride, scriptIdByNewLink, labelByNewLink);
                    xml = bulk.Xml;
                    touches = bulk.Touches;
                    scriptJobs = bulk.ScriptJobs;
                    progress?.Report($"Bulk clone XML ready ({touches} touch(es)).");
                }
                else
                {
                    foreach (var (donorLink, newLink, donorCharId, inject) in pending)
                    {
                        var donorId = GuessIdFromLinkage(donorLink);
                        var newId = GuessIdFromLinkage(newLink);
                        var newCharId = nextId;
                        var (xml2, t) = DuplicateSpriteAndSymbolClass(
                            xml, donorCharId, newCharId, donorLink, newLink, donorId, newId, idProp,
                            deepCloneNested: true);
                        xml = xml2;
                        touches += t;
                        if (inject)
                        {
                            string scriptId = newId;
                            if (scriptIdByNewLink != null
                                && scriptIdByNewLink.TryGetValue(newLink, out var perLink)
                                && !string.IsNullOrWhiteSpace(perLink))
                                scriptId = SanitizeId(perLink);
                            else if (playableOverride != null)
                                scriptId = playableOverride;
                            scriptJobs.Add((donorLink, newLink, donorId, scriptId));
                        }
                        nextId = Math.Max(newCharId + 1, FindMaxSpriteId(xml) + 1);
                    }
                }

                if (touches == 0)
                    throw new InvalidOperationException("No XML changes for batch linkage clone.");

                await File.WriteAllTextAsync(
                    xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                var rebuilt = Path.Combine(work, "rebuilt.swf");
                progress?.Report("xml2swf…");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, rebuilt, cancellationToken);

                if (injectScripts && (scriptJobs.Count > 0 || (misc2Movieclips != null && misc2Movieclips.Count > 0)))
                {
                    progress?.Report(
                        $"Importing {scriptJobs.Count} ActionScript class(es) into {Path.GetFileName(swfPath)}…");
                    rebuilt = await InjectLinkageScriptsBatchAsync(
                        rebuilt, scriptJobs, idProp, jpexsPath, work, progress, cancellationToken,
                        misc2Movieclips);
                }

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(rebuilt, swfPath, overwrite: true);
                progress?.Report($"Created {pending.Count} linkage(s) in {Path.GetFileName(swfPath)}.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// One host SWF pass: clone linkages + prepare select text bounds + uniquify art XML,
        /// then a single xml2swf, optional importText, and bitmap -replace. Cuts ~2 SWF↔XML
        /// round trips vs calling clone / labels / arts separately.
        /// </summary>
        public async Task CloneLabelsAndArtsPipelineAsync(
            string swfPath,
            IReadOnlyList<(string DonorLink, string NewLink, bool InjectScript)> clones,
            IReadOnlyList<string>? labelLinkages,
            string? displayName,
            IReadOnlyList<(string Linkage, string PngPath)>? artReplacements,
            string idProp = "characterID",
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if ((clones == null || clones.Count == 0)
                && (labelLinkages == null || labelLinkages.Count == 0 || string.IsNullOrWhiteSpace(displayName))
                && (artReplacements == null || artReplacements.Count == 0))
                return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-hostpipe-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var pipeSw = Stopwatch.StartNew();
                void Phase(string name) =>
                    progress?.Report($"  [{Path.GetFileName(swfPath)} {pipeSw.Elapsed.TotalSeconds:F1}s] {name}");

                progress?.Report($"Host pipeline → {Path.GetFileName(swfPath)}…");
                // Always swf2xml first and parse SymbolClass from XML — skips a separate
                // -export symbolClass round-trip (~1–2s) before the heavy host work.
                var needsXml = (clones != null && clones.Count > 0)
                    || (artReplacements != null && artReplacements.Count > 0)
                    || (labelLinkages != null && labelLinkages.Count > 0 && !string.IsNullOrWhiteSpace(displayName));
                if (!needsXml) return;

                var xmlPath = Path.Combine(work, "work.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                Phase("swf2xml");
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                Phase($"read xml ({xml.Length / 1_000_000.0:F1} MB)");
                var map = ParseSymbolClassMapFromXml(xml);
                Phase($"symbolClass map ({map.Count})");
                var pending = new List<(string DonorLink, string NewLink, int DonorCharId, bool Inject)>();
                foreach (var (donorLink, newLink, inject) in clones ?? Array.Empty<(string, string, bool)>())
                {
                    var d = donorLink.Trim();
                    var n = newLink.Trim();
                    if (map.ContainsKey(n)) continue;
                    if (!map.TryGetValue(d, out var donorCharId))
                    {
                        progress?.Report($"Skip {n}: donor {d} missing.");
                        continue;
                    }
                    pending.Add((d, n, donorCharId, inject));
                }
                if (pending.Count == 0
                    && (artReplacements == null || artReplacements.Count == 0)
                    && (labelLinkages == null || labelLinkages.Count == 0 || string.IsNullOrWhiteSpace(displayName)))
                    return;

                var nextId = Math.Max(FindMaxSpriteId(xml), map.Values.DefaultIfEmpty(0).Max()) + 1;
                Phase("FindMaxSpriteId");
                var touches = 0;
                // With art replacements, shallow-clone sprites then uniquify only needed bitmaps/text.
                // Deep-cloning every nested JPEG in misc2 XML can take minutes.
                var shallowSprites = artReplacements != null && artReplacements.Count > 0;

                foreach (var (donorLink, newLink, donorCharId, _) in pending)
                {
                    var donorId = GuessIdFromLinkage(donorLink);
                    var newId = GuessIdFromLinkage(newLink);
                    var newCharId = nextId;
                    var (xml2, t) = DuplicateSpriteAndSymbolClass(
                        xml, donorCharId, newCharId, donorLink, newLink, donorId, newId, idProp,
                        deepCloneNested: !shallowSprites);
                    xml = xml2;
                    touches += t;
                    map[newLink] = newCharId;
                    // Never rescan the whole SWF XML for max id after every clone — that alone
                    // is multi-second on misc2. Track ids locally; deep clone bumps via scan once.
                    nextId = newCharId + 1;
                    if (!shallowSprites)
                        nextId = Math.Max(nextId, FindMaxSpriteId(xml) + 1);
                }
                Phase($"cloned {pending.Count} linkage(s)");

                // Select/event text bounds before importText (same XML pass).
                var labelJobs = new List<(string Linkage, List<int> TextIds)>();
                var allTextIds = new List<int>();
                if (labelLinkages != null && !string.IsNullOrWhiteSpace(displayName))
                {
                    foreach (var linkage in labelLinkages.Distinct(StringComparer.OrdinalIgnoreCase))
                    {
                        if (!map.TryGetValue(linkage, out var spriteId)) continue;
                        // Shallow clones share donor DefineText — private-copy before edit/importText.
                        if (shallowSprites)
                        {
                            var (xmlText, textTouches) = UniquifyTextTagsUnderSprite(xml, spriteId, ref nextId);
                            if (textTouches > 0)
                            {
                                xml = xmlText;
                                touches += textTouches;
                            }
                        }
                        var textIds = FindTextCharacterIdsInSprite(xml, spriteId);
                        if (textIds.Count == 0) continue;
                        labelJobs.Add((linkage, textIds));
                        foreach (var id in textIds)
                            if (!allTextIds.Contains(id)) allTextIds.Add(id);

                        // CSS select: always Samus-style single-line (CLONE-SAMUS), never two-line.
                        if (!IsCssSelectLinkage(linkage)) continue;
                        var label = BuildSingleLineChipLabel(displayName!);
                        var (xmlScaled, n) = ScaleSelectChipTextPlaceObjects(xml, spriteId, textIds, label);
                        if (n > 0) { xml = xmlScaled; touches += n; }
                    }
                }
                Phase($"labels prepared ({labelJobs.Count})");

                // Art uniquify / companions / matrix / stock retarget in the same XML.
                var remapped = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var companionRemap = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                var imageTargets = new List<(string Linkage, string ImageId, string PngPath)>();
                if (artReplacements != null && artReplacements.Count > 0)
                {
                    var resolved = new List<(string Linkage, int CharId, string PngPath)>();
                    foreach (var (linkage, png) in artReplacements)
                    {
                        if (string.IsNullOrWhiteSpace(png) || !File.Exists(png))
                            throw new FileNotFoundException("Art PNG not found.", png);
                        if (!map.TryGetValue(linkage, out var charId))
                            throw new InvalidOperationException("Linkage not found: " + linkage);
                        resolved.Add((linkage, charId, png));
                    }

                    var needsExport = new List<(string Linkage, int CharId, string PngPath)>();
                    foreach (var (linkage, charId, png) in resolved)
                    {
                        if (IsDirectImageLinkageName(linkage))
                            imageTargets.Add((linkage, charId.ToString(), png));
                        else
                            needsExport.Add((linkage, charId, png));
                    }
                    WarmDefineBitsOpenIndex(xml);
                    foreach (var (linkage, charId, png) in needsExport)
                    {
                        var imageId = FindLargestNestedBitsIdInXml(xml, charId)?.ToString()
                                      ?? (IsDefineBitsInXml(xml, charId) ? charId.ToString() : null);
                        if (imageId == null)
                            throw new InvalidOperationException("No PNG/bitmap id found for " + linkage);
                        imageTargets.Add((linkage, imageId, png));
                    }
                    Phase($"resolve {imageTargets.Count} bitmap id(s)");

                    foreach (var t in imageTargets)
                        remapped[t.Linkage] = t.ImageId;

                    var needsUniquify = imageTargets.Any(t =>
                    {
                        if (!int.TryParse(t.ImageId, out var imgId)) return false;
                        if (!map.TryGetValue(t.Linkage, out var linkCharId)) return true;
                        if (linkCharId == imgId)
                            return map.Count(kv => kv.Value == imgId) > 1;
                        return true;
                    });
                    if (needsUniquify)
                    {
                        var (xml2, remap2, nextAfter) = UniquifyImagesInXml(
                            xml, map, imageTargets.Select(t => (t.Linkage, t.ImageId)).ToList(), nextId);
                        remapped = remap2;
                        nextId = nextAfter;
                        if (!string.Equals(xml, xml2, StringComparison.Ordinal))
                        {
                            xml = xml2;
                            touches++;
                            WarmDefineBitsOpenIndex(xml);
                        }
                    }
                    Phase("uniquify bitmaps");

                    foreach (var t in imageTargets
                                 .Where(x => x.Linkage.EndsWith("_charselect", StringComparison.OrdinalIgnoreCase)))
                    {
                        var cssp = t.Linkage[..^"_charselect".Length] + "_cssp";
                        if (!map.TryGetValue(cssp, out var csspId) || !IsDefineBitsInXml(xml, csspId))
                            continue;
                        if (!map.TryGetValue(t.Linkage, out var csSprite)) continue;
                        var placed = CollectPlacedBitsWithDepth(xml, csSprite);
                        var top = placed.OrderByDescending(p => p.Depth).FirstOrDefault();
                        if (top.Id == 0 || top.Id == csspId) continue;
                        var (retargeted, touched) = RemapHighestDepthBitsUnderSprite(xml, csSprite, csspId);
                        if (touched)
                        {
                            xml = retargeted;
                            remapped[t.Linkage] = csspId.ToString();
                            touches++;
                        }
                    }
                    var companionTouches = 0;
                    foreach (var t in imageTargets)
                    {
                        var imgId = remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId;
                        if (!map.TryGetValue(t.Linkage, out var spriteCharId)
                            || !int.TryParse(imgId, out var primaryId))
                            continue;
                        var companions = FindCompanionBitsIdsInXml(xml, spriteCharId, primaryId);
                        if (companions.Count == 0) continue;
                        var privateIds = new List<string>();
                        foreach (var companionId in companions)
                        {
                            var (xmlNext, newBitsId) = UniquifySingleBitsUnderSprite(
                                xml, spriteCharId, companionId, ref nextId);
                            if (newBitsId == null || string.Equals(xml, xmlNext, StringComparison.Ordinal))
                            {
                                privateIds.Add(companionId.ToString());
                                continue;
                            }
                            xml = xmlNext;
                            privateIds.Add(newBitsId.Value.ToString());
                            companionTouches++;
                            touches++;
                        }
                        companionRemap[t.Linkage] = privateIds;
                    }
                    if (companionTouches > 0)
                    {
                        WarmDefineBitsOpenIndex(xml);
                        progress?.Report($"Uniquified {companionTouches} companion shadow bitmap(s).");
                    }
                    Phase($"companions ({companionTouches})");

                    var (matrixXml, matrixTouches) = NormalizeBitmapMatricesForTargets(
                        xml, map, imageTargets.Select(t => (
                            t.Linkage,
                            remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId)).ToList());
                    if (matrixTouches > 0)
                    {
                        xml = matrixXml;
                        touches += matrixTouches;
                    }

                    var stockLink = imageTargets.Select(t => t.Linkage)
                        .FirstOrDefault(l => l.EndsWith("_stock", StringComparison.OrdinalIgnoreCase)
                                             && !l.EndsWith("_stock0", StringComparison.OrdinalIgnoreCase));
                    if (stockLink != null)
                    {
                        var charId = stockLink[..^"_stock".Length];
                        var (stockXml, stockTouched) = RetargetStockSpriteToStock0InXml(xml, map, charId);
                        if (stockTouched)
                        {
                            xml = stockXml;
                            touches++;
                            progress?.Report($"Retargeted {charId}_stock → {charId}_stock0");
                        }
                    }
                    Phase($"art done ({imageTargets.Count} target(s))");
                    ClearDefineBitsOpenIndex();
                }

                if (touches == 0 && pending.Count == 0 && imageTargets.Count == 0 && labelJobs.Count == 0)
                    return;

                await File.WriteAllTextAsync(
                    xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                Phase("write xml");
                var rebuilt = Path.Combine(work, "rebuilt.swf");
                progress?.Report($"XML → SWF ({Path.GetFileName(swfPath)})…");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, rebuilt, cancellationToken);
                Phase("xml2swf");
                if (!File.Exists(rebuilt))
                    throw new InvalidOperationException("xml2swf failed in host pipeline.");

                // Labels via importText on the rebuilt SWF.
                if (labelJobs.Count > 0 && allTextIds.Count > 0 && !string.IsNullOrWhiteSpace(displayName))
                {
                    var exportDir = Path.Combine(work, "textexport");
                    Directory.CreateDirectory(exportDir);
                    await _jpexs.ExportSelectedAsync(
                        jpexsPath ?? "", rebuilt, exportDir, allTextIds, "text", cancellationToken);

                    var importRoot = Path.Combine(work, "textimport");
                    var textsDir = Path.Combine(importRoot, "texts");
                    Directory.CreateDirectory(textsDir);
                    var written = 0;
                    foreach (var (linkage, textIds) in labelJobs)
                    {
                        var txtFiles = textIds
                            .Select(id => Directory.GetFiles(exportDir, id + ".txt", SearchOption.AllDirectories)
                                .FirstOrDefault())
                            .Where(f => f != null)
                            .Cast<string>()
                            .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : int.MaxValue)
                            .ToList();
                        if (txtFiles.Count == 0) continue;

                        // CSS select + event: single-line only (no RECORDSEPARATOR).
                        var body = BuildSingleLineChipLabel(displayName!) + "\r\n";
                        for (var i = 0; i < txtFiles.Count; i++)
                        {
                            var name = Path.GetFileName(txtFiles[i]);
                            await File.WriteAllTextAsync(
                                Path.Combine(textsDir, name), body, new UTF8Encoding(false), cancellationToken);
                            written++;
                        }
                    }

                    if (written > 0)
                    {
                        var labeled = Path.Combine(work, "labeled.swf");
                        progress?.Report($"importText {written} label(s) → \"{displayName}\"…");
                        await _jpexs.ImportTextAsync(jpexsPath ?? "", rebuilt, labeled, importRoot, cancellationToken);
                        if (File.Exists(labeled))
                            rebuilt = labeled;

                        // importText can reset PlaceObject translateX — re-center single-line select chips.
                        var selectJobs = labelJobs
                            .Where(j => IsCssSelectLinkage(j.Linkage))
                            .ToList();
                        if (selectJobs.Count > 0)
                        {
                            var alignXmlPath = Path.Combine(work, "align.xml");
                            await _jpexs.SwfToXmlAsync(jpexsPath ?? "", rebuilt, alignXmlPath, cancellationToken);
                            var alignXml = await File.ReadAllTextAsync(alignXmlPath, cancellationToken);
                            var alignMap = ParseSymbolClassMapFromXml(alignXml);
                            var label = BuildSingleLineChipLabel(displayName!);
                            var alignTouches = 0;
                            foreach (var (linkage, textIds) in selectJobs)
                            {
                                if (!alignMap.TryGetValue(linkage, out var spriteId)) continue;
                                var ids = FindTextCharacterIdsInSprite(alignXml, spriteId);
                                if (ids.Count == 0) ids = textIds;
                                var (next, n) = ScaleSelectChipTextPlaceObjects(alignXml, spriteId, ids, label);
                                if (n > 0) { alignXml = next; alignTouches += n; }
                            }
                            if (alignTouches > 0)
                            {
                                await File.WriteAllTextAsync(
                                    alignXmlPath, alignXml,
                                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                                var aligned = Path.Combine(work, "aligned.swf");
                                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", alignXmlPath, aligned, cancellationToken);
                                if (File.Exists(aligned))
                                    rebuilt = aligned;
                            }
                        }
                    }
                }

                // Bitmap replace after uniquify.
                if (imageTargets.Count > 0)
                {
                    var fitDir = Path.Combine(work, "fit");
                    Directory.CreateDirectory(fitDir);
                    var replacePairs = new List<(string CharacterIdOrScriptName, string ImportDataFile)>();
                    var replacedIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var t in imageTargets)
                    {
                        var imgId = remapped.TryGetValue(t.Linkage, out var nid) ? nid : t.ImageId;
                        var fitted = Path.Combine(fitDir, imgId + ".png");
                        if (TryGetDefineBitsSize(xml, imgId, out var tw, out var th) && tw > 0 && th > 0)
                            FitArtPngForLinkage(xml, map, t.Linkage, imgId, t.PngPath, fitted, tw, th);
                        else
                            File.Copy(t.PngPath, fitted, overwrite: true);
                        replacePairs.Add((imgId, fitted));
                        replacedIds.Add(imgId);

                        IEnumerable<string> companionIds = companionRemap.TryGetValue(t.Linkage, out var listed)
                            ? listed
                            : Array.Empty<string>();
                        foreach (var cid in companionIds)
                        {
                            if (!replacedIds.Add(cid)) continue;
                            if (!TryGetDefineBitsSize(xml, cid, out var cw, out var ch)) continue;
                            var shadowPng = Path.Combine(fitDir, cid + "-shadow.png");
                            MakeShadowPngFromAlpha(t.PngPath, shadowPng, cw, ch);
                            replacePairs.Add((cid, shadowPng));
                        }
                    }

                    if (replacePairs.Count > 0)
                    {
                        var replaced = Path.Combine(work, "replaced.swf");
                        progress?.Report($"Replacing {replacePairs.Count} image(s)…");
                        await _jpexs.ReplaceManyAsync(jpexsPath ?? "", rebuilt, replaced, replacePairs, cancellationToken);
                        if (!File.Exists(replaced))
                            throw new InvalidOperationException("FFDec -replace failed in host pipeline.");
                        rebuilt = replaced;
                    }
                }

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(rebuilt, swfPath, overwrite: true);
                progress?.Report(
                    $"Host pipeline done: {pending.Count} clone(s), {labelJobs.Count} label(s), {imageTargets.Count} art(s) → {Path.GetFileName(swfPath)}.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        private static bool IsDirectImageLinkageName(string linkage) =>
            linkage.StartsWith("congrats_", StringComparison.OrdinalIgnoreCase)
            || linkage.StartsWith("allstar_congrats_", StringComparison.OrdinalIgnoreCase)
            || linkage.EndsWith("_stock0", StringComparison.OrdinalIgnoreCase)
            || linkage.EndsWith("_cssp", StringComparison.OrdinalIgnoreCase);

        public static bool TryGetDefineBitsSize(string xml, string characterId, out int width, out int height)
        {
            width = height = 0;
            if (!int.TryParse(characterId, out var id)) return false;
            var openIdx = IndexOfDefineBitsTagOpen(xml, id);
            if (openIdx < 0) return false;
            // Never regex/substring the full open tag — zlibBitmapData attrs are multi‑MB.
            var header = GetDefineBitsHeaderSpan(xml, openIdx);
            if (header.IsEmpty) return false;
            var headerStr = header.ToString();
            var wm = Regex.Match(headerStr, @"bitmapWidth\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
            var hm = Regex.Match(headerStr, @"bitmapHeight\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
            if (!wm.Success || !hm.Success) return false;
            width = int.Parse(wm.Groups[1].Value);
            height = int.Parse(hm.Groups[1].Value);
            return width > 0 && height > 0;
        }

        /// <summary>Open-tag prefix before bitmap payload attrs (safe to copy / regex).</summary>
        private static ReadOnlySpan<char> GetDefineBitsHeaderSpan(string xml, int openIdx)
        {
            var limit = Math.Min(xml.Length, openIdx + 800);
            var payload = xml.IndexOf("zlibBitmapData", openIdx, limit - openIdx, StringComparison.OrdinalIgnoreCase);
            if (payload < 0)
                payload = xml.IndexOf("bitmapPixelData", openIdx, limit - openIdx, StringComparison.OrdinalIgnoreCase);
            if (payload < 0)
                payload = xml.IndexOf("jpegData", openIdx, limit - openIdx, StringComparison.OrdinalIgnoreCase);
            if (payload < 0)
            {
                var tagEnd = FindEndOfOpenXmlTag(xml, openIdx);
                if (tagEnd < 0) return default;
                return xml.AsSpan(openIdx, Math.Min(tagEnd - openIdx, 800));
            }
            return xml.AsSpan(openIdx, payload - openIdx);
        }

        /// <summary>
        /// True for CSS chip linkages (<c>{id}_select</c>), not charselect / stage_select.
        /// </summary>
        public static bool IsCssSelectLinkage(string? linkage)
        {
            if (string.IsNullOrWhiteSpace(linkage)) return false;
            return linkage.EndsWith("_select", StringComparison.OrdinalIgnoreCase)
                   && !linkage.EndsWith("_charselect", StringComparison.OrdinalIgnoreCase)
                   && !linkage.EndsWith("_stage_select", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Scale <paramref name="srcPng"/> into <paramref name="targetW"/>×<paramref name="targetH"/>.
        /// <paramref name="cover"/> true = fill (crop); false = contain (letterbox with transparency).
        /// </summary>
        public static void FitPngToSize(
            string srcPng, string destPng, int targetW, int targetH,
            bool cover = false, int biasY = 0)
        {
            FitPngIntoBitmapWindow(srcPng, destPng, targetW, targetH, 0, 0, targetW, targetH, cover, biasY);
        }

        /// <summary>
        /// Cover/contain-fit <paramref name="srcPng"/> into a window on a transparent
        /// <paramref name="bitmapW"/>×<paramref name="bitmapH"/> canvas. Used for Samus-shaped
        /// select chips whose fill matrix only samples a small upper band of the bitmap.
        /// <paramref name="biasY"/> shifts the fitted image (negative = bump up).
        /// </summary>
        public static void FitPngIntoBitmapWindow(
            string srcPng, string destPng,
            int bitmapW, int bitmapH,
            int winX, int winY, int winW, int winH,
            bool cover = true,
            int biasY = 0)
        {
            if (bitmapW <= 0 || bitmapH <= 0) throw new ArgumentOutOfRangeException(nameof(bitmapW));
            winW = Math.Max(1, winW);
            winH = Math.Max(1, winH);
            winX = Math.Clamp(winX, 0, Math.Max(0, bitmapW - 1));
            winY = Math.Clamp(winY, 0, Math.Max(0, bitmapH - 1));
            if (winX + winW > bitmapW) winW = bitmapW - winX;
            if (winY + winH > bitmapH) winH = bitmapH - winY;
            winW = Math.Max(1, winW);
            winH = Math.Max(1, winH);

            using var src = Image.FromFile(srcPng);
            using var dest = new Bitmap(bitmapW, bitmapH, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingMode = CompositingMode.SourceCopy;
                var scale = cover
                    ? Math.Max((double)winW / src.Width, (double)winH / src.Height)
                    : Math.Min((double)winW / src.Width, (double)winH / src.Height);
                var w = Math.Max(1, (int)Math.Round(src.Width * scale));
                var h = Math.Max(1, (int)Math.Round(src.Height * scale));
                var x = winX + (winW - w) / 2;
                var y = winY + (winH - h) / 2 + biasY;
                g.DrawImage(src, new Rectangle(x, y, w, h));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
            dest.Save(destPng, ImageFormat.Png);
        }

        /// <summary>
        /// Visible bitmap-pixel window for a shape fill under <paramref name="spriteId"/> that
        /// references <paramref name="bitsId"/>. Returns false when no usable matrix is found.
        /// </summary>
        public static bool TryComputeBitmapFillWindow(
            string xml, int spriteId, int bitsId, int bitmapW, int bitmapH,
            out int winX, out int winY, out int winW, out int winH)
        {
            winX = winY = 0;
            winW = bitmapW;
            winH = bitmapH;
            if (bitmapW <= 0 || bitmapH <= 0 || spriteId <= 0 || bitsId <= 0)
                return false;

            var shapeIds = new HashSet<int>();
            CollectShapeIdsUnderSprite(xml, spriteId, shapeIds, 0);
            foreach (var shapeId in shapeIds)
            {
                var shapeOpen = FindShapeTagOpen(xml, shapeId);
                if (shapeOpen == null) continue;
                var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                if (shapeEnd <= shapeOpen.Index) continue;
                var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                if (!Regex.IsMatch(shapeBlock, $@"bitmapId\s*=\s*""{bitsId}""", RegexOptions.IgnoreCase))
                    continue;

                if (!TryParseShapeBounds(shapeBlock, out var xMin, out var xMax, out var yMin, out var yMax))
                    continue;
                if (!TryParseBitmapFillMatrix(shapeBlock, bitsId, out var scaleX, out var scaleY, out var tx, out var ty))
                    continue;
                if (Math.Abs(scaleX) < 0.001 || Math.Abs(scaleY) < 0.001)
                    continue;

                // Shape local twips → bitmap pixels: px = (sx - translate) / scale
                var left = (xMin - tx) / scaleX;
                var right = (xMax - tx) / scaleX;
                var top = (yMin - ty) / scaleY;
                var bottom = (yMax - ty) / scaleY;
                if (right < left) (left, right) = (right, left);
                if (bottom < top) (top, bottom) = (bottom, top);

                var x0 = (int)Math.Floor(left);
                var y0 = (int)Math.Floor(top);
                var x1 = (int)Math.Ceiling(right);
                var y1 = (int)Math.Ceiling(bottom);
                x0 = Math.Clamp(x0, 0, bitmapW);
                y0 = Math.Clamp(y0, 0, bitmapH);
                x1 = Math.Clamp(x1, 0, bitmapW);
                y1 = Math.Clamp(y1, 0, bitmapH);
                if (x1 <= x0 || y1 <= y0) continue;

                winX = x0;
                winY = y0;
                winW = x1 - x0;
                winH = y1 - y0;
                return true;
            }
            return false;
        }

        public static bool TryParseShapeBounds(
            string shapeBlock, out double xMin, out double xMax, out double yMin, out double yMax)
        {
            xMin = xMax = yMin = yMax = 0;
            // FFDec emits attrs in varying order (Samus: Xmax,Xmin,Ymax,Ymin).
            var bounds = Regex.Match(shapeBlock, @"<shapeBounds\b([^>]*)/?>", RegexOptions.IgnoreCase);
            if (!bounds.Success)
                bounds = Regex.Match(shapeBlock, @"shapeBounds\b([^>]*)/?>", RegexOptions.IgnoreCase);
            if (!bounds.Success) return false;
            var attrs = bounds.Groups[1].Value;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var xmin = Regex.Match(attrs, @"Xmin\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var xmax = Regex.Match(attrs, @"Xmax\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var ymin = Regex.Match(attrs, @"Ymin\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var ymax = Regex.Match(attrs, @"Ymax\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            return xmin.Success && xmax.Success && ymin.Success && ymax.Success
                   && double.TryParse(xmin.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out xMin)
                   && double.TryParse(xmax.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out xMax)
                   && double.TryParse(ymin.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out yMin)
                   && double.TryParse(ymax.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out yMax);
        }

        public static bool TryParseBitmapFillMatrix(
            string shapeBlock, int bitsId,
            out double scaleX, out double scaleY, out double translateX, out double translateY)
        {
            scaleX = scaleY = 20;
            translateX = translateY = 0;
            // Prefer matrix nested near this bitmapId.
            var m = Regex.Match(
                shapeBlock,
                $@"bitmapId\s*=\s*""{bitsId}""[\s\S]{{0,800}}?<bitmapMatrix\b([^>]*)/?>",
                RegexOptions.IgnoreCase);
            if (!m.Success)
            {
                m = Regex.Match(
                    shapeBlock,
                    $@"<bitmapMatrix\b([^>]*)/?>[\s\S]{{0,400}}?bitmapId\s*=\s*""{bitsId}""",
                    RegexOptions.IgnoreCase);
            }
            if (!m.Success) return false;
            var attrs = m.Groups[1].Value;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var sx = Regex.Match(attrs, @"scaleX\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var sy = Regex.Match(attrs, @"scaleY\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var tx = Regex.Match(attrs, @"translateX\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var ty = Regex.Match(attrs, @"translateY\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (sx.Success)
                double.TryParse(sx.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out scaleX);
            if (sy.Success)
                double.TryParse(sy.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out scaleY);
            if (tx.Success)
                double.TryParse(tx.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out translateX);
            if (ty.Success)
                double.TryParse(ty.Groups[1].Value, System.Globalization.NumberStyles.Float, inv, out translateY);
            return true;
        }

        /// <summary>Black silhouette from source alpha, sized to target (for charselect shadows).</summary>
        public static void MakeShadowPngFromAlpha(string srcPng, string destPng, int targetW, int targetH)
        {
            var tmp = Path.Combine(Path.GetTempPath(), "ssf2mm-shadow-" + Guid.NewGuid().ToString("N") + ".png");
            try
            {
                FitPngToSize(srcPng, tmp, targetW, targetH);
                using var fitted = new Bitmap(tmp);
                using var dest = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
                for (var y = 0; y < targetH; y++)
                for (var x = 0; x < targetW; x++)
                {
                    var c = fitted.GetPixel(x, y);
                    dest.SetPixel(x, y, c.A < 16 ? Color.FromArgb(0, 0, 0, 0) : Color.FromArgb(c.A, 0, 0, 0));
                }
                Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
                dest.Save(destPng, ImageFormat.Png);
            }
            finally
            {
                try { File.Delete(tmp); } catch { /* ignore */ }
            }
        }

        private static bool IsDefineBitsInXml(string xml, int characterId) =>
            IndexOfDefineBitsTagOpen(xml, characterId) >= 0;

        private static bool IsDefineTextInXml(string xml, int characterId) =>
            FindCharacterTagOpen(xml, characterId, "DefineEditText") != null
            || FindCharacterTagOpen(xml, characterId, "DefineText") != null;

        /// <summary>
        /// Finds an &lt;item …&gt; open tag for a character id whose type starts with
        /// <paramref name="typePrefix"/> (e.g. DefineBits, DefineEditText).
        /// </summary>
        private static Match? FindCharacterTagOpen(string xml, int characterId, string typePrefix)
        {
            // DefineBits* open tags carry multi‑MB payload attrs — never use [^>]* against them.
            if (typePrefix.StartsWith("DefineBits", StringComparison.OrdinalIgnoreCase))
            {
                var openIdx = IndexOfDefineBitsTagOpen(xml, characterId);
                if (openIdx < 0) return null;
                var header = GetDefineBitsHeaderSpan(xml, openIdx);
                if (header.IsEmpty) return null;
                // Match the short header in-place so Match.Index is the real open offset.
                return Regex.Match(xml, Regex.Escape(header.ToString()));
            }

            // EditText/Text open tags often have 300–600 chars of attrs after characterID.
            // Keep a hard cap (not unbounded [^>]*) so huge DefineShape/etc. lines can't explode.
            const int afterId = 800;
            var a = Regex.Match(
                xml,
                $@"<item\b[^>]{{0,200}}?type\s*=\s*""{Regex.Escape(typePrefix)}[^""]*""[^>]{{0,200}}?characterId\s*=\s*""{characterId}""[^>]{{0,{afterId}}}?>",
                RegexOptions.IgnoreCase);
            if (a.Success) return a;
            a = Regex.Match(
                xml,
                $@"<item\b[^>]{{0,200}}?type\s*=\s*""{Regex.Escape(typePrefix)}[^""]*""[^>]{{0,200}}?characterID\s*=\s*""{characterId}""[^>]{{0,{afterId}}}?>",
                RegexOptions.IgnoreCase);
            if (a.Success) return a;
            var b = Regex.Match(
                xml,
                $@"<item\b[^>]{{0,200}}?characterId\s*=\s*""{characterId}""[^>]{{0,200}}?type\s*=\s*""{Regex.Escape(typePrefix)}[^""]*""[^>]{{0,{afterId}}}?>",
                RegexOptions.IgnoreCase);
            if (b.Success) return b;
            b = Regex.Match(
                xml,
                $@"<item\b[^>]{{0,200}}?characterID\s*=\s*""{characterId}""[^>]{{0,200}}?type\s*=\s*""{Regex.Escape(typePrefix)}[^""]*""[^>]{{0,{afterId}}}?>",
                RegexOptions.IgnoreCase);
            return b.Success ? b : null;
        }

        /// <summary>
        /// Rewrites DefineText / DefineEditText on a cloned select chip via FFDec text export/import
        /// so the CSS name bar shows the wizard display name (not the donor label).
        /// </summary>
        public Task RewriteSelectChipLabelsAsync(
            string swfPath,
            string selectLinkage,
            string displayName,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            RewriteLinkageLabelsAsync(
                swfPath, selectLinkage, displayName, jpexsPath, progress, cancellationToken);

        /// <summary>
        /// Rewrites DefineText / DefineEditText nested under any linkage sprite (CSS chip, event PA, …).
        /// </summary>
        public Task RewriteLinkageLabelsAsync(
            string swfPath,
            string linkage,
            string displayName,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default) =>
            RewriteLinkageLabelsBatchAsync(
                swfPath, new[] { linkage }, displayName, jpexsPath, progress, cancellationToken);

        /// <summary>
        /// Fast multi-label rewrite for large Crazy CSS grids: one swf2xml, one text-tag index,
        /// one export/import. Skips per-chip PlaceObject scaling (O(n²) on big hosts).
        /// </summary>
        public async Task RewriteLinkageLabelsByMapAsync(
            string swfPath,
            IReadOnlyDictionary<string, string> linkageToLabel,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (linkageToLabel == null || linkageToLabel.Count == 0) return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-linktext-fast-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Fast label rewrite ({linkageToLabel.Count} linkage(s))…");
                var map = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                var xmlPath = Path.Combine(work, "link.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);

                // One pass: every DefineText / DefineEditText characterId.
                var textTagIds = new HashSet<int>();
                foreach (Match m in Regex.Matches(
                             xml,
                             @"<item\b(?=[^>]*type\s*=\s*""Define(?:Edit)?Text)[^>]*characterId\s*=\s*""(\d+)""",
                             RegexOptions.IgnoreCase))
                {
                    if (int.TryParse(m.Groups[1].Value, out var id)) textTagIds.Add(id);
                }
                foreach (Match m in Regex.Matches(
                             xml,
                             @"<item\b(?=[^>]*characterId\s*=\s*""(\d+)"")[^>]*type\s*=\s*""Define(?:Edit)?Text",
                             RegexOptions.IgnoreCase))
                {
                    if (int.TryParse(m.Groups[1].Value, out var id)) textTagIds.Add(id);
                }

                var neededSprites = new Dictionary<int, string>(linkageToLabel.Count); // spriteId -> linkage
                foreach (var (linkage, _) in linkageToLabel)
                {
                    if (!map.TryGetValue(linkage, out var sid)) continue;
                    neededSprites[sid] = linkage;
                }

                // One pass over DefineSprite opens; only parse needed crazy chips.
                var linkageTextIds = new Dictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);
                foreach (Match m in Regex.Matches(
                             xml,
                             @"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""(\d+)""[^>]*>",
                             RegexOptions.IgnoreCase))
                {
                    if (!int.TryParse(m.Groups[1].Value, out var sid)) continue;
                    if (!neededSprites.TryGetValue(sid, out var linkage)) continue;
                    var end = FindMatchingItemClose(xml, m.Index);
                    if (end <= m.Index) continue;
                    var block = xml.Substring(m.Index, end - m.Index);
                    var ids = new List<int>();
                    foreach (Match cm in Regex.Matches(
                                 block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                    {
                        if (!int.TryParse(cm.Groups[1].Value, out var cid) || cid == sid) continue;
                        if (textTagIds.Contains(cid) && !ids.Contains(cid)) ids.Add(cid);
                    }
                    if (ids.Count > 0) linkageTextIds[linkage] = ids;
                }

                // Alternate attribute order.
                if (linkageTextIds.Count < neededSprites.Count)
                {
                    foreach (Match m in Regex.Matches(
                                 xml,
                                 @"<item\b[^>]*spriteId\s*=\s*""(\d+)""[^>]*type\s*=\s*""DefineSpriteTag""[^>]*>",
                                 RegexOptions.IgnoreCase))
                    {
                        if (!int.TryParse(m.Groups[1].Value, out var sid)) continue;
                        if (!neededSprites.TryGetValue(sid, out var linkage)) continue;
                        if (linkageTextIds.ContainsKey(linkage)) continue;
                        var end = FindMatchingItemClose(xml, m.Index);
                        if (end <= m.Index) continue;
                        var block = xml.Substring(m.Index, end - m.Index);
                        var ids = new List<int>();
                        foreach (Match cm in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                        {
                            if (!int.TryParse(cm.Groups[1].Value, out var cid) || cid == sid) continue;
                            if (textTagIds.Contains(cid) && !ids.Contains(cid)) ids.Add(cid);
                        }
                        if (ids.Count > 0) linkageTextIds[linkage] = ids;
                    }
                }

                var allTextIds = linkageTextIds.Values.SelectMany(v => v).Distinct().ToList();
                if (allTextIds.Count == 0)
                {
                    progress?.Report("No text tags found on target linkages.");
                    return;
                }

                progress?.Report($"Exporting {allTextIds.Count} text tag(s)…");
                var exportDir = Path.Combine(work, "textexport");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportSelectedAsync(
                    jpexsPath ?? "", swfPath, exportDir, allTextIds, "text", cancellationToken);

                var importRoot = Path.Combine(work, "textimport");
                var textsDir = Path.Combine(importRoot, "texts");
                Directory.CreateDirectory(textsDir);
                var written = 0;
                foreach (var (linkage, textIds) in linkageTextIds)
                {
                    if (!linkageToLabel.TryGetValue(linkage, out var rawLabel) || string.IsNullOrWhiteSpace(rawLabel))
                        continue;
                    // DK chips are two TEXTRECORDs (DONKEY/KONG). A single-line import leaves
                    // "KONG" on line 2 — clear it with an empty second record. Also keeps
                    // PREDATOR from overflowing into a fake "PREDATOR KONG" read.
                    var line = BuildSingleLineChipLabel(rawLabel);
                    var body = line + "\r\n--- RECORDSEPARATOR ---\r\n\r\n";
                    foreach (var id in textIds)
                    {
                        var src = Directory.GetFiles(exportDir, id + ".txt", SearchOption.AllDirectories)
                            .FirstOrDefault();
                        if (src == null) continue;
                        await File.WriteAllTextAsync(
                            Path.Combine(textsDir, Path.GetFileName(src)), body,
                            new UTF8Encoding(false), cancellationToken);
                        written++;
                    }
                }

                if (written == 0)
                {
                    progress?.Report("No exported .txt files to rewrite.");
                    return;
                }

                var outSwf = Path.Combine(work, "labeled.swf");
                progress?.Report($"importText {written} file(s)…");
                await _jpexs.ImportTextAsync(jpexsPath ?? "", swfPath, outSwf, importRoot, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("FFDec importText failed for fast label rewrite.");

                // Fit long labels (PREDATOR) into the name bar — one XML pass for all chips.
                progress?.Report("Scaling chip name bars…");
                var alignXmlPath = Path.Combine(work, "align.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", outSwf, alignXmlPath, cancellationToken);
                var alignXml = await File.ReadAllTextAsync(alignXmlPath, cancellationToken);
                var alignMap = ParseSymbolClassMapFromXml(alignXml);
                var alignTouches = BulkScaleSelectChipLabels(
                    ref alignXml, alignMap, linkageToLabel, linkageTextIds);
                if (alignTouches > 0)
                {
                    await File.WriteAllTextAsync(
                        alignXmlPath, alignXml,
                        new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                    var aligned = Path.Combine(work, "aligned.swf");
                    await _jpexs.XmlToSwfAsync(jpexsPath ?? "", alignXmlPath, aligned, cancellationToken);
                    if (File.Exists(aligned)) outSwf = aligned;
                    progress?.Report($"Scaled {alignTouches} name-bar matrix(es).");
                }

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(outSwf, swfPath, overwrite: true);
                progress?.Report($"Fast labels done ({written} text(s)).");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// One forward scan: scale/center CSS name-bar PlaceObjects for many linkages.
        /// Avoids per-chip full-XML FindSpriteTagOpen (O(n²) on large Crazy CSS hosts).
        /// </summary>
        private static int BulkScaleSelectChipLabels(
            ref string xml,
            IReadOnlyDictionary<string, int> symbolMap,
            IReadOnlyDictionary<string, string> linkageToLabel,
            IReadOnlyDictionary<string, List<int>> linkageTextIds)
        {
            var spriteToJob = new Dictionary<int, (string Label, HashSet<int> TextIds)>();
            foreach (var (linkage, label) in linkageToLabel)
            {
                if (!symbolMap.TryGetValue(linkage, out var sid)) continue;
                if (!linkageTextIds.TryGetValue(linkage, out var tids) || tids.Count == 0) continue;
                spriteToJob[sid] = (BuildSingleLineChipLabel(label), new HashSet<int>(tids));
            }
            if (spriteToJob.Count == 0) return 0;

            var replacements = new List<(int Start, int End, string Block)>();
            var touches = 0;
            foreach (Match m in Regex.Matches(
                         xml,
                         @"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""(\d+)""[^>]*>",
                         RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var sid)) continue;
                if (!spriteToJob.TryGetValue(sid, out var job)) continue;
                var end = FindMatchingItemClose(xml, m.Index);
                if (end <= m.Index) continue;
                var block = xml.Substring(m.Index, end - m.Index);
                var (next, n) = ScaleSelectChipTextPlaceObjectsInBlock(block, job.TextIds, job.Label);
                if (n <= 0) continue;
                replacements.Add((m.Index, end, next));
                touches += n;
            }

            // Apply from end so earlier indices stay valid.
            for (var i = replacements.Count - 1; i >= 0; i--)
            {
                var (start, end, block) = replacements[i];
                xml = xml.Substring(0, start) + block + xml.Substring(end);
            }
            return touches;
        }

        /// <summary>Scale/center text PlaceObjects inside an already-extracted sprite XML block.</summary>
        private static (string Block, int Touches) ScaleSelectChipTextPlaceObjectsInBlock(
            string block, HashSet<int> textIds, string labelText)
        {
            if (textIds.Count == 0) return (block, 0);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            // Slightly tighter than the stock estimate — long labels like PREDATOR were
            // clipping the right edge with empty space on the left.
            var scaleX = ComputeSelectChipTextScaleX(labelText, advanceSum: 0) * 0.88;
            scaleX = Math.Max(0.38, scaleX);
            var scaleY = ComputeSelectChipTextScaleY();
            // Extra left nudge (twips) so the painted run sits in the bar, not past the right edge.
            var leftNudge = 70 + Math.Max(0, (labelText?.Trim().Length ?? 0) - 6) * 12;
            var tx = Math.Round(ComputeSelectChipTextTranslateX(labelText ?? "", scaleX) - leftNudge);
            var sx = scaleX.ToString("0.########", inv);
            var sy = scaleY.ToString("0.########", inv);
            var txStr = tx.ToString("0", inv);
            const string tyStr = "-408";
            var touches = 0;

            var next = Regex.Replace(
                block,
                @"<item\b[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>[\s\S]*?</item>",
                m =>
                {
                    var tag = m.Value;
                    var cm = Regex.Match(tag, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                    if (!cm.Success || !int.TryParse(cm.Groups[1].Value, out var cid) || !textIds.Contains(cid))
                        return tag;
                    var updated = tag;
                    if (Regex.IsMatch(updated, @"scaleX\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"scaleX\s*=\s*""[^""]*""", $"scaleX=\"{sx}\"",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(updated, @"scaleY\s*=\s*""[^""]*""", $"scaleY=\"{sy}\"",
                            RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 hasScale=\"true\" scaleX=\"{sx}\" scaleY=\"{sy}\"/>",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(
                            updated,
                            @"hasScale\s*=\s*""false""",
                            "hasScale=\"true\"",
                            RegexOptions.IgnoreCase);
                    }
                    if (Regex.IsMatch(updated, @"translateX\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"translateX\s*=\s*""[^""]*""",
                            $"translateX=\"{txStr}\"", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 translateX=\"{txStr}\"/>",
                            RegexOptions.IgnoreCase);
                    }
                    if (Regex.IsMatch(updated, @"translateY\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"translateY\s*=\s*""[^""]*""",
                            $"translateY=\"{tyStr}\"", RegexOptions.IgnoreCase);
                    }
                    if (!string.Equals(tag, updated, StringComparison.Ordinal))
                        touches++;
                    return updated;
                },
                RegexOptions.IgnoreCase);

            return (next, touches);
        }

        /// <summary>
        /// One symbolClass + swf2xml + text export/import for many linkages on the same SWF.
        /// </summary>
        public async Task RewriteLinkageLabelsBatchAsync(
            string swfPath,
            IReadOnlyList<string> linkages,
            string displayName,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool scaleSelectLayout = true)
        {
            if (string.IsNullOrWhiteSpace(displayName) || linkages == null || linkages.Count == 0)
                return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-linktext-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var map = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                var xmlPath = Path.Combine(work, "link.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);

                var allTextIds = new List<int>();
                var linkageTextFiles = new List<(string Linkage, List<int> TextIds)>();
                foreach (var linkage in linkages.Distinct(StringComparer.OrdinalIgnoreCase))
                {
                    if (!map.TryGetValue(linkage, out var spriteId))
                    {
                        progress?.Report($"Skip label: {linkage} not found.");
                        continue;
                    }
                    var textIds = FindTextCharacterIdsInSprite(xml, spriteId);
                    if (textIds.Count == 0)
                    {
                        progress?.Report($"No text tags on {linkage}.");
                        continue;
                    }
                    linkageTextFiles.Add((linkage, textIds));
                    foreach (var id in textIds)
                        if (!allTextIds.Contains(id)) allTextIds.Add(id);
                }

                if (allTextIds.Count == 0) return;

                // CSS select: Samus-style single-line layout. Skip for huge Crazy CSS grids —
                // per-chip ScaleSelectChipTextPlaceObjects on multi-MB XML is O(n²).
                var xmlTouches = 0;
                if (scaleSelectLayout)
                {
                    foreach (var (linkage, textIds) in linkageTextFiles)
                    {
                        if (!IsCssSelectLinkage(linkage)) continue;
                        if (!map.TryGetValue(linkage, out var spriteId)) continue;

                        var label = BuildSingleLineChipLabel(displayName!);
                        var (xmlScaled, n) = ScaleSelectChipTextPlaceObjects(xml, spriteId, textIds, label);
                        if (n > 0) { xml = xmlScaled; xmlTouches += n; }
                    }
                }
                if (xmlTouches > 0)
                {
                    await File.WriteAllTextAsync(
                        xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                    var scaledSwf = Path.Combine(work, "text-scaled.swf");
                    await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, scaledSwf, cancellationToken);
                    if (File.Exists(scaledSwf))
                    {
                        var bakScale = swfPath + ".ssf2mm.bak";
                        if (!File.Exists(bakScale))
                            File.Copy(swfPath, bakScale, overwrite: false);
                        File.Copy(scaledSwf, swfPath, overwrite: true);
                        progress?.Report($"Prepared select chip text layout ({xmlTouches} touch(es)).");
                    }
                }

                var exportDir = Path.Combine(work, "textexport");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportSelectedAsync(
                    jpexsPath ?? "", swfPath, exportDir, allTextIds, "text", cancellationToken);

                var importRoot = Path.Combine(work, "textimport");
                var textsDir = Path.Combine(importRoot, "texts");
                Directory.CreateDirectory(textsDir);
                var written = 0;

                foreach (var (linkage, textIds) in linkageTextFiles)
                {
                    var txtFiles = textIds
                        .Select(id => Directory.GetFiles(exportDir, id + ".txt", SearchOption.AllDirectories)
                            .FirstOrDefault())
                        .Where(f => f != null)
                        .Cast<string>()
                        .OrderBy(f => int.TryParse(Path.GetFileNameWithoutExtension(f), out var n) ? n : int.MaxValue)
                        .ToList();
                    if (txtFiles.Count == 0) continue;

                    var body = BuildSingleLineChipLabel(displayName!) + "\r\n";
                    for (var i = 0; i < txtFiles.Count; i++)
                    {
                        var name = Path.GetFileName(txtFiles[i]);
                        await File.WriteAllTextAsync(
                            Path.Combine(textsDir, name), body, new UTF8Encoding(false), cancellationToken);
                        written++;
                    }
                }

                if (written == 0)
                {
                    progress?.Report("No exported .txt for linkage labels.");
                    return;
                }

                var outSwf = Path.Combine(work, "labeled.swf");
                progress?.Report($"importText {written} label(s) → \"{displayName}\"…");
                await _jpexs.ImportTextAsync(jpexsPath ?? "", swfPath, outSwf, importRoot, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("FFDec importText failed for linkage labels.");

                // Re-center after importText (can reset translateX).
                var selectJobs = linkageTextFiles
                    .Where(j => IsCssSelectLinkage(j.Linkage))
                    .ToList();
                if (scaleSelectLayout && selectJobs.Count > 0)
                {
                    var alignXmlPath = Path.Combine(work, "align.xml");
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", outSwf, alignXmlPath, cancellationToken);
                    var alignXml = await File.ReadAllTextAsync(alignXmlPath, cancellationToken);
                    var alignMap = ParseSymbolClassMapFromXml(alignXml);
                    var label = BuildSingleLineChipLabel(displayName!);
                    var alignTouches = 0;
                    foreach (var (linkage, textIds) in selectJobs)
                    {
                        if (!alignMap.TryGetValue(linkage, out var spriteId)) continue;
                        var ids = FindTextCharacterIdsInSprite(alignXml, spriteId);
                        if (ids.Count == 0) ids = textIds;
                        var (next, n) = ScaleSelectChipTextPlaceObjects(alignXml, spriteId, ids, label);
                        if (n > 0) { alignXml = next; alignTouches += n; }
                    }
                    if (alignTouches > 0)
                    {
                        await File.WriteAllTextAsync(
                            alignXmlPath, alignXml,
                            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                        var aligned = Path.Combine(work, "aligned.swf");
                        await _jpexs.XmlToSwfAsync(jpexsPath ?? "", alignXmlPath, aligned, cancellationToken);
                        if (File.Exists(aligned))
                            outSwf = aligned;
                    }
                }

                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(swfPath, bak, overwrite: false);
                File.Copy(outSwf, swfPath, overwrite: true);
                progress?.Report($"Set label(s) → \"{displayName}\".");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Chip / nameplate lines. "Clone Dedede" → CLONE, DEDEDE.
        /// 3+ words use first/last so the bar fits ("Clone Chibi Robo" → CLONE, ROBO).
        /// </summary>
        public static string[] GetChipLabelLines(string displayName)
        {
            var trimmed = (displayName ?? "").Trim();
            if (string.IsNullOrEmpty(trimmed))
                return Array.Empty<string>();

            var words = trimmed.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (words.Length == 1)
                return new[] { words[0].ToUpperInvariant() };

            var line1 = words[0].ToUpperInvariant();
            var line2 = words.Length == 2
                ? words[1].ToUpperInvariant()
                : words[^1].ToUpperInvariant();
            return new[] { line1, line2 };
        }

        /// <summary>
        /// Builds FFDec text import body. Two-line CSS chips use RECORDSEPARATOR.
        /// </summary>
        public static string BuildChipLabelTextFile(string displayName)
        {
            var lines = GetChipLabelLines(displayName);
            if (lines.Length == 0) return "";
            if (lines.Length == 1) return lines[0] + "\r\n";
            return lines[0] + "\r\n--- RECORDSEPARATOR ---\r\n" + lines[1] + "\r\n";
        }

        /// <summary>
        /// Single-line chip/nameplate (Samus / Chibi-Robo shaped). Never emits RECORDSEPARATOR.
        /// Hyphenates when the spaced form is longer than 10 characters.
        /// </summary>
        public static string BuildSingleLineChipLabel(string displayName)
        {
            var lines = GetChipLabelLines(displayName);
            if (lines.Length == 0) return "";
            if (lines.Length == 1) return lines[0];
            var spaced = lines[0] + " " + lines[1];
            if (spaced.Length <= 10) return spaced;
            return lines[0] + "-" + lines[1];
        }

        // Stock CSS name-bar calibration (samus_select / ganondorf_select):
        // both center glyphs at local ≈407; PlaceObject TX ≈ -407*scaleX; scaleY stays ~0.8
        // while scaleX compresses so the painted width matches Ganon's bar (~778 twips).
        private const double SelectChipLocalCenter = 407;
        private const double SelectChipRefAdvanceSum = 1342; // GANONDORF glyph advances
        private const double SelectChipRefScaleX = 0.58;
        private const double SelectChipScaleY = 0.8;
        private const double SelectChipBarWidthTwips = SelectChipRefAdvanceSum * SelectChipRefScaleX;
        private const double SelectChipTwipsPerGlyphEstimate = 149; // Ganon average

        /// <summary>
        /// Horizontal PlaceObject scale — stretch/compress to fill the Ganon bar width.
        /// Prefer real glyph advance sums when available (importText leaves donor bounds).
        /// </summary>
        public static double ComputeSelectChipTextScale(
            string label, double currentScale = 0.8, double maxChars = 9.0,
            double minScale = 0.42)
        {
            _ = currentScale;
            _ = maxChars;
            return ComputeSelectChipTextScaleX(label, advanceSum: 0, minScale);
        }

        /// <summary>scaleX that paints <paramref name="advanceSum"/> (or an estimate) into the bar.</summary>
        public static double ComputeSelectChipTextScaleX(
            string label, double advanceSum = 0, double minScale = 0.42)
        {
            var width = advanceSum > 0
                ? advanceSum
                : Math.Max(1, (label ?? "").Trim().Length) * SelectChipTwipsPerGlyphEstimate;
            if (width <= 0) return SelectChipScaleY;
            return Math.Clamp(SelectChipBarWidthTwips / width, minScale, SelectChipScaleY);
        }

        /// <summary>Vertical scale — stock chips keep ~0.8 and stretch X independently.</summary>
        public static double ComputeSelectChipTextScaleY() => SelectChipScaleY;

        /// <summary>
        /// PlaceObject translateX for a DefineText recentered at <see cref="SelectChipLocalCenter"/>.
        /// </summary>
        public static double ComputeSelectChipTextTranslateX(
            string label, double scale, double twipsPerGlyph = SelectChipTwipsPerGlyphEstimate)
        {
            if (scale <= 0) scale = SelectChipRefScaleX;
            return -SelectChipLocalCenter * scale;
        }

        /// <summary>Sum of glyphAdvance values inside a DefineText tag body.</summary>
        public static int SumGlyphAdvancesInDefineText(string defineTextXml)
        {
            var sum = 0;
            foreach (Match m in Regex.Matches(
                         defineTextXml ?? "",
                         @"glyphAdvance\s*=\s*""(-?\d+)""",
                         RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[1].Value, out var adv))
                    sum += adv;
            }
            return sum;
        }

        /// <summary>
        /// Recenters DefineText like stock chips: textMatrix.TX = localCenter - advanceSum/2,
        /// and expands textBounds so long labels aren't clipped to the donor (Samus) box.
        /// Without this, importText leaves left-aligned overflow in a short name's bounds.
        /// </summary>
        public static (string Xml, int Touches) RecenterSelectChipDefineText(string xml, int textId)
        {
            var open = FindCharacterTagOpen(xml, textId, "DefineText");
            if (open == null) return (xml, 0);
            var close = FindMatchingItemClose(xml, open.Index);
            if (close <= open.Index) return (xml, 0);
            var tag = xml.Substring(open.Index, close - open.Index);
            var advanceSum = SumGlyphAdvancesInDefineText(tag);
            if (advanceSum <= 0) return (xml, 0);

            var tx = (int)Math.Round(SelectChipLocalCenter - advanceSum / 2.0);
            var xmin = tx - 10;
            var xmax = tx + advanceSum + 220;
            var next = tag;

            if (Regex.IsMatch(next, @"<textMatrix\b[^>]*>", RegexOptions.IgnoreCase))
            {
                next = Regex.Replace(
                    next,
                    @"(<textMatrix\b[^>]*\btranslateX\s*=\s*"")(-?\d+)("")",
                    m => m.Groups[1].Value + tx + m.Groups[3].Value,
                    RegexOptions.IgnoreCase);
                // Self-closing form without translateX
                if (!Regex.IsMatch(next, @"<textMatrix\b[^>]*\btranslateX\s*=", RegexOptions.IgnoreCase))
                {
                    next = Regex.Replace(
                        next,
                        @"<textMatrix\b([^>]*)/>",
                        m => "<textMatrix" + m.Groups[1].Value + $" translateX=\"{tx}\"/>",
                        RegexOptions.IgnoreCase);
                }
            }

            if (Regex.IsMatch(next, @"<textBounds\b", RegexOptions.IgnoreCase))
            {
                next = Regex.Replace(
                    next,
                    @"(<textBounds\b[^>]*\bXmin\s*=\s*"")(-?\d+)("")",
                    m => m.Groups[1].Value + xmin + m.Groups[3].Value,
                    RegexOptions.IgnoreCase);
                next = Regex.Replace(
                    next,
                    @"(<textBounds\b[^>]*\bXmax\s*=\s*"")(-?\d+)("")",
                    m => m.Groups[1].Value + xmax + m.Groups[3].Value,
                    RegexOptions.IgnoreCase);
            }

            if (string.Equals(tag, next, StringComparison.Ordinal)) return (xml, 0);
            return (xml.Substring(0, open.Index) + next + xml.Substring(close), 1);
        }

        /// <summary>
        /// Grows DefineText bounds to Donkey Kong chip height so a second RECORDSEPARATOR line fits.
        /// </summary>
        public static (string Xml, int Touches) ExpandDefineTextBoundsForTwoLineChip(string xml, int textId)
        {
            const int minYmax = 387; // donkeykong_select textBounds Ymax
            var open = FindCharacterTagOpen(xml, textId, "DefineText");
            if (open == null) return (xml, 0);
            var close = FindMatchingItemClose(xml, open.Index);
            if (close <= open.Index) return (xml, 0);
            var tag = xml.Substring(open.Index, close - open.Index);
            var ym = Regex.Match(tag, @"Ymax\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
            if (!ym.Success) return (xml, 0);
            if (!int.TryParse(ym.Groups[1].Value, out var ymax) || ymax >= minYmax)
                return (xml, 0);
            var next = Regex.Replace(tag, @"Ymax\s*=\s*""\d+""", $"Ymax=\"{minYmax}\"", RegexOptions.IgnoreCase);
            // Also stretch RECT nbits if needed — FFDec usually recomputes on xml2swf.
            if (string.Equals(tag, next, StringComparison.Ordinal)) return (xml, 0);
            return (xml.Substring(0, open.Index) + next + xml.Substring(close), 1);
        }

        /// <summary>
        /// Ensures a second TEXTRECORD (DK-style yOffset≈360) so importText RECORDSEPARATOR
        /// has a landing slot. Samus donor text is a single record.
        /// </summary>
        public static (string Xml, int Touches) EnsureSecondTextRecordForTwoLineChip(
            string xml, int textId, string? displayName = null)
        {
            var open = FindCharacterTagOpen(xml, textId, "DefineText");
            if (open == null) return (xml, 0);
            var close = FindMatchingItemClose(xml, open.Index);
            if (close <= open.Index) return (xml, 0);
            var tag = xml.Substring(open.Index, close - open.Index);
            var recordCount = Regex.Matches(tag, @"type\s*=\s*""TEXTRECORD""", RegexOptions.IgnoreCase).Count;
            if (recordCount >= 2) return (xml, 0);

            // Must set xOffset: without it the pen continues after line 1 (SAMUS after E in CLONE).
            // Absolute x resets to the text origin; equal-length lines use 0; shorter line 2
            // is nudged right to sit under line 1 (DK KONG ≈ +140 under DONKEY).
            var xOffset = ComputeSecondLineChipXOffset(displayName);
            var secondRecord =
                "\n          <item type=\"TEXTRECORD\" styleFlagsHasColor=\"false\" styleFlagsHasFont=\"false\" " +
                $"styleFlagsHasXOffset=\"true\" styleFlagsHasYOffset=\"true\" xOffset=\"{xOffset}\" yOffset=\"360\">\n" +
                "            <glyphEntries>\n            </glyphEntries>\n" +
                "          </item>";

            var recordsClose = tag.LastIndexOf("</textRecords>", StringComparison.OrdinalIgnoreCase);
            if (recordsClose < 0)
            {
                // Some dumps nest records without a wrapper — append before DefineText close.
                var insertAt = tag.LastIndexOf("</item>", StringComparison.OrdinalIgnoreCase);
                if (insertAt < 0) return (xml, 0);
                var next = tag.Insert(insertAt, secondRecord + "\n        ");
                return (xml.Substring(0, open.Index) + next + xml.Substring(close), 1);
            }

            var withSecond = tag.Insert(recordsClose, secondRecord + "\n        ");
            return (xml.Substring(0, open.Index) + withSecond + xml.Substring(close), 1);
        }

        /// <summary>
        /// Twips X for line-2 TEXTRECORD so it stacks under line 1 (0 when lengths match).
        /// </summary>
        public static int ComputeSecondLineChipXOffset(string? displayName)
        {
            var lines = GetChipLabelLines(displayName ?? "");
            if (lines.Length < 2) return 0;
            // ~70 twips/glyph at textHeight 220 — matches DK's ~140 for 2-glyph shorter line 2.
            const int twipsPerGlyph = 70;
            return (lines[0].Length - lines[1].Length) * twipsPerGlyph / 2;
        }

        /// <summary>
        /// Two-line select PlaceObject: scale ≈ 0.65 and translateY ≈ -435 so both lines
        /// stay inside the black header (DK is 0.76/−452; Samus donor was ~0.8/−408).
        /// </summary>
        public static (string Xml, int Touches) AlignSelectChipTextPlaceObjectForTwoLine(
            string xml, int spriteId, IReadOnlyList<int> textIds,
            double forceScale = 0.65, double translateY = -435)
        {
            if (textIds == null || textIds.Count == 0) return (xml, 0);
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, 0);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, 0);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            var idSet = new HashSet<int>(textIds);
            var touches = 0;
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var scaleStr = forceScale.ToString("0.########", inv);
            var tyStr = translateY.ToString("0.########", inv);

            var nextBlock = Regex.Replace(
                block,
                @"<item\b[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>[\s\S]*?</item>",
                m =>
                {
                    var tag = m.Value;
                    var cm = Regex.Match(tag, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                    if (!cm.Success || !int.TryParse(cm.Groups[1].Value, out var cid) || !idSet.Contains(cid))
                        return tag;
                    var updated = tag;
                    if (Regex.IsMatch(updated, @"scaleX\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"scaleX\s*=\s*""[^""]*""", $"scaleX=\"{scaleStr}\"",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(updated, @"scaleY\s*=\s*""[^""]*""", $"scaleY=\"{scaleStr}\"",
                            RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 hasScale=\"true\" scaleX=\"{scaleStr}\" scaleY=\"{scaleStr}\"/>",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(
                            updated,
                            @"hasScale\s*=\s*""false""",
                            "hasScale=\"true\"",
                            RegexOptions.IgnoreCase);
                    }
                    if (Regex.IsMatch(updated, @"translateY\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"translateY\s*=\s*""[^""]*""",
                            $"translateY=\"{tyStr}\"", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 translateY=\"{tyStr}\"/>",
                            RegexOptions.IgnoreCase);
                    }
                    if (!string.Equals(tag, updated, StringComparison.Ordinal))
                        touches++;
                    return updated;
                },
                RegexOptions.IgnoreCase);

            if (touches == 0) return (xml, 0);
            xml = xml.Substring(0, spriteOpen.Index) + nextBlock + xml.Substring(spriteEnd);
            return (xml, touches);
        }

        /// <summary>
        /// True for PA-style portraits that should cover-fit (charselect, cssp, classic, trophy, allstar, event).
        /// </summary>
        public static bool IsPaCoverFitLinkage(string linkage)
        {
            if (string.IsNullOrWhiteSpace(linkage)) return false;
            return linkage.EndsWith("_charselect", StringComparison.OrdinalIgnoreCase)
                   || linkage.EndsWith("_cssp", StringComparison.OrdinalIgnoreCase)
                   || linkage.EndsWith("_classic_trophy", StringComparison.OrdinalIgnoreCase)
                   || linkage.EndsWith("_classic", StringComparison.OrdinalIgnoreCase)
                   || linkage.EndsWith("_allstar", StringComparison.OrdinalIgnoreCase)
                   || linkage.EndsWith("_event", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Cover-fit CSS select art into the fill-matrix visible window.
        /// PA linkages (charselect/cssp/classic/trophy/allstar/event) cover-fit with top bias;
        /// stock/congrats and other linkages contain-fit.
        /// </summary>
        public static void FitArtPngForLinkage(
            string xml,
            IReadOnlyDictionary<string, int> map,
            string linkage,
            string imageId,
            string srcPng,
            string destPng,
            int bitmapW,
            int bitmapH)
        {
            if (IsCssSelectLinkage(linkage)
                && map.TryGetValue(linkage, out var spriteId)
                && int.TryParse(imageId, out var bitsId)
                && TryComputeBitmapFillWindow(
                    xml, spriteId, bitsId, bitmapW, bitmapH,
                    out var winX, out var winY, out var winW, out var winH))
            {
                // Center in the fill window — no top bias (that left empty space under the chip art).
                FitPngIntoBitmapWindow(
                    srcPng, destPng, bitmapW, bitmapH, winX, winY, winW, winH, cover: true, biasY: 0);
                return;
            }

            if (IsPaCoverFitLinkage(linkage))
            {
                // PA panel: slight top bias so faces fill like vanilla portraits.
                var biasY = -(Math.Max(1, bitmapH) / 8);
                FitPngToSize(srcPng, destPng, bitmapW, bitmapH, cover: true, biasY);
                return;
            }

            FitPngToSize(srcPng, destPng, bitmapW, bitmapH, cover: false);
        }

        /// <summary>
        /// Parses SymbolClass tags/names pairs from FFDec SWF XML (no -export symbolClass).
        /// </summary>
        public static Dictionary<string, int> ParseSymbolClassMapFromXml(string xml)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(xml)) return map;

            var searchFrom = 0;
            while (true)
            {
                var typeIdx = xml.IndexOf("SymbolClassTag", searchFrom, StringComparison.OrdinalIgnoreCase);
                if (typeIdx < 0) break;
                var itemStart = xml.LastIndexOf("<item", typeIdx, StringComparison.OrdinalIgnoreCase);
                if (itemStart < 0)
                {
                    searchFrom = typeIdx + 1;
                    continue;
                }
                var itemEnd = FindMatchingItemClose(xml, itemStart);
                if (itemEnd <= itemStart)
                {
                    searchFrom = typeIdx + 1;
                    continue;
                }
                var block = xml.Substring(itemStart, itemEnd - itemStart);
                searchFrom = itemEnd;

                var tagsOpen = block.IndexOf("<tags>", StringComparison.OrdinalIgnoreCase);
                var tagsClose = block.IndexOf("</tags>", StringComparison.OrdinalIgnoreCase);
                var namesOpen = block.IndexOf("<names>", StringComparison.OrdinalIgnoreCase);
                var namesClose = block.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
                if (tagsOpen < 0 || tagsClose < tagsOpen || namesOpen < 0 || namesClose < namesOpen)
                    continue;

                var tagIds = Regex.Matches(
                        block.Substring(tagsOpen, tagsClose - tagsOpen),
                        @"<item>\s*(\d+)\s*</item>",
                        RegexOptions.IgnoreCase)
                    .Select(m => int.Parse(m.Groups[1].Value))
                    .ToList();
                var names = Regex.Matches(
                        block.Substring(namesOpen, namesClose - namesOpen),
                        @"<item>([^<]+)</item>",
                        RegexOptions.IgnoreCase)
                    .Select(m => m.Groups[1].Value.Trim())
                    .ToList();
                var n = Math.Min(tagIds.Count, names.Count);
                for (var i = 0; i < n; i++)
                {
                    if (string.IsNullOrWhiteSpace(names[i])) continue;
                    map[names[i]] = tagIds[i];
                }
            }
            return map;
        }

        /// <summary>
        /// Recenters DefineText (bounds + textMatrix) then stretches PlaceObject like stock
        /// chips: scaleX fills the bar, scaleY stays ~0.8, TX centers at local 407.
        /// </summary>
        public static (string Xml, int Touches) ScaleSelectChipTextPlaceObjects(
            string xml, int spriteId, IReadOnlyList<int> textIds, string labelText,
            double? forceScale = null, double translateY = -408)
        {
            if (textIds == null || textIds.Count == 0) return (xml, 0);
            var touches = 0;
            var advances = new Dictionary<int, int>();
            foreach (var tid in textIds)
            {
                var (recentering, n) = RecenterSelectChipDefineText(xml, tid);
                if (n > 0) { xml = recentering; touches += n; }
                var open = FindCharacterTagOpen(xml, tid, "DefineText");
                if (open == null) continue;
                var close = FindMatchingItemClose(xml, open.Index);
                if (close <= open.Index) continue;
                var sum = SumGlyphAdvancesInDefineText(xml.Substring(open.Index, close - open.Index));
                if (sum > 0) advances[tid] = sum;
            }

            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, touches);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, touches);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            var idSet = new HashSet<int>(textIds);
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            var poTouches = 0;

            var nextBlock = Regex.Replace(
                block,
                @"<item\b[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>[\s\S]*?</item>",
                m =>
                {
                    var tag = m.Value;
                    var cm = Regex.Match(tag, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                    if (!cm.Success || !int.TryParse(cm.Groups[1].Value, out var cid) || !idSet.Contains(cid))
                        return tag;
                    advances.TryGetValue(cid, out var adv);
                    var scaleX = forceScale ?? ComputeSelectChipTextScaleX(labelText, adv);
                    var scaleY = ComputeSelectChipTextScaleY();
                    // Integer twips — FFDec MATRIX translate fields are integral.
                    var tx = Math.Round(ComputeSelectChipTextTranslateX(labelText, scaleX));
                    var sx = scaleX.ToString("0.########", inv);
                    var sy = scaleY.ToString("0.########", inv);
                    var txStr = tx.ToString("0", inv);
                    var tyStr = Math.Round(translateY).ToString("0", inv);
                    var updated = tag;
                    if (Regex.IsMatch(updated, @"scaleX\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"scaleX\s*=\s*""[^""]*""", $"scaleX=\"{sx}\"",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(updated, @"scaleY\s*=\s*""[^""]*""", $"scaleY=\"{sy}\"",
                            RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 hasScale=\"true\" scaleX=\"{sx}\" scaleY=\"{sy}\"/>",
                            RegexOptions.IgnoreCase);
                        updated = Regex.Replace(
                            updated,
                            @"hasScale\s*=\s*""false""",
                            "hasScale=\"true\"",
                            RegexOptions.IgnoreCase);
                    }
                    if (Regex.IsMatch(updated, @"translateX\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"translateX\s*=\s*""[^""]*""",
                            $"translateX=\"{txStr}\"", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 translateX=\"{txStr}\"/>",
                            RegexOptions.IgnoreCase);
                    }
                    if (Regex.IsMatch(updated, @"translateY\s*=\s*""", RegexOptions.IgnoreCase))
                    {
                        updated = Regex.Replace(updated, @"translateY\s*=\s*""[^""]*""",
                            $"translateY=\"{tyStr}\"", RegexOptions.IgnoreCase);
                    }
                    else
                    {
                        updated = Regex.Replace(
                            updated,
                            @"<matrix\b([^>]*)/>",
                            $"<matrix$1 translateY=\"{tyStr}\"/>",
                            RegexOptions.IgnoreCase);
                    }
                    if (!string.Equals(tag, updated, StringComparison.Ordinal))
                        poTouches++;
                    return updated;
                },
                RegexOptions.IgnoreCase);

            if (poTouches == 0) return (xml, touches);
            xml = xml.Substring(0, spriteOpen.Index) + nextBlock + xml.Substring(spriteEnd);
            return (xml, touches + poTouches);
        }

        /// <summary>
        /// Private-copy DefineText / DefineEditText under a sprite so importText / bounds edits
        /// do not mutate the donor after a shallow sprite clone.
        /// </summary>
        public static (string Xml, int Touches) UniquifyTextTagsUnderSprite(
            string xml, int spriteId, ref int nextId)
        {
            var textIds = FindTextCharacterIdsInSprite(xml, spriteId);
            if (textIds.Count == 0) return (xml, 0);

            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, 0);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, 0);

            var touches = 0;
            var insertAt = spriteEnd;
            var spriteBlock = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            foreach (var oldId in textIds.Distinct())
            {
                var open = FindCharacterTagOpen(xml, oldId, "DefineEditText")
                           ?? FindCharacterTagOpen(xml, oldId, "DefineText");
                if (open is not { Success: true }) continue;
                var end = FindMatchingItemClose(xml, open.Index);
                if (end <= open.Index) continue;
                var block = xml.Substring(open.Index, end - open.Index);
                var newId = nextId++;
                var cloned = RemapIdAttribute(block, "characterId", oldId, newId);
                spriteBlock = RemapIdAttribute(spriteBlock, "characterId", oldId, newId);
                xml = xml.Insert(insertAt, "\n" + cloned + "\n");
                insertAt += cloned.Length + 2;
                // spriteOpen index is still valid (insert is after spriteEnd); rebuild sprite in place.
                touches++;
            }

            if (touches == 0) return (xml, 0);

            // Re-find sprite after inserts (indices shifted) and replace its block.
            spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, touches);
            spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, touches);
            xml = xml.Substring(0, spriteOpen.Index) + spriteBlock + xml.Substring(spriteEnd);
            return (xml, touches);
        }

        public static List<int> FindTextCharacterIdsInSprite(string xml, int spriteId)
        {
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
            if (!spriteOpen.Success) return new List<int>();

            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return new List<int>();
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            var textIds = new List<int>();
            foreach (Match m in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineTextInXml(xml, cid) && !textIds.Contains(cid))
                    textIds.Add(cid);
            }
            return textIds;
        }

        /// <summary>
        /// Prepare-once: convert pool <c>mm_c_*_select</c> nameplates from DefineText → DefineEditText
        /// so runtime can set <c>TextField.text</c> from live.json without host SWF writes on pack add.
        /// Idempotent when tags are already DefineEditText.
        /// </summary>
        public async Task EnsurePoolSelectEditableLabelsAsync(
            string menuSwfPath,
            int characterSlots,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (characterSlots <= 0) return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pool-edittext-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Making pool CSS nameplates editable ({characterSlots} chips)…");
                var xmlPath = Path.Combine(work, "menu.xml");
                await _jpexs.SwfToXmlAsync(jpexsPath ?? "", menuSwfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var map = ParseSymbolClassMapFromXml(xml);

                var linkages = new List<string>(characterSlots);
                for (var i = 0; i < characterSlots; i++)
                    linkages.Add($"mm_c_{i:D3}_select");

                var (next, touches) = ConvertPoolSelectNameplatesToEditText(xml, map, linkages);
                if (touches == 0)
                {
                    progress?.Report("Pool CSS nameplates already editable.");
                    return;
                }

                await File.WriteAllTextAsync(xmlPath, next, cancellationToken);
                var rebuilt = Path.Combine(work, "menu-edittext.swf");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, rebuilt, cancellationToken);
                if (!File.Exists(rebuilt))
                    throw new InvalidOperationException("FFDec did not rebuild menu_characters after EditText convert.");
                File.Copy(rebuilt, menuSwfPath, overwrite: true);
                progress?.Report($"Updated {touches} pool nameplate EditText/PlaceObject(s).");
            }
            finally
            {
                try { Directory.Delete(work, recursive: true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Converts / refreshes pool select nameplates to chip-local DefineEditText and resets
        /// PlaceObject matrices so labels sit in the nameplate band (not clipped above the cell).
        /// </summary>
        public static (string Xml, int Touches) ConvertPoolSelectNameplatesToEditText(
            string xml,
            IReadOnlyDictionary<string, int> symbolMap,
            IEnumerable<string> selectLinkages)
        {
            if (string.IsNullOrEmpty(xml) || selectLinkages == null) return (xml, 0);
            var touches = 0;
            foreach (var linkage in selectLinkages)
            {
                if (string.IsNullOrWhiteSpace(linkage)) continue;
                if (symbolMap == null || !symbolMap.TryGetValue(linkage, out var spriteId)) continue;
                foreach (var textId in FindTextCharacterIdsInSprite(xml, spriteId))
                {
                    var open = FindCharacterTagOpen(xml, textId, "DefineEditText")
                               ?? FindCharacterTagOpen(xml, textId, "DefineText");
                    if (open == null) continue;
                    var close = FindMatchingItemClose(xml, open.Index);
                    if (close <= open.Index) continue;
                    var tag = xml.Substring(open.Index, close - open.Index);
                    // FFDec round-trips scramble initialText (· → �) and newlines — compare bounds/layout.
                    if (!IsPoolNameplateEditTextReady(tag))
                    {
                        var converted = BuildPoolNameplateEditText(textId, "·");
                        xml = xml.Substring(0, open.Index) + converted + xml.Substring(close);
                        touches++;
                    }

                    var (xmlPo, poTouches) = ResetSelectNameplatePlaceObject(xml, spriteId, textId);
                    if (poTouches > 0)
                    {
                        xml = xmlPo;
                        touches += poTouches;
                    }
                }
            }
            return (xml, touches);
        }

        /// <summary>
        /// True when a DefineEditText tag already uses chip-local nameplate bounds / font size.
        /// </summary>
        public static bool IsPoolNameplateEditTextReady(string editTextXml)
        {
            if (string.IsNullOrEmpty(editTextXml)) return false;
            if (editTextXml.IndexOf("DefineEditText", StringComparison.OrdinalIgnoreCase) < 0)
                return false;
            if (!Regex.IsMatch(editTextXml, @"fontHeight\s*=\s*""200""", RegexOptions.IgnoreCase))
                return false;
            return Regex.IsMatch(editTextXml, @"Xmin\s*=\s*""-440""", RegexOptions.IgnoreCase)
                   && Regex.IsMatch(editTextXml, @"Xmax\s*=\s*""440""", RegexOptions.IgnoreCase)
                   && Regex.IsMatch(editTextXml, @"Ymin\s*=\s*""-440""", RegexOptions.IgnoreCase)
                   && Regex.IsMatch(editTextXml, @"Ymax\s*=\s*""-200""", RegexOptions.IgnoreCase);
        }

        /// <summary>
        /// Chip-local nameplate EditText: full chip width, top band only (twips, origin at chip center).
        /// </summary>
        public static string BuildPoolNameplateEditText(int characterId, string initialText = "·")
        {
            var escaped = System.Security.SecurityElement.Escape(initialText) ?? initialText;
            // Stock select shapes are RECT(-440..440). Nameplate ≈ top 12px → 240 twips.
            const int xmin = -440;
            const int xmax = 440;
            const int ymin = -440;
            const int ymax = -200;
            const int nbits = 10;
            return
                $"<item type=\"DefineEditTextTag\" align=\"2\" autoSize=\"false\" border=\"false\" " +
                $"characterID=\"{characterId}\" fontHeight=\"200\" fontId=\"18\" " +
                "forceWriteAsLong=\"true\" hasFont=\"true\" hasFontClass=\"false\" hasLayout=\"true\" " +
                "hasMaxLength=\"false\" hasText=\"true\" hasTextColor=\"true\" html=\"false\" indent=\"0\" " +
                $"initialText=\"{escaped}\" leading=\"0\" leftMargin=\"0\" multiline=\"false\" " +
                "noSelect=\"true\" password=\"false\" readOnly=\"true\" rightMargin=\"0\" " +
                "useOutlines=\"true\" variableName=\"\" wasStatic=\"true\" wordWrap=\"false\">\n" +
                $"      <bounds type=\"RECT\" Xmax=\"{xmax}\" Xmin=\"{xmin}\" Ymax=\"{ymax}\" Ymin=\"{ymin}\" nbits=\"{nbits}\"/>\n" +
                "      <textColor type=\"RGBA\" alpha=\"255\" blue=\"255\" green=\"255\" red=\"255\"/>\n" +
                "    </item>";
        }

        /// <summary>
        /// Builds a read-only centered DefineEditText from a DefineText tag body (preserves characterID).
        /// </summary>
        public static string ConvertDefineTextTagToEditText(string defineTextXml, string initialText = "·")
        {
            if (string.IsNullOrEmpty(defineTextXml)) return defineTextXml;
            var idMatch = Regex.Match(
                defineTextXml,
                @"characterID?\s*=\s*""(\d+)""",
                RegexOptions.IgnoreCase);
            if (!idMatch.Success) return defineTextXml;
            if (!int.TryParse(idMatch.Groups[1].Value, out var characterId))
                return defineTextXml;
            return BuildPoolNameplateEditText(characterId, initialText);
        }

        /// <summary>
        /// Resets the nameplate PlaceObject matrix to identity so EditText bounds map in chip space
        /// (legacy donor scale/translateY=-408 clips labels above the grid).
        /// </summary>
        public static (string Xml, int Touches) ResetSelectNameplatePlaceObject(
            string xml, int spriteId, int textId)
        {
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, 0);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, 0);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            var po = Regex.Match(
                block,
                $@"<item\b[^>]*type\s*=\s*""PlaceObject2Tag""[^>]*characterId\s*=\s*""{textId}""[^>]*>[\s\S]*?</item>",
                RegexOptions.IgnoreCase);
            if (!po.Success)
            {
                po = Regex.Match(
                    block,
                    $@"<item\b[^>]*characterId\s*=\s*""{textId}""[^>]*type\s*=\s*""PlaceObject2Tag""[^>]*>[\s\S]*?</item>",
                    RegexOptions.IgnoreCase);
            }
            if (!po.Success) return (xml, 0);

            // Identity matrix in chip space — ignore FFDec attr-order / newline churn.
            if (IsIdentityNameplatePlaceObject(po.Value))
                return (xml, 0);

            var replacement =
                $"<item type=\"PlaceObject2Tag\" characterId=\"{textId}\" depth=\"6\" forceWriteAsLong=\"false\" " +
                "placeFlagHasCharacter=\"true\" placeFlagHasClipActions=\"false\" placeFlagHasClipDepth=\"false\" " +
                "placeFlagHasColorTransform=\"false\" placeFlagHasMatrix=\"true\" placeFlagHasName=\"false\" " +
                "placeFlagHasRatio=\"false\" placeFlagMove=\"false\">\n" +
                "          <matrix type=\"MATRIX\" hasRotate=\"false\" hasScale=\"false\" nRotateBits=\"0\" " +
                "nScaleBits=\"0\" nTranslateBits=\"0\" translateX=\"0\" translateY=\"0\"/>\n" +
                "        </item>";

            var newBlock = block.Substring(0, po.Index) + replacement + block.Substring(po.Index + po.Length);
            xml = xml.Substring(0, spriteOpen.Index) + newBlock + xml.Substring(spriteEnd);
            return (xml, 1);
        }

        /// <summary>
        /// True when the nameplate PlaceObject already maps EditText bounds in chip space
        /// (no donor scale / translateY=-408 clip).
        /// </summary>
        public static bool IsIdentityNameplatePlaceObject(string placeObjectXml)
        {
            if (string.IsNullOrEmpty(placeObjectXml)) return false;
            if (!Regex.IsMatch(placeObjectXml, @"translateX\s*=\s*""0""", RegexOptions.IgnoreCase))
                return false;
            if (!Regex.IsMatch(placeObjectXml, @"translateY\s*=\s*""0""", RegexOptions.IgnoreCase))
                return false;
            // Reject residual donor scale (scaleX/scaleY present and not 1).
            var sx = Regex.Match(placeObjectXml, @"scaleX\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            var sy = Regex.Match(placeObjectXml, @"scaleY\s*=\s*""([^""]+)""", RegexOptions.IgnoreCase);
            if (sx.Success && !string.Equals(sx.Groups[1].Value, "1", StringComparison.Ordinal)
                && !string.Equals(sx.Groups[1].Value, "1.0", StringComparison.Ordinal))
                return false;
            if (sy.Success && !string.Equals(sy.Groups[1].Value, "1", StringComparison.Ordinal)
                && !string.Equals(sy.Groups[1].Value, "1.0", StringComparison.Ordinal))
                return false;
            return true;
        }

        private static int ReadIntAttr(string attrs, string name, int fallback)
        {
            var m = Regex.Match(attrs, $@"{Regex.Escape(name)}\s*=\s*""(-?\d+)""", RegexOptions.IgnoreCase);
            return m.Success && int.TryParse(m.Groups[1].Value, out var v) ? v : fallback;
        }

        /// <summary>
        /// Sets initialText / text bodies on DefineEditText and DefineText tags referenced by a sprite.
        /// </summary>
        public static (string Xml, int Touches) RewriteTextTagsForSprite(
            string xml, int spriteId, string displayName)
        {
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
            if (!spriteOpen.Success) return (xml, 0);

            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, 0);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            var textIds = new List<int>();
            foreach (Match m in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineTextInXml(xml, cid) && !textIds.Contains(cid))
                    textIds.Add(cid);
            }

            var touches = 0;
            var escaped = System.Security.SecurityElement.Escape(displayName) ?? displayName;
            foreach (var textId in textIds)
            {
                var open = FindCharacterTagOpen(xml, textId, "DefineEditText")
                           ?? FindCharacterTagOpen(xml, textId, "DefineText");
                if (open == null) continue;
                var close = FindMatchingItemClose(xml, open.Index);
                if (close <= open.Index) continue;
                var tag = xml.Substring(open.Index, close - open.Index);
                var rewritten = RewriteTextTagContents(tag, escaped);
                if (!string.Equals(tag, rewritten, StringComparison.Ordinal))
                {
                    xml = xml.Substring(0, open.Index) + rewritten + xml.Substring(close);
                    touches++;
                }
            }

            return (xml, touches);
        }

        /// <summary>Updates initialText attribute and/or &lt;initialText&gt;/&lt;text&gt; element bodies.</summary>
        public static string RewriteTextTagContents(string tagXml, string escapedDisplayName)
        {
            var text = tagXml;
            if (Regex.IsMatch(text, @"initialText\s*=\s*""", RegexOptions.IgnoreCase))
            {
                text = Regex.Replace(
                    text,
                    @"initialText\s*=\s*""[^""]*""",
                    $"initialText=\"{escapedDisplayName}\"",
                    RegexOptions.IgnoreCase);
            }

            text = Regex.Replace(
                text,
                @"<initialText>([^<]*)</initialText>",
                $"<initialText>{escapedDisplayName}</initialText>",
                RegexOptions.IgnoreCase);
            // DefineText glyph runs often wrap a single text element.
            text = Regex.Replace(
                text,
                @"<text>([^<]*)</text>",
                $"<text>{escapedDisplayName}</text>",
                RegexOptions.IgnoreCase);
            return text;
        }

        /// <summary>Finds a DefineBits characterId referenced inside a DefineSprite.</summary>
        private static int? FindNestedBitsIdInXml(string xml, int spriteId) =>
            FindLargestNestedBitsIdInXml(xml, spriteId);

        /// <summary>
        /// Primary PA bitmap under a sprite. Prefers highest-depth PlaceObject DefineBits
        /// (charselect art sits above the shadow shape); falls back to largest shape fill.
        /// </summary>
        public static int? FindLargestNestedBitsIdInXml(string xml, int spriteId)
        {
            var placed = CollectPlacedBitsWithDepth(xml, spriteId);
            if (placed.Count > 0)
            {
                var top = placed
                    .Where(p => p.Pixels >= 32 * 32)
                    .OrderByDescending(p => p.Depth)
                    .ThenByDescending(p => p.Pixels)
                    .FirstOrDefault();
                if (top.Id != 0) return top.Id;
                top = placed.OrderByDescending(p => p.Depth).ThenByDescending(p => p.Pixels).First();
                return top.Id;
            }

            var fillBits = new List<(int Id, int Pixels)>();
            var otherBits = new List<(int Id, int Pixels)>();
            CollectPaBitsCandidates(xml, spriteId, fillBits, otherBits, depth: 0);

            static int? Best(List<(int Id, int Pixels)> list) =>
                list.Count == 0 ? null : list.OrderByDescending(c => c.Pixels).First().Id;

            return Best(fillBits.Where(c => c.Pixels >= 32 * 32).ToList())
                   ?? Best(fillBits)
                   ?? Best(otherBits.Where(c => c.Pixels >= 32 * 32).ToList())
                   ?? Best(otherBits);
        }

        /// <summary>
        /// Same-size companions under a sprite (charselect shadow). Prefers lower-depth /
        /// shape-fill bitmaps so the silhouette never paints the top art layer.
        /// </summary>
        public static List<int> FindCompanionBitsIdsInXml(string xml, int spriteId, int primaryId)
        {
            if (!TryGetDefineBitsSize(xml, primaryId.ToString(), out var pw, out var ph) || pw <= 0)
                return new List<int>();

            var placed = CollectPlacedBitsWithDepth(xml, spriteId);
            var primaryDepth = placed.FirstOrDefault(p => p.Id == primaryId).Depth;
            var companions = new List<(int Id, int Depth, bool FromFill)>();

            foreach (var p in placed)
            {
                if (p.Id == primaryId) continue;
                if (!TryGetDefineBitsSize(xml, p.Id.ToString(), out var w, out var h)) continue;
                if (Math.Abs(w - pw) <= 2 && Math.Abs(h - ph) <= 2)
                    companions.Add((p.Id, p.Depth, false));
            }

            var fillBits = new List<(int Id, int Pixels)>();
            var otherBits = new List<(int Id, int Pixels)>();
            CollectPaBitsCandidates(xml, spriteId, fillBits, otherBits, depth: 0);
            foreach (var (id, _) in fillBits)
            {
                if (id == primaryId) continue;
                if (companions.Any(c => c.Id == id)) continue;
                if (!TryGetDefineBitsSize(xml, id.ToString(), out var w, out var h)) continue;
                if (Math.Abs(w - pw) <= 2 && Math.Abs(h - ph) <= 2)
                    companions.Add((id, primaryDepth > 0 ? primaryDepth - 1 : 0, true));
            }

            // Never treat a higher-depth PlaceObject as a shadow companion.
            return companions
                .Where(c => c.FromFill || c.Depth < primaryDepth || primaryDepth == 0)
                .OrderBy(c => c.Depth)
                .Select(c => c.Id)
                .Distinct()
                .ToList();
        }

        private static List<(int Id, int Depth, int Pixels)> CollectPlacedBitsWithDepth(string xml, int spriteId)
        {
            var result = new List<(int Id, int Depth, int Pixels)>();
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return result;
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return result;
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            foreach (Match m in Regex.Matches(
                         block,
                         @"<item\b[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>",
                         RegexOptions.IgnoreCase))
            {
                var tag = m.Value;
                var cm = Regex.Match(tag, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                var dm = Regex.Match(tag, @"depth\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                if (!cm.Success || !int.TryParse(cm.Groups[1].Value, out var cid)) continue;
                var depth = dm.Success && int.TryParse(dm.Groups[1].Value, out var d) ? d : 0;
                if (!IsDefineBitsInXml(xml, cid)) continue;
                if (!TryGetDefineBitsSize(xml, cid.ToString(), out var w, out var h)) continue;
                if (result.Any(r => r.Id == cid)) continue;
                result.Add((cid, depth, w * h));
            }

            // Attribute-order variant: characterId before type
            foreach (Match m in Regex.Matches(
                         block,
                         @"<item\b[^>]*characterId\s*=\s*""(\d+)""[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>",
                         RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid)) continue;
                var dm = Regex.Match(m.Value, @"depth\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                var depth = dm.Success && int.TryParse(dm.Groups[1].Value, out var d) ? d : 0;
                if (!IsDefineBitsInXml(xml, cid)) continue;
                if (!TryGetDefineBitsSize(xml, cid.ToString(), out var w, out var h)) continue;
                if (result.Any(r => r.Id == cid)) continue;
                result.Add((cid, depth, w * h));
            }

            return result;
        }

        private static void CollectPaBitsCandidates(
            string xml, int spriteId,
            List<(int Id, int Pixels)> fillBits,
            List<(int Id, int Pixels)> otherBits,
            int depth)
        {
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return;
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return;
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            foreach (Match m in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineShapeInXml(xml, cid))
                {
                    var shapeOpen = FindShapeTagOpen(xml, cid);
                    if (shapeOpen == null) continue;
                    var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                    if (shapeEnd <= shapeOpen.Index) continue;
                    var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                    foreach (Match bm in Regex.Matches(shapeBlock, @"bitmapId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                    {
                        if (!int.TryParse(bm.Groups[1].Value, out var bid) || bid == 0 || bid == 65535)
                            continue;
                        AddPaBitsCandidate(xml, bid, fillBits);
                    }
                }
                else if (IsDefineBitsInXml(xml, cid))
                {
                    AddPaBitsCandidate(xml, cid, otherBits);
                }
                else if (depth < 3 && IsDefineSpriteInXml(xml, cid))
                {
                    var linkName = FindFirstLinkageNameForCharacterId(xml, cid);
                    if (linkName != null && IsSeriesSymbolLinkageName(linkName))
                        continue;
                    CollectPaBitsCandidates(xml, cid, fillBits, otherBits, depth + 1);
                }
            }
        }

        private static void AddPaBitsCandidate(string xml, int cid, List<(int Id, int Pixels)> list)
        {
            if (list.Any(c => c.Id == cid)) return;
            if (!TryGetDefineBitsSize(xml, cid.ToString(), out var w, out var h)) return;
            list.Add((cid, w * h));
        }

        private static void CollectNestedBitsCandidates(
            string xml, int spriteId, List<(int Id, int Size)> candidates, int depth)
        {
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return;
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return;
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            foreach (Match m in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineBitsInXml(xml, cid))
                {
                    AddBitsCandidate(xml, cid, candidates);
                }
                else if (IsDefineShapeInXml(xml, cid))
                {
                    CollectBitsFromShape(xml, cid, candidates);
                }
                else if (depth < 3 && IsDefineSpriteInXml(xml, cid))
                {
                    CollectNestedBitsCandidates(xml, cid, candidates, depth + 1);
                }
            }
        }

        private static void CollectBitsFromShape(
            string xml, int shapeId, List<(int Id, int Size)> candidates)
        {
            var shapeOpen = FindShapeTagOpen(xml, shapeId);
            if (shapeOpen == null) return;
            var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
            if (shapeEnd <= shapeOpen.Index) return;
            var block = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
            foreach (Match m in Regex.Matches(block, @"bitmapId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var bid) || bid == 0 || bid == 65535)
                    continue;
                if (IsDefineBitsInXml(xml, bid))
                    AddBitsCandidate(xml, bid, candidates);
            }
        }

        private static void AddBitsCandidate(string xml, int cid, List<(int Id, int Size)> candidates)
        {
            if (candidates.Any(c => c.Id == cid)) return;
            var open = FindCharacterTagOpen(xml, cid, "DefineBits");
            if (open == null) return;
            var close = FindMatchingItemClose(xml, open.Index);
            var size = close > open.Index ? close - open.Index : 0;
            candidates.Add((cid, size));
        }

        private static Match? FindShapeTagOpen(string xml, int shapeId)
        {
            var openIdx = IndexOfTaggedItemOpen(xml, "DefineShape", "shapeId", shapeId);
            if (openIdx < 0) return null;
            var tagEnd = FindEndOfOpenXmlTag(xml, openIdx);
            if (tagEnd <= openIdx) return null;
            return Regex.Match(xml, Regex.Escape(xml.Substring(openIdx, tagEnd - openIdx)));
        }

        private static bool IsDefineShapeInXml(string xml, int shapeId) =>
            IndexOfTaggedItemOpen(xml, "DefineShape", "shapeId", shapeId) >= 0;

        /// <summary>
        /// Fixes only broken FILLSTYLE bitmapMatrix scales (&lt; 1 or missing).
        /// Preserves calibrated donor scales (e.g. Samus select 13.209) — forcing 20.0
        /// made FitPng letterbox padding fill the chip window (empty blue cell).
        /// </summary>
        public static (string Xml, int Touches) NormalizeBitmapMatricesForTargets(
            string xml,
            IReadOnlyDictionary<string, int> linkages,
            IReadOnlyList<(string Linkage, string ImageId)> targets)
        {
            var touches = 0;
            foreach (var (linkage, imageId) in targets)
            {
                if (!linkages.TryGetValue(linkage, out var spriteId)) continue;
                if (!int.TryParse(imageId, out var bitsId)) continue;

                var shapeIds = new HashSet<int>();
                CollectShapeIdsUnderSprite(xml, spriteId, shapeIds, 0);

                foreach (var shapeId in shapeIds)
                {
                    var shapeOpen = FindShapeTagOpen(xml, shapeId);
                    if (shapeOpen == null) continue;
                    var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                    if (shapeEnd <= shapeOpen.Index) continue;
                    var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                    if (!Regex.IsMatch(shapeBlock, $@"bitmapId\s*=\s*""{bitsId}""", RegexOptions.IgnoreCase))
                        continue;

                    var next = FixBrokenBitmapScaleNearBits(shapeBlock, bitsId);
                    if (!string.Equals(shapeBlock, next, StringComparison.Ordinal))
                    {
                        xml = xml.Substring(0, shapeOpen.Index) + next + xml.Substring(shapeEnd);
                        touches++;
                    }
                }
            }
            return (xml, touches);
        }

        /// <summary>Returns true when a bitmap fill scale should be reset to 20 (1px = 20 twips).</summary>
        public static bool IsBrokenBitmapScale(string scaleValue)
        {
            if (string.IsNullOrWhiteSpace(scaleValue)) return true;
            if (!double.TryParse(scaleValue, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var v))
                return true;
            return Math.Abs(v) < 1.0;
        }

        private static string FixBrokenBitmapScaleNearBits(string shapeBlock, int bitsId)
        {
            string ReplaceIfBroken(Match m)
            {
                var current = m.Groups[2].Value;
                if (!IsBrokenBitmapScale(current)) return m.Value;
                return m.Groups[1].Value + "20.0" + m.Groups[3].Value;
            }

            var next = Regex.Replace(
                shapeBlock,
                $@"(bitmapId\s*=\s*""{bitsId}""[\s\S]{{0,500}}?scaleX\s*=\s*"")([^""]+)("")",
                ReplaceIfBroken,
                RegexOptions.IgnoreCase);
            next = Regex.Replace(
                next,
                $@"(bitmapId\s*=\s*""{bitsId}""[\s\S]{{0,500}}?scaleY\s*=\s*"")([^""]+)("")",
                ReplaceIfBroken,
                RegexOptions.IgnoreCase);
            next = Regex.Replace(
                next,
                @"(scaleX\s*=\s*"")([^""]+)(""[\s\S]{0,200}?bitmapId\s*=\s*""" + bitsId + @""")",
                ReplaceIfBroken,
                RegexOptions.IgnoreCase);
            next = Regex.Replace(
                next,
                @"(scaleY\s*=\s*"")([^""]+)(""[\s\S]{0,200}?bitmapId\s*=\s*""" + bitsId + @""")",
                ReplaceIfBroken,
                RegexOptions.IgnoreCase);
            return next;
        }

        private static void CollectShapeIdsUnderSprite(
            string xml, int spriteId, HashSet<int> shapeIds, int depth)
        {
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return;
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return;
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            foreach (Match m in Regex.Matches(block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineShapeInXml(xml, cid))
                    shapeIds.Add(cid);
                else if (depth < 3 && IsDefineSpriteInXml(xml, cid))
                    CollectShapeIdsUnderSprite(xml, cid, shapeIds, depth + 1);
            }
        }

        private static Match? FindSpriteTagOpen(string xml, int spriteId)
        {
            var openIdx = IndexOfTaggedItemOpen(xml, "DefineSpriteTag", "spriteId", spriteId);
            if (openIdx < 0) return null;
            var tagEnd = FindEndOfOpenXmlTag(xml, openIdx);
            if (tagEnd <= openIdx) return null;
            return Regex.Match(xml, Regex.Escape(xml.Substring(openIdx, tagEnd - openIdx)));
        }

        private static bool IsDefineSpriteInXml(string xml, int spriteId) =>
            IndexOfTaggedItemOpen(xml, "DefineSpriteTag", "spriteId", spriteId) >= 0;

        /// <summary>
        /// Locates &lt;item type="…Prefix…" idAttr="id"&gt; without [^&gt;]* scans over multi‑MB attrs.
        /// </summary>
        private static int IndexOfTaggedItemOpen(string xml, string typePrefix, string idAttr, int id)
        {
            var idToken = $"{idAttr}=\"{id}\"";
            var searchFrom = 0;
            while (searchFrom < xml.Length)
            {
                var hit = xml.IndexOf(idToken, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (hit < 0) return -1;
                var itemStart = xml.LastIndexOf("<item", hit, StringComparison.OrdinalIgnoreCase);
                if (itemStart >= 0 && hit - itemStart < 300)
                {
                    var windowEnd = Math.Min(xml.Length, hit + idToken.Length + 80);
                    var window = xml.AsSpan(itemStart, windowEnd - itemStart);
                    if (window.Contains(typePrefix, StringComparison.OrdinalIgnoreCase))
                        return itemStart;
                }
                searchFrom = hit + idToken.Length;
            }
            return -1;
        }

        /// <summary>
        /// Finds a bitmap character id from FFDec image exports (never sprite frame 1.png).
        /// </summary>
        private static string? FindBitmapIdFromImageExports(string exportDir, string linkage, int spriteCharId)
        {
            if (!Directory.Exists(exportDir)) return null;
            foreach (var file in Directory.GetFiles(exportDir, "*.*", SearchOption.AllDirectories))
            {
                var ext = Path.GetExtension(file).ToLowerInvariant();
                if (ext is not (".png" or ".jpg" or ".jpeg" or ".gif")) continue;
                var parsed = CharacterAssetService.TryParseDefineBitsExportId(file);
                if (parsed != null) return parsed;
            }

            // images/ folder next to a sprite export named for this linkage
            foreach (var dir in Directory.GetDirectories(exportDir, "*", SearchOption.AllDirectories))
            {
                if (!CharacterAssetService.NameMatchesLinkage(Path.GetFileName(dir), linkage)
                    && !dir.Contains(spriteCharId.ToString(), StringComparison.Ordinal))
                    continue;
                foreach (var file in Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories))
                {
                    var parsed = CharacterAssetService.TryParseDefineBitsExportId(file);
                    if (parsed != null) return parsed;
                }
            }
            return null;
        }

        /// <summary>
        /// Uniquify shared DefineBits for many linkages in one XML string.
        /// Returns updated XML and linkage → final image character id.
        /// </summary>
        private static (string Xml, Dictionary<string, string> Remapped, int NextId) UniquifyImagesInXml(
            string xml,
            IReadOnlyDictionary<string, int> linkages,
            IReadOnlyList<(string Linkage, string ImageId)> targets,
            int nextId)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (linkage, imageId) in targets)
                result[linkage] = imageId;

            foreach (var (linkage, imageId) in targets)
            {
                if (!int.TryParse(imageId, out var oldImgId)) continue;
                if (!linkages.TryGetValue(linkage, out var linkCharId)) continue;

                var sharers = linkages.Count(kv => kv.Value == oldImgId);
                // Image linkage already private → keep. Sprite-backed or shared image → clone bits.
                if (linkCharId == oldImgId && sharers <= 1)
                    continue;

                var (xmlNext, newImgId) = CloneBitsTag(xml, oldImgId, ref nextId);
                if (newImgId == null) continue;
                xml = xmlNext;

                if (linkCharId == oldImgId)
                {
                    xml = RemapSymbolClassCharacterId(xml, linkage, oldImgId, newImgId.Value);
                }
                else
                {
                    // Remap PlaceObject characterId refs and FILLSTYLE bitmapId on shapes
                    // under this sprite (CSS chips use shape bitmap fills, not PlaceObject→bits).
                    xml = RemapBitmapRefsUnderSprite(xml, linkCharId, oldImgId, newImgId.Value, ref nextId);
                }

                result[linkage] = newImgId.Value.ToString();
            }

            return (xml, result, nextId);
        }

        /// <summary>
        /// Clones a DefineBits under <paramref name="spriteId"/> and remaps local refs so
        /// a later -replace won't overwrite a shared donor shadow bitmap.
        /// </summary>
        private static (string Xml, int? NewId) UniquifySingleBitsUnderSprite(
            string xml, int spriteId, int oldImgId, ref int nextId)
        {
            var (xmlNext, newImgId) = CloneBitsTag(xml, oldImgId, ref nextId);
            if (newImgId == null) return (xml, null);
            xmlNext = RemapBitmapRefsUnderSprite(xmlNext, spriteId, oldImgId, newImgId.Value, ref nextId);
            return (xmlNext, newImgId);
        }

        private static (string Xml, int? NewId) CloneBitsTag(string xml, int oldImgId, ref int nextId)
        {
            // Locate via IndexOf (not [^>]* regex — payloads are multi‑MB attributes).
            // Must copy the FULL tag including zlib/jpeg payload. Empty stubs xml2swf'd into
            // invalid DefineBits and crashed the Flash player on launch.
            var openIdx = IndexOfDefineBitsTagOpen(xml, oldImgId);
            if (openIdx < 0) return (xml, null);

            var tagEnd = FindEndOfOpenXmlTag(xml, openIdx);
            if (tagEnd < 0) return (xml, null);
            var selfClosing = tagEnd >= 2 && xml[tagEnd - 2] == '/';
            var bitsEnd = selfClosing ? tagEnd : FindMatchingItemClose(xml, openIdx);
            if (bitsEnd <= openIdx) return (xml, null);

            var newImgId = nextId++;
            var bitsBlock = xml.Substring(openIdx, bitsEnd - openIdx);
            var clonedBits = RemapIdAttribute(bitsBlock, "characterId", oldImgId, newImgId);
            if (string.Equals(clonedBits, bitsBlock, StringComparison.Ordinal))
                clonedBits = RemapIdAttribute(bitsBlock, "characterID", oldImgId, newImgId);
            xml = xml.Insert(bitsEnd, "\n" + clonedBits + "\n");
            return (xml, newImgId);
        }

        [ThreadStatic] private static Dictionary<int, int>? _defineBitsOpenIndex;
        [ThreadStatic] private static int _defineBitsOpenIndexXmlLen;

        private static void WarmDefineBitsOpenIndex(string xml)
        {
            var map = new Dictionary<int, int>();
            var pos = 0;
            while ((pos = xml.IndexOf("DefineBits", pos, StringComparison.Ordinal)) >= 0)
            {
                var itemStart = xml.LastIndexOf("<item", pos, StringComparison.OrdinalIgnoreCase);
                if (itemStart < 0 || pos - itemStart > 160)
                {
                    pos += 10;
                    continue;
                }
                var header = GetDefineBitsHeaderSpan(xml, itemStart);
                if (!header.IsEmpty)
                {
                    var hs = header.ToString();
                    var idm = Regex.Match(hs, @"characterID\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                    if (!idm.Success)
                        idm = Regex.Match(hs, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase);
                    if (idm.Success && int.TryParse(idm.Groups[1].Value, out var id))
                        map[id] = itemStart;
                }
                pos += 10;
            }
            _defineBitsOpenIndex = map;
            _defineBitsOpenIndexXmlLen = xml.Length;
        }

        private static void ClearDefineBitsOpenIndex()
        {
            _defineBitsOpenIndex = null;
            _defineBitsOpenIndexXmlLen = 0;
        }

        private static int IndexOfDefineBitsTagOpen(string xml, int characterId)
        {
            if (_defineBitsOpenIndex != null
                && _defineBitsOpenIndexXmlLen == xml.Length
                && _defineBitsOpenIndex.TryGetValue(characterId, out var cached))
                return cached;

            // characterID="N" (FFDec) near DefineBits* type
            var idToken = $"characterID=\"{characterId}\"";
            var idTokenAlt = $"characterId=\"{characterId}\"";
            var searchFrom = 0;
            while (searchFrom < xml.Length)
            {
                var idIdx = xml.IndexOf(idToken, searchFrom, StringComparison.OrdinalIgnoreCase);
                var idIdx2 = xml.IndexOf(idTokenAlt, searchFrom, StringComparison.OrdinalIgnoreCase);
                if (idIdx < 0) idIdx = int.MaxValue;
                if (idIdx2 < 0) idIdx2 = int.MaxValue;
                var hit = Math.Min(idIdx, idIdx2);
                if (hit == int.MaxValue) return -1;

                var itemStart = xml.LastIndexOf("<item", hit, StringComparison.OrdinalIgnoreCase);
                if (itemStart >= 0)
                {
                    var window = xml.AsSpan(itemStart, Math.Min(120, hit - itemStart + 20));
                    if (window.Contains("DefineBits", StringComparison.OrdinalIgnoreCase))
                        return itemStart;
                }
                searchFrom = hit + 1;
            }
            return -1;
        }

        private static int FindEndOfOpenXmlTag(string xml, int openItemIndex)
        {
            var inQuotes = false;
            for (var i = openItemIndex; i < xml.Length; i++)
            {
                var c = xml[i];
                if (c == '"') inQuotes = !inQuotes;
                else if (c == '>' && !inQuotes)
                    return i + 1;
            }
            return -1;
        }

        /// <summary>
        /// Remaps PlaceObject characterIds and shape FILLSTYLE bitmapIds under a sprite
        /// (and one level of nested sprites) from <paramref name="oldImgId"/> → <paramref name="newImgId"/>.
        /// </summary>
        private static string RemapBitmapRefsUnderSprite(
            string xml, int spriteId, int oldImgId, int newImgId, ref int nextId)
        {
            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return xml;
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return xml;

            var spriteBlock = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            var remappedSprite = RemapIdAttribute(spriteBlock, "characterId", oldImgId, newImgId);
            xml = xml.Substring(0, spriteOpen.Index) + remappedSprite + xml.Substring(spriteEnd);

            // Collect shape / nested-sprite ids from the (possibly remapped) sprite block.
            var shapeIds = new List<int>();
            var nestedSprites = new List<int>();
            foreach (Match m in Regex.Matches(remappedSprite, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
            {
                if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == spriteId) continue;
                if (IsDefineShapeInXml(xml, cid) && !shapeIds.Contains(cid))
                    shapeIds.Add(cid);
                else if (IsDefineSpriteInXml(xml, cid) && !nestedSprites.Contains(cid))
                    nestedSprites.Add(cid);
            }

            foreach (var nested in nestedSprites)
            {
                var nestOpen = FindSpriteTagOpen(xml, nested);
                if (nestOpen == null) continue;
                var nestEnd = FindMatchingItemClose(xml, nestOpen.Index);
                if (nestEnd <= nestOpen.Index) continue;
                var nestBlock = xml.Substring(nestOpen.Index, nestEnd - nestOpen.Index);
                foreach (Match m in Regex.Matches(nestBlock, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                {
                    if (!int.TryParse(m.Groups[1].Value, out var cid)) continue;
                    if (IsDefineShapeInXml(xml, cid) && !shapeIds.Contains(cid))
                        shapeIds.Add(cid);
                }
            }

            // Clone each shape that uses oldImgId, then point THIS sprite tree at the clone.
            // nextId is shared with UniquifyImagesInXml so shape clones never collide with bits.
            foreach (var shapeId in shapeIds)
            {
                var shapeOpen = FindShapeTagOpen(xml, shapeId);
                if (shapeOpen == null) continue;
                var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                if (shapeEnd <= shapeOpen.Index) continue;
                var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                if (!Regex.IsMatch(shapeBlock, $@"bitmapId\s*=\s*""{oldImgId}""", RegexOptions.IgnoreCase))
                    continue;

                var newShapeId = nextId++;
                var clonedShape = RemapIdAttribute(shapeBlock, "shapeId", shapeId, newShapeId);
                clonedShape = Regex.Replace(
                    clonedShape,
                    $@"bitmapId\s*=\s*""{oldImgId}""",
                    $"bitmapId=\"{newImgId}\"",
                    RegexOptions.IgnoreCase);
                xml = xml.Insert(shapeEnd, "\n" + clonedShape + "\n");

                // Retarget PlaceObject refs under the clone sprite (+ nested) to the new shape.
                xml = RemapCharacterIdUnderSpriteTree(xml, spriteId, shapeId, newShapeId, nestedSprites);
            }

            return xml;
        }

        /// <summary>
        /// Points the highest-depth PlaceObject→DefineBits under a sprite at <paramref name="bitsId"/>.
        /// </summary>
        public static (string Xml, bool Touched) RemapHighestDepthBitsUnderSprite(
            string xml, int spriteId, int bitsId)
        {
            var placed = CollectPlacedBitsWithDepth(xml, spriteId);
            if (placed.Count == 0) return (xml, false);
            var top = placed.OrderByDescending(p => p.Depth).First();
            if (top.Id == bitsId) return (xml, false);

            var spriteOpen = FindSpriteTagOpen(xml, spriteId);
            if (spriteOpen == null) return (xml, false);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, false);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
            var remapped = RemapIdAttribute(block, "characterId", top.Id, bitsId);
            if (string.Equals(block, remapped, StringComparison.Ordinal)) return (xml, false);
            return (xml.Substring(0, spriteOpen.Index) + remapped + xml.Substring(spriteEnd), true);
        }

        /// <summary>
        /// Samus pattern: stock sprite PlaceObject should instance the stock0 bitmap id.
        /// </summary>
        public static (string Xml, bool Touched) RetargetStockSpriteToStock0InXml(
            string xml, IReadOnlyDictionary<string, int> linkages, string characterId)
        {
            var stockLink = characterId + "_stock";
            var stock0Link = characterId + "_stock0";
            if (!linkages.TryGetValue(stockLink, out var stockSpriteId)) return (xml, false);
            if (!linkages.TryGetValue(stock0Link, out var stock0BitsId)) return (xml, false);
            if (!IsDefineBitsInXml(xml, stock0BitsId)) return (xml, false);

            var spriteOpen = FindSpriteTagOpen(xml, stockSpriteId);
            if (spriteOpen == null) return (xml, false);
            var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
            if (spriteEnd <= spriteOpen.Index) return (xml, false);
            var block = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);

            // Retarget the first PlaceObject that points at a DefineBits (or wrong shape) to stock0.
            var po = Regex.Match(
                block,
                @"<item\b[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*characterId\s*=\s*""(\d+)""[^>]*>",
                RegexOptions.IgnoreCase);
            if (!po.Success)
            {
                po = Regex.Match(
                    block,
                    @"<item\b[^>]*characterId\s*=\s*""(\d+)""[^>]*type\s*=\s*""PlaceObject\d*Tag""[^>]*>",
                    RegexOptions.IgnoreCase);
            }
            if (!po.Success || !int.TryParse(po.Groups[1].Value, out var currentId))
                return (xml, false);
            if (currentId == stock0BitsId) return (xml, false);

            var remapped = RemapIdAttribute(block, "characterId", currentId, stock0BitsId);
            if (string.Equals(block, remapped, StringComparison.Ordinal)) return (xml, false);
            xml = xml.Substring(0, spriteOpen.Index) + remapped + xml.Substring(spriteEnd);
            return (xml, true);
        }

        private static string RemapCharacterIdUnderSpriteTree(
            string xml, int rootSpriteId, int oldId, int newId, IEnumerable<int> nestedSprites)
        {
            foreach (var sid in nestedSprites.Prepend(rootSpriteId).Distinct())
            {
                var open = FindSpriteTagOpen(xml, sid);
                if (open == null) continue;
                var end = FindMatchingItemClose(xml, open.Index);
                if (end <= open.Index) continue;
                var block = xml.Substring(open.Index, end - open.Index);
                var remapped = RemapIdAttribute(block, "characterId", oldId, newId);
                if (!string.Equals(block, remapped, StringComparison.Ordinal))
                    xml = xml.Substring(0, open.Index) + remapped + xml.Substring(end);
            }
            return xml;
        }

        /// <summary>Updates SymbolClass tags[i] when names[i] == linkageName.</summary>
        public static string RemapSymbolClassCharacterId(string xml, string linkageName, int oldId, int newId)
        {
            return Regex.Replace(
                xml,
                @"(<item\b[^>]*type\s*=\s*""SymbolClassTag""[^>]*>\s*<tags>)([\s\S]*?)(</tags>\s*<names>)([\s\S]*?)(</names>)",
                m =>
                {
                    var tags = Regex.Matches(m.Groups[2].Value, @"<item>(\d+)</item>");
                    var names = Regex.Matches(m.Groups[4].Value, @"<item>([^<]*)</item>");
                    if (tags.Count != names.Count || tags.Count == 0)
                        return m.Value;

                    var tagItems = tags.Cast<Match>().Select(x => x.Groups[1].Value).ToList();
                    var nameItems = names.Cast<Match>().Select(x => x.Groups[1].Value).ToList();
                    var changed = false;
                    for (var i = 0; i < nameItems.Count; i++)
                    {
                        if (nameItems[i].Equals(linkageName, StringComparison.OrdinalIgnoreCase)
                            && tagItems[i] == oldId.ToString())
                        {
                            tagItems[i] = newId.ToString();
                            changed = true;
                        }
                    }
                    if (!changed) return m.Value;

                    var newTags = string.Concat(tagItems.Select(t => $"\n        <item>{t}</item>")) + "\n      ";
                    var newNames = m.Groups[4].Value; // names unchanged
                    return m.Groups[1].Value + newTags + m.Groups[3].Value + newNames + m.Groups[5].Value;
                },
                RegexOptions.IgnoreCase);
        }

        private static string GuessIdFromLinkage(string linkage)
        {
            // congrats_mario / allstar_congrats_mario / mario_select
            foreach (var prefix in new[] { "allstar_congrats_", "congrats_" })
            {
                if (linkage.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    return linkage[prefix.Length..];
            }
            var idx = linkage.LastIndexOf('_');
            if (idx > 0) return linkage[..idx];
            return linkage;
        }

        private static string GuessSuffixFromLinkage(string linkage, string id)
        {
            if (linkage.StartsWith("allstar_congrats_", StringComparison.OrdinalIgnoreCase) ||
                linkage.StartsWith("congrats_", StringComparison.OrdinalIgnoreCase))
                return "";
            if (linkage.StartsWith(id + "_", StringComparison.OrdinalIgnoreCase))
                return linkage[id.Length..];
            return "";
        }

        /// <summary>
        /// Ensures a linkage script exists by dual-package importScript (append new class into the
        /// donor .as file so FFDec's compiler emits both packs). Accepts full linkage names
        /// (e.g. congrats_dedede → congrats_clonededede).
        /// </summary>
        public async Task<MenuSlotCloneResult> EnsureLinkageScriptAsync(
            string swfPath,
            string donorId,
            string newId,
            string suffix,
            string idProp,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var result = new MenuSlotCloneResult();
            donorId = donorId.Trim();
            newId = SanitizeId(newId);
            var donorLink = donorId + suffix;
            var newLink = newId + suffix;
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-linkscript-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var outSwf = await InjectLinkageScriptDualAsync(
                    swfPath, donorLink, newLink, donorId, newId, idProp,
                    jpexsPath, work, progress, cancellationToken);
                if (!string.Equals(outSwf, swfPath, StringComparison.OrdinalIgnoreCase))
                {
                    var bak = swfPath + ".ssf2mm.bak";
                    if (!File.Exists(bak))
                        File.Copy(swfPath, bak, overwrite: false);
                    File.Copy(outSwf, swfPath, overwrite: true);
                }
                result.Success = true;
                result.OutputSwfPath = swfPath;
                result.Message = $"Ensured ActionScript class {newLink} in {Path.GetFileName(swfPath)}.";
                progress?.Report(result.Message);
                return result;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Injects linkage scripts: dual-package <c>importScript</c> for existing packs,
        /// and FFDec-library <c>AddSelectClass</c> for brand-new CSS <c>{id}_select</c> classes.
        /// AddSelectClass uses fresh method stubs (never shares donor iinit/cinit) so the donor
        /// chip class is not corrupted.
        /// </summary>
        private async Task<string> InjectLinkageScriptsBatchAsync(
            string swfPath,
            IReadOnlyList<(string DonorLink, string NewLink, string DonorId, string NewId)> jobs,
            string idProp,
            string? jpexsPath,
            string workRoot,
            IProgress<string>? progress,
            CancellationToken cancellationToken,
            IReadOnlyList<string>? misc2Movieclips = null)
        {
            var hasClips = misc2Movieclips != null && misc2Movieclips.Count > 0;
            if (jobs.Count == 0 && !hasClips) return swfPath;

            var exportDir = Path.Combine(workRoot, "scripts-export-batch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(exportDir);
            progress?.Report($"Exporting scripts from {Path.GetFileName(swfPath)}…");
            await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, exportDir, "script", cancellationToken);

            string? packageRoot = null;
            var modifiedRelPaths = new List<string>();
            var addClassJobs = new List<(string DonorLink, string NewLink, string NewId)>();
            var missingDonorSelect = new List<string>();
            foreach (var (donorLink, newLink, donorId, newId) in jobs)
            {
                var donorAs = Directory.GetFiles(exportDir, donorLink + ".as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (donorAs == null)
                {
                    progress?.Report($"Skip script {newLink}: donor {donorLink}.as not found.");
                    if (IsCssSelectLinkage(newLink))
                        missingDonorSelect.Add($"{newLink} (donor {donorLink}.as missing)");
                    continue;
                }

                packageRoot ??= FindScriptsPackageRoot(exportDir, donorAs);
                var existingNew = Directory.GetFiles(exportDir, newLink + ".as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                var needsId = IsCssSelectLinkage(newLink);
                if (existingNew != null)
                {
                    var existingText = await File.ReadAllTextAsync(existingNew, cancellationToken);
                    var classOk = existingText.Contains($"class {newLink}", StringComparison.Ordinal);
                    var idOk = !needsId || ScriptHasIdAssignment(existingText, idProp, newId);
                    if (classOk && idOk)
                    {
                        progress?.Report($"Script {newLink}.as already present.");
                        continue;
                    }
                }

                // importScript cannot create new ABC packs — AddSelectClass for any missing class.
                // CSS select gets characterID; stock/classic/congrats get stubs matching donor
                // superclass (MovieClip vs BitmapData).
                if (existingNew == null)
                {
                    addClassJobs.Add((donorLink, newLink, newId));
                    progress?.Report($"Queued new AS3 class {newLink} (AddSelectClass).");
                    continue;
                }

                var donorText = await File.ReadAllTextAsync(donorAs, cancellationToken);
                donorText = TakeFirstPackageBlock(donorText);
                var dual = BuildDualPackageSelectScript(
                    donorText, donorLink, newLink, donorId, newId, idProp);
                await File.WriteAllTextAsync(
                    donorAs, dual, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                modifiedRelPaths.Add(Path.GetRelativePath(packageRoot!, donorAs));
                progress?.Report($"Prepared dual-package {donorLink} + {newLink}.");
            }

            if (hasClips)
            {
                var misc2As = Directory.GetFiles(exportDir, "Misc2.as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (misc2As != null)
                {
                    packageRoot ??= FindScriptsPackageRoot(exportDir, misc2As);
                    var text = await File.ReadAllTextAsync(misc2As, cancellationToken);
                    var next = AppendMisc2Movieclips(text, misc2Movieclips!);
                    if (!string.Equals(text, next, StringComparison.Ordinal))
                    {
                        await File.WriteAllTextAsync(
                            misc2As, next, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                        modifiedRelPaths.Add(Path.GetRelativePath(packageRoot!, misc2As));
                        progress?.Report(
                            "Registered in Misc2: " + string.Join(", ",
                                misc2Movieclips!.Distinct(StringComparer.OrdinalIgnoreCase)));
                    }
                }
                else
                {
                    progress?.Report("Misc2.as not found; skipped movieclip register.");
                }
            }

            if (missingDonorSelect.Count > 0 && modifiedRelPaths.Count == 0
                && addClassJobs.Count == 0 && !hasClips)
            {
                throw new InvalidOperationException(
                    "CSS select script(s) could not be installed: " + string.Join("; ", missingDonorSelect));
            }

            var current = swfPath;
            if (modifiedRelPaths.Count > 0 && packageRoot != null)
            {
                var deltaRoot = Path.Combine(workRoot, "scripts-delta-" + Guid.NewGuid().ToString("N"));
                BuildDeltaScriptImportTree(packageRoot, deltaRoot, modifiedRelPaths);
                progress?.Report(
                    $"importScript delta ({modifiedRelPaths.Count} file(s)) into {Path.GetFileName(swfPath)}…");

                var outfile = Path.Combine(workRoot, "with-scripts-" + Guid.NewGuid().ToString("N") + ".swf");
                await _jpexs.ImportScriptAsync(jpexsPath ?? "", current, outfile, deltaRoot, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("FFDec importScript did not produce an output SWF.");
                current = outfile;
            }

            if (addClassJobs.Count > 0)
            {
                var batch = addClassJobs
                    .Select(j => (
                        j.DonorLink,
                        j.NewLink,
                        j.NewId,
                        IsCssSelectLinkage(j.NewLink) ? idProp : "-"))
                    .ToList();
                progress?.Report($"AddSelectClass batch ({batch.Count} class(es))…");
                var outfile = Path.Combine(
                    workRoot, "addclass-batch-" + Guid.NewGuid().ToString("N") + ".swf");
                await _jpexs.AddAs3ClassesBatchAsync(
                    jpexsPath ?? "", current, outfile, batch, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("AddSelectClass batch produced no output.");
                current = outfile;
            }

            return current;
        }

        /// <summary>FFDec export root that contains the package folders for importScript.</summary>
        private static string FindScriptsPackageRoot(string exportDir, string asFilePath)
        {
            var dir = Path.GetDirectoryName(asFilePath)!;
            // Walk up until we hit the export dir — importScript wants the folder with packages.
            while (!string.Equals(dir, exportDir, StringComparison.OrdinalIgnoreCase)
                   && Path.GetDirectoryName(dir) is { } parent
                   && !string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase))
            {
                // Prefer the directory whose children are package folders or .as files.
                if (string.Equals(Path.GetFileName(dir), "scripts", StringComparison.OrdinalIgnoreCase))
                    return dir;
                dir = parent;
            }
            return exportDir;
        }

        /// <summary>Copies only modified .as files into a minimal tree for fast importScript.</summary>
        public static void BuildDeltaScriptImportTree(
            string packageRoot, string deltaRoot, IEnumerable<string> relativeAsPaths)
        {
            Directory.CreateDirectory(deltaRoot);
            foreach (var rel in relativeAsPaths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                var src = Path.Combine(packageRoot, rel);
                if (!File.Exists(src)) continue;
                var dest = Path.Combine(deltaRoot, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
            }
        }

        /// <summary>
        /// Public entry: inject deferred linkage scripts and/or Misc2 movieclip register in one pass.
        /// </summary>
        public async Task InjectScriptsAndMisc2RegisterAsync(
            string swfPath,
            IReadOnlyList<(string DonorLink, string NewLink)> linkages,
            IReadOnlyList<string>? misc2Movieclips,
            string idProp = "characterID",
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var jobs = linkages
                .Select(l => (l.DonorLink, l.NewLink, GuessIdFromLinkage(l.DonorLink), GuessIdFromLinkage(l.NewLink)))
                .ToList();
            if (jobs.Count == 0 && (misc2Movieclips == null || misc2Movieclips.Count == 0))
                return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-injectreg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var outSwf = await InjectLinkageScriptsBatchAsync(
                    swfPath, jobs, idProp, jpexsPath, work, progress, cancellationToken, misc2Movieclips);
                if (!string.Equals(outSwf, swfPath, StringComparison.OrdinalIgnoreCase))
                {
                    var bak = swfPath + ".ssf2mm.bak";
                    if (!File.Exists(bak)) File.Copy(swfPath, bak, overwrite: false);
                    File.Copy(outSwf, swfPath, overwrite: true);
                }
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// True when <paramref name="linkage"/> has an AS class that assigns
        /// <c>this.{idProp} = "{expectedId}"</c> (required for CSS chips to appear).
        /// </summary>
        public async Task<bool> SelectScriptExistsAsync(
            string swfPath,
            string linkage,
            string expectedId,
            string idProp = "characterID",
            string? jpexsPath = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(swfPath) || !File.Exists(swfPath)
                || string.IsNullOrWhiteSpace(linkage) || string.IsNullOrWhiteSpace(expectedId))
                return false;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-sel-script-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await _jpexs.RunFfdecAsync(
                    jpexsPath ?? "",
                    $"-selectclass {linkage} -export script \"{work}\" \"{swfPath}\"",
                    cancellationToken);
                var asFile = Directory.GetFiles(work, linkage + ".as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                if (asFile == null) return false;
                var text = await File.ReadAllTextAsync(asFile, cancellationToken);
                return text.Contains($"class {linkage}", StringComparison.Ordinal)
                       && ScriptHasIdAssignment(text, idProp, expectedId);
            }
            catch
            {
                return false;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Ensures <c>{newId}_select</c> has a working characterID script (AddSelectClass + verify).
        /// <paramref name="playableCharacterId"/> may differ from <paramref name="newId"/> for Crazy CSS
        /// aliases (unique chip linkage, same playable fighter).
        /// </summary>
        public async Task EnsureSelectScriptAsync(
            string menuSwfPath,
            string donorId,
            string newId,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? playableCharacterId = null)
        {
            donorId = SanitizeId(donorId);
            newId = SanitizeId(newId);
            var playable = SanitizeId(
                string.IsNullOrWhiteSpace(playableCharacterId) ? newId : playableCharacterId);
            var donorLink = donorId + "_select";
            var newLink = newId + "_select";
            if (await SelectScriptExistsAsync(
                    menuSwfPath, newLink, playable, "characterID", jpexsPath, cancellationToken))
            {
                progress?.Report($"Select script OK: {newLink}");
                return;
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-ensure-select-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report($"Installing CSS select script {newLink}…");
                var outfile = Path.Combine(work, "menu-select.swf");
                await _jpexs.AddAs3ClassAsync(
                    jpexsPath ?? "", menuSwfPath, outfile, donorLink, newLink, playable,
                    cancellationToken, "characterID");
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("AddSelectClass produced no output for " + newLink);

                var bak = menuSwfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(menuSwfPath, bak, overwrite: false);
                File.Copy(outfile, menuSwfPath, overwrite: true);

                if (!await SelectScriptExistsAsync(
                        menuSwfPath, newLink, playable, "characterID", jpexsPath, cancellationToken))
                {
                    throw new InvalidOperationException(
                        $"CSS select script '{newLink}' was written but verification failed " +
                        $"(characterID != \"{playable}\"). Restoring is available from {Path.GetFileName(bak)}.");
                }

                // Donor must still resolve — catches the old shared-method corruption.
                if (!await SelectScriptExistsAsync(
                        menuSwfPath, donorLink, donorId, "characterID", jpexsPath, cancellationToken))
                {
                    if (File.Exists(bak))
                        File.Copy(bak, menuSwfPath, overwrite: true);
                    throw new InvalidOperationException(
                        $"AddSelectClass corrupted donor '{donorLink}'. Menu restored from backup.");
                }

                progress?.Report($"Select script OK: {newLink}");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Clones <c>{donorId}_select</c> into unique alias chips that all play
        /// <paramref name="playableCharacterId"/>, with optional chip label text.
        /// </summary>
        public Task CloneCssSelectAliasesAsync(
            string menuSwfPath,
            string donorId,
            IReadOnlyList<string> aliasIds,
            string playableCharacterId,
            string? chipLabel = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            donorId = SanitizeId(donorId);
            playableCharacterId = SanitizeId(playableCharacterId);
            var jobs = (aliasIds ?? Array.Empty<string>())
                .Select(SanitizeId)
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(id => (donorId, id, playableCharacterId, chipLabel ?? ""))
                .ToList();
            return CloneCssSelectJobsAsync(menuSwfPath, jobs, jpexsPath, progress, cancellationToken);
        }

        /// <summary>
        /// Rewrites existing <c>{alias}_select</c> scripts so <c>characterID</c> is the
        /// playable fighter id (may differ from the CSS alias / roster id).
        /// </summary>
        public async Task RewriteSelectCharacterIdsBatchAsync(
            string menuSwfPath,
            IReadOnlyList<(string DonorId, string AliasId, string PlayableId)> jobs,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (jobs == null || jobs.Count == 0) return;

            var batch = new List<(string Donor, string Neu, string Id, string Prop)>(jobs.Count);
            foreach (var (donorId, aliasId, playableId) in jobs)
            {
                var d = SanitizeId(donorId);
                var a = SanitizeId(aliasId);
                var p = SanitizeId(playableId);
                if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(p))
                    continue;
                batch.Add((d + "_select", a + "_select", p, "characterID"));
            }
            if (batch.Count == 0) return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-rewrite-cid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var outfile = Path.Combine(work, "menu-cid.swf");
                progress?.Report($"AddSelectClass batch ({batch.Count} characterID rewrite(s))…");
                await _jpexs.AddAs3ClassesBatchAsync(
                    jpexsPath ?? "", menuSwfPath, outfile, batch, cancellationToken);
                if (!File.Exists(outfile))
                    throw new InvalidOperationException("characterID rewrite produced no output SWF.");

                var bak = menuSwfPath + ".ssf2mm.bak";
                if (!File.Exists(bak))
                    File.Copy(menuSwfPath, bak, overwrite: false);
                File.Copy(outfile, menuSwfPath, overwrite: true);
                progress?.Report($"Updated characterID on {batch.Count} select chip(s).");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Batch-clone CSS select chips with per-slot donor / playable id / label.
        /// One SWF↔XML pass + one AddSelectClass JVM for all new scripts.
        /// </summary>
        public async Task CloneCssSelectJobsAsync(
            string menuSwfPath,
            IReadOnlyList<(string DonorId, string AliasId, string PlayableId, string ChipLabel)> jobs,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (jobs == null || jobs.Count == 0) return;

            var normalized = new List<(string DonorId, string AliasId, string PlayableId, string ChipLabel)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (donorId, aliasId, playableId, label) in jobs)
            {
                var d = SanitizeId(donorId);
                var a = SanitizeId(aliasId);
                var p = SanitizeId(playableId);
                if (string.IsNullOrWhiteSpace(d) || string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(p))
                    continue;
                if (!seen.Add(a)) continue;
                normalized.Add((d, a, p, label?.Trim() ?? ""));
            }
            if (normalized.Count == 0) return;

            var clones = normalized
                .Select(j => (j.DonorId + "_select", j.AliasId + "_select", true))
                .ToList();
            var scriptMap = normalized.ToDictionary(
                j => j.AliasId + "_select",
                j => j.PlayableId,
                StringComparer.OrdinalIgnoreCase);

            var linkagesBefore = await ExportLinkageMapAsync(menuSwfPath, jpexsPath, cancellationToken);
            var missingClones = clones
                .Where(c => !linkagesBefore.ContainsKey(c.Item2))
                .ToList();
            if (missingClones.Count > 0)
            {
                progress?.Report($"Crazy CSS: cloning {missingClones.Count} select chip(s)…");
                await CloneLinkagesBatchAsync(
                    menuSwfPath, missingClones, "characterID", jpexsPath, progress, cancellationToken,
                    injectScripts: true, scriptIdByNewLink: scriptMap, shallowClone: true);
            }

            // When chips already exist (re-apply / playable-id fix), rewrite characterID without
            // re-cloning sprites. Fresh clones already get playable ids via scriptMap.
            if (missingClones.Count < normalized.Count)
            {
                progress?.Report($"Crazy CSS: setting playable characterID on {normalized.Count} chip(s)…");
                await RewriteSelectCharacterIdsBatchAsync(
                    menuSwfPath,
                    normalized.Select(j => (j.DonorId, j.AliasId, j.PlayableId)).ToList(),
                    jpexsPath, progress, cancellationToken);
            }

            // Spot-check a few chips instead of re-running AddSelectClass per alias.
            foreach (var sample in normalized.Take(1).Concat(normalized.Skip(Math.Max(0, normalized.Count / 2)).Take(1)))
            {
                if (!await SelectScriptExistsAsync(
                        menuSwfPath, sample.AliasId + "_select", sample.PlayableId,
                        "characterID", jpexsPath, cancellationToken))
                {
                    progress?.Report($"Repairing select script {sample.AliasId}_select…");
                    await EnsureSelectScriptAsync(
                        menuSwfPath, sample.DonorId, sample.AliasId, jpexsPath, progress, cancellationToken,
                        playableCharacterId: sample.PlayableId);
                }
            }

            // Huge Crazy CSS grids: importText only (skip O(n²) PlaceObject scaling).
            var scaleLayout = normalized.Count <= 40;
            foreach (var group in normalized
                         .Where(j => !string.IsNullOrWhiteSpace(j.ChipLabel))
                         .GroupBy(j => j.ChipLabel, StringComparer.Ordinal))
            {
                var labelLinks = group.Select(j => j.AliasId + "_select").ToList();
                progress?.Report($"Crazy CSS: rewriting {labelLinks.Count} chip label(s) → \"{group.Key}\"…");
                await RewriteLinkageLabelsBatchAsync(
                    menuSwfPath, labelLinks, group.Key, jpexsPath, progress, cancellationToken,
                    scaleSelectLayout: scaleLayout);
            }
        }

        private async Task<string> InjectLinkageScriptDualAsync(
            string swfPath,
            string donorLink,
            string newLink,
            string donorId,
            string newId,
            string idProp,
            string? jpexsPath,
            string workRoot,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            return await InjectLinkageScriptsBatchAsync(
                swfPath,
                new[] { (donorLink, newLink, donorId, newId) },
                idProp, jpexsPath, workRoot, progress, cancellationToken);
        }

        private static bool ScriptHasIdAssignment(string asText, string idProp, string id) =>
            asText.Contains($"this.{idProp} = \"{id}\"", StringComparison.Ordinal)
            || asText.Contains($"this.{idProp}=\"{id}\"", StringComparison.Ordinal);

        /// <summary>
        /// Builds a single .as file containing the donor package followed by the new chip class.
        /// FFDec importScript only replaces existing packs; compiling both classes in the donor
        /// file causes the new class to be emitted into the ABC.
        /// </summary>
        public static string BuildDualPackageSelectScript(
            string donorAsText,
            string donorLink,
            string newLink,
            string donorId,
            string newId,
            string idProp)
        {
            var donorOnly = TakeFirstPackageBlock(donorAsText);
            if (string.IsNullOrWhiteSpace(donorOnly))
                throw new InvalidOperationException("Donor script has no package block.");

            var clonePackage = RewriteSelectChipScript(
                donorOnly, donorLink, newLink, donorId, newId, idProp);
            return donorOnly.TrimEnd() + "\r\n" + clonePackage.TrimStart();
        }

        /// <summary>Returns the first top-level <c>package { ... }</c> block (brace-balanced).</summary>
        public static string TakeFirstPackageBlock(string asText)
        {
            if (string.IsNullOrWhiteSpace(asText)) return "";
            var start = asText.IndexOf("package", StringComparison.Ordinal);
            if (start < 0) return asText.Trim();

            var brace = asText.IndexOf('{', start);
            if (brace < 0) return asText.Trim();

            var depth = 0;
            for (var i = brace; i < asText.Length; i++)
            {
                if (asText[i] == '{') depth++;
                else if (asText[i] == '}')
                {
                    depth--;
                    if (depth == 0)
                        return asText.Substring(start, i - start + 1).Trim() + "\r\n";
                }
            }
            return asText.Trim();
        }

        /// <summary>
        /// Rewrites a donor {id}_select / {id}_stage_select script for a new linkage and id property value.
        /// </summary>
        public static string RewriteSelectChipScript(
            string donorAsText,
            string donorLink,
            string newLink,
            string donorId,
            string newId,
            string idProp)
        {
            if (string.IsNullOrWhiteSpace(donorAsText))
                throw new ArgumentException("Donor script is empty.", nameof(donorAsText));

            var text = donorAsText;
            // Decompiler Embed metadata points at the donor sprite id - drop it for reimport.
            text = Regex.Replace(
                text,
                @"^[ \t]*\[Embed\([^\]]*\)\][ \t]*\r?\n",
                "",
                RegexOptions.Multiline);

            text = Regex.Replace(
                text,
                $@"\bclass\s+{Regex.Escape(donorLink)}\b",
                $"class {newLink}");
            text = Regex.Replace(
                text,
                $@"\bfunction\s+{Regex.Escape(donorLink)}\s*\(",
                $"function {newLink}(");
            var hadIdAssign = Regex.IsMatch(
                text,
                $@"this\.{Regex.Escape(idProp)}\s*=\s*""{Regex.Escape(donorId)}""");
            if (hadIdAssign)
            {
                text = Regex.Replace(
                    text,
                    $@"this\.{Regex.Escape(idProp)}\s*=\s*""{Regex.Escape(donorId)}""",
                    $"this.{idProp} = \"{newId}\"");
            }

            if (!text.Contains($"class {newLink}", StringComparison.Ordinal))
                throw new InvalidOperationException($"Failed to rename class to {newLink}.");
            // Many misc2 / congrats stubs have no characterID assignment — class rename is enough.
            if (hadIdAssign && !ScriptHasIdAssignment(text, idProp, newId))
                throw new InvalidOperationException($"Failed to set {idProp} = \"{newId}\".");

            return text;
        }
        /// <summary>
        /// Returns linkage name → character/sprite id from FFDec symbols.csv.
        /// </summary>
        public async Task<Dictionary<string, int>> ExportLinkageMapAsync(
            string swfPath,
            string? jpexsPath,
            CancellationToken cancellationToken)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-symmap-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, work, "symbolClass", cancellationToken);
                var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.GetFiles(work, "*.csv", SearchOption.AllDirectories)
                             .Concat(Directory.GetFiles(work, "*", SearchOption.AllDirectories)))
                {
                    var text = await File.ReadAllTextAsync(file, cancellationToken);
                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"^\s*(?<id>\d+);""(?<n>[^""]+)""",
                                 RegexOptions.Multiline))
                        map[m.Groups["n"].Value] = int.Parse(m.Groups["id"].Value);
                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"^\s*(?<id>\d+);(?<n>[A-Za-z0-9_\.]+)\s*$",
                                 RegexOptions.Multiline))
                        map[m.Groups["n"].Value] = int.Parse(m.Groups["id"].Value);
                }
                return map;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public async Task<IReadOnlyList<string>> ExportLinkageNamesAsync(
            string swfPath,
            string? jpexsPath,
            CancellationToken cancellationToken)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-sym-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, work, "symbolClass", cancellationToken);
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var file in Directory.GetFiles(work, "*", SearchOption.AllDirectories))
                {
                    var text = await File.ReadAllTextAsync(file, cancellationToken);
                    // FFDec symbols.csv: 323;"dedede_select"
                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"^\s*\d+;""(?<n>[^""]+)""",
                                 RegexOptions.Multiline))
                        names.Add(m.Groups["n"].Value);
                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"^\s*\d+;(?<n>[A-Za-z0-9_\.]+)\s*$",
                                 RegexOptions.Multiline))
                        names.Add(m.Groups["n"].Value);
                    foreach (Match m in Regex.Matches(
                                 text,
                                 @"(?:name|linkageName|exportName)\s*[=:]\s*[""']([^""']+)[""']",
                                 RegexOptions.IgnoreCase))
                        names.Add(m.Groups[1].Value);
                }

                // Fallback: swf2xml SymbolClass scrape (heavier) if export was empty.
                if (names.Count == 0)
                {
                    var xmlPath = Path.Combine(work, "fallback.xml");
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                    var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                    foreach (Match m in Regex.Matches(
                                 xml,
                                 @"<item\b[^>]*type\s*=\s*""SymbolClassTag""[\s\S]*?</item>",
                                 RegexOptions.IgnoreCase))
                    {
                        foreach (Match n in Regex.Matches(
                                     m.Value, @"name\s*=\s*""([^""]+)""", RegexOptions.CultureInvariant))
                            names.Add(n.Groups[1].Value);
                    }
                }

                return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public static string SanitizeId(string id)
        {
            id = id.Trim().ToLowerInvariant();
            id = Regex.Replace(id, @"[^a-z0-9_]", "");
            if (string.IsNullOrEmpty(id)) throw new ArgumentException("Id is empty after sanitizing.");
            if (char.IsDigit(id[0])) id = "c" + id;
            return id;
        }

        private static string? FindDataFile(string installRoot, string category, string fileName)
        {
            var preferred = Path.Combine(installRoot, "data", category, fileName);
            if (File.Exists(preferred)) return preferred;
            var data = Path.Combine(installRoot, "data");
            if (!Directory.Exists(data)) return null;
            return Directory.GetFiles(data, fileName, SearchOption.AllDirectories).FirstOrDefault();
        }

        private static int FindMaxSpriteId(string xml)
        {
            var max = 0;
            foreach (Match m in Regex.Matches(
                         xml,
                         @"(?:spriteId|shapeId|characterId)\s*=\s*""(\d+)""",
                         RegexOptions.IgnoreCase))
            {
                if (int.TryParse(m.Groups[1].Value, out var id) && id > max)
                    max = id;
            }
            return max;
        }

        /// <summary>
        /// Rewrites characterId/characterID (or spriteId) values while preserving FFDec attribute casing.
        /// Writing lowercase <c>characterId</c> into a <c>characterID</c> tag causes xml2swf to drop the tag.
        /// </summary>
        private static string RemapIdAttribute(string xmlFragment, string attrName, int oldId, int newId) =>
            Regex.Replace(
                xmlFragment,
                $@"({attrName})\s*=\s*""{oldId}""",
                m => $"{m.Groups[1].Value}=\"{newId}\"",
                RegexOptions.IgnoreCase);

        /// <summary>
        /// Legacy helper used by unit tests â€” aliases SymbolClass to donor id when sprite clone is unavailable.
        /// </summary>
        public static (string Xml, int Touches) DuplicateSymbolInXml(
            string xml,
            int donorCharId,
            int newCharId,
            string donorLink,
            string newLink,
            string donorId,
            string newId,
            string idProp) =>
            DuplicateSpriteAndSymbolClass(xml, donorCharId, newCharId, donorLink, newLink, donorId, newId, idProp);

        /// <summary>
        /// Fast Crazy CSS path: shallow-clone many select sprites with one XML insert + one
        /// SymbolClass rewrite. Private-copies DefineText so labels can be rewritten safely.
        /// </summary>
        private static (
            string Xml,
            int Touches,
            List<(string DonorLink, string NewLink, string DonorId, string NewId)> ScriptJobs
        ) BulkShallowCloneSelectSprites(
            string xml,
            IReadOnlyList<(string DonorLink, string NewLink, int DonorCharId, bool Inject)> pending,
            ref int nextId,
            string? playableOverride,
            IReadOnlyDictionary<string, string>? scriptIdByNewLink,
            IReadOnlyDictionary<string, string>? labelByNewLink = null)
        {
            var scriptJobs = new List<(string DonorLink, string NewLink, string DonorId, string NewId)>();
            if (pending.Count == 0) return (xml, 0, scriptJobs);

            var donorCache = new Dictionary<int, (string SpriteBlock, List<(int OldTextId, string TextBlock)> Texts)>();
            void ExtractDonor(int donorCharId)
            {
                if (donorCache.ContainsKey(donorCharId)) return;
                var spriteOpen = FindSpriteTagOpen(xml, donorCharId)
                    ?? throw new InvalidOperationException($"Donor sprite {donorCharId} missing.");
                var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
                if (spriteEnd <= spriteOpen.Index)
                    throw new InvalidOperationException($"Donor sprite {donorCharId} unclosed.");
                var spriteBlock = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
                var texts = new List<(int, string)>();
                foreach (var textId in FindTextCharacterIdsInSprite(xml, donorCharId))
                {
                    var open = FindCharacterTagOpen(xml, textId, "DefineEditText")
                               ?? FindCharacterTagOpen(xml, textId, "DefineText");
                    if (open is not { Success: true }) continue;
                    var end = FindMatchingItemClose(xml, open.Index);
                    if (end <= open.Index) continue;
                    texts.Add((textId, xml.Substring(open.Index, end - open.Index)));
                }
                donorCache[donorCharId] = (spriteBlock, texts);
            }

            var insertChunks = new StringBuilder(pending.Count * 2048);
            var symbolEntries = new List<(int Id, string Name)>(pending.Count);
            var touches = 0;

            foreach (var (donorLink, newLink, donorCharId, inject) in pending)
            {
                ExtractDonor(donorCharId);
                var (spriteTemplate, textTemplates) = donorCache[donorCharId];
                var newSpriteId = nextId++;
                var spriteClone = RemapIdAttribute(spriteTemplate, "spriteId", donorCharId, newSpriteId);
                string? label = null;
                if (labelByNewLink != null
                    && labelByNewLink.TryGetValue(newLink, out var rawLabel)
                    && !string.IsNullOrWhiteSpace(rawLabel))
                    label = BuildSingleLineChipLabel(rawLabel);

                foreach (var (oldTextId, textBlock) in textTemplates)
                {
                    var newTextId = nextId++;
                    var textClone = RemapIdAttribute(textBlock, "characterId", oldTextId, newTextId);
                    if (label != null)
                    {
                        var escaped = System.Security.SecurityElement.Escape(label) ?? label;
                        textClone = RewriteTextTagContents(textClone, escaped);
                    }
                    spriteClone = RemapIdAttribute(spriteClone, "characterId", oldTextId, newTextId);
                    insertChunks.Append('\n').Append(textClone).Append('\n');
                    touches++;
                }

                insertChunks.Append('\n').Append(spriteClone).Append('\n');
                touches++;
                symbolEntries.Add((newSpriteId, newLink));

                if (inject)
                {
                    var donorId = GuessIdFromLinkage(donorLink);
                    var newId = GuessIdFromLinkage(newLink);
                    string scriptId = newId;
                    if (scriptIdByNewLink != null
                        && scriptIdByNewLink.TryGetValue(newLink, out var perLink)
                        && !string.IsNullOrWhiteSpace(perLink))
                        scriptId = SanitizeId(perLink);
                    else if (playableOverride != null)
                        scriptId = playableOverride;
                    scriptJobs.Add((donorLink, newLink, donorId, scriptId));
                }
            }

            // Insert all new tags once after the first donor sprite (stable anchor).
            var anchorDonor = pending[0].DonorCharId;
            var anchorOpen = FindSpriteTagOpen(xml, anchorDonor)
                ?? throw new InvalidOperationException("Bulk clone anchor sprite missing.");
            var anchorEnd = FindMatchingItemClose(xml, anchorOpen.Index);
            if (anchorEnd <= anchorOpen.Index)
                throw new InvalidOperationException("Bulk clone anchor sprite unclosed.");
            xml = xml.Substring(0, anchorEnd) + insertChunks + xml.Substring(anchorEnd);

            xml = BulkAppendSymbolClassEntries(xml, symbolEntries);
            touches += symbolEntries.Count;
            return (xml, touches, scriptJobs);
        }

        private static string BulkAppendSymbolClassEntries(
            string xml, IReadOnlyList<(int CharacterId, string LinkageName)> entries)
        {
            if (entries.Count == 0) return xml;
            var symIdx = xml.IndexOf("type=\"SymbolClassTag\"", StringComparison.OrdinalIgnoreCase);
            if (symIdx < 0)
                symIdx = xml.IndexOf("type='SymbolClassTag'", StringComparison.OrdinalIgnoreCase);
            if (symIdx < 0) return xml;

            var itemStart = xml.LastIndexOf("<item", symIdx, StringComparison.OrdinalIgnoreCase);
            if (itemStart < 0) return xml;
            var symEnd = FindMatchingItemClose(xml, itemStart);
            if (symEnd <= itemStart) return xml;

            var symBlock = xml.Substring(itemStart, symEnd - itemStart);
            var tagsSb = new StringBuilder();
            var namesSb = new StringBuilder();
            foreach (var (id, name) in entries)
            {
                var nameItem = $"<item>{name}</item>";
                if (symBlock.Contains(nameItem, StringComparison.Ordinal)) continue;
                tagsSb.Append("        <item>").Append(id).Append("</item>\n      ");
                namesSb.Append("        <item>").Append(name).Append("</item>\n      ");
            }
            if (tagsSb.Length == 0) return xml;

            var tagsClose = symBlock.IndexOf("</tags>", StringComparison.OrdinalIgnoreCase);
            var namesClose = symBlock.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
            if (tagsClose < 0 || namesClose < 0) return xml;
            symBlock = symBlock.Insert(tagsClose, tagsSb.ToString());
            namesClose = symBlock.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
            symBlock = symBlock.Insert(namesClose, namesSb.ToString());
            return xml.Substring(0, itemStart) + symBlock + xml.Substring(symEnd);
        }

        /// <summary>
        /// Clones a donor character (DefineSprite OR DefineBits*) to a new id and registers
        /// SymbolClass for <paramref name="newLink"/>. Never aliases the donor id — that left
        /// congrats_mario / congrats_clone sharing one chid with no replaceable art.
        /// </summary>
        public static (string Xml, int Touches) DuplicateSpriteAndSymbolClass(
            string xml,
            int donorCharId,
            int newCharId,
            string donorLink,
            string newLink,
            string donorId,
            string newId,
            string idProp,
            bool deepCloneNested = true)
        {
            var touches = 0;
            _ = idProp;
            _ = donorId;
            _ = newId;
            _ = donorLink;

            var clonedTag = false;

            // 1) DefineSpriteTag (CSS chip / PA sprites)
            var spriteOpen = Regex.Match(
                xml,
                $@"<item\b[^>]*type\s*=\s*""DefineSpriteTag""[^>]*spriteId\s*=\s*""{donorCharId}""[^>]*>",
                RegexOptions.IgnoreCase);
            if (!spriteOpen.Success)
            {
                spriteOpen = Regex.Match(
                    xml,
                    $@"<item\b[^>]*spriteId\s*=\s*""{donorCharId}""[^>]*type\s*=\s*""DefineSpriteTag""[^>]*>",
                    RegexOptions.IgnoreCase);
            }

            if (spriteOpen.Success)
            {
                var end = FindMatchingItemClose(xml, spriteOpen.Index);
                if (end > spriteOpen.Index)
                {
                    var block = xml.Substring(spriteOpen.Index, end - spriteOpen.Index);
                    var cloned = RemapIdAttribute(block, "spriteId", donorCharId, newCharId);

                    // Shallow: sprite shell only. Art uniquify / text uniquify private-copy later
                    // (deep-cloning JPEG DefineBits via xml.Insert on misc2 is O(n²) and multi-minute).
                    if (!deepCloneNested)
                    {
                        xml = xml.Insert(end, "\n" + cloned + "\n");
                        touches++;
                        clonedTag = true;
                    }
                    else
                    {
                    // Deep-clone nested DefineBits / DefineText / anonymous PA sprites so the
                    // clone does not share donor pixels. Keep shared series-symbol PlaceObjects.
                    var nestedCloneIds = new List<int>();
                    foreach (Match m in Regex.Matches(cloned, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                    {
                        if (!int.TryParse(m.Groups[1].Value, out var cid) || cid == donorCharId || cid == newCharId)
                            continue;
                        if (nestedCloneIds.Contains(cid)) continue;

                        var linkName = FindFirstLinkageNameForCharacterId(xml, cid);
                        if (linkName != null && IsSeriesSymbolLinkageName(linkName))
                            continue; // shared series icon

                        if (IsDefineBitsInXml(xml, cid) || IsDefineTextInXml(xml, cid)
                            || IsDefineSpriteInXml(xml, cid) || IsDefineShapeInXml(xml, cid))
                            nestedCloneIds.Add(cid);
                    }

                    var insertAt = end;
                    var nextNestedId = Math.Max(newCharId, FindMaxSpriteId(xml)) + 1;
                    foreach (var oldNestedId in nestedCloneIds)
                    {
                        if (IsDefineShapeInXml(xml, oldNestedId))
                        {
                            var shapeOpen = FindShapeTagOpen(xml, oldNestedId);
                            if (shapeOpen == null) continue;
                            var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                            if (shapeEnd <= shapeOpen.Index) continue;

                            var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                            var newShapeId = nextNestedId++;
                            var clonedShape = RemapIdAttribute(shapeBlock, "shapeId", oldNestedId, newShapeId);

                            // Clone bitmap fills inside the shape.
                            foreach (Match bm in Regex.Matches(
                                         shapeBlock, @"bitmapId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                            {
                                if (!int.TryParse(bm.Groups[1].Value, out var bid) || bid == 0 || bid == 65535)
                                    continue;
                                if (!IsDefineBitsInXml(xml, bid)) continue;
                                var fillBitsOpen = FindCharacterTagOpen(xml, bid, "DefineBits");
                                if (fillBitsOpen is not { Success: true }) continue;
                                var fillBitsEnd = FindMatchingItemClose(xml, fillBitsOpen.Index);
                                if (fillBitsEnd <= fillBitsOpen.Index) continue;
                                var fillBitsBlock = xml.Substring(fillBitsOpen.Index, fillBitsEnd - fillBitsOpen.Index);
                                var newBitsId = nextNestedId++;
                                var clonedBits = RemapIdAttribute(fillBitsBlock, "characterId", bid, newBitsId);
                                clonedShape = Regex.Replace(
                                    clonedShape,
                                    $@"bitmapId\s*=\s*""{bid}""",
                                    $"bitmapId=\"{newBitsId}\"",
                                    RegexOptions.IgnoreCase);
                                xml = xml.Insert(insertAt, "\n" + clonedBits + "\n");
                                insertAt += clonedBits.Length + 2;
                                touches++;
                            }

                            cloned = RemapIdAttribute(cloned, "characterId", oldNestedId, newShapeId);
                            xml = xml.Insert(insertAt, "\n" + clonedShape + "\n");
                            insertAt += clonedShape.Length + 2;
                            touches++;
                            continue;
                        }

                        if (IsDefineSpriteInXml(xml, oldNestedId))
                        {
                            var nestOpen = FindSpriteTagOpen(xml, oldNestedId);
                            if (nestOpen == null) continue;
                            var nestEnd = FindMatchingItemClose(xml, nestOpen.Index);
                            if (nestEnd <= nestOpen.Index) continue;

                            var nestedBlock = xml.Substring(nestOpen.Index, nestEnd - nestOpen.Index);
                            var newNestedId = nextNestedId++;
                            var clonedNested = RemapIdAttribute(nestedBlock, "spriteId", oldNestedId, newNestedId);

                            // Uniquify bits/text/shapes inside the nested PA sprite.
                            foreach (Match im in Regex.Matches(
                                         nestedBlock, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                            {
                                if (!int.TryParse(im.Groups[1].Value, out var innerId) || innerId == oldNestedId)
                                    continue;
                                var innerLink = FindFirstLinkageNameForCharacterId(xml, innerId);
                                if (innerLink != null && IsSeriesSymbolLinkageName(innerLink))
                                    continue;

                                if (IsDefineShapeInXml(xml, innerId))
                                {
                                    var shapeOpen = FindShapeTagOpen(xml, innerId);
                                    if (shapeOpen == null) continue;
                                    var shapeEnd = FindMatchingItemClose(xml, shapeOpen.Index);
                                    if (shapeEnd <= shapeOpen.Index) continue;
                                    var shapeBlock = xml.Substring(shapeOpen.Index, shapeEnd - shapeOpen.Index);
                                    var newShapeId = nextNestedId++;
                                    var clonedShape = RemapIdAttribute(shapeBlock, "shapeId", innerId, newShapeId);
                                    foreach (Match bm in Regex.Matches(
                                                 shapeBlock, @"bitmapId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                                    {
                                        if (!int.TryParse(bm.Groups[1].Value, out var bid) || bid == 0 || bid == 65535)
                                            continue;
                                        if (!IsDefineBitsInXml(xml, bid)) continue;
                                        var fillBitsOpen = FindCharacterTagOpen(xml, bid, "DefineBits");
                                        if (fillBitsOpen is not { Success: true }) continue;
                                        var fillBitsEnd = FindMatchingItemClose(xml, fillBitsOpen.Index);
                                        if (fillBitsEnd <= fillBitsOpen.Index) continue;
                                        var fillBitsBlock = xml.Substring(fillBitsOpen.Index, fillBitsEnd - fillBitsOpen.Index);
                                        var newBitsId = nextNestedId++;
                                        var clonedBits = RemapIdAttribute(fillBitsBlock, "characterId", bid, newBitsId);
                                        clonedShape = Regex.Replace(
                                            clonedShape,
                                            $@"bitmapId\s*=\s*""{bid}""",
                                            $"bitmapId=\"{newBitsId}\"",
                                            RegexOptions.IgnoreCase);
                                        xml = xml.Insert(insertAt, "\n" + clonedBits + "\n");
                                        insertAt += clonedBits.Length + 2;
                                        touches++;
                                    }
                                    clonedNested = RemapIdAttribute(clonedNested, "characterId", innerId, newShapeId);
                                    xml = xml.Insert(insertAt, "\n" + clonedShape + "\n");
                                    insertAt += clonedShape.Length + 2;
                                    touches++;
                                    continue;
                                }

                                var innerOpen = FindCharacterTagOpen(xml, innerId, "DefineBits")
                                                ?? FindCharacterTagOpen(xml, innerId, "DefineEditText")
                                                ?? FindCharacterTagOpen(xml, innerId, "DefineText");
                                if (innerOpen is not { Success: true }) continue;
                                var innerEnd = FindMatchingItemClose(xml, innerOpen.Index);
                                if (innerEnd <= innerOpen.Index) continue;
                                var innerBlock = xml.Substring(innerOpen.Index, innerEnd - innerOpen.Index);
                                var newInnerId = nextNestedId++;
                                var clonedInner = RemapIdAttribute(innerBlock, "characterId", innerId, newInnerId);
                                clonedNested = RemapIdAttribute(clonedNested, "characterId", innerId, newInnerId);
                                xml = xml.Insert(insertAt, "\n" + clonedInner + "\n");
                                insertAt += clonedInner.Length + 2;
                                touches++;
                            }

                            cloned = RemapIdAttribute(cloned, "characterId", oldNestedId, newNestedId);
                            xml = xml.Insert(insertAt, "\n" + clonedNested + "\n");
                            insertAt += clonedNested.Length + 2;
                            touches++;
                            continue;
                        }

                        var nestedOpen = FindCharacterTagOpen(xml, oldNestedId, "DefineBits")
                                         ?? FindCharacterTagOpen(xml, oldNestedId, "DefineEditText")
                                         ?? FindCharacterTagOpen(xml, oldNestedId, "DefineText");
                        if (nestedOpen is not { Success: true }) continue;
                        var bitsEnd = FindMatchingItemClose(xml, nestedOpen.Index);
                        if (bitsEnd <= nestedOpen.Index) continue;

                        var leafBlock = xml.Substring(nestedOpen.Index, bitsEnd - nestedOpen.Index);
                        var newLeafId = nextNestedId++;
                        var clonedLeaf = RemapIdAttribute(leafBlock, "characterId", oldNestedId, newLeafId);
                        cloned = RemapIdAttribute(cloned, "characterId", oldNestedId, newLeafId);
                        xml = xml.Insert(insertAt, "\n" + clonedLeaf + "\n");
                        insertAt += clonedLeaf.Length + 2;
                        touches++;
                    }

                    xml = xml.Insert(insertAt, "\n" + cloned + "\n");
                    touches++;
                    clonedTag = true;
                    } // end deepCloneNested
                }
            }

            // 2) DefineBits* (classic/allstar congrats are often image linkages, not sprites)
            if (!clonedTag)
            {
                var bitsOpen = Regex.Match(
                    xml,
                    $@"<item\b[^>]*type\s*=\s*""DefineBits[^""]*""[^>]*characterId\s*=\s*""{donorCharId}""[^>]*>",
                    RegexOptions.IgnoreCase);
                if (!bitsOpen.Success)
                {
                    bitsOpen = Regex.Match(
                        xml,
                        $@"<item\b[^>]*characterId\s*=\s*""{donorCharId}""[^>]*type\s*=\s*""DefineBits[^""]*""[^>]*>",
                        RegexOptions.IgnoreCase);
                }

                if (bitsOpen.Success)
                {
                    var end = FindMatchingItemClose(xml, bitsOpen.Index);
                    if (end > bitsOpen.Index)
                    {
                        var block = xml.Substring(bitsOpen.Index, end - bitsOpen.Index);
                        var cloned = RemapIdAttribute(block, "characterId", donorCharId, newCharId);
                        xml = xml.Insert(end, "\n" + cloned + "\n");
                        touches++;
                        clonedTag = true;
                    }
                }
            }

            if (!clonedTag)
                throw new InvalidOperationException(
                    $"Donor character {donorCharId} is neither DefineSprite nor DefineBits — cannot clone linkage.");

            // Always register SymbolClass against the NEW character id (never alias donor).
            var before = xml;
            xml = AppendSymbolClassEntry(xml, newCharId, newLink, addNewTagId: true);
            if (!before.Equals(xml, StringComparison.Ordinal))
                touches++;

            return (xml, touches);
        }

        private static int FindMatchingItemClose(string xml, int openItemIndex)
        {
            // Find the end of the opening tag, then balance nested <item> ... </item>.
            var gt = xml.IndexOf('>', openItemIndex);
            if (gt < 0) return -1;
            if (xml[gt - 1] == '/') return gt + 1;

            var depth = 1;
            var i = gt + 1;
            while (i < xml.Length - 6 && depth > 0)
            {
                var nextOpen = xml.IndexOf("<item", i, StringComparison.OrdinalIgnoreCase);
                var nextClose = xml.IndexOf("</item>", i, StringComparison.OrdinalIgnoreCase);
                if (nextClose < 0) return -1;
                if (nextOpen >= 0 && nextOpen < nextClose)
                {
                    // Self-closing?
                    var openEnd = xml.IndexOf('>', nextOpen);
                    if (openEnd > 0 && xml[openEnd - 1] == '/')
                    {
                        i = openEnd + 1;
                        continue;
                    }
                    depth++;
                    i = nextOpen + 5;
                }
                else
                {
                    depth--;
                    i = nextClose + 7;
                    if (depth == 0) return i;
                }
            }
            return -1;
        }

        private static string AppendSymbolClassEntry(
            string xml, int characterId, string linkageName, bool addNewTagId = true)
        {
            // FFDec: <item type="SymbolClassTag"> <tags>...<item>id</item></tags> <names>...<item>name</item></names>
            // Use IndexOf surgery — Regex [\s\S]*? over multi‑MB misc2 XML is multi-second per call.
            var nameItem = $"<item>{linkageName}</item>";
            var symIdx = xml.IndexOf("type=\"SymbolClassTag\"", StringComparison.OrdinalIgnoreCase);
            if (symIdx < 0)
                symIdx = xml.IndexOf("type='SymbolClassTag'", StringComparison.OrdinalIgnoreCase);
            if (symIdx < 0) return xml;

            var itemStart = xml.LastIndexOf("<item", symIdx, StringComparison.OrdinalIgnoreCase);
            if (itemStart < 0) return xml;
            var symEnd = FindMatchingItemClose(xml, itemStart);
            if (symEnd <= itemStart) return xml;

            var symBlock = xml.Substring(itemStart, symEnd - itemStart);
            if (symBlock.Contains(nameItem, StringComparison.Ordinal))
                return xml;

            var tagsClose = symBlock.IndexOf("</tags>", StringComparison.OrdinalIgnoreCase);
            var namesClose = symBlock.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
            if (tagsClose < 0 || namesClose < 0) return xml;

            if (addNewTagId)
            {
                symBlock = symBlock.Insert(
                    tagsClose, $"        <item>{characterId}</item>\n      ");
                namesClose = symBlock.IndexOf("</names>", StringComparison.OrdinalIgnoreCase);
            }
            symBlock = symBlock.Insert(
                namesClose, $"        <item>{linkageName}</item>\n      ");

            return xml.Substring(0, itemStart) + symBlock + xml.Substring(symEnd);
        }

        private async Task<string> TryReplaceLargestImageAsync(
            string swfPath,
            string pngPath,
            string? jpexsPath,
            string workRoot,
            CancellationToken cancellationToken)
        {
            try
            {
                var exportDir = Path.Combine(workRoot, "img-export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", swfPath, exportDir, "image", cancellationToken);
                var images = Directory.GetFiles(exportDir, "*.*", SearchOption.AllDirectories)
                    .Where(f =>
                    {
                        var e = Path.GetExtension(f).ToLowerInvariant();
                        return e is ".png" or ".jpg" or ".jpeg" or ".gif";
                    })
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .ToList();
                if (images.Count == 0) return swfPath;

                // Prefer an image whose name suggests the select chip; else largest.
                var target = images.FirstOrDefault(f =>
                                 Path.GetFileName(f).Contains("select", StringComparison.OrdinalIgnoreCase))
                             ?? images[0];
                var idMatch = Regex.Match(Path.GetFileName(target), @"^(\d+)");
                if (!idMatch.Success) return swfPath;

                var outSwf = Path.Combine(workRoot, "img-replaced.swf");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", swfPath, outSwf, idMatch.Groups[1].Value, pngPath, cancellationToken);
                return File.Exists(outSwf) ? outSwf : swfPath;
            }
            catch
            {
                return swfPath;
            }
        }

        /// <summary>
        /// Clears DefineText tags in menu_characters.swf whose exported text is "btn_gameMode"
        /// (stray label that shows on CSS). Searches by text content so character ids can vary.
        /// </summary>
        public async Task<int> ClearBtnGameModeTextsAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var swf = FindMenuCharacters(installRoot);
            if (swf == null || !File.Exists(swf))
            {
                progress?.Report("menu_characters.swf not found; skip btn_gameMode text clear.");
                return 0;
            }

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-btngm-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var exportDir = Path.Combine(work, "text");
                Directory.CreateDirectory(exportDir);
                progress?.Report("Scanning menu_characters for btn_gameMode DefineTextâ€¦");
                await _jpexs.ExportAsync(jpexsPath ?? "", swf, exportDir, "text", cancellationToken);

                var hits = new List<(string Id, string EmptyPath)>();
                foreach (var file in Directory.GetFiles(exportDir, "*.txt", SearchOption.AllDirectories))
                {
                    var body = (await File.ReadAllTextAsync(file, cancellationToken)).Trim();
                    if (!body.Equals("btn_gameMode", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var id = Path.GetFileNameWithoutExtension(file);
                    // FFDec may export as "113.txt" or nested "texts/113.txt"
                    if (!Regex.IsMatch(id, @"^\d+$"))
                    {
                        var m = Regex.Match(Path.GetFileName(file), @"(\d+)");
                        if (!m.Success) continue;
                        id = m.Groups[1].Value;
                    }

                    var emptyPath = Path.Combine(work, $"empty-{id}.txt");
                    await File.WriteAllTextAsync(emptyPath, "", cancellationToken);
                    hits.Add((id, emptyPath));
                }

                if (hits.Count == 0)
                {
                    progress?.Report("No btn_gameMode DefineText found (already clear).");
                    return 0;
                }

                var bak = swf + ".ssf2mm-btngm.bak";
                if (!File.Exists(bak))
                    File.Copy(swf, bak, overwrite: false);

                var outfile = Path.Combine(work, "menu_characters-cleared.swf");
                progress?.Report($"Clearing {hits.Count} btn_gameMode DefineText tag(s)â€¦");
                await _jpexs.ReplaceManyAsync(
                    jpexsPath ?? "",
                    swf,
                    outfile,
                    hits.Select(h => (h.Id, h.EmptyPath)).ToList(),
                    cancellationToken);

                File.Copy(outfile, swf, overwrite: true);
                progress?.Report($"Cleared btn_gameMode text in {Path.GetFileName(swf)}.");
                return hits.Count;
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>True for series / franchise icons (Kirby_Symbol, piticon, smash_symbol, …).</summary>
        public static bool IsSeriesSymbolLinkageName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (Regex.IsMatch(
                    name,
                    @"_(stock0?|classic(?:_trophy)?|charselect|cssp|event|allstar|select)$",
                    RegexOptions.IgnoreCase))
                return false;
            if (name.StartsWith("congrats_", StringComparison.OrdinalIgnoreCase)
                || name.StartsWith("allstar_congrats_", StringComparison.OrdinalIgnoreCase))
                return false;
            if (name.Equals("stagePreviewer", StringComparison.OrdinalIgnoreCase)) return false;
            if (Regex.IsMatch(name, @"_mc$", RegexOptions.IgnoreCase)) return false;
            // UI chrome / buttons — not franchise badges.
            if (Regex.IsMatch(name, @"^(btn_|button_|mc_|ui_|HUD)", RegexOptions.IgnoreCase))
                return false;

            if (name.Equals("fzero", StringComparison.OrdinalIgnoreCase)
                || name.Equals("talesOf", StringComparison.OrdinalIgnoreCase)
                || name.Equals("bmseries", StringComparison.OrdinalIgnoreCase)
                || name.Equals("warioseries", StringComparison.OrdinalIgnoreCase))
                return true;

            // Franchise badges end with symbol/icon/sign/series/SSS — avoid mid-name "icon" hits.
            return Regex.IsMatch(
                name,
                @"(symbol\d*|icon\d*|sign|series|seriesIcon|SSS)$",
                RegexOptions.IgnoreCase);
        }

        /// <summary>Crop a PNG to a pixel rectangle (clamped to image bounds).</summary>
        public static void CropPng(string srcPng, string destPng, int x, int y, int w, int h)
        {
            using var src = Image.FromFile(srcPng);
            x = Math.Clamp(x, 0, Math.Max(0, src.Width - 1));
            y = Math.Clamp(y, 0, Math.Max(0, src.Height - 1));
            w = Math.Max(1, Math.Min(w, src.Width - x));
            h = Math.Max(1, Math.Min(h, src.Height - y));
            using var dest = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(dest))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                g.DrawImage(
                    src,
                    new Rectangle(0, 0, w, h),
                    new Rectangle(x, y, w, h),
                    GraphicsUnit.Pixel);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(destPng)!);
            dest.Save(destPng, ImageFormat.Png);
        }

        public static string? FindFirstLinkageNameForCharacterId(string xml, int characterId)
        {
            foreach (Match m in Regex.Matches(
                         xml,
                         @"(<item\b[^>]*type\s*=\s*""SymbolClassTag""[^>]*>\s*<tags>)([\s\S]*?)(</tags>\s*<names>)([\s\S]*?)(</names>)",
                         RegexOptions.IgnoreCase))
            {
                var tags = Regex.Matches(m.Groups[2].Value, @"<item>(\d+)</item>");
                var names = Regex.Matches(m.Groups[4].Value, @"<item>([^<]*)</item>");
                var count = Math.Min(tags.Count, names.Count);
                for (var i = 0; i < count; i++)
                {
                    if (tags[i].Groups[1].Value == characterId.ToString())
                        return names[i].Groups[1].Value;
                }
            }
            return null;
        }

        /// <summary>
        /// Appends missing names into Misc2.as <c>register("resources").movieclips</c>.
        /// </summary>
        public static string AppendMisc2Movieclips(string asText, IEnumerable<string> namesToAdd)
        {
            var toAdd = namesToAdd
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(n => !Regex.IsMatch(asText, $@"[""']{Regex.Escape(n)}[""']", RegexOptions.IgnoreCase))
                .ToList();
            if (toAdd.Count == 0) return asText;

            var m = Regex.Match(asText, @"[""']movieclips[""']\s*:\s*\[", RegexOptions.IgnoreCase);
            if (!m.Success)
                throw new InvalidOperationException("Misc2.as: movieclips array not found.");

            var startBracket = asText.IndexOf('[', m.Index);
            if (startBracket < 0)
                throw new InvalidOperationException("Misc2.as: movieclips '[' not found.");

            var depth = 0;
            var end = -1;
            for (var i = startBracket; i < asText.Length; i++)
            {
                if (asText[i] == '[') depth++;
                else if (asText[i] == ']')
                {
                    depth--;
                    if (depth == 0)
                    {
                        end = i;
                        break;
                    }
                }
            }
            if (end < 0)
                throw new InvalidOperationException("Misc2.as: movieclips ']' not found.");

            var insert = string.Concat(toAdd.Select(n => $",\"{n}\""));
            return asText.Insert(end, insert);
        }

        public async Task<IReadOnlyList<string>> ListSeriesSymbolLinkagesAsync(
            string installRoot,
            string? jpexsPath = null,
            CancellationToken cancellationToken = default)
        {
            var misc2 = FindMisc2(installRoot);
            if (misc2 == null || !File.Exists(misc2)) return Array.Empty<string>();
            var names = await ExportLinkageNamesAsync(misc2, jpexsPath, cancellationToken);
            return names
                .Where(IsSeriesSymbolLinkageName)
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Retargets PlaceObject refs inside <c>{characterId}_event</c> from the donor series
        /// symbol character id to the chosen series symbol character id.
        /// </summary>
        public async Task RetargetEventSeriesSymbolAsync(
            string swfPath,
            string characterId,
            string donorSeriesIcon,
            string targetSeriesIcon,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(targetSeriesIcon)) return;
            if (string.Equals(donorSeriesIcon, targetSeriesIcon, StringComparison.OrdinalIgnoreCase))
                return;

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-evtsym-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var map = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
                var eventLink = characterId + "_event";
                if (!map.TryGetValue(eventLink, out var eventSpriteId))
                {
                    progress?.Report($"Skip series retarget: {eventLink} missing.");
                    return;
                }
                if (!map.TryGetValue(targetSeriesIcon, out var targetId))
                    throw new InvalidOperationException("Series symbol not found: " + targetSeriesIcon);

                int? donorId = null;
                if (!string.IsNullOrWhiteSpace(donorSeriesIcon)
                    && map.TryGetValue(donorSeriesIcon, out var d))
                    donorId = d;
                else
                {
                    // Detect which PlaceObject under event is a series symbol.
                    var xmlProbePath = Path.Combine(work, "probe.xml");
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlProbePath, cancellationToken);
                    var xmlProbe = await File.ReadAllTextAsync(xmlProbePath, cancellationToken);
                    var open = FindSpriteTagOpen(xmlProbe, eventSpriteId);
                    if (open != null)
                    {
                        var close = FindMatchingItemClose(xmlProbe, open.Index);
                        var block = xmlProbe.Substring(open.Index, close - open.Index);
                        foreach (Match pm in Regex.Matches(
                                     block, @"characterId\s*=\s*""(\d+)""", RegexOptions.IgnoreCase))
                        {
                            if (!int.TryParse(pm.Groups[1].Value, out var cid)) continue;
                            var name = FindFirstLinkageNameForCharacterId(xmlProbe, cid);
                            if (name != null && IsSeriesSymbolLinkageName(name))
                            {
                                donorId = cid;
                                donorSeriesIcon = name;
                                break;
                            }
                        }
                    }
                }

                if (donorId == null || donorId == targetId)
                {
                    progress?.Report(
                        donorId == targetId
                            ? $"Event already uses {targetSeriesIcon}."
                            : "No donor series symbol PlaceObject on event.");
                    return;
                }

                var xmlPath = Path.Combine(work, "event.xml");
                if (!File.Exists(xmlPath))
                    await _jpexs.SwfToXmlAsync(jpexsPath ?? "", swfPath, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var spriteOpen = FindSpriteTagOpen(xml, eventSpriteId);
                if (spriteOpen == null) return;
                var spriteEnd = FindMatchingItemClose(xml, spriteOpen.Index);
                if (spriteEnd <= spriteOpen.Index) return;
                var spriteBlock = xml.Substring(spriteOpen.Index, spriteEnd - spriteOpen.Index);
                var remapped = RemapIdAttribute(spriteBlock, "characterId", donorId.Value, targetId);
                if (string.Equals(spriteBlock, remapped, StringComparison.Ordinal))
                {
                    progress?.Report("Event series PlaceObject unchanged.");
                    return;
                }

                xml = xml.Substring(0, spriteOpen.Index) + remapped + xml.Substring(spriteEnd);
                await File.WriteAllTextAsync(
                    xmlPath, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false), cancellationToken);
                var outSwf = Path.Combine(work, "retarget.swf");
                await _jpexs.XmlToSwfAsync(jpexsPath ?? "", xmlPath, outSwf, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("Failed to rebuild SWF after series retarget.");
                var bak = swfPath + ".ssf2mm.bak";
                if (!File.Exists(bak)) File.Copy(swfPath, bak, overwrite: false);
                File.Copy(outSwf, swfPath, overwrite: true);
                progress?.Report(
                    $"Retargeted {eventLink} series icon {donorSeriesIcon} → {targetSeriesIcon}.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        /// <summary>Clones an existing series-symbol linkage to a new name (optional PNG replace).</summary>
        public async Task CloneSeriesSymbolAsync(
            string swfPath,
            string templateLinkage,
            string newLinkage,
            string? replacementPng = null,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(templateLinkage) || string.IsNullOrWhiteSpace(newLinkage))
                throw new ArgumentException("Series symbol template and new name are required.");
            if (string.Equals(templateLinkage, newLinkage, StringComparison.OrdinalIgnoreCase))
                return;

            var map = await ExportLinkageMapAsync(swfPath, jpexsPath, cancellationToken);
            if (map.ContainsKey(newLinkage))
            {
                progress?.Report($"Series symbol {newLinkage} already exists.");
                return;
            }
            if (!map.ContainsKey(templateLinkage))
                throw new InvalidOperationException("Series symbol template not found: " + templateLinkage);

            progress?.Report($"Cloning series symbol {templateLinkage} → {newLinkage}…");
            await CloneLinkagesBatchAsync(
                swfPath,
                new[] { (templateLinkage, newLinkage, InjectScript: true) },
                "characterID",
                jpexsPath,
                progress,
                cancellationToken);

            if (!string.IsNullOrWhiteSpace(replacementPng) && File.Exists(replacementPng))
            {
                await ReplaceArtsForLinkagesBatchAsync(
                    swfPath,
                    new[] { (newLinkage, replacementPng!) },
                    jpexsPath,
                    progress,
                    cancellationToken);
            }
        }
    }
}
