using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class AppInfoTests
    {
        [Theory]
        [InlineData("1.0.0", "1.0.0.0", 0)]
        [InlineData("1.0.0.0", "1.0.0", 0)]
        [InlineData("1.0.0", "1.0.1", -1)]
        [InlineData("1.1.0", "1.0.9", 1)]
        [InlineData("v1.0.0", "1.0.0", 0)]
        public void CompareVersions_ShouldNormalizeParts(string local, string remote, int expected)
        {
            AppInfo.CompareVersions(local, remote).Should().Be(expected);
        }

        [Fact]
        public void IsUpToDate_WhenEqual_ShouldReturnTrue()
        {
            AppInfo.IsUpToDate("1.0.0", "1.0.0.0").Should().BeTrue();
        }

        [Fact]
        public void Version_ShouldMatchDisplayVersion()
        {
            AppInfo.DisplayVersion.Should().Be("v" + AppInfo.Version);
        }
    }
}
