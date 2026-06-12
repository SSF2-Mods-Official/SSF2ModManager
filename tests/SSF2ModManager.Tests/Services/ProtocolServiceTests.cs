using FluentAssertions;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ProtocolServiceTests
    {
        [Fact]
        public void TryParse_DlUrl_ShouldExtractParts()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm:https://gamebanana.com/dl/1708765,Mod,679407",
                out var req);

            ok.Should().BeTrue();
            req.ArchiveUrl.Should().Be("https://gamebanana.com/dl/1708765");
            req.ModType.Should().Be("Mod");
            req.ModId.Should().Be(679407);
        }

        [Fact]
        public void TryParse_MmdlUrl_ShouldExtractParts()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm:https://gamebanana.com/mmdl/1708765,Mod,679407",
                out var req);

            ok.Should().BeTrue();
            req.ArchiveUrl.Should().Be("https://gamebanana.com/mmdl/1708765");
            req.ModId.Should().Be(679407);
        }

        [Fact]
        public void TryParse_GameBananaMalformedScheme_ShouldNormalize()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm://https//gamebanana.com/mmdl/1708765,Mod,679407",
                out var req);

            ok.Should().BeTrue();
            req.ArchiveUrl.Should().Be("https://gamebanana.com/mmdl/1708765");
            req.ModType.Should().Be("Mod");
            req.ModId.Should().Be(679407);
        }

        [Fact]
        public void TryParse_WithoutModId_ShouldStillParse()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm:https://example.com/mod.zip,Sound",
                out var req);

            ok.Should().BeTrue();
            req.ModId.Should().BeNull();
            req.ModType.Should().Be("Sound");
        }

        [Fact]
        public void TryParse_InvalidScheme_ShouldReturnFalse()
        {
            ProtocolService.TryParse("https://example.com/mod.zip", out _).Should().BeFalse();
        }

        [Fact]
        public void BuildTestUrl_ShouldMatchParser()
        {
            var url = ProtocolService.BuildTestUrl("https://example.com/a.zip", "Mod", 99);
            ProtocolService.TryParse(url, out var req).Should().BeTrue();
            req.ArchiveUrl.Should().Contain("example.com");
            req.ModId.Should().Be(99);
        }
    }

    public class GameBananaUrlHelperTests
    {
        [Theory]
        [InlineData("https://gamebanana.com/dl/1708765", 1708765)]
        [InlineData("https://gamebanana.com/mmdl/1708765", 1708765)]
        [InlineData("https//gamebanana.com/mmdl/1708765", 1708765)]
        public void TryExtractFileId_ShouldWorkForDlAndMmdl(string url, int expected)
        {
            GameBananaUrlHelper.TryExtractFileId(url, out var id).Should().BeTrue();
            id.Should().Be(expected);
        }

        [Fact]
        public void ReferToSameFile_DlAndMmdl_ShouldMatch()
        {
            GameBananaUrlHelper.ReferToSameFile(
                "https://gamebanana.com/mmdl/1708765",
                "https://gamebanana.com/dl/1708765").Should().BeTrue();
        }

        [Fact]
        public void MatchProtocolFile_ShouldMatchByFileIdAcrossDlAndMmdl()
        {
            var files = new List<GameBananaFile>
            {
                new() { Id = 1708765, FileName = "mod.rar", DownloadUrl = "https://gamebanana.com/dl/1708765" }
            };

            var match = GameBananaUrlHelper.MatchProtocolFile(
                "https://gamebanana.com/mmdl/1708765", files);

            match.Should().NotBeNull();
            match!.Id.Should().Be(1708765);
        }
    }
}
