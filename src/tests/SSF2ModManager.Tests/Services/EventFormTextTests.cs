using System;
using System.IO;
using System.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class EventFormTextTests
    {
        [Fact]
        public void Time_ShowsMinutesAndReadsFrames()
        {
            Assert.Equal("2:00", EventRankText.Show(7200, true));
            Assert.Equal(7200, EventRankText.Read("2:00", true));
            Assert.Equal("7200", EventRankText.Show(7200, false));
            Assert.Equal(7200, EventRankText.Read("7200", false));
        }

        [Fact]
        public void Defaults_FollowTheRecordType()
        {
            Assert.Equal(7200, EventRankText.Defaults("framesAtMost")[0]);
            Assert.Equal(2, EventRankText.Defaults("countAtLeast")[0]);
            Assert.Equal(20, EventRankText.Defaults("damageAtMost")[0]);
            Assert.True(EventRankText.IsTime("perMatch"));
            Assert.False(EventRankText.IsTime("countAtLeast"));
        }

        [Fact]
        public void OneItemList_ChecksOnAndKeepsUntouchedOff()
        {
            var rows = new[]
            {
                new EventItemRow { Id = "food", On = true, WasBanned = false, Touched = true },
                new EventItemRow { Id = "starrod", On = false, WasBanned = true, Touched = false },
                new EventItemRow { Id = "pokeball", On = false, WasBanned = false, Touched = false }
            };
            EventItemList.Split(rows, false, out var required, out var banned);
            Assert.Equal(new[] { "food" }, required);
            Assert.Equal(new[] { "starrod" }, banned);
        }

        [Fact]
        public void Name_RefusesAnExistingEvent()
        {
            var names = new[] { "Classic", "classic", "  Demo  " };
            Assert.True(EventNames.IsTaken(names, "classic"));
            Assert.True(EventNames.IsTaken(names, " Demo "));
            Assert.False(EventNames.IsTaken(names, "New Bout"));
            Assert.False(EventNames.IsTaken(names, "  "));
        }

        [Fact]
        public void Preview_WritesAPng()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-preview-" + Guid.NewGuid().ToString("N"));
            try
            {
                var dest = EventPreview.Write(dir, "Demo", null);
                Assert.True(File.Exists(dest));
                var head = File.ReadAllBytes(dest);
                Assert.Equal(new byte[] { 137, 80, 78, 71 }, head.Take(4));
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void StageBackground_IsWrittenOnThePackList()
        {
            var list = EventAuthorService.PackList("EventMode_BigBabymon", "BigBabymon", new EventInfoDraft
            {
                Name = "Big Babymon",
                StageBackground = "dededearena"
            });
            Assert.Contains("\"stageBackground\": \"dededearena\"", list);
            var mode = new EventRecipe { Stage = "pokemoncolosseum", StageBackground = "dededearena", Music = "bgm_ATBAPM" }
                .WriteSource("EventMode_BigBabymon", new EventInfoDraft { Name = "Big Babymon", Id = "BigBabymon" });
            Assert.DoesNotContain("eventinfo", mode);
            Assert.Equal("pokemoncolosseum", EventRecipe.Parse(mode).Stage);
        }

        [Fact]
        public void Preview_UsesPlayerOneWhenTheEventPicksTheCharacter()
        {
            var locked = EventAuthorService.PackList("EventMode_X", "X", new EventInfoDraft
            {
                ChooseCharacter = false,
                PreviewCharacter = "bowser",
                PreviewCostume = -1,
                Creator = "Wexxy"
            });
            Assert.Contains("\"previewCharacter\": \"bowser\"", locked);
            Assert.Contains("\"previewCostume\": -1", locked);
            Assert.Contains("\"creator\": \"Wexxy\"", locked);

            var choose = EventAuthorService.PackList("EventMode_X", "X", new EventInfoDraft
            {
                ChooseCharacter = true,
                PreviewCharacter = "bowser",
                PreviewCostume = 2
            });
            Assert.DoesNotContain("previewCharacter", choose);
            Assert.DoesNotContain("previewCostume", choose);
        }

        [Fact]
        public async Task VanillaScript_ReusesTheCachedExport()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-event-cache-" + Guid.NewGuid().ToString("N"));
            var cache = Path.Combine(root, "cache");
            Directory.CreateDirectory(Path.Combine(root, "data", "modes"));
            var swf = Path.Combine(root, "data", "modes", "event_mode.swf");
            File.WriteAllText(swf, "swf");
            EventAuthorService.EventScriptCacheRootOverride = cache;
            try
            {
                var cached = EventAuthorService.VanillaScriptCacheFile(swf, "EventMode1_Demo");
                Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
                File.WriteAllText(cached, "cached-script");
                var copy = await EventAuthorService.ExportVanillaSourceAsync(root, "EventMode1_Demo");
                try
                {
                    Assert.Equal("cached-script", File.ReadAllText(copy));
                }
                finally
                {
                    try { File.Delete(copy); } catch { }
                }
            }
            finally
            {
                EventAuthorService.EventScriptCacheRootOverride = null;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void Preview_SkipsAPlayerPick()
        {
            var text = EventAuthorService.PackList("EventMode_X", "X", new EventInfoDraft
            {
                ChooseCharacter = false,
                PreviewCharacter = "(player)",
                PreviewCostume = 3
            });
            Assert.DoesNotContain("previewCharacter", text);
            Assert.DoesNotContain("previewCostume", text);
        }

        [Fact]
        public void OneItemList_OnlyCheckedBansTheRest()
        {
            var rows = new[]
            {
                new EventItemRow { Id = "food", On = true },
                new EventItemRow { Id = "pokeball", On = false }
            };
            EventItemList.Split(rows, true, out var required, out var banned);
            Assert.Equal(new[] { "food" }, required);
            Assert.Equal(new[] { "pokeball" }, banned);
        }

        [Fact]
        public void OmittedCostume_IsBase()
        {
            var source = "extends SSF2CustomMatch playerSettings.push({ \"character\": \"jigglypuff\", \"lives\": 1, \"human\": true })";
            var recipe = EventRecipe.Parse(source);
            Assert.Equal(-1, recipe.Fighters[0].Costume);
        }
    }
}
