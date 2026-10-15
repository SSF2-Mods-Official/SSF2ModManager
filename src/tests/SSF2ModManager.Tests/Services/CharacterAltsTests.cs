using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json.Linq;
using SSF2ModManager.Services;
using Xunit;
using Xunit.Abstractions;

namespace SSF2ModManager.Tests.Services
{
    public class CharacterAltsTests
    {
        private readonly ITestOutputHelper _out;
        public CharacterAltsTests(ITestOutputHelper output) => _out = output;

        private const string Root = "Alternate Voices Plus (1.4.0.1) - Ver. 2.0.1/";

        [Fact]
        public void PlanAlts_GroupsVariantFolders_SkipsVanillaAndAnnouncer()
        {
            var files = new List<(string, string?, bool)>
            {
                (Root + "Modded/Mario/Mario (Kevin Z. Afghani)/DAT222.ssf", "mario", true),
                (Root + "Modded/Mario/Mario (Charles Martinet)/DAT222.ssf", "mario", true),
                (Root + "Modded/Luigi/Luigi (SSBM)/DAT118.ssf", "luigi", true),
                (Root + "Modded/Luigi/Luigi (SSBM)/DAT170.ssf", "luigi_fx", true),
                (Root + "Modded/Luigi/Luigi (SSBM)/DAT67.ssf", "misc", false),
                (Root + "Modded/Captain Falcon (SSBM)/DAT96.ssf", "captainfalcon", true),
                (Root + "Modded/Announcer + SFX/Announcer (SSBB)/DAT141.ssf", "announcer", false),
                (Root + "Vanilla/Mario/DAT222.ssf", "mario", true),
                ("Other/newguy.ssf", "newguy", true)
            };
            var plan = OneClickReceive.PlanAlts(files, new HashSet<string>(new[] { "mario", "luigi", "captainfalcon" }));

            Assert.Equal(4, plan.Alts.Count);
            Assert.Equal(1, plan.Vanilla);
            Assert.Equal(1, plan.Announcer);
            var luigi = plan.Alts.Single(a => a.CharacterId == "luigi");
            Assert.Equal(new[] { "luigi", "luigi_fx" }, luigi.Files.Select(f => f.ResourceId).OrderBy(s => s).ToArray());
            Assert.DoesNotContain("Other/newguy.ssf", plan.Claimed);
        }

        [Fact]
        public void PlanAlts_RealAlternateVoicesPlusList()
        {
            var list = Path.Combine(Path.GetTempPath(), "gbprobe", "641874-list.txt");
            if (!File.Exists(list))
                return;
            var files = File.ReadAllLines(list)
                .Select(l => l.Split('\t').Last())
                .Where(p => p.EndsWith(".ssf", StringComparison.OrdinalIgnoreCase))
                .Select(p => (p, (string?)("c" + Regex.Match(Path.GetFileName(p), @"\d+").Value), true))
                .ToList();
            var taken = new HashSet<string>(files.Select(f => f.Item2!));
            var plan = OneClickReceive.PlanAlts(files, taken);
            var variantFolders = files
                .Where(f => f.p.Contains("/Modded/") && !f.p.Contains("Announcer"))
                .Select(f => Path.GetDirectoryName(f.p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count();
            _out.WriteLine($"files={files.Count} alts={plan.Alts.Count} vanilla={plan.Vanilla} announcer={plan.Announcer} variantFolders={variantFolders}");
            Assert.Equal(variantFolders, plan.Alts.Count);
            Assert.True(plan.Vanilla > 0);
            Assert.True(plan.Announcer > 0);
            Assert.DoesNotContain(plan.Alts, a => a.Folder.Contains("Vanilla", StringComparison.OrdinalIgnoreCase));
        }

        [Fact]
        public void Install_WritesAltsJson_PerFolder()
        {
            var shell = Path.Combine(Path.GetTempPath(), "ssf2mm-alts-" + Guid.NewGuid().ToString("N"));
            var src = Path.Combine(shell, "_src");
            Directory.CreateDirectory(src);
            try
            {
                var a = Path.Combine(src, "DAT222.ssf");
                File.WriteAllText(a, "x");
                CharacterAltsService.Install(shell, 641874, "mario", "Mario (Kevin Z. Afghani)", new[] { ("mario", a) }, "Mario (Kevin Z. Afghani)");
                CharacterAltsService.Install(shell, 641874, "mario", null, new[] { ("mario", a) }, "Mario");
                CharacterAltsService.Install(shell, 641874, "mario", "Mario (Kevin Z. Afghani)", new[] { ("mario", a) }, "Mario (Kevin Z. Afghani)");

                var doc = JObject.Parse(File.ReadAllText(CharacterAltsService.IndexPath(shell)));
                var list = (JArray)doc["characters"]!["mario"]!;
                Assert.Equal(2, list.Count);
                var named = list.Single(e => e["name"] != null);
                Assert.Equal("Mario (Kevin Z. Afghani)", (string?)named["name"]);
                Assert.Equal("expansions/alts/641874/mario/Mario (Kevin Z. Afghani)/DAT222.ssf", (string?)named["files"]!["mario"]);
                Assert.True(File.Exists(Path.Combine(shell, "expansions", "alts", "641874", "mario", "Mario", "DAT222.ssf")));
                Assert.Equal(2, CharacterAltsService.Count(shell, "mario"));
            }
            finally
            {
                Directory.Delete(shell, true);
            }
        }

        [Fact]
        public void NameFromFolder_RepeatOfCharacterIsUnnamed()
        {
            Assert.Equal("Mario (Kevin Z. Afghani)", CharacterAltsService.NameFromFolder("Mario (Kevin Z. Afghani)", "mario"));
            Assert.Null(CharacterAltsService.NameFromFolder("Mario", "mario"));
            Assert.Null(CharacterAltsService.NameFromFolder("DAT222", "mario"));
            Assert.Null(CharacterAltsService.NameFromFolder("SSF2 Beta", "wario"));
            Assert.Null(CharacterAltsService.NameFromFolder("Sandbox", "wario"));
            Assert.Equal("Captain Falcon (SSBM)", CharacterAltsService.NameFromFolder("Captain Falcon (SSBM)", "captainfalcon"));
        }

        [Fact]
        public void DefaultName_GenericFolder_OnlyAlt_TakesModName()
        {
            var alt = new OneClickItem { Kind = OneClickKind.Alt, Id = "wario", OriginalName = "SSF2 Beta", Selected = true };
            var receipt = new OneClickReceipt { Items = { alt } };
            Assert.Equal("Wario Man", OneClickReceive.DefaultDisplayName(receipt, alt, "Wario Man"));
        }

        [Fact]
        public void List_And_Remove()
        {
            var shell = Path.Combine(Path.GetTempPath(), "ssf2mm-alts-" + Guid.NewGuid().ToString("N"));
            var src = Path.Combine(shell, "_src");
            Directory.CreateDirectory(src);
            try
            {
                var a = Path.Combine(src, "DAT7.ssf");
                File.WriteAllText(a, "x");
                CharacterAltsService.Install(shell, 700684, "wario", "Wario Man", new[] { ("wario", a) }, "SSF2 Beta");
                var entry = Assert.Single(CharacterAltsService.List(shell));
                Assert.Equal("Wario Man", entry.Name);
                CharacterAltsService.Remove(shell, entry.Folder);
                Assert.Empty(CharacterAltsService.List(shell));
                Assert.False(Directory.Exists(entry.Folder));
                Assert.Equal(0, CharacterAltsService.Count(shell, "wario"));
            }
            finally
            {
                Directory.Delete(shell, true);
            }
        }
    }
}
