using System.IO;
using System.IO.Compression;
using System.Text;
using FluentAssertions;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class SsfeReviewTests
    {
        [Fact]
        public void CleanFolder_Passes()
        {
            var folder = Temp();
            try
            {
                WriteMod(folder, "FWS" + new string('A', 32));
                var review = SsfeReview.Inspect(folder);
                review.Ok.Should().BeTrue(string.Join("; ", review.Errors));
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void PackageSsfe_SealsTwoModsUnderOneName()
        {
            var root = Temp();
            var left = Path.Combine(root, "left");
            var right = Path.Combine(root, "right");
            var ssfe = Path.Combine(root, "pack.ssfe");
            var previous = SsfeSeal.KeyFileOverride;
            try
            {
                Directory.CreateDirectory(left);
                Directory.CreateDirectory(right);
                WriteSwf(left, EmptySwf());
                File.WriteAllText(Path.Combine(right, "info.json"),
                    "{\"name\":\"Right\",\"creator\":\"Tester\",\"type\":\"Event\"}");
                File.WriteAllBytes(Path.Combine(right, "mod.swf"), EmptySwf());
                SsfeSeal.KeyFileOverride = Path.Combine(root, "author.xml");
                ModPackageService.PackageSsfe(
                    new[]
                    {
                        new PackageModEntry { FolderPath = left, Name = "Left", Creator = "Tester", Type = "Mod" },
                        new PackageModEntry { FolderPath = right, Name = "Right", Creator = "Tester", Type = "Event" }
                    },
                    "Friday Pack",
                    "Two mods.",
                    ssfe);
                SsfeSeal.Accept(ssfe).Should().BeTrue();
                using var zip = ZipFile.OpenRead(ssfe);
                zip.GetEntry("info.json").Should().NotBeNull();
                zip.GetEntry("ssf2mm-pack.json").Should().NotBeNull();
                zip.Entries.Should().Contain(e => e.FullName.EndsWith("/mod.swf", StringComparison.OrdinalIgnoreCase));
                using var reader = new StreamReader(zip.GetEntry("info.json")!.Open());
                reader.ReadToEnd().Should().Contain("Friday Pack");
            }
            finally
            {
                SsfeSeal.KeyFileOverride = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public async Task PackageSsfe_OneClickInstallsEachMod()
        {
            var root = Temp();
            var shell = Path.Combine(root, "shell");
            var ev = Path.Combine(root, "BobOmb");
            var stage = Path.Combine(root, "PeachCastle");
            var ssfe = Path.Combine(root, "pack.ssfe");
            var previous = SsfeSeal.KeyFileOverride;
            OneClickReceipt? receipt = null;
            try
            {
                Directory.CreateDirectory(shell);
                Directory.CreateDirectory(ev);
                Directory.CreateDirectory(stage);
                File.WriteAllText(Path.Combine(ev, "info.json"),
                    "{\"name\":\"Bob-omb Destination\",\"creator\":\"Tester\",\"type\":\"Event\"}");
                File.WriteAllBytes(Path.Combine(ev, "mod.swf"), EmptySwf());
                File.WriteAllText(Path.Combine(stage, "info.json"),
                    "{\"name\":\"Peach's Castle\",\"creator\":\"Tester\",\"type\":\"Stage\"}");
                File.WriteAllBytes(Path.Combine(stage, "stage.swf"), EmptySwf());
                SsfeSeal.KeyFileOverride = Path.Combine(root, "author.xml");
                ModPackageService.PackageSsfe(
                    new[]
                    {
                        new PackageModEntry { FolderPath = ev, Name = "Bob-omb Destination", Creator = "Tester", Type = "Event" },
                        new PackageModEntry { FolderPath = stage, Name = "Peach's Castle", Creator = "Tester", Type = "Stage" }
                    },
                    "Friday Pack",
                    "An event and a stage.",
                    ssfe);
                SsfeSeal.Accept(ssfe).Should().BeTrue();
                receipt = OneClickReceive.ClassifyArchive(ssfe);
                receipt.IsPack.Should().BeTrue();
                await OneClickReceive.InstallAsync(shell, receipt, receipt.Items);
                var eventInfo = Path.Combine(shell, "expansions", "events", "BobOmb", "info.json");
                var stageSwf = Path.Combine(shell, "expansions", "stages", "PeachCastle", "stage.swf");
                File.Exists(eventInfo).Should().BeTrue();
                File.ReadAllText(eventInfo).Should().Contain("Bob-omb Destination");
                File.Exists(stageSwf).Should().BeTrue();
            }
            finally
            {
                receipt?.Cleanup();
                SsfeSeal.KeyFileOverride = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        [Fact]
        public void UrlLoader_Refused()
        {
            var folder = Temp();
            try
            {
                WriteMod(folder, "FWS....URLLoader....");
                var review = SsfeReview.Inspect(folder);
                review.Ok.Should().BeFalse();
                string.Join(" ", review.Errors).Should().Contain("URLLoader");
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Exe_Refused()
        {
            var folder = Temp();
            try
            {
                WriteMod(folder, "FWS");
                File.WriteAllText(Path.Combine(folder, "setup.exe"), "nope");
                SsfeReview.Inspect(folder).Ok.Should().BeFalse();
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void NestedZip_Refused()
        {
            var folder = Temp();
            try
            {
                WriteMod(folder, "FWS");
                File.WriteAllBytes(Path.Combine(folder, "inner.zip"), new byte[] { 1, 2, 3 });
                string.Join(" ", SsfeReview.Inspect(folder).Errors).Should().Contain("Nested archive");
            }
            finally { Directory.Delete(folder, true); }
        }

        [Fact]
        public void Seal_RoundTrip_AndRejectsAPlainZip()
        {
            var folder = Temp();
            var ssfe = Path.Combine(Path.GetTempPath(), "ssfe-" + Guid.NewGuid().ToString("N") + ".ssfe");
            var zip = Path.Combine(Path.GetTempPath(), "ssfe-" + Guid.NewGuid().ToString("N") + ".zip");
            try
            {
                WriteSwf(folder, EmptySwf());
                SsfeSeal.Package(folder, ssfe);
                SsfeSeal.Accept(ssfe).Should().BeTrue();
                using (var archive = ZipFile.Open(zip, ZipArchiveMode.Create))
                {
                    var entry = archive.CreateEntry("info.json");
                    using var writer = new StreamWriter(entry.Open());
                    writer.Write("{}");
                }
                SsfeSeal.Accept(zip).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(folder, true); } catch { }
                try { File.Delete(ssfe); } catch { }
                try { File.Delete(zip); } catch { }
            }
        }

        [Fact]
        public void SemiSupported_IsAZipWithoutSsfe()
        {
            var zip = new GameBananaMod
            {
                Name = "Stage",
                Files = new() { new GameBananaFile { FileName = "clocktown.zip" } }
            };
            var ssfe = new GameBananaMod
            {
                Name = "Stage",
                Files = new() { new GameBananaFile { FileName = "clocktown.ssfe" } }
            };
            BrowseModFilter.IsSemiSupported(zip).Should().BeTrue();
            BrowseModFilter.IsSemiSupported(ssfe).Should().BeFalse();
            BrowseModFilter.IsUnsupported(zip).Should().BeFalse();
        }

        [Fact]
        public void OtherUser_CannotRepackage()
        {
            var folder = Temp();
            var ssfe = Path.Combine(Path.GetTempPath(), "ssfe-" + Guid.NewGuid().ToString("N") + ".ssfe");
            var previous = SsfeSeal.KeyFileOverride;
            try
            {
                SsfeSeal.KeyFileOverride = Path.Combine(folder, "author-a.xml");
                WriteSwf(folder, EmptySwf());
                SsfeSeal.Package(folder, ssfe);
                SsfeSeal.Accept(ssfe).Should().BeTrue();
                var infoPath = Path.Combine(folder, "info.json");
                var info = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(infoPath));
                info.Remove("ssfeAuthor");
                File.WriteAllText(infoPath, info.ToString());
                SsfeSeal.KeyFileOverride = Path.Combine(folder, "author-b.xml");
                var again = () => SsfeSeal.Package(folder, ssfe + ".b");
                again.Should().Throw<InvalidOperationException>().WithMessage("*different author*");
            }
            finally
            {
                SsfeSeal.KeyFileOverride = previous;
                try { Directory.Delete(folder, true); } catch { }
                try { File.Delete(ssfe); } catch { }
            }
        }

        [Fact]
        public void TwoSwfs_DoNotShareTheSameMark()
        {
            var root = Temp();
            var previous = SsfeSeal.KeyFileOverride;
            try
            {
                SsfeSeal.KeyFileOverride = Path.Combine(root, "author.xml");
                var left = Path.Combine(root, "a");
                var right = Path.Combine(root, "b");
                Directory.CreateDirectory(left);
                Directory.CreateDirectory(right);
                var other = EmptySwf();
                other[11] = 2;
                WriteSwf(left, EmptySwf());
                WriteSwf(right, other);
                SsfeSeal.Package(left, Path.Combine(root, "a.ssfe"));
                SsfeSeal.Package(right, Path.Combine(root, "b.ssfe"));
                var codesA = HighCodes(File.ReadAllBytes(Path.Combine(left, "mod.swf")));
                var codesB = HighCodes(File.ReadAllBytes(Path.Combine(right, "mod.swf")));
                codesA.Should().NotBeEmpty();
                codesA.Should().NotEqual(codesB);
            }
            finally
            {
                SsfeSeal.KeyFileOverride = previous;
                try { Directory.Delete(root, true); } catch { }
            }
        }

        private static List<int> HighCodes(byte[] swf)
        {
            var codes = new List<int>();
            var i = 13;
            while (i + 2 <= swf.Length)
            {
                var header = swf[i] | (swf[i + 1] << 8);
                var code = header >> 6;
                var len = header & 63;
                var size = 2;
                if (len == 63)
                {
                    if (i + 6 > swf.Length)
                        break;
                    len = swf[i + 2] | (swf[i + 3] << 8) | (swf[i + 4] << 16) | (swf[i + 5] << 24);
                    size = 6;
                }
                if (code == 0 && len == 0)
                    break;
                if (code >= 200)
                    codes.Add(code);
                i += size + len;
            }
            codes.Sort();
            return codes;
        }

        private static byte[] EmptySwf()
        {
            var swf = new byte[15];
            swf[0] = (byte)'F';
            swf[1] = (byte)'W';
            swf[2] = (byte)'S';
            swf[3] = 9;
            swf[4] = 15;
            swf[10] = 0x18;
            swf[11] = 1;
            return swf;
        }

        private static void WriteSwf(string folder, byte[] swf)
        {
            File.WriteAllText(Path.Combine(folder, "info.json"),
                "{\"name\":\"Test\",\"creator\":\"Tester\",\"type\":\"Mod\"}");
            File.WriteAllBytes(Path.Combine(folder, "mod.swf"), swf);
        }
        private static string Temp()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ssfe-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            return folder;
        }

        private static void WriteMod(string folder, string swfBody)
        {
            File.WriteAllText(Path.Combine(folder, "info.json"),
                "{\"name\":\"Test\",\"creator\":\"Tester\",\"type\":\"Mod\"}");
            File.WriteAllBytes(Path.Combine(folder, "mod.swf"), Encoding.ASCII.GetBytes(swfBody));
        }
    }
}
