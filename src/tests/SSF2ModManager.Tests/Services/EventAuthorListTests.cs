using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class EventAuthorListTests
    {
        [Fact]
        public void ListSources_ListsVanillaEvents()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-events-" + Path.GetRandomFileName());
            var modes = Path.Combine(root, "data", "modes");
            var src = Path.Combine(root, "expansions", "events", "mypack", "src");
            Directory.CreateDirectory(modes);
            Directory.CreateDirectory(src);
            try
            {
                var bytes = new System.Collections.Generic.List<byte> { 0 };
                Write(bytes, "ComboMaker");
                Write(bytes, "13. Combo Maker");
                Write(bytes, "EventMode13_ComboMaker");
                Write(bytes, "EventMode_AllStarBaseMatch");
                Write(bytes, "AllStarBattle01");
                Write(bytes, "All-Star Battle 1");
                Write(bytes, "EventMode10Special_AllStarBattle01");
                File.WriteAllBytes(Path.Combine(modes, "event_mode.swf"), bytes.ToArray());
                File.WriteAllText(Path.Combine(src, "EventMode_Custom.as"), "package { }");

                var labels = EventAuthorService.ListSources(root).Select(s => s.Label).ToList();
                var classes = EventAuthorService.ListSources(root).Select(s => s.ClassName).ToList();

                labels.Should().Contain("13. Combo Maker");
                labels.Should().Contain("All-Star Battle 1");
                labels.Should().Contain("mypack / EventMode_Custom");
                classes.Should().Contain("EventMode13_ComboMaker");
                classes.Should().Contain("EventMode10Special_AllStarBattle01");
                classes.Should().NotContain("EventMode_AllStarBaseMatch");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void IdentityFromName_UsesTheName()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-event-id-" + Path.GetRandomFileName());
            Directory.CreateDirectory(root);
            try
            {
                var first = EventAuthorService.IdentityFromName(root, "Rise of King Koopa!");
                first.Id.Should().Be("RiseOfKingKoopa");
                first.ClassName.Should().Be("EventMode_RiseOfKingKoopa");
                first.Folder.Should().Be("RiseOfKingKoopa");
                EventAuthorService.SuggestClassName("combo maker").Should().Be("EventMode_ComboMaker");

                Directory.CreateDirectory(Path.Combine(root, "expansions", "events", "RiseOfKingKoopa"));
                var second = EventAuthorService.IdentityFromName(root, "Rise of King Koopa!");
                second.Id.Should().Be("RiseOfKingKoopa2");
                second.ClassName.Should().Be("EventMode_RiseOfKingKoopa2");
                second.Folder.Should().Be("RiseOfKingKoopa2");

                EventAuthorService.IdentityFromName(root, "13 fights").Id.Should().Be("E13Fights");

                var source = Path.Combine(root, "source.as");
                File.WriteAllText(source, "package {\r\n\tpublic class EventMode_Old\r\n\t{\r\n\t\tpublic function EventMode_Old()\r\n\t\t{\r\n\t\t}\r\n\t}\r\n}\r\n");
                var dir = EventAuthorService.CreateEvent(root, source, "", new EventInfoDraft
                {
                    Name = "Rise of King Koopa!",
                    Description = "Go."
                });
                dir.Should().EndWith(Path.Combine("events", "RiseOfKingKoopa2"));
                var mode = File.ReadAllText(Path.Combine(dir, "src", "EventMode_RiseOfKingKoopa2.as"));
                mode.Should().Contain("class EventMode_RiseOfKingKoopa2");
                var pack = File.ReadAllText(Path.Combine(dir, "src", "EventPack_RiseOfKingKoopa2.as"));
                pack.Should().Contain("\"id\": \"RiseOfKingKoopa2\"");
                pack.Should().Contain("\"name\": \"Rise of King Koopa!\"");
                File.ReadAllText(Path.Combine(dir, "info.json")).Should().Contain("\"id\": \"RiseOfKingKoopa2\"");

                var act = () => EventAuthorService.IdentityFromName(root, "!!!");
                act.Should().Throw<ArgumentException>();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void CreateEvent_RenamesTheVanillaClass()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-event-rename-" + Path.GetRandomFileName());
            var export = Path.Combine(root, "export", "scripts");
            Directory.CreateDirectory(export);
            try
            {
                var script = Path.Combine(export, "EventMode35_PrettyInPink.as");
                File.WriteAllText(script,
                    "package\r\n{\r\n   public class EventMode35_PrettyInPink extends SSF2CustomMatch\r\n   {\r\n      public function EventMode35_PrettyInPink(param1:*)\r\n      {\r\n         super(param1);\r\n      }\r\n   }\r\n}\r\n");
                EventAuthorService.FindExportedScript(Path.Combine(root, "export"), "EventMode35_PrettyInPink")
                    .Should().Be(script);

                var dir = EventAuthorService.CreateEvent(root, script, "", new EventInfoDraft
                {
                    Name = "Pretty In Pink",
                    Description = "Pink."
                });
                var written = File.ReadAllText(Path.Combine(dir, "src", "EventMode_PrettyInPink.as"));
                written.Should().Contain("class EventMode_PrettyInPink");
                written.Should().NotContain("EventMode35_PrettyInPink");
                var pack = File.ReadAllText(Path.Combine(dir, "src", "EventPack_PrettyInPink.as"));
                pack.Should().Contain("\"id\": \"PrettyInPink\"");
                pack.Should().Contain("\"name\": \"Pretty In Pink\"");
                pack.Should().Contain("\"description\": \"Pink.\"");
                var info = File.ReadAllText(Path.Combine(dir, "info.json"));
                info.Should().Contain("\"id\": \"PrettyInPink\"");
                info.Should().Contain("\"name\": \"Pretty In Pink\"");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public async Task ExportVanillaSource_ReadsPrettyInPink()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "GitHub", "SSF2 Shell Base", "Build");
            var swf = Path.Combine(root, "data", "modes", "event_mode.swf");
            if (!File.Exists(swf))
                return;

            var copy = await EventAuthorService.ExportVanillaSourceAsync(root, "EventMode35_PrettyInPink");
            try
            {
                File.ReadAllText(copy).Should().Contain("class EventMode35_PrettyInPink");
            }
            finally
            {
                try { File.Delete(copy); } catch { /* temp script */ }
            }
        }

        [Fact]
        public async Task EveryStockEvent_RemakesWithRecords()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "GitHub", "SSF2 Shell Base", "Build");
            var swf = Path.Combine(root, "data", "modes", "event_mode.swf");
            if (!File.Exists(swf))
                return;

            var export = await EventAuthorService.ExportEventScriptsAsync(root);
            try
            {
                var files = Directory.GetFiles(export, "EventMode*.as", SearchOption.AllDirectories)
                    .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^EventMode\d+"))
                    .ToList();
                files.Count.Should().BeGreaterOrEqualTo(51);
                var numbers = files
                    .Select(f => Regex.Match(Path.GetFileName(f), @"^EventMode(\d+)").Groups[1].Value)
                    .Select(int.Parse)
                    .ToHashSet();
                for (var n = 1; n <= 51; n++)
                    numbers.Should().Contain(n);

                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    var recipe = EventRecipe.Parse(text);
                    recipe.Kind.Should().NotBe(EventKind.Unknown, Path.GetFileName(file));
                    if (Regex.IsMatch(text, @"scoreType\s*=\s*""([^""]+)"""))
                    {
                        var score = Regex.Match(text, @"scoreType\s*=\s*""([^""]+)""").Groups[1].Value;
                        recipe.ScoreType.Should().Be(score, Path.GetFileName(file));
                    }

                    var written = recipe.WriteSource("EventMode_Copy", new EventInfoDraft { Name = "Copy", Id = "Copy" });
                    var again = EventRecipe.Parse(written);
                    again.Kind.Should().Be(recipe.Kind, Path.GetFileName(file));
                    again.ScoreType.Should().Be(recipe.ScoreType, Path.GetFileName(file));
                    if (recipe.Stage.Length > 0)
                        again.Stage.Should().Be(recipe.Stage, Path.GetFileName(file));
                    again.Ranks.Select(r => r.Letter + r.Value).Should().Equal(recipe.Ranks.Select(r => r.Letter + r.Value), Path.GetFileName(file));
                    again.SameOptions(recipe).Should().BeTrue(Path.GetFileName(file) + " " + recipe.Difference(again));

                    foreach (Match method in Regex.Matches(text, @"function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\("))
                    {
                        var methodName = method.Groups[1].Value;
                        if (methodName is "initMatch" or "matchSetup" or "update" or "getRank" or "initialize" or "onSuccess")
                            continue;
                        if (methodName.StartsWith("EventMode", StringComparison.Ordinal))
                            continue;
                        written.Should().Contain("function " + methodName, Path.GetFileName(file));
                    }
                    if (text.Contains("TIMED_KO_EVENT"))
                        written.Should().Contain("TIMED_KO_EVENT", Path.GetFileName(file));

                    if (text.Contains("matchData.rank"))
                        written.Should().Contain("matchData.rank", Path.GetFileName(file));
                    else if (recipe.Kind is EventKind.AllStarBattle or EventKind.AllStarTarget)
                        written.Should().Contain("extends EventMode_AllStar", Path.GetFileName(file));
                }
            }
            finally
            {
                try { Directory.Delete(export, true); } catch { /* temp scripts */ }
            }
        }

        [Fact]
        public async Task EveryStockEvent_Compiles()
        {
            var root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Documents", "GitHub", "SSF2 Shell Base", "Build");
            var swf = Path.Combine(root, "data", "modes", "event_mode.swf");
            if (!File.Exists(swf))
                return;

            var export = await EventAuthorService.ExportEventScriptsAsync(root);
            var shell = Path.Combine(Path.GetTempPath(), "ssf2mm-event-compile");
            if (Directory.Exists(shell))
                Directory.Delete(shell, true);
            var failures = new System.Collections.Generic.List<string>();
            try
            {
                var files = Directory.GetFiles(export, "EventMode*.as", SearchOption.AllDirectories)
                    .Where(f => Regex.IsMatch(Path.GetFileName(f), @"^EventMode\d+"))
                    .ToList();
                files.Count.Should().BeGreaterOrEqualTo(51);
                foreach (var file in files)
                {
                    var text = File.ReadAllText(file);
                    var recipe = EventRecipe.Parse(text);
                    var id = Regex.Replace(Path.GetFileNameWithoutExtension(file), @"^EventMode\d+(Special)?_", "");
                    var draft = new EventInfoDraft { Name = id, Id = id, ClassName = "EventMode_" + id };
                    var script = recipe.WriteSource(draft.ClassName, draft);
                    var scriptPath = Path.Combine(Path.GetTempPath(), "ssf2mm-" + id + ".as");
                    File.WriteAllText(scriptPath, script);
                    string dir;
                    try
                    {
                        dir = EventAuthorService.CreateEvent(shell, scriptPath, id, draft);
                    }
                    finally
                    {
                        try { File.Delete(scriptPath); } catch { /* temp script */ }
                    }
                    var writtenPath = Path.Combine(dir, "src", draft.ClassName + ".as");
                    var written = File.ReadAllText(writtenPath);
                    Regex.Matches(written, @"var\s+eventinfo").Count.Should().Be(0, id);
                    var packList = File.ReadAllText(Directory.GetFiles(Path.Combine(dir, "src"), "EventPack_*.as")
                        .First(f => !Path.GetFileName(f).Equals("EventPack.as", StringComparison.OrdinalIgnoreCase)));
                    packList.Should().Contain("\"id\": \"" + id + "\"", id);
                    packList.Should().NotContain("eventinfo", id);
                    var (ok, log) = await EventAuthorService.CompileAsync(dir);
                    if (!ok)
                    {
                        var lines = log.Split('\n').Where(l => l.Contains("Error", StringComparison.OrdinalIgnoreCase)).Take(4);
                        failures.Add(id + "\n" + string.Join("\n", lines));
                    }
                }
            }
            finally
            {
                try { Directory.Delete(export, true); } catch { /* temp scripts */ }
                try { Directory.Delete(shell, true); } catch { /* compiled packs */ }
            }
            failures.Should().BeEmpty(string.Join("\n\n", failures));
        }

        [Fact]
        public void ListEvents_IncludesEnabledExpansionPacks()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-events-" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "data", "modes"));
            var pack = Path.Combine(root, "expansions", "events", "BobOmb");
            var hidden = Path.Combine(root, "expansions", "events", "Hidden");
            Directory.CreateDirectory(pack);
            Directory.CreateDirectory(hidden);
            try
            {
                File.WriteAllBytes(Path.Combine(root, "data", "modes", "event_mode.swf"), new byte[] { 0 });
                File.WriteAllText(Path.Combine(pack, "info.json"),
                    "{\"id\":\"BobOmbDestination\",\"name\":\"Bob-omb Destination\",\"chooseCharacter\":true,\"enabled\":true}");
                File.WriteAllText(Path.Combine(hidden, "info.json"),
                    "{\"id\":\"HiddenEvent\",\"name\":\"Hidden\",\"enabled\":false}");
                var list = ShellConfigureLaunchService.ListEvents(root);
                list.Should().Contain(e => e.Id == "BobOmbDestination" && e.Label == "Bob-omb Destination" && e.ChooseCharacter);
                list.Should().NotContain(e => e.Id == "HiddenEvent");
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static void Write(System.Collections.Generic.List<byte> bytes, string text)
        {
            bytes.Add((byte)text.Length);
            bytes.AddRange(Encoding.ASCII.GetBytes(text));
        }
    }
}
