using System.IO;
using System.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class CharacterAssetServiceTests
    {
        [Fact]
        public void GetAssetManifest_IncludesCoreSlots()
        {
            var manifest = CharacterAssetService.GetAssetManifest();
            Assert.Contains(manifest, s => s.Key == "select" && s.IsEditable);
            Assert.Contains(manifest, s => s.Key == "charselect" && s.IsEditable);
            Assert.Contains(manifest, s => s.Key == "stock" && s.IsEditable);
        }

        [Fact]
        public void GetAssetManifest_CongratsEditable_WinRemoved()
        {
            var manifest = CharacterAssetService.GetAssetManifest();
            Assert.Contains(manifest, s => s.Key == "classic" && s.IsEditable);
            Assert.Contains(manifest, s => s.Key == "congrats" && s.IsEditable);
            Assert.Contains(manifest, s => s.Key == "allstar_congrats" && s.IsEditable);
            Assert.DoesNotContain(manifest, s => s.Key == "win");
            Assert.Contains(manifest, s => s.Key == "costumes" && !s.IsEditable);
        }

        [Theory]
        [InlineData("select", "bandanadee", "bandanadee_select")]
        [InlineData("charselect", "mario", "mario_charselect")]
        [InlineData("stock", "clonedee", "clonedee_stock")]
        [InlineData("congrats", "bandanadee", "congrats_bandanadee")]
        [InlineData("allstar_congrats", "mario", "allstar_congrats_mario")]
        public void LinkageFor_MatchesInventory(string key, string id, string expected)
        {
            var def = CharacterAssetService.GetAssetManifest().First(s => s.Key == key);
            Assert.Equal(expected, def.LinkageFor(id));
        }

        [Fact]
        public void NameMatchesLinkage_DoesNotConfuseSelectWithCharselect()
        {
            Assert.True(CharacterAssetService.NameMatchesLinkage("DefineSprite_10_mario_select", "mario_select"));
            Assert.True(CharacterAssetService.NameMatchesLinkage("DefineSprite_11_mario_charselect", "mario_charselect"));
            Assert.False(CharacterAssetService.NameMatchesLinkage("DefineSprite_11_mario_charselect", "mario_select"));
            Assert.False(CharacterAssetService.NameMatchesLinkage("mario_stock0", "mario_stock"));
            Assert.True(CharacterAssetService.NameMatchesLinkage("mario_stock", "mario_stock"));
        }

        [Fact]
        public void PickSpriteFramePng_PrefersLastSubstantive_NotLargest()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-framepick-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                // Frame 1 tiny, frame 2 medium (art), frame 3 huge "shadow"
                var f1 = Path.Combine(dir, "1.png");
                var f2 = Path.Combine(dir, "2.png");
                var f3 = Path.Combine(dir, "3.png");
                File.WriteAllBytes(f1, new byte[100]);
                File.WriteAllBytes(f2, new byte[800]);
                File.WriteAllBytes(f3, new byte[5000]);

                var picked = CharacterAssetService.PickSpriteFramePng(
                    new[] { f1, f2, f3 }, preferFirstFrame: false);
                Assert.Equal(f3, picked); // last substantive (>=500) is f3

                // When middle is last substantive under a threshold scenario:
                File.WriteAllBytes(f3, new byte[200]); // no longer substantive
                picked = CharacterAssetService.PickSpriteFramePng(
                    new[] { f1, f2, f3 }, preferFirstFrame: false);
                Assert.Equal(f2, picked);

                var first = CharacterAssetService.PickSpriteFramePng(
                    new[] { f1, f2, f3 }, preferFirstFrame: true);
                Assert.Equal(f1, first);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void DefaultNewUiDonor_IsMario()
        {
            Assert.Equal("mario", RosterWizardService.DefaultNewUiDonorId);
        }

        [Fact]
        public void GetEditableSlots_ExcludesCostumesAndWin()
        {
            var keys = CharacterAssetService.GetEditableSlots().Select(s => s.Key).ToList();
            Assert.Contains("select", keys);
            Assert.Contains("classic", keys);
            Assert.Contains("congrats", keys);
            Assert.DoesNotContain("win", keys);
            Assert.DoesNotContain("costumes", keys);
        }

        [Fact]
        public void EagerPlaceholderSlotKeys_AreCssPaStock()
        {
            Assert.Equal(new[] { "select", "cssp", "stock" },
                CharacterAssetService.EagerPlaceholderSlotKeys);
        }
    }
}
