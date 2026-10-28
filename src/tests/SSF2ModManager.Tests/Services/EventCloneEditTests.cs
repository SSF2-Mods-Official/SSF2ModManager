using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class EventCloneEditTests
    {
        private const string King = """
            _loc2_.playerSettings.push({
               "character":"bowser",
               "costume":param1.playerSettings[0].costume,
               "lives":2,
               "human":true
            });
            _loc2_.playerSettings.push({
               "character":"mario",
               "lives":2,
               "human":false,
               "level":3
            });
            _loc2_.levelData.stage = "kingdom1";
            _loc2_.levelData.musicOverride = "bgm_king";
            _loc2_.levelData.usingLives = true;
            _loc2_.levelData.lives = 2;
            SSF2API.spawnEnemy(flash.utils.getDefinitionByName("MotherBrain"));
            """;

        [Fact]
        public void Edit_KeepsTheBossAndChangesTheFighter()
        {
            var before = EventRecipe.Parse(King);
            var after = EventRecipe.Parse(King);
            after.Stage = "dreamland";
            after.Music = "bgm_menu";
            after.Fighters[1].Character = "luigi";
            after.Fighters[1].Costume = 2;
            after.Fighters[0].Costume = 4;

            var written = EventCloneEdit.Apply(King, before, after);
            Assert.Contains("getDefinitionByName(\"MotherBrain\")", written);
            Assert.Contains("levelData.stage = \"dreamland\"", written);
            Assert.Contains("musicOverride = \"bgm_menu\"", written);
            Assert.Contains("\"character\":\"luigi\"", written);
            Assert.Contains("\"character\":\"bowser\"", written);
            Assert.Contains("\"costume\": 4", written);
            Assert.DoesNotContain("\"character\":\"mario\"", written);
        }

        [Fact]
        public void Edit_WritesMusicWhenTheCloneHasNone()
        {
            const string source = """
                _loc2_.levelData.stage = "kingdom1";
                _loc2_.levelData.lives = 2;
                """;
            var before = EventRecipe.Parse(source);
            var after = EventRecipe.Parse(source);
            after.Music = "bgm_menu";

            var written = EventCloneEdit.Apply(source, before, after);

            Assert.Contains("levelData.stage = \"kingdom1\"", written);
            Assert.Contains("_loc2_.levelData.musicOverride = \"bgm_menu\"", written);
        }
    }
}
