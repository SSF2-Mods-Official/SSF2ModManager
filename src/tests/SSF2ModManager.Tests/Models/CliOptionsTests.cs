using FluentAssertions;
using SSF2ModManager.Models;
using Xunit;

namespace SSF2ModManager.Tests.Models
{
    public class CliOptionsTests
    {
        [Fact]
        public void CliOptions_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var options = new CliOptions();

            // Assert
            options.Help.Should().BeFalse();
            options.Version.Should().BeFalse();
            options.Verbose.Should().BeFalse();
            options.LogFile.Should().BeNull();
            options.StartMinimized.Should().BeFalse();
            options.StartHidden.Should().BeFalse();
            options.OpenPage.Should().BeNull();
            options.OpenNews.Should().BeFalse();
            options.NewsPath.Should().BeNull();
            options.Theme.Should().BeNull();
            options.RefreshNews.Should().BeFalse();
            options.NewsCacheClear.Should().BeFalse();
            options.NewsPreviewOnly.Should().BeFalse();
            options.Install.Should().BeEmpty();
            options.Uninstall.Should().BeEmpty();
            options.Enable.Should().BeEmpty();
            options.Disable.Should().BeEmpty();
            options.Update.Should().BeEmpty();
            options.ImportGb.Should().BeEmpty();
            options.InstallBatch.Should().BeNull();
            options.SearchQuery.Should().BeNull();
            options.DownloadResultIndex.Should().BeNull();
            options.BrowseLimit.Should().BeNull();
            options.AddBuild.Should().BeEmpty();
            options.RemoveBuild.Should().BeEmpty();
            options.LaunchBuild.Should().BeEmpty();
            options.ConfigPath.Should().BeNull();
            options.ExportConfig.Should().BeNull();
            options.ImportConfig.Should().BeNull();
            options.SetPrefs.Should().BeEmpty();
            options.Proxy.Should().BeNull();
        }

        [Fact]
        public void CliOptions_ShouldSetBooleanFlagsCorrectly()
        {
            // Arrange & Act
            var options = new CliOptions
            {
                Help = true,
                Version = true,
                Verbose = true,
                StartMinimized = true,
                StartHidden = true,
                OpenNews = true,
                RefreshNews = true,
                NewsCacheClear = true,
                NewsPreviewOnly = true
            };

            // Assert
            options.Help.Should().BeTrue();
            options.Version.Should().BeTrue();
            options.Verbose.Should().BeTrue();
            options.StartMinimized.Should().BeTrue();
            options.StartHidden.Should().BeTrue();
            options.OpenNews.Should().BeTrue();
            options.RefreshNews.Should().BeTrue();
            options.NewsCacheClear.Should().BeTrue();
            options.NewsPreviewOnly.Should().BeTrue();
        }

        [Fact]
        public void CliOptions_ShouldSetStringPropertiesCorrectly()
        {
            // Arrange & Act
            var options = new CliOptions
            {
                LogFile = "C:\\Logs\\app.log",
                OpenPage = "installed",
                NewsPath = "C:\\News",
                Theme = "theme-dark",
                InstallBatch = "C:\\batch.txt",
                SearchQuery = "sonic mod",
                ConfigPath = "C:\\config.json",
                ExportConfig = "C:\\export.json",
                ImportConfig = "C:\\import.json",
                Proxy = "http://proxy.example.com:8080"
            };

            // Assert
            options.LogFile.Should().Be("C:\\Logs\\app.log");
            options.OpenPage.Should().Be("installed");
            options.NewsPath.Should().Be("C:\\News");
            options.Theme.Should().Be("theme-dark");
            options.InstallBatch.Should().Be("C:\\batch.txt");
            options.SearchQuery.Should().Be("sonic mod");
            options.ConfigPath.Should().Be("C:\\config.json");
            options.ExportConfig.Should().Be("C:\\export.json");
            options.ImportConfig.Should().Be("C:\\import.json");
            options.Proxy.Should().Be("http://proxy.example.com:8080");
        }

        [Fact]
        public void CliOptions_ShouldSetIntegerPropertiesCorrectly()
        {
            // Arrange & Act
            var options = new CliOptions
            {
                DownloadResultIndex = 0,
                BrowseLimit = 50
            };

            // Assert
            options.DownloadResultIndex.Should().Be(0);
            options.BrowseLimit.Should().Be(50);
        }

        [Fact]
        public void CliOptions_ShouldAddItemsToListsCorrectly()
        {
            // Arrange
            var options = new CliOptions();

            // Act
            options.Install.Add("https://gamebanana.com/mods/123456");
            options.Install.Add("https://gamebanana.com/mods/789012");
            options.Uninstall.Add("mod-id-1");
            options.Enable.Add("mod-id-2");
            options.Disable.Add("mod-id-3");
            options.Update.Add("mod-id-4");
            options.ImportGb.Add("https://gamebanana.com/mods/345678");
            options.AddBuild.Add("C:\\SSF2\\1.4.0");
            options.RemoveBuild.Add("SSF2 Beta 1.3.1");
            options.LaunchBuild.Add("SSF2 Beta 1.4.0");
            options.SetPrefs.Add("theme=dark");
            options.SetPrefs.Add("language=en");

            // Assert
            options.Install.Should().HaveCount(2);
            options.Install.Should().Contain("https://gamebanana.com/mods/123456");
            options.Install.Should().Contain("https://gamebanana.com/mods/789012");
            
            options.Uninstall.Should().HaveCount(1);
            options.Uninstall.Should().Contain("mod-id-1");
            
            options.Enable.Should().HaveCount(1);
            options.Enable.Should().Contain("mod-id-2");
            
            options.Disable.Should().HaveCount(1);
            options.Disable.Should().Contain("mod-id-3");
            
            options.Update.Should().HaveCount(1);
            options.Update.Should().Contain("mod-id-4");
            
            options.ImportGb.Should().HaveCount(1);
            options.ImportGb.Should().Contain("https://gamebanana.com/mods/345678");
            
            options.AddBuild.Should().HaveCount(1);
            options.AddBuild.Should().Contain("C:\\SSF2\\1.4.0");
            
            options.RemoveBuild.Should().HaveCount(1);
            options.RemoveBuild.Should().Contain("SSF2 Beta 1.3.1");
            
            options.LaunchBuild.Should().HaveCount(1);
            options.LaunchBuild.Should().Contain("SSF2 Beta 1.4.0");
            
            options.SetPrefs.Should().HaveCount(2);
            options.SetPrefs.Should().Contain("theme=dark");
            options.SetPrefs.Should().Contain("language=en");
        }
    }
}
