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
        public void ReceiptIntro_MentionsReplacementOnlyWhenListed()
        {
            var plain = new OneClickReceipt { Items = { new OneClickItem { Kind = OneClickKind.Stage, RelativePath = "own/s.ssf" } } };
            Assert.Equal("Choose what to install into SSF2 Shell.\n\n" + OneClickReceive.TempNote(false), OneClickReceive.ReceiptIntro(plain));

            plain.Items.Add(new OneClickItem { Kind = OneClickKind.Stage, RelativePath = "Over FD/s.ssf", Selected = false, Note = "Replaces a built-in stage" });
            plain.LeftOutNineB = 2;
            var intro = OneClickReceive.ReceiptIntro(plain);
            Assert.Contains("replaces a built-in stage", intro);
            Assert.Contains("2 file(s) made for SSF2 0.9b were skipped", intro);
            Assert.DoesNotContain("folder named Over", intro);
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
            Assert.Contains("ssf2mm-oneclick-", nine);
            Assert.Contains("has been deleted", nine);
        }

        [Fact]
        public void ReceiptIntro_SaysWhereDownloadIsAndThatItIsDeleted()
        {
            var intro = OneClickReceive.ReceiptIntro(new OneClickReceipt
            {
                Items = { new OneClickItem { Kind = OneClickKind.Stage, RelativePath = "s.ssf", Selected = true } }
            });
            Assert.Contains(System.IO.Path.GetTempPath(), intro);
            Assert.Contains("is deleted when you install or cancel", intro);
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
        [InlineData("Complete Modpack", "")]
        [InlineData("Stage pack", "mod pack")]
        [InlineData("Moves", "This is a mod-pack of costumes")]
        public void ModpackMention_Unsupported(string name, string text)
        {
            Assert.True(BrowseModFilter.IsUnsupported(new GameBananaMod { Name = name, FullText = text }));
        }

        [Fact]
        public void ExeOrJsonDownload_Unsupported()
        {
            var exe = new GameBananaMod
            {
                Name = "Installer",
                Files = new() { new GameBananaFile { FileName = "setup.exe", DownloadUrl = "https://files.gamebanana.com/setup.exe" } }
            };
            var json = new GameBananaMod
            {
                Name = "Manifest",
                Files = new() { new GameBananaFile { FileName = "mod.json?dl=1" } }
            };
            var zip = new GameBananaMod
            {
                Name = "Stage",
                Files = new() { new GameBananaFile { FileName = "clocktown.zip", DownloadUrl = "https://files.gamebanana.com/clocktown.zip" } }
            };
            Assert.True(BrowseModFilter.HasBlockedDownload(exe));
            Assert.True(BrowseModFilter.IsUnsupported(json));
            Assert.False(BrowseModFilter.HasBlockedDownload(zip));
            Assert.False(BrowseModFilter.IsUnsupported(zip));
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
