using System;
using System.Text.RegularExpressions;

namespace SSF2ModManager.Services
{
    /// <summary>
    /// Copies a cloned event and replaces only the stage, music, match toggles, and fighter rows the form still shows.
    /// </summary>
    public static class EventCloneEdit
    {
        public static string Apply(string source, EventRecipe before, EventRecipe after)
        {
            if (string.IsNullOrEmpty(source) || before == null || after == null)
                return source ?? "";
            var text = source;
            text = ReplaceQuoted(text, "levelData.stage", before.Stage, after.Stage);
            text = ReplaceQuoted(text, "stageOverride", before.Stage, after.Stage);
            text = ReplaceMusic(text, before.Music, after.Music);
            text = ReplaceBool(text, "usingLives", before.UsingLives, after.UsingLives);
            text = ReplaceBool(text, "usingTime", before.UsingTime, after.UsingTime);
            text = ReplaceBool(text, "hazards", before.Hazards, after.Hazards);
            text = ReplaceBool(text, "teamDamage", before.TeamDamage, after.TeamDamage);
            text = ReplaceInt(text, "levelData.lives", before.Lives, after.Lives);
            text = ReplaceInt(text, "levelData.time", before.Minutes, after.Minutes);
            text = ReplacePushes(text, before, after);
            return text;
        }

        private static string ReplaceQuoted(string text, string name, string from, string to)
        {
            if (from.Length == 0 || to.Length == 0 || from == to)
                return text;
            return Regex.Replace(
                text,
                "(" + Regex.Escape(name) + "\\s*=\\s*\")" + Regex.Escape(from) + "(\")",
                "$1" + to.Replace("\\", "\\\\").Replace("$", "$$") + "$2");
        }

        private static string ReplaceMusic(string text, string from, string to)
        {
            if (from == to)
                return text;
            if (Regex.IsMatch(text, @"musicOverride\s*=\s*""[^""]*"""))
            {
                if (to.Length == 0)
                    return Regex.Replace(text, @"[ \t]*[\w.]*musicOverride\s*=\s*""[^""]*""\s*;\r?\n?", "");
                return Regex.Replace(
                    text,
                    "(musicOverride\\s*=\\s*\")[^\"]*(\")",
                    "$1" + to.Replace("\\", "\\\\").Replace("$", "$$") + "$2");
            }
            if (to.Length == 0)
                return text;
            var stage = Regex.Match(text, "(?m)^([ \\t]*)((?:[\\w]+\\.)*)levelData\\.stage\\s*=\\s*\"[^\"]*\"\\s*;");
            if (!stage.Success)
                return text;
            var nl = text.Contains("\r\n") ? "\r\n" : "\n";
            var line = stage.Groups[1].Value + stage.Groups[2].Value + "levelData.musicOverride = \"" + to + "\";";
            return text.Insert(stage.Index + stage.Length, nl + line);
        }

        private static string ReplaceBool(string text, string name, bool from, bool to)
        {
            if (from == to)
                return text;
            return Regex.Replace(
                text,
                "(" + Regex.Escape(name) + "\\s*=\\s*)" + (from ? "true" : "false"),
                "$1" + (to ? "true" : "false"));
        }

        private static string ReplaceInt(string text, string name, int from, int to)
        {
            if (from == to)
                return text;
            return Regex.Replace(
                text,
                "(" + Regex.Escape(name) + "\\s*=\\s*)" + from + "\\b",
                "$1" + to);
        }

        private static string ReplacePushes(string text, EventRecipe before, EventRecipe after)
        {
            var index = 0;
            return Regex.Replace(text, @"playerSettings\.push\(\s*\{[\s\S]*?\}\s*\)", match =>
            {
                var block = match.Value;
                if (index < before.Fighters.Count && index < after.Fighters.Count)
                    block = PatchBlock(block, before.Fighters[index], after.Fighters[index]);
                index++;
                return block;
            });
        }

        private static string PatchBlock(string block, EventFighter from, EventFighter to)
        {
            if (!from.CharacterFromPlayer && !to.CharacterFromPlayer
                && from.Character.Length > 0
                && !from.Character.Equals(to.Character, StringComparison.OrdinalIgnoreCase))
            {
                block = Regex.Replace(
                    block,
                    "(\"character\"\\s*:\\s*\")" + Regex.Escape(from.Character) + "(\")",
                    "$1" + to.Character + "$2",
                    RegexOptions.IgnoreCase);
            }
            if (from.CostumeFromPlayer && to.Costume != -1)
            {
                block = Regex.Replace(
                    block,
                    "\"costume\"\\s*:\\s*(?:param1|initSettings)\\.playerSettings\\[0\\]\\.costume",
                    "\"costume\": " + to.Costume);
            }
            else if (!from.CostumeFromPlayer && from.Costume != to.Costume)
            {
                if (Regex.IsMatch(block, "\"costume\"\\s*:\\s*-?\\d+"))
                    block = Regex.Replace(block, "(\"costume\"\\s*:\\s*)-?\\d+", "${1}" + to.Costume);
                else
                    block = Regex.Replace(block, @"\}\s*\)\s*$", ", \"costume\": " + to.Costume + " })");
            }
            if (from.Human != to.Human)
                block = Regex.Replace(block, "(\"human\"\\s*:\\s*)(true|false)", "${1}" + (to.Human ? "true" : "false"));
            if (from.Level != to.Level)
                block = Regex.Replace(block, "(\"level\"\\s*:\\s*)\\d+", "${1}" + to.Level);
            if (from.Lives != to.Lives)
                block = Regex.Replace(block, "(\"lives\"\\s*:\\s*)\\d+", "${1}" + to.Lives);
            return block;
        }
    }
}
