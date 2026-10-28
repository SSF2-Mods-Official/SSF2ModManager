using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// One How you win row. <see cref="Apply"/> changes the match until that rule can run.
    /// <see cref="AppendUpdate"/> writes the clear. Fighters stay when any character can play the rule.
    /// </summary>
    public static class EventGoalRules
    {
        private static readonly string[] GoalFunctions =
        {
            "checkTag", "onStateChange", "checkBat", "checkBombTimer", "randomPoint", "checkBumper",
            "handleSheik", "checkSaturns", "checkDirt", "pokeBallCheck", "generateProtoman",
            "checkMotherBrain", "checkMH", "handDown", "randomItem", "setTimeMax",
            "handleLoopingBackground", "checkGoal"
        };

        public static bool NeedsInit(EventGoal goal) => goal != EventGoal.Stocks;

        public static bool DropExtra(EventGoal goal, EventKind kind, string method)
        {
            if (IsGoalFunction(method))
                return true;
            if (goal != EventGoal.Stocks && Regex.IsMatch(method ?? "", @"function\s+initialize\s*\("))
                return true;
            return kind is EventKind.Bout or EventKind.Unknown
                && Regex.IsMatch(method ?? "", @"function\s+(generateNextEnemy|initMatch|onSuccess|onPlayerKO)\s*\(");
        }

        public static string Proof(EventGoal goal) => goal switch
        {
            EventGoal.Tag => "function checkTag",
            EventGoal.Combo => "function onStateChange",
            EventGoal.Bat => "generateItem(\"homerunbat\"",
            EventGoal.Bombs => "function checkBombTimer",
            EventGoal.Bumpers => "Bumper1_B",
            EventGoal.Sheik => "function handleSheik",
            EventGoal.Saturns => "generateItem(\"mrsaturn\"",
            EventGoal.Dirt => "getDefinitionByName(\"EventAsset_PileOfDirt\")",
            EventGoal.Pokeball => "function pokeBallCheck",
            EventGoal.Protoman => "getDefinitionByName(\"Protoman\")",
            EventGoal.MotherBrain => "getDefinitionByName(\"MotherBrain\")",
            EventGoal.MasterHand => "spawnEnemy(\"MasterHand\")",
            EventGoal.RandomItems => "function randomItem",
            EventGoal.LoopBackground => "grantFinalSmash",
            _ => "getLives() <= 0"
        };

        private static string SpawnClass(string className) =>
            "SSF2API.spawnEnemy(flash.utils.getDefinitionByName(\"" + className + "\"))";

        public static bool IsGoalFunction(string method)
        {
            var name = Regex.Match(method ?? "", @"function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(");
            return name.Success && GoalFunctions.Contains(name.Groups[1].Value);
        }

        public static void Apply(EventRecipe recipe)
        {
            if (recipe == null || recipe.Kind is not (EventKind.Bout or EventKind.Unknown))
                return;
            switch (recipe.Goal)
            {
                case EventGoal.Tag:
                    NeedItem(recipe, "explodingtag", "MAX");
                    EnsureCpu(recipe, "mario");
                    DisarmLives(recipe);
                    break;
                case EventGoal.Combo:
                    recipe.UsingLives = false;
                    recipe.UsingTime = true;
                    if (recipe.Minutes <= 0) recipe.Minutes = 2;
                    var timeRecord = recipe.RankMeasure is not ("damageAtLeast" or "damageAtMost");
                    recipe.RankMeasure = "damageAtLeast";
                    recipe.ScoreType = "points";
                    if (timeRecord || recipe.Ranks.Count == 0 || recipe.Ranks.All(r => r.Value <= 0) || recipe.Ranks.Any(r => r.Value > 400))
                        recipe.Ranks = Cuts(62, 56, 54, 53, 52, 50);
                    EnsureCpu(recipe, "sandbag");
                    foreach (var cpu in recipe.Fighters.Where(f => !f.Human))
                        cpu.CpuAction = "FORCE_DO_NOTHING";
                    break;
                case EventGoal.Bat:
                    EnsureCpu(recipe, "mario");
                    DisarmLives(recipe);
                    break;
                case EventGoal.Bumpers:
                    recipe.Stage = "casinonightzone";
                    recipe.UsingLives = true;
                    if (recipe.Lives <= 0) recipe.Lives = 2;
                    break;
                case EventGoal.Sheik:
                    foreach (var cpu in recipe.Fighters.Where(f => !f.Human))
                        cpu.Character = "zelda";
                    EnsureCpu(recipe, "zelda");
                    recipe.UsingLives = true;
                    break;
                case EventGoal.Saturns:
                    NeedItem(recipe, "mrsaturn", "MEDIUM");
                    EnsureCpu(recipe, "mario");
                    break;
                case EventGoal.Dirt:
                    recipe.UsingLives = true;
                    recipe.UsingTime = true;
                    if (recipe.Minutes <= 0) recipe.Minutes = 1;
                    break;
                case EventGoal.Pokeball:
                    NeedItem(recipe, "pokeball", "MAX");
                    EnsureCpu(recipe, "pikachu");
                    break;
                case EventGoal.Protoman:
                    EnsureCpu(recipe, "mario");
                    break;
                case EventGoal.MotherBrain:
                    recipe.UsingLives = true;
                    recipe.UsingTime = false;
                    if (recipe.Lives <= 0) recipe.Lives = 1;
                    break;
                case EventGoal.MasterHand:
                    recipe.UsingLives = true;
                    recipe.UsingTime = false;
                    if (recipe.Lives <= 0) recipe.Lives = 1;
                    break;
                case EventGoal.RandomItems:
                    recipe.ItemFrequency = "OFF";
                    EnsureCpu(recipe, "mario");
                    break;
                case EventGoal.LoopBackground:
                    recipe.Stage = "homeruncontest";
                    recipe.UsingLives = false;
                    recipe.UsingTime = true;
                    if (recipe.Minutes <= 0) recipe.Minutes = 1;
                    foreach (var cpu in recipe.Fighters.Where(f => !f.Human))
                        cpu.CpuAction = "FORCE_RUN";
                    break;
            }
        }

        public static IEnumerable<string> DeclareFields(EventRecipe recipe)
        {
            var have = new HashSet<string>(StringComparer.Ordinal);
            foreach (var field in recipe.Fields)
            {
                have.Add(FieldName(field));
                yield return field;
            }
            foreach (var field in WantedFields(recipe.Goal))
            {
                var name = FieldName(field);
                if (name.Length == 0 || !have.Add(name))
                    continue;
                yield return field;
            }
        }

        public static void AppendInit(EventRecipe recipe, StringBuilder sb)
        {
            switch (recipe.Goal)
            {
                case EventGoal.Combo:
                    foreach (var slot in CpuSlots(recipe))
                        sb.Append("\t\t\tSSF2API.getPlayer(").Append(slot).Append(").addEventListener(SSF2Event.STATE_CHANGE, this.onStateChange, { \"persistent\": true });\r\n");
                    break;
                case EventGoal.Bombs:
                    sb.Append("\t\t\tthis.m_bombTimer = new FrameTimer(60);\r\n");
                    sb.Append("\t\t\tSSF2API.setCamStageFocus(int.MAX_VALUE);\r\n");
                    break;
                case EventGoal.Bumpers:
                    sb.Append("\t\t\tSSF2API.setCamStageFocus(int.MAX_VALUE);\r\n");
                    sb.Append("\t\t\tSSF2API.getPlayer(1).addEventListener(SSF2Event.CHAR_HURT, this.checkBumper, { \"persistent\": true });\r\n");
                    break;
                case EventGoal.Sheik:
                    sb.Append("\t\t\tthis.transformTimer = 270;\r\n");
                    foreach (var slot in CpuSlots(recipe))
                        sb.Append("\t\t\tSSF2API.getPlayer(").Append(slot).Append(").setLivesEnabled(false);\r\n");
                    break;
                case EventGoal.Saturns:
                    sb.Append("\t\t\tvar saturnSpots:Array = [-180, -60, 60];\r\n");
                    sb.Append("\t\t\tvar saturnN:int = 0;\r\n");
                    sb.Append("\t\t\twhile (saturnN < saturnSpots.length)\r\n\t\t\t{\r\n");
                    sb.Append("\t\t\t\tvar saturn:* = SSF2API.generateItem(\"mrsaturn\", saturnSpots[saturnN], 80, true);\r\n");
                    sb.Append("\t\t\t\tsaturn.updateItemStats({ \"time_max\": int.MAX_VALUE });\r\n");
                    sb.Append("\t\t\t\tsaturn.resetTime();\r\n");
                    sb.Append("\t\t\t\tsaturn.addToCamera();\r\n");
                    sb.Append("\t\t\t\tthis.saturnArray.push(saturn);\r\n");
                    sb.Append("\t\t\t\tsaturnN++;\r\n\t\t\t}\r\n");
                    break;
                case EventGoal.Dirt:
                    sb.Append("\t\t\tvar dirtSpots:Array = [-160, -40, 80, 200];\r\n");
                    sb.Append("\t\t\tvar dirtN:int = 0;\r\n");
                    sb.Append("\t\t\twhile (dirtN < dirtSpots.length)\r\n\t\t\t{\r\n");
                    sb.Append("\t\t\t\tvar pile:* = ").Append(SpawnClass("EventAsset_PileOfDirt")).Append(";\r\n");
                    sb.Append("\t\t\t\tpile.setX(dirtSpots[dirtN]);\r\n");
                    sb.Append("\t\t\t\tpile.setY(40);\r\n");
                    sb.Append("\t\t\t\tpile.forceOnGround();\r\n");
                    sb.Append("\t\t\t\tpile.addToCamera();\r\n");
                    sb.Append("\t\t\t\tthis.dirtArray.push(pile);\r\n");
                    sb.Append("\t\t\t\tdirtN++;\r\n\t\t\t}\r\n");
                    break;
                case EventGoal.Pokeball:
                    sb.Append("\t\t\tvar pokeN:int = 1;\r\n");
                    sb.Append("\t\t\twhile (pokeN <= ").Append(Math.Max(recipe.Fighters.Count, 1)).Append(")\r\n\t\t\t{\r\n");
                    sb.Append("\t\t\t\tSSF2API.getPlayer(pokeN).setHurtInterrupt(this.pokeBallCheck);\r\n");
                    sb.Append("\t\t\t\tpokeN++;\r\n\t\t\t}\r\n");
                    break;
                case EventGoal.Protoman:
                    sb.Append("\t\t\tthis.generateProtoman();\r\n");
                    sb.Append("\t\t\tif (this.m_assist) this.m_assist.forceAttack(\"entrance\");\r\n");
                    break;
                case EventGoal.MotherBrain:
                    sb.Append("\t\t\tSSF2API.getPlayer(1).faceLeft();\r\n");
                    sb.Append("\t\t\tthis.motherBrain = ").Append(SpawnClass("MotherBrain")).Append(";\r\n");
                    sb.Append("\t\t\tthis.motherBrain.setHP(200);\r\n");
                    sb.Append("\t\t\tthis.motherBrain.attachHealthBox(\"MOTHER BRAIN\", \"motherBrain_icon\", \"Series_Metroid\");\r\n");
                    sb.Append("\t\t\tthis.motherBrain.setX(200);\r\n");
                    sb.Append("\t\t\tthis.motherBrain.setY(80);\r\n");
                    sb.Append("\t\t\tthis.motherBrain.setTimer(100000);\r\n");
                    sb.Append("\t\t\tthis.motherBrain.addToCamera();\r\n");
                    break;
                case EventGoal.MasterHand:
                    sb.Append("\t\t\tthis.enemy = SSF2API.spawnEnemy(\"MasterHand\");\r\n");
                    sb.Append("\t\t\tthis.enemy.setHomePosition(new flash.geom.Point(200, 40));\r\n");
                    sb.Append("\t\t\tthis.enemy.setHurtInterrupt(this.checkMH);\r\n");
                    sb.Append("\t\t\tthis.enemy.setX(200);\r\n");
                    sb.Append("\t\t\tthis.enemy.setY(40);\r\n");
                    sb.Append("\t\t\tthis.enemy2 = SSF2API.spawnEnemy(\"MasterHand\");\r\n");
                    sb.Append("\t\t\tthis.enemy2.setHomePosition(new flash.geom.Point(-200, 40));\r\n");
                    sb.Append("\t\t\tthis.enemy2.setHurtInterrupt(this.checkMH);\r\n");
                    sb.Append("\t\t\tthis.enemy2.faceLeft();\r\n");
                    sb.Append("\t\t\tthis.enemy2.setX(-200);\r\n");
                    sb.Append("\t\t\tthis.enemy2.setY(40);\r\n");
                    sb.Append("\t\t\tthis.enemy.setHP(500);\r\n");
                    sb.Append("\t\t\tthis.enemy2.setHP(500);\r\n");
                    break;
                case EventGoal.RandomItems:
                    sb.Append("\t\t\tthis.randomItem();\r\n");
                    sb.Append("\t\t\tSSF2API.addEventListener(SSF2Event.GAME_ITEM_CREATED, this.setTimeMax);\r\n");
                    break;
                case EventGoal.LoopBackground:
                    sb.Append("\t\t\tSSF2API.getPlayer(1).grantFinalSmash();\r\n");
                    break;
            }
        }

        public static void AppendUpdate(EventRecipe recipe, StringBuilder sb)
        {
            const string pad = "\t\t\t";
            switch (recipe.Goal)
            {
                case EventGoal.Bombs:
                    sb.Append(pad).Append("this.checkBombTimer();\r\n");
                    AppendKoClear(recipe, sb, pad);
                    break;
                case EventGoal.Sheik:
                    AppendSheikTick(recipe, sb, pad);
                    sb.Append(pad).Append("this.handleSheik();\r\n");
                    AppendKoClear(recipe, sb, pad);
                    break;
                case EventGoal.Saturns:
                    AppendSaturnWatch(sb, pad);
                    AppendKoClear(recipe, sb, pad, "this.saturnFailed");
                    break;
                case EventGoal.Dirt:
                    AppendDirtWatch(sb, pad);
                    sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
                    AppendLoss(recipe, sb, pad);
                    sb.Append(pad).Append("if (this.dirtArray.length > 0) return;\r\n");
                    recipe.AppendSucceed(sb, pad);
                    break;
                case EventGoal.Protoman:
                    sb.Append(pad).Append("if (!this.m_assist || this.m_assist.inState(EState.DEAD)) this.generateProtoman();\r\n");
                    AppendKoClear(recipe, sb, pad);
                    break;
                case EventGoal.RandomItems:
                    sb.Append(pad).Append("this.randomItem();\r\n");
                    AppendKoClear(recipe, sb, pad);
                    break;
                case EventGoal.Combo:
                    sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
                    AppendLoss(recipe, sb, pad);
                    break;
                case EventGoal.Bumpers:
                    sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
                    AppendLoss(recipe, sb, pad);
                    sb.Append(pad).Append("if (!(this.bumper1 && this.bumper2 && this.bumper3 && this.bumper4 && this.bumper5 && this.bumper6)) return;\r\n");
                    recipe.AppendSucceed(sb, pad);
                    break;
                case EventGoal.MotherBrain:
                    sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
                    AppendLoss(recipe, sb, pad);
                    sb.Append(pad).Append("if (!(this.motherBrain && this.motherBrain.inState(EState.DEAD))) return;\r\n");
                    recipe.AppendSucceed(sb, pad);
                    break;
                case EventGoal.MasterHand:
                    sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
                    AppendLoss(recipe, sb, pad);
                    sb.Append(pad).Append("if (!(this.handDown(this.enemy) && this.handDown(this.enemy2))) return;\r\n");
                    recipe.AppendSucceed(sb, pad);
                    break;
                case EventGoal.LoopBackground:
                    AppendLoop(recipe, sb, pad);
                    break;
                default:
                    AppendKoClear(recipe, sb, pad);
                    break;
            }
        }

        public static void AppendMethods(EventRecipe recipe, StringBuilder sb)
        {
            switch (recipe.Goal)
            {
                case EventGoal.Tag:
                    sb.Append("\t\tpublic function checkTag(param1:*):void\r\n\t\t{\r\n");
                    sb.Append("\t\t\tvar who:* = param1.data.caller;\r\n");
                    sb.Append("\t\t\tvar stats:* = who.getLastHurtAttackBoxStats();\r\n");
                    sb.Append("\t\t\tvar owner:* = stats ? stats.owner : null;\r\n");
                    sb.Append("\t\t\tif (owner && owner.getType() == \"SSF2Item\" && owner.getItemStat(\"linkage_id\") == \"explodingtag\")\r\n\t\t\t{\r\n");
                    sb.Append("\t\t\t\twho.removeEventListener(SSF2Event.CHAR_KO_DEATH, this.checkTag);\r\n");
                    sb.Append("\t\t\t\twho.setLivesEnabled(true);\r\n");
                    sb.Append("\t\t\t\twho.setLives(0);\r\n");
                    sb.Append("\t\t\t\twho.setStandby(true);\r\n\t\t\t}\r\n");
                    sb.Append("\t\t\telse who.setLivesEnabled(false);\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.Bat:
                    sb.Append("\t\tpublic function checkBat(param1:*):void\r\n\t\t{\r\n");
                    sb.Append("\t\t\tvar who:* = param1.data.caller;\r\n");
                    sb.Append("\t\t\tvar stats:* = who.getLastHurtAttackBoxStats();\r\n");
                    sb.Append("\t\t\tvar owner:* = stats ? stats.owner : null;\r\n");
                    sb.Append("\t\t\tif (owner && owner.getType() == \"SSF2Item\" && owner.getItemStat(\"linkage_id\") == \"homerunbat\")\r\n\t\t\t{\r\n");
                    sb.Append("\t\t\t\twho.removeEventListener(SSF2Event.CHAR_KO_DEATH, this.checkBat);\r\n");
                    sb.Append("\t\t\t\twho.setLivesEnabled(true);\r\n");
                    sb.Append("\t\t\t\twho.setLives(0);\r\n");
                    sb.Append("\t\t\t\twho.setStandby(true);\r\n\t\t\t}\r\n");
                    sb.Append("\t\t\telse who.setLivesEnabled(false);\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.Combo:
                    AppendCombo(recipe, sb);
                    break;
                case EventGoal.Bombs:
                    sb.Append("\t\tpublic function checkBombTimer():void\r\n\t\t{\r\n");
                    sb.Append("\t\t\tthis.m_bombTimer.tick();\r\n");
                    sb.Append("\t\t\tif (!this.m_bombTimer.completed) return;\r\n");
                    sb.Append("\t\t\tvar bomb:* = SSF2API.generateItem(\"bobomb\", this.randomPoint(), -300, true);\r\n");
                    sb.Append("\t\t\tbomb.setScale(1.2, 1.2);\r\n");
                    sb.Append("\t\t\tbomb.toToss();\r\n");
                    sb.Append("\t\t\tbomb.addToCamera();\r\n");
                    sb.Append("\t\t\tthis.m_bombTimer.reset();\r\n");
                    sb.Append("\t\t}\r\n");
                    sb.Append("\t\tpublic function randomPoint():Number\r\n\t\t{\r\n");
                    sb.Append("\t\t\tvar spots:Array = [0, 80, 160, 240, 320];\r\n");
                    sb.Append("\t\t\treturn spots[int(SSF2API.random() * spots.length)];\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.Bumpers:
                    AppendBumpers(sb);
                    break;
                case EventGoal.Sheik:
                    sb.Append("\t\tpublic function handleSheik():void\r\n\t\t{\r\n");
                    foreach (var slot in CpuSlots(recipe))
                    {
                        sb.Append("\t\t\tvar sheik").Append(slot).Append(":* = SSF2API.getPlayer(").Append(slot).Append(");\r\n");
                        sb.Append("\t\t\tsheik").Append(slot).Append(".setLivesEnabled(sheik").Append(slot).Append(".getCharacterStat(\"statsName\") == \"sheik\");\r\n");
                    }
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.Pokeball:
                    sb.Append("\t\tpublic function pokeBallCheck(param1:Object):Boolean\r\n\t\t{\r\n");
                    sb.Append("\t\t\tif (!param1 || param1.target == null) return false;\r\n");
                    sb.Append("\t\t\tvar kind:String = param1.target.getType();\r\n");
                    sb.Append("\t\t\tif (kind == \"SSF2Item\") return !(param1.target.getItemStat(\"linkage_id\") == \"pokeball\" || param1.target.getOwner().getType() == \"SSF2Enemy\");\r\n");
                    sb.Append("\t\t\tif (kind == \"SSF2Enemy\") return false;\r\n");
                    sb.Append("\t\t\tif (kind == \"SSF2Projectile\") return param1.target.getOwner().getType() != \"SSF2Enemy\";\r\n");
                    sb.Append("\t\t\treturn true;\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.Protoman:
                    sb.Append("\t\tpublic function generateProtoman():void\r\n\t\t{\r\n");
                    sb.Append("\t\t\tif (this.m_assist && !this.m_assist.inState(EState.DEAD)) this.m_assist.destroy();\r\n");
                    sb.Append("\t\t\tthis.m_assist = ").Append(SpawnClass("Protoman")).Append(";\r\n");
                    sb.Append("\t\t\tthis.m_assist.setX(SSF2API.getPlayer(1).getX());\r\n");
                    sb.Append("\t\t\tthis.m_assist.setY(SSF2API.getPlayer(1).getY());\r\n");
                    sb.Append("\t\t\tthis.m_assist.setOwner(SSF2API.getPlayer(1));\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.MasterHand:
                    sb.Append("\t\tpublic function checkMH(param1:*):Boolean\r\n\t\t{\r\n");
                    sb.Append("\t\t\tif (!param1 || !param1.target || param1.target.getType() != \"SSF2Enemy\") return false;\r\n");
                    sb.Append("\t\t\treturn param1.target.getEnemyStat(\"linkage_id\") == \"masterhand_enemy\";\r\n");
                    sb.Append("\t\t}\r\n");
                    sb.Append("\t\tprivate function handDown(who:*):Boolean\r\n\t\t{\r\n");
                    sb.Append("\t\t\treturn !who || who.isDisposed() || who.inState(EState.DEAD);\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
                case EventGoal.RandomItems:
                    AppendRandomItem(sb);
                    break;
                case EventGoal.LoopBackground:
                    sb.Append("\t\tprivate function handleLoopingBackground():void\r\n\t\t{\r\n");
                    sb.Append("\t\t\tvar back:* = SSF2API.getStage().getBackground();\r\n");
                    sb.Append("\t\t\tvar loop1:* = back ? back.getChildByName(\"hrc_groundloop1\") : null;\r\n");
                    sb.Append("\t\t\tvar loop2:* = back ? back.getChildByName(\"hrc_groundloop2\") : null;\r\n");
                    sb.Append("\t\t\tif (!loop1 || !loop2) return;\r\n");
                    sb.Append("\t\t\tvar camX:Number = SSF2API.getCamera().getTopLeftPoint().x - back.x;\r\n");
                    sb.Append("\t\t\tif (camX <= 2220) return;\r\n");
                    sb.Append("\t\t\tvar bounds:* = SSF2API.getStage().getCameraBounds();\r\n");
                    sb.Append("\t\t\tvar view:* = SSF2API.getCamera().getMC();\r\n");
                    sb.Append("\t\t\tif (camX + view.width + 600 > bounds.x + bounds.width + SSF2API.getStage().getMidground().x - back.x)\r\n");
                    sb.Append("\t\t\t\tbounds.width = bounds.width + 2000;\r\n");
                    sb.Append("\t\t\tvar span:Number = 2280;\r\n");
                    sb.Append("\t\t\tloop1.x = 2220 + span * Math.floor((camX - 2220 - 500) / span);\r\n");
                    sb.Append("\t\t\tloop2.x = loop1.x + span;\r\n");
                    sb.Append("\t\t}\r\n");
                    break;
            }
        }

        private static void AppendKoClear(EventRecipe recipe, StringBuilder sb, string pad, string extraFail = "")
        {
            sb.Append(pad).Append("var players:Array = SSF2API.getPlayers();\r\n");
            AppendLoss(recipe, sb, pad, extraFail);
            sb.Append(pad).Append("var done:Boolean = true;\r\n");
            sb.Append(pad).Append("var i:int = 1;\r\n");
            sb.Append(pad).Append("while (i < players.length) { if (players[i].getLives() > 0) done = false; i++; }\r\n");
            sb.Append(pad).Append("if (!done) return;\r\n");
            recipe.AppendSucceed(sb, pad);
        }

        private static void AppendLoss(EventRecipe recipe, StringBuilder sb, string pad, string extraFail = "")
        {
            var parts = new List<string>();
            if (recipe.UsingLives)
                parts.Add("players[0].getLives() <= 0");
            if (recipe.UsingTime)
                parts.Add("SSF2API.getGameTimer().getCurrentTime() <= 0");
            if (extraFail.Length > 0)
                parts.Add(extraFail);
            if (parts.Count == 0)
                return;
            sb.Append(pad).Append("if (").Append(string.Join(" || ", parts)).Append(")\r\n");
            sb.Append(pad).Append("{\r\n");
            recipe.AppendFail(sb, pad + "\t");
            sb.Append(pad).Append("}\r\n");
        }

        private static void AppendSheikTick(EventRecipe recipe, StringBuilder sb, string pad)
        {
            sb.Append(pad).Append("this.transformTimer--;\r\n");
            sb.Append(pad).Append("if (this.transformTimer <= 0)\r\n");
            sb.Append(pad).Append("{\r\n");
            sb.Append(pad).Append("\tvar down:Array = [0, 1, 1088, 20];\r\n");
            foreach (var slot in CpuSlots(recipe))
            {
                sb.Append(pad).Append("\tvar cpu").Append(slot).Append(":* = SSF2API.getPlayer(").Append(slot).Append(");\r\n");
                sb.Append(pad).Append("\tif (cpu").Append(slot).Append(" && !cpu").Append(slot).Append(".inState(CState.DEAD) && SSF2API.random() < 0.45)\r\n");
                sb.Append(pad).Append("\t\tcpu").Append(slot).Append(".importCPUControls(down);\r\n");
            }
            sb.Append(pad).Append("\tthis.transformTimer = 270;\r\n");
            sb.Append(pad).Append("}\r\n");
        }

        private static void AppendSaturnWatch(StringBuilder sb, string pad)
        {
            sb.Append(pad).Append("var saturnI:int = 0;\r\n");
            sb.Append(pad).Append("while (saturnI < this.saturnArray.length)\r\n");
            sb.Append(pad).Append("{\r\n");
            sb.Append(pad).Append("\tvar saturn:* = this.saturnArray[saturnI];\r\n");
            sb.Append(pad).Append("\tif (saturn.inState(IState.DEAD)) this.saturnFailed = true;\r\n");
            sb.Append(pad).Append("\telse { saturn.updateItemStats({ \"time_max\": int.MAX_VALUE }); saturn.resetTime(); }\r\n");
            sb.Append(pad).Append("\tsaturnI++;\r\n");
            sb.Append(pad).Append("}\r\n");
        }

        private static void AppendDirtWatch(StringBuilder sb, string pad)
        {
            sb.Append(pad).Append("var dirtI:int = 0;\r\n");
            sb.Append(pad).Append("while (dirtI < this.dirtArray.length)\r\n");
            sb.Append(pad).Append("{\r\n");
            sb.Append(pad).Append("\tif (this.dirtArray[dirtI].inState(EState.DEAD))\r\n");
            sb.Append(pad).Append("\t{\r\n");
            sb.Append(pad).Append("\t\tthis.dirtArray[dirtI].removeFromCamera();\r\n");
            sb.Append(pad).Append("\t\tthis.dirtArray.splice(dirtI, 1);\r\n");
            sb.Append(pad).Append("\t}\r\n");
            sb.Append(pad).Append("\telse dirtI++;\r\n");
            sb.Append(pad).Append("}\r\n");
        }

        private static void AppendLoop(EventRecipe recipe, StringBuilder sb, string pad)
        {
            sb.Append(pad).Append("this.handleLoopingBackground();\r\n");
            sb.Append(pad).Append("var who:* = SSF2API.getPlayers()[0];\r\n");
            sb.Append(pad).Append("var smash:* = who.getMC();\r\n");
            sb.Append(pad).Append("var stance:* = who.getStanceMC();\r\n");
            sb.Append(pad).Append("if (");
            if (recipe.UsingTime)
                sb.Append("SSF2API.getGameTimer().getCurrentTime() <= 0 || ");
            sb.Append("(smash.currentLabel == \"special\" && stance.currentLabel == \"failed\") || (smash.currentLabel != \"special\" && !who.hasSmashBallAura()))\r\n");
            sb.Append(pad).Append("{\r\n");
            recipe.AppendFail(sb, pad + "\t");
            sb.Append(pad).Append("}\r\n");
            sb.Append(pad).Append("if (smash.currentLabel == \"special\")\r\n");
            sb.Append(pad).Append("{\r\n");
            sb.Append(pad).Append("\tvar cut:* = who.getFinalSmashCutscene();\r\n");
            sb.Append(pad).Append("\tif (cut && cut.currentLabel == \"end\")\r\n");
            sb.Append(pad).Append("\t{\r\n");
            recipe.AppendSucceed(sb, pad + "\t\t");
            sb.Append(pad).Append("\t}\r\n");
            sb.Append(pad).Append("}\r\n");
        }

        private static void AppendCombo(EventRecipe recipe, StringBuilder sb)
        {
            var threshold = recipe.Ranks.Count > 0 ? recipe.Ranks.Min(r => r.Value) : 50;
            sb.Append("\t\tpublic function onStateChange(param1:* = null):void\r\n\t\t{\r\n");
            sb.Append("\t\t\tif (!param1 || !param1.data || !param1.data.caller) return;\r\n");
            sb.Append("\t\t\tvar stunned:Array = [CState.CRASH_GETUP, CState.CRASH_LAND, CState.TECH_GROUND, CState.TECH_ROLL, CState.ROLL, CState.CAUGHT, CState.LAND, CState.INJURED, CState.FROZEN, CState.DIZZY, CState.STUNNED, CState.PITFALL, CState.EGG, CState.FLYING];\r\n");
            sb.Append("\t\t\tif (stunned.indexOf(param1.data.fromState) < 0 || stunned.indexOf(param1.data.toState) >= 0) return;\r\n");
            sb.Append("\t\t\tvar who:* = param1.data.caller;\r\n");
            sb.Append("\t\t\tif (who.getDamage() < ").Append(threshold).Append(")\r\n\t\t\t{\r\n");
            sb.Append("\t\t\t\twho.setDamage(0);\r\n");
            sb.Append("\t\t\t\treturn;\r\n\t\t\t}\r\n");
            sb.Append("\t\t\tvar rank:String = \"F\";\r\n");
            var first = true;
            foreach (var cut in recipe.Ranks)
            {
                sb.Append("\t\t\t").Append(first ? "if" : "else if").Append(" (who.getDamage() >= ").Append(cut.Value).Append(") rank = \"").Append(cut.Letter).Append("\";\r\n");
                first = false;
            }
            sb.Append("\t\t\tmatchData.rank = rank;\r\n");
            sb.Append("\t\t\tmatchData.success = true;\r\n");
            sb.Append("\t\t\tmatchData.scoreType = \"").Append(recipe.ScoreType).Append("\";\r\n");
            sb.Append("\t\t\tmatchData.score = who.getDamage();\r\n");
            if (recipe.RecordStock)
                sb.Append("\t\t\tmatchData.stock = SSF2API.getPlayers()[0].getLives();\r\n");
            if (recipe.RecordFps)
                sb.Append("\t\t\tmatchData.fps = SSF2API.getAverageFPS();\r\n");
            sb.Append("\t\t\tSSF2API.endGame({ \"success\": true, \"immediate\": false });\r\n");
            sb.Append("\t\t}\r\n");
        }

        private static void AppendBumpers(StringBuilder sb)
        {
            (string Flag, string Name)[] bumps =
            {
                ("bumper1", "Bumper1_B"),
                ("bumper2", "Bumper1_T"),
                ("bumper3", "Bumper2_L"),
                ("bumper4", "Bumper2_R"),
                ("bumper5", "Bumper3_L"),
                ("bumper6", "Bumper3_R")
            };
            sb.Append("\t\tpublic function checkBumper(param1:*):void\r\n\t\t{\r\n");
            sb.Append("\t\t\tif (!param1 || !param1.data || !param1.data.attackBoxData || !param1.data.attackBoxData.metadata) return;\r\n");
            sb.Append("\t\t\tvar name:String = param1.data.attackBoxData.metadata.bumperName;\r\n");
            sb.Append("\t\t\tvar hit:* = param1.data.attackBoxData.metadata.bumper;\r\n");
            sb.Append("\t\t\tif (!name) return;\r\n");
            var first = true;
            foreach (var bump in bumps)
            {
                sb.Append("\t\t\t").Append(first ? "if" : "else if").Append(" (name == \"").Append(bump.Name).Append("\" && !this.").Append(bump.Flag).Append(")\r\n");
                sb.Append("\t\t\t{\r\n");
                sb.Append("\t\t\t\tthis.").Append(bump.Flag).Append(" = true;\r\n");
                sb.Append("\t\t\t\tvar clip:* = SSF2API.getStage().getMidground()[name];\r\n");
                sb.Append("\t\t\t\tif (clip) clip.visible = false;\r\n");
                sb.Append("\t\t\t\tif (hit) hit.destroy();\r\n");
                sb.Append("\t\t\t}\r\n");
                first = false;
            }
            sb.Append("\t\t}\r\n");
        }

        private static void AppendRandomItem(StringBuilder sb)
        {
            sb.Append("\t\tpublic function randomItem():void\r\n\t\t{\r\n");
            sb.Append("\t\t\tvar held:Array = [\"trophystand\", \"fan\", \"beamsword\", \"homerunbat\", \"starrod\", \"beamrod\", \"bobomb\", \"mrsaturn\", \"greenShell\", \"fireFlower\"];\r\n");
            sb.Append("\t\t\tvar n:int = 0;\r\n");
            sb.Append("\t\t\tvar players:Array = SSF2API.getPlayers();\r\n");
            sb.Append("\t\t\twhile (n < players.length)\r\n\t\t\t{\r\n");
            sb.Append("\t\t\t\tvar who:* = players[n];\r\n");
            sb.Append("\t\t\t\tif (!who.getItem() && who.getLives() > 0 && !who.inState(CState.DEAD) && who.getCharacterStat(\"canHoldItems\"))\r\n");
            sb.Append("\t\t\t\t\twho.generateItem(held[SSF2API.randomInteger(0, held.length)], true, false, true);\r\n");
            sb.Append("\t\t\t\tn++;\r\n\t\t\t}\r\n");
            sb.Append("\t\t}\r\n");
            sb.Append("\t\tpublic function setTimeMax(param1:*):void\r\n\t\t{\r\n");
            sb.Append("\t\t\tif (!param1 || !param1.data || !param1.data.item) return;\r\n");
            sb.Append("\t\t\tvar limit:int = param1.data.item.getItemStat(\"linkage_id\") == \"motionSensorBomb\" ? 300 : 1;\r\n");
            sb.Append("\t\t\tparam1.data.item.updateItemStats({ \"time_max\": limit });\r\n");
            sb.Append("\t\t\tparam1.data.item.resetTime();\r\n");
            sb.Append("\t\t}\r\n");
        }

        private static IEnumerable<string> WantedFields(EventGoal goal)
        {
            switch (goal)
            {
                case EventGoal.Bombs:
                    yield return "private var m_bombTimer:*;";
                    break;
                case EventGoal.Bumpers:
                    yield return "public var bumper1:Boolean;";
                    yield return "public var bumper2:Boolean;";
                    yield return "public var bumper3:Boolean;";
                    yield return "public var bumper4:Boolean;";
                    yield return "public var bumper5:Boolean;";
                    yield return "public var bumper6:Boolean;";
                    break;
                case EventGoal.Sheik:
                    yield return "private var transformTimer:int;";
                    break;
                case EventGoal.Saturns:
                    yield return "private var saturnArray:Array = [];";
                    yield return "private var saturnFailed:Boolean = false;";
                    break;
                case EventGoal.Dirt:
                    yield return "private var dirtArray:Array = [];";
                    break;
                case EventGoal.Protoman:
                    yield return "internal var m_assist:* = null;";
                    break;
                case EventGoal.MotherBrain:
                    yield return "internal var motherBrain:*;";
                    break;
                case EventGoal.MasterHand:
                    yield return "private var enemy:*;";
                    yield return "private var enemy2:*;";
                    break;
            }
        }

        private static void NeedItem(EventRecipe recipe, string id, string frequencyIfOff)
        {
            recipe.BannedItems.RemoveAll(item => item.Equals(id, StringComparison.OrdinalIgnoreCase));
            if (!recipe.RequiredItems.Any(item => item.Equals(id, StringComparison.OrdinalIgnoreCase)))
                recipe.RequiredItems.Add(id);
            if (recipe.ItemFrequency == "OFF")
                recipe.ItemFrequency = frequencyIfOff;
        }

        private static void EnsureCpu(EventRecipe recipe, string character)
        {
            if (recipe.Fighters.Any(f => !f.Human && f.Character.Length > 0) || recipe.Fighters.Count >= 4)
                return;
            recipe.Fighters.Add(new EventFighter
            {
                Character = character,
                Human = false,
                Level = 9,
                Lives = recipe.Lives > 0 ? recipe.Lives : 1
            });
        }

        private static void DisarmLives(EventRecipe recipe)
        {
            foreach (var cpu in recipe.Fighters.Where(f => !f.Human))
                cpu.LivesEnabled = false;
        }

        private static IEnumerable<int> CpuSlots(EventRecipe recipe)
        {
            for (var i = 0; i < recipe.Fighters.Count; i++)
            {
                if (!recipe.Fighters[i].Human)
                    yield return i + 1;
            }
        }

        private static List<RankCut> Cuts(params int[] values)
        {
            var list = new List<RankCut>();
            for (var i = 0; i < values.Length && i < EventRecipe.RankLetters.Length; i++)
                list.Add(new RankCut { Letter = EventRecipe.RankLetters[i], Value = values[i] });
            return list;
        }

        private static string FieldName(string field)
        {
            var match = Regex.Match(field ?? "", @"\bvar\s+([A-Za-z_][A-Za-z0-9_]*)\b");
            return match.Success ? match.Groups[1].Value : "";
        }
    }
}
