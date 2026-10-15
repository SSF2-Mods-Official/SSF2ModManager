using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class GameBananaInstallsTests
    {
        private static string NewShell()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-gbinstalls-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        [Fact]
        public void Installed_WhileItemsExist_PerBuild()
        {
            var shell = NewShell();
            var other = NewShell();
            try
            {
                var stage = Path.Combine(ShellWorkspaceService.StageExpansionsDirectory(shell), "draculascastle");
                Directory.CreateDirectory(stage);
                GameBananaInstalls.Record(shell, 149685, 387381, "Fountain of Dreams",
                    new[] { (OneClickKind.Stage, "draculascastle"), (OneClickKind.Song, "bgm_bloodytears") });

                Assert.Contains(149685, GameBananaInstalls.InstalledModIds(shell));
                Assert.Empty(GameBananaInstalls.InstalledModIds(other));

                Directory.Delete(stage, true);
                Assert.DoesNotContain(149685, GameBananaInstalls.InstalledModIds(shell));
            }
            finally
            {
                Directory.Delete(shell, true);
                Directory.Delete(other, true);
            }
        }

        [Fact]
        public void StampGameBananaId_AddsToInfo()
        {
            var dir = NewShell();
            try
            {
                File.WriteAllText(Path.Combine(dir, "info.json"), "{\"name\":\"Fountain of Dreams\",\"enabled\":true}");
                OneClickReceive.StampGameBananaId(dir, 149685);
                var info = JObject.Parse(File.ReadAllText(Path.Combine(dir, "info.json")));
                Assert.Equal(149685, (int)info["gamebanana"]!);
                Assert.Equal("Fountain of Dreams", (string?)info["name"]);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void DefaultDisplayName_OneStageTakesModName()
        {
            var stage = new OneClickItem { Kind = OneClickKind.Stage, Id = "draculascastle", OriginalName = "Dracula's Castle", Selected = true };
            var song = new OneClickItem { Kind = OneClickKind.Song, Id = "bgm_bloodytears", Selected = true };
            var receipt = new OneClickReceipt { Items = { stage, song } };
            Assert.Equal("Fountain of Dreams", OneClickReceive.DefaultDisplayName(receipt, stage, "Fountain of Dreams"));
            Assert.Equal("bgm_bloodytears", OneClickReceive.DefaultDisplayName(receipt, song, "Fountain of Dreams"));

            var second = new OneClickItem { Kind = OneClickKind.Stage, Id = "other", OriginalName = "Other", Selected = true };
            receipt.Items.Add(second);
            Assert.Equal("Dracula's Castle", OneClickReceive.DefaultDisplayName(receipt, stage, "Fountain of Dreams"));
        }

        [Fact]
        public void NamePair_CharacterOnly_NeverTheId()
        {
            var isaac = new OneClickItem { Kind = OneClickKind.Character, Id = "isaac", OriginalName = "Isaac", DisplayName = "Isaac (Golden Sun)" };
            Assert.Equal(("Isaac", "Isaac (Golden Sun)"), OneClickReceive.NamePair(isaac));
            var linkageOnly = new OneClickItem { Kind = OneClickKind.Character, Id = "isaac2", OriginalName = "isaac2", DisplayName = "Isaac" };
            Assert.Null(OneClickReceive.NamePair(linkageOnly));
            var stage = new OneClickItem { Kind = OneClickKind.Stage, Id = "draculascastle", OriginalName = "draculascastle", DisplayName = "Fountain of Dreams" };
            Assert.Null(OneClickReceive.NamePair(stage));
        }

        [Fact]
        public void NameStage_SetsMappingsName()
        {
            var mappings = JObject.Parse("{\"stage\":{\"bf\":{\"file\":\"bf.swf\",\"name\":\"Battlefield\"}}}");
            OneClickReceive.NameStage(mappings, "bf", "Battlefield 2");
            Assert.Equal("Battlefield 2", (string?)mappings["stage"]!["bf"]!["name"]);
            Assert.Equal("bf.swf", (string?)mappings["stage"]!["bf"]!["file"]);

            OneClickReceive.NameStage(mappings, "draculascastle2", "Fountain of Dreams");
            var entry = mappings["stage"]!["draculascastle2"]!;
            Assert.Equal("Fountain of Dreams", (string?)entry["name"]);
            Assert.Equal("expansions/stages/draculascastle2/draculascastle2.swf", (string?)entry["file"]);
        }

        [Fact]
        public void RawList_NoGameFiles_StopsBeforeDownload()
        {
            Assert.True(OneClickReceive.RawListHasGameFiles(Array.Empty<string>()));
            Assert.True(OneClickReceive.RawListHasGameFiles(new[] { "fod/DAT8.ssf", "fod/Readme.txt" }));
            Assert.False(OneClickReceive.RawListHasGameFiles(new[] { "readme.txt", "preview.png" }));
            Assert.Contains("Nothing was downloaded", OneClickReceive.NothingInFileListMessage(new[] { "readme.txt" }, "X"));
        }
    }
}
