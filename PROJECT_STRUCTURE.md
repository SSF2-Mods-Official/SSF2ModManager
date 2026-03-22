# SSF2 Mod Manager Project Structure

This document explains the main structure and purpose of each folder and file in the SSF2 Mod Manager project.

## Root Files
- **App.xaml / App.xaml.cs**: Application entry point and global resources for WPF.
- **MainWindow.xaml / MainWindow.xaml.cs**: Main window UI and logic.
- **AssemblyInfo.cs**: Assembly metadata.
- **SSF2ModManager.csproj**: Project file and build configuration.
- **SSF2ModManager.sln**: Visual Studio solution file.
- **run.bat**: Batch script to build and run the project.
- **SSF2M.ico**: Application icon (used for taskbar, Alt+Tab, etc.).
- **SSF2M.png / SSF232.png**: PNG images used in the UI.
- **1-Click-News-Themes-Plan.md**: Project planning and feature checklist.

## Folders
- **bin/**: Build output (compiled binaries, auto-generated).
- **obj/**: Intermediate build files (auto-generated).
- **Converters/**: Value converters for WPF data binding.
  - `Converters.cs`: Contains custom converter classes.
- **Dialogs/**: Custom dialog windows for user interaction.
  - `BuildSettingsDialog.cs`, `FileSelectionDialog.cs`, `InputDialog.cs`, `InstalledModSettingsDialog.cs`, `ListPickerDialog.cs`: Dialog implementations for various mod manager features.
- **Models/**: Data models representing mods and related entities.
  - `GameBananaMod.cs`: Represents a mod from GameBanana.
  - `InstalledMod.cs`: Represents a mod installed in SSF2.
- **Services/**: Core logic and API clients.
  - `ModManagerService.cs`: Handles mod management operations.
  - `GameBananaApiClient.cs`: Communicates with the GameBanana API.
  - `DebugLogger.cs`: Logging utility for debugging.

## Notes
- The project uses WPF (.NET 8) for the UI.
- All UI resources (icons, images) are embedded as resources or referenced in XAML.
- The application icon must be a .ico file with multiple sizes for best appearance in Windows.

For more details on specific files or folders, see the inline comments in the code or ask for a deeper breakdown.