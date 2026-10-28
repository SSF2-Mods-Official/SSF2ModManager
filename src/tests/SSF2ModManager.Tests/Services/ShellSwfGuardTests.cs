using System;
using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ShellSwfGuardTests
    {
        [Fact]
        public void KnownBooter_Passes()
        {
            ShellSwfGuard.Compare(ShellSwfGuard.BooterSha256).Should().BeNull();
        }

        [Fact]
        public void OtherHash_IsModified()
        {
            ShellSwfGuard.Compare("d82d709487162af51fcabc2d123a3639e5aa399d70d841f34e5104f6256a4063")
                .Should().Be("modified");
        }

        [Fact]
        public void MissingFile_DoesNotStart()
        {
            var folder = Path.Combine(Path.GetTempPath(), "ssf2-booter-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var act = () => ShellSwfGuard.Ensure(folder);
                act.Should().Throw<InvalidOperationException>();
            }
            finally
            {
                Directory.Delete(folder, true);
            }
        }
    }
}
