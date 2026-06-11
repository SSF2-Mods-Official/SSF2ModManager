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
                "ssf2mm:https://gamebanana.com/dl/1708765,Mod,679407",
                out var req);

            ok.Should().BeTrue();
            req.ArchiveUrl.Should().Be("https://gamebanana.com/dl/1708765");
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
}
