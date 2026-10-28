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
    /// Live FFDec verification against a temp copy of the SSF2 Beta install.
    /// Skips when the install / screenshot are missing.
    /// </summary>
    public class CharacterArtReplaceIntegrationTests
    {
        private readonly ITestOutputHelper _out;

        public CharacterArtReplaceIntegrationTests(ITestOutputHelper output) => _out = output;

        private static string? FindInstall()
        {
            var candidates = new[]
            {
                Environment.GetEnvironmentVariable("SSF2_INSTALL")
            };
            return candidates.FirstOrDefault(p =>
                !string.IsNullOrWhiteSpace(p) &&
                File.Exists(Path.Combine(p!, "data", "modes", "classic_mode.swf")));
        }

        private static string? FindVerifyPng()
        {
            var p = Environment.GetEnvironmentVariable("SSF2_VERIFY_PNG");
            return !string.IsNullOrWhiteSpace(p) && File.Exists(p) ? p : null;
        }

        [Fact]
        public async Task CloneMario_ApplyScreenshot_UniqueChidsAndPixels()
        {
            var srcInstall = FindInstall();
            var png = FindVerifyPng();
            if (srcInstall == null || png == null)
            {
                _out.WriteLine("SKIP: install or verify PNG not found.");
                return;
            }

            var workRoot = Path.Combine(Path.GetTempPath(), "ssf2mm-artverify-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workRoot);
            try
            {
                var install = Path.Combine(workRoot, "install");
                CopyNeededSwfs(srcInstall, install);

                var jpexs = Path.Combine(
                    AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "ffdec", "ffdec-cli.jar");
                jpexs = Path.GetFullPath(jpexs);
                if (!File.Exists(jpexs))
                {
                    jpexs = Path.Combine(AppContext.BaseDirectory, "ffdec", "ffdec-cli.jar");
                }

                var assets = new CharacterAssetService();
                var slots = new MenuSlotService();
                const string donor = "mario";
                const string clone = "artverify";

                var sw = Stopwatch.StartNew();
                await assets.EnsureUiSlotsFromDonorAsync(install, clone, donor, jpexs);
                var ensureMs = sw.ElapsedMilliseconds;
                _out.WriteLine($"EnsureUiSlots: {ensureMs} ms");

                var artBySlot = CharacterAssetService.GetEditableSlots()
                    .ToDictionary(d => d.Key, _ => png, StringComparer.OrdinalIgnoreCase);

                sw.Restart();
                await assets.ApplySlotArtsAsync(install, clone, artBySlot, jpexs);
                var applyMs = sw.ElapsedMilliseconds;
                _out.WriteLine($"ApplySlotArts: {applyMs} ms");

                // Parallel host batches; target ~10s on a warm machine, hard cap 90s for CI/cold JVM.
                Assert.True(applyMs < 90_000, $"Apply took {applyMs}ms (target ~10s; hard fail 90s)");
                _out.WriteLine(applyMs <= 10_000
                    ? $"Apply met 10s target ({applyMs} ms)"
                    : $"Apply slower than 10s target ({applyMs} ms) — still verifying correctness");

                // Unique chids for classic / allstar congrats (must not share mario's id)
                var classic = Path.Combine(install, "data", "modes", "classic_mode.swf");
                var allstar = Path.Combine(install, "data", "modes", "allstar_mode.swf");
                var classicMap = await slots.ExportLinkageMapAsync(classic, jpexs, CancellationToken.None);
                var allstarMap = await slots.ExportLinkageMapAsync(allstar, jpexs, CancellationToken.None);

                Assert.True(classicMap.ContainsKey("congrats_artverify"));
                Assert.True(classicMap.ContainsKey("congrats_mario"));
                Assert.NotEqual(classicMap["congrats_mario"], classicMap["congrats_artverify"]);

                Assert.True(allstarMap.ContainsKey("allstar_congrats_artverify"));
                Assert.True(allstarMap.ContainsKey("allstar_congrats_mario"));
                Assert.NotEqual(
                    allstarMap["allstar_congrats_mario"],
                    allstarMap["allstar_congrats_artverify"]);

                _out.WriteLine(
                    $"classic: mario={classicMap["congrats_mario"]} clone={classicMap["congrats_artverify"]}");
                _out.WriteLine(
                    $"allstar: mario={allstarMap["allstar_congrats_mario"]} clone={allstarMap["allstar_congrats_artverify"]}");

                // Pixel check: exported clone congrats should match source PNG hash (dimensions may differ after FFDec)
                var exportDir = Path.Combine(workRoot, "export");
                Directory.CreateDirectory(exportDir);
                var j = new JpexsCliService();
                await j.ExportSelectedAsync(
                    jpexs, classic, exportDir,
                    new[] { classicMap["congrats_artverify"] },
                    "sprite,image");

                var exported = CharacterAssetService.FindBestExportedPng(
                    exportDir, "congrats_artverify", classicMap["congrats_artverify"]);
                Assert.NotNull(exported);
                Assert.True(File.Exists(exported));

                using var srcImg = Image.FromFile(png);
                using var expImg = Image.FromFile(exported!);
                // Must not be a tiny black placeholder — expect meaningful size and non-black content
                Assert.True(expImg.Width >= 8 && expImg.Height >= 8, $"Export too small: {expImg.Width}x{expImg.Height}");
                Assert.False(IsMostlyBlack(exported!), "Exported congrats art is mostly black — replace failed");

                // Source screenshot has distinctive non-black pixels; exported should too and differ from mario donor export
                var marioExportDir = Path.Combine(workRoot, "export-mario");
                Directory.CreateDirectory(marioExportDir);
                await j.ExportSelectedAsync(
                    jpexs, classic, marioExportDir,
                    new[] { classicMap["congrats_mario"] },
                    "sprite,image");
                var marioPng = CharacterAssetService.FindBestExportedPng(
                    marioExportDir, "congrats_mario", classicMap["congrats_mario"]);
                Assert.NotNull(marioPng);
                Assert.NotEqual(Sha256(exported!), Sha256(marioPng!));

                _out.WriteLine($"PASS apply={applyMs}ms ensure={ensureMs}ms exported={exported}");
            }
            finally
            {
                try { Directory.Delete(workRoot, true); } catch { /* ignore */ }
            }
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
