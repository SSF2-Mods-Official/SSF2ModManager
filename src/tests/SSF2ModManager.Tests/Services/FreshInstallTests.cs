using System;
using System.IO;
using System.IO.Compression;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class FreshInstallTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "ssf2mm-fresh-" + Guid.NewGuid().ToString("N"));

        public FreshInstallTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void FindApi_ReadsCustomMatchFromEventExport()
        {
            var scripts = Path.Combine(_dir, "scripts");
            Directory.CreateDirectory(scripts);
            File.WriteAllText(Path.Combine(scripts, "SSF2CustomMatch.as"), "package { public class SSF2CustomMatch {} }");
            Assert.Equal(scripts, EventAuthorService.FindApi(_dir));
            Assert.Null(EventAuthorService.FindApi(Path.Combine(_dir, "missing")));
        }

        [Fact]
        public void ShellRootFromPack_FindsEventMode()
        {
            var shell = Path.Combine(_dir, "SSF2 Shell");
            var pack = Path.Combine(shell, "expansions", "events", "Pink");
            Directory.CreateDirectory(Path.Combine(shell, "data", "modes"));
            Directory.CreateDirectory(pack);
            File.WriteAllBytes(Path.Combine(shell, "data", "modes", "event_mode.swf"), new byte[] { 1 });
            Assert.Equal(shell, EventAuthorService.ShellRootFromPack(pack));
        }

        [Fact]
        public void FilesFrom_ReadsMismatchLine()
        {
            Assert.Equal("SSF2.swf, data/DAT51.ssf",
                VanillaHashPrompt.FilesFrom("SSF2 Shell build failed: vanilla hash mismatch: SSF2.swf, data/DAT51.ssf"));
            Assert.Null(VanillaHashPrompt.FilesFrom("python missing"));
        }

        [Fact]
        public void VanillaCautionPaths_SkipsShellAndBooter()
        {
            var report = new BuildTrust.Report();
            report.HashCaution.Add("SSF2.swf");
            report.HashCaution.Add("data/character/mario.swf");
            report.HashCaution.Add("data/ssf2mm/mod.swf");
            Assert.Equal(new[] { "data/character/mario.swf" }, BuildTrust.VanillaCautionPaths(report));
        }

        [Fact]
        public void ExtractChanged_SkipsMatchingSize()
        {
            var zip = Path.Combine(_dir, "mgr.zip");
            var install = Path.Combine(_dir, "install");
            Directory.CreateDirectory(install);
            WriteZip(zip, ("SSF2ModManager.exe", new byte[8]), ("readme.txt", new byte[3]));
            var first = LauncherUpdateService.ExtractChanged(zip, install);
            Assert.Equal(2, first.Copied);
            Assert.Equal(0, first.Skipped);
            File.WriteAllBytes(Path.Combine(install, "readme.txt"), new byte[1]);
            var second = LauncherUpdateService.ExtractChanged(zip, install);
            Assert.Equal(1, second.Copied);
            Assert.Equal(1, second.Skipped);
            Assert.Equal(0, second.Staged);
            Assert.Equal(3, new FileInfo(Path.Combine(install, "readme.txt")).Length);
        }

        [Fact]
        public void ApplyStaged_RenamesTheRunningExe()
        {
            var root = Path.Combine(_dir, "stage");
            Directory.CreateDirectory(root);
            var exe = Path.Combine(root, "SSF2ModManager.exe");
            File.WriteAllBytes(exe, new byte[] { 1 });
            File.WriteAllBytes(exe + ".new", new byte[] { 2, 2 });
            var n = LauncherUpdateService.ApplyStaged(root, exe);
            Assert.Equal(1, n);
            Assert.Equal(new byte[] { 2, 2 }, File.ReadAllBytes(exe));
            Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(exe + ".old"));
            Assert.False(File.Exists(exe + ".new"));
        }

        [Fact]
        public void CopyChanged_SkipsMatchingSizeAndWritesTag()
        {
            var src = Path.Combine(_dir, "src");
            var dest = Path.Combine(_dir, "dest");
            Directory.CreateDirectory(Path.Combine(src, "runtime"));
            File.WriteAllBytes(Path.Combine(src, "runtime", "n2f.py"), new byte[4]);
            var first = Next2FlashSetup.CopyChanged(src, dest);
            Assert.Equal(1, first.Copied);
            Next2FlashSetup.WriteReleaseTag(dest, "v0.2.0");
            Assert.Equal("v0.2.0", Next2FlashSetup.ReadReleaseTag(dest));
            var second = Next2FlashSetup.CopyChanged(src, dest);
            Assert.Equal(0, second.Copied);
            Assert.Equal(1, second.Skipped);
        }

        private static void WriteZip(string path, params (string Name, byte[] Bytes)[] files)
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            foreach (var file in files)
            {
                var entry = zip.CreateEntry(file.Name);
                using var stream = entry.Open();
                stream.Write(file.Bytes, 0, file.Bytes.Length);
            }
        }
    }
}
