using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SSF2ModManager.Services
{
    public class CharacterPackageManifest
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DonorId { get; set; } = "";
        public string SeriesIcon { get; set; } = "smash_symbol";
        public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedUtc { get; set; }
    }

    public class CharacterPackageInfo
    {
        public string FolderPath { get; set; } = "";
        public string FolderName { get; set; } = "";
        public CharacterPackageManifest Manifest { get; set; } = new();
        public string SwfPath => Path.Combine(FolderPath, Manifest.Id + ".swf");
        public string AssetsDir => Path.Combine(FolderPath, "assets");
        public string FlaDir => Path.Combine(FolderPath, "FLA");
        public string CostumesPath => Path.Combine(FolderPath, "costumes.json");
        public string SymbolPath => Path.Combine(AssetsDir, "symbol.png");
        public string ManifestPath => Path.Combine(FolderPath, "character.json");
        public bool SwfExists => File.Exists(SwfPath);
    }

    public class CharacterUpdatePrefs
    {
        public bool UpdateAssets { get; set; } = true;
        public bool UpdateSwf { get; set; } = true;
    }

    /// <summary>
    /// Per-character edit packages under <c>data/character/{DisplayName}/</c>.
    /// </summary>
    public class CharacterPackageService
    {
        private readonly JpexsCliService _jpexs;
        private readonly CharacterAssetService _assets;
        private readonly CharacterCloneService _clone;

        public CharacterPackageService(
            JpexsCliService? jpexs = null,
            CharacterAssetService? assets = null,
            CharacterCloneService? clone = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _assets = assets ?? new CharacterAssetService(_jpexs);
            _clone = clone ?? new CharacterCloneService(_jpexs);
        }

        public static string CharacterDataDir(string installRoot) =>
            Path.Combine(installRoot, "data", "character");

        /// <summary>Folder-safe display name (keeps spaces; strips path-illegal chars).</summary>
        public static string SanitizeFolderName(string displayName)
        {
            var name = (displayName ?? "").Trim();
            if (string.IsNullOrEmpty(name)) name = "Character";
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            name = name.Trim('.', ' ');
            return string.IsNullOrEmpty(name) ? "Character" : name;
        }

        /// <summary>Mappings-relative SWF path: <c>{DisplayName}/{id}.swf</c>.</summary>
        public static string MappingsRelativeSwfPath(string displayName, string id)
        {
            var folder = SanitizeFolderName(displayName);
            var safeId = MenuSlotService.SanitizeId(id);
            return folder + "/" + safeId + ".swf";
        }

        public static string PackageFolderPath(string installRoot, string displayName) =>
            Path.Combine(CharacterDataDir(installRoot), SanitizeFolderName(displayName));

        public static CharacterPackageManifest? TryReadManifest(string folderPath)
        {
            var path = Path.Combine(folderPath, "character.json");
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<CharacterPackageManifest>(File.ReadAllText(path));
            }
            catch { return null; }
        }

        public static IReadOnlyList<CharacterPackageInfo> ScanPackages(string installRoot)
        {
            var root = CharacterDataDir(installRoot);
            var list = new List<CharacterPackageInfo>();
            if (!Directory.Exists(root)) return list;

            foreach (var dir in Directory.GetDirectories(root))
            {
                var name = Path.GetFileName(dir);
                if (string.Equals(name, "expansions", StringComparison.OrdinalIgnoreCase))
                    continue;
                var manifest = TryReadManifest(dir);
                if (manifest == null)
                {
                    // Infer from a single *.swf in the folder.
                    var swfs = Directory.GetFiles(dir, "*.swf");
                    if (swfs.Length == 0) continue;
                    var id = Path.GetFileNameWithoutExtension(swfs[0]);
                    manifest = new CharacterPackageManifest
                    {
                        Id = id,
                        DisplayName = name,
                        DonorId = ""
                    };
                }
                if (string.IsNullOrWhiteSpace(manifest.Id)) continue;
                list.Add(new CharacterPackageInfo
                {
                    FolderPath = dir,
                    FolderName = name,
                    Manifest = manifest
                });
            }

            return list.OrderBy(p => p.Manifest.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// Convention filenames → wizard slot keys.
        /// </summary>
        public static Dictionary<string, string> LoadAssetsFromFolder(string folderPath)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!Directory.Exists(folderPath)) return result;

            void Try(string fileName, string slotKey)
            {
                var p = Path.Combine(folderPath, fileName);
                if (File.Exists(p)) result[slotKey] = p;
                var nested = Path.Combine(folderPath, "assets", fileName);
                if (File.Exists(nested)) result[slotKey] = nested;
            }

            Try("select.png", "select");
            Try("chip.png", "select");
            Try("pa.png", "pa");
            Try("cssp.png", "pa");
            Try("PA.png", "pa");
            Try("stock.png", "stock");
            Try("congrats.png", "congrats");
            Try("allstar_congrats.png", "allstar_congrats");
            Try("symbol.png", "symbol");
            return result;
        }

        public async Task ExportDonorAssetFolderAsync(
            string installRoot,
            string donorId,
            string outputFolder,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            string? seriesIcon = null)
        {
            Directory.CreateDirectory(outputFolder);
            var assetsDir = Path.Combine(outputFolder, "assets");
            Directory.CreateDirectory(assetsDir);
            var id = MenuSlotService.SanitizeId(donorId);
            progress?.Report($"Exporting donor assets for '{id}'…");

            // Export into temp so we never leave `{id}_*_placeholder.png` next to clean names.
            var temp = Path.Combine(Path.GetTempPath(), "ssf2mm-donor-assets-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(temp);
            try
            {
                var keys = new[] { "select", "charselect", "cssp", "stock", "congrats", "allstar_congrats" };
                var bulk = await _assets.ExportSlotsBulkAsync(
                    installRoot, id, keys, temp, jpexsPath, progress, cancellationToken);

                void CopySlot(string slotKey, string fileName)
                {
                    if (!bulk.TryGetValue(slotKey, out var info) || string.IsNullOrWhiteSpace(info.PngPath))
                        return;
                    File.Copy(info.PngPath!, Path.Combine(assetsDir, fileName), overwrite: true);
                }

                // CSS chip art = visible fill bitmap (not the framed sprite with name bar).
                var selectDest = Path.Combine(assetsDir, "select.png");
                var cropped = await _assets.ExportSelectChipFillArtAsync(
                    installRoot, id, selectDest, jpexsPath, progress, cancellationToken);
                if (cropped == null)
                    CopySlot("select", "select.png");

                // PA: cssp when present, else charselect (vanilla).
                if (bulk.TryGetValue("cssp", out var cssp)
                    && !string.IsNullOrWhiteSpace(cssp.PngPath)
                    && File.Exists(cssp.PngPath))
                    File.Copy(cssp.PngPath!, Path.Combine(assetsDir, "pa.png"), overwrite: true);
                else
                    CopySlot("charselect", "pa.png");

                CopySlot("stock", "stock.png");
                CopySlot("congrats", "congrats.png");
                CopySlot("allstar_congrats", "allstar_congrats.png");

                var symbolDest = Path.Combine(assetsDir, "symbol.png");
                if (!string.IsNullOrWhiteSpace(seriesIcon))
                {
                    await _assets.ExportSeriesSymbolPngAsync(
                        installRoot, seriesIcon.Trim(), symbolDest, jpexsPath, progress, cancellationToken);
                }
                if (!File.Exists(symbolDest))
                    await File.WriteAllBytesAsync(
                        symbolDest, CharacterCloneService.CreatePlaceholderPngBytes(), cancellationToken);

                CleanupPackageAssetDirt(outputFolder);
            }
            finally
            {
                try { Directory.Delete(temp, true); } catch { /* ignore */ }
            }

            progress?.Report($"Donor asset folder ready: {outputFolder}");
        }

        /// <summary>Remove placeholder dupes and legacy package-root symbol.png.</summary>
        public static void CleanupPackageAssetDirt(string packageFolder)
        {
            var assetsDir = Path.Combine(packageFolder, "assets");
            if (Directory.Exists(assetsDir))
            {
                foreach (var junk in Directory.GetFiles(assetsDir, "*_placeholder.png"))
                {
                    try { File.Delete(junk); } catch { /* ignore */ }
                }
            }
            var rootSym = Path.Combine(packageFolder, "symbol.png");
            if (File.Exists(rootSym))
            {
                try { File.Delete(rootSym); } catch { /* ignore */ }
            }
        }

        public async Task<CharacterPackageInfo> CreateOrUpdatePackageAsync(
            string installRoot,
            string id,
            string displayName,
            string donorId,
            string seriesIcon,
            string characterSwfPath,
            IReadOnlyDictionary<string, string>? slotPngs,
            string? seriesSymbolPng,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            bool exportFla = true)
        {
            var safeId = MenuSlotService.SanitizeId(id);
            var folderName = SanitizeFolderName(displayName);
            var folder = Path.Combine(CharacterDataDir(installRoot), folderName);
            Directory.CreateDirectory(folder);
            var assetsDir = Path.Combine(folder, "assets");
            Directory.CreateDirectory(assetsDir);
            var flaDir = Path.Combine(folder, "FLA");
            Directory.CreateDirectory(flaDir);

            progress?.Report($"Writing character package '{folderName}'…");
            var destSwf = Path.Combine(folder, safeId + ".swf");
            if (!File.Exists(characterSwfPath))
                throw new FileNotFoundException("Character SWF not found for package.", characterSwfPath);
            // Clone already wrote into the package path — never File.Copy onto itself (locks / sharing errors).
            if (!string.Equals(characterSwfPath, destSwf, StringComparison.OrdinalIgnoreCase))
                CopyFileWithRetry(characterSwfPath, destSwf);

            // Remove legacy flat SWF if present and different.
            var legacy = Path.Combine(CharacterDataDir(installRoot), safeId + ".swf");
            if (File.Exists(legacy)
                && !string.Equals(legacy, destSwf, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(legacy); } catch { /* ignore */ }
            }

            void SaveAsset(string slotKey, string fileName)
            {
                if (slotPngs == null) return;
                if (!slotPngs.TryGetValue(slotKey, out var src) || string.IsNullOrWhiteSpace(src) || !File.Exists(src))
                    return;
                File.Copy(src, Path.Combine(assetsDir, fileName), overwrite: true);
            }

            SaveAsset("select", "select.png");
            if (slotPngs != null)
            {
                var pa = slotPngs.GetValueOrDefault("pa")
                         ?? slotPngs.GetValueOrDefault("cssp")
                         ?? slotPngs.GetValueOrDefault("charselect");
                if (!string.IsNullOrWhiteSpace(pa) && File.Exists(pa))
                    File.Copy(pa, Path.Combine(assetsDir, "pa.png"), overwrite: true);
            }
            SaveAsset("stock", "stock.png");
            SaveAsset("congrats", "congrats.png");
            SaveAsset("allstar_congrats", "allstar_congrats.png");

            // Seed editable assets from the donor when the package is missing key files.
            if (!File.Exists(Path.Combine(assetsDir, "select.png"))
                || !File.Exists(Path.Combine(assetsDir, "pa.png"))
                || !File.Exists(Path.Combine(assetsDir, "stock.png"))
                || !File.Exists(Path.Combine(assetsDir, "symbol.png")))
            {
                try
                {
                    progress?.Report("Exporting donor asset placeholders into package…");
                    await ExportDonorAssetFolderAsync(
                        installRoot, donorId, folder, jpexsPath, progress, cancellationToken,
                        seriesIcon: seriesIcon);
                }
                catch (Exception ex)
                {
                    progress?.Report("Donor asset export warning: " + ex.Message);
                }
            }

            // Optional user-provided series PNG wins; symbol lives only under assets/.
            var symbolDest = Path.Combine(assetsDir, "symbol.png");
            if (!string.IsNullOrWhiteSpace(seriesSymbolPng) && File.Exists(seriesSymbolPng))
                File.Copy(seriesSymbolPng, symbolDest, overwrite: true);
            else if (!File.Exists(symbolDest) && !string.IsNullOrWhiteSpace(seriesIcon))
            {
                try
                {
                    await _assets.ExportSeriesSymbolPngAsync(
                        installRoot, seriesIcon.Trim(), symbolDest, jpexsPath, progress, cancellationToken);
                }
                catch (Exception ex)
                {
                    progress?.Report("Series symbol export warning: " + ex.Message);
                }
            }
            if (!File.Exists(symbolDest))
                await File.WriteAllBytesAsync(symbolDest, CharacterCloneService.CreatePlaceholderPngBytes(), cancellationToken);

            CleanupPackageAssetDirt(folder);

            await WriteCostumesJsonAsync(installRoot, donorId, safeId, folder, jpexsPath, progress, cancellationToken);

            var manifest = new CharacterPackageManifest
            {
                Id = safeId,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? safeId : displayName.Trim(),
                DonorId = MenuSlotService.SanitizeId(donorId),
                SeriesIcon = string.IsNullOrWhiteSpace(seriesIcon) ? "smash_symbol" : seriesIcon.Trim(),
                CreatedUtc = DateTime.UtcNow,
                UpdatedUtc = DateTime.UtcNow
            };
            var existing = TryReadManifest(folder);
            if (existing != null)
                manifest.CreatedUtc = existing.CreatedUtc;
            await File.WriteAllTextAsync(
                Path.Combine(folder, "character.json"),
                JsonConvert.SerializeObject(manifest, Formatting.Indented),
                cancellationToken);

            if (exportFla)
            {
                progress?.Report("Exporting FLA (JPEXS fla:cs6)…");
                // Purge prior temp-named FLA leftovers from older builds.
                try
                {
                    foreach (var stale in Directory.GetDirectories(flaDir, "ssf2mm-fla-*"))
                        Directory.Delete(stale, recursive: true);
                    foreach (var stale in Directory.GetFiles(flaDir, "ssf2mm-fla-*"))
                        File.Delete(stale);
                }
                catch { /* ignore */ }

                // Export from a temp folder copy named {id}.swf so FFDec's nested binary
                // inside the XFL package is asa.fla — not ssf2mm-fla-{guid}.fla.
                var flaWork = Path.Combine(Path.GetTempPath(), "ssf2mm-fla-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(flaWork);
                var flaSrc = Path.Combine(flaWork, safeId + ".swf");
                try
                {
                    CopyFileWithRetry(destSwf, flaSrc);
                    await _jpexs.ExportFlaAsync(
                        jpexsPath ?? "", flaSrc, flaDir, "cs6", cancellationToken,
                        outputBaseName: safeId, progress: progress, characterPretty: true);

                    // Remove any leftover temp-named binaries nested under the XFL folder.
                    var xflDir = Path.Combine(flaDir, safeId + ".fla");
                    if (Directory.Exists(xflDir))
                    {
                        foreach (var junk in Directory.GetFiles(xflDir, "ssf2mm-fla-*.fla"))
                            try { File.Delete(junk); } catch { /* ignore */ }
                    }
                }
                catch (Exception ex)
                {
                    progress?.Report("FLA export warning: " + ex.Message);
                }
                finally
                {
                    try { Directory.Delete(flaWork, recursive: true); } catch { /* ignore */ }
                }
            }

            return new CharacterPackageInfo
            {
                FolderPath = folder,
                FolderName = folderName,
                Manifest = manifest
            };
        }

        private async Task WriteCostumesJsonAsync(
            string installRoot,
            string donorId,
            string newId,
            string folder,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            var dest = Path.Combine(folder, "costumes.json");
            var misc = Path.Combine(installRoot, "data", "misc", "misc.swf");
            if (!File.Exists(misc))
                misc = Path.Combine(installRoot, "data", "misc", "misc.ssf");
            if (!File.Exists(misc))
            {
                await File.WriteAllTextAsync(dest, CharacterCloneService.ToCostumesJson(null, newId), cancellationToken);
                return;
            }

            try
            {
                var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pkg-costumes-" + Guid.NewGuid().ToString("N"));
                Directory.CreateDirectory(work);
                try
                {
                    await _jpexs.ExportAsync(jpexsPath ?? "", misc, work, "script", cancellationToken);
                    var miscAs = Directory.GetFiles(work, "Misc.as", SearchOption.AllDirectories).FirstOrDefault();
                    if (miscAs != null)
                    {
                        var text = await File.ReadAllTextAsync(miscAs, cancellationToken);
                        var block = CharacterCloneService.ExtractCostumeBlock(text, donorId, newId)
                                    ?? CharacterCloneService.ExtractCostumeBlock(text, newId, newId);
                        if (!string.IsNullOrEmpty(block))
                        {
                            await File.WriteAllTextAsync(dest, CharacterCloneService.ToCostumesJson(block, newId), cancellationToken);
                            return;
                        }
                    }
                }
                finally
                {
                    try { Directory.Delete(work, true); } catch { /* ignore */ }
                }
            }
            catch (Exception ex)
            {
                progress?.Report("costumes.json warning: " + ex.Message);
            }

            await File.WriteAllTextAsync(dest, CharacterCloneService.ToCostumesJson(null, newId), cancellationToken);
        }

        public static void CopyFileWithRetry(string source, string dest, int attempts = 10)
        {
            if (string.Equals(source, dest, StringComparison.OrdinalIgnoreCase))
                return;
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            Exception? last = null;
            for (var i = 0; i < attempts; i++)
            {
                try
                {
                    File.Copy(source, dest, overwrite: true);
                    return;
                }
                catch (IOException ex)
                {
                    last = ex;
                    Thread.Sleep(80 * (i + 1));
                }
                catch (UnauthorizedAccessException ex)
                {
                    last = ex;
                    Thread.Sleep(80 * (i + 1));
                }
            }
            throw last ?? new IOException("Failed to copy " + source);
        }
    }

    /// <summary>Weighted multi-step progress for the character wizard overlay.</summary>
    public sealed class WizardStepProgress
    {
        private readonly IProgress<(string Message, double Percent, string? Step)>? _sink;
        private readonly (string Title, double Weight)[] _steps;
        private readonly double _totalWeight;
        private int _index;
        private double _completedWeight;

        public WizardStepProgress(
            IProgress<(string Message, double Percent, string? Step)>? sink,
            params (string Title, double Weight)[] steps)
        {
            _sink = sink;
            _steps = steps ?? Array.Empty<(string, double)>();
            _totalWeight = Math.Max(0.0001, _steps.Sum(s => s.Weight));
            _index = -1;
        }

        public int StepCount => _steps.Length;

        public IProgress<string> AsStringProgress() =>
            new Progress<string>(msg => ReportCurrent(msg));

        public void BeginStep(int index, string? detail = null)
        {
            if (index < 0 || index >= _steps.Length) return;
            // Credit prior step fully when advancing.
            if (_index >= 0 && index > _index)
            {
                _completedWeight = _steps.Take(index).Sum(s => s.Weight);
            }
            _index = index;
            ReportCurrent(detail ?? _steps[index].Title);
        }

        public void ReportCurrent(string message)
        {
            if (_index < 0 || _index >= _steps.Length)
            {
                _sink?.Report((message, 0, null));
                return;
            }
            var within = _steps[_index].Weight * 0.35; // mid-step nudge
            var pct = 100.0 * (_completedWeight + within) / _totalWeight;
            var label = $"Step {_index + 1}/{_steps.Length} — {_steps[_index].Title}";
            _sink?.Report((message, Math.Clamp(pct, 0, 99), label));
        }

        public void CompleteStep()
        {
            if (_index < 0 || _index >= _steps.Length) return;
            _completedWeight = _steps.Take(_index + 1).Sum(s => s.Weight);
            var pct = 100.0 * _completedWeight / _totalWeight;
            var label = $"Step {_index + 1}/{_steps.Length} — {_steps[_index].Title}";
            _sink?.Report((_steps[_index].Title + " done", Math.Min(100, pct), label));
        }

        public void Finish(string message = "Done")
        {
            _sink?.Report((message, 100, $"Step {_steps.Length}/{_steps.Length} — Done"));
        }

        /// <summary>Matches real clone phases in <see cref="RosterWizardService"/>.</summary>
        public static (string Title, double Weight)[] CloneCharacterSteps { get; } =
        {
            ("Clone character SWF", 20),
            ("Install UI art + scripts", 50),
            ("Package folder + FLA", 20),
            ("Save mappings", 10)
        };

        public static (string Title, double Weight)[] UpdateCharacterSteps { get; } =
        {
            ("Prepare", 10),
            ("Update assets", 55),
            ("Update character SWF", 25),
            ("Mappings commit", 10)
        };
    }
}
