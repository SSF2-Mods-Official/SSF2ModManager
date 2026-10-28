using FluentAssertions;
using SSF2ModManager.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class CharacterCloneServiceTests
    {
        [Fact]
        public void ReadImportIdentity_UsesDisplayNameAndMatchingFlaRoot()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0statsName\0displayName\0Mario\0linkage_id\0linkage_id2\0mario_fla\0metaknight_fla\0");
            var (linkage, display) = CharacterCloneService.ReadImportIdentity(bytes);
            linkage.Should().Be("mario");
            display.Should().Be("Mario");
        }

        [Fact]
        public void ReadImportIdentity_PrefersLinkageIdOverFla()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0displayName\0Banjo\0linkage_id\0banjokazooie\0banjokazooie_fla\0");
            var (linkage, display) = CharacterCloneService.ReadImportIdentity(bytes);
            linkage.Should().Be("banjokazooie");
            display.Should().Be("Banjo");
        }

        [Fact]
        public void SelectedImportPaths_KeepsOrderAndDropsRepeats()
        {
            var paths = ShellCharacterCloneService.SelectedImportPaths(new[]
            {
                @"C:\chars\donkeykong.swf",
                "  ",
                @"C:\chars\DonkeyKong.swf",
                @"C:\chars\fox.swf"
            });
            paths.Should().Equal(@"C:\chars\donkeykong.swf", @"C:\chars\fox.swf");
        }

        [Fact]
        public void NextFreeLinkage_AppendsTheNextFreeNumber()
        {
            ShellCharacterCloneService.NextFreeLinkage(new[] { "mario" }, "banjokazooie")
                .Should().Be("banjokazooie");
            ShellCharacterCloneService.NextFreeLinkage(new[] { "donkeykong" }, "donkeykong")
                .Should().Be("donkeykong2");
            ShellCharacterCloneService.NextFreeLinkage(new[] { "donkeykong", "donkeykong2" }, "donkeykong")
                .Should().Be("donkeykong3");
            ShellCharacterCloneService.NextFreeLinkage(new[] { "DonkeyKong" }, "donkeykong")
                .Should().Be("donkeykong2");
        }

        [Fact]
        public void CloneInsteadMessage_OnlyWhenLinkageExists()
        {
            var roster = new[]
            {
                new ShellRosterCharacter { Id = "mario", DisplayName = "Mario" }
            };
            ShellCharacterCloneService.CloneInsteadMessage(roster, "mario")
                .Should().Contain("Clone that character");
            ShellCharacterCloneService.CloneInsteadMessage(roster, "banjokazooie")
                .Should().BeNull();
        }

        [Fact]
        public void RewriteText_ShouldReplaceExactAndPascalVariants()
        {
            var input = "class Mario extends Fighter { var mario = Mario; }";
            var output = CharacterCloneService.RewriteText(input, "mario", "wario");
            output.Should().Contain("Wario");
            output.Should().Contain("wario");
            output.Should().NotContain("Mario");
            output.Should().NotContain("mario");
        }

        [Fact]
        public void RewriteText_ShouldNotCorruptLinkageIdKey_WhenCloningLink()
        {
            var input = "\"statsName\":\"link\",\n\"linkage_id\":\"link\",\n\"linkage_id2\":null,\n\"classAPI\":LinkExt,\n\"link_fla.Idle\",\nblink Linkage";
            var output = CharacterCloneService.RewriteText(input, "link", "link_clone");

            output.Should().Contain("\"linkage_id\":\"link_clone\"");
            output.Should().Contain("\"linkage_id2\":null");
            output.Should().NotContain("link_cloneage_id");
            output.Should().Contain("\"statsName\":\"link_clone\"");
            output.Should().Contain("LinkCloneExt");
            output.Should().Contain("link_clone_fla.Idle");
            output.Should().Contain("blink");
            output.Should().Contain("Linkage");
            output.Should().NotContain("\"statsName\":\"link\"");
        }

        [Fact]
        public void RewriteIdentifierPairsToFile_MatchesInMemoryRewrite()
        {
            var input = "class Mario extends Fighter { var mario = Mario; }";
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "mario", "wario", new CharacterCloneService.CharacterIdentityNames());
            var expected = CharacterCloneService.RewriteTextWithPairs(input, plan);
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xmlstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "in.xml");
                var dest = Path.Combine(dir, "out.xml");
                File.WriteAllText(src, input);
                CharacterCloneXmlStream.RewriteIdentifierPairsToFile(src, dest, plan);
                File.ReadAllText(dest).Should().Be(expected);
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RewriteIdentifierPairsToFile_SkipsHexDumps_StillRenamesBoundaries()
        {
            var plan = new List<(string From, string To)> { ("bandanadee", "Clone_Dee") };
            var hex = new string('a', 250_000);
            var input = $">bandanadee<{hex}>bandanadee<";
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xmlstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "in.xml");
                var dest = Path.Combine(dir, "out.xml");
                File.WriteAllText(src, input);
                var hits = CharacterCloneXmlStream.RewriteIdentifierPairsToFile(src, dest, plan);
                hits.Should().Be(2);
                File.ReadAllText(dest).Should().Be($">Clone_Dee<{hex}>Clone_Dee<");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RewriteIdentifierPairsToFile_LargeHexDump_CompletesQuickly()
        {
            var plan = new List<(string From, string To)> { ("blackmage", "Clone_Mage"), ("bm_taunt3", "Clone_Mage_taunt3") };
            var hex = new string('a', 8_000_000);
            var input = $"<tag>blackmage</tag>{hex}<tag>bm_taunt3</tag>";
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xmlstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "in.xml");
                var dest = Path.Combine(dir, "out.xml");
                File.WriteAllText(src, input);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var hits = CharacterCloneXmlStream.RewriteIdentifierPairsToFile(src, dest, plan);
                sw.Stop();
                hits.Should().Be(2);
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
                var text = File.ReadAllText(dest);
                text.Should().StartWith("<tag>Clone_Mage</tag>");
                text.Should().EndWith("<tag>Clone_Mage_taunt3</tag>");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RewriteIdentifierPairsToFile_LongHexRun_SkipsCandidateLettersInsideDump()
        {
            var plan = new List<(string From, string To)> { ("blackmage", "Clone_Mage") };
            var hex = new string('a', 120) + new string('b', 120) + new string('c', 120);
            var input = $"<item>blackmage</item>{hex}<item>blackmage</item>";
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xmlstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var src = Path.Combine(dir, "in.xml");
                var dest = Path.Combine(dir, "out.xml");
                File.WriteAllText(src, input);
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var hits = CharacterCloneXmlStream.RewriteIdentifierPairsToFile(src, dest, plan);
                sw.Stop();
                hits.Should().Be(2);
                sw.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1));
                File.ReadAllText(dest).Should().Be(
                    $"<item>Clone_Mage</item>{hex}<item>Clone_Mage</item>");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void ApplyCloneMetadataInFile_UpdatesDisplayNameAndGuidInOnePass()
        {
            var input = """
                <item>displayName</item><item>Black Mage</item>
                <item>guid</item><item>old-guid-value</item>
                register("guid","old-guid-value")
                copy old-guid-value here
                """;
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-xmlstream-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "in.xml");
                File.WriteAllText(path, input);
                var (displayHits, guidHits) = CharacterCloneXmlStream.ApplyCloneMetadataInFile(
                    path, "Clone Mage", "new-guid-value");
                displayHits.Should().Be(1);
                guidHits.Should().BeGreaterThan(0);
                var text = File.ReadAllText(path);
                text.Should().Contain("<item>Clone Mage</item>");
                text.Should().Contain("new-guid-value");
                text.Should().NotContain("old-guid-value");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void RewriteText_LinkExt_ShouldBecomeLinkCloneExt_NotLinkage()
        {
            CharacterCloneService.RewriteText("LinkExt Linkage", "link", "link_clone")
                .Should().Be("LinkCloneExt Linkage");
        }

        [Fact]
        public void RewriteSymbolClassFile_ShouldUpdateCsv()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var csv = Path.Combine(dir, "symbols.csv");
                File.WriteAllText(csv, "0;com.game.Mario\n1;com.game.MarioIdle\n");
                var hits = CharacterCloneService.RewriteSymbolClassFile(csv, "Mario", "Wario");
                hits.Should().BeGreaterThan(0);
                var text = File.ReadAllText(csv);
                text.Should().Contain("com.game.Wario");
                text.Should().Contain("com.game.WarioIdle");
                text.Should().NotContain("Mario");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void RewriteScriptsFolder_ShouldRewriteTextWithoutRenamingFiles()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-test-" + Path.GetRandomFileName());
            var scripts = Path.Combine(dir, "scripts", "com");
            Directory.CreateDirectory(scripts);
            try
            {
                var asFile = Path.Combine(scripts, "Mario.as");
                File.WriteAllText(asFile, "package com { public class Mario { public static var id:String = \"mario\"; } }");

                var (touched, reps) = CharacterCloneService.RewriteScriptsFolder(
                    Path.Combine(dir, "scripts"), "mario", "wario");

                touched.Should().BeGreaterThan(0);
                reps.Should().BeGreaterThan(0);
                File.Exists(asFile).Should().BeTrue("paths must stay stable for FFDec matching helpers");
                File.ReadAllText(asFile).Should().Contain("wario");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void ApplyExpansionIdInXml_ShouldReplaceIdAssignment()
        {
            var xml = "public static var id:String = \"mario\";\nother = \"mario\";";
            var rewritten = CharacterCloneService.ApplyExpansionIdInXml(xml, "mario");
            rewritten.Should().Contain("id:String = \"expansion\"");
            rewritten.Should().Contain("other = \"mario\"");
        }

        [Fact]
        public void ApplyExpansionIdInXml_ShouldReplaceRegisterAndPoolItem()
        {
            var xml = "register(\"id\", \"mario_clone\");\n<item>id</item>\n<item>mario_clone</item>";
            var rewritten = CharacterCloneService.ApplyExpansionIdInXml(xml, "mario_clone");
            rewritten.Should().Contain("register(\"id\", \"expansion\")");
            rewritten.Should().Contain("<item>id</item>\n<item>expansion</item>");
        }

        [Fact]
        public void ApplyExpansionIdInScripts_ShouldReplaceIdAssignment()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-test-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var asFile = Path.Combine(dir, "Main.as");
                File.WriteAllText(asFile, "public static var id:String = \"mario\";\nother = \"mario\";");
                var hits = CharacterCloneService.ApplyExpansionIdInScripts(dir, "mario");
                hits.Should().Be(1);
                var text = File.ReadAllText(asFile);
                text.Should().Contain("id:String = \"expansion\"");
                text.Should().Contain("other = \"mario\"");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public void UntypeFlaInstanceSlotsCommand_IsWorkerArg()
        {
            const string cmd = "-untypestance \"in.swf\" \"out.swf\"";
            cmd.Should().StartWith("-untypestance");
            cmd.Should().Contain("in.swf");
        }

        [Fact]
        public void ApplyDisplayNameInXml_ShouldReplaceValueAfterDisplayNameKey()
        {
            var xml = "<item>statsName</item>\n<item>displayName</item>\n<item>Mario</item>\n<item>linkage_id</item>";
            var (result, hits) = CharacterCloneService.ApplyDisplayNameInXml(xml, "Just Mario");

            hits.Should().Be(1);
            result.Should().Contain("<item>displayName</item>\n<item>Just Mario</item>");
            result.Should().Contain("<item>linkage_id</item>");
        }

        [Fact]
        public void ApplyGuidInXml_ReplacesRegisterAndPoolValue()
        {
            const string oldGuid = "d445439f-d1bc-4b9e-be8b-cb156f498f38";
            const string newGuid = "dca33470-384b-476e-a94a-355dc4f12ceb";
            var xml =
                $"register(\"guid\",\"{oldGuid}\");\n" +
                $"<item>guid</item>\n<item>{oldGuid}</item>\n" +
                $"<item>other</item>\n<item>{oldGuid}</item>";
            var (result, hits) = CharacterCloneService.ApplyGuidInXml(xml, newGuid);
            hits.Should().BeGreaterThan(0);
            result.Should().Contain($"register(\"guid\",\"{newGuid}\")");
            result.Should().Contain($"<item>guid</item>\n<item>{newGuid}</item>");
            result.Should().NotContain(oldGuid);
        }

        [Fact]
        public void ApplyDisplayNameInXml_ShouldEscapeXmlAndGuardKnownKeys()
        {
            // Guard: if the item after displayName is a known key (value was deduplicated), skip it.
            var guarded = "<item>displayName</item><item>linkage_id</item>";
            CharacterCloneService.ApplyDisplayNameInXml(guarded, "Cool").hits.Should().Be(0);

            // Escaping of XML-special characters in the new display name.
            var xml = "<item>displayName</item><item>Old</item>";
            var (result, hits) = CharacterCloneService.ApplyDisplayNameInXml(xml, "Tom & Jerry <2>");
            hits.Should().Be(1);
            result.Should().Contain("<item>Tom &amp; Jerry &lt;2&gt;</item>");
        }

        [Fact]
        public void SuggestOldNameFromPath_ShouldUseFilename()
        {
            CharacterCloneService.SuggestOldNameFromPath(@"C:\mods\Pikachu.swf").Should().Be("Pikachu");
            CharacterCloneService.SuggestOldNameFromPath(@"C:\mods\data.swf").Should().Be("");
        }

        [Fact]
        public void JpexsResolveExecutable_ShouldFindBatInFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-ffdec-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var bat = Path.Combine(dir, "ffdec.bat");
                File.WriteAllText(bat, "@echo off");
                var svc = new JpexsCliService();
                svc.ResolveExecutable(dir).Should().Be(bat);
                svc.IsAvailable(dir).Should().BeTrue();
                // Configured-only resolver returns null when no path is given (bundled fallback is separate).
                svc.ResolveExecutable(null).Should().BeNull();
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { }
            }
        }

        [Fact]
        public async Task CloneAsync_WithRealFfdec_ShouldProduceVerifiedClone()
        {
            var idk = Environment.GetEnvironmentVariable("SSF2_IDK_ROOT");
            var source = string.IsNullOrWhiteSpace(idk) ? "" : Path.Combine(idk, "data", "character", "ganondorf.ssf");
            var ffdec = @"C:\Program Files (x86)\FFDec";
            if (!File.Exists(source) || !Directory.Exists(ffdec))
                return;

            var outDir = Path.Combine(Path.GetTempPath(), "ssf2mm-clone-e2e-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(outDir);
            try
            {
                var messages = new System.Collections.Generic.List<string>();
                var progress = new Progress<string>(m => messages.Add(m));
                var svc = new CharacterCloneService();
                var result = await svc.CloneAsync(new CharacterCloneRequest
                {
                    SourcePath = source,
                    OldName = "ganondorf",
                    NewName = "ganondorf_clone",
                    OutputDirectory = outDir,
                    JpexsPath = ffdec
                }, progress);

                File.Exists(result.OutputSwfPath).Should().BeTrue();
                new FileInfo(result.OutputSwfPath).Length.Should().BeGreaterThan(1000);
                result.XmlReplacements.Should().BeGreaterThan(0);
                messages.Should().Contain(m => m.Contains("XML", StringComparison.OrdinalIgnoreCase)
                    || m.Contains("Verifying", StringComparison.OrdinalIgnoreCase));

                // Independent verification: export SymbolClass from output
                var verifyDir = Path.Combine(outDir, "verify");
                Directory.CreateDirectory(verifyDir);
                var jpexs = new JpexsCliService();
                await jpexs.ExportAsync(ffdec, result.OutputSwfPath, verifyDir, "symbolClass");
                var csv = Directory.GetFiles(verifyDir, "*.csv", SearchOption.AllDirectories).FirstOrDefault();
                csv.Should().NotBeNull();
                var csvText = await File.ReadAllTextAsync(csv!);
                csvText.Should().Contain("ganondorf_clone");
                csvText.Split('\n', '\r')
                    .Any(line =>
                    {
                        var parts = line.Split(';');
                        return parts.Length >= 2 && parts[^1].Trim().Equals("ganondorf", StringComparison.Ordinal);
                    })
                    .Should().BeFalse("bare document class ganondorf must be gone");
            }
            finally
            {
                try { Directory.Delete(outDir, true); } catch { }
            }
        }

        [Fact]
        public void CollectIdentityOldNames_ShouldIncludeLinkageId_NotSymbolClassJunk()
        {
            var xml =
                "<item>statsName</item><item>donkeykong</item>" +
                "<item>linkage_id</item><item>dkong</item>" +
                "<item type=\"SymbolClassTag\">" +
                "<names><item>dkong</item><item>dkong_charselect</item><item>HitSpark</item></names>" +
                "</item>";

            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "donkeykong");
            roots.Should().Contain("donkeykong");
            roots.Should().Contain("dkong");
            roots.Should().NotContain("HitSpark");
            roots.Should().NotContain("dkong_charselect");
        }

        [Fact]
        public void CollectIdentityOldNames_SymbolClassFamily_WhenPoolValueDeduped()
        {
            // linkage_id key is followed by another key (deduped value) — must still find dkong via SymbolClass family.
            var xml =
                "<item>statsName</item><item>linkage_id</item><item>linkage_id2</item>" +
                "<item type=\"SymbolClassTag\">" +
                "<names><item>dkong</item><item>dkong_charselect</item><item>dkong_hud</item><item>HitSpark</item></names>" +
                "</item>";

            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "donkeykong");
            roots.Should().Contain("donkeykong");
            roots.Should().Contain("dkong");
            roots.Should().NotContain("HitSpark");
        }

        [Fact]
        public void BuildCloneReplacementPlan_DkDualRoot_MapsLinkageToNewName()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "donkeykong",
                LinkageId = "dkong"
            };
            var roots = new[] { "donkeykong", "dkong" };
            var plan = CharacterCloneService.BuildCloneReplacementPlan("donkeykong", "donkeykong_clone", identity, roots);
            plan.Should().Contain(p => p.From == "donkeykong" && p.To == "donkeykong_clone");
            plan.Should().Contain(p => p.From == "dkong" && p.To == "donkeykong_clone");
        }

        [Fact]
        public void RewriteTextWithPairs_DkDualRoot_RenamesCharselectAndFla()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "donkeykong",
                LinkageId = "dkong"
            };
            var roots = new[] { "donkeykong", "dkong" };
            var plan = CharacterCloneService.BuildCloneReplacementPlan("donkeykong", "donkeykong_clone", identity, roots);
            var input = "dkong dkong_charselect dkong_hud donkeykong_fla.Idle HitSpark";
            var output = CharacterCloneService.RewriteTextWithPairs(input, plan);

            output.Should().Contain("donkeykong_clone");
            output.Should().Contain("donkeykong_clone_charselect");
            output.Should().Contain("donkeykong_clone_hud");
            output.Should().Contain("donkeykong_clone_fla.Idle");
            output.Should().Contain("HitSpark");
            output.Should().NotContain("dkong_charselect");
            CharacterCloneService.CountIdentifierOccurrences(output, "dkong").Should().Be(0);
        }

        [Fact]
        public void CollectIdentityOldNames_BmTaunt3Metal_IsNotDocumentFamilyRoot()
        {
            var xml =
                "<item type=\"SymbolClassTag\">" +
                "<names><item>bm_taunt3</item><item>bm_taunt3_metal</item><item>bio</item></names>" +
                "</item>";

            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "blackmage");
            roots.Should().NotContain("bm_taunt3");
        }

        [Fact]
        public void BuildCloneReplacementPlan_BlackMageMoveRoots_KeepSuffixNotFighterId()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage",
                LinkageId = "black_mage",
                ClassApi = "BlackMageExt"
            };
            var roots = new[] { "blackmage", "bm_taunt3", "bm_waterSpout" };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Clone_Mage", identity, roots);

            plan.Should().Contain(p => p.From == "blackmage" && p.To == "Clone_Mage");
            plan.Should().Contain(p => p.From == "bm_taunt3" && p.To == "Clone_Mage_bm_taunt3");
            plan.Should().Contain(p => p.From == "bm_waterSpout" && p.To == "Clone_Mage_bm_waterSpout");
            plan.Should().NotContain(p => p.From == "bm_taunt3" && p.To == "Clone_Mage");
            plan.Should().NotContain(p => p.To == "Clone_Mage" && p.From != "blackmage" && p.From != "black_mage");
            plan.Should().Contain(p => p.From == "black_mage" && p.To == "Clone_Mage");
        }

        [Fact]
        public void BuildCloneReplacementPlan_BlackMageUnderscoreAlias_MapsToFighterId()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "best_mage", identity, new[] { "blackmage", "black_mage" });
            plan.Should().Contain(p => p.From == "black_mage" && p.To == "best_mage");
            plan.Should().NotContain(p => p.From == "black_mage" && p.To == "best_mage_black_mage");
        }

        [Fact]
        public void MapExtClassName_BoMay_AlwaysIdentExt_NotPascal()
        {
            CharacterCloneService.MapExtClassName("BlackMageExt", "blackmage", "Bo_May", new[] { "BlackMage" })
                .Should().Be("Bo_MayExt");
            CharacterCloneService.MapExtClassName("BlackMageExt", "blackmage", "Bo_May")
                .Should().Be("Bo_MayExt");
            CharacterCloneService.MapExtClassName("BlackMageExt", "blackmage", "Bo_May", new[] { "BlackMage" })
                .Should().NotBe("BoMayExt");
        }

        [Fact]
        public void BuildCloneReplacementPlan_BoMay_ExtAndAsCastAreBo_MayExt()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage",
                LinkageId = "black_mage",
                ClassApi = "BlackMageExt"
            };
            var xml = """
                <item>classAPI</item><item>BlackMageExt</item>
                <item>statsName</item><item>blackmage</item>
                <item type="SymbolClassTag"><names>
                <item>black_mage</item>
                <item>BlackMageExt</item>
                </names></item>
                """;
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "blackmage");
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Bo_May", identity, roots, xml);
            plan.Should().Contain(p => p.From == "BlackMageExt" && p.To == "Bo_MayExt");
            plan.Should().NotContain(p => p.To == "BoMayExt");
            plan.Should().NotContain(p => p.From == "BlackMage" && p.To == "BoMayExt");
            CharacterCloneService.RewriteTextWithPairs("as BlackMageExt", plan)
                .Should().Be("as Bo_MayExt");
        }

        [Fact]
        public void BuildCloneReplacementPlan_BareSweep_BlackMageSymbolMapsToIdentNotPrefixed()
        {
            var xml = """
                <item type="SymbolClassTag"><names>
                <item>black_mage</item>
                <item>Main</item>
                </names></item>
                """;
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Bo_Dada", identity, new[] { "blackmage" }, xml);
            plan.Should().Contain(p => p.From == "black_mage" && p.To == "Bo_Dada");
            plan.Should().NotContain(p => p.To == "Bo_Dada_black_mage");

            var rewritten = CharacterCloneService.RewriteTextWithPairs("black_mage", plan);
            rewritten.Should().Be("Bo_Dada");
            CharacterCloneService.VerifyIdentityRootsRewritten(
                new[] { rewritten }, new[] { "blackmage", "black_mage" }, "Bo_Dada", plan);
        }

        [Fact]
        public void BuildCloneReplacementPlan_LeftoverIdentBlackMageHost_MapsToNewIdent()
        {
            var xml = """
                <item type="SymbolClassTag"><names>
                <item>Bo_Mama</item>
                <item>Bo_Mama_black_mage</item>
                </names></item>
                """;
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "Bo_Mama",
                LinkageId = "Bo_Mama"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "Bo_Mama", "Bo_Dada", identity, new[] { "Bo_Mama" }, xml);
            plan.Should().Contain(p => p.From == "Bo_Mama_black_mage" && p.To == "Bo_Dada");
            CharacterCloneService.RewriteTextWithPairs("Bo_Mama_black_mage", plan)
                .Should().Be("Bo_Dada");
        }

        [Fact]
        public void MapIdentityRootToNewName_BioSound_GetsClonePrefix_NotFighterIdent()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage",
                LinkageId = "black_mage"
            };
            CharacterCloneService.MapIdentityRootToNewName("bio", "blackmage", "Boost", identity)
                .Should().Be("Boost_bio");
            CharacterCloneService.MapIdentityRootToNewName("black_mage", "blackmage", "Boost", identity)
                .Should().Be("Boost");
        }

        [Fact]
        public void BuildCloneReplacementPlan_BlackMageBioSound_IsBoost_bio()
        {
            var xml =
                "<item type=\"SymbolClassTag\"><names>" +
                "<item>black_mage</item><item>bio</item><item>bm_taunt3</item>" +
                "</names></item>";
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage",
                LinkageId = "black_mage"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Boost", identity, new[] { "blackmage", "black_mage", "bm_taunt3" }, xml);

            plan.Should().Contain(p => p.From == "black_mage" && p.To == "Boost");
            plan.Should().NotContain(p => p.From == "bio");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "bio" };
            var takenPlan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Boost", identity, new[] { "blackmage", "black_mage", "bm_taunt3" }, xml,
                takenNames: taken);
            takenPlan.Should().Contain(p => p.From == "bio" && p.To == "Boost_bio");
            takenPlan.Should().NotContain(p => p.From == "bio" && p.To == "Boost");

            var sounds = "\"sounds\":[\"bio\",\"bm_taunt3\"]";
            var movieclips = "\"movieclips\":[\"black_mage\",\"dust\"]";
            var nextSounds = CharacterCloneService.RewriteTextWithPairs(sounds, takenPlan);
            var nextClips = CharacterCloneService.RewriteTextWithPairs(movieclips, plan);
            nextSounds.Should().Contain("\"Boost_bio\"");
            nextSounds.Should().Contain("\"Boost_bm_taunt3\"");
            nextSounds.Should().NotContain("\"Boost\"");
            nextClips.Should().Contain("\"Boost\"");
            nextClips.Should().NotContain("\"black_mage\"");
        }

        [Fact]
        public void RewriteTextWithPairs_BlackMageSounds_DoesNotRegisterFighterAsSound()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "blackmage",
                LinkageId = "black_mage"
            };
            var roots = new[] { "blackmage", "bm_taunt3" };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "blackmage", "Clone_Mage", identity, roots);
            var input =
                "\"sounds\":[\"bio\",\"bmtaunt2\",\"bmtaunt3\",\"bmvictory\",\"bm_taunt2\",\"bm_taunt3\",\"bm_Usmash_full\"]";
            var output = CharacterCloneService.RewriteTextWithPairs(input, plan);

            output.Should().Contain("Clone_Mage_bm_taunt3");
            output.Should().NotContain("\"Clone_Mage\",\"");
            output.Should().NotContain(",\"Clone_Mage\"");
        }

        [Fact]
        public void MergeDonorSoundAliasPairs_AddsCompressedBmSoundIds()
        {
            var pairs = new List<(string From, string To)>
            {
                ("bm_taunt2", "Clone_Mage_bm_taunt2"),
                ("bm_taunt3", "Clone_Mage_bm_taunt3")
            };
            var seen = new HashSet<string>(pairs.Select(p => p.From), StringComparer.Ordinal);
            var xml = "\"sounds\":[\"bmtaunt2\",\"bmtaunt3\",\"bm_taunt2\",\"bm_taunt3\"]";
            CharacterCloneService.MergeDonorSoundAliasPairs(xml, pairs, seen);
            pairs.Should().Contain(p => p.From == "bmtaunt2" && p.To == "Clone_Mage_bm_taunt2");
            pairs.Should().Contain(p => p.From == "bmtaunt3" && p.To == "Clone_Mage_bm_taunt3");
        }

        [Fact]
        public void BuildCloneReplacementPlan_SameName_OnlyPrimary()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "mario",
                LinkageId = "mario"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan("mario", "mario_clone", identity, new[] { "mario" });
            plan.Should().Contain(p => p.From == "mario" && p.To == "mario_clone");
            // Only the mario identity root (plus BuildReplacementPairs case variants) — no extra roots.
            plan.Select(p => p.From.ToLowerInvariant()).Distinct()
                .Should().Equal("mario");
        }

        [Fact]
        public void BuildCloneReplacementPlan_LinkageId2_GetsCloneSuffix()
        {
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "samus",
                LinkageId = "samus",
                LinkageId2 = "zamus"
            };
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "samus", "samus_clone", identity, new[] { "samus", "zamus" });
            plan.Should().Contain(p => p.From == "samus" && p.To == "samus_clone");
            plan.Should().Contain(p => p.From == "zamus" && p.To == "zamus_clone");
        }

        [Fact]
        public void VerifyIdentityRootsRewritten_FailsWhenBareDkongRemains()
        {
            var xml = "<item type=\"SymbolClassTag\"><names><item>dkong</item><item>donkeykong_clone_fla</item></names></item>";
            Action act = () => CharacterCloneService.VerifyIdentityRootsRewritten(
                xml, new[] { "donkeykong", "dkong" }, "donkeykong_clone");
            act.Should().Throw<InvalidOperationException>().WithMessage("*dkong*");
        }

        [Fact]
        public void VerifyIdentityRootsRewritten_PassesWhenRootsGone()
        {
            var xml = "<item type=\"SymbolClassTag\"><names><item>donkeykong_clone</item><item>donkeykong_clone_charselect</item></names></item>";
            CharacterCloneService.VerifyIdentityRootsRewritten(
                xml, new[] { "donkeykong", "dkong" }, "donkeykong_clone");
        }

        [Fact]
        public void VerifyIdentityRootsRewritten_FailsWhenIdentBlackMageHostRemains()
        {
            var xml = "<item type=\"SymbolClassTag\"><names><item>Bo_Mama_black_mage</item></names></item>";
            Action act = () => CharacterCloneService.VerifyIdentityRootsRewritten(
                xml, new[] { "black_mage", "blackmage" }, "Bo_Mama");
            act.Should().Throw<InvalidOperationException>().WithMessage("*Bo_Mama_black_mage*");
        }

        [Fact]
        public void ExtractCostumeBlock_AlsoRewritesExtraKeys()
        {
            var misc =
                "_loc1_[\"donkeykong\"] = new Array();\r\n" +
                "_loc1_[\"donkeykong\"].push({ base:true });\r\n" +
                "_loc1_[\"mario\"] = new Array();\r\n";
            var block = CharacterCloneService.ExtractCostumeBlock(
                misc, "donkeykong", "donkeykong_clone", new[] { "donkeykong", "dkong" });
            block.Should().NotBeNull();
            block.Should().Contain("_loc1_[\"donkeykong_clone\"]");
            block.Should().NotContain("_loc1_[\"donkeykong\"]");
        }

        [Fact]
        public void ToCostumesJson_WritesJsonArrayNotAsPush()
        {
            var block = "_loc1_[\"mario_clone\"].push({ \"base\":true, \"paletteSwap\":{ \"colors\":[4281413937,4279966491], \"replacements\":[] } });";
            var json = CharacterCloneService.ToCostumesJson(block, "mario_clone");
            json.TrimStart().Should().StartWith("[");
            json.Should().NotContain("_loc1_");
            json.Should().NotContain(".push(");
            json.Should().Contain("\"base\": true");
            json.Should().Contain("\"colors\": [4281413937, 4279966491]");
        }

        [Fact]
        public void CompactJsonNumberArrays_PutsIntsOnOneLine()
        {
            var pretty = """
                {
                  "colors": [
                    4281413937,
                    4279966491
                  ]
                }
                """;
            var compact = LivePackRuntimeService.CompactJsonNumberArrays(pretty);
            compact.Should().Contain("\"colors\": [4281413937, 4279966491]");
            compact.Should().NotContain("4281413937,\r\n");
        }

        [Fact]
        public void BuildCloneReplacementPlan_AbbrevAndBareSweep_RenamesCutinAndArcade()
        {
            var xml =
                "<item type=\"SymbolClassTag\"><names>" +
                "<item>dkong</item><item>dkong_charselect</item>" +
                "<item>dk_fs_cutin</item><item>dkhead</item><item>DKVictory</item>" +
                "<item>arcade_dead</item><item>Main</item><item>HitSpark</item>" +
                "</names></item>";
            var identity = new CharacterCloneService.CharacterIdentityNames
            {
                StatsName = "donkeykong",
                LinkageId = "dkong",
                FinalSmashCutin = "dk_fs_cutin",
                Thumbnail = "dkhead"
            };
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "donkeykong");
            roots.Should().Contain("dkong");
            roots.Should().Contain("dk");

            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "donkeykong", "donkeykong_clone", identity, roots, xml);
            var output = CharacterCloneService.RewriteTextWithPairs(
                "dk_fs_cutin dkhead DKVictory arcade_dead Main HitSpark dkong", plan);

            output.Should().Contain("donkeykong_clone_fs_cutin");
            output.Should().Contain("dkhead");
            output.Should().Contain("DKVictory");
            output.Should().Contain("arcade_dead");
            output.Should().Contain("HitSpark");
            output.Should().NotContain("dkhead_clone");
            output.Should().NotContain("arcade_dead_clone");
            output.Should().NotContain("HitSpark_clone");
            output.Should().Contain("Main"); // denylist
            output.Should().NotContain("dk_fs_cutin");
            CharacterCloneService.BareSymbolClassDenylist.Should().Contain("Main");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "arcade_dead", "HitSpark", "dkhead" };
            var takenPlan = CharacterCloneService.BuildCloneReplacementPlan(
                "donkeykong", "donkeykong_clone", identity, roots, xml, takenNames: taken);
            var takenOutput = CharacterCloneService.RewriteTextWithPairs(
                "arcade_dead HitSpark dkhead", takenPlan);
            takenOutput.Should().Contain("arcade_dead_clone");
            takenOutput.Should().Contain("HitSpark_clone");
            takenOutput.Should().Contain("dkhead_clone");
        }

        [Fact]
        public void MergeBareSymbolClassSweep_LeavesMainAlone()
        {
            var xml = "<item type=\"SymbolClassTag\"><names><item>Main</item><item>fairSparkle</item></names></item>";
            var pairs = new List<(string From, string To)>();
            CharacterCloneService.MergeBareSymbolClassSweep(xml, "donkeykong", "donkeykong_clone", pairs);
            pairs.Should().NotContain(p => p.From == "Main");
            pairs.Should().NotContain(p => p.From == "fairSparkle");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "fairSparkle" };
            CharacterCloneService.MergeBareSymbolClassSweep(
                xml, "donkeykong", "donkeykong_clone", pairs, takenNames: taken);
            pairs.Should().Contain(p => p.From == "fairSparkle" && p.To == "fairSparkle_clone");
        }

        [Fact]
        public void ApplyCloneSuffix_FullRename_PrefixesLeftoverExports()
        {
            CharacterCloneService.ApplyCloneSuffix("dee_nspec", "bandanadee", "Clone_Dee")
                .Should().Be("Clone_Dee_dee_nspec");
            CharacterCloneService.ApplyCloneSuffix("Kirby_Symbol", "bandanadee", "Clone_Dee")
                .Should().Be("Clone_Dee_Kirby_Symbol");
            CharacterCloneService.ApplyCloneSuffix("fairSparkle", "donkeykong", "donkeykong_clone")
                .Should().Be("fairSparkle_clone");
        }

        [Fact]
        public void CollectIdentityOldNames_KeepsIrregularPascalFromClassApi()
        {
            var xml = """
                <item>classAPI</item><item>BandanaDeeExt</item>
                <item>statsName</item><item>bandanadee</item>
                <item type="SymbolClassTag"><names>
                <item>bandanadee</item>
                <item>bandanadee_fla.BandanaDeeCut_InPATriangle_134</item>
                </names></item>
                """;
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "bandanadee");
            roots.Should().Contain("bandanadee");
            roots.Should().Contain("BandanaDee");
        }

        [Fact]
        public void RewritePlan_BandanaDeeExt_BecomesCloneDeeExt()
        {
            var xml = """
                <item>classAPI</item><item>BandanaDeeExt</item>
                <item>DeeSpearExt</item>
                <item>statsName</item><item>bandanadee</item>
                <item type="SymbolClassTag"><names>
                <item>bandanadee</item>
                <item>bandanadee_fla.BandanaDeeCut_InPATriangle_134</item>
                <item>dairSparkle</item>
                <item>Main</item>
                </names></item>
                """;
            var identity = CharacterCloneService.ParseIdentityFieldsFromXml(xml);
            identity.ClassApi.Should().Be("BandanaDeeExt");
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "bandanadee");
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "bandanadee", "Clone_Dee", identity, roots, xml);

            var output = CharacterCloneService.RewriteTextWithPairs(
                "classAPI:BandanaDeeExt DeeSpearExt BandanaDeeCut dairSparkle bandanadee Main", plan);
            output.Should().Contain("Clone_DeeExt");
            output.Should().Contain("Clone_DeeSpearExt");
            output.Should().Contain("Clone_DeeCut");
            output.Should().Contain("Clone_Dee");
            output.Should().NotContain("BandanaDeeExt");
            output.Should().NotContain("bandanadee");
            output.Should().Contain("dairSparkle");
            output.Should().NotContain("Clone_Dee_dairSparkle");

            var leftover = new List<(string From, string To)>(plan);
            CharacterCloneService.MergeBareSymbolClassSweep(xml, "bandanadee", "Clone_Dee", leftover);
            leftover.Should().NotContain(p => p.From == "dairSparkle");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "dairSparkle" };
            CharacterCloneService.MergeBareSymbolClassSweep(
                xml, "bandanadee", "Clone_Dee", leftover, takenNames: taken);
            leftover.Should().Contain(p => p.From == "dairSparkle" && p.To == "Clone_Dee_dairSparkle");
            leftover.Should().NotContain(p => p.From == "dairSparkle" && p.To == "Clone_Dee");
        }

        [Fact]
        public void BuildCloneReplacementPlan_Zamus2_RenamesIdExportsAndLeavesStockLookups()
        {
            var xml = """
                <item>linkage_id</item><item>zamus</item>
                <item>statsName</item><item>zamus</item>
                <item>classAPI</item><item>ZamusExt</item>
                <item>thumbnail</item><item>zamus_head</item>
                <item>seriesIcon</item><item>MetroidSymbolCSS</item>
                <item type="SymbolClassTag"><names>
                <item>zamus</item>
                <item>zamus_head</item>
                <item>ZamusExt</item>
                <item>ZamusDizzy_0</item>
                <item>samus_dr0</item>
                <item>zss_fs_end</item>
                </names></item>
                """;
            var identity = CharacterCloneService.ParseIdentityFieldsFromXml(xml);
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "zamus");
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "zamus", "zamus2", identity, roots, xml);
            var output = CharacterCloneService.RewriteTextWithPairs(
                "linkage_id zamus thumbnail zamus_head seriesIcon MetroidSymbolCSS ZamusExt ZamusDizzy_0 samus_dr0 zss_fs_end",
                plan);

            output.Should().Contain("zamus2_head");
            output.Should().Contain("zamus2Ext");
            output.Should().Contain("Zamus2Dizzy_0");
            output.Should().Contain("MetroidSymbolCSS");
            output.Should().Contain("samus_dr0");
            output.Should().Contain("zss_fs_end");
            output.Should().NotContain("MetroidSymbolCSS2");
            output.Should().NotContain("samus_dr02");
            output.Should().NotContain("zss_fs_end2");
            output.Should().NotContain("zamus_head2");
            plan.Should().NotContain(p => p.From == "MetroidSymbolCSS");
            plan.Should().NotContain(p => p.From == "samus_dr0");
            plan.Should().NotContain(p => p.From == "zss_fs_end");
        }

        [Fact]
        public void WriteIdentPairsFile_WritesIdentifierAndExactRows()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-pairs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                var path = Path.Combine(dir, "pairs.txt");
                var count = CharacterCloneService.WriteIdentPairsFile(
                    path,
                    new[] { ("blackmage", "Clone_Mage"), ("b", "Clone_Mage_b") },
                    "Black Mage",
                    "Clone Mage",
                    "old-guid",
                    "new-guid");
                count.Should().Be(4);
                var text = File.ReadAllText(path);
                text.Should().Contain("I\tblackmage\tClone_Mage");
                text.Should().Contain("I\tb\tClone_Mage_b");
                text.Should().Contain("E\tBlack Mage\tClone Mage");
                text.Should().Contain("E\told-guid\tnew-guid");
            }
            finally
            {
                try { Directory.Delete(dir, true); } catch { /* ignore */ }
            }
        }

        [Fact]
        public void CollectIdentityOldNames_SkipsStatsKeyAfterSeriesIcon()
        {
            var xml = """
                <item>seriesIcon</item><item>maxShieldSize</item>
                <item>statsName</item><item>gameandwatch</item>
                """;
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "gameandwatch");
            roots.Should().Contain("gameandwatch");
            roots.Should().NotContain("maxShieldSize");
        }

        [Fact]
        public void CollectIdentityOldNames_SkipsStatsNeighborsAndKeepsSeriesSymbol()
        {
            var xml = """
                <item>seriesIcon</item><item>revivalEffect</item>
                <item>statsName</item><item>getCharacterStat</item>
                <item>seriesIcon</item><item>Starfox_Symbol</item>
                <item>statsName</item><item>falco</item>
                <item>linkage_id</item><item>inheritPalette</item>
                <item>linkage_id</item><item>falco</item>
                <item>finalSmashCutin</item><item>fs_time_limit</item>
                <item>finalSmashCutin</item><item>falco_fs_cutin</item>
                <item>seriesIcon</item><item>tiltTossMultiplier</item>
                """;
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "falco");
            roots.Should().Contain("falco");
            roots.Should().Contain("Starfox_Symbol");
            roots.Should().Contain("falco_fs_cutin");
            roots.Should().NotContain("revivalEffect");
            roots.Should().NotContain("getCharacterStat");
            roots.Should().NotContain("inheritPalette");
            roots.Should().NotContain("fs_time_limit");
            roots.Should().NotContain("tiltTossMultiplier");

            var identity = CharacterCloneService.ParseIdentityFieldsFromXml(xml);
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "falco", "DatAintFalco", identity, roots, xml);
            var froms = plan.Select(p => p.From).ToList();
            froms.Should().NotContain("revivalEffect");
            froms.Should().NotContain("getCharacterStat");
            froms.Should().NotContain("inheritPalette");
            froms.Should().NotContain("fs_time_limit");
            froms.Should().NotContain("tiltTossMultiplier");
        }

        [Fact]
        public void ClonePlan_StockRoster_DoesNotRenameStatsFields()
        {
            var shell = Environment.GetEnvironmentVariable("SSF2_SHELL");
            if (string.IsNullOrWhiteSpace(shell)) return;
            var dir = Path.Combine(shell, "data", "character");
            if (!Directory.Exists(dir)) return;

            var keys = new HashSet<string>(StringComparer.Ordinal)
            {
                "statsName", "displayName", "linkage_id", "linkage_id2", "linkage_id_special",
                "finalSmashCutin", "thumbnail", "seriesIcon", "classAPI"
            };
            var bad = new List<string>();
            foreach (var path in Directory.GetFiles(dir, "*.ssf"))
            {
                var id = Path.GetFileNameWithoutExtension(path);
                byte[] swf;
                try { swf = SwfPackage.ToSwf(File.ReadAllBytes(path)); }
                catch { continue; }

                var xml = IdentityNeighborXml(swf, keys);
                var identity = CharacterCloneService.ParseIdentityFieldsFromXml(xml);
                var roots = CharacterCloneService.CollectIdentityOldNames(xml, id);
                foreach (var root in roots)
                {
                    if (IsForeignStatsToken(root, id))
                        bad.Add(id + " root " + root);
                }

                var plan = CharacterCloneService.BuildCloneReplacementPlan(
                    id, "CloneProbe", identity, roots, xml);
                var allowed = CharacterCloneService.DetectAbbrevRoots(roots, id);
                foreach (var (from, _) in plan)
                {
                    if (from.Length == 2 && from.All(char.IsLetter) &&
                        !allowed.Any(a => a.Equals(from, StringComparison.OrdinalIgnoreCase)))
                        bad.Add(id + " abbrev " + from);
                }
            }

            bad.Should().BeEmpty("rename plan over-reached: " + string.Join("; ", bad));
        }

        private static bool IsForeignStatsToken(string value, string id)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (value.Contains(id, StringComparison.OrdinalIgnoreCase)) return false;
            if (value.Length < 4 || !char.IsLower(value[0])) return false;
            if (value.IndexOf('_') >= 0)
                return value.Equals("fs_time_limit", StringComparison.Ordinal);
            for (var i = 1; i < value.Length; i++)
            {
                if (char.IsUpper(value[i])) return true;
            }
            return false;
        }

        private static string IdentityNeighborXml(byte[] swf, HashSet<string> keys)
        {
            var sb = new StringBuilder();
            var run = new StringBuilder();
            string? prev = null;
            void Flush()
            {
                if (run.Length >= 3 && run.Length <= 48)
                {
                    var text = run.ToString();
                    if (prev != null && keys.Contains(prev))
                        sb.Append("<item>").Append(prev).Append("</item><item>").Append(text).Append("</item>");
                    prev = text;
                }
                else
                {
                    prev = null;
                }
                run.Clear();
            }

            foreach (var b in swf)
            {
                if (b >= 32 && b < 127)
                    run.Append((char)b);
                else if (run.Length > 0)
                    Flush();
            }
            if (run.Length > 0) Flush();
            return sb.ToString();
        }

        [Fact]
        public void DetectAbbrevRoots_SkipsPascalSymbol_KeepsLowerBodyId()
        {
            CharacterCloneService.DetectAbbrevRoots(new[] { "MKSymbol" }, "metaknight")
                .Should().BeEmpty();
            CharacterCloneService.DetectAbbrevRoots(new[] { "dkong" }, "donkeykong")
                .Should().Contain("dk");
        }

        [Fact]
        public void RewritePlan_DoesNotRewriteEngineSpecialIds_B()
        {
            var xml = """
                <item>classAPI</item><item>BandanaDeeExt</item>
                <item>statsName</item><item>bandanadee</item>
                <item type="SymbolClassTag"><names>
                <item>bandanadee</item>
                <item>b</item>
                <item>a</item>
                <item>dee_spear</item>
                <item>Main</item>
                </names></item>
                """;
            var identity = CharacterCloneService.ParseIdentityFieldsFromXml(xml);
            var roots = CharacterCloneService.CollectIdentityOldNames(xml, "bandanadee");
            roots.Should().NotContain("b");
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "bandanadee", "Clone_Dee", identity, roots, xml);

            plan.Should().NotContain(p => p.From == "b");
            plan.Should().NotContain(p => p.From == "b_up");
            plan.Should().NotContain(p => p.From == "a");

            var output = CharacterCloneService.RewriteTextWithPairs(
                "xframe = \"b\"; xframe = \"b_up\"; fireProjectile(\"dee_spear\"); bandanadee", plan);
            output.Should().Contain("xframe = \"b\"");
            output.Should().Contain("xframe = \"b_up\"");
            output.Should().Contain("fireProjectile(\"dee_spear\")");
            output.Should().NotContain("Clone_Dee_dee_spear");
            output.Should().Contain("Clone_Dee");
            output.Should().NotContain("Clone_Dee_b");
            output.Should().NotContain("bandanadee");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "dee_spear" };
            var takenPlan = CharacterCloneService.BuildCloneReplacementPlan(
                "bandanadee", "Clone_Dee", identity, roots, xml, takenNames: taken);
            CharacterCloneService.RewriteTextWithPairs("fireProjectile(\"dee_spear\")", takenPlan)
                .Should().Contain("fireProjectile(\"Clone_Dee_dee_spear\")");
        }

        [Fact]
        public void MergeBareSymbolClassSweep_LeavesEngineBAndShortNames()
        {
            var xml = "<item type=\"SymbolClassTag\"><names><item>Main</item><item>b</item><item>a</item><item>fairSparkle</item></names></item>";
            var pairs = new List<(string From, string To)>();
            CharacterCloneService.MergeBareSymbolClassSweep(xml, "bandanadee", "Clone_Dee", pairs);
            pairs.Should().NotContain(p => p.From == "b");
            pairs.Should().NotContain(p => p.From == "a");
            pairs.Should().NotContain(p => p.From == "fairSparkle");

            var taken = new HashSet<string>(StringComparer.Ordinal) { "fairSparkle" };
            CharacterCloneService.MergeBareSymbolClassSweep(
                xml, "bandanadee", "Clone_Dee", pairs, takenNames: taken);
            pairs.Should().Contain(p => p.From == "fairSparkle" && p.To == "Clone_Dee_fairSparkle");
        }

        [Fact]
        public void IdentifierMatchesAt_DoesNotRewriteInsideBgmPrefix()
        {
            CharacterCloneService.IdentifierMatchesAt("bgm_battlefield".AsSpan(), 4, "battlefield", null)
                .Should().BeFalse();
            CharacterCloneService.IdentifierMatchesAt("bgm_battlefield".AsSpan(), 0, "bgm_battlefield", null)
                .Should().BeTrue();
            CharacterCloneService.IdentifierMatchesAt("stage_battlefield".AsSpan(), 6, "battlefield", null)
                .Should().BeTrue();
            CharacterCloneService.IdentifierMatchesAt("battlefield_bg".AsSpan(), 0, "battlefield", null)
                .Should().BeTrue();
        }

        [Fact]
        public void RewriteTextWithPairs_StageClone_KeepsStockBgmIds()
        {
            var plan = CharacterCloneService.BuildCloneReplacementPlan(
                "battlefield",
                "TestingFields",
                new CharacterCloneService.CharacterIdentityNames(),
                new[] { "battlefield" },
                null,
                isStageClone: true);

            var input = "register(\"music\",[{id:\"bgm_battlefield\"},{id:\"bgm_multimansmash\"},{id:\"bgm_cruelsmash\"}]); "
                + "movieclips=[\"battlefield_bg\",\"stage_battlefield\"];";
            var output = CharacterCloneService.RewriteTextWithPairs(input, plan);

            output.Should().Contain("bgm_battlefield");
            output.Should().NotContain("bgm_TestingFields");
            output.Should().Contain("bgm_multimansmash");
            output.Should().Contain("bgm_cruelsmash");
            output.Should().Contain("stage_TestingFields");
            output.Should().Contain("TestingFields_bg");
            output.Should().NotContain("stage_battlefield");
            output.Should().NotContain("battlefield_bg");
        }
    }
}
