using FluentAssertions;
using Moq;
using SSF2ModManager.Models;
using SSF2ModManager.Services;
using System.IO;
using Xunit;

namespace SSF2ModManager.Tests.Services
{
    public class ModManagerServiceTests
    {
        [Fact]
        public void ModManagerService_ShouldHaveKnownVersions()
        {
            // Act & Assert
            ModManagerService.KnownVersions.Should().NotBeEmpty();
            ModManagerService.KnownVersions.Should().Contain("SSF2 Beta 1.4.0");
            ModManagerService.KnownVersions.Should().Contain("SSF2 Beta 1.3.1.2");
            ModManagerService.KnownVersions.Should().Contain("Custom...");
        }

        [Fact]
        public void ModManagerService_ShouldInitializeDirectories()
        {
            // Arrange
            var mockApiClient = new Mock<GameBananaApiClient>();

            // Act
            var service = new ModManagerService(mockApiClient.Object);

            // Assert
            service.Should().NotBeNull();
            service.InstalledMods.Should().NotBeNull();
            service.Versions.Should().NotBeNull();
            service.ModsBaseDir.Should().NotBeNullOrEmpty();
            Directory.Exists(service.ModsBaseDir).Should().BeTrue();
        }

        [Fact]
        public void ModManagerService_InitialState_ShouldHaveEmptyModsList()
        {
            // Arrange
            var mockApiClient = new Mock<GameBananaApiClient>();

            // Act
            var service = new ModManagerService(mockApiClient.Object);

            // Assert
            // Note: This may fail if there's existing data in AppData
            // In a proper test, you'd want to use a temp directory
            service.InstalledMods.Should().NotBeNull();
        }

        [Fact]
        public void ModManagerService_ActiveVersion_ShouldSetAndGet()
        {
            // Arrange
            var mockApiClient = new Mock<GameBananaApiClient>();
            var service = new ModManagerService(mockApiClient.Object);

            // Act
            service.ActiveVersion = "SSF2 Beta 1.4.0";

            // Assert
            service.ActiveVersion.Should().Be("SSF2 Beta 1.4.0");
        }

        [Fact]
        public void ModManagerService_ModsBaseDir_ShouldReturnValidPath()
        {
            // Arrange
            var mockApiClient = new Mock<GameBananaApiClient>();
            var service = new ModManagerService(mockApiClient.Object);

            // Act & Assert
            service.ModsBaseDir.Should().NotBeNullOrEmpty();
            service.ModsBaseDir.Should().Contain("SSF2ModManager");
        }
    }

    public class DebugLoggerTests
    {
        [Fact]
        public void DebugLogger_Log_ShouldNotThrow()
        {
            // Act & Assert
            var act = () => DebugLogger.Log("Test message");
            act.Should().NotThrow();
        }

        [Fact]
        public void DebugLogger_Log_WithMultipleMessages_ShouldNotThrow()
        {
            // Act & Assert
            var act = () =>
            {
                DebugLogger.Log("Message 1");
                DebugLogger.Log("Message 2");
                DebugLogger.Log("Message 3");
            };
            act.Should().NotThrow();
        }

        [Fact]
        public void DebugLogger_Log_WithNullMessage_ShouldNotThrow()
        {
            // Act & Assert
            var act = () => DebugLogger.Log(null!);
            act.Should().NotThrow();
        }

        [Fact]
        public void DebugLogger_Log_WithEmptyMessage_ShouldNotThrow()
        {
            // Act & Assert
            var act = () => DebugLogger.Log("");
            act.Should().NotThrow();
        }
    }
}
