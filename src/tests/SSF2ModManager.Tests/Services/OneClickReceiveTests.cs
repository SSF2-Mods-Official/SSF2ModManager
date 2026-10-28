using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class OneClickReceiveTests
    {
        [Fact]
        public void OneClickLaunch_IsNotAnInstall()
        {
            ProtocolService.IsOneClickLaunch(
                "ssf2mm://https//gamebanana.com/mmdl/1768124,Mod,698679").Should().BeTrue();
            OneClickReceive.DirectDownloadUrl("https://gamebanana.com/mmdl/1768124")
                .Should().Be("https://gamebanana.com/dl/1768124");
            OneClickReceive.FormatDownload(4_500_000, 9_000_000)
                .Should().Be("Downloading… 50% (4.3 MB / 8.6 MB)");
        }

        [Fact]
        public void DocumentClass_Main_And_MainTimeline()
        {
            SwfPackage.DocumentClass(Swf("Main", "jumpSpeed")).Should().Be("Main");
            SwfPackage.DocumentClass(Swf("MainTimeline", "stage_finaldestination")).Should().Be("MainTimeline");
            SwfPackage.DocumentClass(Swf("triplebaka_fla.MainTimeline", "stage_triplebaka"))
                .Should().Be("triplebaka_fla.MainTimeline");
            SwfPackage.IsBetaDocument("Main").Should().BeTrue();
            SwfPackage.IsTimelineDocument("smashville_fla.MainTimeline").Should().BeTrue();
        }

        [Fact]
        public void DocumentClass_Reads_A_Wrapped_Ssf()
        {
            var wrapped = DataFolderCompression.Wrap(Swf("Main", "backgrounds"));
            SwfPackage.DocumentClass(wrapped).Should().Be("Main");
        }

        [Fact]
        public void Classify_Prefers_Own_Slot_And_Leaves_Over_Unchecked()
        {
            var dir = Temp();
            try
            {
                Write(dir, Path.Combine("Own Slot", "triplebaka.ssf"), Swf("Main", "stage_triplebaka"));
                Write(dir, Path.Combine("Over Smashville", "DAT63.ssf"), Swf("Main", "stage_smashville"));
                var receipt = OneClickReceive.ClassifyDirectory(dir);
                receipt.Items.Should().HaveCount(2);
                receipt.Items.Single(i => i.Id == "triplebaka").Selected.Should().BeTrue();
                receipt.Items.Single(i => i.Id == "smashville").Selected.Should().BeFalse();
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Classify_Lists_Sandbox_And_Leaves_Out_9b()
        {
            var dir = Temp();
            try
            {
                Write(dir, Path.Combine("Sandbox", "throne.ssf"), Swf("Main", "stage_finaldestination"));
                Write(dir, Path.Combine("V.9B", "DAT25.ssf"), Swf("MainTimeline", "stage_finaldestination"));
                var receipt = OneClickReceive.ClassifyDirectory(dir);
                receipt.LeftOutNineB.Should().Be(1);
                receipt.Items.Should().ContainSingle();
                receipt.Items[0].Kind.Should().Be(OneClickKind.Stage);
                receipt.Items[0].RelativePath.Should().Contain("Sandbox");
                receipt.Items[0].Selected.Should().BeTrue();
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Classify_Prefers_Sandbox_Character_Over_Another_Beta_Copy()
        {
            var dir = Temp();
            try
            {
                Write(dir, Path.Combine("SSF2 BETA", "DAT39.ssf"), Swf("Main", "jumpSpeed"));
                Write(dir, Path.Combine("SSF2 SANDBOX", "data", "character", "Chell.ssf"), Swf("Main", "statsName"));
                var receipt = OneClickReceive.ClassifyDirectory(dir);
                receipt.LeftOutOtherVersions.Should().Be(1);
                receipt.Items.Should().ContainSingle();
                receipt.Items[0].Kind.Should().Be(OneClickKind.Character);
                receipt.Items[0].RelativePath.Should().Contain("SANDBOX");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Classify_Stage_Keeps_Its_Songs()
        {
            var dir = Temp();
            try
            {
                Write(dir, Path.Combine("stage", "DAT372.ssf"), Swf("Main", "backgrounds\0stage_smashville\0bgm_djkk\0bgm_kkcruisin"));
                Write(dir, Path.Combine("sound", "bgm_djkk.ssf"), Swf("Main", "bgm_djkk"));
                Write(dir, Path.Combine("sound", "bgm_kkcruisin.ssf"), Swf("Main", "bgm_kkcruisin"));
                var receipt = OneClickReceive.ClassifyDirectory(dir);
                receipt.Items.Should().ContainSingle(i => i.Kind == OneClickKind.Stage && i.Id == "smashville");
                receipt.Items.Where(i => i.Kind == OneClickKind.Song).Select(i => i.Id)
                    .Should().BeEquivalentTo(new[] { "bgm_djkk", "bgm_kkcruisin" });
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Classify_Pack_Is_A_2_0_Archive()
        {
            var dir = Temp();
            var zip = Path.Combine(dir, "pack.zip");
            try
            {
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("ssf2mm-pack.json");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write("{\"format\":\"ssf2mm-pack\",\"mods\":[]}");
                }
                var receipt = OneClickReceive.ClassifyArchive(zip);
                try
                {
                    receipt.IsPack.Should().BeTrue();
                    receipt.Items.Should().ContainSingle(i => i.Kind == OneClickKind.Pack);
                }
                finally
                {
                    receipt.Cleanup();
                }
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void PlanSongIds_Uses_The_Next_Free_Id_When_The_Stock_Id_Is_Taken()
        {
            var map = OneClickReceive.PlanSongIds(
                new[] { "bgm_joggingcountdown", "bgm_minorcircuit", "bgm_djkk" },
                new[] { "bgm_joggingcountdown" });
            map.Should().ContainKey("bgm_joggingcountdown");
            map["bgm_joggingcountdown"].Should().Be("bgm_joggingcountdown2");
            map.ContainsKey("bgm_minorcircuit").Should().BeFalse();
            map.ContainsKey("bgm_djkk").Should().BeFalse();
        }

        [Fact]
        public void Classify_Real_GameBanana_Archives_When_Cached()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick");
            var triple = Path.Combine(root, "triple_baka.rar");
            if (!File.Exists(triple))
                return;

            AssertArchive(Path.Combine(root, "triple_baka.rar"), receipt =>
            {
                receipt.LeftOutNineB.Should().Be(2);
                receipt.Items.Should().BeEmpty();
            });
            AssertArchive(Path.Combine(root, "bowletta.rar"), receipt =>
            {
                receipt.LeftOutNineB.Should().BeGreaterThan(0);
                receipt.Items.Should().BeEmpty();
            });
            AssertArchive(Path.Combine(root, "alrest.rar"), receipt =>
            {
                receipt.Items.Should().Contain(i => i.Kind == OneClickKind.Stage);
                receipt.Items.Where(i => i.Kind == OneClickKind.Song).Select(i => i.Id)
                    .Should().BeEquivalentTo(new[] { "bgm_djkk", "bgm_kkcruisin" });
            });
            AssertArchive(Path.Combine(root, "megaman.rar"), receipt =>
            {
                receipt.Items.Should().ContainSingle(i => i.Kind == OneClickKind.Character);
            });
            AssertArchive(Path.Combine(root, "chell.rar"), receipt =>
            {
                receipt.Items.Should().ContainSingle(i => i.Kind == OneClickKind.Character);
                receipt.Items[0].RelativePath.ToLowerInvariant().Should().Contain("sandbox");
            });
            AssertArchive(Path.Combine(root, "nick_vhs.rar"), receipt =>
            {
                receipt.Items.Should().Contain(i => i.Kind == OneClickKind.Stage && i.Id == "battlefield2");
                receipt.Items.Count(i => i.Kind == OneClickKind.Song).Should().Be(7);
                receipt.Items.Should().NotContain(i => i.RelativePath.Contains("mappings", StringComparison.OrdinalIgnoreCase));
            });
            var shadow = Path.Combine(root, "shadow.zip");
            if (!File.Exists(shadow))
                return;
            AssertArchive(shadow, receipt =>
            {
                receipt.Items.Should().Contain(i => i.Kind == OneClickKind.Character);
                receipt.Items.Should().OnlyContain(i => i.RelativePath.Contains("Sandbox", StringComparison.OrdinalIgnoreCase));
                receipt.LeftOutOtherVersions.Should().BeGreaterThan(0);
            });
        }

        private static void AssertArchive(string path, Action<OneClickReceipt> assert)
        {
            var receipt = OneClickReceive.ClassifyArchive(path);
            try
            {
                assert(receipt);
            }
            finally
            {
                receipt.Cleanup();
            }
        }

        private static string Temp()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-oneclick-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            return dir;
        }

        private static void Write(string root, string relative, byte[] bytes)
        {
            var path = Path.Combine(root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, bytes);
        }

        [Fact]
        public void Classify_LeavesOut_MainTimeline()
        {
            var dir = Temp();
            try
            {
                Write(dir, "Olympus Coliseum/DAT50.ssf", Swf("chaosshrine_fla.MainTimeline", "stage_chaosshrine"));
                Write(dir, "beta/stage.ssf", Swf("Main", "stage_battlefield"));
                var receipt = OneClickReceive.ClassifyDirectory(dir);
                receipt.LeftOutNineB.Should().Be(1);
                receipt.Items.Should().ContainSingle(i => i.Kind == OneClickKind.Stage && i.Id == "battlefield");
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ReadSongGuid_ReadsPoolStringAfterGuidKey()
        {
            var value = "tboz-rugrats-my-getaway";
            var bytes = new byte[6 + value.Length];
            bytes[0] = 4;
            bytes[1] = (byte)'g';
            bytes[2] = (byte)'u';
            bytes[3] = (byte)'i';
            bytes[4] = (byte)'d';
            bytes[5] = (byte)value.Length;
            Encoding.ASCII.GetBytes(value).CopyTo(bytes, 6);
            OneClickReceive.ReadSongGuid(bytes).Should().Be(value);
        }

        private static byte[] Swf(string documentClass, string extra, byte version = 15)
        {
            var name = Encoding.UTF8.GetBytes(documentClass);
            var extraBytes = Encoding.ASCII.GetBytes(extra);
            var bodyLen = 2 + 2 + name.Length + 1;
            if (bodyLen >= 63)
                throw new InvalidOperationException("Test document class is too long for a short tag.");
            var len = 8 + 1 + 2 + 2 + 2 + bodyLen + 2 + extraBytes.Length;
            var swf = new byte[len];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = version;
            swf[4] = (byte)len;
            swf[5] = (byte)(len >> 8);
            swf[6] = (byte)(len >> 16);
            swf[7] = (byte)(len >> 24);
            var p = 13;
            var code = (76 << 6) | bodyLen;
            swf[p++] = (byte)code;
            swf[p++] = (byte)(code >> 8);
            swf[p++] = 1;
            swf[p++] = 0;
            swf[p++] = 0;
            swf[p++] = 0;
            Buffer.BlockCopy(name, 0, swf, p, name.Length);
            p += name.Length;
            swf[p++] = 0;
            p += 2;
            Buffer.BlockCopy(extraBytes, 0, swf, p, extraBytes.Length);
            return swf;
        }
    }
}
