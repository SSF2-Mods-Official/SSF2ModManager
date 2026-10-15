using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class MappingsEditServiceTests
    {
        [Fact]
        public void DropMissingFiles_RemovesCharacterWhoseSwfIsGone()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-mapdrop-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(root, "data", "character"));
            File.WriteAllBytes(Path.Combine(root, "data", "character", "mario.ssf"), new byte[] { 1 });
            var doc = JObject.Parse(
                "{\"character\":{\"mario\":{\"file\":\"mario.ssf\"},\"BanjoKazoozoo\":{\"file\":\"expansions/BanjoKazoozoo/BanjoKazoozoo.swf\"}}," +
                "\"character_select_screen\":{\"rows\":[[\"mario\",\"BanjoKazoozoo\"]]}}");
            var dropped = MappingsEditService.DropMissingFiles(doc, root);
            dropped.Should().Equal("BanjoKazoozoo");
            doc["character"]!["mario"].Should().NotBeNull();
            (doc["character"]!["BanjoKazoozoo"] == null).Should().BeTrue();
            doc["character_select_screen"]!["rows"]![0]!.Select(t => (string?)t).Should().Equal("mario");
            Directory.Delete(root, true);
        }

        [Fact]
        public void ReplaceInRows_ReplacesAllMatches()
        {
            var rows = new List<List<string>>
            {
                new() { "mario", "sandbag", "random" },
                new() { "sandbag", "luigi" }
            };
            MappingsEditService.ReplaceInRows(rows, "sandbag", "myfighter").Should().BeTrue();
            rows[0][1].Should().Be("myfighter");
            rows[1][0].Should().Be("myfighter");
        }

        [Fact]
        public void InsertAndAppend_GrowRow()
        {
            var rows = new List<List<string>> { new() { "mario", "luigi" } };
            MappingsEditService.InsertInRow(rows, 0, 1, "peach");
            rows[0].Should().Equal("mario", "peach", "luigi");
            MappingsEditService.AppendToRow(rows, 0, "random");
            rows[0].Should().EndWith("random");
        }

        [Fact]
        public void UpsertEntry_CreatesCharacterFields()
        {
            var doc = new JObject();
            var entry = MappingsEditService.UpsertEntry(
                doc, "character", "myfighter", "myfighter.ssf",
                name: "My Fighter", seriesIcon: "smash_symbol");
            entry["file"]!.ToString().Should().Be("myfighter.swf");
            entry["name"]!.ToString().Should().Be("My Fighter");
            entry["guid"]!.ToString().Should().NotBeNullOrWhiteSpace();
            doc["character"]!["myfighter"].Should().NotBeNull();
        }

        [Fact]
        public void PlaceOnSelect_AddsCharacterToTheLastRow()
        {
            var doc = new JObject();
            MappingsEditService.SetCssRows(doc, new[] { new[] { "mario" } });
            MappingsEditService.PlaceOnSelect(doc, true, "gojo", "expansions/gojo/gojo.swf", "Gojo");
            MappingsEditService.GetCssRows(doc)[0].Should().Equal("mario", "gojo");
            doc["character"]!["gojo"]!["file"]!.ToString().Should().Be("expansions/gojo/gojo.swf");
            doc["character"]!["gojo"]!["name"]!.ToString().Should().Be("Gojo");
        }

        [Fact]
        public void PlaceOnSelect_AddsStageWithoutDuplicating()
        {
            var doc = new JObject();
            MappingsEditService.SetSssNormalRows(doc, new[] { new[] { "battlefield" } });
            MappingsEditService.PlaceOnSelect(doc, false, "waitingroom", "expansions/stages/waitingroom/waitingroom.swf", "Waiting Room");
            MappingsEditService.PlaceOnSelect(doc, false, "waitingroom", "expansions/stages/waitingroom/waitingroom.swf", "Waiting Room");
            var flat = MappingsEditService.GetSssNormalRows(doc).SelectMany(r => r).ToList();
            flat.Should().Equal("battlefield", "waitingroom");
        }

        [Fact]
        public void SwfPackage_TellsACharacterFromAStage()
        {
            var character = System.Text.Encoding.ASCII.GetBytes("statsName jumpSpeed");
            var stage = System.Text.Encoding.ASCII.GetBytes("backgrounds list");
            var boxingRing = System.Text.Encoding.ASCII.GetBytes("statsName backgrounds");
            var steelDiver = System.Text.Encoding.ASCII.GetBytes("jumpSpeed backgrounds");
            SwfPackage.IsCharacter(character).Should().BeTrue();
            SwfPackage.IsStage(character).Should().BeFalse();
            SwfPackage.IsStage(stage).Should().BeTrue();
            SwfPackage.IsCharacter(stage).Should().BeFalse();
            SwfPackage.IsStage(boxingRing).Should().BeTrue();
            SwfPackage.IsCharacter(boxingRing).Should().BeFalse();
            SwfPackage.IsStage(steelDiver).Should().BeTrue();
            SwfPackage.IsCharacter(steelDiver).Should().BeFalse();
        }

        [Fact]
        public void EnsurePlainSwf_UnwrapsSsfPackage()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-plain-swf-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var swf = new byte[] { (byte)'F', (byte)'W', (byte)'S', 9, 21, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
                var packed = Path.Combine(root, "misc.ssf");
                File.WriteAllBytes(packed, DataFolderCompression.Wrap(swf));
                var plain = SwfPackage.EnsurePlainSwf(packed, root);
                plain.Should().EndWith("misc.swf");
                File.ReadAllBytes(plain)[0].Should().Be((byte)'F');
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void IsDataCharacterFile_OnlyMatchesDataCharacterFolder()
        {
            ShellCharacterCloneService.IsDataCharacterFile(@"C:\game\data\character\mario.swf").Should().BeTrue();
            ShellCharacterCloneService.IsDataCharacterFile(@"C:\game\data\character\packs\mario.ssf").Should().BeTrue();
            ShellCharacterCloneService.IsDataCharacterFile(@"C:\game\expansions\character\banjo\banjo.swf").Should().BeFalse();
            ShellCharacterCloneService.IsDataCharacterFile(@"C:\Downloads\banjokazooie.swf").Should().BeFalse();
        }

        [Fact]
        public void SetCssRows_RoundTrips()
        {
            var doc = new JObject();
            MappingsEditService.SetCssRows(doc, new[]
            {
                new[] { "mario", "luigi" },
                new[] { "random" }
            });
            var rows = MappingsEditService.GetCssRows(doc);
            rows.Should().HaveCount(2);
            rows[0].Should().Equal("mario", "luigi");
            MappingsEditService.FlattenSelectRowIds(doc["character_select_screen"]!["rows"])
                .Should().BeEquivalentTo("mario", "luigi", "random");
        }

        [Fact]
        public void DuplicateSpriteAndSymbolClass_ClonesDefineBitsAndUniqueSymbolClass()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterID=""35"" forceWriteAsLong=""true"">
      <bitmapFormat>5</bitmapFormat>
    </item>
<item type=""SymbolClassTag"" forceWriteAsLong=""true"">
      <tags>
        <item>35</item>
      </tags>
      <names>
        <item>congrats_mario</item>
      </names>
    </item>";
            var (next, touches) = MenuSlotService.DuplicateSpriteAndSymbolClass(
                xml, 35, 99, "congrats_mario", "congrats_clonemario", "mario", "clonemario", "characterID");
            touches.Should().BeGreaterThan(0);
            next.Should().Contain("<item>congrats_clonemario</item>");
            // Preserve FFDec attribute casing (characterID, not characterId).
            next.Should().Contain("characterID=\"99\"");
            // Must not alias donor chid for the new linkage.
            next.Should().Contain("<item>99</item>");
            Regex.Matches(next, @"<item>35</item>").Count.Should().Be(1);
        }

        [Fact]
        public void SanitizeId_StripsInvalid()
        {
            MenuSlotService.SanitizeId("My Fighter!").Should().Be("myfighter");
        }

        [Fact]
        public void AllocateUniqueGuid_SkipsExistingAcrossCategories()
        {
            var taken = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            var doc = new JObject
            {
                ["character"] = new JObject
                {
                    ["mario"] = new JObject { ["guid"] = taken }
                },
                ["stage"] = new JObject
                {
                    ["battlefield"] = new JObject { ["guid"] = "11111111-2222-3333-4444-555555555555" }
                },
                ["music"] = new JObject
                {
                    ["bgm_menu"] = new JObject { ["guid"] = "99999999-8888-7777-6666-555555555555" }
                }
            };

            var allocated = MappingsEditService.AllocateUniqueGuid(doc);
            allocated.Should().NotBeNullOrWhiteSpace();
            allocated.Should().NotBeEquivalentTo(taken);
            allocated.Should().NotBeEquivalentTo("11111111-2222-3333-4444-555555555555");
            allocated.Should().NotBeEquivalentTo("99999999-8888-7777-6666-555555555555");

            MappingsEditService.CollectExistingGuids(doc).Should().HaveCount(3);
            MappingsEditService.CollectExistingGuids(doc).Should().Contain(taken);
        }

        [Fact]
        public void UpsertEntry_AllocatesUniqueGuidWhenMissing()
        {
            var taken = "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee";
            var doc = new JObject
            {
                ["character"] = new JObject
                {
                    ["mario"] = new JObject { ["file"] = "mario.swf", ["guid"] = taken }
                }
            };
            var entry = MappingsEditService.UpsertEntry(doc, "character", "clonedee", "clonedee.swf", name: "Clone Dee");
            entry["guid"]!.ToString().Should().NotBeNullOrWhiteSpace();
            entry["guid"]!.ToString().Should().NotBeEquivalentTo(taken);
        }

        [Fact]
        public void RewriteSelectChipScript_RenamesClassAndId()
        {
            const string donor = @"package
{
   import flash.display.MovieClip;
   
   [Embed(source=""/_assets/assets.swf"", symbol=""symbol323"")]
   public dynamic class dedede_select extends MovieClip
   {
      public var characterID:String;
      public function dedede_select()
      {
         super();
         addFrameScript(0,this.frame1);
      }
      internal function frame1() : *
      {
         this.characterID = ""dedede"";
      }
   }
}
";
            var next = MenuSlotService.RewriteSelectChipScript(
                donor, "dedede_select", "clonedee_select", "dedede", "clonedee", "characterID");
            next.Should().Contain("class clonedee_select");
            next.Should().Contain("function clonedee_select(");
            next.Should().Contain("this.characterID = \"clonedee\"");
            next.Should().NotContain("dedede_select");
            next.Should().NotContain("[Embed");
        }

        [Fact]
        public void BuildDualPackageSelectScript_KeepsDonorAndAddsClone()
        {
            const string donor = @"package
{
   import flash.display.MovieClip;
   [Embed(source=""/_assets/assets.swf"", symbol=""symbol239"")]
   public dynamic class bandanadee_select extends MovieClip
   {
      public var characterID:String;
      public function bandanadee_select()
      {
         super();
         addFrameScript(0,this.frame1);
      }
      internal function frame1() : *
      {
         this.characterID = ""bandanadee"";
      }
   }
}
";
            var dual = MenuSlotService.BuildDualPackageSelectScript(
                donor + "\r\npackage { public dynamic class leftover_select {} }\r\n",
                "bandanadee_select", "clonedee_select", "bandanadee", "clonedee", "characterID");
            dual.Should().Contain("class bandanadee_select");
            dual.Should().Contain("this.characterID = \"bandanadee\"");
            dual.Should().Contain("class clonedee_select");
            dual.Should().Contain("this.characterID = \"clonedee\"");
            dual.Should().NotContain("leftover_select");
            // donor Embed kept; clone Embed stripped by RewriteSelectChipScript
            dual.Should().Contain("[Embed");
        }

        [Fact]
        public void EncodeDecodeMappingsBlob_RoundTrips()
        {
            const string key = MappingsEditService.MappingsMetadataKey;
            var json = "{\"character\":{\"mario\":{\"file\":\"mario.swf\"}}}";
            var encoded = DatMappingService.EncodeJsonBlob(json, key);
            var blob = System.Text.Encoding.UTF8.GetString(encoded);
            var decoded = DatMappingService.DecodeJsonBlob(blob, key);
            JObject.Parse(decoded)["character"]!["mario"]!["file"]!.ToString().Should().Be("mario.swf");
        }

        [Fact]
        public void InsertOnly_DoesNotReplaceExistingIds()
        {
            var rows = new List<List<string>> { new() { "mario", "sandbag", "luigi" } };
            MappingsEditService.AppendToRow(rows, 0, "myfighter");
            rows[0].Should().Equal("mario", "sandbag", "luigi", "myfighter");
            rows[0].Should().Contain("sandbag");
        }

        [Fact]
        public void InsertAfterId_MovesExistingIdInsteadOfDuplicating()
        {
            var rows = new List<List<string>>
            {
                new() { "mario", "luigi", "peach" },
                new() { "kirby", "dedede" }
            };
            MappingsEditService.InsertAfterId(rows, afterId: "kirby", id: "luigi");
            rows[0].Should().Equal("mario", "peach");
            rows[1].Should().Equal("kirby", "luigi", "dedede");
            rows.SelectMany(r => r).Count(x => x.Equals("luigi", StringComparison.OrdinalIgnoreCase))
                .Should().Be(1);
        }

        [Fact]
        public void EnsureUniqueRowIds_RemovesLaterDuplicates()
        {
            var rows = new List<List<string>>
            {
                new() { "mario", "luigi" },
                new() { "mario", "peach" }
            };
            MappingsEditService.EnsureUniqueRowIds(rows).Should().Be(1);
            rows.SelectMany(r => r).Should().Equal("mario", "luigi", "peach");
        }

        [Fact]
        public void FitSelectLayout_ShrinksWideGridsProportionally()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 42);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 42);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.x", -51);
            var wide = new List<List<string>>
            {
                Enumerable.Range(0, 18).Select(i => $"c{i}").ToList()
            };
            var warn = MappingsEditService.FitSelectLayout(doc, isCss: true, wide);
            warn.Should().NotBeNullOrWhiteSpace();
            var size = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width");
            size.Should().NotBeNull();
            size!.Value.Should().BeLessThan(42);
            size.Value.Should().Be(MappingsEditService.SuggestIconSize(18, rowCount: 1));
            MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.height")
                .Should().Be(size);
            var x = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.x");
            x.Should().Be(MappingsEditService.SuggestCssPositionX(18, size.Value));
        }

        [Fact]
        public void FitSelectLayout_SupportsFiftyColRow()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 42);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 42);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.x", -51);
            var wide = new List<List<string>>
            {
                Enumerable.Range(0, 50).Select(i => $"c{i}").ToList()
            };
            MappingsEditService.FitSelectLayout(doc, isCss: true, wide);
            var size = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width");
            size.Should().Be(12);
            var x = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.x");
            x.Should().Be(MappingsEditService.SuggestCssPositionX(50, 12));
            x!.Value.Should().Be(-100);
        }

        [Fact]
        public void FitSelectLayout_AutoXForThirtyNineByTwo()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 20);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 20);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.x", -51);
            var grid = new List<List<string>>
            {
                Enumerable.Range(0, 39).Select(i => $"a{i}").ToList(),
                Enumerable.Range(0, 10).Select(i => $"b{i}").ToList()
            };
            MappingsEditService.FitSelectLayout(doc, isCss: true, grid);
            var size = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width");
            size.Should().Be(15);
            var x = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.x");
            x.Should().Be(MappingsEditService.SuggestCssPositionX(39, 15));
            x!.Value.Should().NotBe(-51);
        }

        [Fact]
        public void FitSelectLayout_MaximizesUsingRowCount()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 12);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 12);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.position.x", -51);
            // Sparse 13×4: width ~46; height band 150/4=37 → cell 37.
            var grid = Enumerable.Range(0, 4)
                .Select(_ => Enumerable.Range(0, 13).Select(i => $"c{i}").ToList())
                .ToList();
            MappingsEditService.FitSelectLayout(doc, isCss: true, grid);
            var size = MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width");
            size.Should().Be(MappingsEditService.SuggestIconSize(13, 4));
            size!.Value.Should().Be(37);
            MappingsEditService.GetLayoutNumber(doc, "character_select_screen.position.x")
                .Should().Be(MappingsEditService.SuggestCssPositionX(13, 37));
        }

        [Fact]
        public void FitSelectLayout_FifteenRowsUsesHudBand()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 21);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 21);
            var grid = Enumerable.Range(0, 15)
                .Select(r => new List<string> { $"r{r}" })
                .ToList();
            MappingsEditService.FitSelectLayout(doc, isCss: true, grid);
            // availH 150 / 15 rows → cell 10
            MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width")
                .Should().Be(10);
            MappingsEditService.CssMaxRows.Should().Be(15);
        }

        [Fact]
        public void FitSelectLayout_TenByThirtyFillsWidthAndHeight()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 12);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 12);
            var grid = new List<List<string>>
            {
                Enumerable.Range(0, 30).Select(i => $"t{i}").ToList()
            };
            for (var r = 0; r < 9; r++)
                grid.Add(new List<string> { $"a{r}", $"b{r}" });
            MappingsEditService.FitSelectLayout(doc, isCss: true, grid);
            // 30 cols → 20; 10 rows in 150px → 15; cell 15
            MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width")
                .Should().Be(15);
        }

        [Fact]
        public void MaybeWarnAndNudgeLayout_DelegatesToFit()
        {
            var doc = new JObject();
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.width", 42);
            MappingsEditService.SetLayoutNumber(doc, "character_select_screen.icon_size.height", 42);
            var wide = new List<List<string>>
            {
                Enumerable.Range(0, 16).Select(i => $"c{i}").ToList()
            };
            var warn = MappingsEditService.MaybeWarnAndNudgeLayout(doc, isCss: true, wide);
            warn.Should().NotBeNullOrWhiteSpace();
            MappingsEditService.GetLayoutNumber(doc, "character_select_screen.icon_size.width")
                .Should().Be(MappingsEditService.SuggestIconSize(16, rowCount: 1));
        }

        [Fact]
        public async System.Threading.Tasks.Task SaveDocumentAsync_WritesMappingsJson()
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "ssf2mm-map-save-" + System.Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(System.IO.Path.Combine(root, "data", "misc"));
            try
            {
                var doc = new JObject
                {
                    ["character"] = new JObject
                    {
                        ["myfighter"] = new JObject { ["file"] = "myfighter.swf", ["guid"] = "g1" }
                    }
                };
                var svc = new MappingsEditService();
                await svc.SaveDocumentAsync(root, doc);
                var path = MappingsEditService.GetPlaintextPath(root);
                System.IO.File.Exists(path).Should().BeTrue();
                var loaded = await svc.LoadDocumentAsync(root, exportIfMissing: false);
                loaded["character"]!["myfighter"]!["file"]!.ToString().Should().Be("myfighter.swf");
            }
            finally
            {
                try { System.IO.Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
