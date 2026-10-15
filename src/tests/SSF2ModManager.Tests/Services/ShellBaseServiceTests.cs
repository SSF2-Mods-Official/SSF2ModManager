using System;
using System.IO;
using System.Linq;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ShellBaseServiceTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "shellbase-test-" + Guid.NewGuid().ToString("N"));

        public ShellBaseServiceTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        [Fact]
        public void InstalledVersion_ReadsBuildInfo()
        {
            Assert.Null(ShellBaseService.InstalledVersion(_dir));
            File.WriteAllText(Path.Combine(_dir, ShellBaseService.BuildInfoFile),
                "{\"version\": \"1.0.0\", \"assets\": \"ba201e486e9c\"}");
            Assert.Equal("1.0.0", ShellBaseService.InstalledVersion(_dir));
        }

        [Fact]
        public void DevShell_IsNeverRebuilt()
        {
            Assert.False(ShellBaseService.IsDevShell(_dir));
            Directory.CreateDirectory(Path.Combine(_dir, "expansions", "decompiled"));
            Assert.True(ShellBaseService.IsDevShell(_dir));
        }

        [Fact]
        public void VanillaFolder_NeedsSsf2Swf()
        {
            Assert.False(ShellBaseService.IsVanillaFolder(_dir));
            File.WriteAllBytes(Path.Combine(_dir, "SSF2.swf"), new byte[] { 0x46 });
            Assert.True(ShellBaseService.IsVanillaFolder(_dir));
        }

        [Fact]
        public void PruneCache_KeepsOnlyTheBuiltRelease()
        {
            foreach (var name in new[] { "shell-base-1.0.0.ssfpatch", "shell-base-1.1.0.ssfpatch",
                         "shell-assets-ba201e486e9c.ssfpatch", "shell-assets-0123456789ab.ssfpatch", "notes.txt" })
                File.WriteAllText(Path.Combine(_dir, name), "x");
            Directory.CreateDirectory(Path.Combine(_dir, "out"));

            var removed = ShellBaseService.PruneCache(_dir, "shell-base-1.1.0.ssfpatch", "shell-assets-ba201e486e9c.ssfpatch");

            Assert.Equal(2, removed);
            Assert.Equal(new[] { "notes.txt", "shell-assets-ba201e486e9c.ssfpatch", "shell-base-1.1.0.ssfpatch" },
                Array.ConvertAll(Directory.GetFiles(_dir), f => Path.GetFileName(f)!).OrderBy(f => f, StringComparer.Ordinal).ToArray());
            Assert.True(Directory.Exists(Path.Combine(_dir, "out")));
        }
    }
}
