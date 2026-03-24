using FluentAssertions;
using SSF2ModManager.Converters;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Xunit;

namespace SSF2ModManager.Tests.Converters
{
    public class BoolToEnabledTextConverterTests
    {
        private readonly BoolToEnabledTextConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturnEnabled()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<string>();
            result.ToString().Should().ContainAny("Enabled", "Activado");
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturnDisabled()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<string>();
            result.ToString().Should().ContainAny("Disabled", "Desactivado");
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class BoolToToggleTextConverterTests
    {
        private readonly BoolToToggleTextConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturnDisable()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<string>();
            result.ToString().Should().ContainAny("Disable", "Desactivar");
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturnEnable()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<string>();
            result.ToString().Should().ContainAny("Enable", "Activar");
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class BoolToColorConverterTests
    {
        private readonly BoolToColorConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturnGreenBrush()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(Brush), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<SolidColorBrush>();
            var brush = (SolidColorBrush)result;
            brush.Color.Should().Be(Color.FromRgb(76, 175, 80));
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturnGrayBrush()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(Brush), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().BeOfType<SolidColorBrush>();
            var brush = (SolidColorBrush)result;
            brush.Color.Should().Be(Color.FromRgb(158, 158, 158));
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class BoolToOpacityConverterTests
    {
        private readonly BoolToOpacityConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturn1()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(double), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(1.0);
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturn0Point5()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(double), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(0.5);
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class BoolToVisibilityConverterTests
    {
        private readonly BoolToVisibilityConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturnVisible()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Visible);
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturnCollapsed()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Collapsed);
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class InverseBoolToVisibilityConverterTests
    {
        private readonly InverseBoolToVisibilityConverter _converter = new();

        [Fact]
        public void Convert_WhenTrue_ShouldReturnCollapsed()
        {
            // Arrange
            bool value = true;

            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Collapsed);
        }

        [Fact]
        public void Convert_WhenFalse_ShouldReturnVisible()
        {
            // Arrange
            bool value = false;

            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Visible);
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(bool), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class NullToVisibilityConverterTests
    {
        private readonly NullToVisibilityConverter _converter = new();

        [Fact]
        public void Convert_WhenNotNull_ShouldReturnVisible()
        {
            // Arrange
            var value = new object();

            // Act
            var result = _converter.Convert(value, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Visible);
        }

        [Fact]
        public void Convert_WhenNull_ShouldReturnCollapsed()
        {
            // Arrange
            object? value = null;

            // Act
            var result = _converter.Convert(value!, typeof(Visibility), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be(Visibility.Collapsed);
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(object), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }

    public class FileSizeConverterTests
    {
        private readonly FileSizeConverter _converter = new();

        [Fact]
        public void Convert_WhenBytes_ShouldReturnBytesFormat()
        {
            // Arrange
            long value = 512;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("512 B");
        }

        [Fact]
        public void Convert_WhenKilobytes_ShouldReturnKBFormat()
        {
            // Arrange
            long value = 1536; // 1.5 KB

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("1.5 KB");
        }

        [Fact]
        public void Convert_WhenMegabytes_ShouldReturnMBFormat()
        {
            // Arrange
            long value = 2621440; // 2.5 MB

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("2.5 MB");
        }

        [Fact]
        public void Convert_WhenZero_ShouldReturn0B()
        {
            // Arrange
            long value = 0;

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("0 B");
        }

        [Fact]
        public void Convert_WhenInvalidType_ShouldReturn0B()
        {
            // Arrange
            var value = "invalid";

            // Act
            var result = _converter.Convert(value, typeof(string), null!, CultureInfo.InvariantCulture);

            // Assert
            result.Should().Be("0 B");
        }

        [Fact]
        public void ConvertBack_ShouldThrowNotImplementedException()
        {
            // Act & Assert
            var act = () => _converter.ConvertBack(null!, typeof(long), null!, CultureInfo.InvariantCulture);
            act.Should().Throw<NotImplementedException>();
        }
    }
}
