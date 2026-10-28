using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ShellDirectLaunchServiceTests
    {
        [Fact]
        public void WriteCommand_CreatesLaunchJson()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-launch-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                ShellDirectLaunchService.WriteCommand(root, "css");
                var path = ShellDirectLaunchService.GetLaunchFilePath(root);
                File.Exists(path).Should().BeTrue();
                File.ReadAllText(path).Should().Contain("\"screen\": \"css\"");
                ShellDirectLaunchService.ClearCommand(root);
                File.Exists(path).Should().BeFalse();
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
