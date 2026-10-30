using System.Text.Json;
using FluentAssertions;
using SSF2ModManager.Services;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class GitHubReleaseChannelTests
    {
        private const string MixedReleases = """
            [
              {
                "tag_name": "v0.9.5-exp.1",
                "draft": false,
                "prerelease": true,
                "assets": []
              },
              {
                "tag_name": "v0.9.4",
                "draft": false,
                "prerelease": false,
                "assets": []
              }
            ]
            """;

        private const string DraftFirst = """
            [
              {
                "tag_name": "v9.9.9",
                "draft": true,
                "prerelease": true,
                "assets": []
              },
              {
                "tag_name": "v0.9.4",
                "draft": false,
                "prerelease": false,
                "assets": []
              }
            ]
            """;

        [Fact]
        public void PickNewestTag_SkipsDrafts_TakesNewestIncludingPrerelease()
        {
            using var doc = JsonDocument.Parse(MixedReleases);
            GitHubReleaseChannel.PickNewestTag(doc.RootElement).Should().Be("v0.9.5-exp.1");
        }

        [Fact]
        public void PickNewestTag_SkipsDraftOnly()
        {
            using var doc = JsonDocument.Parse(DraftFirst);
            GitHubReleaseChannel.PickNewestTag(doc.RootElement).Should().Be("v0.9.4");
        }

        [Fact]
        public void ParseRelease_ReadsManagerAssets()
        {
            const string json = """
                {
                  "tag_name": "v0.9.5-exp.1",
                  "name": "v0.9.5-exp.1",
                  "html_url": "https://example/releases/tag/v0.9.5-exp.1",
                  "assets": [
                    {
                      "name": "SSF2ModManager-win-x64.zip",
                      "browser_download_url": "https://example/a.zip",
                      "size": 12
                    },
                    {
                      "name": "source.zip",
                      "browser_download_url": "https://example/s.zip",
                      "size": 3
                    }
                  ]
                }
                """;
            using var doc = JsonDocument.Parse(json);
            var info = LauncherUpdateService.ParseRelease(doc.RootElement);
            info.TagName.Should().Be("v0.9.5-exp.1");
            info.Assets.Should().ContainSingle(a => a.Name == "SSF2ModManager-win-x64.zip");
        }

        [Theory]
        [InlineData("0.9.4", "0.9.5-exp.1", -1)]
        [InlineData("0.9.5-exp.1", "0.9.5", 0)]
        [InlineData("0.9.5-exp.2", "0.9.4", 1)]
        public void CompareVersions_StripsExperimentalSuffix(string local, string remote, int expected)
        {
            AppInfo.CompareVersions(local, remote).Should().Be(expected);
        }
    }
}
