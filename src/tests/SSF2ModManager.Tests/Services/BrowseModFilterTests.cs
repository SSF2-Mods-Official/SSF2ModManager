using SSF2ModManager.Models;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class BrowseModFilterTests
    {
        [Theory]
        [InlineData("Aigis(Persona 3 and 4Arena)[SSF2 0.9b]", "")]
        [InlineData("Biblically Accurate SSF1 Blade and Blue (9B/FMC)", "9b and FMC")]
        [InlineData("Olympus Coliseum | Kingdom Hearts", "9B")]
        [InlineData("SSF2 v0.9b: Spike (Edit)", "")]
        [InlineData("Hyper’s 9b Sonic Edit", "")]
        [InlineData("(V0.9B) Brawlhalla Crystal Temple Port", "")]
        [InlineData("SSF2 Mods: My Old Mods", "V0.9b")]
        [InlineData("Super Sonic God [0.9B + SANDBOX]", "")]
        [InlineData("Bowletta's Throne (M&LSS) [SSF2 V.9b/Sandbox]", "")]
        [InlineData("Fieldbattle (v0.9b) & (Beta 1.4.0.1)", "")]
        public void AnyNineBMention_Hidden(string name, string version)
        {
            Assert.True(BrowseModFilter.MentionsNineB(new GameBananaMod { Name = name, Version = version }));
        }

        [Theory]
        [InlineData("Pillarmen Stage | Jojo's Bizarre Adventure", "Beta 1.4")]
        [InlineData("[Beta 1.4] Smash Ultimate Mr. Game & Watch", "")]
        [InlineData("Lionel Messi", "")]
        [InlineData("Q9B Robot", "29b")]
        public void Others_Listed(string name, string version)
        {
            Assert.False(BrowseModFilter.MentionsNineB(new GameBananaMod { Name = name, Version = version }));
        }

        [Fact]
        public void NineB_ReadsDescription()
        {
            var cr7 = new GameBananaMod
            {
                Name = "Cristiano ronaldo! (Joke mod)",
                FullText = "This Cristiano Ronaldo mod is made as a joke, it contains various memes and references to CR7, " +
                           "Real Madrid, and other football players!<br><br>(0.9b)"
            };
            Assert.True(BrowseModFilter.MentionsNineB(cr7));
            cr7.FullText += "<br>Also works in Sandbox.";
            Assert.True(BrowseModFilter.MentionsNineB(cr7));
        }

        [Fact]
        public void NothingToInstall_ExplainsWhy()
        {
            Assert.Null(OneClickReceive.NothingToInstallMessage(new OneClickReceipt
            {
                Items = { new OneClickItem { Kind = OneClickKind.Stage, RelativePath = "s.ssf" } }
            }));
            var nine = OneClickReceive.NothingToInstallMessage(new OneClickReceipt { LeftOutNineB = 12, LeftOutUnknown = 3 }, "CR7");
            Assert.Contains("CR7 can't be installed", nine);
            Assert.Contains("12 file(s) are made for SSF2 0.9b", nine);
            Assert.Contains("3 file(s) aren't", nine);
            Assert.Contains("no SSF2 game files", OneClickReceive.NothingToInstallMessage(new OneClickReceipt()));
        }

        [Fact]
        public void ExtractGuard_AllowsSsfAndNamesBlockedType()
        {
            long bytes = 0;
            var count = 0;
            Assert.Null(ArchiveExtractGuard.Reject(@"SSF2 CR7\dat36.ssf", 1000, ref bytes, ref count));
            var blocked = ArchiveExtractGuard.Reject(@"SSF2 CR7\setup.exe", 1000, ref bytes, ref count);
            Assert.Contains(".exe", blocked);
        }

        [Theory]
        [InlineData("Characters", "character")]
        [InlineData("Maps", "stage")]
        [InlineData("Sounds", "music")]
        [InlineData("Skins", "mod")]
        [InlineData("Game files", "mod")]
        [InlineData(null, "mod")]
        public void KindFor_RootCategory(string? root, string kind)
        {
            Assert.Equal(kind, BrowseModFilter.KindFor(root));
        }
    }
}
