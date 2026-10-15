using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SSF2ModManager.Services
{
    public sealed class CrazyCssRequest
    {
        public string SourceCharacterId { get; set; } = "";
        public int Rows { get; set; } = 3;
        public int SlotsPerRow { get; set; } = 8;
        public string ChipLabel { get; set; } = "";
    }

    /// <summary>One Crazy CSS cell: chip art, playable fighter, and on-chip label.</summary>
    public sealed class CrazyCssCell
    {
        public string DonorId { get; set; } = "";
        public string PlayableId { get; set; } = "";
        public string ChipLabel { get; set; } = "";

        public CrazyCssCell Clone() => new()
        {
            DonorId = DonorId,
            PlayableId = PlayableId,
            ChipLabel = ChipLabel
        };
    }

    /// <summary>Row-major Crazy CSS design ready to apply.</summary>
    public sealed class CrazyCssDesign
    {
        public int Rows { get; set; } = 3;
        public int Cols { get; set; } = 8;
        public List<CrazyCssCell> Cells { get; set; } = new();

        public int Total => Math.Max(0, Rows) * Math.Max(0, Cols);

        public static CrazyCssDesign Fill(
            int rows,
            int cols,
            string donorId,
            string playableId,
            string chipLabel)
        {
            rows = Math.Clamp(rows, 1, MappingsEditService.CssMaxRows);
            cols = Math.Clamp(cols, 1, MappingsEditService.CssMaxColsPerRow);
            var total = rows * cols;
            var cells = new List<CrazyCssCell>(total);
            for (var i = 0; i < total; i++)
            {
                cells.Add(new CrazyCssCell
                {
                    DonorId = donorId,
                    PlayableId = playableId,
                    ChipLabel = chipLabel
                });
            }
            return new CrazyCssDesign { Rows = rows, Cols = cols, Cells = cells };
        }

        public void EnsureSize()
        {
            Rows = Math.Clamp(Rows, 1, MappingsEditService.CssMaxRows);
            Cols = Math.Clamp(Cols, 1, MappingsEditService.CssMaxColsPerRow);
            var total = Rows * Cols;
            while (Cells.Count < total)
                Cells.Add(new CrazyCssCell());
            if (Cells.Count > total)
                Cells.RemoveRange(total, Cells.Count - total);
        }

        public IReadOnlyList<(string DonorId, string PlayableId, string ChipLabel)> ToCellTuples()
        {
            EnsureSize();
            return Cells
                .Select(c => (
                    string.IsNullOrWhiteSpace(c.DonorId) ? c.PlayableId : c.DonorId,
                    string.IsNullOrWhiteSpace(c.PlayableId) ? c.DonorId : c.PlayableId,
                    c.ChipLabel ?? ""))
                .ToList();
        }
    }

    public sealed class CrazyCssState
    {
        public List<List<string>> VanillaRows { get; set; } = new();
        public double? PositionX { get; set; }
        public double? PositionY { get; set; }
        public double? IconWidth { get; set; }
        public double? IconHeight { get; set; }
        public List<string> CrazyAliasIds { get; set; } = new();
        public string? SourceCharacterId { get; set; }
        public string? ChipLabel { get; set; }
        public DateTimeOffset CapturedAt { get; set; }
    }

    /// <summary>
    /// Builds custom character-select grids (unique CSS aliases, shared playable
    /// <c>characterID</c>s) and can restore the pre-crazy CSS layout.
    /// </summary>
    public sealed class CrazyCssService
    {
        public const string AliasPrefix = "cz_";
        public const string StateFileName = "ssf2mm-crazy-css.json";

        private readonly MenuSlotService _menuSlots;
        private readonly MappingsEditService _mappings;

        public CrazyCssService(MenuSlotService? menuSlots = null, MappingsEditService? mappings = null)
        {
            _menuSlots = menuSlots ?? new MenuSlotService();
            _mappings = mappings ?? new MappingsEditService();
        }

        public static string StatePath(string installRoot) =>
            Path.Combine(installRoot, "data", "menu", StateFileName);

        public static bool IsCrazyAliasId(string? id) =>
            !string.IsNullOrWhiteSpace(id)
            && id.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase);

        public static CrazyCssState? LoadState(string installRoot)
        {
            var path = StatePath(installRoot);
            if (!File.Exists(path)) return null;
            try
            {
                return JsonConvert.DeserializeObject<CrazyCssState>(File.ReadAllText(path));
            }
            catch
            {
                return null;
            }
        }

        public static void SaveState(string installRoot, CrazyCssState state)
        {
            var path = StatePath(installRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonConvert.SerializeObject(state, Formatting.Indented));
        }

        public static List<string> BuildAliasIds(string sourceId, int count)
        {
            sourceId = MenuSlotService.SanitizeId(sourceId);
            var stem = sourceId.Length > 12 ? sourceId[..12] : sourceId;
            var ids = new List<string>(count);
            for (var i = 0; i < count; i++)
                ids.Add(MenuSlotService.SanitizeId($"{AliasPrefix}{stem}_{i:D3}"));
            return ids;
        }

        public static List<List<string>> BuildGrid(IReadOnlyList<string> aliasIds, int rows, int slotsPerRow)
        {
            var grid = new List<List<string>>();
            var idx = 0;
            for (var r = 0; r < rows; r++)
            {
                var row = new List<string>();
                for (var c = 0; c < slotsPerRow && idx < aliasIds.Count; c++, idx++)
                    row.Add(aliasIds[idx]);
                if (row.Count > 0) grid.Add(row);
            }
            return grid;
        }

        public Task ApplyAsync(
            string installRoot,
            JObject doc,
            CrazyCssRequest req,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            var label = req.ChipLabel?.Trim() ?? "";
            var design = CrazyCssDesign.Fill(
                req.Rows, req.SlotsPerRow, req.SourceCharacterId, req.SourceCharacterId, label);
            return ApplyDesignAsync(installRoot, doc, design, jpexsPath, progress, cancellationToken);
        }

        /// <summary>
        /// Applies a full Crazy CSS design (mixed art / playable / labels per cell).
        /// </summary>
        public Task ApplyDesignAsync(
            string installRoot,
            JObject doc,
            CrazyCssDesign design,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (design == null) throw new ArgumentNullException(nameof(design));
            design.EnsureSize();
            return ApplyCustomGridAsync(
                installRoot, doc, design.Rows, design.Cols, design.ToCellTuples(),
                jpexsPath, progress, cancellationToken);
        }

        /// <summary>
        /// Row-major custom Crazy CSS grid. Each cell: donor art, playable characterID, chip label.
        /// </summary>
        public async Task ApplyCustomGridAsync(
            string installRoot,
            JObject doc,
            int rows,
            int cols,
            IReadOnlyList<(string DonorId, string PlayableId, string ChipLabel)> cells,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            rows = Math.Clamp(rows, 1, MappingsEditService.CssMaxRows);
            cols = Math.Clamp(cols, 1, MappingsEditService.CssMaxColsPerRow);
            var total = rows * cols;
            if (cells == null || cells.Count != total)
                throw new ArgumentException($"Expected {total} cells for a {cols}×{rows} grid.");

            var menu = MenuSlotService.FindMenuCharacters(installRoot)
                ?? throw new FileNotFoundException("menu_characters.swf not found.", installRoot);
            var map = await _menuSlots.ExportLinkageMapAsync(menu, jpexsPath, cancellationToken);

            EnsureVanillaSnapshot(installRoot, doc);
            var previous = LoadState(installRoot);
            if (previous?.CrazyAliasIds is { Count: > 0 })
            {
                progress?.Report("Clearing previous Crazy CSS aliases from mappings…");
                RemoveAliasEntries(doc, previous.CrazyAliasIds);
            }

            var aliasIds = new List<string>(total);
            var selectJobs = new List<(string DonorId, string AliasId, string PlayableId, string ChipLabel)>(total);
            for (var i = 0; i < total; i++)
            {
                var (donorId, playableId, label) = cells[i];
                donorId = MenuSlotService.SanitizeId(donorId);
                playableId = MenuSlotService.SanitizeId(playableId);
                if (string.IsNullOrWhiteSpace(donorId))
                    throw new InvalidOperationException($"Cell {i} is missing chip art (donor).");
                if (string.IsNullOrWhiteSpace(playableId))
                    throw new InvalidOperationException($"Cell {i} is missing a playable character.");
                if (doc["character"]?[playableId] is not JObject playableEntry)
                    throw new InvalidOperationException($"Character '{playableId}' is not in mappings.");
                if (!map.ContainsKey(donorId + "_select"))
                    throw new InvalidOperationException(
                        $"No CSS chip '{donorId}_select' in menu_characters.swf. " +
                        "Chip art must come from a character that already appears on CSS.");

                var alias = MenuSlotService.SanitizeId($"{AliasPrefix}grid_{i:D3}");
                aliasIds.Add(alias);
                var file = playableEntry["file"]?.ToString() ?? (playableId + ".swf");
                var seriesIcon = playableEntry["seriesIcon"]?.ToString();
                var name = string.IsNullOrWhiteSpace(label) ? playableId : label.Trim();
                MappingsEditService.UpsertEntry(
                    doc, "character", alias, file,
                    name: name,
                    seriesIcon: seriesIcon,
                    guid: MappingsEditService.AllocateUniqueGuid(doc));
                // characterID = real fighter. Engine must hash CSS chips by roster id
                // (SSF2MM_CSS_HASH_BY_ROSTER) so duplicate playable IDs still all show.
                selectJobs.Add((donorId, alias, playableId, name));
            }

            await _menuSlots.CloneCssSelectJobsAsync(
                menu, selectJobs, jpexsPath, progress, cancellationToken);

            var grid = BuildGrid(aliasIds, rows, cols);
            MappingsEditService.SetCssRows(doc, grid);
            var fit = MappingsEditService.FitSelectLayout(doc, isCss: true, grid);
            if (!string.IsNullOrWhiteSpace(fit))
                progress?.Report(fit);

            var state = LoadState(installRoot) ?? new CrazyCssState();
            state.CrazyAliasIds = aliasIds;
            var distinctPlayable = cells
                .Select(c => MenuSlotService.SanitizeId(c.PlayableId))
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            state.SourceCharacterId = distinctPlayable.Count == 1 ? distinctPlayable[0] : "custom";
            state.ChipLabel = cells.Select(c => c.ChipLabel).FirstOrDefault(l => !string.IsNullOrWhiteSpace(l))
                              ?? "custom";
            SaveState(installRoot, state);

            progress?.Report("Saving mappings…");
            await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            progress?.Report($"Crazy CSS ready — {cols}×{rows} ({total} chips).");
        }

        public async Task RestoreVanillaAsync(
            string installRoot,
            JObject doc,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            var state = LoadState(installRoot)
                ?? throw new InvalidOperationException(
                    "No Crazy CSS snapshot found. Vanilla CSS is only available after Crazy CSS has been used once.");

            if (state.VanillaRows == null || state.VanillaRows.Count == 0)
                throw new InvalidOperationException("Crazy CSS snapshot has no vanilla rows to restore.");

            progress?.Report("Restoring vanilla CSS layout…");
            if (state.CrazyAliasIds is { Count: > 0 })
                RemoveAliasEntries(doc, state.CrazyAliasIds);

            MappingsEditService.SetCssRows(doc, state.VanillaRows);
            if (state.PositionX.HasValue)
                MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.x", state.PositionX.Value);
            if (state.PositionY.HasValue)
                MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.y", state.PositionY.Value);
            if (state.IconWidth.HasValue)
                MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", state.IconWidth.Value);
            if (state.IconHeight.HasValue)
                MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", state.IconHeight.Value);

            var fit = MappingsEditService.FitSelectLayout(doc, isCss: true, state.VanillaRows);
            if (!string.IsNullOrWhiteSpace(fit))
                progress?.Report(fit);

            state.CrazyAliasIds = new List<string>();
            state.SourceCharacterId = null;
            state.ChipLabel = null;
            SaveState(installRoot, state);

            progress?.Report("Saving mappings…");
            await _mappings.CommitAsync(installRoot, doc, jpexsPath, progress, cancellationToken);
            progress?.Report("Vanilla CSS restored.");
        }

        private static void EnsureVanillaSnapshot(string installRoot, JObject doc)
        {
            var existing = LoadState(installRoot);
            if (existing?.VanillaRows is { Count: > 0 })
                return;

            var rows = MappingsEditService.GetCssRows(doc);
            if (rows.SelectMany(r => r).Any(IsCrazyAliasId))
            {
                throw new InvalidOperationException(
                    "CSS already looks Crazy and there is no vanilla snapshot to keep. " +
                    "Restore mappings from backup, or rearrange manually, then try Crazy CSS again.");
            }

            SaveState(installRoot, new CrazyCssState
            {
                VanillaRows = rows.Select(r => r.ToList()).ToList(),
                PositionX = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.x"),
                PositionY = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.y"),
                IconWidth = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width")
                    ?? MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size"),
                IconHeight = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.height")
                    ?? MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size"),
                CrazyAliasIds = new List<string>(),
                CapturedAt = DateTimeOffset.UtcNow
            });
        }

        private static void RemoveAliasEntries(JObject doc, IEnumerable<string> aliasIds)
        {
            if (doc["character"] is not JObject cat) return;
            foreach (var id in aliasIds.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (IsCrazyAliasId(id) || id.StartsWith(AliasPrefix, StringComparison.OrdinalIgnoreCase))
                    cat.Remove(id);
            }
        }
    }
}
