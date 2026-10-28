using System.IO;
using System.Xml.Linq;
using FluentAssertions;
using FlaStepWizard;
using Xunit;

namespace SSF2ModManager.Tests.Services;

public class FlaInventoryTests
{
    [Fact]
    public void FromLayers_CountsBoxesUnnamedAndLibs()
    {
        var layers = XElement.Parse("""
            <layers>
              <DOMLayer name="HitBox"><frames>
                <DOMFrame index="0" duration="10"><elements>
                  <DOMSymbolInstance name="hitBox" libraryItemName="Symbol 353"/>
                </elements></DOMFrame>
                <DOMFrame index="10" duration="118"><elements/></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="Layer 6"><frames>
                <DOMFrame index="127"><elements>
                  <DOMSymbolInstance libraryItemName="Symbol 1573"/>
                </elements></DOMFrame>
              </frames></DOMLayer>
            </layers>
            """);
        var clip = FlaInventory.FromLayers("B", layers);
        clip.MaxFrame.Should().Be(128);
        clip.Unnamed.Should().Be(1);
        clip.Libs["Symbol 1573"].Should().Be(1);
        clip.Names["hitBox"].Should().Be(10);
        clip.BoxFamilies["HitBox"].Should().Be(10);
        clip.BoxPlacements["0|hitBox|Symbol 353||"].Should().Be(1);
        clip.BoxPlacements["9|hitBox|Symbol 353||"].Should().Be(1);
        clip.BoxPlacements.ContainsKey("10|hitBox|Symbol 353||").Should().BeFalse();
        clip.Layers.Should().Equal("HitBox", "Layer 6");
    }

    [Fact]
    public void FromLayers_CountsHeldTailWhenLastBoxKeyEndsBeforeClip()
    {
        var layers = XElement.Parse("""
            <layers>
              <DOMLayer name="AttackBox"><frames>
                <DOMFrame index="6" duration="3"><elements>
                  <DOMSymbolInstance name="attackBox" libraryItemName="Symbol 353">
                    <matrix><Matrix tx="53.6" ty="40.65"/></matrix>
                  </DOMSymbolInstance>
                </elements></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="cloney"><frames>
                <DOMFrame index="0" duration="16"><elements>
                  <DOMBitmapInstance libraryItemName="gdtilt 0.png"/>
                </elements></DOMFrame>
              </frames></DOMLayer>
            </layers>
            """);
        var clip = FlaInventory.FromLayers("Crouch_attack", layers);
        clip.BoxPlacements["6|attackBox|Symbol 353|53.6|40.65"].Should().Be(1);
        clip.BoxPlacements["8|attackBox|Symbol 353|53.6|40.65"].Should().Be(1);
        clip.BoxPlacements["9|attackBox|Symbol 353|53.6|40.65"].Should().Be(1);
        clip.BoxPlacements["15|attackBox|Symbol 353|53.6|40.65"].Should().Be(1);
        clip.Names["attackBox"].Should().Be(10);
    }

    [Fact]
    public void Diff_ReportsHeldAttackBoxTailAsExtraPlacement()
    {
        var beforeLayers = XElement.Parse("""
            <layers>
              <DOMLayer name="Layer 18"><frames>
                <DOMFrame index="6" duration="3"><elements>
                  <DOMSymbolInstance name="attackBox" libraryItemName="Symbol 353">
                    <matrix><Matrix tx="53.6" ty="40.65"/></matrix>
                  </DOMSymbolInstance>
                </elements></DOMFrame>
                <DOMFrame index="9" duration="7"><elements/></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="cloney"><frames>
                <DOMFrame index="0" duration="16"><elements>
                  <DOMBitmapInstance libraryItemName="gdtilt 0.png"/>
                </elements></DOMFrame>
              </frames></DOMLayer>
            </layers>
            """);
        var afterLayers = XElement.Parse("""
            <layers>
              <DOMLayer name="AttackBox"><frames>
                <DOMFrame index="6" duration="3"><elements>
                  <DOMSymbolInstance name="attackBox" libraryItemName="Symbol 353">
                    <matrix><Matrix tx="53.6" ty="40.65"/></matrix>
                  </DOMSymbolInstance>
                </elements></DOMFrame>
              </frames></DOMLayer>
              <DOMLayer name="cloney"><frames>
                <DOMFrame index="0" duration="16"><elements>
                  <DOMBitmapInstance libraryItemName="gdtilt 0.png"/>
                </elements></DOMFrame>
              </frames></DOMLayer>
            </layers>
            """);
        var before = new FlaInventory.Report();
        before.Clips["LIBRARY/Animations/Crouch_attack/Crouch_attack.xml"] = FlaInventory.FromLayers("c", beforeLayers);
        var after = new FlaInventory.Report();
        after.Clips["LIBRARY/Animations/Crouch_attack/Crouch_attack.xml"] = FlaInventory.FromLayers("c", afterLayers);
        var issues = FlaInventory.Diff(before, after);
        issues.Should().Contain(i => i.Contains("extra box") && i.Contains("9|attackBox"));
    }

    [Fact]
    public void Diff_ReportsDroppedOverlayLib()
    {
        var before = new FlaInventory.Report();
        before.Clips["LIBRARY/Animations/B.xml"] = new FlaInventory.Clip
        {
            Libs = { ["Symbol 1573"] = 1, ["Symbol 353"] = 10 },
            Names = { ["hitBox"] = 5 },
            BoxFamilies = { ["HitBox"] = 5 }
        };
        var after = new FlaInventory.Report();
        after.Clips["LIBRARY/Animations/B.xml"] = new FlaInventory.Clip
        {
            Libs = { ["Symbol 353"] = 10 },
            Names = { ["hitBox"] = 5 },
            BoxFamilies = { ["HitBox"] = 5 }
        };
        var issues = FlaInventory.Diff(before, after);
        issues.Should().Contain(i => i.Contains("Symbol 1573"));
    }

    [Fact]
    public void Diff_ReportsExtraBoxPlacement()
    {
        var before = new FlaInventory.Report();
        before.Clips["A_air_up"] = new FlaInventory.Clip
        {
            BoxPlacements = { ["15|hitBox4|Symbol 353|23.0|67.5"] = 0 }
        };
        var after = new FlaInventory.Report();
        after.Clips["A_air_up"] = new FlaInventory.Clip
        {
            BoxPlacements = { ["15|hitBox4|Symbol 353|23.0|67.5"] = 1 }
        };
        var issues = FlaInventory.Diff(before, after);
        issues.Should().Contain(i => i.Contains("extra box") && i.Contains("15|hitBox4"));
    }

    [Fact]
    public void Diff_ReportsDroppedBoxNameCount()
    {
        var before = new FlaInventory.Report();
        before.Clips["B"] = new FlaInventory.Clip
        {
            Names = { ["hitBox"] = 33 },
            BoxFamilies = { ["HitBox"] = 33 }
        };
        var after = new FlaInventory.Report();
        after.Clips["B"] = new FlaInventory.Clip
        {
            Names = { ["hitBox"] = 10 },
            BoxFamilies = { ["HitBox"] = 10 }
        };
        var issues = FlaInventory.Diff(before, after);
        issues.Should().Contain(i => i.Contains("hitBox"));
        issues.Should().Contain(i => i.Contains("family HitBox"));
    }

    [Fact]
    public void CloneyInventories_Step1VsNext_NoDrops_IfPresent()
    {
        var step1Fla = CloneyFla("cloney-step1.fla");
        var nextFla = CloneyFla("cloney-next.fla");
        if (step1Fla == null || nextFla == null)
            return;
        var issues = FlaInventory.Diff(FlaInventory.FromFla(step1Fla), FlaInventory.FromFla(nextFla));
        issues.Should().BeEmpty("wizard steps must not drop, extra, or misplace boxes/libs");
    }

    [Fact]
    public void CloneyStep1_BInventory_HasOverlaySymbol1573_IfFlaPresent()
    {
        var fla = CloneyFla("cloney-step1.fla");
        if (fla == null)
            return;
        var b = FlaInventory.FromFla(fla).Clips
            .FirstOrDefault(kv => kv.Key.Replace('\\', '/').EndsWith("/B.xml", StringComparison.OrdinalIgnoreCase));
        b.Value.Should().NotBeNull("B movieclip should be in the FLA");
        b.Value!.Libs.Keys.Should().Contain(k => k.Contains("1573"),
            "B overlay FX must be in the step-1 inventory so later steps can be diffed");
    }

    private static string? CloneyFla(string fileName)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var path = Path.Combine(dir.FullName,
                "SSF2 Mod Manager", "SSF2 Shell", "build", "data", "character", "expansions", "cloney", "FLA", fileName);
            if (File.Exists(path))
                return path;
            dir = dir.Parent;
        }
        return null;
    }
}
