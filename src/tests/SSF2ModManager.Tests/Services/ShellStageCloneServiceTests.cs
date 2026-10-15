using System.IO;
using System.Text;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ShellStageCloneServiceTests
    {
        [Fact]
        public void ReadStageImportIdentity_UsesIdAndSkipsMusic()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0id\0bgm_finaldestination\0id\0LastDestination\0stage_LastDestination\0LastDestination_bg\0");
            var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
            linkage.Should().Be("LastDestination");
            display.Should().Be("LastDestination");
        }

        [Fact]
        public void ReadStageImportIdentity_UsesDisplayName()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0displayName\0Last Destination\0id\0LastDestination\0");
            var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
            linkage.Should().Be("LastDestination");
            display.Should().Be("Last Destination");
        }

        [Fact]
        public void ReadStageImportIdentity_UsesStageClipOverHudNoise()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0stage_effects\0id\0bG\0displayName\0toUpperCase\0stage_boxingring\0backgrounds\0");
            var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
            linkage.Should().Be("boxingring");
            display.Should().Be("boxingring");
        }

        [Fact]
        public void ReadStageImportIdentity_UsesDisplayNameInsideTheStageRecord()
        {
            var bytes = Encoding.ASCII.GetBytes(
                "\0displayName\0toUpperCase\0id\0boxingring\0displayName\0Boxing Ring\0resources\0movieclips\0backgrounds\0stage_boxingring\0");
            var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
            linkage.Should().Be("boxingring");
            display.Should().Be("Boxing Ring");
        }

        [Fact]
        public void ReadStageImportIdentity_FallsBackToStageClip()
        {
            var bytes = Encoding.ASCII.GetBytes("\0stage_battlefield\0battlefield_bg\0");
            var (linkage, display) = ShellStageCloneService.ReadStageImportIdentity(bytes);
            linkage.Should().Be("battlefield");
            display.Should().Be("battlefield");
        }

        [Fact]
        public void PreviewBesideSwf_ReturnsThePngBesideTheSwf()
        {
            var dir = Path.Combine(Path.GetTempPath(), "ssf2mm-stage-preview-" + Path.GetRandomFileName());
            Directory.CreateDirectory(dir);
            try
            {
                var swf = Path.Combine(dir, "battlefield.swf");
                File.WriteAllBytes(swf, new byte[] { 1 });
                ShellStageCloneService.PreviewBesideSwf(swf).Should().BeNull();
                var png = Path.Combine(dir, "stagepreview.png");
                File.WriteAllBytes(png, new byte[] { 2 });
                ShellStageCloneService.PreviewBesideSwf(swf).Should().Be(png);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
