using System.IO;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class SetupServiceTests
    {
        [Fact]
        public void CheckVanilla_NoSwf_IsMissing()
        {
            var dir = Directory.CreateTempSubdirectory("ssf2mm-vanilla-").FullName;
            try
            {
                Assert.Equal(ShellBaseService.VanillaCheck.Missing, ShellBaseService.CheckVanilla(dir));
                Assert.Equal(ShellBaseService.VanillaCheck.Missing, ShellBaseService.CheckVanilla(null));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Theory]
        [InlineData("9afefb865f540eba5c26549f47c6892cdfbb1802ac93aa55723462c469eaacf4", "installer64", "64-bit installer")]
        [InlineData("d37bfe824779757d135333e45e9ceeea9eece1ad055094ca18f7b13fb5fe8bea", "portable64", "64-bit portable")]
        [InlineData("0f13237a19bc6a7f63acb8e7371f3ce8420516292099c279061f4ad84f488fdc", "installer32", "32-bit installer")]
        [InlineData("ebf175c88bc37b46b0c1a34c368eae6d529d0a50f7e8ada85fdefd874f5b17c5", "portable32", "32-bit portable")]
        public void BuildForHash_NamesEachBuild(string sha, string id, string label)
        {
            var build = ShellBaseService.BuildForHash(sha);
            Assert.NotNull(build);
            Assert.Equal(id, build!.Id);
            Assert.Equal(label, build.Label);
        }

        [Fact]
        public void BuildForHash_UnknownIsNull()
        {
            Assert.Null(ShellBaseService.BuildForHash("abc"));
            Assert.Null(ShellBaseService.BuildForHash(null));
        }

        [Fact]
        public void CheckVanilla_OtherSwf_IsWrongVersion()
        {
            var dir = Directory.CreateTempSubdirectory("ssf2mm-vanilla-").FullName;
            try
            {
                File.WriteAllBytes(Path.Combine(dir, "SSF2.swf"), new byte[] { 0x46, 0x57, 0x53, 0x0A });
                Assert.Equal(ShellBaseService.VanillaCheck.WrongVersion, ShellBaseService.CheckVanilla(dir));
            }
            finally { Directory.Delete(dir, true); }
        }

        [Fact]
        public void IsNeeded_WhenNext2FlashOrShellMissing()
        {
            var ready = new SetupService.Status("n2f", "v", ShellBaseService.VanillaCheck.Ok, "shell", "1.0.1", false, null);
            Assert.False(SetupService.IsNeeded(ready));
            Assert.True(SetupService.IsNeeded(ready with { Next2Flash = null }));
            Assert.True(SetupService.IsNeeded(ready with { Shell = null }));
        }

        [Fact]
        public void ShowNext_HidesUntilTheStepButtonFinishes()
        {
            Assert.False(SetupService.ShowNext(0, false));
            Assert.False(SetupService.ShowNext(1, false));
            Assert.False(SetupService.ShowNext(2, false));
            Assert.True(SetupService.ShowNext(0, true));
            Assert.True(SetupService.ShowNext(3, false));
            Assert.False(SetupService.ShowNext(4, true));
        }
    }
}
