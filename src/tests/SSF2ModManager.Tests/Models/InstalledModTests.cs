using FluentAssertions;
using SSF2ModManager.Models;
using Xunit;

namespace SSF2ModManager.Tests.Models
{
    public class InstalledModTests
    {
        [Fact]
        public void InstalledMod_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var mod = new InstalledMod();

            // Assert
            mod.Id.Should().BeEmpty();
            mod.GameBananaId.Should().Be(0);
            mod.Name.Should().BeEmpty();
            mod.Author.Should().BeEmpty();
            mod.Enabled.Should().BeTrue();
            mod.IgnoreUpdates.Should().BeFalse();
            mod.IsGameFiles.Should().BeFalse();
            mod.InstalledFiles.Should().BeEmpty();
            mod.BackedUpFiles.Should().BeEmpty();
            mod.InstalledDate.Should().BeCloseTo(DateTime.Now, TimeSpan.FromSeconds(5));
        }

        [Fact]
        public void InstalledMod_ShouldSetPropertiesCorrectly()
        {
            // Arrange
            var testDate = DateTime.Parse("2026-03-23");
            var mod = new InstalledMod
            {
                Id = "test-mod-123",
                GameBananaId = 456789,
                Name = "Test Mod",
                Author = "Test Author",
                Creator = "Test Creator",
                ModType = "Character",
                Description = "A test mod",
                Category = "Test Category",
                Version = "1.0.0",
                FolderName = "test_mod",
                ThumbnailUrl = "https://example.com/thumb.jpg",
                ProfileUrl = "https://gamebanana.com/mods/456789",
                Enabled = false,
                InstalledDate = testDate,
                TargetVersion = "SSF2 Beta 1.4.0",
                IsGameFiles = true,
                IgnoreUpdates = true,
                BuildDisplayName = "SSF2 Beta 1.4.0"
            };

            // Assert
            mod.Id.Should().Be("test-mod-123");
            mod.GameBananaId.Should().Be(456789);
            mod.Name.Should().Be("Test Mod");
            mod.Author.Should().Be("Test Author");
            mod.Creator.Should().Be("Test Creator");
            mod.ModType.Should().Be("Character");
            mod.Description.Should().Be("A test mod");
            mod.Category.Should().Be("Test Category");
            mod.Version.Should().Be("1.0.0");
            mod.FolderName.Should().Be("test_mod");
            mod.ThumbnailUrl.Should().Be("https://example.com/thumb.jpg");
            mod.ProfileUrl.Should().Be("https://gamebanana.com/mods/456789");
            mod.Enabled.Should().BeFalse();
            mod.InstalledDate.Should().Be(testDate);
            mod.TargetVersion.Should().Be("SSF2 Beta 1.4.0");
            mod.IsGameFiles.Should().BeTrue();
            mod.IgnoreUpdates.Should().BeTrue();
            mod.BuildDisplayName.Should().Be("SSF2 Beta 1.4.0");
        }

        [Fact]
        public void InstalledMod_ShouldAddInstalledFilesCorrectly()
        {
            // Arrange
            var mod = new InstalledMod();
            
            // Act
            mod.InstalledFiles.Add("file1.swf");
            mod.InstalledFiles.Add("file2.png");

            // Assert
            mod.InstalledFiles.Should().HaveCount(2);
            mod.InstalledFiles.Should().Contain("file1.swf");
            mod.InstalledFiles.Should().Contain("file2.png");
        }

        [Fact]
        public void InstalledMod_ShouldAddBackedUpFilesCorrectly()
        {
            // Arrange
            var mod = new InstalledMod();
            var backup1 = new BackedUpFile
            {
                OriginalRelativePath = "path/to/file1.swf",
                BackupFullPath = "C:\\backups\\file1.swf.bak"
            };
            var backup2 = new BackedUpFile
            {
                OriginalRelativePath = "path/to/file2.png",
                BackupFullPath = "C:\\backups\\file2.png.bak"
            };

            // Act
            mod.BackedUpFiles.Add(backup1);
            mod.BackedUpFiles.Add(backup2);

            // Assert
            mod.BackedUpFiles.Should().HaveCount(2);
            mod.BackedUpFiles[0].OriginalRelativePath.Should().Be("path/to/file1.swf");
            mod.BackedUpFiles[1].BackupFullPath.Should().Be("C:\\backups\\file2.png.bak");
        }
    }

    public class SSF2VersionEntryTests
    {
        [Fact]
        public void SSF2VersionEntry_ShouldInitializeWithDefaultValues()
        {
            // Arrange & Act
            var entry = new SSF2VersionEntry();

            // Assert
            entry.VersionName.Should().BeEmpty();
            entry.FolderPath.Should().BeEmpty();
            entry.Nickname.Should().BeEmpty();
            entry.DisplayName.Should().BeEmpty();
        }

        [Fact]
        public void SSF2VersionEntry_DisplayName_ShouldReturnVersionNameWhenNoNickname()
        {
            // Arrange
            var entry = new SSF2VersionEntry
            {
                VersionName = "SSF2 Beta 1.4.0",
                FolderPath = "C:\\SSF2\\1.4.0"
            };

            // Act & Assert
            entry.DisplayName.Should().Be("SSF2 Beta 1.4.0");
        }

        [Fact]
        public void SSF2VersionEntry_DisplayName_ShouldIncludeNicknameWhenSet()
        {
            // Arrange
            var entry = new SSF2VersionEntry
            {
                VersionName = "SSF2 Beta 1.4.0",
                Nickname = "Latest Build",
                FolderPath = "C:\\SSF2\\1.4.0"
            };

            // Act & Assert
            entry.DisplayName.Should().Be("Latest Build (SSF2 Beta 1.4.0)");
        }
    }

    public class ModDatabaseTests
    {
        [Fact]
        public void ModDatabase_ShouldInitializeWithEmptyCollections()
        {
            // Arrange & Act
            var db = new ModDatabase();

            // Assert
            db.Mods.Should().BeEmpty();
            db.Versions.Should().BeEmpty();
            db.ActiveVersion.Should().BeEmpty();
        }

        [Fact]
        public void ModDatabase_ShouldAddModsCorrectly()
        {
            // Arrange
            var db = new ModDatabase();
            var mod1 = new InstalledMod { Id = "mod1", Name = "Mod 1" };
            var mod2 = new InstalledMod { Id = "mod2", Name = "Mod 2" };

            // Act
            db.Mods.Add(mod1);
            db.Mods.Add(mod2);

            // Assert
            db.Mods.Should().HaveCount(2);
            db.Mods[0].Name.Should().Be("Mod 1");
            db.Mods[1].Name.Should().Be("Mod 2");
        }

        [Fact]
        public void ModDatabase_ShouldAddVersionsCorrectly()
        {
            // Arrange
            var db = new ModDatabase();
            var v1 = new SSF2VersionEntry { VersionName = "1.4.0", FolderPath = "C:\\SSF2\\1.4.0" };
            var v2 = new SSF2VersionEntry { VersionName = "1.3.1", FolderPath = "C:\\SSF2\\1.3.1" };

            // Act
            db.Versions.Add(v1);
            db.Versions.Add(v2);

            // Assert
            db.Versions.Should().HaveCount(2);
            db.Versions[0].VersionName.Should().Be("1.4.0");
            db.Versions[1].VersionName.Should().Be("1.3.1");
        }

        [Fact]
        public void ModDatabase_ShouldSetActiveVersionCorrectly()
        {
            // Arrange
            var db = new ModDatabase();

            // Act
            db.ActiveVersion = "SSF2 Beta 1.4.0";

            // Assert
            db.ActiveVersion.Should().Be("SSF2 Beta 1.4.0");
        }
    }
}
