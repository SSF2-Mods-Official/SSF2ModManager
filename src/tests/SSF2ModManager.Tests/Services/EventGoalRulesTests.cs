using System.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class EventGoalRulesTests
    {
        private static EventRecipe King()
        {
            return new EventRecipe
            {
                Kind = EventKind.Bout,
                Stage = "kingdom1",
                UsingLives = true,
                UsingTime = true,
                Lives = 2,
                Minutes = 3,
                ItemFrequency = "HIGH",
                Fighters =
                {
                    new EventFighter { Character = "bowser", Human = true, Lives = 2 },
                    new EventFighter { Character = "mario", Human = false, Level = 3, Lives = 2, SizeStatus = 1, DamageRatio = 2 }
                }
            };
        }

        private static string Write(EventRecipe recipe) =>
            recipe.WriteSource("EventMode_Copy", new EventInfoDraft { Name = "Copy", Id = "Copy" });

        [Fact]
        public void Loop_OnTroubledKing_KeepsTheFightersAndSetsTheSmash()
        {
            var recipe = King();
            recipe.Goal = EventGoal.LoopBackground;
            EventGoalRules.Apply(recipe);

            Assert.Equal("homeruncontest", recipe.Stage);
            Assert.False(recipe.UsingLives);
            Assert.True(recipe.UsingTime);
            Assert.Equal("bowser", recipe.Fighters[0].Character);
            Assert.Equal("mario", recipe.Fighters[1].Character);
            Assert.Equal(1, recipe.Fighters[1].SizeStatus);
            Assert.Equal("FORCE_RUN", recipe.Fighters[1].CpuAction);

            var source = Write(recipe);
            Assert.Contains("grantFinalSmash", source);
            Assert.Contains("hrc_groundloop1", source);
            Assert.Contains("getFinalSmashCutscene", source);
            Assert.DoesNotContain("function checkTag", source);
        }

        [Fact]
        public void ClonedInitialize_DoesNotBlockMotherBrain()
        {
            var recipe = King();
            recipe.ExtraMethods.Add("override public function initialize() : void\r\n{\r\nSSF2API.getPlayer(2).setSizeStatus(1);\r\nSSF2API.getGameTimer().setCurrentTime(SSF2API.getGameTimer().getCurrentTime());\r\n}");
            recipe.Goal = EventGoal.MotherBrain;
            EventGoalRules.Apply(recipe);

            var source = Write(recipe);
            Assert.Contains("getDefinitionByName(\"MotherBrain\")", source);
            Assert.Contains("setSizeStatus(1)", source);
            Assert.DoesNotContain("getGameTimer().getCurrentTime())", source);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(source, @"function\s+initialize\s*\(").Count);
        }

        [Fact]
        public void ClonedInitialize_DoesNotBlockTheBat()
        {
            var recipe = King();
            recipe.ExtraMethods.Add("override public function initialize() : void\r\n{\r\nSSF2API.getPlayer(2).setSizeStatus(1);\r\n}");
            recipe.Goal = EventGoal.Bat;
            EventGoalRules.Apply(recipe);

            var source = Write(recipe);
            Assert.Contains("generateItem(\"homerunbat\"", source);
            Assert.Contains("function checkBat", source);
            Assert.Contains("CHAR_KO_DEATH, this.checkBat", source);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(source, @"function\s+initialize\s*\(").Count);
        }

        [Fact]
        public void ClonedInitialize_DoesNotBlockTheSmashRun()
        {
            var recipe = King();
            recipe.ExtraMethods.Add("override public function initialize() : void\r\n{\r\nSSF2API.getPlayer(2).setSizeStatus(1);\r\n}");
            recipe.Goal = EventGoal.LoopBackground;
            EventGoalRules.Apply(recipe);

            var source = Write(recipe);
            Assert.Equal("homeruncontest", recipe.Stage);
            Assert.Contains("grantFinalSmash", source);
            Assert.Contains("homeruncontest", source);
            Assert.Contains("hrc_groundloop1", source);
            Assert.Equal(1, System.Text.RegularExpressions.Regex.Matches(source, @"function\s+initialize\s*\(").Count);
        }
        [Fact]
        public void MotherBrain_SpawnsTheBossAndKeepsBowser()
        {
            var recipe = King();
            recipe.Goal = EventGoal.MotherBrain;
            EventGoalRules.Apply(recipe);

            Assert.Equal("bowser", recipe.Fighters[0].Character);
            Assert.Equal("mario", recipe.Fighters[1].Character);
            Assert.False(recipe.UsingTime);

            var source = Write(recipe);
            Assert.Contains("getDefinitionByName(\"MotherBrain\")", source);
            Assert.Contains("inState(EState.DEAD)", source);
            Assert.DoesNotContain("grantFinalSmash", source);
        }

        [Fact]
        public void SwitchingAway_DropsThePreviousClear()
        {
            var recipe = King();
            recipe.ExtraMethods.Add("private function handleLoopingBackground():void\r\n{\r\n}");
            recipe.Goal = EventGoal.Stocks;

            var source = Write(recipe);
            Assert.DoesNotContain("handleLoopingBackground", source);
            Assert.DoesNotContain("grantFinalSmash", source);
            Assert.Contains("getCurrentTime() <= 0", source);
            Assert.Contains("lockSizeStatus(true)", source);
        }

        [Fact]
        public void Tag_TurnsTheExplodingTagOn()
        {
            var recipe = King();
            recipe.Goal = EventGoal.Tag;
            recipe.ItemFrequency = "OFF";
            EventGoalRules.Apply(recipe);

            Assert.Contains("explodingtag", recipe.RequiredItems);
            Assert.Equal("MAX", recipe.ItemFrequency);
            Assert.False(recipe.Fighters[1].LivesEnabled);

            var source = Write(recipe);
            Assert.Contains("function checkTag", source);
            Assert.Contains("explodingtag", source);
        }

        [Fact]
        public void Sheik_SetsEachCpuToZelda()
        {
            var recipe = King();
            recipe.Goal = EventGoal.Sheik;
            EventGoalRules.Apply(recipe);

            Assert.Equal("bowser", recipe.Fighters[0].Character);
            Assert.Equal("zelda", recipe.Fighters[1].Character);
            Assert.Contains("function handleSheik", Write(recipe));
        }

        [Fact]
        public void Bumpers_UseCasinoNight()
        {
            var recipe = King();
            recipe.Goal = EventGoal.Bumpers;
            EventGoalRules.Apply(recipe);

            Assert.Equal("casinonightzone", recipe.Stage);
            Assert.Equal("mario", recipe.Fighters[1].Character);
            Assert.Contains("Bumper1_B", Write(recipe));
        }

        [Theory]
        [InlineData(EventGoal.Combo, "function onStateChange")]
        [InlineData(EventGoal.Bat, "function checkBat")]
        [InlineData(EventGoal.Bombs, "function checkBombTimer")]
        [InlineData(EventGoal.Saturns, "mrsaturn")]
        [InlineData(EventGoal.Dirt, "getDefinitionByName(\"EventAsset_PileOfDirt\")")]
        [InlineData(EventGoal.Pokeball, "function pokeBallCheck")]
        [InlineData(EventGoal.Protoman, "getDefinitionByName(\"Protoman\")")]
        [InlineData(EventGoal.MasterHand, "spawnEnemy(\"MasterHand\")")]
        [InlineData(EventGoal.RandomItems, "function randomItem")]
        public void EachClear_WritesItsRule(EventGoal goal, string token)
        {
            var recipe = King();
            recipe.Goal = goal;
            EventGoalRules.Apply(recipe);
            Assert.Contains(token, Write(recipe));
        }
    }
}
