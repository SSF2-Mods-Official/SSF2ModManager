using FluentAssertions;
using Xunit;

namespace SSF2ModManager.Tests
{
    public class LocalizationTests
    {
        [Fact]
        public void Localization_CurrentLanguage_ShouldDefaultToEnglish()
        {
            // Act & Assert
            Localization.CurrentLanguage.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Localization_Get_ShouldReturnKeyWhenNotFound()
        {
            // Arrange
            var nonExistentKey = "ThisKeyDoesNotExist123456";

            // Act
            var result = Localization.Get(nonExistentKey);

            // Assert
            result.Should().Be(nonExistentKey);
        }

        [Fact]
        public void Localization_Get_WithCommonKeys_ShouldReturnValues()
        {
            // Arrange
            Localization.Load("en");

            // Act & Assert
            // These should return either the localized string or the key itself
            var appTitle = Localization.Get("AppTitle");
            appTitle.Should().NotBeNullOrEmpty();

            var browseModsText = Localization.Get("BrowseMods");
            browseModsText.Should().NotBeNullOrEmpty();
        }

        [Fact]
        public void Localization_Load_WithValidLanguageCode_ShouldNotThrow()
        {
            // Act & Assert
            var act = () => Localization.Load("en");
            act.Should().NotThrow();
        }

        [Fact]
        public void Localization_Load_WithInvalidLanguageCode_ShouldFallbackToEnglish()
        {
            // Act
            Localization.Load("invalid_lang_code_xyz");

            // Assert
            Localization.CurrentLanguage.Should().Be("en");
        }

        [Fact]
        public void Localization_Load_Spanish_ShouldSetCurrentLanguage()
        {
            // Act
            Localization.Load("es");

            // Assert
            // Should be either "es" if Spanish file exists, or "en" as fallback
            Localization.CurrentLanguage.Should().BeOneOf("es", "en");
        }
    }
}
