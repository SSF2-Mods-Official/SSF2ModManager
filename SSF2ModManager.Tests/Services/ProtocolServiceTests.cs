using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ProtocolServiceTests
    {
        [Fact]
        public void TryParse_ValidUrl_ShouldExtractParts()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm:https://files.gamebanana.com/mmdownload/123/test.zip,Character,456",
                out var req);

            ok.Should().BeTrue();
            req.ArchiveUrl.Should().Be("https://files.gamebanana.com/mmdownload/123/test.zip");
            req.ModType.Should().Be("Character");
            req.ModId.Should().Be(456);
        }

        [Fact]
        public void TryParse_WithoutModId_ShouldStillParse()
        {
            var ok = ProtocolService.TryParse(
                "ssf2mm:https://example.com/mod.zip,Stage",
                out var req);

            ok.Should().BeTrue();
            req.ModId.Should().BeNull();
            req.ModType.Should().Be("Stage");
        }

        [Fact]
        public void TryParse_InvalidScheme_ShouldReturnFalse()
        {
            ProtocolService.TryParse("https://example.com/mod.zip", out _).Should().BeFalse();
        }

        [Fact]
        public void BuildTestUrl_ShouldMatchParser()
        {
            var url = ProtocolService.BuildTestUrl("https://example.com/a.zip", "Character", 99);
            ProtocolService.TryParse(url, out var req).Should().BeTrue();
            req.ArchiveUrl.Should().Contain("example.com");
            req.ModId.Should().Be(99);
        }
    }
}
