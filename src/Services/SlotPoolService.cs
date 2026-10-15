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
    /// <summary>
    /// One-time CSS/SSS/PA/stock slot pool injected into stock menu hosts during Prepare.
    /// After build, hosts are not mutated per content add.
    /// </summary>
    public sealed class SlotPoolService
    {
        public const int DefaultCharacterSlots = 32;
        public const int DefaultStageSlots = 64;
        /// <summary>Bump when pool contents change (e.g. PA/stock shells, editable CSS labels).</summary>
        public const int UiPoolVersion = 3;
        public const string CharacterSlotPrefix = "mm_c_";
        public const string StageSlotPrefix = "mm_s_";
        public const string ReadyFileName = "slot-pool-ready.json";

        private readonly MenuSlotService _menuSlots;

        public SlotPoolService(MenuSlotService? menuSlots = null)
        {
            _menuSlots = menuSlots ?? new MenuSlotService();
        }

        public static string CharacterSlotId(int slot) =>
            $"{CharacterSlotPrefix}{Math.Max(0, slot):D3}";

        public static string StageSlotId(int slot) =>
            $"{StageSlotPrefix}{Math.Max(0, slot):D3}";

        public static string ReadyPath(string installRoot) =>
            Path.Combine(ContentPackService.RootDir(installRoot), ReadyFileName);

        public static bool IsReady(string installRoot)
        {
            var path = ReadyPath(installRoot);
            if (!File.Exists(path)) return false;
            try
            {
                var jo = JObject.Parse(File.ReadAllText(path));
                if (jo["ready"]?.Value<bool>() != true) return false;
                var ver = jo["uiPoolVersion"]?.Value<int>() ?? 1;
                return ver >= UiPoolVersion;
            }
            catch
            {
                return false;
            }
        }

        public async Task EnsureSlotPoolAsync(
            string installRoot,
            string? jpexsPath = null,
            IProgress<string>? progress = null,
            CancellationToken cancellationToken = default,
            int characterSlots = DefaultCharacterSlots,
            int stageSlots = DefaultStageSlots)
        {
            ContentPackService.EnsureRoot(installRoot);
            if (IsReady(installRoot))
            {
                progress?.Report("Slot pool already ready — skipping rebuild.");
                return;
            }

            // Stale v1 ready file (CSS-only) must rebuild for PA/stock shells.
            var readyPath = ReadyPath(installRoot);
            if (File.Exists(readyPath))
            {
                try { File.Delete(readyPath); } catch { /* ignore */ }
                progress?.Report("Upgrading slot pool to UI pool v" + UiPoolVersion + "…");
            }

            var menuChars = MenuSlotService.FindMenuCharacters(installRoot)
                ?? throw new FileNotFoundException("menu_characters.swf not found.", installRoot);

            var charMap = await _menuSlots.ExportLinkageMapAsync(menuChars, jpexsPath, cancellationToken);
            var donorChar = PickDonor(charMap, "_select", new[] { "mario", "link", "pikachu", "donkeykong" });
            if (donorChar == null)
                throw new InvalidOperationException("No donor CSS chip found to build the character slot pool.");

            progress?.Report($"Building character CSS slot pool ({characterSlots} × {donorChar}_select)…");
            var charJobs = new List<(string DonorId, string AliasId, string PlayableId, string ChipLabel)>(characterSlots);
            for (var i = 0; i < characterSlots; i++)
            {
                var alias = CharacterSlotId(i);
                charJobs.Add((donorChar, alias, alias, "·"));
            }
            await _menuSlots.CloneCssSelectJobsAsync(
                menuChars, charJobs, jpexsPath, progress, cancellationToken);

            await _menuSlots.EnsurePoolSelectEditableLabelsAsync(
                menuChars, characterSlots, jpexsPath, progress, cancellationToken);

            // PA + stock HUD shells in misc2 (once). Runtime loaders overlay pack assets/.
            var misc2 = MenuSlotService.FindMisc2(installRoot)
                        ?? throw new FileNotFoundException("misc2.swf not found.", installRoot);
            var misc2Map = await _menuSlots.ExportLinkageMapAsync(misc2, jpexsPath, cancellationToken);
            var donorUi = PickDonor(misc2Map, "_charselect", new[] { donorChar, "mario", "link", "pikachu" })
                          ?? donorChar;
            if (!misc2Map.ContainsKey(donorUi + "_charselect"))
                throw new InvalidOperationException(
                    $"No donor PA ({donorUi}_charselect) found to build the UI slot pool.");

            progress?.Report(
                $"Building PA/stock UI pool ({characterSlots} × {donorUi}_charselect/_stock)…");
            // Deep-clone in chunks — shallow path only supports DefineSprite (not stock0 BitmapData).
            const int chunkSize = 8;
            for (var start = 0; start < characterSlots; start += chunkSize)
            {
                var end = Math.Min(characterSlots, start + chunkSize);
                var uiClones = new List<(string DonorLink, string NewLink, bool Inject)>();
                var misc2Clips = new List<string>();
                for (var i = start; i < end; i++)
                {
                    var alias = CharacterSlotId(i);
                    uiClones.Add((donorUi + "_charselect", alias + "_charselect", true));
                    if (misc2Map.ContainsKey(donorUi + "_stock"))
                        uiClones.Add((donorUi + "_stock", alias + "_stock", true));
                    if (misc2Map.ContainsKey(donorUi + "_stock0"))
                        uiClones.Add((donorUi + "_stock0", alias + "_stock0", true));
                    misc2Clips.Add(alias + "_stock");
                    misc2Clips.Add(alias + "_charselect");
                }

                progress?.Report($"  UI pool chunk slots {start}..{end - 1} ({uiClones.Count} linkages)…");
                await _menuSlots.CloneLinkagesBatchAsync(
                    misc2, uiClones, "characterID", jpexsPath, progress, cancellationToken,
                    injectScripts: true,
                    misc2Movieclips: misc2Clips,
                    shallowClone: false);

                // Refresh map after each chunk so subsequent donor lookups stay valid.
                misc2Map = await _menuSlots.ExportLinkageMapAsync(misc2, jpexsPath, cancellationToken);
            }

            // Ensure stock sprites PlaceObject → stock0 Bitmap (Samus pattern) for every pool slot.
            try
            {
                progress?.Report("Retargeting pool stock → stock0 bitmaps…");
                await RetargetPoolStocksAsync(
                    misc2, characterSlots, jpexsPath, progress, cancellationToken);
            }
            catch (Exception ex)
            {
                progress?.Report("Warning: stock→stock0 retarget: " + ex.Message);
            }

            string? donorStage = null;
            if (stageSlots > 0)
            {
                var menuStages = MenuSlotService.FindMenuStages(installRoot)
                    ?? throw new FileNotFoundException("menu_stages.swf not found.", installRoot);
                var stageMap = await _menuSlots.ExportLinkageMapAsync(menuStages, jpexsPath, cancellationToken);
                donorStage = PickDonor(stageMap, "_stage_select", new[] { "battlefield", "finaldestination", "hyrulecastle" });
                if (donorStage == null)
                {
                    donorStage = stageMap.Keys
                        .FirstOrDefault(k => k.EndsWith("_stage_select", StringComparison.OrdinalIgnoreCase))
                        ?[..^"_stage_select".Length];
                }
                if (string.IsNullOrWhiteSpace(donorStage))
                    throw new InvalidOperationException("No donor SSS chip found to build the stage slot pool.");

                progress?.Report($"Building stage slot pool ({stageSlots} × {donorStage}_stage_select)…");
                var stageClones = new List<(string DonorLink, string NewLink, bool Inject)>(stageSlots);
                var stageScriptMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                for (var i = 0; i < stageSlots; i++)
                {
                    var alias = StageSlotId(i);
                    stageClones.Add((donorStage + "_stage_select", alias + "_stage_select", true));
                    stageScriptMap[alias + "_stage_select"] = alias;
                }
                await _menuSlots.CloneLinkagesBatchAsync(
                    menuStages, stageClones, "stageID", jpexsPath, progress, cancellationToken,
                    injectScripts: true, scriptIdByNewLink: stageScriptMap, shallowClone: true);
            }
            else
            {
                progress?.Report("Skipping stage slot pool (character-only mode).");
            }

            var index = ContentPackService.LoadIndex(installRoot);
            index.CharacterSlotCount = characterSlots;
            index.StageSlotCount = stageSlots;
            ContentPackService.SaveIndex(installRoot, index);

            var ready = new JObject
            {
                ["ready"] = true,
                ["uiPoolVersion"] = UiPoolVersion,
                ["characterSlots"] = characterSlots,
                ["stageSlots"] = stageSlots,
                ["donorCharacter"] = donorChar,
                ["donorUi"] = donorUi,
                ["donorStage"] = donorStage,
                ["builtAt"] = DateTimeOffset.UtcNow.ToString("o")
            };
            await File.WriteAllTextAsync(ReadyPath(installRoot), ready.ToString(Formatting.Indented), cancellationToken);
            progress?.Report(
                $"Slot pool ready v{UiPoolVersion} — {characterSlots} CSS + PA/stock shells, {stageSlots} stage shells.");
        }

        private async Task RetargetPoolStocksAsync(
            string misc2Path,
            int characterSlots,
            string? jpexsPath,
            IProgress<string>? progress,
            CancellationToken cancellationToken)
        {
            var work = Path.Combine(Path.GetTempPath(), "ssf2mm-pool-stock0-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            try
            {
                var xmlPath = Path.Combine(work, "misc2.xml");
                await _menuSlots.Jpexs.SwfToXmlAsync(jpexsPath ?? "", misc2Path, xmlPath, cancellationToken);
                var xml = await File.ReadAllTextAsync(xmlPath, cancellationToken);
                var map = MenuSlotService.ParseSymbolClassMapFromXml(xml);
                var touched = 0;
                for (var i = 0; i < characterSlots; i++)
                {
                    var id = CharacterSlotId(i);
                    var (next, did) = MenuSlotService.RetargetStockSpriteToStock0InXml(xml, map, id);
                    if (did)
                    {
                        xml = next;
                        touched++;
                    }
                }
                if (touched == 0)
                {
                    progress?.Report("No stock→stock0 retargets needed (already wired or missing).");
                    return;
                }

                var outXml = Path.Combine(work, "misc2-retarget.xml");
                await File.WriteAllTextAsync(outXml, xml, cancellationToken);
                var outSwf = Path.Combine(work, "misc2-retarget.swf");
                await _menuSlots.Jpexs.XmlToSwfAsync(jpexsPath ?? "", outXml, outSwf, cancellationToken);
                if (!File.Exists(outSwf))
                    throw new InvalidOperationException("xml2swf failed for stock retarget.");
                File.Copy(outSwf, misc2Path, overwrite: true);
                progress?.Report($"Retargeted stock→stock0 for {touched} pool slot(s).");
            }
            finally
            {
                try { Directory.Delete(work, true); } catch { /* ignore */ }
            }
        }

        private static string? PickDonor(
            Dictionary<string, int> map,
            string suffix,
            IEnumerable<string> preferred)
        {
            foreach (var id in preferred)
            {
                if (map.ContainsKey(id + suffix))
                    return id;
            }
            return map.Keys
                .Where(k => k.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)
                            && !k.StartsWith("mm_", StringComparison.OrdinalIgnoreCase)
                            && !k.StartsWith("cz_", StringComparison.OrdinalIgnoreCase))
                .Select(k => k[..^suffix.Length])
                .FirstOrDefault();
        }
    }
}
