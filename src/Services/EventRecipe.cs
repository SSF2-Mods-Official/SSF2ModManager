using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    public enum EventKind
    {
        Unknown,
        Bout,
        MultiMan,
        AllStarBattle,
        AllStarTarget
    }

    public enum EventGoal
    {
        Stocks,
        Tag,
        Combo,
        Bat,
        Bombs,
        Bumpers,
        Sheik,
        Saturns,
        Dirt,
        Pokeball,
        Protoman,
        MotherBrain,
        MasterHand,
        RandomItems,
        LoopBackground
    }

    public class RankCut
    {
        public string Letter { get; set; } = "";
        public int Value { get; set; }
    }

    public class EventFighter
    {
        public string Character { get; set; } = "";
        public string Stage { get; set; } = "";
        public bool Human { get; set; }
        public int Level { get; set; } = 9;
        public int Lives { get; set; } = 1;
        public bool CostumeFromPlayer { get; set; }
        public int Costume { get; set; } = -1;
        public int Team { get; set; } = -1;
        public int SizeStatus { get; set; }
        public bool Metal { get; set; }
        public double Scale { get; set; } = 1;
        public double DamageRatio { get; set; } = 1;
        public double AttackRatio { get; set; } = 1;
        public string CpuAction { get; set; } = "";
        public bool SpecialEvent { get; set; }
        public bool MovesLocked { get; set; }
        public bool CharacterFromPlayer { get; set; }
        public bool LivesEnabled { get; set; } = true;
    }

    /// <summary>
    /// The fields Make a new event can show and write. Bout, multi-man, all-star battle, and target race.
    /// Record cuts are the S–E thresholds next to <c>matchData.rank</c>.
    /// </summary>
    public class EventRecipe
    {
        public EventKind Kind { get; set; } = EventKind.Bout;
        public string Stage { get; set; } = "";
        /// <summary>Camera background stage id. Written on the pack list as <c>stageBackground</c>.</summary>
        public string StageBackground { get; set; } = "";
        public string Music { get; set; } = "";
        public string ScoreType { get; set; } = "time";
        /// <summary>framesAtMost, framesAtLeast, damageAtMost, damageAtLeast, countAtLeast, or perMatch.</summary>
        public string RankMeasure { get; set; } = "framesAtMost";
        public List<RankCut> Ranks { get; set; } = new();
        public List<EventFighter> Fighters { get; set; } = new();
        public bool UsingLives { get; set; } = true;
        public bool UsingTime { get; set; }
        public int Lives { get; set; } = 1;
        public int Minutes { get; set; } = 2;
        public bool Hazards { get; set; } = true;
        public bool TeamDamage { get; set; }
        public string ItemFrequency { get; set; } = "OFF";
        public int PlayerLives { get; set; } = 1;
        public int CpuLevel { get; set; } = 9;
        public int BodyCount { get; set; }
        public int SpawnCount { get; set; }
        public int MaxOnScreen { get; set; } = 1;
        public double AttackRatio { get; set; } = 1;
        public double DifficultyDamage { get; set; } = 1;
        public bool HalfTimer { get; set; }
        public bool ClearItems { get; set; }
        public string WinCondition { get; set; } = "KO_BASED";
        public int StockOverride { get; set; }
        public string Unlock { get; set; } = "";
        public bool RecordStock { get; set; }
        public bool RecordFps { get; set; }
        public List<string> ExtraMethods { get; set; } = new();
        public List<string> Imports { get; set; } = new();
        public List<string> Fields { get; set; } = new();
        public EventGoal Goal { get; set; } = EventGoal.Stocks;
        public List<string> BannedItems { get; set; } = new();
        public List<string> RequiredItems { get; set; } = new();

        public static readonly string[] ScoreTypes = { "time", "kos", "points", "damage" };
        public static readonly string[] Frequencies = { "OFF", "LOW", "MEDIUM", "HIGH", "VERY_HIGH", "MAX", "ULTRA_HIGH" };
        public static readonly string[] RankLetters = { "S", "A", "B", "C", "D", "E" };
        public static readonly string[] RankMeasures = { "framesAtMost", "framesAtLeast", "damageAtMost", "damageAtLeast", "countAtLeast", "perMatch" };
        public static readonly string[] CpuActions = { "", "EVADE", "FORCE_RUN", "FORCE_WALK", "FORCE_DO_NOTHING" };
        public static readonly string[] Goals = Enum.GetNames(typeof(EventGoal));
        /// <summary>Item ids from <c>data/misc/items.swf</c>, the name cluster before <c>SSF2Asset</c>.</summary>
        public static readonly string[] KnownItems =
        {
            "assistTrophy", "beamrod", "beamsword", "blastbox", "bloodsword", "bobomb", "bomber",
            "bumper", "capsule", "coconutGun", "crate", "cucco", "dekuNut", "energytank", "explodingtag",
            "fan", "fireFlower", "firework", "flipper", "fooditem", "freezie", "gooeybomb", "greenShell",
            "gustJar", "heartContainer", "homerunbat", "iceFlower", "icewing", "masterball", "maximumtomato",
            "medusahead", "metal_box", "motionSensorBomb", "mrsaturn", "oreclub", "partyball", "pitfall",
            "poisonBomb", "poisondagger", "poisonmushroom", "pokeball", "potion", "pow_block", "rayGun",
            "redShell", "screw_attack", "shield_monitor", "smartbomb", "smashball", "spinyShell", "starman",
            "starrod", "supermushroom", "transport_item", "trophystand", "unira", "yellowShell"
        };

        public bool SameOptions(EventRecipe other)
        {
            if (other == null) return false;
            if (Kind != other.Kind || Stage != other.Stage || Music != other.Music) return false;
            if (ScoreType != other.ScoreType || RankMeasure != other.RankMeasure) return false;
            if (UsingLives != other.UsingLives || UsingTime != other.UsingTime) return false;
            if (Lives != other.Lives || Minutes != other.Minutes) return false;
            if (Hazards != other.Hazards || TeamDamage != other.TeamDamage) return false;
            if (ItemFrequency != other.ItemFrequency) return false;
            if (PlayerLives != other.PlayerLives || CpuLevel != other.CpuLevel) return false;
            if (BodyCount != other.BodyCount || SpawnCount != other.SpawnCount || MaxOnScreen != other.MaxOnScreen) return false;
            if (WinCondition != other.WinCondition || StockOverride != other.StockOverride) return false;
            if (Unlock != other.Unlock || RecordStock != other.RecordStock || RecordFps != other.RecordFps) return false;
            if (HalfTimer != other.HalfTimer || ClearItems != other.ClearItems || Goal != other.Goal) return false;
            if (Math.Abs(AttackRatio - other.AttackRatio) > 0.001 || Math.Abs(DifficultyDamage - other.DifficultyDamage) > 0.001) return false;
            if (!BannedItems.SequenceEqual(other.BannedItems) || !RequiredItems.SequenceEqual(other.RequiredItems)) return false;
            if (Ranks.Count != other.Ranks.Count) return false;
            for (var i = 0; i < Ranks.Count; i++)
            {
                if (Ranks[i].Letter != other.Ranks[i].Letter || Ranks[i].Value != other.Ranks[i].Value)
                    return false;
            }
            if (Fighters.Count != other.Fighters.Count) return false;
            for (var i = 0; i < Fighters.Count; i++)
            {
                if (!Fighters[i].Character.Equals(other.Fighters[i].Character, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (!Fighters[i].Stage.Equals(other.Fighters[i].Stage, StringComparison.OrdinalIgnoreCase))
                    return false;
                if (Fighters[i].Human != other.Fighters[i].Human || Fighters[i].Level != other.Fighters[i].Level)
                    return false;
                if (Fighters[i].Lives != other.Fighters[i].Lives || Fighters[i].Costume != other.Fighters[i].Costume)
                    return false;
                if (Fighters[i].CostumeFromPlayer != other.Fighters[i].CostumeFromPlayer || Fighters[i].Team != other.Fighters[i].Team)
                    return false;
                if (Fighters[i].SizeStatus != other.Fighters[i].SizeStatus || Fighters[i].Metal != other.Fighters[i].Metal)
                    return false;
                if (Math.Abs(Fighters[i].Scale - other.Fighters[i].Scale) > 0.001) return false;
                if (Math.Abs(Fighters[i].DamageRatio - other.Fighters[i].DamageRatio) > 0.001) return false;
                if (Math.Abs(Fighters[i].AttackRatio - other.Fighters[i].AttackRatio) > 0.001) return false;
                if (Fighters[i].CpuAction != other.Fighters[i].CpuAction || Fighters[i].SpecialEvent != other.Fighters[i].SpecialEvent)
                    return false;
                if (Fighters[i].MovesLocked != other.Fighters[i].MovesLocked) return false;
                if (Fighters[i].CharacterFromPlayer != other.Fighters[i].CharacterFromPlayer) return false;
                if (Fighters[i].LivesEnabled != other.Fighters[i].LivesEnabled) return false;
            }
            return true;
        }

        public string Difference(EventRecipe other)
        {
            var bits = new List<string>();
            void Add(string name, object? a, object? b)
            {
                if (!Equals(a, b)) bits.Add(name + " " + a + " -> " + b);
            }
            Add("kind", Kind, other.Kind);
            Add("stage", Stage, other.Stage);
            Add("stageBackground", StageBackground, other.StageBackground);
            Add("music", Music, other.Music);
            Add("score", ScoreType, other.ScoreType);
            Add("rank", RankMeasure, other.RankMeasure);
            Add("lives", Lives, other.Lives);
            Add("minutes", Minutes, other.Minutes);
            Add("usingLives", UsingLives, other.UsingLives);
            Add("usingTime", UsingTime, other.UsingTime);
            Add("hazards", Hazards, other.Hazards);
            Add("teamDamage", TeamDamage, other.TeamDamage);
            if (Math.Abs(AttackRatio - other.AttackRatio) > 0.001) bits.Add("attack " + AttackRatio + " -> " + other.AttackRatio);
            if (Math.Abs(DifficultyDamage - other.DifficultyDamage) > 0.001) bits.Add("taken " + DifficultyDamage + " -> " + other.DifficultyDamage);
            Add("items", ItemFrequency, other.ItemFrequency);
            Add("goal", Goal, other.Goal);
            Add("clear", ClearItems, other.ClearItems);
            Add("half", HalfTimer, other.HalfTimer);
            Add("bodies", BodyCount, other.BodyCount);
            Add("spawn", SpawnCount, other.SpawnCount);
            Add("screen", MaxOnScreen, other.MaxOnScreen);
            Add("playerLives", PlayerLives, other.PlayerLives);
            Add("cpu", CpuLevel, other.CpuLevel);
            Add("win", WinCondition, other.WinCondition);
            Add("stocks", StockOverride, other.StockOverride);
            Add("unlock", Unlock, other.Unlock);
            if (ExtraMethods.Count != other.ExtraMethods.Count)
                bits.Add("extras " + ExtraMethods.Count + " -> " + other.ExtraMethods.Count);
            if (!BannedItems.SequenceEqual(other.BannedItems))
                bits.Add("banned " + string.Join(",", BannedItems) + " -> " + string.Join(",", other.BannedItems));
            if (!RequiredItems.SequenceEqual(other.RequiredItems))
                bits.Add("need " + string.Join(",", RequiredItems) + " -> " + string.Join(",", other.RequiredItems));
            if (Ranks.Count != other.Ranks.Count || Ranks.Where((r, i) => i < other.Ranks.Count && (r.Letter != other.Ranks[i].Letter || r.Value != other.Ranks[i].Value)).Any())
                bits.Add("ranks " + string.Join(",", Ranks.Select(r => r.Letter + r.Value)) + " -> " + string.Join(",", other.Ranks.Select(r => r.Letter + r.Value)));
            Add("fighters", Fighters.Count, other.Fighters.Count);
            for (var i = 0; i < Math.Min(Fighters.Count, other.Fighters.Count); i++)
            {
                var a = Fighters[i];
                var b = other.Fighters[i];
                if (a.Character != b.Character || a.Stage != b.Stage || a.Human != b.Human || a.Level != b.Level || a.Lives != b.Lives
                    || a.Costume != b.Costume || a.CostumeFromPlayer != b.CostumeFromPlayer || a.Team != b.Team
                    || a.SizeStatus != b.SizeStatus || a.Metal != b.Metal || Math.Abs(a.Scale - b.Scale) > 0.001
                    || Math.Abs(a.DamageRatio - b.DamageRatio) > 0.001 || Math.Abs(a.AttackRatio - b.AttackRatio) > 0.001 || a.CpuAction != b.CpuAction
                    || a.SpecialEvent != b.SpecialEvent || a.MovesLocked != b.MovesLocked
                    || a.CharacterFromPlayer != b.CharacterFromPlayer || a.LivesEnabled != b.LivesEnabled)
                    bits.Add("p" + (i + 1) + " " + a.Character + " team " + a.Team + " size " + a.SizeStatus + " metal " + a.Metal
                        + " scale " + a.Scale + " cpu " + a.CpuAction + " lives " + a.Lives + " on " + a.LivesEnabled
                        + " -> " + b.Character + " team " + b.Team + " size " + b.SizeStatus + " cpu " + b.CpuAction + " lives " + b.Lives + " on " + b.LivesEnabled);
            }
            return bits.Count == 0 ? "same" : string.Join("; ", bits);
        }

        public static EventRecipe Parse(string source)
        {
            source ??= "";
            var recipe = new EventRecipe();
            if (source.Contains("extends EventMode_AllStarBattleMatch"))
                recipe.Kind = EventKind.AllStarBattle;
            else if (source.Contains("extends EventMode_AllStarTargetMatch"))
                recipe.Kind = EventKind.AllStarTarget;
            else if (source.Contains("extends SSF2MultiManMatch"))
                recipe.Kind = EventKind.MultiMan;
            else if (source.Contains("extends SSF2CustomMatch"))
                recipe.Kind = EventKind.Bout;
            else
                recipe.Kind = EventKind.Unknown;

            recipe.Stage = First(source, @"levelData\.stage\s*=\s*""([^""]+)""")
                ?? First(source, @"stageOverride\s*=\s*""([^""]+)""")
                ?? "";
            recipe.StageBackground = First(source, @"""stageBackground""\s*:\s*""([^""]+)""") ?? "";
            if (recipe.StageBackground.StartsWith("pack:", StringComparison.Ordinal))
                recipe.StageBackground = "";
            recipe.Music = First(source, @"musicOverride\s*=\s*""([^""]+)""") ?? "";
            var score = First(source, @"scoreType\s*=\s*""([^""]+)""");
            recipe.ScoreType = score ?? (recipe.Kind is EventKind.AllStarBattle or EventKind.AllStarTarget ? "time" : "time");
            if (recipe.Kind is EventKind.AllStarBattle or EventKind.AllStarTarget && score == null)
                recipe.RankMeasure = "perMatch";

            recipe.UsingLives = !source.Contains("usingLives = false") && !source.Contains("usingLives=false");
            recipe.UsingTime = source.Contains("usingTime = true") || source.Contains("usingTime=true");
            recipe.Hazards = !source.Contains("hazards = false") && !source.Contains("hazards=false");
            recipe.TeamDamage = source.Contains("teamDamage = true") || source.Contains("teamDamage=true");
            if (int.TryParse(First(source, @"levelData\.lives\s*=\s*(\d+)"), out var lives))
                recipe.Lives = lives;
            if (int.TryParse(First(source, @"levelData\.time\s*=\s*(\d+)"), out var minutes))
                recipe.Minutes = minutes;
            else if (int.TryParse(First(source, @"maxTime\s*=\s*(\d+)"), out var maxTime))
                recipe.Minutes = maxTime;
            if (source.Contains("TIMED_KO_EVENT")) recipe.WinCondition = "TIMED_KO_EVENT";
            else if (source.Contains("SSF2MultiManMatch.SURVIVAL")) recipe.WinCondition = "SURVIVAL";
            else if (source.Contains("KO_BASED")) recipe.WinCondition = "KO_BASED";
            else if (source.Contains("SSF2MultiManMatch.TIMED")) recipe.WinCondition = "TIMED";
            if (int.TryParse(First(source, @"stockOverride\s*=\s*(\d+)"), out var stocks))
                recipe.StockOverride = stocks;
            recipe.Unlock = First(source, @"triggerUnlock\(Unlockable\.([A-Za-z0-9_]+)\)") ?? "";
            recipe.RecordStock = source.Contains("matchData.stock");
            recipe.RecordFps = source.Contains("getAverageFPS");
            var difficultyEarly = Regex.Match(source, @"difficultySettings\s*=\s*\{([\s\S]*?)\}");
            if (difficultyEarly.Success && int.TryParse(First(difficultyEarly.Groups[1].Value, @"""level""\s*:\s*(\d+)"), out var dlevel))
                recipe.CpuLevel = dlevel;
            var freq = First(source, @"FREQUENCY_([A-Z_]+)");
            if (!string.IsNullOrEmpty(freq))
                recipe.ItemFrequency = freq;
            if (int.TryParse(First(source, @"playerLives\s*=\s*(\d+)"), out var pl))
                recipe.PlayerLives = pl;
            if (int.TryParse(First(source, @"cpuLevel\s*=\s*(\d+)"), out var cpu))
                recipe.CpuLevel = cpu;
            if (int.TryParse(First(source, @"bodyCount\s*=\s*(\d+)"), out var bodies))
                recipe.BodyCount = bodies;
            if (int.TryParse(First(source, @"spawnCount\s*=\s*(\d+)"), out var spawns))
                recipe.SpawnCount = spawns;
            if (int.TryParse(First(source, @"MAX_ENEMIES_ON_SCREEN\s*=\s*(\d+)"), out var max))
                recipe.MaxOnScreen = max;

            if (recipe.Kind == EventKind.MultiMan)
            {
                var player = First(source, @"playerCharOverride\s*=\s*""([^""]+)""") ?? "";
                if (player.Length > 0)
                    recipe.Fighters.Add(new EventFighter { Character = player, Human = true, Lives = recipe.PlayerLives });
                var array = First(source, @"charArray\s*=\s*\[([^\]]*)\]");
                if (array != null)
                {
                    foreach (Match name in Regex.Matches(array, @"""([^""]+)"""))
                        recipe.Fighters.Add(new EventFighter { Character = name.Groups[1].Value, Human = false, Level = recipe.CpuLevel });
                }
            }
            else if (recipe.Kind is EventKind.AllStarBattle or EventKind.AllStarTarget)
            {
                foreach (Match wave in Regex.Matches(source, @"\{([^{}]*)\}"))
                {
                    var body = wave.Groups[1].Value;
                    var who = First(body, @"""(?:opponent|targetMC)""\s*:\s*""([^""]+)""");
                    var stage = First(body, @"""stage""\s*:\s*""([^""]+)""");
                    if (who == null && stage == null)
                        continue;
                    recipe.Fighters.Add(new EventFighter
                    {
                        Character = who ?? "",
                        Stage = stage ?? "",
                        Human = false,
                        Level = recipe.CpuLevel,
                        Lives = recipe.PlayerLives
                    });
                }
                if (recipe.Stage.Length == 0 && recipe.Fighters.Count > 0)
                    recipe.Stage = recipe.Fighters[0].Stage;
            }
            else
            {
                foreach (Match push in Regex.Matches(source, @"playerSettings\.push\(\s*\{([\s\S]*?)\}\s*\)"))
                {
                    var body = push.Groups[1].Value;
                    var fromPlayer = Regex.IsMatch(body, @"""character""\s*:\s*(?:param1|initSettings)\.playerSettings");
                    var who = First(body, @"""character""\s*:\s*""([^""]+)""");
                    if (who == null && !fromPlayer)
                        continue;
                    var fighter = new EventFighter
                    {
                        Character = fromPlayer ? "(player)" : who!,
                        CharacterFromPlayer = fromPlayer,
                        Human = Regex.IsMatch(body, @"""human""\s*:\s*true"),
                        Lives = int.TryParse(First(body, @"""lives""\s*:\s*(\d+)"), out var lv) ? lv : 1
                    };
                    if (int.TryParse(First(body, @"""level""\s*:\s*(\d+)"), out var level))
                        fighter.Level = level;
                    if (Regex.IsMatch(body, @"""costume""\s*:\s*(?:param1|initSettings)"))
                        fighter.CostumeFromPlayer = true;
                    else if (int.TryParse(First(body, @"""costume""\s*:\s*(-?\d+)"), out var costume))
                        fighter.Costume = costume;
                    if (int.TryParse(First(body, @"""team""\s*:\s*(-?\d+)"), out var team))
                        fighter.Team = team;
                    if (double.TryParse(First(body, @"""damageRatio""\s*:\s*([0-9.]+)"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var ratio))
                        fighter.DamageRatio = ratio;
                    if (double.TryParse(First(body, @"""attackRatio""\s*:\s*([0-9.]+)"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var atk))
                        fighter.AttackRatio = atk;
                    recipe.Fighters.Add(fighter);
                }
            }

            ReadFighterMods(source, recipe);
            ReadItems(source, recipe);
            ReadGoal(source, recipe);
            var difficulty = Regex.Match(source, @"difficultySettings\s*=\s*\{([\s\S]*?)\}");
            if (difficulty.Success && double.TryParse(First(difficulty.Groups[1].Value, @"""attackRatio""\s*:\s*([0-9.]+)"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var attack))
                recipe.AttackRatio = attack;
            if (difficulty.Success && double.TryParse(First(difficulty.Groups[1].Value, @"""damageRatio""\s*:\s*([0-9.]+)"), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var diffDmg))
                recipe.DifficultyDamage = diffDmg;
            recipe.HalfTimer = source.Contains("getCurrentTime() / 2");
            ReadRanks(source, recipe);
            if (recipe.Kind is EventKind.AllStarBattle or EventKind.AllStarTarget && recipe.Ranks.Count == 0)
            {
                recipe.RankMeasure = "perMatch";
                recipe.ScoreType = "time";
                int[] cuts = { 300, 550, 900, 1300, 1600, 2500 };
                for (var i = 0; i < cuts.Length; i++)
                    recipe.Ranks.Add(new RankCut { Letter = RankLetters[i], Value = cuts[i] });
            }
            ReadExtras(source, recipe);
            foreach (Match import in Regex.Matches(source, @"import\s+[\w.]+\s*;"))
            {
                var line = import.Value.Trim();
                if (!recipe.Imports.Contains(line))
                    recipe.Imports.Add(line);
            }
            foreach (Match field in Regex.Matches(source, @"(?:public|private|protected|internal)\s+var\s+[A-Za-z_][A-Za-z0-9_]*\s*:[^;]+;"))
            {
                if (DepthAt(source, field.Index) != 2) continue;
                if (field.Value.Contains("eventinfo")) continue;
                recipe.Fields.Add(field.Value.Trim());
            }
            return recipe;
        }

        public string WriteSource(string className, EventInfoDraft draft)
        {
            _ = draft;
            var baseName = Kind switch
            {
                EventKind.MultiMan => "SSF2MultiManMatch",
                EventKind.AllStarBattle => "EventMode_AllStarBattleMatch",
                EventKind.AllStarTarget => "EventMode_AllStarTargetMatch",
                _ => "SSF2CustomMatch"
            };
            var sb = new StringBuilder();
            sb.Append("package\r\n{\r\n");
            foreach (var import in Imports)
                sb.Append("\t").Append(import).Append("\r\n");
            if (Imports.Count > 0)
                sb.Append("\r\n");
            sb.Append("\tpublic class ").Append(className).Append(" extends ").Append(baseName).Append("\r\n\t{\r\n");
            var fields = EventGoalRules.DeclareFields(this).ToList();
            foreach (var field in fields)
                sb.Append("\t\t").Append(field).Append("\r\n");
            if (fields.Count > 0)
                sb.Append("\r\n");
            sb.Append("\t\tpublic function ").Append(className).Append("(api:*)\r\n\t\t{\r\n\t\t\tsuper(api);\r\n");
            if (Kind is EventKind.AllStarBattle or EventKind.AllStarTarget)
            {
                sb.Append("\t\t\tplayerLives = ").Append(PlayerLives).Append(";\r\n");
                if (Kind == EventKind.AllStarBattle)
                    sb.Append("\t\t\tcpuLevel = ").Append(CpuLevel).Append(";\r\n");
                sb.Append("\t\t\tmatchArray = [");
                for (var i = 0; i < Fighters.Count; i++)
                {
                    if (i > 0) sb.Append(',');
                    var f = Fighters[i];
                    if (Kind == EventKind.AllStarTarget)
                        sb.Append("{ \"targetMC\": \"").Append(Esc(f.Character)).Append("\", \"stage\": \"").Append(Esc(f.Stage)).Append("\" }");
                    else
                        sb.Append("{ \"opponent\": \"").Append(Esc(f.Character)).Append("\", \"stage\": \"").Append(Esc(f.Stage)).Append("\" }");
                }
                sb.Append("];\r\n");
            }
            sb.Append("\t\t}\r\n\r\n");
            if (Kind == EventKind.Bout || Kind == EventKind.Unknown)
                AppendInitialize(sb);
            if (Kind == EventKind.MultiMan)
                AppendMultiMan(sb);
            else if (Kind == EventKind.Bout || Kind == EventKind.Unknown)
                AppendBout(sb);
            else if (Ranks.Count > 0 && RankMeasure == "perMatch")
                AppendPerWaveRank(sb);
            else if (Ranks.Count > 0)
                AppendRankFunction(sb, "getRank");
            AppendGoal(sb);
            foreach (var method in ExtraMethods)
            {
                if (EventGoalRules.DropExtra(Goal, Kind, method))
                    continue;
                sb.Append("\r\n\t\t").Append(method.Trim()).Append("\r\n");
            }
            sb.Append("\t}\r\n}\r\n");
            return sb.ToString();
        }

        private void AppendInitialize(StringBuilder sb)
        {
            if (HasExtra("initialize") && Goal == EventGoal.Stocks)
                return;
            var mods = false;
            for (var i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                if (f.SizeStatus != 0 || f.Metal || Math.Abs(f.Scale - 1) > 0.001 || !string.IsNullOrEmpty(f.CpuAction)
                    || f.SpecialEvent || f.MovesLocked || !f.LivesEnabled)
                    mods = true;
            }
            if (Goal == EventGoal.Bat || EventGoalRules.NeedsInit(Goal))
                mods = true;
            if (!mods)
                return;
            sb.Append("\t\toverride public function initialize():void\r\n\t\t{\r\n");
            if (Goal == EventGoal.Bat && Fighters.Count > 0)
                sb.Append("\t\t\tSSF2API.getPlayer(1).generateItem(\"homerunbat\", true, false, true);\r\n");
            for (var i = 0; i < Fighters.Count; i++)
            {
                var f = Fighters[i];
                var slot = "SSF2API.getPlayer(" + (i + 1) + ")";
                if (f.SizeStatus != 0)
                {
                    sb.Append("\t\t\t").Append(slot).Append(".setSizeStatus(").Append(f.SizeStatus).Append(");\r\n");
                    sb.Append("\t\t\t").Append(slot).Append(".lockSizeStatus(true);\r\n");
                }
                if (f.Metal)
                    sb.Append("\t\t\t").Append(slot).Append(".setMetalStatus(true);\r\n");
                if (Math.Abs(f.Scale - 1) > 0.001)
                {
                    var scale = f.Scale.ToString(System.Globalization.CultureInfo.InvariantCulture);
                    sb.Append("\t\t\t").Append(slot).Append(".setScale(").Append(scale).Append(", ").Append(scale).Append(");\r\n");
                }
                if (!string.IsNullOrEmpty(f.CpuAction))
                    sb.Append("\t\t\t").Append(slot).Append(".setCPUForcedAction(CPUState.").Append(f.CpuAction).Append(");\r\n");
                if (f.SpecialEvent)
                    sb.Append("\t\t\t").Append(slot).Append(".setSpecialEvent(true);\r\n");
                if (!f.LivesEnabled)
                {
                    sb.Append("\t\t\t").Append(slot).Append(".setLivesEnabled(false);\r\n");
                    if (Goal == EventGoal.Tag)
                        sb.Append("\t\t\t").Append(slot).Append(".addEventListener(SSF2Event.CHAR_KO_DEATH, this.checkTag, { \"persistent\": true });\r\n");
                    if (Goal == EventGoal.Bat)
                        sb.Append("\t\t\t").Append(slot).Append(".addEventListener(SSF2Event.CHAR_KO_DEATH, this.checkBat, { \"persistent\": true });\r\n");
                }
            }
            if (Fighters.Any(f => f.MovesLocked))
                sb.Append("\t\t\tthis.disableMoves();\r\n");
            EventGoalRules.AppendInit(this, sb);
            sb.Append("\t\t}\r\n\r\n");
            if (Fighters.Any(f => f.MovesLocked) && !HasExtra("disableMoves"))
                sb.Append("\t\tprivate function disableMoves(param1:* = null):void\r\n\t\t{\r\n\t\t}\r\n\r\n");
        }

        private void AppendBout(StringBuilder sb)
        {
            sb.Append("\t\toverride public function matchSetup(initSettings:Object):Object\r\n\t\t{\r\n");
            sb.Append("\t\t\tvar game:Object = { \"playerSettings\": [], \"levelData\": initSettings.levelData, \"items\": initSettings.items };\r\n");
            foreach (var f in Fighters)
            {
                sb.Append("\t\t\tgame.playerSettings.push({ \"character\": ");
                if (f.CharacterFromPlayer)
                    sb.Append("initSettings.playerSettings[0].character");
                else
                    sb.Append("\"").Append(Esc(f.Character)).Append("\"");
                sb.Append(", \"lives\": ").Append(f.Lives);
                sb.Append(", \"human\": ").Append(f.Human ? "true" : "false");
                if (!f.Human)
                    sb.Append(", \"level\": ").Append(f.Level);
                if (f.CostumeFromPlayer)
                    sb.Append(", \"costume\": initSettings.playerSettings[0].costume");
                else
                    sb.Append(", \"costume\": ").Append(f.Costume);
                sb.Append(", \"team\": ").Append(f.Team);
                if (Math.Abs(f.DamageRatio - 1) > 0.001)
                    sb.Append(", \"damageRatio\": ").Append(f.DamageRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
                if (Math.Abs(f.AttackRatio - 1) > 0.001)
                    sb.Append(", \"attackRatio\": ").Append(f.AttackRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
                sb.Append(" });\r\n");
            }
            sb.Append("\t\t\tgame.levelData.usingLives = ").Append(UsingLives ? "true" : "false").Append(";\r\n");
            sb.Append("\t\t\tgame.levelData.usingTime = ").Append(UsingTime ? "true" : "false").Append(";\r\n");
            sb.Append("\t\t\tgame.levelData.lives = ").Append(Lives).Append(";\r\n");
            sb.Append("\t\t\tgame.levelData.time = ").Append(Minutes).Append(";\r\n");
            sb.Append("\t\t\tgame.levelData.hazards = ").Append(Hazards ? "true" : "false").Append(";\r\n");
            sb.Append("\t\t\tgame.levelData.teamDamage = ").Append(TeamDamage ? "true" : "false").Append(";\r\n");
            if (Stage.Length > 0)
                sb.Append("\t\t\tgame.levelData.stage = \"").Append(Esc(Stage)).Append("\";\r\n");
            if (Music.Length > 0)
                sb.Append("\t\t\tgame.levelData.musicOverride = \"").Append(Esc(Music)).Append("\";\r\n");
            if (ClearItems)
            {
                sb.Append("\t\t\tvar allItems:Array = SSF2API.getItemStatsList();\r\n");
                sb.Append("\t\t\tvar n:int = 0;\r\n");
                sb.Append("\t\t\twhile (n < allItems.length) { delete game.items.items[allItems[n].statsName]; n++; }\r\n");
            }
            foreach (var item in BannedItems)
                sb.Append("\t\t\tdelete game.items.items.").Append(item).Append(";\r\n");
            foreach (var item in RequiredItems)
                sb.Append("\t\t\tgame.items.items.").Append(item).Append(" = true;\r\n");
            sb.Append("\t\t\tgame.items.frequency = ItemSettings.FREQUENCY_").Append(ItemFrequency).Append(";\r\n");
            if (HalfTimer)
                sb.Append("\t\t\tSSF2API.setCurrentTime(SSF2API.getCurrentTime() / 2);\r\n");
            sb.Append("\t\t\treturn game;\r\n\t\t}\r\n\r\n");
            sb.Append("\t\toverride public function update():void\r\n\t\t{\r\n");
            sb.Append("\t\t\tif (SSF2API.isGameEnded()) return;\r\n");
            EventGoalRules.AppendUpdate(this, sb);
            sb.Append("\t\t}\r\n");
        }

        private void AppendMultiMan(StringBuilder sb)
        {
            var player = Fighters.FirstOrDefault(f => f.Human) ?? Fighters.FirstOrDefault();
            var enemies = Fighters.Where(f => player == null || !ReferenceEquals(f, player)).ToList();
            if (player != null && enemies.Count == Fighters.Count)
                enemies = Fighters.Skip(1).ToList();
            sb.Append("\t\toverride public function initMatch(gameSettings:Object = null):void\r\n\t\t{\r\n");
            var win = WinCondition is "TIMED" or "TIMED_KO_EVENT" or "SURVIVAL" or "KO_BASED" ? WinCondition : "KO_BASED";
            sb.Append("\t\t\twinCondition = SSF2MultiManMatch.").Append(win).Append(";\r\n");
            sb.Append("\t\t\tmaxTime = ").Append(Minutes).Append(";\r\n");
            if (player != null)
                sb.Append("\t\t\tplayerCharOverride = \"").Append(Esc(player.Character)).Append("\";\r\n");
            if (Stage.Length > 0)
                sb.Append("\t\t\tstageOverride = \"").Append(Esc(Stage)).Append("\";\r\n");
            if (Music.Length > 0)
                sb.Append("\t\t\tmusicOverride = \"").Append(Esc(Music)).Append("\";\r\n");
            sb.Append("\t\t\tbodyCount = ").Append(BodyCount).Append(";\r\n");
            sb.Append("\t\t\tspawnCount = ").Append(SpawnCount).Append(";\r\n");
            if (StockOverride > 0)
                sb.Append("\t\t\tstockOverride = ").Append(StockOverride).Append(";\r\n");
            sb.Append("\t\t\tMAX_ENEMIES_ON_SCREEN = ").Append(MaxOnScreen).Append(";\r\n");
            sb.Append("\t\t\tcharArray = [");
            sb.Append(string.Join(",", enemies.Select(f => "\"" + Esc(f.Character) + "\"")));
            sb.Append("];\r\n");
            sb.Append("\t\t\tSSF2API.queueResources(charArray);\r\n");
            sb.Append("\t\t\tdifficultySettings = { \"level\": ").Append(CpuLevel);
            sb.Append(", \"attackRatio\": ").Append(AttackRatio.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(", \"damageRatio\": ").Append(DifficultyDamage.ToString(System.Globalization.CultureInfo.InvariantCulture));
            sb.Append(" };\r\n");
            sb.Append("\t\t}\r\n\r\n");
            sb.Append("\t\toverride public function onSuccess():void\r\n\t\t{\r\n");
            AppendRankAssign(sb, "\t\t\t");
            sb.Append("\t\t\tmatchData.success = true;\r\n");
            sb.Append("\t\t\tmatchData.rank = rank;\r\n");
            sb.Append("\t\t\tmatchData.scoreType = \"").Append(ScoreType).Append("\";\r\n");
            sb.Append("\t\t\tmatchData.score = SSF2API.getElapsedFrames();\r\n");
            if (RecordStock)
                sb.Append("\t\t\tmatchData.stock = SSF2API.getPlayers()[0].getLives();\r\n");
            if (RecordFps)
                sb.Append("\t\t\tmatchData.fps = SSF2API.getAverageFPS();\r\n");
            if (Unlock.Length > 0)
                sb.Append("\t\t\tSSF2API.triggerUnlock(Unlockable.").Append(Unlock).Append(");\r\n");
            sb.Append("\t\t\tSSF2API.endGame({ \"success\": true, \"immediate\": false });\r\n");
            sb.Append("\t\t}\r\n");
        }

        private void AppendRankFunction(StringBuilder sb, string name)
        {
            sb.Append("\t\toverride public function ").Append(name).Append("():String\r\n\t\t{\r\n");
            AppendRankAssign(sb, "\t\t\t");
            sb.Append("\t\t\treturn rank;\r\n\t\t}\r\n");
        }

        private void AppendRankAssign(StringBuilder sb, string indent)
        {
            sb.Append(indent).Append("var rank:String = \"F\";\r\n");
            var first = true;
            foreach (var cut in Ranks)
            {
                var word = first ? "if" : "else if";
                first = false;
                sb.Append(indent).Append(word).Append(" (").Append(RankTest(cut)).Append(") rank = \"").Append(cut.Letter).Append("\";\r\n");
            }
        }

        private string RankTest(RankCut cut)
        {
            return RankMeasure switch
            {
                "framesAtLeast" => "SSF2API.getElapsedFrames() >= " + cut.Value,
                "damageAtMost" => "SSF2API.getPlayers()[0].getDamage() <= " + cut.Value,
                "damageAtLeast" => "SSF2API.getPlayers()[0].getDamage() >= " + cut.Value,
                "countAtLeast" => "bodyCount >= " + cut.Value,
                _ => "SSF2API.getElapsedFrames() <= " + cut.Value
            };
        }

        private void AppendPerWaveRank(StringBuilder sb)
        {
            sb.Append("\t\toverride public function getRank():String\r\n\t\t{\r\n");
            sb.Append("\t\t\tvar frames:int = EventMode.currentInstance.getMatchTime();\r\n");
            foreach (var cut in Ranks)
                sb.Append("\t\t\tif (frames <= matchArray.length * ").Append(cut.Value).Append(") return \"").Append(cut.Letter).Append("\";\r\n");
            sb.Append("\t\t\treturn \"F\";\r\n\t\t}\r\n");
        }

        private bool HasExtra(string name) =>
            ExtraMethods.Any(m => Regex.IsMatch(m, @"function\s+" + Regex.Escape(name) + @"\s*\("));

        private static void ReadExtras(string source, EventRecipe recipe)
        {
            var skip = new HashSet<string>(StringComparer.Ordinal)
            {
                "initMatch", "matchSetup", "update", "getRank", "onSuccess"
            };
            foreach (Match m in Regex.Matches(source, @"(?:override\s+)?(?:public|private|protected|internal)\s+function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("))
            {
                if (DepthAt(source, m.Index) != 2) continue;
                var name = m.Groups[1].Value;
                if (skip.Contains(name) || name.StartsWith("EventMode", StringComparison.Ordinal)) continue;
                var brace = source.IndexOf('{', m.Index);
                if (brace < 0) continue;
                var depth = 0;
                var end = brace;
                for (var i = brace; i < source.Length; i++)
                {
                    if (source[i] == '{') depth++;
                    else if (source[i] == '}')
                    {
                        depth--;
                        if (depth == 0) { end = i; break; }
                    }
                }
                recipe.ExtraMethods.Add(source.Substring(m.Index, end - m.Index + 1).Trim());
            }
        }

        private static int DepthAt(string source, int index)
        {
            var depth = 0;
            for (var i = 0; i < index && i < source.Length; i++)
            {
                if (source[i] == '{') depth++;
                else if (source[i] == '}') depth--;
            }
            return depth;
        }

        internal void AppendFail(StringBuilder sb, string indent)
        {
            sb.Append(indent).Append("matchData.success = false;\r\n");
            sb.Append(indent).Append("SSF2API.endGame({ \"success\": false, \"immediate\": false });\r\n");
            sb.Append(indent).Append("return;\r\n");
        }

        internal void AppendSucceed(StringBuilder sb, string indent)
        {
            AppendRankAssign(sb, indent);
            sb.Append(indent).Append("matchData.success = true;\r\n");
            sb.Append(indent).Append("matchData.rank = rank;\r\n");
            sb.Append(indent).Append("matchData.scoreType = \"").Append(ScoreType).Append("\";\r\n");
            sb.Append(indent).Append("matchData.score = SSF2API.getElapsedFrames();\r\n");
            if (RecordStock)
                sb.Append(indent).Append("matchData.stock = SSF2API.getPlayers()[0].getLives();\r\n");
            if (RecordFps)
                sb.Append(indent).Append("matchData.fps = SSF2API.getAverageFPS();\r\n");
            if (Unlock.Length > 0)
                sb.Append(indent).Append("SSF2API.triggerUnlock(Unlockable.").Append(Unlock).Append(");\r\n");
            sb.Append(indent).Append("SSF2API.endGame({ \"success\": true, \"immediate\": false });\r\n");
        }

        private void AppendGoal(StringBuilder sb) => EventGoalRules.AppendMethods(this, sb);

        private static void ReadFighterMods(string source, EventRecipe recipe)
        {
            void At(int index, Action<EventFighter> apply)
            {
                if (index >= 0 && index < recipe.Fighters.Count)
                    apply(recipe.Fighters[index]);
            }
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setSizeStatus\((-?\d+)\)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.SizeStatus = int.Parse(m.Groups[2].Value));
            foreach (Match m in Regex.Matches(source, @"_loc1_\[(\d+)\]\.setSizeStatus\((-?\d+)\)"))
                At(int.Parse(m.Groups[1].Value), f => f.SizeStatus = int.Parse(m.Groups[2].Value));
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setMetalStatus\(true\)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.Metal = true);
            foreach (Match m in Regex.Matches(source, @"_loc1_\[(\d+)\]\.setMetalStatus\(true\)"))
                At(int.Parse(m.Groups[1].Value), f => f.Metal = true);
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setScale\(([0-9.]+)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.Scale = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (Match m in Regex.Matches(source, @"_loc1_\[(\d+)\]\.setScale\(([0-9.]+)"))
                At(int.Parse(m.Groups[1].Value), f => f.Scale = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setCPUForcedAction\(CPUState\.([A-Z_]+)\)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.CpuAction = m.Groups[2].Value);
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setSpecialEvent\(true\)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.SpecialEvent = true);
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.setLivesEnabled\(false\)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.LivesEnabled = false);
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\.updateCharacterStats\(\{\s*""damageRatio""\s*:\s*([0-9.]+)"))
                At(int.Parse(m.Groups[1].Value) - 1, f => f.DamageRatio = double.Parse(m.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture));
            if (source.Contains("disableMoves("))
            {
                foreach (var fighter in recipe.Fighters)
                    if (!fighter.Human) fighter.MovesLocked = true;
            }
            void ApplyBody(int index, string body)
            {
                var size = Regex.Match(body, @"setSizeStatus\((-?\d+)\)");
                if (size.Success) At(index, f => f.SizeStatus = int.Parse(size.Groups[1].Value));
                if (body.Contains("setMetalStatus(true)")) At(index, f => f.Metal = true);
                var scale = Regex.Match(body, @"setScale\(([0-9.]+)");
                if (scale.Success) At(index, f => f.Scale = double.Parse(scale.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
                var action = Regex.Match(body, @"setCPUForcedAction\(CPUState\.([A-Z_]+)\)");
                if (action.Success) At(index, f => f.CpuAction = action.Groups[1].Value);
                if (body.Contains("setSpecialEvent(true)")) At(index, f => f.SpecialEvent = true);
                if (body.Contains("setLivesEnabled(false)")) At(index, f => f.LivesEnabled = false);
                var ratio = Regex.Match(body, @"""damageRatio""\s*:\s*([0-9.]+)");
                if (ratio.Success && double.TryParse(ratio.Groups[1].Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var dmg))
                    At(index, f => f.DamageRatio = dmg);
            }
            foreach (Match m in Regex.Matches(source, @"getPlayer\((\d+)\)\s*;([\s\S]{0,500}?)(?=getPlayer\(\d+\)|function\s|override\s+public|$)"))
                ApplyBody(int.Parse(m.Groups[1].Value) - 1, m.Groups[2].Value);
            foreach (Match m in Regex.Matches(source, @"(_loc\d+_)\s*(?::\w+)?\s*=\s*(\d+)\s*;\s*while\s*\(\s*\1\s*<=\s*(\d+)\s*\)\s*\{([\s\S]*?)\1\s*\+\+"))
            {
                var start = int.Parse(m.Groups[2].Value);
                var end = int.Parse(m.Groups[3].Value);
                for (var n = start; n <= end; n++)
                    ApplyBody(n - 1, m.Groups[4].Value);
            }
        }

        private static void ReadItems(string source, EventRecipe recipe)
        {
            recipe.ClearItems = source.Contains("getItemStatsList");
            foreach (Match m in Regex.Matches(source, @"delete\s+\w+\.items\.items\.(\w+)"))
                if (!recipe.BannedItems.Contains(m.Groups[1].Value))
                    recipe.BannedItems.Add(m.Groups[1].Value);
            foreach (Match m in Regex.Matches(source, @"items\.items\.(\w+)\s*=\s*(?:true|false)"))
            {
                var id = m.Groups[1].Value;
                if (m.Value.Contains("true") && !recipe.RequiredItems.Contains(id))
                    recipe.RequiredItems.Add(id);
                else if (m.Value.Contains("false") && !recipe.BannedItems.Contains(id))
                    recipe.BannedItems.Add(id);
            }
        }

        private static void ReadGoal(string source, EventRecipe recipe)
        {
            if (source.Contains("function checkTag")) recipe.Goal = EventGoal.Tag;
            else if (source.Contains("function onStateChange")) recipe.Goal = EventGoal.Combo;
            else if (source.Contains("function checkBat")) recipe.Goal = EventGoal.Bat;
            else if (source.Contains("function checkBombTimer")) recipe.Goal = EventGoal.Bombs;
            else if (source.Contains("function checkBumper")) recipe.Goal = EventGoal.Bumpers;
            else if (source.Contains("function handleSheik")) recipe.Goal = EventGoal.Sheik;
            else if (source.Contains("saturnArray") || source.Contains("function checkSaturns")) recipe.Goal = EventGoal.Saturns;
            else if (source.Contains("dirtArray") || source.Contains("function checkDirt")) recipe.Goal = EventGoal.Dirt;
            else if (source.Contains("function pokeBallCheck")) recipe.Goal = EventGoal.Pokeball;
            else if (source.Contains("function generateProtoman")) recipe.Goal = EventGoal.Protoman;
            else if (source.Contains("MotherBrain")) recipe.Goal = EventGoal.MotherBrain;
            else if (source.Contains("function checkMH")) recipe.Goal = EventGoal.MasterHand;
            else if (source.Contains("function randomItem")) recipe.Goal = EventGoal.RandomItems;
            else if (source.Contains("function handleLoopingBackground")) recipe.Goal = EventGoal.LoopBackground;
        }

        private static void ReadRanks(string source, EventRecipe recipe)
        {
            var waves = Regex.Matches(source, @"matchArray\.length\s*\*\s*(\d+)[\s\S]{0,60}?""([SABCDE])""");
            if (waves.Count > 0)
            {
                recipe.Ranks.Clear();
                recipe.RankMeasure = "perMatch";
                var seenWave = new HashSet<string>();
                foreach (Match m in waves)
                {
                    if (!seenWave.Add(m.Groups[2].Value)) continue;
                    recipe.Ranks.Add(new RankCut { Letter = m.Groups[2].Value, Value = int.Parse(m.Groups[1].Value) });
                }
                return;
            }
            if (recipe.RankMeasure == "perMatch")
                return;
            var rx = new Regex(
                @"(getElapsedFrames\(\)|getDamage\(\)|KOs|bodyCount)\s*(<=|>=)\s*(\d+)(?:\s*\*\s*(\d+))?[\s\S]{0,120}?=\s*""([SABCDE])""",
                RegexOptions.CultureInvariant);
            string? measure = null;
            var seen = new HashSet<string>();
            foreach (Match m in rx.Matches(source))
            {
                var letter = m.Groups[5].Value;
                if (!seen.Add(letter))
                    continue;
                var value = int.Parse(m.Groups[3].Value);
                if (m.Groups[4].Success)
                    value *= int.Parse(m.Groups[4].Value);
                var kind = m.Groups[1].Value;
                var op = m.Groups[2].Value;
                var thisMeasure = kind switch
                {
                    "getDamage()" when op == "<=" => "damageAtMost",
                    "getDamage()" => "damageAtLeast",
                    "getElapsedFrames()" when op == ">=" => "framesAtLeast",
                    "getElapsedFrames()" => "framesAtMost",
                    _ => "countAtLeast"
                };
                measure ??= thisMeasure;
                if (thisMeasure != measure)
                    continue;
                recipe.Ranks.Add(new RankCut { Letter = letter, Value = value });
            }
            if (measure != null)
                recipe.RankMeasure = measure;
        }

        private static string? First(string text, string pattern)
        {
            var m = Regex.Match(text, pattern);
            return m.Success ? m.Groups[1].Value : null;
        }

        private static string Esc(string? value) => (value ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
