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
    }
}
