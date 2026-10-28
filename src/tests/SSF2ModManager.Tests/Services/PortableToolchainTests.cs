using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class PortableToolchainTests
    {
        [Fact]
        public void ResolveJavaExecutable_DoesNotReturnBareJavaFromPath()
        {
            // Bundled-only: never the string "java" from where.exe fallback.
            var java = JpexsCliService.ResolveJavaExecutable();
            if (java != null)
            {
                java.Should().EndWith("java.exe", because: "portable JRE path must be an absolute exe");
                File.Exists(java).Should().BeTrue();
                Path.IsPathRooted(java).Should().BeTrue();
            }
        }

        [Fact]
        public void HasPython_MatchesResolvePythonExe()
        {
            PortableToolchain.HasPython().Should().Be(PortableToolchain.ResolvePythonExe() != null);
        }
    }
}
