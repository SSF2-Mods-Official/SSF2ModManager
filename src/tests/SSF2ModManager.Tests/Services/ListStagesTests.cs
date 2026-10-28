using System.IO;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ListStagesTests
    {
        [Fact]
        public void ListStages_IncludesSwfAndSsfIds()
        {
            var root = Path.Combine(Path.GetTempPath(), "ssf2mm-liststages-" + Path.GetRandomFileName());
            var stage = Path.Combine(root, "data", "stage");
            Directory.CreateDirectory(stage);
            try
            {
                File.WriteAllText(Path.Combine(stage, "battlefield.swf"), "x");
                File.WriteAllText(Path.Combine(stage, "finaldestination.ssf"), "x");
                File.WriteAllText(Path.Combine(stage, "readme.txt"), "x");

                var ids = ShellConfigureLaunchService.ListStages(root);

                ids.Should().BeEquivalentTo(new[] { "battlefield", "finaldestination" });
            }
            finally
            {
                try { Directory.Delete(root, true); } catch { /* ignore */ }
            }
        }
    }
}
