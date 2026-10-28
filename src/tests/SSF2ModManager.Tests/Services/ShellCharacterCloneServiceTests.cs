using System.IO;
using System.Threading;
using System.Windows;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using SSF2ModManager.Views;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ShellCharacterCloneServiceTests
    {
        [Fact]
        public void InstallExpansionFromTemplate_ReplacesBuuAndRenamesAssets()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-shell-exp-" + Guid.NewGuid().ToString("N"));
            var template = Path.Combine(root, "template");
            var dest = Path.Combine(root, "expansions", "myfighter");
            var cloned = Path.Combine(root, "cloned.swf");
            Directory.CreateDirectory(template);
            try
            {
                File.WriteAllText(Path.Combine(template, "buu.swf"), "donor-fighter");
                File.WriteAllText(Path.Combine(template, "assets_buu.swf"), "donor-assets");
                File.WriteAllText(Path.Combine(template, "classic.json"), "{\"series\":\"smash\"}");
                File.WriteAllText(cloned, "cloned-fighter");

                var folder = ShellCharacterCloneService.InstallExpansionFromTemplate(
                    template, dest, "My Fighter!", "My Fighter", cloned);

                folder.Should().Be(dest);
                File.ReadAllText(Path.Combine(dest, "My_Fighter_.swf")).Should().Be("cloned-fighter");
                File.Exists(Path.Combine(dest, "buu.swf")).Should().BeFalse();
                File.Exists(Path.Combine(dest, "assets_buu.swf")).Should().BeFalse();
                File.ReadAllText(Path.Combine(dest, "assets_My_Fighter_.swf")).Should().Be("donor-assets");
                File.ReadAllText(Path.Combine(dest, "assets.swf")).Should().Be("donor-assets");
                File.Exists(Path.Combine(dest, "classic.json")).Should().BeTrue();
                Directory.Exists(Path.Combine(dest, "FLA")).Should().BeTrue();

                var meta = JObject.Parse(File.ReadAllText(Path.Combine(dest, "css_meta.json")));
                meta["displayName"]!.ToString().Should().Be("My Fighter");
                meta["id"]!.ToString().Should().Be("My_Fighter_");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ListPlayableCharacters_SkipsRandomAndMissingFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-shell-roster-" + Guid.NewGuid().ToString("N"));
            var charDir = Path.Combine(root, "data", "character");
            Directory.CreateDirectory(charDir);
            try
            {
                File.WriteAllText(Path.Combine(charDir, "mario.swf"), "x");
                var doc = JObject.Parse("""
                {
                  "character": {
                    "mario": { "file": "mario.swf", "name": "Mario" },
                    "missing": { "file": "missing.swf", "name": "Gone" }
                  },
                  "character_select_screen": {
                    "rows": [ ["mario", "random", "empty1", "missing"] ]
                  }
                }
                """);

                var list = ShellCharacterCloneService.ListPlayableCharacters(root, doc);
                list.Should().ContainSingle();
                list[0].Id.Should().Be("mario");
                list[0].DisplayName.Should().Be("Mario");
                list[0].Label.Should().Be("Mario  (mario)");

                File.Delete(Path.Combine(charDir, "mario.swf"));
                File.WriteAllText(Path.Combine(charDir, "mario.ssf"), "x");
                doc["character"]!["mario"]!["file"] = "mario.ssf";
                var packed = ShellCharacterCloneService.ListPlayableCharacters(root, doc);
                packed.Should().ContainSingle();
                packed[0].SwfPath.Should().EndWith("mario.ssf");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ExpansionFolderAndGameId_CloneDee_KeepsSpacesAndMatchesRealExpansions()
        {
            ShellCharacterCloneService.ExpansionFolderName("Clone Dee", "Clone Dee")
                .Should().Be("Clone Dee");
            ShellCharacterCloneService.ExpansionGameId("Clone Dee")
                .Should().Be("Clone_Dee");
        }

        [Fact]
        public void InstallExpansionFromTemplate_CloneDeeFolder_WritesCloneDeeSwf()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-shell-exp-" + Guid.NewGuid().ToString("N"));
            var template = Path.Combine(root, "template");
            var dest = Path.Combine(root, "expansions", "Clone Dee");
            var cloned = Path.Combine(root, "cloned.swf");
            Directory.CreateDirectory(template);
            try
            {
                File.WriteAllText(Path.Combine(template, "buu.swf"), "donor-fighter");
                File.WriteAllText(Path.Combine(template, "assets.swf"), "donor-assets");
                File.WriteAllText(cloned, "cloned-fighter");

                var folder = ShellCharacterCloneService.InstallExpansionFromTemplate(
                    template, dest, "Clone Dee", "Clone Dee", cloned);

                folder.Should().Be(dest);
                Path.GetFileName(folder).Should().Be("Clone Dee");
                File.ReadAllText(Path.Combine(dest, "Clone_Dee.swf")).Should().Be("cloned-fighter");
                File.ReadAllText(Path.Combine(dest, "assets.swf")).Should().Be("donor-assets");

                var meta = JObject.Parse(File.ReadAllText(Path.Combine(dest, "css_meta.json")));
                meta["displayName"]!.ToString().Should().Be("Clone Dee");
                meta["id"]!.ToString().Should().Be("Clone_Dee");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void CloneAssetSource_TakenLinkage_IsThatCharacter()
        {
            ShellCharacterCloneService.CloneAssetSource("mario", new[] { "mario", "link" })
                .Should().Be("mario");
        }

        [Fact]
        public void CloneAssetSource_NumberedClone_UsesBase()
        {
            ShellCharacterCloneService.CloneAssetSource("donkeykong2", new[] { "donkeykong" })
                .Should().Be("donkeykong");
            ShellCharacterCloneService.CloneAssetSource("donkeykong3", new[] { "donkeykong", "donkeykong2" })
                .Should().Be("donkeykong");
        }

        [Fact]
        public void CloneAssetSource_NewCharacter_IsNull()
        {
            ShellCharacterCloneService.CloneAssetSource("vegeta", new[] { "mario" })
                .Should().BeNull();
            ShellCharacterCloneService.CloneAssetSource("mario1", new[] { "mario" })
                .Should().BeNull();
        }

        [Fact]
        public void CloneAssetChoices_ListsOnlyClones()
        {
            var files = new[]
            {
                new ImportCloneAssetChoice { Path = @"C:\a.swf", FileName = "a.swf", DisplayName = "Mario", SourceId = "mario" },
                new ImportCloneAssetChoice { Path = @"C:\b.swf", FileName = "b.swf", DisplayName = "Vegeta", SourceId = "vegeta" },
                new ImportCloneAssetChoice { Path = @"C:\c.swf", FileName = "c.swf", DisplayName = "Donkey Kong", SourceId = "donkeykong2" }
            };
            var list = ShellCharacterCloneService.CloneAssetChoices(files, new[] { "mario", "donkeykong" });
            list.Should().HaveCount(2);
            list[0].SourceId.Should().Be("mario");
            list[1].SourceId.Should().Be("donkeykong");
            list[1].DisplayName.Should().Be("Donkey Kong");
        }

        [Fact]
        public void InstallExpansionFromTemplate_KeepOldAssets_WritesCssMeta()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-shell-exp-" + Guid.NewGuid().ToString("N"));
            var template = Path.Combine(root, "template");
            var dest = Path.Combine(root, "expansions", "mario2");
            var cloned = Path.Combine(root, "cloned.swf");
            Directory.CreateDirectory(template);
            try
            {
                File.WriteAllText(Path.Combine(template, "assets.swf"), "donor-assets");
                File.WriteAllText(cloned, "cloned-fighter");

                ShellCharacterCloneService.InstallExpansionFromTemplate(
                    template, dest, "mario2", "Mario", cloned, "mario");

                var meta = JObject.Parse(File.ReadAllText(Path.Combine(dest, "css_meta.json")));
                meta["oldassets"]!.Value<bool>().Should().BeTrue();
                meta["oldassetid"]!.ToString().Should().Be("mario");
                meta["displayName"]!.ToString().Should().Be("Mario");
                meta["id"]!.ToString().Should().Be("mario2");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void PickOwner_UnshownWindow_IsSkipped()
        {
            Exception? error = null;
            Window? owner = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var hosted = new Window();
                    owner = KeepCloneAssetsWindow.PickOwner(hosted, new[] { hosted });
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            error.Should().BeNull();
            owner.Should().BeNull();
        }

        [Fact]
        public void SetRowsChecked_ChecksEveryRow()
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                try
                {
                    var rows = new[]
                    {
                        new System.Windows.Controls.CheckBox(),
                        new System.Windows.Controls.CheckBox()
                    };
                    KeepCloneAssetsWindow.SetRowsChecked(rows, true);
                    if (rows[0].IsChecked != true || rows[1].IsChecked != true)
                        throw new InvalidOperationException("Check all left a row unchecked.");
                    KeepCloneAssetsWindow.SetRowsChecked(rows, false);
                    if (rows[0].IsChecked != false || rows[1].IsChecked != false)
                        throw new InvalidOperationException("Clearing Check all left a row checked.");
                }
                catch (Exception ex)
                {
                    error = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
            error.Should().BeNull();
        }

        [Fact]
        public void CollectTakenSymbolNames_ReadsStockAndExpansionExports()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-taken-" + Guid.NewGuid().ToString("N"));
            var stock = Path.Combine(root, "data", "character");
            var expansion = Path.Combine(root, "expansions", "character", "zamus2");
            var template = Path.Combine(root, "expansions", "character", "template");
            Directory.CreateDirectory(stock);
            Directory.CreateDirectory(expansion);
            Directory.CreateDirectory(template);
            try
            {
                File.WriteAllBytes(Path.Combine(stock, "zamus.swf"), SymbolSwf("MetroidSymbolCSS"));
                File.WriteAllBytes(Path.Combine(expansion, "zamus2.swf"), SymbolSwf("fairSparkle"));
                File.WriteAllBytes(Path.Combine(template, "buu.swf"), SymbolSwf("TemplateOnly"));
                var taken = ShellCharacterCloneService.CollectTakenSymbolNames(root);
                taken.Should().Contain("MetroidSymbolCSS");
                taken.Should().Contain("fairSparkle");
                taken.Should().NotContain("TemplateOnly");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        private static byte[] SymbolSwf(string className)
        {
            var name = System.Text.Encoding.UTF8.GetBytes(className);
            var bodyLen = 4 + name.Length + 1;
            var length = 13 + 2 + bodyLen + 2;
            var swf = new byte[length];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = 10;
            swf[4] = (byte)length;
            swf[5] = (byte)(length >> 8);
            swf[6] = (byte)(length >> 16);
            swf[7] = (byte)(length >> 24);
            swf[10] = 1;
            swf[11] = 1;
            var code = (76 << 6) | bodyLen;
            swf[13] = (byte)code;
            swf[14] = (byte)(code >> 8);
            swf[15] = 1;
            swf[17] = 1;
            System.Buffer.BlockCopy(name, 0, swf, 19, name.Length);
            return swf;
        }
    }
}
