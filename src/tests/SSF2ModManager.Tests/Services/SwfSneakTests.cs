using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class SwfSneakTests
    {
        [Fact]
        public void PlainUrlLoader_IsRefused()
        {
            SwfApiScan.RefusedIn(Fws("....URLLoader....")).Should().Contain("URLLoader");
        }

        [Fact]
        public void SplitUrlLoader_IsRefused()
        {
            var body = Encoding.ASCII.GetBytes("FWS....URL\0\0Loader....");
            SwfApiScan.RefusedIn(body).Should().Contain("URLLoader");
        }

        [Fact]
        public void NestedSwf_IsRefused()
        {
            var inner = Cws("URLLoader");
            var outer = new byte[8 + inner.Length];
            Encoding.ASCII.GetBytes("FWS").CopyTo(outer, 0);
            inner.CopyTo(outer, 8);
            SwfApiScan.RefusedIn(outer).Should().Contain("URLLoader");
        }

        [Fact]
        public void Garbage_IsUnreadable()
        {
            SwfApiScan.RefusedIn(Encoding.ASCII.GetBytes("not-a-swf")).Should().Contain("unreadable");
        }

        [Fact]
        public void FileStream_IsRefused()
        {
            SwfApiScan.RefusedIn(Fws("new FileStream()")).Should().Contain("FileStream");
        }

        [Fact]
        public void GetDefinitionByName_IsRefused()
        {
            SwfApiScan.RefusedIn(Fws("getDefinitionByName(\"flash.net.URLLoader\")")).Should().Contain("getDefinitionByName");
        }

        [Fact]
        public void LoadBytes_IsRefused()
        {
            SwfApiScan.RefusedIn(Fws("loader.loadBytes(hidden)")).Should().Contain("loadBytes");
        }

        [Fact]
        public void FromCharCode_IsRefused()
        {
            SwfApiScan.RefusedIn(Fws("String.fromCharCode(70,105,108,101)")).Should().Contain("fromCharCode");
        }

        [Fact]
        public void EngineName_Passes()
        {
            SwfApiScan.RefusedIn(Fws("com.mcleodgaming.ssf2.Main")).Should().BeEmpty();
        }

        [Fact]
        public void OutsidePath_IsRefused()
        {
            ModFolder.Inside("../secrets.txt").Should().BeFalse();
            ModFolder.Inside("C:/Users/me/Desktop/note.txt").Should().BeFalse();
            ModFolder.Inside("/etc/passwd").Should().BeFalse();
            ModFolder.Inside("notes/readme.txt").Should().BeTrue();
        }

        [Fact]
        public void FailedScan_IsNotRemembered()
        {
            var root = Path.Combine(Path.GetTempPath(), "sneak-" + Guid.NewGuid().ToString("N"));
            var trust = Path.Combine(root, "trust.json");
            Directory.CreateDirectory(Path.Combine(root, "expansions", "mods", "bad"));
            File.WriteAllBytes(Path.Combine(root, "expansions", "mods", "bad", "mod.swf"), Fws("FileStream"));
            File.WriteAllBytes(Path.Combine(root, "SSF2.swf"), Fws("booter"));
            var previous = BuildTrustStore.FilePath;
            try
            {
                BuildTrustStore.FilePath = trust;
                var report = BuildTrust.Check(root);
                report.DoNotRun.Should().NotBeEmpty();
                BuildTrustStore.TryGet(root, "expansions/mods/bad/mod.swf", out _).Should().BeFalse();
            }
            finally
            {
                BuildTrustStore.FilePath = previous;
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void PassedScan_IsRemembered()
        {
            var root = Path.Combine(Path.GetTempPath(), "sneak-" + Guid.NewGuid().ToString("N"));
            var trust = Path.Combine(root, "trust.json");
            var mod = Path.Combine(root, "expansions", "mods", "ok", "mod.swf");
            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
            File.WriteAllBytes(mod, Fws("com.mcleodgaming.ssf2.Main"));
            File.WriteAllBytes(Path.Combine(root, "SSF2.swf"), Fws("booter"));
            var previous = BuildTrustStore.FilePath;
            try
            {
                BuildTrustStore.FilePath = trust;
                var report = BuildTrust.Check(root);
                report.DoNotRun.Should().BeEmpty();
                BuildTrustStore.TryGet(root, "expansions/mods/ok/mod.swf", out var entry).Should().BeTrue();
                entry.Sha256.Should().Be(BuildTrustStore.Sha256(mod));
                entry.Rules.Should().Be(SwfApiScan.RulesId);
            }
            finally
            {
                BuildTrustStore.FilePath = previous;
                Directory.Delete(root, true);
            }
        }

        [Fact]
        public void StaleRules_AreScannedAgain()
        {
            var root = Path.Combine(Path.GetTempPath(), "sneak-" + Guid.NewGuid().ToString("N"));
            var trust = Path.Combine(root, "trust.json");
            var mod = Path.Combine(root, "expansions", "mods", "hid", "mod.swf");
            Directory.CreateDirectory(Path.GetDirectoryName(mod)!);
            File.WriteAllBytes(mod, Fws("String.fromCharCode(1)"));
            File.WriteAllBytes(Path.Combine(root, "SSF2.swf"), Fws("booter"));
            var previous = BuildTrustStore.FilePath;
            try
            {
                BuildTrustStore.FilePath = trust;
                BuildTrustStore.Remember(root, "expansions/mods/hid/mod.swf", BuildTrustStore.Sha256(mod), new FileInfo(mod).Length, BuildTrustStore.UnixSeconds(mod), "old-rules");
                var report = BuildTrust.Check(root);
                report.DoNotRun.Should().Contain(item => item.Contains("fromCharCode"));
                BuildTrustStore.TryGet(root, "expansions/mods/hid/mod.swf", out var entry).Should().BeTrue();
                entry.Rules.Should().Be("old-rules");
            }
            finally
            {
                BuildTrustStore.FilePath = previous;
                Directory.Delete(root, true);
            }
        }

        private static byte[] Fws(string body)
        {
            var text = "FWS" + body;
            return Encoding.ASCII.GetBytes(text);
        }

        private static byte[] Cws(string body)
        {
            var raw = Encoding.ASCII.GetBytes(body);
            using var payload = new MemoryStream();
            using (var zlib = new ZLibStream(payload, CompressionLevel.SmallestSize, true))
                zlib.Write(raw, 0, raw.Length);
            var compressed = payload.ToArray();
            var result = new byte[8 + compressed.Length];
            Encoding.ASCII.GetBytes("CWS").CopyTo(result, 0);
            compressed.CopyTo(result, 8);
            return result;
        }
    }
}
