using System.IO;
using System.Text.RegularExpressions;
using FluentAssertions;
using SSF2ModManager;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class CloneScriptAndLabelTests
    {
        [Fact]
        public void RewriteSelectChipScript_PrefixCongrats_RenamesClassWithoutCharacterId()
        {
            var donor = @"
package {
  public class congrats_dedede extends MovieClip {
    public function congrats_dedede() {
      super();
    }
  }
}
";
            var next = MenuSlotService.RewriteSelectChipScript(
                donor, "congrats_dedede", "congrats_clonededede", "dedede", "clonededede", "characterID");
            next.Should().Contain("class congrats_clonededede");
            next.Should().Contain("function congrats_clonededede(");
            next.Should().NotContain("class congrats_dedede");
        }

        [Fact]
        public void RewriteSelectChipScript_Select_SetsCharacterId()
        {
            var donor = @"
package {
  public class dedede_select extends MovieClip {
    public function dedede_select() {
      this.characterID = ""dedede"";
    }
  }
}
";
            var next = MenuSlotService.RewriteSelectChipScript(
                donor, "dedede_select", "clonededede_select", "dedede", "clonededede", "characterID");
            next.Should().Contain("class clonededede_select");
            next.Should().Contain("this.characterID = \"clonededede\"");
        }

        [Fact]
        public void BuildDualPackage_PrefixLinkages()
        {
            var donor = @"
package {
  public class congrats_dedede extends MovieClip {
    public function congrats_dedede() { super(); }
  }
}
";
            var dual = MenuSlotService.BuildDualPackageSelectScript(
                donor, "congrats_dedede", "congrats_clonededede", "dedede", "clonededede", "characterID");
            dual.Should().Contain("class congrats_dedede");
            dual.Should().Contain("class congrats_clonededede");
        }

        [Fact]
        public void BuildChipLabelTextFile_SplitsOnFirstSpace()
        {
            var body = MenuSlotService.BuildChipLabelTextFile("Clone Dedede");
            body.Should().Contain("CLONE");
            body.Should().Contain("--- RECORDSEPARATOR ---");
            body.Should().Contain("DEDEDE");
        }

        [Fact]
        public void GetChipLabelLines_UsesFirstAndLastForLongNames()
        {
            MenuSlotService.GetChipLabelLines("Clone Chibi Robo")
                .Should().Equal("CLONE", "ROBO");
            MenuSlotService.GetChipLabelLines("Clone Samus")
                .Should().Equal("CLONE", "SAMUS");
        }

        [Fact]
        public void BuildSingleLineChipLabel_NeverUsesRecordSeparator()
        {
            var body = MenuSlotService.BuildSingleLineChipLabel("Clone Samus");
            body.Should().Be("CLONE-SAMUS"); // 11 chars spaced → hyphen
            body.Should().NotContain("RECORDSEPARATOR");
            MenuSlotService.BuildSingleLineChipLabel("Clone Fox").Should().Be("CLONE FOX");
        }

        [Fact]
        public void IsBrokenBitmapScale_OnlyFlagsTinyOrInvalid()
        {
            MenuSlotService.IsBrokenBitmapScale("13.209").Should().BeFalse();
            MenuSlotService.IsBrokenBitmapScale("20.0").Should().BeFalse();
            MenuSlotService.IsBrokenBitmapScale("0").Should().BeTrue();
            MenuSlotService.IsBrokenBitmapScale("0.5").Should().BeTrue();
            MenuSlotService.IsBrokenBitmapScale("").Should().BeTrue();
        }

        [Fact]
        public void NormalizeBitmapMatrices_PreservesCalibratedScale()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""528"" bitmapWidth=""134"" bitmapHeight=""211""/>
<item type=""DefineShapeTag"" shapeId=""529"">
  <shapeBounds type=""RECT"" Xmax=""440"" Xmin=""-440"" Ymax=""440"" Ymin=""-440"" nbits=""10""/>
  <item type=""FILLSTYLE"" bitmapId=""528"" fillStyleType=""64"">
    <bitmapMatrix type=""MATRIX"" scaleX=""13.209"" scaleY=""13.209"" translateX=""-991"" translateY=""-364""/>
  </item>
</item>
<item type=""DefineSpriteTag"" spriteId=""519"">
  <subTags><item type=""PlaceObject2Tag"" characterId=""529"" depth=""5""/></subTags>
</item>";
            var map = new Dictionary<string, int> { ["clonesamus_select"] = 519 };
            var (next, touches) = MenuSlotService.NormalizeBitmapMatricesForTargets(
                xml, map, new[] { ("clonesamus_select", "528") });
            touches.Should().Be(0);
            next.Should().Contain("scaleX=\"13.209\"");
        }

        [Fact]
        public void TryComputeBitmapFillWindow_SamusSelectUpperBand()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""528"" bitmapWidth=""134"" bitmapHeight=""211""/>
<item type=""DefineShapeTag"" shapeId=""529"">
  <shapeBounds type=""RECT"" Xmax=""440"" Xmin=""-440"" Ymax=""440"" Ymin=""-440"" nbits=""10""/>
  <item type=""FILLSTYLE"" bitmapId=""528"" fillStyleType=""64"">
    <bitmapMatrix type=""MATRIX"" scaleX=""13.209"" scaleY=""13.209"" translateX=""-991"" translateY=""-364""/>
  </item>
</item>
<item type=""DefineSpriteTag"" spriteId=""519"">
  <subTags><item type=""PlaceObject2Tag"" characterId=""529"" depth=""5""/></subTags>
</item>";
            var ok = MenuSlotService.TryComputeBitmapFillWindow(
                xml, spriteId: 519, bitsId: 528, bitmapW: 134, bitmapH: 211,
                out var winX, out var winY, out var winW, out var winH);
            ok.Should().BeTrue();
            winX.Should().BeInRange(35, 50);
            winY.Should().Be(0);
            winW.Should().BeInRange(55, 75);
            winH.Should().BeInRange(55, 75);
        }

        [Fact]
        public void SingleLineSelectPrep_ScalesAndCentersPlaceObject()
        {
            // Donor Samus bounds + real CLONE-SAMUS advances (left-aligned until recenter).
            var xml = @"
<item type=""DefineTextTag"" characterID=""270"" forceWriteAsLong=""true"">
  <textBounds type=""RECT"" Xmax=""1006"" Xmin=""24"" Ymax=""231"" Ymin=""4"" nbits=""11""/>
  <textMatrix type=""MATRIX"" translateX=""23"" translateY=""0""/>
  <textRecords>
    <item type=""TEXTRECORD"" fontId=""18"" styleFlagsHasYOffset=""true"" textHeight=""220"" yOffset=""200"">
      <glyphEntries>
        <item type=""GLYPHENTRY"" glyphAdvance=""148"" glyphIndex=""35""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""119"" glyphIndex=""44""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""151"" glyphIndex=""47""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""152"" glyphIndex=""46""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""135"" glyphIndex=""37""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""67"" glyphIndex=""13""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""136"" glyphIndex=""51""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""144"" glyphIndex=""33""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""199"" glyphIndex=""45""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""153"" glyphIndex=""53""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""127"" glyphIndex=""51""/>
      </glyphEntries>
    </item>
  </textRecords>
</item>
<item type=""DefineSpriteTag"" spriteId=""271"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""270"" depth=""6"">
      <matrix type=""MATRIX"" hasScale=""true"" scaleX=""0.7999878"" scaleY=""0.7999878"" translateX=""0"" translateY=""-408""/>
    </item>
  </subTags>
</item>";
            var label = MenuSlotService.BuildSingleLineChipLabel("Clone Samus");
            label.Should().Be("CLONE-SAMUS");

            var (next, n) = MenuSlotService.ScaleSelectChipTextPlaceObjects(
                xml, spriteId: 271, textIds: new[] { 270 }, label);
            n.Should().BeGreaterThan(0);
            // textMatrix recentered: 407 - 1531/2 = -358.5 → -358
            Regex.Match(next, @"textMatrix[^>]*translateX=""(-?\d+)""")
                .Groups[1].Value.Should().Be("-358");
            next.Should().Contain("translateY=\"-408\"");
            var scaleX = double.Parse(
                Regex.Match(next, @"scaleX=""([^""]+)""").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            var scaleY = double.Parse(
                Regex.Match(next, @"scaleY=""([^""]+)""").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            scaleX.Should().BeApproximately(1342 * 0.58 / 1531.0, 0.01); // ~0.51
            scaleY.Should().BeApproximately(0.8, 0.001); // stock vertical stretch
            var tx = double.Parse(
                Regex.Match(next, @"PlaceObject2Tag[\s\S]*?translateX=""([^""]+)""").Groups[1].Value,
                System.Globalization.CultureInfo.InvariantCulture);
            tx.Should().BeApproximately(-407 * scaleX, 2);
        }

        [Fact]
        public void IsCssSelectLinkage_ExcludesCharselectAndStageSelect()
        {
            MenuSlotService.IsCssSelectLinkage("clonesamus_select").Should().BeTrue();
            MenuSlotService.IsCssSelectLinkage("clonesamus_charselect").Should().BeFalse();
            MenuSlotService.IsCssSelectLinkage("battlefield_stage_select").Should().BeFalse();
        }

        [Fact]
        public void ParseSymbolClassMapFromXml_ReadsTagsAndNames()
        {
            var xml = @"
<item type=""SymbolClassTag"">
  <tags>
    <item>271</item>
    <item>519</item>
  </tags>
  <names>
    <item>samus_select</item>
    <item>clonesamus_select</item>
  </names>
</item>";
            var map = MenuSlotService.ParseSymbolClassMapFromXml(xml);
            map.Should().ContainKey("samus_select").WhoseValue.Should().Be(271);
            map.Should().ContainKey("clonesamus_select").WhoseValue.Should().Be(519);
        }

        [Fact]
        public void ConvertDefineTextTagToEditText_UsesChipLocalNameplateBounds()
        {
            var defineText = @"
<item type=""DefineTextTag"" characterID=""512"" forceWriteAsLong=""true"">
      <textBounds type=""RECT"" Xmax=""643"" Xmin=""380"" Ymax=""231"" Ymin=""4"" nbits=""11""/>
      <textRecords>
        <item type=""TEXTRECORD"" fontId=""18"" textHeight=""220"">
          <textColor type=""RGB"" blue=""255"" green=""255"" red=""255""/>
        </item>
      </textRecords>
    </item>";
            var edit = MenuSlotService.ConvertDefineTextTagToEditText(defineText, "·");
            edit.Should().Contain("DefineEditTextTag");
            edit.Should().Contain("characterID=\"512\"");
            edit.Should().Contain("fontId=\"18\"");
            edit.Should().Contain("readOnly=\"true\"");
            edit.Should().Contain("initialText=\"·\"");
            edit.Should().Contain("Xmin=\"-440\"");
            edit.Should().Contain("Xmax=\"440\"");
            edit.Should().Contain("Ymin=\"-440\"");
            edit.Should().Contain("Ymax=\"-200\"");
        }

        [Fact]
        public void ConvertPoolSelectNameplatesToEditText_IsIdempotent()
        {
            var xml = @"
<item type=""SymbolClassTag"">
  <tags><item>511</item></tags>
  <names><item>mm_c_000_select</item></names>
</item>
<item type=""DefineTextTag"" characterID=""512"" forceWriteAsLong=""true"">
      <textBounds type=""RECT"" Xmax=""643"" Xmin=""380"" Ymax=""231"" Ymin=""4"" nbits=""11""/>
      <textRecords>
        <item type=""TEXTRECORD"" fontId=""18"" textHeight=""220"">
          <textColor type=""RGB"" blue=""255"" green=""255"" red=""255""/>
        </item>
      </textRecords>
    </item>
<item type=""DefineSpriteTag"" spriteId=""511"" forceWriteAsLong=""true"" frameCount=""1"" hasEndTag=""true"">
      <subTags>
        <item type=""PlaceObject2Tag"" characterId=""512"" depth=""6"" forceWriteAsLong=""false"" placeFlagHasCharacter=""true"" placeFlagHasClipActions=""false"" placeFlagHasClipDepth=""false"" placeFlagHasColorTransform=""false"" placeFlagHasMatrix=""true"" placeFlagHasName=""false"" placeFlagHasRatio=""false"" placeFlagMove=""false"">
          <matrix type=""MATRIX"" hasRotate=""false"" hasScale=""true"" nRotateBits=""0"" nScaleBits=""17"" nTranslateBits=""10"" scaleX=""0.8"" scaleY=""0.8"" translateX=""-326"" translateY=""-408""/>
        </item>
        <item type=""ShowFrameTag"" forceWriteAsLong=""false""/>
      </subTags>
    </item>";
            var map = MenuSlotService.ParseSymbolClassMapFromXml(xml);
            var (once, touches) = MenuSlotService.ConvertPoolSelectNameplatesToEditText(
                xml, map, new[] { "mm_c_000_select" });
            touches.Should().BeGreaterThan(0);
            once.Should().Contain("DefineEditTextTag");
            once.Should().NotContain("DefineTextTag");
            once.Should().Contain("translateY=\"0\"");
            once.Should().NotContain("translateY=\"-408\"");
            var (twice, touches2) = MenuSlotService.ConvertPoolSelectNameplatesToEditText(
                once, map, new[] { "mm_c_000_select" });
            touches2.Should().Be(0);
            twice.Should().Be(once);
        }

        [Fact]
        public void ConvertPoolSelectNameplatesToEditText_RefreshesLongFfdecEditTextOpenTags()
        {
            // FFDec emits ~380+ chars after characterID — FindCharacterTagOpen must still match.
            var xml = @"
<item type=""SymbolClassTag"">
  <tags><item>511</item></tags>
  <names><item>mm_c_000_select</item></names>
</item>
<item type=""DefineEditTextTag"" align=""2"" autoSize=""false"" border=""false"" characterID=""512"" fontHeight=""220"" fontId=""18"" forceWriteAsLong=""true"" hasFont=""true"" hasFontClass=""false"" hasLayout=""true"" hasMaxLength=""false"" hasText=""true"" hasTextColor=""true"" html=""false"" indent=""0"" initialText=""·"" leading=""0"" leftMargin=""0"" multiline=""false"" noSelect=""true"" password=""false"" readOnly=""true"" rightMargin=""0"" useOutlines=""true"" variableName="""" wasStatic=""true"" wordWrap=""false"">
      <bounds type=""RECT"" Xmax=""440"" Xmin=""-440"" Ymax=""-220"" Ymin=""-440"" nbits=""10""/>
      <textColor type=""RGBA"" alpha=""255"" blue=""255"" green=""255"" red=""255""/>
    </item>
<item type=""DefineSpriteTag"" spriteId=""511"" forceWriteAsLong=""true"" frameCount=""1"" hasEndTag=""true"">
      <subTags>
        <item type=""PlaceObject2Tag"" characterId=""512"" depth=""6"" forceWriteAsLong=""false"" placeFlagHasCharacter=""true"" placeFlagHasClipActions=""false"" placeFlagHasClipDepth=""false"" placeFlagHasColorTransform=""false"" placeFlagHasMatrix=""true"" placeFlagHasName=""false"" placeFlagHasRatio=""false"" placeFlagMove=""false"">
          <matrix type=""MATRIX"" hasRotate=""false"" hasScale=""true"" nRotateBits=""0"" nScaleBits=""17"" nTranslateBits=""10"" scaleX=""0.8"" scaleY=""0.8"" translateX=""-326"" translateY=""-408""/>
        </item>
        <item type=""ShowFrameTag"" forceWriteAsLong=""false""/>
      </subTags>
    </item>";
            var map = MenuSlotService.ParseSymbolClassMapFromXml(xml);
            var (next, touches) = MenuSlotService.ConvertPoolSelectNameplatesToEditText(
                xml, map, new[] { "mm_c_000_select" });
            touches.Should().BeGreaterThan(0);
            next.Should().Contain("fontHeight=\"200\"");
            next.Should().Contain("Ymax=\"-200\"");
            next.Should().Contain("translateY=\"0\"");
            next.Should().NotContain("translateY=\"-408\"");

            // Round-tripped initialText (�) must not force another rewrite.
            var roundTripped = next.Replace("initialText=\"·\"", "initialText=\"�\"");
            var (_, touches2) = MenuSlotService.ConvertPoolSelectNameplatesToEditText(
                roundTripped, map, new[] { "mm_c_000_select" });
            touches2.Should().Be(0);
        }

        [Fact]
        public void ComputeSelectChipTextScale_MatchesGanondorfBarWidth()
        {
            MenuSlotService.ComputeSelectChipTextScaleX("GANONDORF", advanceSum: 1342)
                .Should().BeApproximately(0.58, 0.01);
            var s = MenuSlotService.ComputeSelectChipTextScaleX("CLONE-SAMUS", advanceSum: 1531);
            s.Should().BeApproximately(1342 * 0.58 / 1531.0, 0.01);
            MenuSlotService.ComputeSelectChipTextScaleY().Should().Be(0.8);
            MenuSlotService.ComputeSelectChipTextScaleX("SAMUS", advanceSum: 769)
                .Should().BeApproximately(0.8, 0.01); // capped
        }

        [Fact]
        public void ComputeSelectChipTextTranslateX_CentersAtLocal407()
        {
            MenuSlotService.ComputeSelectChipTextTranslateX("GANONDORF", 0.58)
                .Should().BeApproximately(-407 * 0.58, 1);
            MenuSlotService.ComputeSelectChipTextTranslateX("SAMUS", 0.8)
                .Should().BeApproximately(-407 * 0.8, 1);
        }

        [Fact]
        public void RecenterSelectChipDefineText_ExpandsDonorBounds()
        {
            var xml = @"
<item type=""DefineTextTag"" characterID=""520"">
  <textBounds type=""RECT"" Xmax=""1006"" Xmin=""24"" Ymax=""231"" Ymin=""4"" nbits=""11""/>
  <textMatrix type=""MATRIX"" translateX=""23"" translateY=""0""/>
  <textRecords>
    <item type=""TEXTRECORD"">
      <glyphEntries>
        <item type=""GLYPHENTRY"" glyphAdvance=""100""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""200""/>
        <item type=""GLYPHENTRY"" glyphAdvance=""300""/>
      </glyphEntries>
    </item>
  </textRecords>
</item>";
            var (next, n) = MenuSlotService.RecenterSelectChipDefineText(xml, 520);
            n.Should().Be(1);
            // 407 - 600/2 = 107
            next.Should().Contain("translateX=\"107\"");
            next.Should().Contain("Xmin=\"97\"");
            next.Should().Contain("Xmax=\"927\""); // 107+600+220
        }

        [Fact]
        public void BuildDeltaScriptImportTree_CopiesOnlyListedFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-delta-test-" + Guid.NewGuid().ToString("N"));
            var delta = root + "-out";
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root, "keep.as"), "keep");
                File.WriteAllText(Path.Combine(root, "skip.as"), "skip");
                MenuSlotService.BuildDeltaScriptImportTree(root, delta, new[] { "keep.as" });
                File.Exists(Path.Combine(delta, "keep.as")).Should().BeTrue();
                File.Exists(Path.Combine(delta, "skip.as")).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
                try { Directory.Delete(delta, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void BuildChipLabelTextFile_TwoLineForDonkeyKongStyle()
        {
            var body = MenuSlotService.BuildChipLabelTextFile("Clone Samus");
            body.Should().Contain("RECORDSEPARATOR");
            body.Should().Contain("CLONE");
            body.Should().Contain("SAMUS");
        }

        [Fact]
        public void FindTextCharacterIdsInSprite_FindsDefineText()
        {
            var xml = @"
<item type=""DefineTextTag"" characterID=""322"" forceWriteAsLong=""true""></item>
<item type=""DefineSpriteTag"" spriteId=""323"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""322"" depth=""6""/>
  </subTags>
</item>";
            var ids = MenuSlotService.FindTextCharacterIdsInSprite(xml, 323);
            ids.Should().Contain(322);
        }

        [Theory]
        [InlineData(true, false, true, false)]
        [InlineData(false, true, false, true)]
        [InlineData(true, true, true, false)]
        [InlineData(false, false, true, false)]
        public void ApplyCssRowCheckboxMutualExclusion(
            bool? lastIn, bool? newIn, bool expectLast, bool expectNew)
        {
            MainWindow.ApplyCssRowCheckboxMutualExclusion(lastIn, newIn, out var last, out var neu);
            last.Should().Be(expectLast);
            neu.Should().Be(expectNew);
        }

        [Fact]
        public void AppendMisc2Movieclips_AddsMissingNames()
        {
            const string asText = @"
register(""resources"",{
  ""movieclips"":[""mario_stock"",""mario_charselect"",""Kirby_Symbol""],
  ""sounds"":[]
});
";
            var next = MenuSlotService.AppendMisc2Movieclips(
                asText, new[] { "clonededede_stock", "clonededede_charselect", "mario_stock" });
            next.Should().Contain("clonededede_stock");
            next.Should().Contain("clonededede_charselect");
            // Existing name not duplicated
            next.Split("mario_stock", StringSplitOptions.None).Length.Should().Be(2);
        }

        [Theory]
        [InlineData("Kirby_Symbol", true)]
        [InlineData("piticon", true)]
        [InlineData("smash_symbol", true)]
        [InlineData("clonededede_charselect", false)]
        [InlineData("dedede_stock", false)]
        [InlineData("stagePreviewer", false)]
        public void IsSeriesSymbolLinkageName_FiltersPaSlots(string name, bool expect) =>
            MenuSlotService.IsSeriesSymbolLinkageName(name).Should().Be(expect);

        [Fact]
        public void TryParseDefineBitsExportId_RejectsSpriteFrames()
        {
            CharacterAssetService.TryParseDefineBitsExportId(@"C:\tmp\sprites\foo\1.png").Should().BeNull();
            CharacterAssetService.TryParseDefineBitsExportId(@"C:\tmp\35.png").Should().BeNull();
            CharacterAssetService.TryParseDefineBitsExportId(@"C:\tmp\DefineBitsLossless_35_x.png")
                .Should().Be("35");
            CharacterAssetService.TryParseDefineBitsExportId(@"C:\tmp\645_congrats.png")
                .Should().Be("645");
        }

        [Fact]
        public void FindLargestNestedBitsIdInXml_PicksBiggestTag()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""10"" bitmapWidth=""10"" bitmapHeight=""10"">SMALL</item>
<item type=""DefineBitsLossless2Tag"" characterId=""20"" bitmapWidth=""100"" bitmapHeight=""100"">LARGE</item>
<item type=""DefineSpriteTag"" spriteId=""100"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""10"" depth=""1""/>
    <item type=""PlaceObject2Tag"" characterId=""20"" depth=""2""/>
  </subTags>
</item>";
            MenuSlotService.FindLargestNestedBitsIdInXml(xml, 100).Should().Be(20);
        }

        [Fact]
        public void FindLargestNestedBitsIdInXml_FindsShapeBitmapFill()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""324"" bitmapWidth=""164"" bitmapHeight=""150"">PA</item>
<item type=""DefineShapeTag"" shapeId=""321"">
  <item type=""FILLSTYLE"" bitmapId=""324"" fillStyleType=""65""/>
</item>
<item type=""DefineSpriteTag"" spriteId=""519"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""321"" depth=""4""></item>
    <item type=""ShowFrameTag""/>
  </subTags>
</item>";
            MenuSlotService.FindLargestNestedBitsIdInXml(xml, 519).Should().Be(324);
        }

        [Fact]
        public void FindLargestNestedBitsIdInXml_PrefersHighestDepthPlacedBits()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""52"" bitmapWidth=""164"" bitmapHeight=""150"">SHADOW</item>
<item type=""DefineBitsLossless2Tag"" characterId=""53"" bitmapWidth=""164"" bitmapHeight=""150"">ART</item>
<item type=""DefineShapeTag"" shapeId=""50"">
  <item type=""FILLSTYLE"" bitmapId=""52"" fillStyleType=""65""/>
</item>
<item type=""DefineSpriteTag"" spriteId=""54"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""50"" depth=""3""></item>
    <item type=""PlaceObject2Tag"" characterId=""53"" depth=""4""></item>
  </subTags>
</item>";
            MenuSlotService.FindLargestNestedBitsIdInXml(xml, 54).Should().Be(53);
            MenuSlotService.FindCompanionBitsIdsInXml(xml, 54, 53).Should().Contain(52);
        }

        [Fact]
        public void RetargetStockSpriteToStock0InXml_PointsPlaceObjectAtStock0()
        {
            var xml = @"
<item type=""DefineBitsLossless2Tag"" characterId=""10"" bitmapWidth=""32"" bitmapHeight=""32""/>
<item type=""DefineShapeTag"" shapeId=""99""/>
<item type=""DefineSpriteTag"" spriteId=""20"">
  <subTags>
    <item type=""PlaceObject2Tag"" characterId=""99"" depth=""1""/>
  </subTags>
</item>
<item type=""SymbolClassTag"">
  <tags><item>20</item><item>10</item></tags>
  <names><item>clonesamus_stock</item><item>clonesamus_stock0</item></names>
</item>";
            // Minimal SymbolClass parsing may not match — use linkage map directly.
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            {
                ["clonesamus_stock"] = 20,
                ["clonesamus_stock0"] = 10
            };
            var (next, touched) = MenuSlotService.RetargetStockSpriteToStock0InXml(xml, map, "clonesamus");
            touched.Should().BeTrue();
            next.Should().Contain("characterId=\"10\"");
        }
    }
}
