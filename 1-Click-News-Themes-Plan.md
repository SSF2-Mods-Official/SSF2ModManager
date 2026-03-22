
# SSF2 Mod Manager – 1-Click, News, Themes, and UI Improvements: Checklist

## Phase 1: 1-Click Installer Integration
- [ ] Register custom URI protocol `ssf2mm:` in Windows registry
  - [ ] Add code to register protocol on first launch or via menu option
  - [ ] Use registry path: `HKEY_CURRENT_USER\Software\Classes\ssf2mm`
  - [ ] Set default value, `URL Protocol`, and command to launch app with `%1` as argument
- [ ] Handle command-line arguments in `App.xaml.cs`
  - [ ] Override `OnStartup` to parse command-line arguments
  - [ ] If argument matches `ssf2mm:` schema, extract download URL, mod type, and mod ID
  - [ ] Trigger mod download/install flow automatically (with user confirmation)
- [ ] (Optional) Single instance handling
  - [ ] Prevent multiple instances, or forward protocol URL to running instance

## Phase 2: UI/UX Improvements
- [ ] Update Sandbox resource description in Resources tab
  - [ ] Change Sandbox entry to: “Sandbox is a popular SSF2 Beta modpack from version 1.3.”
- [ ] Add News tab
  - [ ] Add new sidebar tab: “News”
  - [ ] News page displays articles (title, date, content)
  - [ ] Add first article:
    - [ ] **Title:** “Introducing SSF2 Mod Manager”
    - [ ] **Content:** “Welcome to the first public release of SSF2 Mod Manager! Manage, install, and update your SSF2 mods with ease. 1-Click GameBanana integration coming soon.”
  - [ ] Store read/unread state (in user settings or local file)
  - [ ] Show red notification badge with count of unread articles on Resources tab
- [ ] Fix taskbar icon size for WPF
  - [ ] Use a 256x256 PNG for the `Window.Icon` property
  - [ ] Embed the icon as a resource in the project
  - [ ] Reference the icon using a pack URI in both `MainWindow.xaml` and `App.xaml`
- [ ] Implement themes
  - [ ] Add theme system (at least “Dark” and “Light”)
  - [ ] Store user’s theme preference (in settings)
  - [ ] Allow switching themes via menu or settings page
  - [ ] Refactor XAML to use DynamicResource for all colors/brushes
  - [ ] Provide two ResourceDictionary files: `ThemeDark.xaml` and `ThemeLight.xaml`

## Relevant files
- `App.xaml`, `App.xaml.cs` — Startup, theme loading, protocol handling
- `MainWindow.xaml`, `MainWindow.xaml.cs` — UI, sidebar, news tab, icon
- `Services/ModManagerService.cs`, `Services/GameBananaApiClient.cs` — Download/install logic
- `Resources/ThemeDark.xaml`, `Resources/ThemeLight.xaml` — Theme definitions
- Settings/user config file — Store theme and news read state

## Verification
- [ ] Registering the protocol creates the correct registry keys
- [ ] Clicking a `ssf2mm:` link in browser launches the app and triggers mod install
- [ ] News tab appears, shows articles, and unread badge updates as expected
- [ ] Taskbar icon is crisp and large (256x256 PNG, embedded and referenced via pack URI)
- [ ] Theme switching works and persists across restarts

## Decisions
- Protocol name: `ssf2mm:` (can be changed if GameBanana requests)
- News articles stored locally (JSON or similar)
- Theme system uses ResourceDictionaries for easy expansion

## Further Considerations
- [ ] Add menu option to “Register 1-Click Handler” for users without admin rights
- [ ] Use named mutex or IPC for single instance/protocol forwarding
- [ ] Allow for future theme expansion (custom colors, more themes)
