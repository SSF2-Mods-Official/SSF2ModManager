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
    public class MappingsValidationIssue
    {
        public string Severity { get; set; } = "warn"; // warn | error | info
        public string Category { get; set; } = "";
        public string Id { get; set; } = "";
        public string Message { get; set; } = "";
    }

    public class MappingsRosterItem
    {
        public string Id { get; set; } = "";
        public string Category { get; set; } = ""; // character | stage | music | extra
        public string Name { get; set; } = "";
        public string File { get; set; } = "";
        public string FilePub { get; set; } = "";
        public string Guid { get; set; } = "";
        public string SeriesIcon { get; set; } = "";
        public bool? TrainingOnly { get; set; }
        public bool InSelectRows { get; set; }
        public bool HasSelectSymbol { get; set; }
        public bool HasPortraitSymbol { get; set; }
        public bool FileExistsOnDisk { get; set; }
        public string StatusSummary
        {
            get
            {
                if (Category is "music" or "extra")
                    return FileExistsOnDisk ? "OK" : "Missing file";
                var parts = new List<string>();
                if (!FileExistsOnDisk) parts.Add("no file");
                if (!InSelectRows) parts.Add("not in rows");
                if (!HasSelectSymbol) parts.Add("no menu chip");
                if (Category == "character" && !HasPortraitSymbol) parts.Add("no portrait");
                return parts.Count == 0 ? "OK" : string.Join(", ", parts);
            }
        }
    }

    /// <summary>
    /// Plaintext mappings.json sidecar + apply back into data/misc/mappings.swf.
    /// </summary>
    public class MappingsEditService
    {
        public const string MappingsMetadataKey = "b7d60849-fa99-4514-b41e-16693ce7acc0";
        public const string PlaintextRelativePath = @"data\misc\mappings.json";

        private readonly JpexsCliService _jpexs;
        private readonly MenuSlotService _menuSlots;

        public MappingsEditService(JpexsCliService? jpexs = null, MenuSlotService? menuSlots = null)
        {
            _jpexs = jpexs ?? new JpexsCliService();
            _menuSlots = menuSlots ?? new MenuSlotService(_jpexs);
        }

        public static string GetPlaintextPath(string installRoot) =>
            Path.Combine(installRoot, PlaintextRelativePath);

        public static string? FindMappingsSwf(string installRoot)
        {
            var preferred = Path.Combine(installRoot, "data", "misc", "mappings.swf");
            if (File.Exists(preferred)) return preferred;
            var data = Path.Combine(installRoot, "data");
            if (!Directory.Exists(data)) return null;
            return Directory.GetFiles(data, "mappings.swf", SearchOption.AllDirectories).FirstOrDefault();
        }

        /// <summary>
        /// Decrypts mappingsJSON from mappings.swf and writes data/misc/mappings.json.
        /// </summary>
        public async Task<string> ExportPlaintextAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var mappingsSwf = FindMappingsSwf(installRoot)
                ?? throw new FileNotFoundException("mappings.swf not found under data/.", installRoot);

            progress?.Report($"Exporting mappings from {Path.GetFileName(mappingsSwf)}…");
            var json = await ReadMappingsJsonFromSwfAsync(mappingsSwf, jpexsPath, progress, cancellationToken);
            // Pretty-print for human editing.
            var pretty = JToken.Parse(json).ToString(Formatting.Indented);
            var outPath = GetPlaintextPath(installRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
            var bak = outPath + ".bak";
            if (File.Exists(outPath))
                File.Copy(outPath, bak, overwrite: true);
            await File.WriteAllTextAsync(outPath, pretty, cancellationToken);
            progress?.Report($"Wrote {outPath}");
            return outPath;
        }

        public async Task<JObject> LoadDocumentAsync(
            string installRoot,
            bool exportIfMissing = true,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var path = GetPlaintextPath(installRoot);
            if (!File.Exists(path))
            {
                if (!exportIfMissing)
                    throw new FileNotFoundException("mappings.json not found. Export it first.", path);
                await ExportPlaintextAsync(installRoot, jpexsPath, progress, cancellationToken);
            }

            var text = await File.ReadAllTextAsync(path, cancellationToken);
            return JObject.Parse(text);
        }

        public async Task SaveDocumentAsync(
            string installRoot,
            JObject document,
            CancellationToken cancellationToken = default)
        {
            var path = GetPlaintextPath(installRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bak = path + ".bak";
            if (File.Exists(path))
                File.Copy(path, bak, overwrite: true);
            await File.WriteAllTextAsync(path, document.ToString(Formatting.Indented), cancellationToken);
        }

        /// <summary>
        /// Save plaintext mappings.json and immediately apply into mappings.swf (guided UX).
        /// </summary>
        public async Task CommitAsync(
            string installRoot,
            JObject document,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            progress?.Report("Saving mappings.json…");
            await SaveDocumentAsync(installRoot, document, cancellationToken);
            await ApplyToMappingsSwfAsync(installRoot, jpexsPath, progress, cancellationToken);
        }

        public static void SetLayoutNumber(JObject doc, string path, double value)
        {
            // path like "character_select_screen.position.x"
            var parts = path.Split('.');
            JToken cur = doc;
            for (var i = 0; i < parts.Length - 1; i++)
            {
                if (cur[parts[i]] is not JObject obj)
                {
                    obj = new JObject();
                    if (cur is JObject parent) parent[parts[i]] = obj;
                    else return;
                }
                cur = obj;
            }
            if (cur is JObject leaf)
                leaf[parts[^1]] = value;
        }

        public static double? GetLayoutNumber(JObject doc, string path)
        {
            var parts = path.Split('.');
            JToken? cur = doc;
            foreach (var p in parts)
            {
                cur = cur?[p];
                if (cur == null) return null;
            }
            return cur.Type is JTokenType.Float or JTokenType.Integer ? cur.Value<double>() : null;
        }

        /// <summary>
        /// Maximize CSS/SSS icon_size for the current grid; CSS also recenters position.x in chip_bounds.
        /// Row membership is left unchanged — only layout numbers are adjusted.
        /// </summary>
        public static string? MaybeWarnAndNudgeLayout(JObject doc, bool isCss, List<List<string>> rows) =>
            FitSelectLayout(doc, isCss, rows);

        // Width from chip_bounds; height fills the starfield down to just above P1–P4 (not full chip_bounds 310).
        private const double CssAvailW = 610;
        private const double CssAvailH = 200; // fill more of the starfield; 11 rows → ~18px cells
        private const double CssChipLeft = -105;
        private const double CssDefaultY = 23;
        private const double CssMinIcon = 8;
        private const double CssMaxIcon = 42; // grow back toward vanilla when the widest row is short
        /// <summary>Practical max columns that still fit at min readable pitch.</summary>
        public const int CssMaxColsPerRow = 50;
        /// <summary>Max CSS rows that still fit above the player HUD at min cell size.</summary>
        public const int CssMaxRows = 15;

        private const double SssAvailW = 640;
        private const double SssAvailH = 200;
        private const double SssDefaultLeft = -288;
        private const double SssDefaultY = 1;
        private const double SssMaxW = 48;
        private const double SssMaxH = 26;

        /// <summary>
        /// Maximize select-screen icon pitch for (maxCols × rowCount) inside the chip area.
        /// CSS position.x is centered in chip_bounds so wide rows stay on-screen.
        /// With the engine scaleAll patch, icon_size also drives visual scale (vanilla CSS 42 / SSS 48×26).
        /// </summary>
        public static string? FitSelectLayout(JObject doc, bool isCss, List<List<string>> rows)
        {
            var nonEmpty = rows.Where(r => r.Count > 0).ToList();
            var maxCols = nonEmpty.Count == 0 ? 0 : nonEmpty.Max(r => r.Count);
            var rowCount = nonEmpty.Count;
            if (maxCols <= 0 || rowCount <= 0) return null;

            var widthPath = isCss
                ? "character_select_screen.icon_size.width"
                : "stage_select_screen.normal.icon_size.width";
            var heightPath = isCss
                ? "character_select_screen.icon_size.height"
                : "stage_select_screen.normal.icon_size.height";
            var posXPath = isCss
                ? "character_select_screen.position.x"
                : "stage_select_screen.normal.position.x";
            var posYPath = isCss
                ? "character_select_screen.position.y"
                : "stage_select_screen.normal.position.y";

            var defaultY = isCss ? CssDefaultY : SssDefaultY;
            var curW = GetLayoutNumber(doc, widthPath)
                       ?? GetLayoutNumber(doc, isCss ? "character_select_screen.icon_size" : "stage_select_screen.normal.icon_size")
                       ?? (isCss ? CssMaxIcon : SssMaxW);
            var curH = GetLayoutNumber(doc, heightPath) ?? (isCss ? curW : SssMaxH);
            var curX = GetLayoutNumber(doc, posXPath) ?? (isCss ? CssChipLeft : SssDefaultLeft);
            var curY = GetLayoutNumber(doc, posYPath) ?? defaultY;

            // Prefer chip_bounds from mappings when present (CSS).
            var chipLeft = CssChipLeft;
            var chipW = CssAvailW;
            if (isCss)
            {
                var bx = GetLayoutNumber(doc, "character_select_screen.chip_bounds.x");
                var bw = GetLayoutNumber(doc, "character_select_screen.chip_bounds.width");
                if (bx is not null) chipLeft = bx.Value;
                if (bw is not null && bw.Value > 100) chipW = bw.Value;
            }

            double width;
            double height;
            if (isCss)
            {
                var cellW = Math.Floor(chipW / maxCols);
                var cellH = Math.Floor(CssAvailH / rowCount);
                var cell = Math.Clamp(Math.Min(cellW, cellH), CssMinIcon, CssMaxIcon);
                width = cell;
                height = cell;
            }
            else
            {
                width = Math.Clamp(Math.Floor(SssAvailW / maxCols), 20, SssMaxW);
                height = Math.Clamp(Math.Floor(SssAvailH / rowCount), 12, SssMaxH);
            }

            double x;
            if (isCss)
            {
                x = SuggestCssPositionX(maxCols, width, chipLeft, chipW);
            }
            else
            {
                x = curX;
                if (x < -400 || x > -100)
                    x = SssDefaultLeft;
            }

            var y = curY;
            if (double.IsNaN(y) || double.IsInfinity(y) || y < -100 || y > 500)
                y = defaultY;
            var availH = isCss ? CssAvailH : SssAvailH;
            var gridH = rowCount * height;
            if (y + gridH > defaultY + availH)
                y = defaultY;

            SetLayoutNumber(doc, widthPath, width);
            SetLayoutNumber(doc, heightPath, height);
            SetLayoutNumber(doc, posXPath, x);
            SetLayoutNumber(doc, posYPath, y);

            if (Math.Abs(width - curW) < 0.5 && Math.Abs(height - curH) < 0.5 && Math.Abs(x - curX) < 0.5 && Math.Abs(y - curY) < 0.5)
                return null;

            return $"Fitted grid {maxCols}×{rowCount} → cell {width:0}×{height:0}, x {x:0} (rows unchanged).";
        }

        /// <summary>Center a CSS row of <paramref name="maxCols"/> cells inside chip_bounds.</summary>
        public static double SuggestCssPositionX(int maxCols, double cell, double chipLeft = CssChipLeft, double chipW = CssAvailW)
        {
            if (maxCols <= 0 || cell <= 0) return chipLeft;
            var rowWidth = maxCols * cell;
            return Math.Round(chipLeft + (chipW - rowWidth) / 2.0);
        }

        /// <summary>Suggested icon size for a grid shape (preview only). CSS returns square cell; SSS returns width.</summary>
        public static double SuggestIconSize(int maxCols, bool isCss = true) =>
            SuggestIconSize(maxCols, rowCount: 1, isCss);

        /// <summary>Suggested icon size for (maxCols × rowCount). CSS returns square cell; SSS returns width.</summary>
        public static double SuggestIconSize(int maxCols, int rowCount, bool isCss = true)
        {
            if (maxCols <= 0) return isCss ? CssMaxIcon : SssMaxW;
            var rows = Math.Max(1, rowCount);
            if (isCss)
            {
                var cellW = Math.Floor(CssAvailW / maxCols);
                var cellH = Math.Floor(CssAvailH / rows);
                return Math.Clamp(Math.Min(cellW, cellH), CssMinIcon, CssMaxIcon);
            }

            return Math.Clamp(Math.Floor(SssAvailW / maxCols), 20, SssMaxW);
        }

        /// <summary>
        /// Encrypts plaintext mappings.json and FFDec-replaces mappingsJSON inside mappings.swf.
        /// </summary>
        public async Task ApplyToMappingsSwfAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var jsonPath = GetPlaintextPath(installRoot);
            if (!File.Exists(jsonPath))
                throw new FileNotFoundException("mappings.json not found. Export or save it first.", jsonPath);

            var mappingsSwf = FindMappingsSwf(installRoot)
                ?? throw new FileNotFoundException("mappings.swf not found under data/.", installRoot);

            var json = await File.ReadAllTextAsync(jsonPath, cancellationToken);
            // Normalize file extensions for prepared installs.
            json = DatMappingService.RewriteFileExtensionsToSwf(json);
            // Re-parse for compact encode (game accepts either; compact is fine).
            var compact = JToken.Parse(json).ToString(Formatting.None);

            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-mapapply-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                progress?.Report("Exporting mappingsJSON binary id…");
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", mappingsSwf, exportDir, "binaryData", cancellationToken);

                var metadataBinary = Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(
                        "mappingsJSON", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidOperationException("No mappingsJSON binary found in mappings.swf.");

                var idMatch = Regex.Match(Path.GetFileName(metadataBinary), @"^(\d+)_");
                if (!idMatch.Success)
                    throw new InvalidOperationException("Could not parse mappingsJSON character id.");

                var encoded = DatMappingService.EncodeJsonBlob(compact, MappingsMetadataKey);
                var newBin = Path.Combine(work, "mappings-new.bin");
                await File.WriteAllBytesAsync(newBin, encoded, cancellationToken);

                var swfBak = mappingsSwf + ".ssf2mm.bak";
                if (!File.Exists(swfBak))
                    File.Copy(mappingsSwf, swfBak, overwrite: false);

                var outSwf = Path.Combine(work, "mappings-out.swf");
                progress?.Report("Writing mappings.swf…");
                await _jpexs.ReplaceAsync(
                    jpexsPath ?? "", mappingsSwf, outSwf, idMatch.Groups[1].Value, newBin, cancellationToken);
                File.Copy(outSwf, mappingsSwf, overwrite: true);

                // Keep plaintext in sync with normalized .swf file fields.
                await File.WriteAllTextAsync(
                    jsonPath, JToken.Parse(compact).ToString(Formatting.Indented), cancellationToken);
                progress?.Report("Applied mappings.json → mappings.swf.");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public async Task<string> ReadMappingsJsonFromSwfAsync(
            string mappingsSwfPath,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-mapread-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var exportDir = Path.Combine(work, "export");
                Directory.CreateDirectory(exportDir);
                await _jpexs.ExportAsync(jpexsPath ?? "", mappingsSwfPath, exportDir, "binaryData", cancellationToken);
                var metadataBinary = Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                    .OrderByDescending(f => new FileInfo(f).Length)
                    .FirstOrDefault(f => Path.GetFileName(f).Contains(
                        "mappingsJSON", StringComparison.OrdinalIgnoreCase))
                    ?? Directory.GetFiles(exportDir, "*.bin", SearchOption.AllDirectories)
                        .OrderByDescending(f => new FileInfo(f).Length)
                        .FirstOrDefault()
                    ?? throw new InvalidOperationException("No mappingsJSON binary found.");

                var blob = System.Text.Encoding.UTF8.GetString(await File.ReadAllBytesAsync(metadataBinary, cancellationToken))
                    .Trim('\0', '\uFEFF', ' ', '\r', '\n', '\t');
                return DatMappingService.DecodeJsonBlob(blob, MappingsMetadataKey);
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        public List<MappingsRosterItem> BuildRosterItems(
            JObject doc,
            string installRoot,
            MenuSymbolIndex? symbols = null)
        {
            var cssIds = FlattenSelectRowIds(doc["character_select_screen"]?["rows"]);
            var sssIds = FlattenSelectRowIds(doc["stage_select_screen"]?["normal"]?["rows"])
                .Concat(FlattenSelectRowIds(doc["stage_select_screen"]?["past"]?["rows"]))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var items = new List<MappingsRosterItem>();
            AddCategory(items, doc, "character", cssIds, installRoot, symbols, isStage: false);
            AddCategory(items, doc, "stage", sssIds, installRoot, symbols, isStage: true);
            AddCategory(items, doc, "music", null, installRoot, symbols, isStage: false);
            AddCategory(items, doc, "extra", null, installRoot, symbols, isStage: false);
            return items
                .OrderBy(i => i.Category)
                .ThenBy(i => i.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public List<MappingsValidationIssue> Validate(
            JObject doc,
            string installRoot,
            MenuSymbolIndex? symbols = null)
        {
            var issues = new List<MappingsValidationIssue>();
            var items = BuildRosterItems(doc, installRoot, symbols);
            foreach (var item in items)
            {
                if (IsPlaceholderId(item.Id)) continue;
                if (item.Category is "character" or "stage")
                {
                    if (string.IsNullOrWhiteSpace(item.File) && item.Category == "character")
                    {
                        // empty stubs
                        if (!item.FileExistsOnDisk && string.IsNullOrWhiteSpace(item.Guid))
                            continue;
                    }
                    if (!string.IsNullOrWhiteSpace(item.File) && !item.FileExistsOnDisk)
                        issues.Add(new MappingsValidationIssue
                        {
                            Severity = "error",
                            Category = item.Category,
                            Id = item.Id,
                            Message = $"File missing on disk: {item.File}"
                        });
                    if (!string.IsNullOrWhiteSpace(item.File) && !item.InSelectRows)
                        issues.Add(new MappingsValidationIssue
                        {
                            Severity = "warn",
                            Category = item.Category,
                            Id = item.Id,
                            Message = "Mapped but not placed in select-screen rows (will not appear)."
                        });
                    if (!string.IsNullOrWhiteSpace(item.File) && item.InSelectRows && !item.HasSelectSymbol)
                        issues.Add(new MappingsValidationIssue
                        {
                            Severity = "error",
                            Category = item.Category,
                            Id = item.Id,
                            Message = item.Category == "stage"
                                ? $"In SSS rows but menu_stages lacks {item.Id}_stage_select."
                                : $"In CSS rows but menu_characters lacks {item.Id}_select."
                        });
                    if (item.Category == "character" && !string.IsNullOrWhiteSpace(item.File) &&
                        item.InSelectRows && !item.HasPortraitSymbol)
                        issues.Add(new MappingsValidationIssue
                        {
                            Severity = "warn",
                            Category = item.Category,
                            Id = item.Id,
                            Message = $"Missing {item.Id}_charselect portrait in misc2 (player box may fall back)."
                        });
                }
            }

            // Rows referencing unknown ids
            foreach (var id in FlattenSelectRowIds(doc["character_select_screen"]?["rows"]))
            {
                if (id is "random" or "cxp") continue;
                if (doc["character"]?[id] == null)
                    issues.Add(new MappingsValidationIssue
                    {
                        Severity = "error",
                        Category = "character_select_screen",
                        Id = id,
                        Message = "CSS row references id not present under character."
                    });
            }

            return issues;
        }

        public static List<List<string>> GetCssRows(JObject doc) =>
            ParseRows(doc["character_select_screen"]?["rows"]);

        public static List<List<string>> GetSssNormalRows(JObject doc) =>
            ParseRows(doc["stage_select_screen"]?["normal"]?["rows"]);

        public static List<List<string>> GetSssPastRows(JObject doc) =>
            ParseRows(doc["stage_select_screen"]?["past"]?["rows"]);

        public static void SetCssRows(JObject doc, IEnumerable<IEnumerable<string>> rows)
        {
            EnsureObject(doc, "character_select_screen")["rows"] = ToRowsToken(rows);
        }

        public static void SetSssNormalRows(JObject doc, IEnumerable<IEnumerable<string>> rows)
        {
            EnsureObject(EnsureObject(doc, "stage_select_screen"), "normal")["rows"] = ToRowsToken(rows);
        }

        public static void SetSssPastRows(JObject doc, IEnumerable<IEnumerable<string>> rows)
        {
            EnsureObject(EnsureObject(doc, "stage_select_screen"), "past")["rows"] = ToRowsToken(rows);
        }

        public static bool ReplaceInRows(List<List<string>> rows, string oldId, string newId)
        {
            var changed = false;
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < rows[r].Count; c++)
                {
                    if (!rows[r][c].Equals(oldId, StringComparison.OrdinalIgnoreCase)) continue;
                    rows[r][c] = newId;
                    changed = true;
                }
            }
            return changed;
        }

        /// <summary>
        /// Drops character and stage entries whose file is not on disk, and takes those ids off the select screens.
        /// The game reads mappings.json, so a deleted SWF left in this file is still loaded and then crashes stats import.
        /// </summary>
        public static List<string> DropMissingFiles(JObject doc, string installRoot)
        {
            var dropped = new List<string>();
            DropMissingSection(doc, installRoot, "character", dropped);
            DropMissingSection(doc, installRoot, "stage", dropped);
            if (dropped.Count == 0)
                return dropped;
            RemoveIdsFromSelect(doc, dropped);
            return dropped;
        }

        public static List<string> DropMissingFiles(string installRoot)
        {
            var path = GetPlaintextPath(installRoot);
            if (!File.Exists(path))
                return new List<string>();
            var doc = JObject.Parse(File.ReadAllText(path));
            var dropped = DropMissingFiles(doc, installRoot);
            if (dropped.Count == 0)
                return dropped;
            File.Copy(path, path + ".bak", overwrite: true);
            File.WriteAllText(path, doc.ToString(Formatting.Indented));
            return dropped;
        }

        /// <summary>Removes ids from the character and stage lists and from the select screens. Does not delete folders.</summary>
        public static void RemoveMappedIds(JObject doc, IEnumerable<string> ids)
        {
            var list = ids.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (list.Count == 0)
                return;
            foreach (var sectionName in new[] { "character", "stage" })
            {
                if (doc[sectionName] is not JObject section)
                    continue;
                foreach (var prop in section.Properties().ToList())
                {
                    if (list.Any(id => id.Equals(prop.Name, StringComparison.OrdinalIgnoreCase)))
                        prop.Remove();
                }
            }
            RemoveIdsFromSelect(doc, list);
        }

        public static bool MappedFileExists(string installRoot, string section, string? file, string? filePub = null)
        {
            if (string.IsNullOrWhiteSpace(file) && string.IsNullOrWhiteSpace(filePub))
                return true;
            if (!string.IsNullOrWhiteSpace(file) && OneMappedFileExists(installRoot, section, file))
                return true;
            return !string.IsNullOrWhiteSpace(filePub) && OneMappedFileExists(installRoot, section, filePub);
        }

        private static bool OneMappedFileExists(string installRoot, string section, string file)
        {
            var rel = file.Replace('\\', '/');
            if (section.Equals("character", StringComparison.OrdinalIgnoreCase))
            {
                if (rel.StartsWith("expansions/", StringComparison.OrdinalIgnoreCase))
                    rel = "expansions/character/" + rel["expansions/".Length..];
                else if (!rel.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                    rel = "data/character/" + rel;
            }
            else if (section.Equals("stage", StringComparison.OrdinalIgnoreCase))
            {
                if (rel.StartsWith("expansions/stages/", StringComparison.OrdinalIgnoreCase))
                { }
                else if (!rel.StartsWith("data/", StringComparison.OrdinalIgnoreCase))
                    rel = "data/stage/" + rel;
            }
            return File.Exists(Path.Combine(installRoot, rel.Replace('/', Path.DirectorySeparatorChar)))
                || File.Exists(Path.Combine(installRoot, SwapSwfExtension(rel).Replace('/', Path.DirectorySeparatorChar)));
        }

        private static void DropMissingSection(JObject doc, string installRoot, string sectionName, List<string> dropped)
        {
            if (doc[sectionName] is not JObject section)
                return;
            foreach (var prop in section.Properties().ToList())
            {
                var file = prop.Value?["file"]?.ToString();
                var filePub = prop.Value?["file_pub"]?.ToString();
                if (MappedFileExists(installRoot, sectionName, file, filePub))
                    continue;
                dropped.Add(prop.Name);
                prop.Remove();
            }
        }

        private static void RemoveIdsFromSelect(JObject doc, IReadOnlyList<string> ids)
        {
            var css = GetCssRows(doc);
            var normal = GetSssNormalRows(doc);
            var past = GetSssPastRows(doc);
            var changedCss = false;
            var changedNormal = false;
            var changedPast = false;
            foreach (var id in ids)
            {
                if (RemoveIdFromRows(css, id) > 0) changedCss = true;
                if (RemoveIdFromRows(normal, id) > 0) changedNormal = true;
                if (RemoveIdFromRows(past, id) > 0) changedPast = true;
            }
            if (changedCss) SetCssRows(doc, css);
            if (changedNormal) SetSssNormalRows(doc, normal);
            if (changedPast) SetSssPastRows(doc, past);
        }

        private static string SwapSwfExtension(string rel)
        {
            if (rel.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                return rel[..^4] + ".ssf";
            if (rel.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                return rel[..^4] + ".swf";
            return rel;
        }

        /// <summary>
        /// Removes every occurrence of <paramref name="id"/> (case-insensitive).
        /// Drops empty rows afterward (keeps a single empty row if the grid would otherwise be empty).
        /// </summary>
        public static int RemoveIdFromRows(List<List<string>> rows, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || rows.Count == 0) return 0;
            var removed = 0;
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = rows[r].Count - 1; c >= 0; c--)
                {
                    if (!rows[r][c].Equals(id, StringComparison.OrdinalIgnoreCase)) continue;
                    rows[r].RemoveAt(c);
                    removed++;
                }
            }

            for (var r = rows.Count - 1; r >= 0; r--)
            {
                if (rows[r].Count == 0 && rows.Count > 1)
                    rows.RemoveAt(r);
            }

            return removed;
        }

        /// <summary>True if <paramref name="id"/> appears anywhere in the grid.</summary>
        public static bool RowsContainId(IEnumerable<IEnumerable<string>> rows, string id) =>
            !string.IsNullOrWhiteSpace(id) &&
            rows.Any(r => r.Any(x => x.Equals(id, StringComparison.OrdinalIgnoreCase)));

        /// <summary>
        /// Inserts <paramref name="id"/> at the given slot after removing any prior occurrences (unique roster).
        /// </summary>
        public static void InsertInRow(List<List<string>> rows, int rowIndex, int colIndex, string id)
        {
            RemoveIdFromRows(rows, id);
            if (rows.Count == 0) rows.Add(new List<string>());
            if (rowIndex < 0) rowIndex = 0;
            if (rowIndex >= rows.Count) rows.Add(new List<string>());
            rowIndex = Math.Min(rowIndex, rows.Count - 1);
            colIndex = Math.Clamp(colIndex, 0, rows[rowIndex].Count);
            rows[rowIndex].Insert(colIndex, id);
        }

        public static void AppendToRow(List<List<string>> rows, int rowIndex, string id)
        {
            RemoveIdFromRows(rows, id);
            if (rows.Count == 0) rows.Add(new List<string>());
            if (rowIndex < 0 || rowIndex >= rows.Count) rowIndex = rows.Count - 1;
            rows[rowIndex].Add(id);
        }

        /// <summary>
        /// Adds the id under character or stage, then puts it on that select screen if it is not already there.
        /// </summary>
        public static void PlaceOnSelect(JObject doc, bool character, string id, string file, string displayName)
        {
            UpsertEntry(
                doc,
                character ? "character" : "stage",
                id,
                file,
                displayName,
                filePub: file);
            var rows = character ? GetCssRows(doc) : GetSssNormalRows(doc);
            if (!RowsContainId(rows, id))
                AppendToRow(rows, rows.Count - 1, id);
            if (character)
                SetCssRows(doc, rows);
            else
                SetSssNormalRows(doc, rows);
            FitSelectLayout(doc, character, rows);
        }

        /// <summary>
        /// Inserts after the first occurrence of <paramref name="afterId"/>, or appends to the last row if not found.
        /// Ensures uniqueness by removing <paramref name="id"/> first.
        /// </summary>
        public static void InsertAfterId(List<List<string>> rows, string? afterId, string id)
        {
            RemoveIdFromRows(rows, id);
            if (!string.IsNullOrWhiteSpace(afterId) &&
                !afterId.Equals(id, StringComparison.OrdinalIgnoreCase))
            {
                for (var r = 0; r < rows.Count; r++)
                {
                    var c = rows[r].FindIndex(x => x.Equals(afterId, StringComparison.OrdinalIgnoreCase));
                    if (c < 0) continue;
                    rows[r].Insert(c + 1, id);
                    return;
                }
            }

            if (rows.Count == 0) rows.Add(new List<string>());
            rows[^1].Add(id);
        }

        /// <summary>Keeps the first occurrence of each id; removes later duplicates.</summary>
        public static int EnsureUniqueRowIds(List<List<string>> rows)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var removed = 0;
            for (var r = 0; r < rows.Count; r++)
            {
                for (var c = 0; c < rows[r].Count;)
                {
                    var id = rows[r][c];
                    if (string.IsNullOrWhiteSpace(id) || seen.Add(id))
                    {
                        c++;
                        continue;
                    }
                    rows[r].RemoveAt(c);
                    removed++;
                }
            }

            for (var r = rows.Count - 1; r >= 0; r--)
            {
                if (rows[r].Count == 0 && rows.Count > 1)
                    rows.RemoveAt(r);
            }

            return removed;
        }

        /// <summary>
        /// Collects every resource GUID under character / stage / music (case-insensitive).
        /// </summary>
        public static HashSet<string> CollectExistingGuids(JObject doc)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (doc == null) return set;
            foreach (var category in new[] { "character", "stage", "music" })
            {
                if (doc[category] is not JObject cat) continue;
                foreach (var prop in cat.Properties())
                {
                    if (prop.Value is not JObject entry) continue;
                    var g = entry["guid"]?.ToString()?.Trim();
                    if (!string.IsNullOrEmpty(g))
                        set.Add(g);
                }
            }
            return set;
        }

        /// <summary>
        /// Allocates a new GUID that does not collide with any character/stage/music GUID in mappings.
        /// Manager-only — never accept a user-supplied value for new content.
        /// </summary>
        public static string AllocateUniqueGuid(JObject doc, int maxAttempts = 64)
        {
            var existing = CollectExistingGuids(doc);
            for (var i = 0; i < maxAttempts; i++)
            {
                var candidate = Guid.NewGuid().ToString();
                if (existing.Add(candidate))
                    return candidate;
            }
            throw new InvalidOperationException(
                $"Could not allocate a unique resource GUID after {maxAttempts} attempts.");
        }

        public static JObject UpsertEntry(
            JObject doc,
            string category,
            string id,
            string file,
            string? name = null,
            string? seriesIcon = null,
            string? filePub = null,
            string? guid = null,
            bool? trainingOnly = null)
        {
            var cat = EnsureObject(doc, category);
            var entry = cat[id] as JObject ?? new JObject();
            entry["file"] = file.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase)
                ? file[..^4] + ".swf"
                : file.EndsWith(".swf", StringComparison.OrdinalIgnoreCase) ? file : file + ".swf";
            if (!string.IsNullOrWhiteSpace(name)) entry["name"] = name;
            if (!string.IsNullOrWhiteSpace(seriesIcon)) entry["seriesIcon"] = seriesIcon;
            if (!string.IsNullOrWhiteSpace(filePub)) entry["file_pub"] = filePub;
            else if (entry["file_pub"] == null) entry["file_pub"] = $"DAT_CUSTOM_{id}.ssf";
            if (!string.IsNullOrWhiteSpace(guid)) entry["guid"] = guid;
            else if (entry["guid"] == null) entry["guid"] = AllocateUniqueGuid(doc);
            if (trainingOnly.HasValue) entry["training_only"] = trainingOnly.Value;
            cat[id] = entry;
            return entry;
        }

        public static void AddToRandomPool(JObject doc, string poolKey, string listKey, string id)
        {
            var pool = EnsureObject(doc, poolKey);
            var arr = pool[listKey] as JArray ?? new JArray();
            if (!arr.Any(t => t.ToString().Equals(id, StringComparison.OrdinalIgnoreCase)))
                arr.Add(id);
            pool[listKey] = arr;
        }

        public static void OpenInExplorer(string path)
        {
            if (Directory.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }
            if (File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }
            throw new FileNotFoundException("Path not found.", path);
        }

        public static void OpenInDefaultApp(string path)
        {
            if (!File.Exists(path))
                throw new FileNotFoundException("File not found.", path);
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }

        public static HashSet<string> FlattenSelectRowIds(JToken? rowsToken)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in ParseRows(rowsToken))
            foreach (var id in row)
                if (!string.IsNullOrWhiteSpace(id))
                    set.Add(id);
            return set;
        }

        public static bool IsPlaceholderId(string id) =>
            id.StartsWith("empty", StringComparison.OrdinalIgnoreCase) ||
            id.StartsWith("locked", StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Resolves an on-disk SWF for a mappings entry (character / stage / music / …).
        /// Returns null when the mapping exists but the file is missing.
        /// </summary>
        public static string? ResolveEntrySwfPath(string installRoot, JObject doc, string category, string id)
        {
            if (string.IsNullOrWhiteSpace(installRoot) || doc == null || string.IsNullOrWhiteSpace(id))
                return null;
            if (doc[category] is not JObject cat) return null;
            if (cat[id] is not JObject obj) return null;

            var candidates = new List<string>();
            var file = obj["file"]?.ToString();
            if (!string.IsNullOrWhiteSpace(file))
            {
                var relative = DatMappingService.BuildDataRelativeSwfPath(category, file!);
                if (relative.StartsWith("expansions\\", StringComparison.OrdinalIgnoreCase)
                    || relative.StartsWith("expansions/", StringComparison.OrdinalIgnoreCase))
                {
                    candidates.Add(Path.Combine(installRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
                }
                else if (relative.StartsWith("ssf2mm\\", StringComparison.OrdinalIgnoreCase)
                         || relative.StartsWith("ssf2mm/", StringComparison.OrdinalIgnoreCase))
                {
                    var rest = relative.Replace('/', '\\');
                    if (rest.StartsWith("ssf2mm\\", StringComparison.OrdinalIgnoreCase))
                        rest = rest["ssf2mm\\".Length..];
                    candidates.Add(Path.Combine(ShellWorkspaceService.Ssf2mmDirectory(installRoot), rest));
                }
                else
                {
                    candidates.Add(Path.Combine(installRoot, "data", relative));
                    // Legacy pre-layout-move path for expansion packs still under data/character/expansions.
                    if (relative.StartsWith("character\\expansions\\", StringComparison.OrdinalIgnoreCase))
                    {
                        var rest = relative["character\\expansions\\".Length..];
                        candidates.Add(Path.Combine(
                            ShellWorkspaceService.CharacterExpansionsDirectory(installRoot), rest));
                    }
                }
            }

            // Fallback: {id}.swf in the category folder (same as music seed).
            var folder = category switch
            {
                "character" => "character",
                "stage" => "stage",
                "music" or "sound" or "audio" => "sound",
                "extra" => "misc",
                _ => category
            };
            candidates.Add(Path.Combine(installRoot, "data", folder, id + ".swf"));

            foreach (var path in candidates.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (File.Exists(path)) return path;
                var alt = SiblingSwf(path);
                if (alt != null && File.Exists(alt)) return alt;
            }
            return null;
        }

        private static string? SiblingSwf(string path)
        {
            if (path.EndsWith(".swf", StringComparison.OrdinalIgnoreCase))
                return path[..^4] + ".ssf";
            if (path.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                return path[..^4] + ".swf";
            return null;
        }

        public static bool EntryExistsOnDisk(string installRoot, JObject doc, string category, string id) =>
            ResolveEntrySwfPath(installRoot, doc, category, id) != null;

        public static int CountExistingEntries(string installRoot, JObject doc, string category)
        {
            if (doc[category] is not JObject cat) return 0;
            return cat.Properties().Count(p => EntryExistsOnDisk(installRoot, doc, category, p.Name));
        }

        public static List<string> ListExistingIds(string installRoot, JObject doc, string category)
        {
            if (doc[category] is not JObject cat) return new List<string>();
            return cat.Properties()
                .Where(p => EntryExistsOnDisk(installRoot, doc, category, p.Name))
                .Select(p => p.Name)
                .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Distinct <c>seriesIcon</c> values from character (and optionally stage) mappings.</summary>
        public static List<string> ListSeriesIcons(JObject doc, bool includeStages = false)
        {
            var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Collect(string category)
            {
                if (doc[category] is not JObject cat) return;
                foreach (var prop in cat.Properties())
                {
                    if (prop.Value is not JObject obj) continue;
                    var icon = obj["seriesIcon"]?.ToString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(icon))
                        set.Add(icon);
                }
            }
            Collect("character");
            if (includeStages) Collect("stage");
            return set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static void AddCategory(
            List<MappingsRosterItem> items,
            JObject doc,
            string category,
            HashSet<string>? rowIds,
            string installRoot,
            MenuSymbolIndex? symbols,
            bool isStage)
        {
            if (doc[category] is not JObject cat) return;
            foreach (var prop in cat.Properties())
            {
                if (prop.Value is not JObject obj) continue;
                var id = prop.Name;
                var file = obj["file"]?.ToString() ?? "";
                var full = ResolveEntrySwfPath(installRoot, doc, category, id) ?? "";

                var item = new MappingsRosterItem
                {
                    Id = id,
                    Category = category,
                    Name = obj["name"]?.ToString() ?? "",
                    File = file,
                    FilePub = obj["file_pub"]?.ToString() ?? "",
                    Guid = obj["guid"]?.ToString() ?? "",
                    SeriesIcon = obj["seriesIcon"]?.ToString() ?? "",
                    TrainingOnly = obj["training_only"]?.Type == JTokenType.Boolean
                        ? obj["training_only"]!.Value<bool>()
                        : null,
                    InSelectRows = rowIds?.Contains(id) == true,
                    FileExistsOnDisk = !string.IsNullOrEmpty(full),
                    HasSelectSymbol = symbols != null && (
                        isStage
                            ? symbols.StageSelect.Contains(id)
                            : symbols.CharacterSelect.Contains(id)),
                    HasPortraitSymbol = symbols?.CharSelectPortraits.Contains(id) == true
                };
                items.Add(item);
            }
        }

        private static List<List<string>> ParseRows(JToken? rowsToken)
        {
            var result = new List<List<string>>();
            if (rowsToken is not JArray rows) return result;
            foreach (var row in rows)
            {
                if (row is not JArray cells) continue;
                result.Add(cells.Select(c => c.ToString()).ToList());
            }
            return result;
        }

        private static JArray ToRowsToken(IEnumerable<IEnumerable<string>> rows)
        {
            var arr = new JArray();
            foreach (var row in rows)
                arr.Add(new JArray(row.Select(c => (JToken)c)));
            return arr;
        }

        private static JObject EnsureObject(JObject parent, string name)
        {
            if (parent[name] is JObject obj) return obj;
            obj = new JObject();
            parent[name] = obj;
            return obj;
        }
    }
}
