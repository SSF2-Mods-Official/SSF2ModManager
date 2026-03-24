# SSF2ModManager Tests

Comprehensive unit test suite for SSF2 Mod Manager.

## Test Coverage

### Models
- **InstalledModTests**: Tests for `InstalledMod`, `SSF2VersionEntry`, `BackedUpFile`, and `ModDatabase`
  - Default initialization
  - Property setters/getters
  - Collection management
  - Display name generation

- **GameBananaModTests**: Tests for GameBanana API models
  - `GameBananaMod` properties
  - `GameBananaSubmitter` handling
  - `GameBananaImage` thumbnail URL generation
  - `GameBananaFile` properties
  - `GameBananaCategory` handling

- **CliOptionsTests**: Tests for command-line options
  - Default values
  - Boolean flags
  - String properties
  - Integer properties
  - List operations

### Converters
- **BoolToEnabledTextConverter**: Tests enabled/disabled text conversion
- **BoolToToggleTextConverter**: Tests enable/disable button text
- **BoolToColorConverter**: Tests green/gray color conversion
- **BoolToOpacityConverter**: Tests opacity values (1.0/0.5)
- **BoolToVisibilityConverter**: Tests Visible/Collapsed conversion
- **InverseBoolToVisibilityConverter**: Tests inverse visibility
- **NullToVisibilityConverter**: Tests null-based visibility
- **FileSizeConverter**: Tests file size formatting (B/KB/MB)

### Services
- **ModManagerServiceTests**: Tests for mod management service
  - Initialization
  - Known versions
  - Active version management
  - Directory creation

- **DebugLoggerTests**: Tests for debug logging utility
  - Basic logging
  - Null/empty message handling
  - Multiple messages

### Utilities
- **LocalizationTests**: Tests for localization system
  - Language loading
  - Fallback to English
  - Key retrieval
  - Invalid language handling

## Running Tests

### Visual Studio
1. Open the solution in Visual Studio
2. Build the solution
3. Open Test Explorer (Test > Test Explorer)
4. Click "Run All Tests"

### Command Line
```bash
cd SSF2ModManager.Tests
dotnet test
```

### With Coverage
```bash
dotnet test /p:CollectCoverage=true /p:CoverletOutputFormat=opencover
```

## Test Framework

- **xUnit**: Test framework
- **FluentAssertions**: Assertion library for readable tests
- **Moq**: Mocking framework for dependencies
- **Coverlet**: Code coverage

## Adding New Tests

1. Create a new test class in the appropriate folder (Models/Services/Converters)
2. Follow the naming convention: `{ClassName}Tests`
3. Use FluentAssertions for assertions
4. Use Moq for mocking dependencies
5. Follow Arrange-Act-Assert pattern

Example:
```csharp
[Fact]
public void MyMethod_WhenCondition_ShouldExpectedBehavior()
{
    // Arrange
    var sut = new MyClass();
    
    // Act
    var result = sut.MyMethod();
    
    // Assert
    result.Should().Be(expectedValue);
}
```

## Test Categories

Tests are organized by:
- **Unit Tests**: Test individual components in isolation
- **Integration Tests**: Test component interactions (future)
- **UI Tests**: Test WPF UI components (future)

## Known Limitations

- Some tests require the application to not have existing data in AppData
- File system tests may create temporary directories
- Some tests depend on localization files existing

## CI/CD Integration

Tests can be run in CI/CD pipelines:
```yaml
- name: Run tests
  run: dotnet test --configuration Release --no-build --verbosity normal
```
