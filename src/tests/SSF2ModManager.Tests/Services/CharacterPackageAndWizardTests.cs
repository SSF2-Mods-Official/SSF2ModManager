using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using FluentAssertions;
using FlaStepWizard;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class CharacterPackageAndWizardTests
    {
        [Fact]
        public void FanOutSharedPaSlots_CopiesPaToAllSharedKeys()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-fanout-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var pa = Path.Combine(dir, "pa.png");
                File.WriteAllBytes(pa, CharacterCloneService.CreatePlaceholderPngBytes());
                var arts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["pa"] = pa,
                    ["select"] = pa
                };
                CharacterAssetService.FanOutSharedPaSlots(arts);
                foreach (var key in CharacterAssetService.SharedPaSlotKeys)
                    arts.Should().ContainKey(key).WhoseValue.Should().Be(pa);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void IsPaCoverFitLinkage_IncludesClassicAllstarEventTrophy()
        {
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_charselect").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_cssp").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_classic").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_classic_trophy").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_allstar").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_event").Should().BeTrue();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_stock").Should().BeFalse();
            MenuSlotService.IsPaCoverFitLinkage("congrats_clonesamus").Should().BeFalse();
            MenuSlotService.IsPaCoverFitLinkage("clonesamus_select").Should().BeFalse();
        }

        [Fact]
        public void BuildSingleLineChipLabel_UsedForPreviewNotEllipsisClone()
        {
            MenuSlotService.BuildSingleLineChipLabel("Clone Samus").Should().Be("CLONE-SAMUS");
            MenuSlotService.BuildSingleLineChipLabel("Clone Samus").Should().NotContain("…");
            MenuSlotService.BuildSingleLineChipLabel("Clone Fox").Should().Be("CLONE FOX");
        }

        [Fact]
        public void MappingsRelativeSwfPath_UsesDisplayNameFolder()
        {
            CharacterPackageService.MappingsRelativeSwfPath("Clone Samus", "clonesamus")
                .Should().Be("Clone Samus/clonesamus.swf");
            CharacterPackageService.SanitizeFolderName("Clone:Samus?").Should().Be("Clone_Samus_");
            DatMappingService.BuildDataRelativeSwfPath(
                    "character", "Clone Samus/clonesamus.swf")
                .Replace('/', '\\')
                .Should().Be(@"character\Clone Samus\clonesamus.swf");
        }

        [Fact]
        public void LoadAssetsFromFolder_ReadsConventionNames()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-assets-" + Guid.NewGuid().ToString("N"));
            var assets = Path.Combine(dir, "assets");
            Directory.CreateDirectory(assets);
            try
            {
                var bytes = CharacterCloneService.CreatePlaceholderPngBytes();
                File.WriteAllBytes(Path.Combine(assets, "select.png"), bytes);
                File.WriteAllBytes(Path.Combine(assets, "pa.png"), bytes);
                File.WriteAllBytes(Path.Combine(assets, "stock.png"), bytes);
                File.WriteAllBytes(Path.Combine(assets, "symbol.png"), bytes);
                File.WriteAllBytes(Path.Combine(dir, "symbol.png"), bytes);
                File.WriteAllBytes(Path.Combine(assets, "goku_select_placeholder.png"), bytes);
                var loaded = CharacterPackageService.LoadAssetsFromFolder(dir);
                loaded.Should().ContainKey("select");
                loaded.Should().ContainKey("pa");
                loaded.Should().ContainKey("stock");
                loaded.Should().ContainKey("symbol");
                CharacterPackageService.CleanupPackageAssetDirt(dir);
                File.Exists(Path.Combine(dir, "symbol.png")).Should().BeFalse();
                File.Exists(Path.Combine(assets, "goku_select_placeholder.png")).Should().BeFalse();
                File.Exists(Path.Combine(assets, "symbol.png")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void WizardStepProgress_ReportsIncreasingPercent()
        {
            var reports = new List<(string Message, double Percent, string? Step)>();
            var prog = new Progress<(string Message, double Percent, string? Step)>(r => reports.Add(r));
            var steps = new WizardStepProgress(prog, WizardStepProgress.CloneCharacterSteps);
            steps.BeginStep(0, "clone");
            steps.CompleteStep();
            steps.BeginStep(1, "ui");
            steps.CompleteStep();
            steps.Finish("done");
            reports.Should().NotBeEmpty();
            reports.Last().Percent.Should().Be(100);
            reports.Any(r => r.Step != null && r.Step.StartsWith("Step ")).Should().BeTrue();
            // After first complete, percent should be ~15
            var afterFirst = reports.First(r => r.Message.Contains("done", StringComparison.OrdinalIgnoreCase)
                                                || r.Percent >= 14);
            afterFirst.Percent.Should().BeGreaterThanOrEqualTo(14);
        }

        [Fact]
        public void CharacterUpdatePrefs_DefaultBothTrue()
        {
            var p = new CharacterUpdatePrefs();
            p.UpdateAssets.Should().BeTrue();
            p.UpdateSwf.Should().BeTrue();
        }

        [Fact]
        public void WizardPreviewSlotKeys_ExcludesRedundantPaVariants()
        {
            CharacterAssetService.WizardPreviewSlotKeys.Should().Contain("select");
            CharacterAssetService.WizardPreviewSlotKeys.Should().Contain("pa");
            CharacterAssetService.WizardPreviewSlotKeys.Should().NotContain("charselect");
            CharacterAssetService.WizardPreviewSlotKeys.Should().NotContain("classic");
            CharacterAssetService.WizardPreviewSlotKeys.Should().NotContain("allstar");
            CharacterAssetService.WizardPreviewSlotKeys.Should().Contain("congrats");
            CharacterAssetService.WizardPreviewSlotKeys.Should().Contain("event");
        }

        [Fact]
        public void ResolveFlaOutputBaseName_PrefersExplicitIdOverTempSwfStem()
        {
            JpexsCliService.ResolveFlaOutputBaseName(
                    @"C:\temp\ssf2mm-fla-abcdef.swf", "knuckles")
                .Should().Be("knuckles");
            JpexsCliService.ResolveFlaOutputBaseName(
                    @"C:\temp\ssf2mm-fla-abcdef.swf", null)
                .Should().Be("ssf2mm-fla-abcdef");
            JpexsCliService.ResolveFlaOutputBaseName(
                    @"C:\temp\bad|name.swf", "notmario")
                .Should().Be("notmario");
        }

        [Fact]
        public void FlattenFlaExport_PromotesNestedBinaryFla_MovesAllSidecarsUp()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-flaflat-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(dir, "Clone_Mage.fla");
            Directory.CreateDirectory(nested);
            try
            {
                File.WriteAllBytes(Path.Combine(nested, "Clone_Mage.fla"), new byte[] { 1, 2, 3, 4 });
                File.WriteAllText(Path.Combine(nested, "Main.as"), "timeline entrypoint");
                File.WriteAllText(Path.Combine(nested, "SSF2API.as"), "engine api sidecar");
                File.WriteAllText(Path.Combine(nested, "Clone_Mage_bm_bthrow0.as"), "linked symbol class");
                File.WriteAllText(Path.Combine(nested, "CloneMageExt.as"), "character ext");
                // Action-layer scripts live in {name}_fla/ — they should be moved up too
                Directory.CreateDirectory(Path.Combine(nested, "Clone_Mage_fla"));
                File.WriteAllText(Path.Combine(nested, "Clone_Mage_fla", "AirDodge_110.as"), "package Clone_Mage_fla { }");

                var dest = JpexsCliService.FlattenFlaExport(dir, "Clone_Mage");
                dest.Should().Be(Path.Combine(dir, "Clone_Mage.fla"));
                File.Exists(dest).Should().BeTrue();
                File.ReadAllBytes(dest).Should().Equal(1, 2, 3, 4);
                // The outer nested dir (which was a directory) is gone
                Directory.Exists(nested).Should().BeFalse();
                // All sidecars promoted to dir root
                File.Exists(Path.Combine(dir, "Main.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "SSF2API.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Mage_bm_bthrow0.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "CloneMageExt.as")).Should().BeTrue();
                // Action-layer folder moved up alongside the FLA
                Directory.Exists(Path.Combine(dir, "Clone_Mage_fla")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Mage_fla", "AirDodge_110.as")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FlattenFlaExport_PromotesSwfNamedInnerFlaToRequestedBaseName()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-flaflat-swfname-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(dir, "Clone_Mage.fla");
            Directory.CreateDirectory(nested);
            try
            {
                File.WriteAllBytes(Path.Combine(nested, "Clone_Mage_untyped.fla"), new byte[] { 7, 8, 9 });
                File.WriteAllText(Path.Combine(nested, "Main.as"), "document class");

                var dest = JpexsCliService.FlattenFlaExport(dir, "Clone_Mage");
                dest.Should().Be(Path.Combine(dir, "Clone_Mage.fla"));
                File.Exists(dest).Should().BeTrue();
                File.ReadAllBytes(dest).Should().Equal(7, 8, 9);
                Directory.Exists(nested).Should().BeFalse();
                File.Exists(Path.Combine(dir, "Main.as")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FlattenFlaExport_MovesUncompressedXflChildrenUp()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xflflat-" + Guid.NewGuid().ToString("N"));
            var nested = Path.Combine(dir, "hero.fla");
            Directory.CreateDirectory(Path.Combine(nested, "LIBRARY"));
            try
            {
                File.WriteAllText(Path.Combine(nested, "DOMDocument.xml"), "<DOMDocument/>");
                File.WriteAllText(Path.Combine(nested, "LIBRARY", "a.xml"), "a");

                JpexsCliService.FlattenFlaExport(dir, "hero");
                File.Exists(Path.Combine(dir, "DOMDocument.xml")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "LIBRARY", "a.xml")).Should().BeTrue();
                Directory.Exists(nested).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ParseSymbolClassNamesFromCsv_ReadsQuotedAndBareNames()
        {
            var csv = """
                1556;"Clone_Mage_black_mage"
                1555;"Clone_Mage_fla.TechRoll_137"
                100;Main
                """;
            var names = JpexsCliService.ParseSymbolClassNamesFromCsv(csv);
            names.Should().Contain("Clone_Mage_black_mage");
            names.Should().Contain("Clone_Mage_fla.TechRoll_137");
            names.Should().Contain("Main");
        }

        [Fact]
        public void PruneFlaSidecars_KeepsExternalApiScripts_DeletesLinkedSymbolDumps()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-flaprune-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "Clone_Mage.fla"), new byte[] { 9, 8, 7 });
                File.WriteAllText(Path.Combine(dir, "Main.as"), """
                    package { public dynamic class Main extends SSF2Asset { } }
                    """);
                File.WriteAllText(Path.Combine(dir, "CloneMageExt.as"), """
                    package { public class CloneMageExt extends Clone_Mage_SSF2CharacterExt { } }
                    """);
                File.WriteAllText(Path.Combine(dir, "Clone_Mage_bm_bthrow0.as"), """
                    package { import flash.display.BitmapData;
                    public dynamic class Clone_Mage_bm_bthrow0 extends BitmapData { } }
                    """);
                Directory.CreateDirectory(Path.Combine(dir, "Clone_Mage_fla"));
                File.WriteAllText(Path.Combine(dir, "Clone_Mage_fla", "AirDodge_110.as"), "package Clone_Mage_fla { }");
                Directory.CreateDirectory(Path.Combine(dir, "fl"));
                File.WriteAllText(Path.Combine(dir, "fl", "motion.xml"), "<motion/>");

                var sym = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Main",
                    "Clone_Mage_bm_bthrow0",
                    "Clone_Mage_fla.AirDodge_110"
                };
                JpexsCliService.PruneFlaSidecars(dir, "Clone_Mage", sym);

                File.Exists(Path.Combine(dir, "Main.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "CloneMageExt.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Mage_bm_bthrow0.as")).Should().BeFalse();
                Directory.Exists(Path.Combine(dir, "Clone_Mage_fla")).Should().BeFalse();
                Directory.Exists(Path.Combine(dir, "fl")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Mage.fla")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void IsEmptyFlaSidecar_SoundStubTrue_GrabWithMethodFalse()
        {
            var sound = """
                package
                {
                   import flash.media.Sound;
                   public dynamic class Clone_Maage_bio extends Sound
                   {
                      public function Clone_Maage_bio()
                      {
                         super();
                      }
                   }
                }
                """;
            JpexsCliService.IsEmptyFlaSidecar(sound, "Clone_Maage_bio").Should().BeTrue();

            var grab = """
                package Clone_Maage_fla
                {
                   import flash.display.MovieClip;
                   public dynamic class Grab_106 extends MovieClip
                   {
                      public var self:CloneMaageExt;
                      public function Grab_106() { super(); }
                      public function xSpeedDecay() : void
                      {
                         this.self.setXSpeed(0);
                      }
                   }
                }
                """;
            JpexsCliService.IsEmptyFlaSidecar(grab, "Grab_106").Should().BeFalse();

            var idle = """
                package Clone_Maage_fla
                {
                   import flash.display.MovieClip;
                   public dynamic class Idle_3 extends MovieClip
                   {
                      public function Idle_3() { addFrameScript(0, this.frame1); }
                      function frame1() : * { stop(); }
                   }
                }
                """;
            JpexsCliService.IsEmptyFlaSidecar(idle, "Idle_3").Should().BeTrue();
        }

        [Fact]
        public void PruneEmptyFlaSidecars_DeletesSoundStub_KeepsMainAndGrab()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-emptystub-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "Clone_Maage_fla"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"), """
                    package { public dynamic class Main extends SSF2Asset {
                      public function Main() { super(); }
                    } }
                    """);
                File.WriteAllText(Path.Combine(dir, "Clone_Maage_bio.as"), """
                    package { import flash.media.Sound;
                    public dynamic class Clone_Maage_bio extends Sound {
                      public function Clone_Maage_bio() { super(); }
                    } }
                    """);
                File.WriteAllText(Path.Combine(dir, "Clone_Maage_fla", "Grab_106.as"), """
                    package Clone_Maage_fla {
                      public dynamic class Grab_106 extends MovieClip {
                        public function Grab_106() { super(); }
                        public function xSpeedDecay() : void { this.self.setXSpeed(0); }
                      }
                    }
                    """);
                File.WriteAllText(Path.Combine(dir, "Clone_Maage_fla", "Idle_3.as"), """
                    package Clone_Maage_fla {
                      public dynamic class Idle_3 extends MovieClip {
                        public function Idle_3() { addFrameScript(0, this.frame1); }
                        function frame1() : * { stop(); }
                      }
                    }
                    """);

                JpexsCliService.PruneEmptyFlaSidecars(dir).Should().Be(2);
                File.Exists(Path.Combine(dir, "Main.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Maage_bio.as")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "Clone_Maage_fla", "Grab_106.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Clone_Maage_fla", "Idle_3.as")).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RelocateFlaScriptsToCommon_MovesSsf2NotMainExtOrFl()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-commonas-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, "fl", "motion"));
            Directory.CreateDirectory(Path.Combine(dir, "Hero_fla"));
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"), "package { public class Main {} }");
                File.WriteAllText(Path.Combine(dir, "HeroExt.as"), "package { public class HeroExt extends lol_SSF2CharacterExt {} }");
                File.WriteAllText(Path.Combine(dir, "lol_SSF2CharacterExt.as"), "package { public class lol_SSF2CharacterExt {} }");
                File.WriteAllText(Path.Combine(dir, "SSF2Character.as"), "package { public class SSF2Character {} }");
                File.WriteAllText(Path.Combine(dir, "fl", "motion", "ColorMatrix.as"), "package fl.motion {}");
                File.WriteAllText(Path.Combine(dir, "Hero_fla", "Grab_106.as"), "package Hero_fla { public class Grab_106 {} }");

                JpexsCliService.RelocateFlaScriptsToCommon(dir).Should().BeGreaterThan(0);
                File.Exists(Path.Combine(dir, "Main.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "HeroExt.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "lol_SSF2CharacterExt.as")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "common", "SSF2CharacterExt.as")).Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "HeroExt.as")).Should().Contain("extends SSF2CharacterExt");
                File.ReadAllText(Path.Combine(dir, "common", "SSF2CharacterExt.as")).Should().Contain("class SSF2CharacterExt");
                File.Exists(Path.Combine(dir, "SSF2Character.as")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "common", "SSF2Character.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "Hero_fla", "Grab_106.as")).Should().BeTrue();
                File.Exists(Path.Combine(dir, "common", "Hero_fla", "Grab_106.as")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "fl", "motion", "ColorMatrix.as")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "common", "fl", "motion", "ColorMatrix.as")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void MatchWavForSound_UsesStemThenTrailingId()
        {
            var wavs = new[] { @"C:\tmp\12.wav", @"C:\tmp\idle.wav" };
            JpexsCliService.MatchWavForSound("hit_12.mp3", wavs).Should().Be(@"C:\tmp\12.wav");
            JpexsCliService.MatchWavForSound("sound1", new[] { @"C:\tmp\sound1.wav" })
                .Should().Be(@"C:\tmp\sound1.wav");
            JpexsCliService.MatchWavForSound("sound1", wavs).Should().BeNull();
            JpexsCliService.MatchWavForSound("idle.mp3", wavs).Should().Be(@"C:\tmp\idle.wav");
            JpexsCliService.MatchWavForSound("missing.mp3", wavs).Should().BeNull();

            var dumps = new[] { @"C:\tmp\1.goku_charge_yell.wav", @"C:\tmp\12.goku_jump.wav" };
            JpexsCliService.MatchWavForSound("sound1.wav", dumps).Should().Be(@"C:\tmp\1.goku_charge_yell.wav");
            JpexsCliService.MatchWavForSound("goku_charge_yell", dumps).Should().Be(@"C:\tmp\1.goku_charge_yell.wav");
        }

        [Fact]
        public void ApplyFlaVanillaExportPatches_InjectsLeftoverJabHelpersDespiteAddEventListener()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-leftover-" + Guid.NewGuid().ToString("N"));
            var fla = Path.Combine(dir, "Clone.fla");
            var pkg = Path.Combine(dir, "Clone_fla");
            Directory.CreateDirectory(pkg);
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """<DOMDocument xmlns="http://ns.adobe.com/xfl/2008/"/>""");
                    WriteZipEntry(zip, "LIBRARY/Symbol 1545.xml", """
                        <DOMSymbolItem name="Symbol 1545">
                          <DOMFrame index="0">
                            <Actionscript>
                              <script>this.self.createTimer(1,-1,this.checkControls);</script>
                            </Actionscript>
                          </DOMFrame>
                        </DOMSymbolItem>
                        """);
                }
                File.WriteAllText(Path.Combine(pkg, "JabCombo_25.as"), """
                    package Clone_fla {
                      public dynamic class JabCombo_25 extends MovieClip {
                        public function jumpCancel(param1:* = null):void {
                          this.self.removeEventListener(SSF2Event.ATTACK_HIT, this.jumpCancel);
                        }
                        public function checkControls():* {
                          this.pressed = true;
                        }
                      }
                    }
                    """);

                JpexsCliService.ApplyFlaVanillaExportPatches(fla, "Clone");

                using var read = ZipFile.OpenRead(fla);
                using var xml = new StreamReader(read.GetEntry("LIBRARY/Symbol 1545.xml")!.Open());
                var body = xml.ReadToEnd();
                body.Should().Contain("function checkControls(");
                body.Should().Contain("createTimer(1,-1,this.checkControls)");
                File.Exists(Path.Combine(pkg, "JabCombo_25.as")).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void SniffSoundMagic_DetectsFlvAndRiff()
        {
            JpexsCliService.SniffSoundMagic("FLV"u8).Should().Be(JpexsCliService.SoundMediaKind.Flv);
            JpexsCliService.SniffSoundMagic("RIFF"u8).Should().Be(JpexsCliService.SoundMediaKind.RiffWav);
            JpexsCliService.SniffSoundMagic("ID3"u8).Should().Be(JpexsCliService.SoundMediaKind.Mp3);
        }

        [Fact]
        public void PatchDomSoundItemXml_SetsUncompressed_DoesNotRename()
        {
            var xml = """<DOMSoundItem name="sound1" href="sound1.mp3" format="22kHz 16bit Mono" exportFormat="2"/>""";
            var wav = new JpexsCliService.WavFormatInfo(44100, 16, 1, 100);
            var next = JpexsCliService.PatchDomSoundItemXml(xml, wav);
            next.Should().Contain("name=\"sound1.wav\"");
            next.Should().Contain("href=\"sound1.wav\"");
            next.Should().Contain("44kHz 16bit Mono");
            next.Should().Contain("exportFormat=\"3\"");
            next.Should().Contain("compressionType=\"Raw\"");
        }

        [Fact]
        public void PatchDomSoundItemXml_StripsSoundDataHRef_SetsSampleCountFromHeader()
        {
            var xml = """<DOMSoundItem name="snd" href="sound1.flv" soundDataHRef="M 1 1.dat" sampleCount="1"/>""";
            var wav = new JpexsCliService.WavFormatInfo(22050, 16, 1, 12345);
            var next = JpexsCliService.PatchDomSoundItemXml(xml, wav);
            next.Should().NotContain("soundDataHRef");
            next.Should().Contain("name=\"sound1.wav\"");
            next.Should().Contain("href=\"sound1.wav\"");
            next.Should().Contain("sampleCount=\"12345\"");
            next.Should().Contain("22kHz 16bit Mono");
        }

        [Fact]
        public void MergeCommonIntoAs3PackagePaths_AppendsCommon()
        {
            var xml = "<flash_profiles><AS3PackagePaths>.</AS3PackagePaths></flash_profiles>";
            JpexsCliService.MergeCommonIntoAs3PackagePaths(xml)
                .Should().Contain("<AS3PackagePaths>.;common</AS3PackagePaths>");
        }

        [Fact]
        public void TryLaunchFlaCleanupJsfl_MissingScript_ReturnsFalse()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-jsfl-missing-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                JpexsCliService.TryLaunchFlaCleanupJsfl(dir).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyFlaPrettyPass_RunsSteps_DoesNotWriteToolsOrBackups()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-pretty-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "hero.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <symbols><Include href="Symbol 12.xml"/></symbols>
                          <timelines><DOMTimeline name="Scene 1"><layers>
                            <DOMLayer name="Symbol 12"><frames>
                              <DOMFrame index="0"><elements>
                                <DOMSymbolInstance libraryItemName="Hero" name="stance"/>
                              </elements></DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Hero.xml", """
                        <DOMSymbolItem name="Hero" linkageExportForAS="true" linkageClassName="hero">
                          <timeline><DOMTimeline><layers><DOMLayer name="Symbol 12"><frames>
                            <DOMFrame index="0" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Symbol 12" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 12.xml", """
                        <DOMSymbolItem name="Symbol 12">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.ApplyFlaPrettyPass(fla, dir, "hero");

                File.Exists(fla).Should().BeTrue();
                Directory.Exists(Path.Combine(dir, "tools")).Should().BeFalse();
                File.Exists(Path.Combine(dir, "FlaStepWizard.exe")).Should().BeFalse();
                Directory.GetFiles(dir, "*.bak", SearchOption.AllDirectories).Should().BeEmpty();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void HarvestExternalFlaSidecars_CopiesExternalOnly_SkipsFlaFolderAndLinkedDumps()
        {
            var src = Path.Combine(Path.GetTempPath(), "ssf2mm-flaharvest-src-" + Guid.NewGuid().ToString("N"));
            var dest = Path.Combine(Path.GetTempPath(), "ssf2mm-flaharvest-dest-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(src);
            try
            {
                File.WriteAllBytes(Path.Combine(src, "Clone_Mage.fla"), new byte[] { 1, 2, 3 });
                File.WriteAllText(Path.Combine(src, "Main.as"), """
                    package { public dynamic class Main extends SSF2Asset { } }
                    """);
                File.WriteAllText(Path.Combine(src, "CloneMageExt.as"), """
                    package { public class CloneMageExt extends Clone_Mage_SSF2CharacterExt { } }
                    """);
                File.WriteAllText(Path.Combine(src, "Clone_Mage_bm_bthrow0.as"), """
                    package { import flash.display.BitmapData;
                    public dynamic class Clone_Mage_bm_bthrow0 extends BitmapData { } }
                    """);
                Directory.CreateDirectory(Path.Combine(src, "Clone_Mage_fla"));
                File.WriteAllText(Path.Combine(src, "Clone_Mage_fla", "Grab_106.as"), "package Clone_Mage_fla { }");
                Directory.CreateDirectory(Path.Combine(src, "fl"));
                File.WriteAllText(Path.Combine(src, "fl", "motion.xml"), "<motion/>");

                var sym = new HashSet<string>(StringComparer.Ordinal)
                {
                    "Main",
                    "Clone_Mage_bm_bthrow0",
                    "Clone_Mage_fla.Grab_106"
                };
                JpexsCliService.HarvestExternalFlaSidecars(src, dest, "Clone_Mage", sym);

                File.Exists(Path.Combine(dest, "Clone_Mage.fla")).Should().BeFalse();
                File.Exists(Path.Combine(dest, "Main.as")).Should().BeTrue();
                File.Exists(Path.Combine(dest, "CloneMageExt.as")).Should().BeTrue();
                File.Exists(Path.Combine(dest, "Clone_Mage_bm_bthrow0.as")).Should().BeFalse();
                Directory.Exists(Path.Combine(dest, "Clone_Mage_fla")).Should().BeFalse();
                Directory.Exists(Path.Combine(dest, "fl")).Should().BeTrue();
                File.ReadAllText(Path.Combine(dest, "fl", "motion.xml")).Should().Be("<motion/>");
            }
            finally
            {
                try { Directory.Delete(src, true); } catch { /* ignore */ }
                try { Directory.Delete(dest, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FormatFlaExportConfig_IsDefaultSinglePass()
        {
            var config = JpexsCliService.FormatFlaExportConfig();
            config.Should().Contain("autoDeobfuscate=0");
            config.Should().Contain("openFolderAfterFlaExport=0");
            config.Should().Contain("parallelSpeedUp=1");
            config.Should().NotContain("lastExportResampleWav");
            config.Should().NotContain("lastFlaExportCompressed");
            config.Should().NotContain("exportFlaAs3DisableScriptLayer");
        }

        [Fact]
        public void FormatFlaExportConfig_KeysAreLiveInBundledFfdec()
        {
            var lib = FindBundledFfdecLibJar();
            if (lib == null)
                return;
            var live = JpexsCliService.ReadLiveFfdecConfigKeys(lib);
            live.Should().Contain("autoDeobfuscate");
            live.Should().Contain("openFolderAfterFlaExport");
            live.Should().NotContain("lastExportResampleWav",
                "that field is @ConfigurationRemoved and NPEs FFDec -config");
            JpexsCliService.EnsureFlaExportConfigKeysAreLive(lib!);
        }

        [Fact]
        public void FormatFlaExportConfig_BundledFfdecCliAcceptsKeys()
        {
            var jar = FindBundledFfdecJar();
            var java = JpexsCliService.ResolveJavaExecutable();
            if (jar == null || java == null)
                return;

            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = java,
                ArgumentList =
                {
                    "-Djava.awt.headless=true",
                    "-jar",
                    jar,
                    "-config",
                    JpexsCliService.FlaExportConfigPairs,
                    "-help"
                },
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = System.Diagnostics.Process.Start(psi)!;
            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            if (!p.WaitForExit(60000))
            {
                try { p.Kill(entireProcessTree: true); } catch { /* ignore */ }
                throw new TimeoutException("FFDec -config -help timed out.");
            }

            var combined = stdout + "\n" + stderr;
            combined.Should().NotContain("NullPointerException");
            combined.Should().NotContain("field is null");
            combined.Should().Contain("Config autoDeobfuscate set to");
            combined.Should().Contain("Config openFolderAfterFlaExport set to");
        }

        private static string? FindBundledFfdecLibJar() =>
            FindUnderFfdec("lib", "ffdec_lib.jar");

        private static string? FindBundledFfdecJar() =>
            FindUnderFfdec("ffdec.jar");

        private static string? FindUnderFfdec(params string[] parts)
        {
            var n2f = AppPaths.FfdecDirectory;
            if (n2f != null && File.Exists(Path.Combine(new[] { n2f }.Concat(parts).ToArray())))
                return Path.Combine(new[] { n2f }.Concat(parts).ToArray());
            var start = AppContext.BaseDirectory;
            for (var i = 0; i < 8; i++)
            {
                var ffdec = Path.Combine(new[] { start, "ffdec" }.Concat(parts).ToArray());
                if (File.Exists(ffdec))
                    return ffdec;
                var srcFfdec = Path.Combine(new[] { start, "src", "ffdec" }.Concat(parts).ToArray());
                if (File.Exists(srcFfdec))
                    return srcFfdec;
                var parent = Directory.GetParent(start);
                if (parent == null)
                    break;
                start = parent.FullName;
            }

            return null;
        }

        [Fact]
        public void ApplyAutoDeclareStageInstancesOff_SetsFalse()
        {
            var missing = """
                <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/" currentTimeline="1">
                </DOMDocument>
                """;
            JpexsCliService.ApplyAutoDeclareStageInstancesOff(missing)
                .Should().Contain("autoDeclareStageInsts=\"false\"");

            var alreadyTrue = """<DOMDocument autoDeclareStageInsts="true" currentTimeline="1"/>""";
            JpexsCliService.ApplyAutoDeclareStageInstancesOff(alreadyTrue)
                .Should().Contain("autoDeclareStageInsts=\"false\"");

            var alreadyFalse = """<DOMDocument autoDeclareStageInsts="false"/>""";
            JpexsCliService.ApplyAutoDeclareStageInstancesOff(alreadyFalse)
                .Should().Be(alreadyFalse);
        }

        [Fact]
        public void ApplyAnimateCompilerSettings_DisablesAutoDeclare_KeepsWarnings()
        {
            var xml = """
                <flash_profiles>
                  <AS3Coach>0</AS3Coach>
                  <AS3AutoDeclare>4096</AS3AutoDeclare>
                  <AS3Flags>4102</AS3Flags>
                </flash_profiles>
                """;
            var next = JpexsCliService.ApplyAnimateCompilerSettings(xml);
            next.Should().Contain("<AS3AutoDeclare>0</AS3AutoDeclare>");
            next.Should().Contain("<AS3Coach>4</AS3Coach>");
            next.Should().Contain("<AS3Flags>6</AS3Flags>");
            next.Should().Contain("<StreamFormat>7</StreamFormat>");
            next.Should().Contain("<EventFormat>7</EventFormat>");
            next.Should().Contain("<OverrideSounds>1</OverrideSounds>");
            next.Should().NotContain("OverrideSoundSettings");
        }

        [Fact]
        public void ApplyPublishSoundQuality_WritesNumericRawOverrideTagsAnimateStores()
        {
            var xml = """
                <flash_profiles>
                  <PublishFlashProperties enabled="true">
                    <StreamFormat>0
                </StreamFormat>
                    <StreamCompress>7
                </StreamCompress>
                    <EventFormat>0
                </EventFormat>
                    <EventCompress>7
                </EventCompress>
                    <OverrideSounds>0
                </OverrideSounds>
                  </PublishFlashProperties>
                </flash_profiles>
                """;
            var next = JpexsCliService.ApplyPublishSoundQuality(xml);
            next.Should().Contain("<StreamFormat>7\n</StreamFormat>");
            next.Should().Contain("<StreamCompress>0\n</StreamCompress>");
            next.Should().Contain("<EventFormat>7\n</EventFormat>");
            next.Should().Contain("<EventCompress>0\n</EventCompress>");
            next.Should().Contain("<OverrideSounds>1\n</OverrideSounds>");
            next.Should().NotContain("160 kbps");
            next.Should().NotContain("OverrideSoundSettings");
            next.Should().NotContain("<OverrideSounds>0");
        }

        [Fact]
        public void ApplyPublishSoundQuality_ReplacesLegacyStringTagsAndDropsFakeOverride()
        {
            var xml = "<flash_profile><StreamFormat>MP3, 16 kbps, Mono</StreamFormat><EventFormat>MP3, 16 kbps, Mono</EventFormat><OverrideSoundSettings>false</OverrideSoundSettings></flash_profile>";
            var next = JpexsCliService.ApplyPublishSoundQuality(xml);
            next.Should().Contain("<StreamFormat>7</StreamFormat>");
            next.Should().Contain("<EventFormat>7</EventFormat>");
            next.Should().Contain("<OverrideSounds>1</OverrideSounds>");
            next.Should().NotContain("16 kbps");
            next.Should().NotContain("OverrideSoundSettings");
        }

        [Fact]
        public void DisableAutoDeclareStageInstances_SetsAttributeOnDomDocument()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-autodeclare-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/" currentTimeline="1">
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "PublishSettings.xml", """
                        <flash_profiles>
                          <AS3Coach>4</AS3Coach>
                          <AS3AutoDeclare>4096</AS3AutoDeclare>
                          <AS3Flags>4102</AS3Flags>
                        </flash_profiles>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1280.xml", """
                        <DOMSymbolItem linkageClassName="Clone_Mage_fla.Idle_3" linkageExportForAS="true"/>
                        """);
                }

                JpexsCliService.DisableAutoDeclareStageInstances(fla).Should().BeTrue();
                using var read = ZipFile.OpenRead(fla);
                using var reader = new StreamReader(read.GetEntry("DOMDocument.xml")!.Open());
                reader.ReadToEnd().Should().Contain("autoDeclareStageInsts=\"false\"");
                using var pubReader = new StreamReader(read.GetEntry("PublishSettings.xml")!.Open());
                var pub = pubReader.ReadToEnd();
                pub.Should().Contain("<AS3AutoDeclare>0</AS3AutoDeclare>");
                pub.Should().Contain("<AS3Coach>4</AS3Coach>");
                pub.Should().Contain("<AS3Flags>6</AS3Flags>");
                using var idleReader = new StreamReader(read.GetEntry("LIBRARY/Symbol 1280.xml")!.Open());
                idleReader.ReadToEnd().Should().Contain("Clone_Mage_fla.Idle_3");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildActionsLayerFromFlaClass_MapsAddFrameScriptToActions()
        {
            var asText = """
                package Clone_Mage_fla {
                  public dynamic class Idle_3 extends MovieClip {
                    public function Idle_3() {
                      addFrameScript(0,this.frame1,11,this.frame12);
                    }
                    function frame1() : * {
                      this.gotoAndStop("loop");
                    }
                    function frame12() : * {
                      repeats++;
                    }
                    function helper() : * {
                      return 1;
                    }
                  }
                }
                """;
            var layer = JpexsCliService.BuildActionsLayerFromFlaClass(asText);
            layer.Should().Contain("name=\"Actions\"");
            layer.Should().Contain("index=\"0\"");
            layer.Should().Contain("index=\"11\"");
            layer.Should().Contain("gotoAndStop");
            layer.Should().Contain("function helper(");
            layer.Should().Contain("repeats++");
        }

        [Fact]
        public void BuildActionsLayerFromFlaClass_BindsHelpersOnThis()
        {
            var asText = """
                package Clone_Mage_fla {
                  public dynamic class Idle_3 extends MovieClip {
                    public function Idle_3() { addFrameScript(0,this.frame1); }
                    function frame1() : * {
                      this.restoreSpecials();
                      this.self.addEventListener(SSF2Event.STATE_CHANGE,this.uncrouch);
                    }
                    function restoreSpecials() : * { this.self.setAttackEnabled(true,"b_forward"); }
                    function uncrouch(param1:*) : * { if(param1.data.fromState == 12) this.self.stancePlayFrame("uncrouch"); }
                  }
                }
                """;
            var layer = JpexsCliService.BuildActionsLayerFromFlaClass(asText);
            layer.Should().Contain("function restoreSpecials(");
            layer.Should().Contain("function uncrouch(param1:*");
            layer.Should().Contain("if(!param1 || !param1.data) return;");
            layer.Should().Contain("this.restoreSpecials();");
            layer.Should().Contain("this.uncrouch");
            layer.Should().NotContain("this.restoreSpecials = function");
        }

        [Fact]
        public void RewriteBareHelperReferences_QualifiesBareIds()
        {
            var names = new HashSet<string>(StringComparer.Ordinal) { "xSpeedDecay", "uncrouch" };
            var rewritten = JpexsCliService.RewriteBareHelperReferences(
                "this.self.stancePlayFrame(\"uncrouch\"); self.destroyTimer(xSpeedDecay); this.self.createTimer(1,-1,this.xSpeedDecay);",
                names);
            rewritten.Should().Contain("stancePlayFrame(\"uncrouch\")");
            rewritten.Should().NotContain("stancePlayFrame(\"this.uncrouch\")");
            rewritten.Should().Contain("destroyTimer(this.xSpeedDecay)");
            rewritten.Should().Contain("createTimer(1,-1,this.xSpeedDecay)");
        }

        [Fact]
        public void BuildActionsLayerFromFlaClass_PreservesHelperParameters()
        {
            var asText = """
                package Clone_Mage_fla {
                  public dynamic class Idle_3 extends MovieClip {
                    public function Idle_3() { addFrameScript(0,this.frame1); }
                    function frame1() : * { this.restoreSpecials(); }
                    function restoreSpecials() : * {
                      this.self.setAttackEnabled(true,"b_forward");
                    }
                    function uncrouch(param1:*) : * {
                      if(param1.data.fromState == 12) this.self.stancePlayFrame("uncrouch");
                    }
                  }
                }
                """;
            var layer = JpexsCliService.BuildActionsLayerFromFlaClass(asText);
            layer.Should().Contain("function uncrouch(param1:*)");
            layer.Should().Contain("param1.data.fromState");
            layer.Should().Contain("function restoreSpecials(");
            layer.Should().NotContain("function uncrouch():");
        }

        [Fact]
        public void FixTimelineFunctionParameters_AddsMissingParamN()
        {
            var script = """
                function uncrouch():* {
                if(param1.data.fromState == 12) return;
                }
                function restoreSpecials():* {
                this.self.setAttackEnabled(true,"b_forward");
                }
                """;
            var next = JpexsCliService.FixTimelineFunctionParameters(script);
            next.Should().Contain("function uncrouch(param1:*):*");
            next.Should().Contain("function restoreSpecials():*");
        }

        [Fact]
        public void RewriteFrameScriptForTimeline_InjectsSelfBinding()
        {
            var rewritten = JpexsCliService.RewriteFrameScriptForTimeline(
                "self.destroyTimer(xSpeedDecay);");
            rewritten.Should().Contain("var self:* = this.self != null ? this.self : this;");
            rewritten.Should().Contain("self.destroyTimer(xSpeedDecay);");

            var already = JpexsCliService.RewriteFrameScriptForTimeline(
                "var self:* = this;\nself.x = 1;");
            already.Should().NotContain("this.self != null");

            JpexsCliService.RewriteFrameScriptForTimeline("this.self.playSound(\"x\");")
                .Should().Be("this.self.playSound(\"x\");");

            var second = JpexsCliService.RewriteFrameScriptForTimeline(
                "self.destroyTimer(xSpeedDecay);", declareSelf: false);
            second.Should().StartWith("self = this.self != null ? this.self : this;");
            second.Should().NotContain("var self");
        }

        [Fact]
        public void CaptureClosureTimerHelpers_DeclaresBareDestroyTimerIds()
        {
            var next = JpexsCliService.CaptureClosureTimerHelpers(
                "self.destroyTimer(xSpeedDecay);");
            next.Should().StartWith("var xSpeedDecay:* = this.xSpeedDecay;");
            next.Should().Contain("self.destroyTimer(xSpeedDecay);");

            JpexsCliService.CaptureClosureTimerHelpers("self.destroyTimer(this.xSpeedDecay);")
                .Should().Be("self.destroyTimer(this.xSpeedDecay);");
        }

        [Fact]
        public void SanitizeSymbolActionsSelfBindings_FixesPlainScriptGrabClosure()
        {
            var xml = """
                <DOMSymbolItem>
                  <DOMFrame index="16">
                    <Actionscript>
                      <script>this.self.createTimer(1,-1,this.xSpeedDecay);
                this.self.addEventListener(SSF2Event.CHAR_GRAB,function(param1:* = null):*
                {
                   self.destroyTimer(xSpeedDecay);
                });
                </script>
                    </Actionscript>
                  </DOMFrame>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.SanitizeSymbolActionsSelfBindings(xml);
            next.Should().Contain("<![CDATA[");
            next.Should().Contain("var self:* = this.self != null ? this.self : this;");
            next.Should().Contain("var xSpeedDecay:* = this.xSpeedDecay;");
            next.Should().Contain("self.destroyTimer(xSpeedDecay);");
        }

        [Fact]
        public void SanitizeSymbolActionsSelfBindings_KeepsHoistedXSpeedDecayWithoutEarlySnapshot()
        {
            var xml = """
                <DOMSymbolItem>
                  <DOMFrame index="16">
                    <Actionscript>
                      <script>function xSpeedDecay():* { this.self.x = 0; }
                this.self.createTimer(1,-1,xSpeedDecay);
                this.self.addEventListener(SSF2Event.CHAR_GRAB,function(param1:* = null):*
                {
                   self.destroyTimer(xSpeedDecay);
                });
                </script>
                    </Actionscript>
                  </DOMFrame>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.SanitizeSymbolActionsSelfBindings(xml);
            next.Should().Contain("function xSpeedDecay(");
            next.Should().NotContain("this.xSpeedDecay = function");
            var snapshotAt = next.IndexOf("var xSpeedDecay:* = this.xSpeedDecay;", StringComparison.Ordinal);
            var functionAt = next.IndexOf("function xSpeedDecay(", StringComparison.Ordinal);
            functionAt.Should().BeGreaterThanOrEqualTo(0);
            if (snapshotAt >= 0)
                snapshotAt.Should().BeGreaterThan(functionAt);
            next.Should().Contain("createTimer(1,-1,xSpeedDecay)");
            next.Should().Contain("destroyTimer(xSpeedDecay)");
        }

        [Fact]
        public void CaptureClosureTimerHelpers_SkipsSnapshotWhenFunctionExists()
        {
            var body = """
                function xSpeedDecay():* { this.self.x = 0; }
                self.createTimer(1,-1,xSpeedDecay);
                self.destroyTimer(xSpeedDecay);
                """;
            var next = JpexsCliService.CaptureClosureTimerHelpers(body);
            next.Should().NotContain("var xSpeedDecay:* = this.xSpeedDecay;");
            next.Should().Contain("function xSpeedDecay(");
        }

        [Fact]
        public void DeduplicateXmlAttributes_RemovesRepeatedLocked()
        {
            var xml = """<DOMLayer name="Layer 6" color="#29f9d9" locked="true" parentLayerIndex="0" locked="true">""";
            var next = JpexsCliService.DeduplicateXmlAttributes(xml);
            next.Should().Be("""<DOMLayer name="Layer 6" color="#29f9d9" locked="true" parentLayerIndex="0">""");
            next.Should().NotContain("locked=\"true\" locked=\"true\"");
        }

        [Fact]
        public void ApplyFlaVanillaExportPatches_SanitizesSelfAndKeepsScene1()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-vanilla-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/" currentTimeline="1" nextSceneIdentifier="2">
                          <timelines>
                            <DOMTimeline name="Scene 1">
                              <layers>
                                <DOMLayer name="Layer 1">
                                  <frames>
                                    <DOMFrame index="0">
                                      <elements>
                                        <DOMSymbolInstance libraryItemName="Symbol 1794"/>
                                      </elements>
                                    </DOMFrame>
                                  </frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1794.xml", """
                        <DOMSymbolItem>
                          <timeline>
                            <DOMTimeline>
                              <layers>
                                <DOMLayer name="Layer 6" color="#29f9d9" locked="true" parentLayerIndex="0" locked="true">
                                  <frames>
                                    <DOMFrame index="16">
                                      <Actionscript>
                                        <script>this.self.createTimer(1,-1,this.xSpeedDecay);
                        this.self.addEventListener(SSF2Event.CHAR_GRAB,function(param1:* = null):*
                        {
                           self.destroyTimer(xSpeedDecay);
                        });
                        </script>
                                      </Actionscript>
                                    </DOMFrame>
                                  </frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.ApplyFlaVanillaExportPatches(fla, "Ban_Goku");

                using var read = ZipFile.OpenRead(fla);
                using (var scene = new StreamReader(read.GetEntry("DOMDocument.xml")!.Open()))
                {
                    var dom = scene.ReadToEnd();
                    dom.Should().Contain("name=\"Scene 1\"");
                    dom.Should().Contain("libraryItemName=\"Symbol 1794\"");
                    dom.Should().Contain("as3SourcePath");
                }
                using var symbol = new StreamReader(read.GetEntry("LIBRARY/Symbol 1794.xml")!.Open());
                var xml = symbol.ReadToEnd();
                xml.Should().Contain("var self:* = this.self != null ? this.self : this;");
                xml.Should().Contain("var xSpeedDecay:* = this.xSpeedDecay;");
                xml.Should().NotContain("locked=\"true\" locked=\"true\"");
                xml.Should().Contain("locked=\"true\"");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void FindBundledFlvToWav_PrefersDownloadedTool()
        {
            var path = JpexsCliService.FindBundledFlvToWav();
            if (path == null)
                return;
            File.Exists(path).Should().BeTrue();
            Path.GetFileName(path).Should().Be("ffmpeg.exe");
            path!.Replace('\\', '/').Should().NotContain("/Windows/");
        }

        [Fact]
        public void FlaSoundDecoder_WrapsFlvPcm()
        {
            var pcm = new byte[] { 0x00, 0x10, 0x00, 0x20 };
            var flv = BuildPcmFlv(pcm, rateFlags: 2, stereo: false, bits16: true);
            FlaSoundDecoder.TryDecodeToPcm16Wav(flv, out var wav).Should().BeTrue();
            wav.Length.Should().Be(44 + pcm.Length);
            System.Text.Encoding.ASCII.GetString(wav, 0, 4).Should().Be("RIFF");
            JpexsCliService.TryReadWavFormat(wav, out var info).Should().BeTrue();
            info.IsPcm16.Should().BeTrue();
            info.SampleRate.Should().Be(22050);
        }

        private static byte[] BuildPcmFlv(byte[] pcm, int rateFlags, bool stereo, bool bits16)
        {
            var flags = (byte)((3 << 4) | ((rateFlags & 3) << 2) | ((bits16 ? 1 : 0) << 1) | (stereo ? 1 : 0));
            var dataSize = 1 + pcm.Length;
            var tag = new byte[11 + dataSize];
            tag[0] = 8;
            tag[1] = (byte)((dataSize >> 16) & 0xFF);
            tag[2] = (byte)((dataSize >> 8) & 0xFF);
            tag[3] = (byte)(dataSize & 0xFF);
            tag[11] = flags;
            Buffer.BlockCopy(pcm, 0, tag, 12, pcm.Length);
            var flv = new byte[9 + 4 + tag.Length];
            flv[0] = (byte)'F';
            flv[1] = (byte)'L';
            flv[2] = (byte)'V';
            flv[3] = 1;
            flv[4] = 4;
            flv[8] = 9;
            Buffer.BlockCopy(tag, 0, flv, 13, tag.Length);
            return flv;
        }

        [Fact]
        public void BuildActionsLayerFromFlaClass_DeclaresSelfOnce()
        {
            var asText = """
                package Clone_Mage_fla {
                  public dynamic class Grab_106 extends MovieClip {
                    public function Grab_106() { addFrameScript(0,this.frame1,16,this.frame17); }
                    function frame1() : * { this.self = SSF2API.getCharacter(this); }
                    function frame17() : * { self.destroyTimer(xSpeedDecay); }
                    function xSpeedDecay() : * { self.x = 0; }
                  }
                }
                """;
            var layer = JpexsCliService.BuildActionsLayerFromFlaClass(asText);
            var varCount = Regex.Matches(layer, @"var self:\*").Count;
            varCount.Should().Be(1);
            layer.Should().Contain("function xSpeedDecay(");
            layer.Should().Contain("self.destroyTimer(this.xSpeedDecay);");
            layer.Should().Contain("this.self = SSF2API.getCharacter(this);");
        }

        [Fact]
        public void PatchFlaDocumentClassMainAs_AddsMovieClipImport()
        {
            var patched = JpexsCliService.PatchFlaDocumentClassMainAs(
                "package\n{\n   public dynamic class Main extends SSF2Asset\n   {\n   }\n}\n");
            patched.Should().Contain("import flash.display.MovieClip;");
            patched.Should().Contain("extends SSF2Asset");
            JpexsCliService.PatchSsf2AssetExtendsMovieClip(
                    "public dynamic class SSF2Asset extends MovieClip")
                .Should().Contain("extends flash.display.MovieClip");
        }

        [Fact]
        public void StripAllTimelineFlaExportLinkage_StripsOnlyFlaPackage()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-stripall-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Idle.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_fla.Idle_3" linkageExportForAS="true"/>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_black_mage" linkageExportForAS="true"/>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Proj.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_bm_bthrow0" linkageExportForAS="true"/>
                        """);
                }

                JpexsCliService.StripAllTimelineFlaExportLinkage(fla, "Clone_Mage").Should().Be(1);

                using var read = ZipFile.OpenRead(fla);
                using var idle = new StreamReader(read.GetEntry("LIBRARY/Idle.xml")!.Open());
                idle.ReadToEnd().Should().NotContain("linkageClassName");
                using var host = new StreamReader(read.GetEntry("LIBRARY/Host.xml")!.Open());
                host.ReadToEnd().Should().Contain("Clone_Mage_black_mage");
                using var proj = new StreamReader(read.GetEntry("LIBRARY/Proj.xml")!.Open());
                proj.ReadToEnd().Should().Contain("Clone_Mage_bm_bthrow0");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void StripTimelineFlaExportLinkageFromSymbolXml_RemovesFla_KeepsMainAndBm()
        {
            var flaXml = """
                <DOMSymbolItem name="Symbol 1280" linkageClassName="Clone_Mage_fla.Idle_3" linkageExportForAS="true"/>
                """;
            var stripped = JpexsCliService.StripTimelineFlaExportLinkageFromSymbolXml(flaXml, "Clone_Mage");
            stripped.Should().NotContain("linkageClassName");
            stripped.Should().NotContain("linkageExportForAS");

            JpexsCliService.StripTimelineFlaExportLinkageFromSymbolXml(
                    """<DOMSymbolItem linkageClassName="Clone_Mage_black_mage" linkageExportForAS="true"/>""",
                    "Clone_Mage")
                .Should().Contain("Clone_Mage_black_mage");
            JpexsCliService.StripTimelineFlaExportLinkageFromSymbolXml(
                    """<DOMSymbolItem linkageClassName="Clone_Mage_bm_bthrow0" linkageExportForAS="true"/>""",
                    "Clone_Mage")
                .Should().Contain("Clone_Mage_bm_bthrow0");

            var keep = new HashSet<string>(StringComparer.Ordinal) { "Grab_106" };
            JpexsCliService.StripTimelineFlaExportLinkageFromSymbolXml(
                    """<DOMSymbolItem linkageClassName="Clone_Mage_fla.Grab_106" linkageExportForAS="true"/>""",
                    "Clone_Mage",
                    keep)
                .Should().Contain("Clone_Mage_fla.Grab_106");
        }

        [Fact]
        public void CollectRemainingFlaSidecarClassNames_ReadsFlaPackage()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-keepfla-" + Guid.NewGuid().ToString("N"));
            var pkg = Path.Combine(dir, "Clone_Mage_fla");
            Directory.CreateDirectory(pkg);
            try
            {
                File.WriteAllText(Path.Combine(pkg, "Grab_106.as"), """
                    package Clone_Mage_fla { public dynamic class Grab_106 extends MovieClip {
                      public function Grab_106() { super(); }
                      public function xSpeedDecay() : void {}
                    } }
                    """);
                var names = JpexsCliService.CollectRemainingFlaSidecarClassNames(dir, "Clone_Mage");
                names.Should().Contain("Grab_106");
                names.Should().Contain("Clone_Mage_fla.Grab_106");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void CollectStanceChildLibraryItemNames_ReadsStanceInstances()
        {
            var xml = """
                <DOMSymbolItem>
                  <elements>
                    <DOMSymbolInstance libraryItemName="Symbol 1280" name="stance"/>
                    <DOMSymbolInstance name="stance" libraryItemName="Symbol 1547"/>
                    <DOMSymbolInstance libraryItemName="Symbol 106" name="grab"/>
                  </elements>
                </DOMSymbolItem>
                """;
            var names = JpexsCliService.CollectStanceChildLibraryItemNames(xml);
            names.Should().Contain("Symbol 1280");
            names.Should().Contain("Symbol 1547");
            names.Should().NotContain("Symbol 106");
        }

        [Fact]
        public void ExtractExtraFlaMethodsForTimeline_IdleKeepsRestoreNotFrame1()
        {
            var idle = """
                package Gas_Mages_fla {
                  public dynamic class Idle_3 extends MovieClip {
                    public function Idle_3() { super(); }
                    function frame1() : * { stop(); }
                    public function restoreSpecials() : * { this.self.setAttackEnabled(true,"b_forward"); }
                    public function uncrouch(param1:* = null) : * { this.self.stancePlayFrame("uncrouch"); }
                  }
                }
                """;
            var extra = JpexsCliService.ExtractExtraFlaMethodsForTimeline(idle, "Idle_3");
            extra.Should().Contain("function restoreSpecials(");
            extra.Should().Contain("function uncrouch(");
            extra.Should().NotContain("function frame1(");
            extra.Should().NotContain("function Idle_3(");
        }

        [Fact]
        public void ExtractExtraFlaMethodsForTimeline_KeepsSetupTauntAsFunction()
        {
            var src = """
                package gallu_fla {
                  public dynamic class Symbol1843_115 extends MovieClip {
                    public function Symbol1843_115() { addFrameScript(94, this.frame95); }
                    function frame95() : * { setupTaunt(); }
                    public function setupTaunt() : * { this.self.playStance("loop"); }
                  }
                }
                """;
            var extra = JpexsCliService.ExtractExtraFlaMethodsForTimeline(src, "Symbol1843_115");
            extra.Should().Contain("function setupTaunt(");
            extra.Should().NotContain("this.setupTaunt = function");
            extra.Should().NotContain("function frame95(");
        }

        [Fact]
        public void FindLibraryClipForFlaSidecar_MatchesSetupTauntCallWithoutLinkage()
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/Symbol 1843.xml"] = Encoding.UTF8.GetBytes("""
                    <DOMSymbolItem name="Symbol 1843">
                      <DOMFrame index="94">
                        <Actionscript><script>setupTaunt();</script></Actionscript>
                      </DOMFrame>
                    </DOMSymbolItem>
                    """)
            };
            var extra = "function setupTaunt():* { this.self.playStance(\"loop\"); }\n";
            var hit = JpexsCliService.FindLibraryClipForFlaSidecar(entries, "Taunts_115", extra);
            hit.Should().Be("LIBRARY/Symbol 1843.xml");
        }

        [Fact]
        public void FindLibraryClipForFlaSidecar_MatchesCreateTimerCallbackWithoutCallParens()
        {
            var extra = """
                function jumpCancel():void {}
                function checkControls():* {}
                function checkForGoToJab2():* {}
                """;
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/Symbol 1671.xml"] = Encoding.UTF8.GetBytes("""
                    <DOMSymbolItem name="Symbol 1671">
                      <script>this.self.createTimer(1,-1,this.checkControls);</script>
                    </DOMSymbolItem>
                    """),
                ["LIBRARY/Symbol 1545.xml"] = Encoding.UTF8.GetBytes("""
                    <DOMSymbolItem name="Symbol 1545">
                      <script>this.self.createTimer(1,-1,this.checkControls);
                      this.self.createTimer(1,-1,this.checkForGoToJab2);</script>
                    </DOMSymbolItem>
                    """)
            };
            var hit = JpexsCliService.FindLibraryClipForFlaSidecar(entries, "JabCombo_25", extra);
            hit.Should().Be("LIBRARY/Symbol 1545.xml");
        }

        [Fact]
        public void RebuildTimelineFlaAnimations_InjectsActionsAndStripsLinkage()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-rebuild-" + Guid.NewGuid().ToString("N"));
            var scripts = Path.Combine(dir, "scripts", "Clone_Mage_fla");
            var fla = Path.Combine(dir, "Clone_Mage.fla");
            Directory.CreateDirectory(scripts);
            try
            {
                File.WriteAllText(Path.Combine(scripts, "Idle_3.as"), """
                    package Clone_Mage_fla {
                      public dynamic class Idle_3 extends MovieClip {
                        public function Idle_3() { addFrameScript(0,this.frame1); }
                        function frame1() : * { self.play(); }
                      }
                    }
                    """);
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Symbol 1280.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_fla.Idle_3" linkageExportForAS="true">
                          <timeline>
                            <DOMTimeline name="Symbol 1280">
                              <layers>
                                <DOMLayer name="Labels Layer">
                                  <frames>
                                    <DOMFrame index="0" keyMode="9728" name="loop" labelType="name"><elements/></DOMFrame>
                                  </frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 9.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_bm_bthrow0" linkageExportForAS="true">
                          <timeline><DOMTimeline name="x"><layers/></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.RebuildTimelineFlaAnimations(fla, "Clone_Mage", Path.Combine(dir, "scripts"))
                    .Should().Be(1);

                using var read = ZipFile.OpenRead(fla);
                using var idleReader = new StreamReader(read.GetEntry("LIBRARY/Symbol 1280.xml")!.Open());
                var idle = idleReader.ReadToEnd();
                idle.Should().Contain("name=\"Actions\"");
                idle.Should().Contain("var self:*");
                idle.Should().NotContain("linkageClassName");
                using var bmReader = new StreamReader(read.GetEntry("LIBRARY/Symbol 9.xml")!.Open());
                bmReader.ReadToEnd().Should().Contain("Clone_Mage_bm_bthrow0");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyXframeActionsLayer_AddsActionsFromFrameLabels()
        {
            var xml = """
                <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_black_mage">
                  <timeline>
                    <DOMTimeline name="Symbol 1556">
                      <layers>
                        <DOMLayer name="Labels Layer">
                          <frames>
                            <DOMFrame index="0" keyMode="9728" name="stand" labelType="name"><elements/></DOMFrame>
                            <DOMFrame index="1" keyMode="9728" name="entrance" labelType="name"><elements/></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 2">
                          <frames>
                            <DOMFrame index="0"><elements><DOMSymbolInstance name="stance"/></elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyXframeActionsLayer(xml);
            next.Should().Contain("name=\"Actions\"");
            next.Should().Contain("this.xframe = \"stand\"");
            next.Should().Contain("this.xframe = \"entrance\"");
            next.Should().Contain("stop();");
            JpexsCliService.ApplyXframeActionsLayer(next).Should().Be(next);
        }

        [Fact]
        public void InjectXframeActionsOnStanceHostFla_WritesActionsLayerIntoZip()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-xframe-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Symbol 1556.xml", """
                        <DOMSymbolItem xmlns="http://ns.adobe.com/xfl/2008/" linkageClassName="Clone_Mage_black_mage" linkageExportForAS="true">
                          <timeline>
                            <DOMTimeline name="Symbol 1556">
                              <layers>
                                <DOMLayer name="Labels Layer">
                                  <frames>
                                    <DOMFrame index="0" keyMode="9728" name="stand" labelType="name"><elements/></DOMFrame>
                                  </frames>
                                </DOMLayer>
                                <DOMLayer name="Layer 2">
                                  <frames>
                                    <DOMFrame index="0"><elements>
                                      <DOMSymbolInstance name="stance"/>
                                      <DOMSymbolInstance name="stance"/>
                                    </elements></DOMFrame>
                                  </frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.InjectXframeActionsOnStanceHostFla(fla).Should().BeTrue();
                using var read = ZipFile.OpenRead(fla);
                using var reader = new StreamReader(read.GetEntry("LIBRARY/Symbol 1556.xml")!.Open());
                var xml = reader.ReadToEnd();
                xml.Should().Contain("this.xframe = \"stand\"");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_MapsUniqueSharedAndAudio()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-orgbp-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "lol.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" duration="10" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="IdleClip" name="stance"/>
                                <DOMSymbolInstance libraryItemName="SharedFx"/>
                              </elements>
                            </DOMFrame>
                            <DOMFrame index="10" duration="10" name="walk">
                              <elements>
                                <DOMSymbolInstance libraryItemName="WalkClip" name="stance"/>
                                <DOMSymbolInstance libraryItemName="SharedFx"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/IdleClip.xml", """
                        <DOMSymbolItem name="IdleClip">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements><DOMBitmapInstance libraryItemName="Bitmap 1"/></elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/WalkClip.xml", """<DOMSymbolItem name="WalkClip"/>""");
                    WriteZipEntry(zip, "LIBRARY/SharedFx.xml", """<DOMSymbolItem name="SharedFx"/>""");
                    WriteZipEntry(zip, "LIBRARY/Bitmap 1.xml", """<DOMBitmapItem name="Bitmap 1"/>""");
                    WriteZipEntry(zip, "LIBRARY/Hit.xml", """<DOMSoundItem name="hit"/>""");
                    WriteZipEntry(zip, "LIBRARY/Leftover.xml", """
                        <DOMSymbolItem name="Leftover">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements><DOMBitmapInstance libraryItemName="Bitmap 9"/></elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Bitmap 9.xml", """<DOMBitmapItem name="Bitmap 9"/>""");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Audio\thit");
                tsv.Should().Contain("Animations/Idle\tIdleClip\tIdle");
                tsv.Should().Contain("Animations/Walk\tWalkClip\tWalk");
                tsv.Should().Contain("Animations/Idle/Movieclips\tSharedFx");
                tsv.Should().NotContain("Shared Animations/SharedFx");
                tsv.Should().Contain("Animations/Idle/Sprites\tBitmap 1");
                tsv.Should().NotContain("Animations/Idle/Bitmaps");
                tsv.Should().Contain("Shared Animations/Leftover\tLeftover");
                tsv.Should().Contain("Shared Animations/Leftover/Sprites\tBitmap 9");
                tsv.Should().NotContain("Shared Animations/Bitmaps\tBitmap 9");
                tsv.Should().Contain("\tHost\tLol");
                tsv.Should().NotContain("Animations/Idle/Movieclips\tIdleClip");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_Stage_KeepsClipsUnderAnimations()
        {
            JpexsCliService.IsStageDataSwf("stage/battlefield.swf").Should().BeTrue();
            JpexsCliService.IsStageDataSwf("character/mario.swf").Should().BeFalse();

            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-orgstage-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "battlefield.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" duration="10" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="IdleClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/IdleClip.xml", """<DOMSymbolItem name="IdleClip"/>""");
                    WriteZipEntry(zip, "LIBRARY/Leftover.xml", """
                        <DOMSymbolItem name="Leftover">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements><DOMBitmapInstance libraryItemName="Bitmap 9"/></elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Bitmap 9.xml", """<DOMBitmapItem name="Bitmap 9"/>""");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla, stageLibrary: true);
                tsv.Should().NotContain("Shared Animations");
                tsv.Should().Contain("Animations/Idle\tIdleClip\tIdle");
                tsv.Should().Contain("Animations/Leftover\tLeftover");
                tsv.Should().Contain("Animations/Leftover/Sprites\tBitmap 9");
                tsv.Should().Contain("Animations/Hero\tHost\tHero");
                tsv.Should().NotContain("\tHost\tLol");
                tsv.Should().NotContain("\tHost\tBattlefield");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void OrganizeRowIsNoOp_WhenAlreadyAtDestPath()
        {
            JpexsCliService.OrganizeRowIsNoOp("Audio", "Audio/Hit", "Hit").Should().BeTrue();
            JpexsCliService.OrganizeRowIsNoOp("Audio", "sound46.wav", "Hit").Should().BeFalse();
            JpexsCliService.OrganizeRowIsNoOp("Animations/Idle", "IdleClip", "Idle").Should().BeFalse();
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_OmitsRowsAlreadyAtDest()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-orgnop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "hero.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero"/>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Audio/hit.xml", """
                        <DOMSoundItem name="Audio/hit"/>
                        """);
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().NotContain("Audio\tAudio/hit");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void SmartLibraryName_StripsIdentAndSplitsCamelCase()
        {
            JpexsCliService.SmartLibraryName("br_mage_bm_FS_Error", "br_mage").Should().Be("FS Error");
            JpexsCliService.SmartLibraryName("br_mage_FinalFantasyVictory", "br_mage").Should().Be("Final Fantasy Victory");
            JpexsCliService.SmartLibraryName("br_mage_BM_Laugh", "br_mage").Should().Be("BM Laugh");
            JpexsCliService.SmartLibraryName("br_mage_BMflood", "br_mage").Should().Be("BM flood");
            JpexsCliService.SmartLibraryName("br_mage_landLight", "br_mage").Should().Be("land Light");
            JpexsCliService.FriendlyCharacterLibraryName("lol").Should().Be("Lol");
            JpexsCliService.FriendlyCharacterLibraryName("bmi").Should().Be("Bmi");
            JpexsCliService.FriendlyCharacterLibraryName("best mage").Should().Be("Best Mage");
            JpexsCliService.FriendlyCharacterLibraryName("back_mage").Should().Be("Back Mage");
            JpexsCliService.FriendlyCharacterLibraryName("cloney.fla").Should().Be("Cloney");
            JpexsCliService.FriendlyCharacterLibraryName(@"C:\pack\cloney.fla.bak").Should().Be("Cloney");
            JpexsCliService.CharacterLinkageIdent("best mage").Should().Be("best_mage");
            JpexsCliService.FontLibraryName("""<DOMFontItem name="Font 1558" font="SegoeUI"/>""")
                .Should().Be("Segoe UI");
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_ReadsDomDocumentBitmaps_PlacesWithParentGraphic()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-dombit-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "br_mage.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="entrance">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Symbol 1291" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1291.xml", """
                        <DOMSymbolItem name="Symbol 1291">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Symbol 1286"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1286.xml", """
                        <DOMSymbolItem name="Symbol 1286" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <BitmapFill bitmapPath="Bitmap 1285.png"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <media>
                            <DOMBitmapItem name="Bitmap 1285.png" linkageExportForAS="true" linkageClassName="br_mage_bm_entranceGlow"/>
                            <DOMSoundItem name="sound20.wav" linkageExportForAS="true" linkageClassName="br_mage_bm_FS_Error"/>
                            <DOMFontItem name="Font 1558" font="SegoeUI"/>
                          </media>
                        </DOMDocument>
                        """);
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Animations/Entrance\tSymbol 1291\tEntrance");
                tsv.Should().Contain("Animations/Entrance/Graphics\tSymbol 1286");
                tsv.Should().Contain("Animations/Entrance/Sprites\tBitmap 1285.png\tentrance Glow");
                tsv.Should().Contain("Audio\tsound20.wav\tFS Error");
                tsv.Should().Contain("Fonts\tFont 1558\tSegoe UI");
                tsv.Should().Contain("\tHost\tBr Mage");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_StanceOnTwoLabels_GoesToFirstPoseFolder()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-stand-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero">
                          <timeline><DOMTimeline><layers>
                            <DOMLayer name="labels"><frames>
                              <DOMFrame index="0" name="stand"/>
                              <DOMFrame index="77" name="edgelean"/>
                            </frames></DOMLayer>
                            <DOMLayer name="stance"><frames>
                              <DOMFrame index="0">
                                <elements>
                                  <DOMSymbolInstance libraryItemName="Symbol 1280" name="stance"/>
                                </elements>
                              </DOMFrame>
                              <DOMFrame index="77">
                                <elements>
                                  <DOMSymbolInstance libraryItemName="Symbol 1280" name="stance"/>
                                </elements>
                              </DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1280.xml", """<DOMSymbolItem name="Symbol 1280"/>""");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Animations/Stand\tSymbol 1280\tStand");
                tsv.Should().NotContain("Shared Animations/Symbol 1280");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_BitmapOnThrowClip_GoesToPoseSprites()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-uthrow-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "best_mage.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Symbol 1556" linkageExportForAS="true" linkageClassName="best_mage_black_mage">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="throw_up">
                              <elements>
                                <DOMSymbolInstance libraryItemName="ThrowClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/ThrowClip.xml", """
                        <DOMSymbolItem name="ThrowClip">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMBitmapInstance libraryItemName="Bitmap 73.png"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <media>
                            <DOMBitmapItem name="Bitmap 73.png" linkageExportForAS="true" linkageClassName="best_mage_uthrow0"/>
                          </media>
                        </DOMDocument>
                        """);
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Animations/Throw_up/Sprites\tBitmap 73.png\tuthrow0");
                tsv.Should().NotContain("Shared Animations/Bitmaps");
                tsv.Should().NotContain("Shared Animations/Sprites\tBitmap 73.png");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_UtiltBitmap_NestsUnderExistingUpTilt()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-utilt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "cloney.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="cloney">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="A_up_tilt">
                              <elements>
                                <DOMSymbolInstance libraryItemName="TiltClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/TiltClip.xml", """
                        <DOMSymbolItem name="TiltClip">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMBitmapInstance libraryItemName="Bitmap 126.png"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <media>
                            <DOMBitmapItem name="Bitmap 126.png" href="Bitmap 126.png" linkageExportForAS="true" linkageClassName="cloney_utilt1_8"/>
                          </media>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Bitmap 126.png", "png");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Animations/A_up_tilt/Sprites\tBitmap 126.png");
                tsv.Should().NotContain("Shared Animations/Sprites\tBitmap 126.png");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PatchHostSymbolOpeningTag_SetsDisplayNameAndIdentLinkage()
        {
            var xml = """<DOMSymbolItem name="Symbol 1556" linkageExportForAS="true" linkageClassName="best_mage_black_mage">""";
            var next = JpexsCliService.PatchHostSymbolOpeningTag(xml, "Bmi", "bmi");
            next.Should().Contain("name=\"Bmi\"");
            next.Should().Contain("linkageClassName=\"bmi\"");
            next.Should().NotContain("best_mage_black_mage");
        }

        [Fact]
        public void RewriteLibraryItemReferences_UpdatesHrefAndInstances()
        {
            var xml = """<Include href="Symbol 1556.xml"/><DOMSymbolInstance libraryItemName="Symbol 1556"/>""";
            var next = JpexsCliService.RewriteLibraryItemReferences(xml, "Symbol 1556", "Bmi");
            next.Should().Contain("href=\"Bmi.xml\"");
            next.Should().Contain("libraryItemName=\"Bmi\"");
            next.Should().NotContain("Symbol 1556");
        }

        [Fact]
        public void PatchFlaHostSymbol_RenamesZipEntryAndReferences()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-host-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <symbols><Include href="Symbol 1556.xml"/></symbols>
                          <timelines><DOMTimeline name="Scene 1"><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Symbol 1556" name="stance"/>
                                <DOMSymbolInstance libraryItemName="Symbol 1556" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 1556.xml", """
                        <DOMSymbolItem name="Symbol 1556" linkageExportForAS="true" linkageClassName="bmi_black_mage">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="IdleClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/IdleClip.xml", """<DOMSymbolItem name="IdleClip"/>""");
                }

                JpexsCliService.PatchFlaHostSymbol(fla, "bmi").Should().BeTrue();
                using (var read = ZipFile.OpenRead(fla))
                {
                    read.GetEntry("LIBRARY/Bmi.xml").Should().NotBeNull();
                    read.GetEntry("LIBRARY/Symbol 1556.xml").Should().BeNull();
                    using (var reader = new StreamReader(read.GetEntry("LIBRARY/Bmi.xml")!.Open()))
                    {
                        var host = reader.ReadToEnd();
                        host.Should().Contain("name=\"Bmi\"");
                        host.Should().Contain("linkageClassName=\"bmi\"");
                    }
                    using (var reader = new StreamReader(read.GetEntry("DOMDocument.xml")!.Open()))
                    {
                        var dom = reader.ReadToEnd();
                        dom.Should().Contain("href=\"Bmi.xml\"");
                        dom.Should().Contain("libraryItemName=\"Bmi\"");
                        dom.Should().NotContain("Symbol 1556");
                    }
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("\tBmi");
                tsv.Should().NotContain("\tSymbol 1556\t");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_MergesHitBoxAndLocks()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Layer 1">
                          <frames>
                            <DOMFrame index="0"><elements>
                              <DOMSymbolInstance name="hitbox" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 2">
                          <frames>
                            <DOMFrame index="0"><elements>
                              <DOMSymbolInstance name="hitbox2" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Goku", isHost: false);
            Regex.Matches(next, "<DOMLayer\\b").Count.Should().Be(1);
            next.Should().Contain("name=\"HitBox\"");
            next.Should().Contain("locked=\"true\"");
            next.Should().Contain("outline=\"true\"");
            Regex.Matches(next, "name=\"hitbox").Count.Should().Be(2);
        }

        [Fact]
        public void MergeBoxLayersInXml_MergesHitBoxFamily_DoesNotLock()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Gaku">
                          <frames><DOMFrame index="0" duration="40"><elements>
                            <DOMSymbolInstance libraryItemName="body"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 3">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="hitBox2">
                          <frames><DOMFrame index="12"><elements>
                            <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.MergeBoxLayersInXml(xml, out var timelines, out var removed);
            timelines.Should().Be(1);
            removed.Should().Be(1);
            Regex.Matches(next, "<DOMLayer\\b").Count.Should().Be(2);
            next.Should().Contain("name=\"HitBox\"");
            next.Should().Contain("index=\"12\"");
            next.Should().NotContain("locked=");
        }

        [Fact]
        public void MergeBoxLayersInXml_MergesDocumentScene1()
        {
            var xml = """
                <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/">
                  <timelines>
                    <DOMTimeline name="Scene 1">
                      <layers>
                        <DOMLayer name="hitBox">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="hitBox 2">
                          <frames><DOMFrame index="4"><elements>
                            <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timelines>
                </DOMDocument>
                """;
            var next = JpexsCliService.MergeBoxLayersInXml(xml, out var timelines, out var removed);
            timelines.Should().Be(1);
            removed.Should().Be(1);
            Regex.Matches(next, "<DOMLayer\\b").Count.Should().Be(1);
            next.Should().Contain("name=\"HitBox\"");
            next.Should().Contain("index=\"4\"");
        }

        [Fact]
        public void MergeFlaBoxLayers_RewritesZip_AndLeavesBak()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-merge-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <timelines><DOMTimeline name="Scene 1"><layers>
                            <DOMLayer name="hitBox">
                              <frames><DOMFrame index="0"><elements>
                                <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                              </elements></DOMFrame></frames>
                            </DOMLayer>
                            <DOMLayer name="Layer 7">
                              <frames><DOMFrame index="3"><elements>
                                <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                              </elements></DOMFrame></frames>
                            </DOMLayer>
                          </layers></DOMTimeline></timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/idle.xml", """
                        <DOMSymbolItem name="idle">
                          <timeline><DOMTimeline name="idle"><layers>
                            <DOMLayer name="art">
                              <frames><DOMFrame index="0"><elements>
                                <DOMSymbolInstance libraryItemName="body"/>
                              </elements></DOMFrame></frames>
                            </DOMLayer>
                          </layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                var result = JpexsCliService.MergeFlaBoxLayers(fla);
                result.Ok.Should().BeTrue(result.Message);
                result.LayersRemoved.Should().Be(1);
                File.Exists(fla + ".bak").Should().BeTrue();
                using var zip2 = ZipFile.OpenRead(fla);
                using var reader = new StreamReader(zip2.GetEntry("DOMDocument.xml")!.Open());
                var doc = reader.ReadToEnd();
                Regex.Matches(doc, "<DOMLayer\\b").Count.Should().Be(1);
                doc.Should().Contain("name=\"HitBox\"");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
                try { File.Delete(fla + ".bak"); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_DoesNotAppendFramesPastDestEnd()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Gaku">
                          <frames>
                            <DOMFrame index="0" duration="50"><elements>
                              <DOMSymbolInstance libraryItemName="body"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 1">
                          <frames>
                            <DOMFrame index="0" duration="50"><elements>
                              <DOMSymbolInstance name="hitbox" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 2">
                          <frames>
                            <DOMFrame index="0" duration="50"><elements>
                              <DOMSymbolInstance name="hitbox2" libraryItemName="hb"/>
                            </elements></DOMFrame>
                            <DOMFrame index="50" duration="6"><elements>
                              <DOMSymbolInstance name="hitbox3" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            next.Should().NotContain("index=\"50\"");
            next.Should().Contain("name=\"HitBox\"");
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_OverlaysSameFrameIndex()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Gaku">
                          <frames>
                            <DOMFrame index="0" duration="50"><elements>
                              <DOMSymbolInstance libraryItemName="body"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 1">
                          <frames>
                            <DOMFrame index="0" duration="50"><elements>
                              <DOMSymbolInstance name="hitbox" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                        <DOMLayer name="Layer 2">
                          <frames>
                            <DOMFrame index="20"><elements>
                              <DOMSymbolInstance name="hitbox2" libraryItemName="hb"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            next.Should().Contain("index=\"20\"");
            next.Should().Contain("name=\"hitbox2\"");
            next.Should().Contain("name=\"hitbox\"");
            var hitAt20 = next.IndexOf("index=\"20\"", StringComparison.Ordinal);
            var hitbox2 = next.IndexOf("name=\"hitbox2\"", StringComparison.Ordinal);
            var laterHitbox = next.LastIndexOf("name=\"hitbox\"", StringComparison.Ordinal);
            hitAt20.Should().BeGreaterThan(0);
            hitbox2.Should().BeGreaterThan(hitAt20);
            laterHitbox.Should().BeGreaterThan(hitbox2);
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_DocumentKeepsScene1()
        {
            var xml = """
                <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/" currentTimeline="1" nextSceneIdentifier="2">
                  <timelines>
                    <DOMTimeline name="Scene 2">
                      <layers>
                        <DOMLayer name="Layer 1">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                    <DOMTimeline name="Scene 1">
                      <layers>
                        <DOMLayer name="stance">
                          <frames>
                            <DOMFrame index="0"><elements>
                              <DOMSymbolInstance name="stance" libraryItemName="Gaku"/>
                            </elements></DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timelines>
                </DOMDocument>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            Regex.Matches(next, "<DOMTimeline\\b").Count.Should().Be(1);
            next.Should().Contain("name=\"Scene 1\"");
            next.Should().NotContain("name=\"Scene 2\"");
            next.Should().Contain("name=\"stance\"");
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_MergesNumberedHitBoxLayerNames()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Gaku">
                          <frames><DOMFrame index="0" duration="40"><elements>
                            <DOMSymbolInstance libraryItemName="body"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="hitBox2">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="hitBox 2">
                          <frames><DOMFrame index="12"><elements>
                            <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="hitBox2 2">
                          <frames><DOMFrame index="20"><elements>
                            <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            Regex.Matches(next, "<DOMLayer\\b").Count.Should().Be(2);
            next.Should().Contain("name=\"HitBox\"");
            Regex.Matches(next, "<DOMLayer[^>]*name=\"hitBox").Count.Should().Be(0);
            next.Should().Contain("index=\"12\"");
            next.Should().Contain("index=\"20\"");
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_GroundFolderAndDisplayNameOnTop()
        {
            var xml = """
                <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/">
                  <timelines>
                    <DOMTimeline name="Scene 1">
                      <layers>
                        <DOMLayer name="Actions">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="Gaku">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance name="stance" libraryItemName="Gaku"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="fla.ground Ref mc 2">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance libraryItemName="fla.ground Ref mc 2"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="fla.ground Ref mc 2 2">
                          <frames><DOMFrame index="0"><elements>
                            <DOMSymbolInstance libraryItemName="fla.ground Ref mc 2"/>
                          </elements></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timelines>
                </DOMDocument>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            var ground = next.IndexOf("name=\"Ground\"", StringComparison.Ordinal);
            var ref1 = next.IndexOf("name=\"reference 1\"", StringComparison.Ordinal);
            ground.Should().BeGreaterThan(0);
            ref1.Should().BeGreaterThan(ground);
            var gaku = next.IndexOf("name=\"Gaku\"", StringComparison.Ordinal);
            var actions = next.IndexOf("name=\"Actions\"", StringComparison.Ordinal);
            gaku.Should().BeGreaterThan(0);
            gaku.Should().BeLessThan(actions);
        }

        [Fact]
        public void ApplyCloneFlaPass_ImTiredFla1_IfPresent_AuditHasNoUnresolvedSprites()
        {
            var src = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SSF2 Mod Manager", "SSF2 Shell", "build", "data", "character", "expansions", "Im Tired", "FLA", "FLA1", "Im_Tired.fla"));
            if (!File.Exists(src))
                return;
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-imtired-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "Im_Tired.fla");
            try
            {
                File.Copy(src, fla, overwrite: true);
                JpexsCliService.ApplyCloneFlaPass(fla, dir, "Im_Tired");
                var issues = JpexsCliService.AuditFlaExport(fla);
                issues.Where(i => i.Contains("/Sprites/", StringComparison.OrdinalIgnoreCase))
                    .Should().BeEmpty();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyCloneFlaPass_StockFla_OverlaysBoxesPrunesEmptyAndKeepsScene1()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-clonepass-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "hero.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/" currentTimeline="1">
                          <media>
                            <DOMBitmapItem name="Animations/Stand/Sprites/idle0" href="idle0.png"/>
                          </media>
                          <timelines>
                            <DOMTimeline name="Scene 2">
                              <layers>
                                <DOMLayer name="Layer 1">
                                  <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                            <DOMTimeline name="Scene 1">
                              <layers>
                                <DOMLayer name="stance">
                                  <frames><DOMFrame index="0"><elements>
                                    <DOMSymbolInstance name="stance" libraryItemName="Idle"/>
                                  </elements></DOMFrame></frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Idle.xml", """
                        <DOMSymbolItem name="Idle" xmlns="http://ns.adobe.com/xfl/2008/">
                          <timeline>
                            <DOMTimeline name="Idle">
                              <layers>
                                <DOMLayer name="Gaku">
                                  <frames>
                                    <DOMFrame index="0" duration="50"><elements>
                                      <DOMSymbolInstance libraryItemName="body"/>
                                      <DOMBitmapInstance libraryItemName="Animations/Stand/Sprites/idle0"/>
                                    </elements></DOMFrame>
                                  </frames>
                                </DOMLayer>
                                <DOMLayer name="Layer 1">
                                  <frames>
                                    <DOMFrame index="0" duration="50"><elements>
                                      <DOMSymbolInstance name="hitbox" libraryItemName="hb"/>
                                    </elements></DOMFrame>
                                  </frames>
                                </DOMLayer>
                                <DOMLayer name="Layer 2">
                                  <frames>
                                    <DOMFrame index="20"><elements>
                                      <DOMSymbolInstance name="hitbox2" libraryItemName="hb"/>
                                    </elements></DOMFrame>
                                    <DOMFrame index="50"><elements>
                                      <DOMSymbolInstance name="hitbox3" libraryItemName="empty"/>
                                    </elements></DOMFrame>
                                  </frames>
                                </DOMLayer>
                              </layers>
                            </DOMTimeline>
                          </timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/empty.xml", """
                        <DOMSymbolItem name="empty" xmlns="http://ns.adobe.com/xfl/2008/">
                          <timeline><DOMTimeline name="empty"><layers>
                            <DOMLayer name="Layer 1"><frames>
                              <DOMFrame index="0"><elements>
                                <DOMShape><fills/><edges/></DOMShape>
                              </elements></DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/body.xml", """
                        <DOMSymbolItem name="body" xmlns="http://ns.adobe.com/xfl/2008/">
                          <timeline><DOMTimeline name="body"><layers>
                            <DOMLayer name="Layer 1"><frames>
                              <DOMFrame index="0"><elements>
                                <DOMShape><fills/><edges><Edge edges="! 0 0| 20 0"/></edges></DOMShape>
                              </elements></DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/hb.xml", """
                        <DOMSymbolItem name="hb" xmlns="http://ns.adobe.com/xfl/2008/">
                          <timeline><DOMTimeline name="hb"><layers>
                            <DOMLayer name="Layer 1"><frames>
                              <DOMFrame index="0"><elements>
                                <DOMShape><fills/><edges><Edge edges="! 0 0| 10 0"/></edges></DOMShape>
                              </elements></DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.ApplyCloneFlaPass(fla, dir, "hero").Should().BeTrue();
                JpexsCliService.AuditFlaExport(fla).Should().BeEmpty();
                using var read = ZipFile.OpenRead(fla);
                var idle = ReadZipEntry(read, "LIBRARY/Idle.xml");
                idle.Should().Contain("name=\"HitBox\"");
                idle.Should().Contain("outline=\"true\"");
                idle.Should().Contain("index=\"20\"");
                idle.Should().NotContain("index=\"50\"");
                idle.Should().Contain("idle0");
                read.GetEntry("LIBRARY/empty.xml").Should().BeNull();
                var dom = ReadZipEntry(read, "DOMDocument.xml");
                Regex.Matches(dom, "<DOMTimeline\\b").Count.Should().Be(1);
                dom.Should().Contain("name=\"Scene 1\"");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_HostKeepsLabelsAndScripts()
        {
            var xml = """
                <DOMSymbolItem name="gaku" linkageClassName="gaku" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="gaku">
                      <layers>
                        <DOMLayer name="Labels Layer">
                          <frames><DOMFrame index="0" name="idle" labelType="name"><elements/></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="Script Layer">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            JpexsCliService.IsHostSymbolXml(xml, "gaku", "Gaku").Should().BeTrue();
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: true);
            next.Should().Contain("name=\"Labels Layer\"");
            next.Should().Contain("name=\"Scripts\"");
            next.Should().NotContain("name=\"Actions\"");
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_NestedKeepsLabelsOffActions()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Labels Layer">
                          <frames><DOMFrame index="0" name="idle" labelType="name"><elements/></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="Script Layer">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            next.Should().Contain("name=\"Labels Layer\"");
            next.Should().Contain("name=\"Actions\"");
            next.Should().NotContain("name=\"Script Layer\"");
        }

        [Fact]
        public void ApplyFlaTimelineLayerPolicy_WrapsEffectsFolder()
        {
            var xml = """
                <DOMSymbolItem name="idle" xmlns="http://ns.adobe.com/xfl/2008/">
                  <timeline>
                    <DOMTimeline name="idle">
                      <layers>
                        <DOMLayer name="Effects 1">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                        <DOMLayer name="Effects 2">
                          <frames><DOMFrame index="0"><elements/></DOMFrame></frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = JpexsCliService.ApplyFlaTimelineLayerPolicy(xml, "Gaku", isHost: false);
            next.Should().Contain("name=\"Effects\"");
            next.Should().Contain("layerType=\"folder\"");
            next.Should().Contain("parentLayerIndex=");
        }

        [Fact]
        public void ApplyFlaOrganizeNamePrefixes_SetsFolderLeafWithoutMovingKeys()
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/IdleClip.xml"] = Encoding.UTF8.GetBytes("""<DOMSymbolItem name="IdleClip"/>"""),
                ["DOMDocument.xml"] = Encoding.UTF8.GetBytes("""<DOMDocument><Include href="IdleClip.xml" libraryItemName="IdleClip"/></DOMDocument>""")
            };
            var n = JpexsCliService.ApplyFlaOrganizeNamePrefixes(entries, "Animations/Idle\tIdleClip\tIdle");
            n.Should().BeGreaterThan(0);
            Encoding.UTF8.GetString(entries["LIBRARY/IdleClip.xml"]).Should().Contain("name=\"Animations/Idle/Idle\"");
            entries.Keys.Should().Contain("LIBRARY/IdleClip.xml");
            entries.Keys.Should().NotContain("LIBRARY/Animations/Idle/Idle.xml");

            JpexsCliService.ApplyFlaOrganizeNamePrefixes(entries, "Animations/Idle\tIdleClip\tIdle");
            Encoding.UTF8.GetString(entries["LIBRARY/IdleClip.xml"]).Should().Contain("name=\"Animations/Idle/Idle\"");
            Encoding.UTF8.GetString(entries["LIBRARY/IdleClip.xml"]).Should().NotContain("name=\"Animations/Idle/Animations/Idle/Idle\"");
        }

        [Fact]
        public void ApplyOrganizeXml_SetsLibraryItemName_TimelineUsesDestLeaf()
        {
            var xml = """
                <DOMSymbolItem name="Symbol 1545" itemID="1">
                  <timeline>
                    <DOMTimeline name="Symbol 1545">
                      <DOMLayer name="Layer 1">
                        <DOMSymbolInstance libraryItemName="Symbol 1545"/>
                      </DOMLayer>
                    </DOMTimeline>
                  </timeline>
                </DOMSymbolItem>
                """;
            var next = FlaCleanupSteps.ApplyOrganizeXml(xml, "Animations/A\tSymbol 1545\tA");
            next.Should().Contain("name=\"Animations/A/A\"");
            next.Should().Contain("<DOMTimeline name=\"A\"");
            next.Should().Contain("libraryItemName=\"Animations/A/A\"");
            next.Should().NotContain("<DOMTimeline name=\"Animations/A/A\"");
            next.Should().NotContain("<DOMTimeline name=\"Symbol 1545\"");
        }

        [Fact]
        public void InjectLibraryFolders_InsertsFolderItemsBeforeSymbols()
        {
            var xml = """<DOMDocument><media/><symbols><Include href="Symbol 1545.xml"/></symbols></DOMDocument>""";
            var next = FlaCleanupSteps.InjectLibraryFolders(xml, new[] { "Animations/A/A", "Audio/hit.wav" });
            next.Should().Contain("<folders>");
            next.Should().Contain("<DOMFolderItem name=\"Animations\"");
            next.Should().Contain("<DOMFolderItem name=\"Animations/A\"");
            next.Should().Contain("<DOMFolderItem name=\"Audio\"");
            next.Should().Contain("isExpanded=\"false\"");
            next.Should().NotContain("isExpanded=\"true\"");
            next.Should().Contain("</folders>");
            next.Should().NotContain("<DOMFolderItem name=\"Animations/A/A\"");
        }

        [Fact]
        public void KeepMediaExtension_AppendsPngWhenDestHasNone()
        {
            FlaCleanupSteps.KeepMediaExtension("Bitmap 1318.png", "dtilt 10")
                .Should().Be("dtilt 10.png");
            FlaCleanupSteps.KeepMediaExtension("sound1.wav", "shout kk")
                .Should().Be("shout kk.wav");
            FlaCleanupSteps.KeepMediaExtension("Bitmap 1.png", "idle.png")
                .Should().Be("idle.png");
            var bmp = FlaCleanupSteps.ApplyOrganizeXml(
                """<DOMBitmapItem name="Bitmap 1318.png" href="Bitmap 1318.png"/>""",
                "Animations/A/Sprites\tBitmap 1318.png\tdtilt 10");
            bmp.Should().Contain("name=\"Animations/A/Sprites/dtilt 10.png\"");
            bmp.Should().Contain("href=\"Animations/A/Sprites/dtilt 10.png\"");
        }

        [Fact]
        public void ApplyOrganizeXml_FoldersSoundName_KeepsHrefOnOriginalWav()
        {
            var xml = """<DOMSoundItem name="sound1.wav" href="sound1.wav"/>""";
            var next = FlaCleanupSteps.ApplyOrganizeXml(
                xml, "Audio\tsound1.wav\tcharge yell\nAnimations/A\tSymbol 1545\tA");
            next.Should().Contain("name=\"Audio/charge yell.wav\"");
            next.Should().Contain("href=\"sound1.wav\"");
            next.Should().NotContain("href=\"Audio/charge yell.wav\"");
        }

        [Fact]
        public void ApplyOrganizeXml_RewritesBitmapPathToMovedFile()
        {
            var xml = """<DOMShape><BitmapFill bitmapPath="Bitmap 1318.png"/></DOMShape>""";
            var next = FlaCleanupSteps.ApplyOrganizeXml(xml, "Animations/A/Sprites\tBitmap 1318.png\tdtilt 10");
            next.Should().Contain("bitmapPath=\"Animations/A/Sprites/dtilt 10.png\"");
            next.Should().NotContain("bitmapPath=\"Bitmap 1318.png\"");
        }

        [Fact]
        public void RelocateLibraryFiles_DoesNotMoveAudio()
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/sound75.wav"] = "wav"u8.ToArray(),
                ["LIBRARY/sound75.flv"] = "flv"u8.ToArray()
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["sound75.wav"] = "Audio/charge yell.wav",
                ["sound75"] = "Audio/charge yell.wav"
            };
            FlaCleanupSteps.RelocateLibraryFiles(entries, map);
            entries.Keys.Should().Contain("LIBRARY/sound75.wav");
            entries.Keys.Should().Contain("LIBRARY/sound75.flv");
            entries.Keys.Should().NotContain("LIBRARY/Audio/charge yell.wav");
        }

        [Fact]
        public void RenameLayers_ArtWithStrayItemBox_StaysDisplayName()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash0.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 2"><frames>
                    <DOMFrame index="0" duration="45"><elements/></DOMFrame>
                    <DOMFrame index="45">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="2">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash1.png"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="4">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash2.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "cloney");
            ((string?)layers.Elements("DOMLayer").Last().Attribute("name")).Should().Be("cloney");
            ((string?)layers.Elements("DOMLayer").First().Attribute("name")).Should().Be("cloney 2");
            ((string?)layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .Attribute("name")).Should().Be("Item");
        }

        [Fact]
        public void RenameLayers_FirstNamedInstance_BecomesLayerName()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 4"><frames>
                    <DOMFrame index="8">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 248"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="127">
                      <elements>
                        <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "cloney");
            ((string?)layers.Elements("DOMLayer").First().Attribute("name")).Should().Be("aurabase");
            ((string?)layers.Elements("DOMLayer").Last().Attribute("name")).Should().Be("cloney");
        }

        [Fact]
        public void RenameLayers_MixedFxAndHitBox_KeepsFxLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                    </elements></DOMFrame>
                    <DOMFrame index="127"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="pose.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "cloney");
            ((string?)layers.Elements("DOMLayer").First().Attribute("name")).Should().Be("Layer 6");
            FlaCleanupSteps.PeelStrayBoxes(layers, "cloney");
            layers.Elements("DOMLayer").Should().Contain(l => (string?)l.Attribute("name") == "HitBox");
            var fx = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Layer 6");
            fx.ToString().Should().Contain("Symbol 1573");
            fx.ToString().Should().NotContain("hitBox2");
        }

        [Fact]
        public void DropStackedUnnamedLeftovers_KeepsUniqueLibOnSameFramesAsAurabase()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="127"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0" duration="200"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("Layer 6", "aurabase");
            layers.ToString().Should().Contain("Symbol 1573");
        }

        [Fact]
        public void DropStackedUnnamedLeftovers_RemovesTrueDuplicateLib()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="127"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1613"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0" duration="200"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1613"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("aurabase");
            layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1613"))
                .Should().Be(1);
        }

        [Fact]
        public void DropStackedUnnamedLeftovers_KeepsSameLibAtDifferentPosition()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="35"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1548">
                        <matrix><Matrix tx="59.95" ty="53.1"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 4"><frames>
                    <DOMFrame index="35"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1548">
                        <matrix><Matrix tx="59.0" ty="56.4"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1548"))
                .Should().Be(2);
        }

        [Fact]
        public void DropStackedUnnamedLeftovers_KeepsFxOnUncoveredFrames()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="10"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("Layer 6", "aurabase");
            layers.ToString().Should().Contain("Symbol 1573");
        }

        [Fact]
        public void DropStackedUnnamedLeftovers_KeepsInformationStaticTextOnDuplicateLibLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 149"/>
                      <DOMStaticText>
                        <textRuns>
                          <DOMTextRun>
                            <characters>INFORMATION</characters>
                          </DOMTextRun>
                        </textRuns>
                      </DOMStaticText>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Clone Sora"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 149"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            layers.ToString().Should().Contain("INFORMATION");
            layers.Descendants("DOMStaticText").Should().NotBeEmpty();
        }

        [Fact]
        public void NamesAndMerge_KeepLimitStaticTextOnHostWithUnnamedGraphics()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 8"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 163"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMStaticText>
                        <textRuns>
                          <DOMTextRun>
                            <characters>Limit</characters>
                          </DOMTextRun>
                        </textRuns>
                      </DOMStaticText>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 160"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "Clone Sora");
            FlaCleanupSteps.PeelStrayBoxes(layers, "Clone Sora");
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "Clone Sora");
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.ToString().Should().Contain("Limit");
            layers.Descendants("DOMStaticText").Should().NotBeEmpty();
        }

        [Fact]
        public void ComplementaryMergeAdjacent_DoesNotDropInformationTextOnlyLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 149"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 2"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMStaticText>
                        <textRuns>
                          <DOMTextRun>
                            <characters>INFORMATION</characters>
                          </DOMTextRun>
                        </textRuns>
                      </DOMStaticText>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "Clone Sora");
            layers.ToString().Should().Contain("INFORMATION");
            layers.Descendants("DOMStaticText").Should().NotBeEmpty();
        }

        [Fact]
        public void PeelStrayBoxes_MovesHitBoxOffFxLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="8">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 248"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="295" duration="3">
                      <elements>
                        <DOMSymbolInstance name="hitBox3" libraryItemName="Symbol 353"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.PeelStrayBoxes(layers, "cloney");
            var fx = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "aurabase");
            fx.ToString().Should().NotContain("hitBox3");
            fx.ToString().Should().Contain("Symbol 248");
            layers.Elements("DOMLayer").Where(l => (string?)l.Attribute("name") == "HitBox")
                .SelectMany(l => l.Descendants("DOMSymbolInstance"))
                .Should().Contain(e => (string?)e.Attribute("name") == "hitBox3");
            FlaCleanupSteps.Outline(layers, "cloney");
            ((string?)fx.Attribute("outline")).Should().BeNull();
        }

        [Fact]
        public void PeelStrayBoxes_MovesItemBoxOffArtOntoItemLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.PeelStrayBoxes(layers, "cloney");
            var art = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "cloney");
            art.ToString().Should().Contain("pose.png");
            art.ToString().Should().NotContain("itemBox");
            var item = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item");
            item.ToString().Should().Contain("itemBox");
        }

        [Fact]
        public void ComplementaryMergeAdjacent_MergesDisjointFxLayers()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 5"><frames>
                    <DOMFrame index="0" duration="208"><elements/></DOMFrame>
                    <DOMFrame index="208">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 1628"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="209">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 1629"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0" duration="208">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 248"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="208" duration="5"><elements/></DOMFrame>
                    <DOMFrame index="213">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 1633"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "cloney");
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("aurabase", "cloney");
            var fx = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "aurabase");
            fx.ToString().Should().Contain("Symbol 1628");
            fx.ToString().Should().Contain("Symbol 1629");
            fx.ToString().Should().Contain("Symbol 248");
            fx.ToString().Should().Contain("Symbol 1633");
        }

        [Fact]
        public void ComplementaryMergeAdjacent_MergesNonOverlappingAttackBoxes()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="AttackBox"><frames>
                    <DOMFrame index="0" duration="5"><elements>
                      <DOMSymbolInstance name="attackBox" libraryItemName="ab"/>
                    </elements></DOMFrame>
                    <DOMFrame index="5" duration="5"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="AttackBox"><frames>
                    <DOMFrame index="0" duration="6"><elements/></DOMFrame>
                    <DOMFrame index="6"><elements>
                      <DOMSymbolInstance name="attackBox2" libraryItemName="ab2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="pose.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "cloney");
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("AttackBox", "cloney");
            var atk = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "AttackBox");
            atk.ToString().Should().Contain("attackBox");
            atk.ToString().Should().Contain("attackBox2");
        }

        [Fact]
        public void CollapseLibraryFolders_SetsExpandedFalse()
        {
            var xml = """<DOMDocument><folders><DOMFolderItem name="Animations" isExpanded="true"/></folders></DOMDocument>""";
            FlaCleanupSteps.CollapseLibraryFolders(xml).Should().Contain("isExpanded=\"false\"");
            FlaCleanupSteps.CollapseLibraryFolders(xml).Should().NotContain("isExpanded=\"true\"");
        }

        [Fact]
        public void ComplementaryMergeAdjacent_SkipsOverlappingAndBoxArt()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="spark"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance libraryItemName="fxA"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="flash"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance libraryItemName="fxB"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "cloney");
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("HitBox", "spark", "flash", "cloney");
        }

        [Fact]
        public void TrimTrailingEmptyFrames_DropsTailKeepsMiddleEmpty()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"><frames>
                    <DOMFrame index="0" duration="86"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0" duration="80">
                      <elements>
                        <DOMSymbolInstance name="stance" libraryItemName="Stand"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="80"><elements/></DOMFrame>
                    <DOMFrame index="85">
                      <elements>
                        <DOMSymbolInstance name="stance" libraryItemName="Tech_roll"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="86" duration="3"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0" duration="86">
                      <elements>
                        <DOMSymbolInstance libraryItemName="Symbol 1501"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="86" duration="3"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.TrimTrailingEmptyFrames(layers);
            var cloney = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "cloney");
            FrameIndexes(cloney).Should().Equal(0, 80, 85);
            LayerEnd(cloney).Should().Be(86);
            cloney.ToString().Should().Contain("Tech_roll");
            var gfx = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Layer 1");
            FrameIndexes(gfx).Should().Equal(0);
            LayerEnd(gfx).Should().Be(86);
            FrameIndexes(layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Actions"))
                .Should().Equal(0);
        }

        [Fact]
        public void TrimTrailingEmptyFrames_KeepsLabelAndSoundOnTail()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="pose.png"/>
                      </elements>
                    </DOMFrame>
                    <DOMFrame index="10" duration="3" name="end" labelType="name"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.TrimTrailingEmptyFrames(layers);
            FrameIndexes(layers.Elements("DOMLayer").First()).Should().Equal(0, 10);
            LayerEnd(layers.Elements("DOMLayer").First()).Should().Be(13);
        }

        [Fact]
        public void RelocateBoxLayerContents_MovesBitmapsOntoFilledArtKey_LeavesStrayClips()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                        <DOMSymbolInstance libraryItemName="spark"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash2.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash0.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "cloney");
            var item = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item");
            item.ToString().Should().NotContain("itmsmash2.png");
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            hit.ToString().Should().Contain("name=\"hitBox\"");
            hit.ToString().Should().Contain("libraryItemName=\"spark\"");
            layers.ToString().Should().Contain("itmsmash2.png");
            layers.Elements("DOMLayer").Should().NotContain(l => (string?)l.Attribute("name") == "Effect 1");
        }

        [Fact]
        public void RelocateBoxLayerContents_PeelsBitmapOffItemWhenArtKeyMissing()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="45">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash2.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash0.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "cloney");
            layers.Elements("DOMLayer").Should().NotContain(l => (string?)l.Attribute("name") == "Effect 1");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .ToString().Should().NotContain("itmsmash2.png");
            layers.ToString().Should().Contain("itmsmash2.png");
        }

        [Fact]
        public void RelocateBoxLayerContents_PeelsChargeShapeOffItemLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="42">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                        <DOMShape isFloating="true">
                          <fills>
                            <FillStyle index="1">
                              <BitmapFill bitmapPath="Bitmap 1544.png"/>
                            </FillStyle>
                          </fills>
                        </DOMShape>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="FalseSora"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="sf0.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "FalseSora");
            var item = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item");
            item.ToString().Should().Contain("itemBox");
            item.ToString().Should().NotContain("Bitmap 1544");
            layers.ToString().Should().Contain("Bitmap 1544");
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Contain(n => n != null && n.StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void RelocateAndPeel_PreserveColorOnBitmapInstance()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="4">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                        <DOMBitmapInstance libraryItemName="sf0.png">
                          <color>
                            <Color redMultiplier="0.69921875" greenMultiplier="0.69921875" blueMultiplier="0.69921875" redOffset="77" greenOffset="77"/>
                          </color>
                        </DOMBitmapInstance>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="FalseSora"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMBitmapInstance libraryItemName="sf1.png"/>
                        <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "FalseSora");
            FlaCleanupSteps.PeelStrayBoxes(layers, "FalseSora");

            var item = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item");
            item.Descendants("DOMBitmapInstance").Should().BeEmpty();
            item.ToString().Should().Contain("itemBox");

            var colored = layers.Descendants("DOMBitmapInstance")
                .First(e => (string?)e.Attribute("libraryItemName") == "sf0.png");
            colored.ToString().Should().Contain("redMultiplier");
            colored.ToString().Should().Contain("redOffset=\"77\"");

            var art = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "FalseSora");
            art.ToString().Should().Contain("sf1.png");
            art.ToString().Should().NotContain("itemBox");
        }

        [Fact]
        public void RenameLayers_NamesBitmapBodyLayerAfterCharacter()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="sf0.png"/>
                    </elements></DOMFrame>
                    <DOMFrame index="1"><elements>
                      <DOMBitmapInstance libraryItemName="sf1.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                      <DOMBitmapInstance libraryItemName="sf25.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "FalseSora");
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names[0].Should().Be("FalseSora");
            names[1].Should().Be("FalseSora 2");
            names.Should().NotContain("Layer 3");
        }

        [Fact]
        public void NamesAndOutlines_ForwardSmash_SplitsArtBoxesAndChargeFills()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="sf0.png"/>
                    </elements></DOMFrame>
                    <DOMFrame index="41"><elements>
                      <DOMBitmapInstance libraryItemName="sf17.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="42"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                      <DOMShape isFloating="true">
                        <fills>
                          <FillStyle index="1">
                            <BitmapFill bitmapPath="Bitmap 1544.png"/>
                          </FillStyle>
                        </fills>
                      </DOMShape>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                    </elements></DOMFrame>
                    <DOMFrame index="42"><elements>
                      <DOMBitmapInstance libraryItemName="sf25.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RenameLayers(layers, "FalseSora");
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "FalseSora");
            FlaCleanupSteps.PeelStrayBoxes(layers, "FalseSora");
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "FalseSora");
            FlaCleanupSteps.MergeBoxLayers(layers);
            FlaCleanupSteps.Outline(layers, "FalseSora");

            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().NotContain("Layer 3");
            names.Should().Contain("FalseSora");
            names.Should().Contain("FalseSora 2");
            names.Should().Contain("Item");
            names.Count(n => n != null && n.StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterThanOrEqualTo(2);

            var body = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "FalseSora");
            body.ToString().Should().Contain("sf0.png");
            body.ToString().Should().NotContain("itemBox");
            ((string?)body.Attribute("outline")).Should().BeNull();

            var item = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item");
            item.Descendants("DOMSymbolInstance")
                .Should().OnlyContain(e => (string?)e.Attribute("name") == "itemBox");
            item.Descendants("DOMShape").Should().BeEmpty();
            item.Descendants("DOMBitmapInstance").Should().BeEmpty();
            ((string?)item.Attribute("outline")).Should().BeNull();

            layers.ToString().Should().Contain("Bitmap 1544");
            layers.ToString().Should().Contain("sf25.png");
            foreach (var art in layers.Elements("DOMLayer")
                         .Where(l => ((string?)l.Attribute("name") ?? "")
                             .StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase)))
            {
                art.Descendants("DOMSymbolInstance")
                    .Should().NotContain(e =>
                        ((string?)e.Attribute("name") ?? "").StartsWith("itemBox", StringComparison.OrdinalIgnoreCase));
            }
        }

        [Fact]
        public void NamesAndOutlines_FalseSoraFlaForwardSmash_IfPresent()
        {
            var fla = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..",
                "SSF2 Mod Manager", "SSF2 Shell", "expansions", "character", "FalseSora", "FLA", "FalseSora.fla"));
            if (!File.Exists(fla))
                return;
            using var zip = ZipFile.OpenRead(fla);
            var entry = zip.GetEntry("LIBRARY/Animations/A_forwardsmash/A_forwardsmash.xml");
            entry.Should().NotBeNull();
            using var stream = entry!.Open();
            var doc = XDocument.Load(stream);
            var layers = doc.Descendants().First(e => e.Name.LocalName == "layers");
            FlaCleanupSteps.RenameLayers(layers, "FalseSora");
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "FalseSora");
            FlaCleanupSteps.PeelStrayBoxes(layers, "FalseSora");
            FlaCleanupSteps.ComplementaryMergeAdjacent(layers, "FalseSora");
            FlaCleanupSteps.MergeBoxLayers(layers);
            FlaCleanupSteps.Outline(layers, "FalseSora");

            var names = layers.Elements().Where(e => e.Name.LocalName == "DOMLayer")
                .Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().NotContain("Layer 3");
            names.Should().Contain("FalseSora");
            names.Should().Contain("FalseSora 2");
            var body = layers.Elements().First(l => (string?)l.Attribute("name") == "FalseSora");
            body.ToString().Should().Contain("sf0.png");
            layers.ToString().Should().Contain("sf25.png");
            layers.Elements().Where(l => l.Name.LocalName == "DOMLayer" &&
                    ((string?)l.Attribute("name") ?? "")
                        .StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase))
                .SelectMany(l => l.Descendants().Where(e => e.Name.LocalName == "DOMShape"))
                .Should().NotBeEmpty();
            names.Count(n => n != null && n.StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase))
                .Should().BeGreaterThanOrEqualTo(2);

            var item = layers.Elements().First(l =>
                l.Name.LocalName == "DOMLayer" &&
                string.Equals((string?)l.Attribute("name"), "Item", StringComparison.OrdinalIgnoreCase));
            item.Descendants().Where(e => e.Name.LocalName == "DOMShape").Should().BeEmpty();
            item.Descendants().Where(e => e.Name.LocalName == "DOMBitmapInstance").Should().BeEmpty();
            item.ToString().Should().Contain("itemBox");

            layers.ToString().Should().Contain("Bitmap 1544");
            foreach (var art in layers.Elements().Where(l =>
                         l.Name.LocalName == "DOMLayer" &&
                         ((string?)l.Attribute("name") ?? "")
                             .StartsWith("FalseSora", StringComparison.OrdinalIgnoreCase)))
            {
                art.Descendants().Where(e => e.Name.LocalName == "DOMSymbolInstance")
                    .Should().NotContain(e =>
                        ((string?)e.Attribute("name") ?? "")
                            .StartsWith("itemBox", StringComparison.OrdinalIgnoreCase),
                        "art layer {0} should not keep item boxes", (string?)art.Attribute("name"));
            }
        }

        [Fact]
        public void RelocateBoxLayerContents_LeavesStrayClipOnHitBox()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance libraryItemName="spark"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="charge spark effects"><frames>
                    <DOMFrame index="4"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "cloney");
            layers.Elements("DOMLayer").Should().NotContain(l => (string?)l.Attribute("name") == "Effect 1");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox")
                .ToString().Should().Contain("libraryItemName=\"spark\"");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "charge spark effects")
                .ToString().Should().NotContain("libraryItemName=\"spark\"");
        }

        [Fact]
        public void RelocateBoxLayerContents_DoesNotFillEmptyArtKey()
        {
            var layers = ItemSmashLikeLayers();
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "cloney");

            var cloney = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "cloney");
            FrameIndexes(cloney).Should().OnlyHaveUniqueItems();
            LayerEnd(cloney).Should().Be(66);
            BitmapAt(cloney, 45).Should().BeEmpty();
            BitmapAt(cloney, 51).Should().Equal("itmsmash7.png");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .Descendants("DOMBitmapInstance").Should().BeEmpty();
            layers.ToString().Should().Contain("itmsmash2.png");
        }

        [Fact]
        public void RelocateBoxLayerContents_SplitsFilledArtHold()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="45">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash2.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0" duration="50">
                      <elements>
                        <DOMBitmapInstance libraryItemName="itmsmash0.png"/>
                      </elements>
                    </DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.RelocateBoxLayerContents(layers, "cloney");
            var cloney = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "cloney");
            FrameIndexes(cloney).Should().Equal(0);
            BitmapAt(cloney, 0).Should().Equal("itmsmash0.png");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .Descendants("DOMBitmapInstance").Should().BeEmpty();
            layers.ToString().Should().Contain("itmsmash2.png");
        }

        [Fact]
        public void RelocateBoxLayerContents_PreservesRenameOnlyLayerOrderAndSpan()
        {
            var before = ItemSmashLikeLayers();
            var after = XElement.Parse(before.ToString());
            FlaCleanupSteps.RelocateBoxLayerContents(after, "cloney");

            foreach (var layer in after.Elements("DOMLayer"))
            {
                FrameIndexes(layer).Should().OnlyHaveUniqueItems(
                    "layer {0} must not get stacked keyframes", (string?)layer.Attribute("name"));
            }
            after.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .Descendants("DOMBitmapInstance").Should().BeEmpty();
            after.Descendants("DOMBitmapInstance").Count()
                .Should().Be(before.Descendants("DOMBitmapInstance").Count());
            after.Descendants("DOMSymbolInstance").Count()
                .Should().Be(before.Descendants("DOMSymbolInstance").Count());
            after.Descendants("DOMSymbolInstance")
                .Where(e => ((string?)e.Attribute("name") ?? "").StartsWith("itemBox", StringComparison.OrdinalIgnoreCase))
                .Should().OnlyContain(e =>
                    ((string?)e.Ancestors().First(a => a.Name.LocalName == "DOMLayer").Attribute("name")) == "Item");
        }

        [Fact]
        public void Outline_OnlyBoxLayers_NotItemOrArtOrEffects()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"/>
                  <DOMLayer name="HitBox"/>
                  <DOMLayer name="HurtBox"/>
                  <DOMLayer name="AttackBox"/>
                  <DOMLayer name="Item"/>
                  <DOMLayer name="charge spark effects"/>
                  <DOMLayer name="cloney"/>
                </layers>
                """);
            FlaCleanupSteps.Outline(layers, "cloney");
            Attr(layers, "HitBox", "outline").Should().Be("true");
            Attr(layers, "HitBox", "useOutlineView").Should().Be("true");
            Attr(layers, "HurtBox", "outline").Should().Be("true");
            Attr(layers, "AttackBox", "outline").Should().Be("true");
            Attr(layers, "Item", "outline").Should().BeNull();
            Attr(layers, "Item", "useOutlineView").Should().BeNull();
            Attr(layers, "charge spark effects", "outline").Should().BeNull();
            Attr(layers, "cloney", "outline").Should().BeNull();
            Attr(layers, "Actions", "outline").Should().BeNull();
        }

        [Fact]
        public void Outline_OutlinesHandInstanceLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 12"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hand" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"/>
                </layers>
                """);
            FlaCleanupSteps.Outline(layers, "cloney");
            Attr(layers, "Layer 12", "outline").Should().Be("true");
            Attr(layers, "Layer 12", "useOutlineView").Should().Be("true");
        }

        [Fact]
        public void Outline_BoxesByInstanceName_SkipsItemBox()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 12"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 7"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="ib"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.Outline(layers, "cloney");
            Attr(layers, "Layer 12", "outline").Should().Be("true");
            Attr(layers, "Layer 7", "outline").Should().BeNull();
        }

        [Fact]
        public void FoldersAndLock_Scene1_KeepsOriginalLayerStack()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Cloney"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="nspec charge 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="nspec charge 1"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "cloney", isDoc: true);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("cloney", "nspec charge 1", "Layer 1");
            layers.ToString().Should().Contain("Cloney");
            layers.ToString().Should().Contain("nspec charge 1");
        }

        [Fact]
        public void FoldersAndLock_StageSkipsGroundFolder()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"/>
                  <DOMLayer name="bg"/>
                  <DOMLayer name="Layer 2"/>
                </layers>
                """);
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "TestingFields", isDoc: false, characterPretty: false);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("Actions", "bg", "Layer 2");
            names.Should().NotContain("Ground");
            names.Should().NotContain("Effects");
        }

        [Fact]
        public void FoldersAndLock_Host_GroundFolderOutlinesLayersBelowArt()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"><frames>
                    <DOMFrame index="0" name="stand"><elements>
                      <Actionscript><script>stop();</script></Actionscript>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="stance" libraryItemName="Stand"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 5"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1501"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 3"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1501"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "cloney", isDoc: false);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("Labels Layer", "Actions", "cloney", "Ground", "Layer 5", "Layer 3");
            Attr(layers, "Ground", "layerType").Should().Be("folder");
            Attr(layers, "Ground", "open").Should().Be("false");
            Attr(layers, "Layer 5", "parentLayerIndex").Should().Be("3");
            Attr(layers, "Layer 5", "outline").Should().Be("true");
            Attr(layers, "Layer 3", "outline").Should().Be("true");
            Attr(layers, "cloney", "locked").Should().BeNull();
            Attr(layers, "Layer 5", "locked").Should().Be("true");
            layers.ToString().Should().Contain("name=\"stand\"");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Actions")
                .ToString().Should().NotContain("name=\"stand\"");
        }

        [Fact]
        public void FoldersAndLock_AttackClip_SplitsLabelsOffActions()
        {
            var doc = XElement.Parse("""
                <DOMSymbolItem name="A_forwardsmash">
                  <timeline><DOMTimeline name="A_forwardsmash">
                    <layers>
                      <DOMLayer name="Actions"><frames>
                        <DOMFrame index="4" name="hit" duration="2"><elements>
                          <Actionscript><script>stop();</script></Actionscript>
                        </elements></DOMFrame>
                      </frames></DOMLayer>
                      <DOMLayer name="FalseSora"><frames>
                        <DOMFrame index="0"><elements>
                          <DOMBitmapInstance libraryItemName="sf0.png"/>
                        </elements></DOMFrame>
                      </frames></DOMLayer>
                    </layers>
                  </DOMTimeline></timeline>
                </DOMSymbolItem>
                """);
            var layers = doc.Descendants("layers").Single();
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "FalseSora", isDoc: false);

            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names[0].Should().Be("Labels Layer");
            names[1].Should().Be("Actions");
            var labels = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Labels Layer");
            labels.ToString().Should().Contain("name=\"hit\"");
            var actions = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Actions");
            actions.ToString().Should().Contain("stop()");
            actions.ToString().Should().NotContain("name=\"hit\"");
            var hit = labels.Descendants("DOMFrame").First(f => (string?)f.Attribute("name") == "hit");
            ((string?)hit.Attribute("index")).Should().Be("4");
            labels.Descendants("DOMFrame").Should().Contain(f =>
                (string?)f.Attribute("index") == "0" && f.Attribute("name") == null);
        }

        [Fact]
        public void FoldersAndLock_TwoLabels_KeepTheirFrameIndexes()
        {
            var layers = XElement.Parse("""
                <DOMSymbolItem name="A_jab">
                  <timeline><DOMTimeline name="A_jab"><layers>
                    <DOMLayer name="Actions"><frames>
                      <DOMFrame index="0" name="start" labelType="name"><elements/></DOMFrame>
                      <DOMFrame index="1" duration="19"><elements/></DOMFrame>
                      <DOMFrame index="20" name="hit" labelType="name"><elements>
                        <Actionscript><script>stop();</script></Actionscript>
                      </elements></DOMFrame>
                      <DOMFrame index="21" duration="9"><elements/></DOMFrame>
                    </frames></DOMLayer>
                  </layers></DOMTimeline></timeline>
                </DOMSymbolItem>
                """).Descendants("layers").Single();
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "Kirbolion", isDoc: false);

            var labels = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Labels Layer");
            var frames = labels.Elements("frames").Elements("DOMFrame").ToList();
            frames.Select(f => (string?)f.Attribute("index")).Should().Equal("0", "20");
            ((string?)frames[0].Attribute("name")).Should().Be("start");
            ((string?)frames[0].Attribute("duration")).Should().Be("20");
            ((string?)frames[1].Attribute("name")).Should().Be("hit");
            ((string?)frames[1].Attribute("duration")).Should().Be("10");
            var actions = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Actions");
            actions.ToString().Should().NotContain("name=\"start\"");
            actions.ToString().Should().NotContain("name=\"hit\"");
            actions.ToString().Should().NotContain("labelType");
            actions.ToString().Should().Contain("stop()");
        }

        [Fact]
        public void FoldersAndLock_Scene1_UnlocksEveryLayer_SplitsLabels()
        {
            var doc = XElement.Parse("""
                <DOMDocument name="Scene 1">
                  <timelines><DOMTimeline name="Scene 1">
                    <layers>
                      <DOMLayer name="Actions" locked="true"><frames>
                        <DOMFrame index="0" name="go"><elements>
                          <Actionscript><script>stop();</script></Actionscript>
                        </elements></DOMFrame>
                      </frames></DOMLayer>
                      <DOMLayer name="Layer 1" locked="true"><frames>
                        <DOMFrame index="0"><elements>
                          <DOMSymbolInstance libraryItemName="Stand"/>
                        </elements></DOMFrame>
                      </frames></DOMLayer>
                      <DOMLayer name="mask" locked="true" layerType="mask"/>
                    </layers>
                  </DOMTimeline></timelines>
                </DOMDocument>
                """);
            var layers = doc.Descendants("layers").Single();
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "poli", isDoc: true);

            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("Labels Layer", "Actions", "Layer 1", "mask");
            layers.Descendants("DOMLayer").Should().OnlyContain(l => l.Attribute("locked") == null);
            layers.ToString().Should().NotContain("Ground");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Labels Layer")
                .ToString().Should().Contain("name=\"go\"");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Actions")
                .ToString().Should().NotContain("name=\"go\"");
        }

        [Fact]
        public void FoldersAndLock_NamedClipWithStance_DoesNotOutlineVfxBelowArt()
        {
            var item = XElement.Parse("""
                <DOMSymbolItem name="Revival">
                  <timeline><DOMTimeline name="Revival"><layers>
                    <DOMLayer name="cloney"><frames>
                      <DOMFrame index="0"><elements>
                        <DOMSymbolInstance name="stance" libraryItemName="Stand"/>
                      </elements></DOMFrame>
                    </frames></DOMLayer>
                    <DOMLayer name="Layer 1"><frames>
                      <DOMFrame index="0"><elements>
                        <DOMSymbolInstance libraryItemName="Symbol 1499"/>
                      </elements></DOMFrame>
                    </frames></DOMLayer>
                  </layers></DOMTimeline></timeline>
                </DOMSymbolItem>
                """);
            var layers = item.Descendants().First(e => e.Name.LocalName == "layers");
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "cloney", isDoc: false);
            Attr(layers, "Layer 1", "outline").Should().BeNull();
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().NotContain("Ground");
        }

        [Fact]
        public void Outline_DoesNotOutlineArtLayerWithStrayBoxInstance()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="touchBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.Outline(layers, "cloney");
            Attr(layers, "cloney", "outline").Should().BeNull();
        }

        [Fact]
        public void FoldersAndLock_Clip_LeavesLeftoverLayersInPlace()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"/>
                  <DOMLayer name="HitBox"/>
                  <DOMLayer name="Layer 6"/>
                  <DOMLayer name="spark effects"/>
                  <DOMLayer name="Layer 4"/>
                  <DOMLayer name="cloney"/>
                  <DOMLayer name="Item"/>
                  <DOMLayer name="ground"/>
                </layers>
                """);
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "cloney", isDoc: false);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Contain("Layer 6");
            names.Should().Contain("Layer 4");
            names.Should().NotContain("Effect 1");
            Array.IndexOf(names, "Layer 6").Should().BeLessThan(Array.IndexOf(names, "HitBox") + 10);
            Array.IndexOf(names, "Layer 6").Should().BeLessThan(Array.IndexOf(names, "Layer 4"));
            names.Should().Contain("Effects");
            names.Should().Contain("spark effects");
            names.Should().Contain("Ground");
            names.Should().Contain("reference 1");
            Attr(layers, "cloney", "locked").Should().BeNull();
            var list = layers.Elements("DOMLayer").ToList();
            var effects = list.FindIndex(l => (string?)l.Attribute("name") == "Effects");
            var spark = list.FindIndex(l => (string?)l.Attribute("name") == "spark effects");
            spark.Should().Be(effects + 1);
            var ground = list.FindIndex(l => (string?)l.Attribute("name") == "Ground");
            var reference = list.FindIndex(l => (string?)l.Attribute("name") == "reference 1");
            reference.Should().Be(ground + 1);
        }

        [Fact]
        public void MergeBoxLayers_DoesNotMergeSameFamilyAcrossInterveningLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb1"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="art"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="idle.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("HitBox", "art", "HitBox");
            layers.Elements("DOMLayer").First().ToString().Should().Contain("hb1");
            layers.Elements("DOMLayer").Last().ToString().Should().Contain("hb2");
        }

        [Fact]
        public void MergeBoxLayers_MergesAdjacentSameFamilyOnly()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb1"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="4"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="art"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="idle.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("HitBox", "art");
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            hit.ToString().Should().Contain("hb1");
            hit.ToString().Should().Contain("hb2");
        }

        [Fact]
        public void MergeBoxLayers_MergesHitBoxAndItem_OutlinesHitBoxOnly()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Actions"><frames>
                    <DOMFrame index="0" duration="13"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="13"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="5"><elements/></DOMFrame>
                    <DOMFrame index="5" duration="8"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="0" duration="13"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="ib"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="0" duration="8"><elements/></DOMFrame>
                    <DOMFrame index="8" duration="5"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="ib2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0" duration="13"><elements>
                      <DOMBitmapInstance libraryItemName="idle.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Equal("Actions", "HitBox", "Item", "cloney");
            Attr(layers, "HitBox", "outline").Should().Be("true");
            Attr(layers, "HitBox", "useOutlineView").Should().Be("true");
            Attr(layers, "Item", "outline").Should().BeNull();
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox")
                .ToString().Should().Contain("hb2");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "Item")
                .ToString().Should().Contain("ib2");
        }

        [Fact]
        public void MergeBoxLayers_LeavesFxOffHitBox()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="boxA"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="164"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="boxB"/>
                      <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0" duration="200"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            hit.ToString().Should().Contain("boxA");
            hit.ToString().Should().Contain("boxB");
            hit.ToString().Should().NotContain("Symbol 1573");
            layers.ToString().Should().Contain("Symbol 1573");
            layers.Elements("DOMLayer").Should().NotContain(l =>
                ((string?)l.Attribute("name") ?? "").Equals("Layer", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void NamesPeelDrop_PreservesUniqueOverlayFxCount()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 6"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="hb"/>
                    </elements></DOMFrame>
                    <DOMFrame index="127"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                    </elements></DOMFrame>
                    <DOMFrame index="128"><elements>
                      <DOMSymbolInstance libraryItemName="Symbol 1575"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 4"><frames>
                    <DOMFrame index="0" duration="200"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="Layer 1"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMBitmapInstance libraryItemName="pose.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            var before = layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1573"));
            before.Should().Be(1);
            FlaCleanupSteps.RenameLayers(layers, "cloney");
            FlaCleanupSteps.PeelStrayBoxes(layers, "cloney");
            FlaCleanupSteps.DropStackedUnnamedLeftovers(layers);
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1573"))
                .Should().Be(1, "unique overlay FX must not be deleted");
            layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1575"))
                .Should().Be(1);
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            hit.ToString().Should().NotContain("Symbol 1573");
        }

        [Fact]
        public void MergeBoxLayers_DoesNotStretchFilledHoldIntoEmptyFrames()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="10" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox4" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="23.0" ty="67.5"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="10" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox4" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="23.0" ty="67.5"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                    <DOMFrame index="12" duration="20"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            HitBoxAt(layers, 15).Should().BeEmpty("hitBox4 hold 10-12 must not be stretched to frame 16");
        }

        [Fact]
        public void MergeBoxLayers_DoesNotShortenSourceHoldOntoShorterDestKey()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="3"><elements>
                      <DOMSymbolInstance name="hitBox4" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="30.5" ty="64.5"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="3" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox4" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="30.5" ty="64.5"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            HitBoxAt(layers, 3).Should().NotBeEmpty();
            HitBoxAt(layers, 4).Select(e => (string?)e.Attribute("name")).Should().Equal("hitBox4");
        }

        [Fact]
        public void MergeBoxLayers_DoesNotCopyBoxFromLongEmptyDestTail()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="298"><elements/></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="10" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox4" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="23.0" ty="67.5"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="15" duration="3"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="41.5" ty="41.0"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            HitBoxAt(layers, 15).Select(e => (string?)e.Attribute("name")).Should().Equal("hitBox");
        }

        private static List<XElement> HitBoxAt(XElement layers, int frame) =>
            LayerAt(layers, "HitBox", frame);

        private static List<XElement> LayerAt(XElement layers, string layerName, int frame)
        {
            var layer = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == layerName);
            return layer.Descendants("DOMFrame").Where(f =>
            {
                var i = int.Parse((string?)f.Attribute("index") ?? "0");
                var d = int.TryParse((string?)f.Attribute("duration"), out var n) && n > 0 ? n : 1;
                return i <= frame && i + d > frame;
            }).SelectMany(f => f.Descendants("DOMSymbolInstance")).ToList();
        }

        [Fact]
        public void MergeBoxLayers_KeepsSameNamedBoxesAtDifferentPositions()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="5"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="10" ty="20"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="5"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="Symbol 353">
                        <matrix><Matrix tx="40" ty="50"/></matrix>
                      </DOMSymbolInstance>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.Descendants("DOMSymbolInstance")
                .Count(e => (string?)e.Attribute("name") == "hitBox2")
                .Should().Be(2);
        }

        [Fact]
        public void MergeBoxLayers_PutsStrayAttackBoxesOnAttackBoxLayer()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="attackBox" libraryItemName="ab"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("HitBox", "AttackBox");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox")
                .ToString().Should().NotContain("attackBox");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "AttackBox")
                .ToString().Should().Contain("attackBox");
            Attr(layers, "AttackBox", "outline").Should().Be("true");
            Attr(layers, "AttackBox", "useOutlineView").Should().Be("true");
        }

        [Fact]
        public void MergeBoxLayers_PadsAttackBoxLeadAndTailToClipLength()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="6"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                    <DOMFrame index="6" duration="3"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                      <DOMSymbolInstance name="attackBox" libraryItemName="ab"/>
                    </elements></DOMFrame>
                    <DOMFrame index="9" duration="7"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0" duration="16"><elements>
                      <DOMBitmapInstance libraryItemName="gdtilt 0.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var attack = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "AttackBox");
            LayerEnd(attack).Should().Be(16);
            var first = attack.Descendants("DOMFrame").OrderBy(f => int.Parse((string?)f.Attribute("index") ?? "0")).First();
            int.Parse((string?)first.Attribute("index") ?? "0").Should().Be(0);
            first.ToString().Should().NotContain("attackBox");
            LayerAt(layers, "AttackBox", 0).Should().BeEmpty();
            LayerAt(layers, "AttackBox", 5).Should().BeEmpty();
            LayerAt(layers, "AttackBox", 6).Should().Contain(e => (string?)e.Attribute("name") == "attackBox");
            LayerAt(layers, "AttackBox", 8).Should().Contain(e => (string?)e.Attribute("name") == "attackBox");
            LayerAt(layers, "AttackBox", 9).Should().BeEmpty();
            LayerAt(layers, "AttackBox", 15).Should().BeEmpty();
        }

        [Fact]
        public void MergeBoxLayers_InsertsEmptyKeyInGapBetweenAttackBoxBursts()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="AttackBox"><frames>
                    <DOMFrame index="8" duration="8"><elements>
                      <DOMSymbolInstance name="attackBox" libraryItemName="ab"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="AttackBox"><frames>
                    <DOMFrame index="20" duration="8"><elements>
                      <DOMSymbolInstance name="attackBox2" libraryItemName="ab2"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0" duration="32"><elements>
                      <DOMBitmapInstance libraryItemName="pose.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var attack = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "AttackBox");
            var keys = attack.Descendants("DOMFrame")
                .OrderBy(f => int.Parse((string?)f.Attribute("index") ?? "0"))
                .ToList();
            var gap = keys.FirstOrDefault(f => (string?)f.Attribute("index") == "16");
            gap.Should().NotBeNull("a hole with no DOMFrame is not an empty key; publish would keep one PlaceObject");
            gap!.Descendants("DOMSymbolInstance").Should().BeEmpty();
            var dur = int.TryParse((string?)gap.Attribute("duration"), out var n) && n > 0 ? n : 1;
            dur.Should().Be(4);
            LayerAt(layers, "AttackBox", 15).Should().Contain(e => (string?)e.Attribute("name") == "attackBox");
            LayerAt(layers, "AttackBox", 16).Should().BeEmpty();
            LayerAt(layers, "AttackBox", 19).Should().BeEmpty();
            LayerAt(layers, "AttackBox", 8).Should().Contain(e => (string?)e.Attribute("name") == "attackBox");
            LayerAt(layers, "AttackBox", 20).Should().Contain(e => (string?)e.Attribute("name") == "attackBox2");
        }

        [Fact]
        public void MergeBoxLayers_DoesNotDuplicateUnnamedFx()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="10"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                      <DOMSymbolInstance libraryItemName="Symbol 1581"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="10"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="hb2"/>
                      <DOMSymbolInstance libraryItemName="Symbol 1581"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="aurabase"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="aurabase" libraryItemName="Symbol 1571"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.Descendants("DOMSymbolInstance")
                .Count(e => ((string?)e.Attribute("libraryItemName") ?? "").Contains("1581"))
                .Should().Be(1);
            layers.Elements("DOMLayer").Should().NotContain(l =>
                ((string?)l.Attribute("name") ?? "").Equals("Layer", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void MergeBoxLayers_DoesNotMergeArtLayerWithItemBoxInstances()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Item"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="ib"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="cloney"><frames>
                    <DOMFrame index="0"><elements>
                      <DOMSymbolInstance name="itemBox" libraryItemName="body"/>
                      <DOMBitmapInstance libraryItemName="pose.png"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name"))
                .Should().Equal("Item", "cloney");
            layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "cloney")
                .ToString().Should().Contain("pose.png");
        }

        [Fact]
        public void MergeBoxLayers_KeepsDestHoldAndAddsSourceBox()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="13"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="boxA"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="5"><elements/></DOMFrame>
                    <DOMFrame index="5" duration="8"><elements>
                      <DOMSymbolInstance name="hitBox2" libraryItemName="boxB"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            LayerEnd(hit).Should().Be(13);
            var frames = hit.Descendants("DOMFrame").OrderBy(f => int.Parse((string?)f.Attribute("index") ?? "0")).ToList();
            frames.Should().HaveCount(2);
            frames[0].ToString().Should().Contain("boxA");
            frames[0].ToString().Should().NotContain("boxB");
            frames[1].ToString().Should().Contain("boxA");
            frames[1].ToString().Should().Contain("boxB");
        }

        [Fact]
        public void MergeBoxLayers_PreservesLongerSourceSpan()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="295" duration="3"><elements>
                      <DOMSymbolInstance name="hitBox3" libraryItemName="boxC"/>
                    </elements></DOMFrame>
                  </frames></DOMLayer>
                  <DOMLayer name="HitBox"><frames>
                    <DOMFrame index="0" duration="2"><elements>
                      <DOMSymbolInstance name="hitBox" libraryItemName="boxA"/>
                    </elements></DOMFrame>
                    <DOMFrame index="2" duration="296"><elements/></DOMFrame>
                  </frames></DOMLayer>
                </layers>
                """);
            FlaCleanupSteps.MergeBoxLayers(layers);
            var hit = layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == "HitBox");
            LayerEnd(hit).Should().Be(298);
            var frames = hit.Descendants("DOMFrame").OrderBy(f => int.Parse((string?)f.Attribute("index") ?? "0")).ToList();
            int.Parse((string?)frames[0].Attribute("index") ?? "0").Should().Be(0);
            frames[0].ToString().Should().Contain("boxA");
        }

        [Fact]
        public void FoldersAndLock_SkipsMaskAndMaskedLayers()
        {
            var layers = XElement.Parse("""
                <layers>
                  <DOMLayer name="Layer 2" layerType="mask"/>
                  <DOMLayer name="Layer 3" layerType="masked" parentLayerIndex="0"/>
                  <DOMLayer name="cloney"/>
                </layers>
                """);
            FlaCleanupSteps.ApplyFoldersAndLock(layers, "cloney", isDoc: false);
            var names = layers.Elements("DOMLayer").Select(l => (string?)l.Attribute("name")).ToArray();
            names.Should().Contain("Layer 2");
            names.Should().Contain("Layer 3");
            names.Should().NotContain("Effect 1");
            Attr(layers, "Layer 2", "layerType").Should().Be("mask");
            Attr(layers, "Layer 3", "parentLayerIndex").Should().Be("0");
        }

        private static string? Attr(XElement layers, string layerName, string attr) =>
            (string?)layers.Elements("DOMLayer").First(l => (string?)l.Attribute("name") == layerName)
                .Attribute(attr);

        private static XElement ItemSmashLikeLayers() => XElement.Parse("""
            <layers>
              <DOMLayer name="HitBox"><frames>
                <DOMFrame index="0" duration="45"><elements>
                  <DOMSymbolInstance name="hitBox" libraryItemName="hb"/>
                </elements></DOMFrame>
                <DOMFrame index="45" duration="21"><elements/></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="charge spark effects"><frames>
                <DOMFrame index="0" duration="45"><elements>
                  <DOMSymbolInstance libraryItemName="charge spark effects"/>
                </elements></DOMFrame>
                <DOMFrame index="45" duration="21"><elements/></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="cloney"><frames>
                <DOMFrame index="0" duration="2"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash0.png"/>
                </elements></DOMFrame>
                <DOMFrame index="2" duration="43"><elements>
                  <DOMSymbolInstance libraryItemName="Symbol 1760"/>
                </elements></DOMFrame>
                <DOMFrame index="45" duration="6"><elements/></DOMFrame>
                <DOMFrame index="51" duration="2"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash7.png"/>
                </elements></DOMFrame>
                <DOMFrame index="53" duration="7"><elements/></DOMFrame>
                <DOMFrame index="60" duration="6"><elements/></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="Item"><frames>
                <DOMFrame index="0" duration="45"><elements>
                  <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                </elements></DOMFrame>
                <DOMFrame index="45" duration="1"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash2.png"/>
                </elements></DOMFrame>
                <DOMFrame index="46" duration="1"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash4.png"/>
                </elements></DOMFrame>
                <DOMFrame index="47" duration="2"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash5.png"/>
                </elements></DOMFrame>
                <DOMFrame index="49" duration="2"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash6.png"/>
                </elements></DOMFrame>
                <DOMFrame index="51" duration="9"><elements>
                  <DOMSymbolInstance name="itemBox" libraryItemName="box"/>
                </elements></DOMFrame>
                <DOMFrame index="60" duration="3"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash11.png"/>
                </elements></DOMFrame>
                <DOMFrame index="63" duration="3"><elements>
                  <DOMBitmapInstance libraryItemName="itmsmash12.png"/>
                </elements></DOMFrame>
              </frames></DOMLayer>
            </layers>
            """);

        private static int LayerEnd(XElement layer) =>
            layer.Descendants("DOMFrame").Select(f =>
            {
                var i = int.Parse((string?)f.Attribute("index") ?? "0");
                var d = int.TryParse((string?)f.Attribute("duration"), out var n) && n > 0 ? n : 1;
                return i + d;
            }).DefaultIfEmpty(0).Max();

        private static int[] FrameIndexes(XElement layer) =>
            layer.Descendants("DOMFrame")
                .Select(f => int.Parse((string?)f.Attribute("index") ?? "0"))
                .ToArray();

        private static string[] BitmapAt(XElement layer, int index) =>
            layer.Descendants("DOMFrame")
                .Where(f => (string?)f.Attribute("index") == index.ToString())
                .SelectMany(f => f.Descendants("DOMBitmapInstance"))
                .Select(e => ((string?)e.Attribute("libraryItemName") ?? "").Split('/').Last())
                .ToArray();

        [Fact]
        public void ApplyOrganizeXml_RewritesIncludeHrefToFolderPath()
        {
            var xml = """<DOMDocument><symbols><Include href="Symbol 1545.xml"/></symbols></DOMDocument>""";
            var next = FlaCleanupSteps.ApplyOrganizeXml(xml, "Animations/A\tSymbol 1545\tA");
            next.Should().Contain("href=\"Animations/A/A.xml\"");
            next.Should().NotContain("href=\"Symbol 1545.xml\"");
        }

        [Fact]
        public void RelocateLibraryFiles_MovesSymbolXmlOffLibraryRoot()
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/Symbol 1545.xml"] = Encoding.UTF8.GetBytes("""<DOMSymbolItem name="Symbol 1545"/>"""),
                ["LIBRARY/M 1.dat"] = new byte[] { 1, 2, 3 }
            };
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Symbol 1545"] = "Animations/A/A"
            };
            FlaCleanupSteps.RelocateLibraryFiles(entries, map).Should().Be(1);
            entries.Keys.Should().Contain("LIBRARY/Animations/A/A.xml");
            entries.Keys.Should().NotContain("LIBRARY/Symbol 1545.xml");
            entries.Keys.Should().Contain("LIBRARY/M 1.dat");
        }

        [Fact]
        public void PruneEmptyLibrarySymbols_RemovesUnreferencedEmptyClips()
        {
            var entries = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                ["LIBRARY/EmptyClip.xml"] = Encoding.UTF8.GetBytes("""
                    <DOMSymbolItem name="EmptyClip">
                      <timeline><DOMTimeline name="EmptyClip"><layers>
                        <DOMLayer name="Layer 1"><frames>
                          <DOMFrame index="0"><elements/></DOMFrame>
                        </frames></DOMLayer>
                      </layers></DOMTimeline></timeline>
                    </DOMSymbolItem>
                    """),
                ["LIBRARY/UsedClip.xml"] = Encoding.UTF8.GetBytes("""
                    <DOMSymbolItem name="UsedClip" linkageExportForAS="true">
                      <timeline><DOMTimeline name="UsedClip"><layers>
                        <DOMLayer name="Layer 1"><frames>
                          <DOMFrame index="0"><elements>
                            <DOMSymbolInstance libraryItemName="body"/>
                          </elements></DOMFrame>
                        </frames></DOMLayer>
                      </layers></DOMTimeline></timeline>
                    </DOMSymbolItem>
                    """)
            };
            JpexsCliService.PruneEmptyLibrarySymbols(entries).Should().Be(1);
            entries.Keys.Should().NotContain("LIBRARY/EmptyClip.xml");
            entries.Keys.Should().Contain("LIBRARY/UsedClip.xml");
        }

        [Fact]
        public void MapBoxLayerFamily_GroupsHitAttackItem()
        {
            JpexsCliService.MapBoxLayerFamily("hitbox2").Should().Be("HitBox");
            JpexsCliService.MapBoxLayerFamily("attackbox*").Should().Be("AttackBox");
            JpexsCliService.MapBoxLayerFamily("itemBox").Should().Be("Item");
            JpexsCliService.MapBoxLayerFamily("stance").Should().BeNull();
        }

        [Fact]
        public void BuildFlaPcmFfmpegArgs_Writes44100Pcm()
        {
            var args = JpexsCliService.BuildFlaPcmFfmpegArgs("in.flv", "out.wav");
            args.Should().Contain("-ar 44100");
            args.Should().Contain("pcm_s16le");
            args.Should().Contain("-vn");
            args.Should().NotContain("adelay");
            args.Should().NotContain("adeclick");
        }

        [Fact]
        public void TryReadWavFormat_Pcm16RequiresWaveFormatTag1()
        {
            JpexsCliService.TryReadWavFormat(MakeRiffWav(1, 16), out var pcm).Should().BeTrue();
            pcm.IsPcm16.Should().BeTrue();
            JpexsCliService.TryReadWavFormat(MakeRiffWav(17, 16), out var adpcm).Should().BeTrue();
            adpcm.IsPcm16.Should().BeFalse();
        }

        [Fact]
        public void InjectStanceChildFlaMethods_InjectsIdleExtras_KeepsListenerSidecar()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-stanceinj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "bmi.fla");
            var flaPkg = Path.Combine(dir, "bmi_fla");
            Directory.CreateDirectory(flaPkg);
            try
            {
                File.WriteAllText(Path.Combine(flaPkg, "Idle_3.as"), """
                    package bmi_fla {
                      public dynamic class Idle_3 extends MovieClip {
                        public var self:*;
                        public function Idle_3() { super(); }
                        public function restoreSpecials() : * { this.self.setAttackEnabled(true); }
                      }
                    }
                    """);
                File.WriteAllText(Path.Combine(flaPkg, "Grab_106.as"), """
                    package bmi_fla {
                      public dynamic class Grab_106 extends MovieClip {
                        public function Grab_106() { super(); }
                        public function arm() : * { addEventListener(Event.ENTER_FRAME, this.tick); }
                      }
                    }
                    """);
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Symbol 1556.xml", """
                        <DOMSymbolItem name="Symbol 1556" linkageExportForAS="true" linkageClassName="bmi">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="IdleClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/IdleClip.xml", """
                        <DOMSymbolItem name="IdleClip" linkageExportForAS="true" linkageClassName="bmi_fla.Idle_3">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.InjectStanceChildFlaMethods(fla, dir, "bmi").Should().BeGreaterThan(0);
                using (var read = ZipFile.OpenRead(fla))
                {
                    read.GetEntry("LIBRARY/IdleClip.xml").Should().NotBeNull();
                    read.GetEntry("LIBRARY/Animations/Idle/Idle.xml").Should().BeNull();
                    using var reader = new StreamReader(read.GetEntry("LIBRARY/IdleClip.xml")!.Open());
                    reader.ReadToEnd().Should().Contain("restoreSpecials");
                }

                File.Exists(Path.Combine(flaPkg, "Idle_3.as")).Should().BeFalse();
                File.Exists(Path.Combine(flaPkg, "Grab_106.as")).Should().BeTrue();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void DisableBitmapSmoothingInXml_SetsAllowSmoothingFalse()
        {
            var xml = """<DOMBitmapItem name="Bitmap 1.png" allowSmoothing="true"/>""";
            JpexsCliService.DisableBitmapSmoothingInXml(xml).Should().Contain("allowSmoothing=\"false\"");
            JpexsCliService.DisableBitmapSmoothingInXml("""<DOMBitmapItem name="Bitmap 2.png"/>""")
                .Should().Contain("allowSmoothing=\"false\"");
        }

        [Fact]
        public void PatchFlaMainLinkageId_StripsOldBodySuffix()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-linkid-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"), """
                    _loc1_ = { "linkage_id":"best_mage_black_mage", "statsName":"best_mage" };
                    """);
                JpexsCliService.PatchFlaMainLinkageId(dir, "best_mage").Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "Main.as")).Should().Contain("\"linkage_id\":\"best_mage\"");
                File.ReadAllText(Path.Combine(dir, "Main.as")).Should().NotContain("best_mage_black_mage");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PatchFlaMainLinkageId_PreservesProjectileIds_PatchesExtAndMovieclips()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-linkproj-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"), """
                    register("resources",{ "movieclips":["bmi_black_mage","bmi_bmmeteorprojectile"] });
                    _loc1_ = { "linkage_id":"bmi_black_mage" };
                    "fsmashfull":{
                       "classAPI":SSF2Projectile,
                       "linkage_id":"bmi",
                    };
                    """);
                File.WriteAllText(Path.Combine(dir, "BmiExt.as"), """
                    _loc1_ = { "linkage_id":"bmi_black_mage" };
                    "fsmashfull":{
                       "classAPI":SSF2Projectile,
                       "linkage_id":"bmi_BMfsmashfull",
                    };
                    """);
                JpexsCliService.PatchFlaMainLinkageId(dir, "bmi").Should().BeTrue();
                var main = File.ReadAllText(Path.Combine(dir, "Main.as"));
                var ext = File.ReadAllText(Path.Combine(dir, "BmiExt.as"));
                main.Should().Contain("\"linkage_id\":\"bmi\"");
                main.Should().NotContain("bmi_black_mage");
                main.Should().Contain("\"bmi_bmmeteorprojectile\"");
                main.Should().Contain("\"linkage_id\":\"bmi_BMfsmashfull\"");
                ext.Should().Contain("\"linkage_id\":\"bmi\"");
                ext.Should().NotContain("bmi_black_mage");
                ext.Should().Contain("bmi_BMfsmashfull");
                JpexsCliService.CollectForbiddenFlaBodyIdentityHits(dir, "bmi").Should().BeEmpty();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void CollectForbiddenFlaBodyIdentityHits_ReportsBodyAlias_IgnoresHud()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-leftover-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                File.WriteAllText(Path.Combine(dir, "Main.as"), """
                    movieclips:["Bo_Mama_black_mage","Bo_Mama_black_mage_hud","Bo_Mama_bmmeteorprojectile"];
                    """);
                var hits = JpexsCliService.CollectForbiddenFlaBodyIdentityHits(dir, "Bo_Mama");
                hits.Should().Contain(h => h.Contains("Bo_Mama_black_mage"));
                hits.Should().NotContain(h => h.Contains("hud"));
                hits.Should().NotContain(h => h.Contains("bmmeteorprojectile"));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BoMamaFla_HasNoDonorBodyIdentityLeftovers()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            string? fla = null;
            for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            {
                var cand = Path.Combine(dir.FullName, "SSF2 Mod Manager", "SSF2 Shell", "build",
                    "data", "character", "expansions", "Bo Mama", "FLA");
                if (Directory.Exists(cand))
                {
                    fla = cand;
                    break;
                }
            }
            if (fla == null)
                return;
            JpexsCliService.CollectForbiddenFlaBodyIdentityHits(fla, "Bo_Mama").Should().BeEmpty();
        }

        [Fact]
        public void StripDocumentTimelinesToEmptyFrame_ClearsSceneInstances()
        {
            var xml = """
                <DOMDocument xmlns="http://ns.adobe.com/xfl/2008/">
                  <timelines>
                    <DOMTimeline name="Scene 1">
                      <layers>
                        <DOMLayer name="Layer 1">
                          <frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                            <DOMFrame index="1">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Bees2"/>
                              </elements>
                            </DOMFrame>
                          </frames>
                        </DOMLayer>
                      </layers>
                    </DOMTimeline>
                  </timelines>
                </DOMDocument>
                """;
            var next = JpexsCliService.StripDocumentTimelinesToEmptyFrame(xml);
            next.Should().Contain("name=\"Scene 1\"");
            next.Should().Contain("<elements/>");
            next.Should().NotContain("libraryItemName=\"Bees2\"");
            next.Should().NotContain("index=\"1\"");
        }

        [Fact]
        public void BuildFlaOrganizeBlueprint_SharedUnusedChildren_GoInTypeFolders()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-shared-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Hero">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="IdleClip" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/IdleClip.xml", """<DOMSymbolItem name="IdleClip"/>""");
                    WriteZipEntry(zip, "LIBRARY/Fx.xml", """
                        <DOMSymbolItem name="Fx">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMSymbolInstance libraryItemName="FxGraphic"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/FxGraphic.xml", """
                        <DOMSymbolItem name="FxGraphic" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements><DOMBitmapInstance libraryItemName="Bitmap Fx"/></elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Bitmap Fx.xml", """<DOMBitmapItem name="Bitmap Fx"/>""");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().Contain("Shared Animations/Fx\tFx");
                tsv.Should().Contain("Shared Animations/Fx/Graphics\tFxGraphic");
                tsv.Should().Contain("Shared Animations/Fx/Sprites\tBitmap Fx");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        private static void WriteZipEntry(ZipArchive zip, string name, string content)
        {
            var entry = zip.CreateEntry(name, System.IO.Compression.CompressionLevel.Fastest);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }

        private static byte[] MakeRiffWav(ushort audioFormat, ushort bits)
        {
            var dataBytes = 8;
            var wav = new byte[44 + dataBytes];
            System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(wav, 0);
            BitConverter.GetBytes(36 + dataBytes).CopyTo(wav, 4);
            System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(wav, 8);
            System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(wav, 12);
            BitConverter.GetBytes(16).CopyTo(wav, 16);
            BitConverter.GetBytes(audioFormat).CopyTo(wav, 20);
            BitConverter.GetBytes((ushort)1).CopyTo(wav, 22);
            BitConverter.GetBytes(22050).CopyTo(wav, 24);
            var block = (ushort)(bits / 8);
            BitConverter.GetBytes(22050 * block).CopyTo(wav, 28);
            BitConverter.GetBytes(block).CopyTo(wav, 32);
            BitConverter.GetBytes(bits).CopyTo(wav, 34);
            System.Text.Encoding.ASCII.GetBytes("data").CopyTo(wav, 36);
            BitConverter.GetBytes(dataBytes).CopyTo(wav, 40);
            return wav;
        }

        [Fact]
        public void IsFlaBoxInstanceName_MatchesBoxSubstring()
        {
            JpexsCliService.IsFlaBoxInstanceName("hurtbox").Should().BeTrue();
            JpexsCliService.IsFlaBoxInstanceName("Hurtbox2").Should().BeTrue();
            JpexsCliService.IsFlaBoxInstanceName("attackbox").Should().BeTrue();
            JpexsCliService.IsFlaBoxInstanceName("HANDBOX").Should().BeTrue();
            JpexsCliService.IsFlaBoxInstanceName("hitbox").Should().BeTrue();
            JpexsCliService.IsFlaBoxInstanceName("itemBox").Should().BeFalse();
            JpexsCliService.IsFlaBoxInstanceName("item_box").Should().BeFalse();
            JpexsCliService.IsFlaBoxInstanceName("Layer 1").Should().BeFalse();
            JpexsCliService.IsFlaBoxInstanceName("Labels").Should().BeFalse();
            JpexsCliService.IsFlaBoxInstanceName("").Should().BeFalse();
        }

        [Fact]
        public void MarkFlaBoxLayersAsOutlineInXml_UsesInstanceNamesNotLayerName()
        {
            var xml = """
                <DOMLayer name="Layer 1" color="#FF0000">
                  <frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="hurtbox" libraryItemName="Symbol 9"/>
                      </elements>
                    </DOMFrame>
                  </frames>
                </DOMLayer>
                <DOMLayer name="Layer 2" color="#4FFF4F">
                  <frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="stance" libraryItemName="Idle"/>
                      </elements>
                    </DOMFrame>
                  </frames>
                </DOMLayer>
                <DOMLayer name="hurtbox">
                  <frames>
                    <DOMFrame index="0"><elements/></DOMFrame>
                  </frames>
                </DOMLayer>
                """;
            var next = JpexsCliService.MarkFlaBoxLayersAsOutlineInXml(xml);
            next.Should().MatchRegex("name=\"Layer 1\"[^>]*outline=\"true\"");
            next.Should().Contain("color=\"#FF0000\"");
            next.Should().NotMatchRegex("name=\"Layer 2\"[^>]*outline=\"true\"");
            next.Should().NotMatchRegex("name=\"hurtbox\"[^>]*outline=\"true\"");
        }

        [Fact]
        public void MarkFlaBoxLayersAsOutlineInXml_SkipsItemBox()
        {
            var xml = """
                <DOMLayer name="Layer 7" color="#FF0000">
                  <frames>
                    <DOMFrame index="0">
                      <elements>
                        <DOMSymbolInstance name="itemBox" libraryItemName="Symbol 9"/>
                      </elements>
                    </DOMFrame>
                  </frames>
                </DOMLayer>
                """;
            var next = JpexsCliService.MarkFlaBoxLayersAsOutlineInXml(xml);
            next.Should().NotMatchRegex("name=\"Layer 7\"[^>]*outline=\"true\"");
        }

        [Fact]
        public void RewriteLibraryOrganizeRefs_DoesNotRewriteLayerNames()
        {
            var xml = """
                <DOMLayer name="Symbol 12">
                  <Include href="Symbol 12.xml"/>
                  <DOMSymbolInstance libraryItemName="Symbol 12"/>
                </DOMLayer>
                """;
            var next = JpexsCliService.RewriteLibraryOrganizeRefs(xml, "Symbol 12", "Animations/Idle/Idle");
            next.Should().Contain("libraryItemName=\"Animations/Idle/Idle\"");
            next.Should().Contain("name=\"Symbol 12\"");
            next.Should().Contain("href=\"Symbol 12.xml\"");
        }

        [Fact]
        public void ParseFlaOrganizeBlueprint_KeepsFullOldPath()
        {
            var tsv = "Audio\tFolderA/Spark\tBoom\nEffects\tFolderB/Spark\tBoom";
            var moves = JpexsCliService.ParseFlaOrganizeBlueprint(tsv);
            moves.Should().Contain(m => m.OldName == "FolderA/Spark" && m.NewLibraryPath == "Audio/Boom");
            moves.Should().Contain(m => m.OldName == "FolderB/Spark" && m.NewLibraryPath == "Effects/Boom");
        }

        [Fact]
        public void WriteFlaOrganizeBlueprint_WritesTsv_DoesNotRewriteFlaZip()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-orgzip-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "hero.fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "DOMDocument.xml", """
                        <DOMDocument>
                          <symbols><Include href="Symbol 12.xml"/></symbols>
                          <timelines><DOMTimeline name="Scene 1"><layers>
                            <DOMLayer name="Symbol 12"><frames>
                              <DOMFrame index="0"><elements>
                                <DOMSymbolInstance libraryItemName="Hero" name="stance"/>
                              </elements></DOMFrame>
                            </frames></DOMLayer>
                          </layers></DOMTimeline></timelines>
                        </DOMDocument>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Hero.xml", """
                        <DOMSymbolItem name="Hero" linkageExportForAS="true" linkageClassName="hero">
                          <timeline><DOMTimeline><layers><DOMLayer name="Symbol 12"><frames>
                            <DOMFrame index="0" name="idle">
                              <elements>
                                <DOMSymbolInstance libraryItemName="Symbol 12" name="stance"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/Symbol 12.xml", """
                        <DOMSymbolItem name="Symbol 12">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                var before = File.ReadAllBytes(fla);
                JpexsCliService.WriteFlaOrganizeBlueprint(fla, dir).Should().BeGreaterThan(0);
                File.ReadAllBytes(fla).Should().Equal(before);
                var tsv = File.ReadAllText(Path.Combine(dir, "tools", "ssf2mm_organize.txt"));
                tsv.Should().Contain("Symbol 12");
                tsv.Should().Contain("Animations/Idle");
                using var read = ZipFile.OpenRead(fla);
                using (var reader = new StreamReader(read.GetEntry("LIBRARY/Symbol 12.xml")!.Open()))
                    reader.ReadToEnd().Should().Contain("name=\"Symbol 12\"");
                using (var reader = new StreamReader(read.GetEntry("LIBRARY/Hero.xml")!.Open()))
                {
                    var host = reader.ReadToEnd();
                    host.Should().Contain("libraryItemName=\"Symbol 12\"");
                    host.Should().NotContain("libraryItemName=\"Animations/Idle");
                }
                using (var reader = new StreamReader(read.GetEntry("DOMDocument.xml")!.Open()))
                    reader.ReadToEnd().Should().Contain("href=\"Symbol 12.xml\"");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RewriteCharacterExtCasts_CloneDayPascal_ToIdentExt()
        {
            var src = "this.self = SSF2API.getCharacter(this) as CloneDayExt;";
            JpexsCliService.RewriteCharacterExtCasts(src, "Clone_Day")
                .Should().Be("this.self = SSF2API.getCharacter(this) as Clone_DayExt;");
        }

        [Fact]
        public void RewriteCharacterExtCasts_BlackMageExt_ToIdentExt()
        {
            var next = JpexsCliService.RewriteCharacterExtCasts(
                "this.self = SSF2API.getCharacter(this) as BlackMageExt;",
                "Clone_Day");
            next.Should().Contain("as Clone_DayExt");
            next.Should().NotContain("BlackMageExt");
            next.Should().NotContain("CloneDayExt");
        }

        [Fact]
        public void RewriteCharacterExtCasts_LeavesEngineExtAlone()
        {
            var src = "if (this is SSF2CharacterExt) return;";
            JpexsCliService.RewriteCharacterExtCasts(src, "Clone_Day").Should().Be(src);
        }

        [Fact]
        public void PatchFlaExtCasts_RewritesSidecarAndTimeline()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-extcast-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var fla = Path.Combine(dir, "Clone_Day.fla");
            try
            {
                File.WriteAllText(Path.Combine(dir, "Revival_8.as"), """
                    this.self = SSF2API.getCharacter(this) as CloneDayExt;
                    if (SSF2API.isReady())
                        this.self.setGlobalVariable("canStartRise", true);
                    """);
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Revival_8.xml", """
                        <DOMSymbolItem name="Revival 8" linkageClassName="Clone_Day_fla.Revival_8">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <Actionscript><script><![CDATA[this.self = SSF2API.getCharacter(this) as BlackMageExt;]]></script></Actionscript>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.PatchFlaExtCasts(dir, "Clone_Day", fla).Should().BeTrue();
                File.ReadAllText(Path.Combine(dir, "Revival_8.as"))
                    .Should().Contain("as Clone_DayExt")
                    .And.NotContain("CloneDayExt");
                using var read = ZipFile.OpenRead(fla);
                using var reader = new StreamReader(read.GetEntry("LIBRARY/Revival_8.xml")!.Open());
                var xml = reader.ReadToEnd();
                xml.Should().Contain("as Clone_DayExt");
                xml.Should().NotContain("BlackMageExt");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PromoteSmashChargeClipsToMovieClip_KeepsChargeClipsAsMovieClip()
        {
            var fla = Path.Combine(Path.GetTempPath(), "ssf2mm-charge-" + Guid.NewGuid().ToString("N") + ".fla");
            try
            {
                using (var zip = ZipFile.Open(fla, ZipArchiveMode.Create))
                {
                    WriteZipEntry(zip, "LIBRARY/Host.xml", """
                        <DOMSymbolItem name="Host" linkageExportForAS="true" linkageClassName="Clone_Day">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0" name="idle"><elements/></DOMFrame>
                            <DOMFrame index="10" name="usmash">
                              <elements>
                                <DOMSymbolInstance libraryItemName="usmash uncharged"/>
                              </elements>
                            </DOMFrame>
                            <DOMFrame index="20" name="dsmash">
                              <elements>
                                <DOMSymbolInstance libraryItemName="dsmash ice"/>
                              </elements>
                            </DOMFrame>
                            <DOMFrame index="30" name="fsmash">
                              <elements>
                                <DOMSymbolInstance libraryItemName="A_forwardsmash"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/usmash uncharged.xml", """
                        <DOMSymbolItem name="usmash uncharged" linkageClassName="Clone_Day_usmash_uncharged" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/dsmash ice.xml", """
                        <DOMSymbolItem name="dsmash ice" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMSymbolInstance libraryItemName="ice"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/ice.xml", """
                        <DOMSymbolItem name="ice" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/A_forwardsmash.xml", """
                        <DOMSymbolItem name="A_forwardsmash" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0">
                              <elements>
                                <DOMSymbolInstance libraryItemName="fsmash nested"/>
                              </elements>
                            </DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/fsmash nested.xml", """
                        <DOMSymbolItem name="fsmash nested" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                    WriteZipEntry(zip, "LIBRARY/FxGraphic.xml", """
                        <DOMSymbolItem name="FxGraphic" symbolType="graphic">
                          <timeline><DOMTimeline><layers><DOMLayer><frames>
                            <DOMFrame index="0"><elements/></DOMFrame>
                          </frames></DOMLayer></layers></DOMTimeline></timeline>
                        </DOMSymbolItem>
                        """);
                }

                JpexsCliService.PromoteSmashChargeClipsToMovieClip(fla).Should().BeTrue();
                using (var read = ZipFile.OpenRead(fla))
                {
                    ReadZipEntry(read, "LIBRARY/usmash uncharged.xml").Should().Contain("symbolType=\"movie clip\"");
                    ReadZipEntry(read, "LIBRARY/dsmash ice.xml").Should().Contain("symbolType=\"movie clip\"");
                    ReadZipEntry(read, "LIBRARY/ice.xml").Should().Contain("symbolType=\"movie clip\"");
                    ReadZipEntry(read, "LIBRARY/A_forwardsmash.xml").Should().Contain("symbolType=\"movie clip\"");
                    ReadZipEntry(read, "LIBRARY/fsmash nested.xml").Should().Contain("symbolType=\"movie clip\"");
                    ReadZipEntry(read, "LIBRARY/FxGraphic.xml").Should().Contain("symbolType=\"graphic\"");
                }

                var tsv = JpexsCliService.BuildFlaOrganizeBlueprint(fla);
                tsv.Should().NotContain("/Graphics\tusmash uncharged");
                tsv.Should().NotContain("/Graphics\tdsmash ice");
                tsv.Should().NotContain("/Graphics\tice");
            }
            finally
            {
                try { File.Delete(fla); } catch { /* ignore */ }
            }
        }

        private static string ReadZipEntry(ZipArchive zip, string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        [Fact]
        public void BuildDualPackageSelectScript_SetsCharacterId()
        {
            var donor = """
                package
                {
                   import flash.display.MovieClip;
                   public dynamic class mario_select extends MovieClip
                   {
                      public var characterID:String;
                      public function mario_select()
                      {
                         super();
                         addFrameScript(0,this.frame1);
                      }
                      internal function frame1() : *
                      {
                         this.characterID = "mario";
                      }
                   }
                }
                """;
            var dual = MenuSlotService.BuildDualPackageSelectScript(
                donor, "mario_select", "notmario_select", "mario", "notmario", "characterID");
            dual.Should().Contain("class mario_select");
            dual.Should().Contain("class notmario_select");
            dual.Should().Contain("this.characterID = \"notmario\"");
            MenuSlotService.IsCssSelectLinkage("notmario_select").Should().BeTrue();
        }
    }
}
