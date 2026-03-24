using FluentAssertions;
using SSF2ModManager.Models;
using Xunit;

namespace SSF2ModManager.Tests.Models
{
    public class GameBananaModTests
    {
        [Fact]
        public void GameBananaMod_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var mod = new GameBananaMod();

            // Assert
            mod.Id.Should().Be(0);
            mod.Name.Should().BeEmpty();
            mod.Description.Should().BeEmpty();
            mod.ModelName.Should().BeEmpty();
            mod.FullText.Should().BeEmpty();
            mod.Likes.Should().Be(0);
            mod.Views.Should().Be(0);
            mod.Downloads.Should().Be(0);
            mod.ProfileUrl.Should().BeEmpty();
            mod.Version.Should().BeEmpty();
        }

        [Fact]
        public void GameBananaMod_ShouldSetPropertiesCorrectly()
        {
            // Arrange & Act
            var mod = new GameBananaMod
            {
                Id = 123456,
                Name = "Test Character Mod",
                Description = "A test character mod",
                ModelName = "Mod",
                FullText = "Full description of the test mod",
                Likes = 100,
                Views = 5000,
                Downloads = 1500,
                ProfileUrl = "https://gamebanana.com/mods/123456",
                Version = "1.0.0",
                DateAddedTimestamp = 1711152000,
                DateUpdatedTimestamp = 1711238400
            };

            // Assert
            mod.Id.Should().Be(123456);
            mod.Name.Should().Be("Test Character Mod");
            mod.Description.Should().Be("A test character mod");
            mod.ModelName.Should().Be("Mod");
            mod.FullText.Should().Be("Full description of the test mod");
            mod.Likes.Should().Be(100);
            mod.Views.Should().Be(5000);
            mod.Downloads.Should().Be(1500);
            mod.ProfileUrl.Should().Be("https://gamebanana.com/mods/123456");
            mod.Version.Should().Be("1.0.0");
            mod.DateAddedTimestamp.Should().Be(1711152000);
            mod.DateUpdatedTimestamp.Should().Be(1711238400);
        }

        [Fact]
        public void GameBananaMod_ShouldHandleSubmitterCorrectly()
        {
            // Arrange
            var submitter = new GameBananaSubmitter
            {
                Id = 789,
                Name = "Test Author",
                AvatarUrl = "https://example.com/avatar.png",
                ProfileUrl = "https://gamebanana.com/members/789"
            };

            var mod = new GameBananaMod
            {
                Submitter = submitter
            };

            // Assert
            mod.Submitter.Should().NotBeNull();
            mod.Submitter!.Id.Should().Be(789);
            mod.Submitter.Name.Should().Be("Test Author");
            mod.Submitter.AvatarUrl.Should().Be("https://example.com/avatar.png");
            mod.Submitter.ProfileUrl.Should().Be("https://gamebanana.com/members/789");
        }

        [Fact]
        public void GameBananaMod_ShouldHandleCategoryCorrectly()
        {
            // Arrange
            var category = new GameBananaCategory
            {
                Name = "Characters",
                ProfileUrl = "https://gamebanana.com/characters"
            };

            var rootCategory = new GameBananaCategory
            {
                Name = "Mods",
                ProfileUrl = "https://gamebanana.com/mods"
            };

            var mod = new GameBananaMod
            {
                Category = category,
                RootCategory = rootCategory
            };

            // Assert
            mod.Category.Should().NotBeNull();
            mod.Category!.Name.Should().Be("Characters");
            mod.RootCategory.Should().NotBeNull();
            mod.RootCategory!.Name.Should().Be("Mods");
        }
    }

    public class GameBananaImageTests
    {
        [Fact]
        public void GameBananaImage_ShouldGenerateThumbnailUrl_WithFile220()
        {
            // Arrange
            var image = new GameBananaImage
            {
                BaseUrl = "https://images.gamebanana.com/img/ss/mods",
                File = "original.png",
                File220 = "220x220_thumb.png"
            };

            // Act & Assert
            image.ThumbnailUrl.Should().Be("https://images.gamebanana.com/img/ss/mods/220x220_thumb.png");
        }

        [Fact]
        public void GameBananaImage_ShouldGenerateThumbnailUrl_WithoutFile220()
        {
            // Arrange
            var image = new GameBananaImage
            {
                BaseUrl = "https://images.gamebanana.com/img/ss/mods",
                File = "original.png",
                File220 = ""
            };

            // Act & Assert
            image.ThumbnailUrl.Should().Be("https://images.gamebanana.com/img/ss/mods/original.png");
        }
    }

    public class GameBananaFileTests
    {
        [Fact]
        public void GameBananaFile_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var file = new GameBananaFile();

            // Assert
            file.Id.Should().Be(0);
            file.FileName.Should().BeEmpty();
            file.Description.Should().BeEmpty();
            file.DownloadUrl.Should().BeEmpty();
            file.FileSize.Should().Be(0);
            file.DateAdded.Should().Be(0);
        }

        [Fact]
        public void GameBananaFile_ShouldSetPropertiesCorrectly()
        {
            // Arrange & Act
            var file = new GameBananaFile
            {
                Id = 111,
                FileName = "character_mod.zip",
                Description = "Main mod file",
                DownloadUrl = "https://files.gamebanana.com/mods/character_mod.zip",
                FileSize = 1024000,
                DateAdded = 1711152000
            };

            // Assert
            file.Id.Should().Be(111);
            file.FileName.Should().Be("character_mod.zip");
            file.Description.Should().Be("Main mod file");
            file.DownloadUrl.Should().Be("https://files.gamebanana.com/mods/character_mod.zip");
            file.FileSize.Should().Be(1024000);
            file.DateAdded.Should().Be(1711152000);
        }
    }
}
