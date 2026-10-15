using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using SSF2ModManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace SSF2ModManager.Tests.Services
{
    /// <summary>
    /// Merge-gate: clone Dedede UI slots, apply the screenshot PNG everywhere, assert
    /// Samus-shaped scripts + unique chids + replaced pixels + CSS chip label.
    /// </summary>
    public class DededeCloneCompletenessIntegrationTests
    {
        private readonly ITestOutputHelper _out;

        public DededeCloneCompletenessIntegrationTests(ITestOutputHelper output) => _out = output;

        private static string? FindInstall()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("SSF2_INSTALL")
            };
            return candidates.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p) &&
                File.Exists(Path.Combine(p!, "data", "character", "dedede.swf")) &&
                File.Exists(Path.Combine(p!, "data", "modes", "classic_mode.swf")));
        }

        private static string? FindVerifyPng()
        {
            var p = Environment.GetEnvironmentVariable("SSF2_TEST_PNG");
            return File.Exists(p) ? p : null;
        }

        private static string ResolveJpexs()
        {
            var jpexs = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ffdec", "ffdec-cli.jar"));
            if (File.Exists(jpexs)) return jpexs;
            jpexs = Path.Combine(AppContext.BaseDirectory, "ffdec", "ffdec-cli.jar");
            return jpexs;
        }

        [Fact]
        public async Task CloneDedede_ScreenshotOnAllSlots_ScriptsChidsLabelAndPixels()
        {
            var srcInstall = FindInstall();
            var png = FindVerifyPng();
            if (srcInstall == null || png == null)
            {
                _out.WriteLine("SKIP: Dedede install or verify PNG not found.");
                return;
            }

            var jpexs = ResolveJpexs();
            Assert.True(File.Exists(jpexs), "ffdec-cli.jar missing: " + jpexs);

            var workRoot = Path.Combine(Path.GetTempPath(), "ssf2mm-dedede-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
            try
            {
                var install = Path.Combine(workRoot, "install");
                CopyNeededSwfs(srcInstall, install);

                var assets = new CharacterAssetService();
                var slots = new MenuSlotService();
                var j = new JpexsCliService();
                const string donor = "dedede";
                const string clone = "clonededede";
                const string displayName = "Clone Dedede";

                var sw = Stopwatch.StartNew();
                await assets.EnsureUiSlotsFromDonorAsync(
                    install, clone, donor, jpexs, cancellationToken: CancellationToken.None,
                    displayName: displayName,
                    seriesIcon: "Kirby_Symbol");
                _out.WriteLine($"EnsureUiSlots: {sw.ElapsedMilliseconds} ms");

                var artBySlot = CharacterAssetService.GetEditableSlots()
                    .ToDictionary(d => d.Key, _ => png!, StringComparer.OrdinalIgnoreCase);
                sw.Restart();
                await assets.ApplySlotArtsAsync(install, clone, artBySlot, jpexs);
                _out.WriteLine($"ApplySlotArts: {sw.ElapsedMilliseconds} ms");

                var menu = Path.Combine(install, "data", "menu", "menu_characters.swf");
                var misc2 = Path.Combine(install, "data", "misc", "misc2.swf");
                var classic = Path.Combine(install, "data", "modes", "classic_mode.swf");
                var allstar = Path.Combine(install, "data", "modes", "allstar_mode.swf");

                // --- Unique chids (no SymbolClass alias) ---
                var classicMap = await slots.ExportLinkageMapAsync(classic, jpexs, CancellationToken.None);
                var allstarMap = await slots.ExportLinkageMapAsync(allstar, jpexs, CancellationToken.None);
                var misc2Map = await slots.ExportLinkageMapAsync(misc2, jpexs, CancellationToken.None);

                Assert.True(classicMap.ContainsKey("congrats_clonededede"));
                Assert.NotEqual(classicMap["congrats_dedede"], classicMap["congrats_clonededede"]);
                Assert.True(allstarMap.ContainsKey("allstar_congrats_clonededede"));
                Assert.NotEqual(
                    allstarMap["allstar_congrats_dedede"],
                    allstarMap["allstar_congrats_clonededede"]);
                Assert.True(misc2Map.ContainsKey("clonededede_cssp"));
                Assert.NotEqual(misc2Map["dedede_cssp"], misc2Map["clonededede_cssp"]);

                _out.WriteLine(
                    $"classic chids donor={classicMap["congrats_dedede"]} clone={classicMap["congrats_clonededede"]}");
                _out.WriteLine(
                    $"allstar chids donor={allstarMap["allstar_congrats_dedede"]} clone={allstarMap["allstar_congrats_clonededede"]}");

                // --- Scripts (Samus-shaped) ---
                await AssertScriptsExistAsync(j, jpexs, menu, new[] { "clonededede_select" }, workRoot, "menu");
                var misc2Scripts = new List<string>
                {
                    "clonededede_charselect", "clonededede_stock", "clonededede_cssp",
                    "clonededede_classic", "clonededede_classic_trophy",
                    "clonededede_allstar", "clonededede_event"
                };
                if (misc2Map.ContainsKey("dedede_stock0"))
                    misc2Scripts.Add("clonededede_stock0");
                await AssertScriptsExistAsync(j, jpexs, misc2, misc2Scripts, workRoot, "misc2");
                await AssertScriptsExistAsync(j, jpexs, classic, new[] { "congrats_clonededede" }, workRoot, "classic");
                await AssertScriptsExistAsync(
                    j, jpexs, allstar, new[] { "allstar_congrats_clonededede" }, workRoot, "allstar");

                // --- CSS chip label via exported text (DefineText glyphs) ---
                var menuMap = await slots.ExportLinkageMapAsync(menu, jpexs, CancellationToken.None);
                Assert.True(menuMap.ContainsKey("clonededede_select"));
                var chipTextDir = Path.Combine(workRoot, "chip-text");
                Directory.CreateDirectory(chipTextDir);
                // Export texts referenced by the clone select sprite
                var menuXmlPath = Path.Combine(workRoot, "menu.xml");
                await j.SwfToXmlAsync(jpexs, menu, menuXmlPath, CancellationToken.None);
                var menuXml = await File.ReadAllTextAsync(menuXmlPath);
                var textIds = MenuSlotService.FindTextCharacterIdsInSprite(
                    menuXml, menuMap["clonededede_select"]);
                Assert.NotEmpty(textIds);
                await j.ExportSelectedAsync(jpexs, menu, chipTextDir, textIds, "text");
                var labelBlob = string.Join("\n",
                    Directory.GetFiles(chipTextDir, "*.txt", SearchOption.AllDirectories)
                        .Select(File.ReadAllText));
                Assert.Contains("CLONE", labelBlob, StringComparison.OrdinalIgnoreCase);
                Assert.Contains("DEDEDE", labelBlob, StringComparison.OrdinalIgnoreCase);
                _out.WriteLine("CSS chip text export contains CLONE / DEDEDE.");

                // --- Misc2 register(resources).movieclips ---
                var misc2As = Directory.GetFiles(
                        Path.Combine(workRoot, "scripts-misc2"), "Misc2.as", SearchOption.AllDirectories)
                    .FirstOrDefault();
                Assert.NotNull(misc2As);
                var misc2Text = await File.ReadAllTextAsync(misc2As!);
                Assert.Contains("clonededede_charselect", misc2Text, StringComparison.Ordinal);
                Assert.Contains("clonededede_stock", misc2Text, StringComparison.Ordinal);
                Assert.Contains("clonededede_classic", misc2Text, StringComparison.Ordinal);
                _out.WriteLine("Misc2.as registers clonededede stock/classic/charselect.");

                // --- Event label ---
                var misc2XmlPath = Path.Combine(workRoot, "misc2.xml");
                await j.SwfToXmlAsync(jpexs, misc2, misc2XmlPath, CancellationToken.None);
                var misc2Xml = await File.ReadAllTextAsync(misc2XmlPath);
                Assert.True(misc2Map.ContainsKey("clonededede_event"));
                var eventTextIds = MenuSlotService.FindTextCharacterIdsInSprite(
                    misc2Xml, misc2Map["clonededede_event"]);
                if (eventTextIds.Count > 0)
                {
                    var eventTextDir = Path.Combine(workRoot, "event-text");
                    Directory.CreateDirectory(eventTextDir);
                    await j.ExportSelectedAsync(jpexs, misc2, eventTextDir, eventTextIds, "text");
                    var eventBlob = string.Join("\n",
                        Directory.GetFiles(eventTextDir, "*.txt", SearchOption.AllDirectories)
                            .Select(File.ReadAllText));
                    Assert.True(
                        eventBlob.Contains("CLONE", StringComparison.OrdinalIgnoreCase)
                        && eventBlob.Contains("DEDEDE", StringComparison.OrdinalIgnoreCase),
                        "Event text should contain CLONE and DEDEDE, got: " + eventBlob.Replace('\r', ' ').Replace('\n', ' '));
                    _out.WriteLine("Event text export contains CLONE / DEDEDE.");
                }
                else
                    _out.WriteLine("WARN: no text tags on clonededede_event (skipped label assert).");

                // --- Pixels: congrats + sprite PAs clone ≠ donor ---
                await AssertLinkagePixelsDifferAsync(
                    j, jpexs, classic, "congrats_clonededede", "congrats_dedede",
                    classicMap, workRoot, "congrats", png, requireExactSize: true);
                foreach (var (cloneLink, donorLink) in new[]
                         {
                             ("clonededede_stock", "dedede_stock"),
                             ("clonededede_event", "dedede_event"),
                             ("clonededede_classic", "dedede_classic"),
                         })
                {
                    if (!misc2Map.ContainsKey(cloneLink) || !misc2Map.ContainsKey(donorLink))
                        continue;
                    await AssertLinkagePixelsDifferAsync(
                        j, jpexs, misc2, cloneLink, donorLink,
                        misc2Map, workRoot, cloneLink, png, requireExactSize: false);
                }

                _out.WriteLine("PASS Dedede clone completeness.");
            }
            finally
            {
                try { Directory.Delete(workRoot, true); } catch { /* ignore */ }
            }
        }

        private async Task AssertScriptsExistAsync(
            JpexsCliService j,
            string jpexs,
            string swf,
            IEnumerable<string> classNames,
            string workRoot,
            string label)
        {
            var dir = Path.Combine(workRoot, "scripts-" + label);
            Directory.CreateDirectory(dir);
            await j.ExportAsync(jpexs, swf, dir, "script", CancellationToken.None);
            var found = Directory.GetFiles(dir, "*.as", SearchOption.AllDirectories)
                .Select(Path.GetFileNameWithoutExtension)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var name in classNames)
            {
                Assert.True(found.Contains(name), $"Missing script {name}.as in {Path.GetFileName(swf)} ({label})");
                _out.WriteLine($"OK script {name}");
            }
        }

        private async Task AssertLinkagePixelsDifferAsync(
            JpexsCliService j,
            string jpexs,
            string swf,
            string cloneLink,
            string donorLink,
            IReadOnlyDictionary<string, int> map,
            string workRoot,
            string label,
            string? verifyPng,
            bool requireExactSize)
        {
            var exportClone = Path.Combine(workRoot, "exp-" + label + "-clone");
            var exportDonor = Path.Combine(workRoot, "exp-" + label + "-donor");
            Directory.CreateDirectory(exportClone);
            Directory.CreateDirectory(exportDonor);
            await j.ExportSelectedAsync(jpexs, swf, exportClone, new[] { map[cloneLink] }, "sprite,image");
            await j.ExportSelectedAsync(jpexs, swf, exportDonor, new[] { map[donorLink] }, "sprite,image");
            var clonePng = CharacterAssetService.FindBestExportedPng(exportClone, cloneLink, map[cloneLink]);
            var donorPng = CharacterAssetService.FindBestExportedPng(exportDonor, donorLink, map[donorLink]);
            Assert.NotNull(clonePng);
            Assert.NotNull(donorPng);
            Assert.NotEqual(Sha256(clonePng!), Sha256(donorPng!));
            Assert.False(IsMostlyBlack(clonePng!), $"Clone {cloneLink} art is mostly black");
            if (requireExactSize && verifyPng != null)
            {
                using var img = Image.FromFile(clonePng!);
                using var src = Image.FromFile(verifyPng);
                Assert.Equal(src.Width, img.Width);
                Assert.Equal(src.Height, img.Height);
            }
            _out.WriteLine($"OK pixels differ for {cloneLink}");
        }

        private static void CopyNeededSwfs(string srcInstall, string destInstall)
        {
            var files = new[]
            {
                @"data\menu\menu_characters.swf",
                @"data\misc\misc2.swf",
                @"data\misc\misc.swf",
                @"data\modes\classic_mode.swf",
                @"data\modes\allstar_mode.swf",
                @"data\character\dedede.swf",
            };
            foreach (var rel in files)
            {
                var src = Path.Combine(srcInstall, rel);
                if (!File.Exists(src)) continue;
                var dest = Path.Combine(destInstall, rel);
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Copy(src, dest, overwrite: true);
            }
        }

        private static string Sha256(string path)
        {
            using var fs = File.OpenRead(path);
            return Convert.ToHexString(SHA256.HashData(fs));
        }

        private static bool IsMostlyBlack(string path)
        {
            using var bmp = new Bitmap(path);
            var samples = 0;
            var black = 0;
            for (var y = 0; y < bmp.Height; y += Math.Max(1, bmp.Height / 16))
            for (var x = 0; x < bmp.Width; x += Math.Max(1, bmp.Width / 16))
            {
                var c = bmp.GetPixel(x, y);
                samples++;
                if (c.A < 16 || (c.R < 12 && c.G < 12 && c.B < 12))
                    black++;
            }
            return samples > 0 && black * 100 / samples > 90;
        }
    }
}
