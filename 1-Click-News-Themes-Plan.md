# Plan: SSF2 Mod Manager – 1-Click, News, Themes, and UI Improvements

**TL;DR:**
Implement 1-click installer protocol (URI registration + argument handling), update Sandbox resource description, add a News tab with unread notification, fix taskbar icon size, and add theming support.

---

## Steps

### Phase 1: 1-Click Installer Integration

#### Custom URI Protocol Registration
- Add code to register `ssf2mm:` protocol in Windows registry (on first launch or via a menu option).
- Registry path: `HKEY_CURRENT_USER\Software\Classes\ssf2mm`
- Set default value, `URL Protocol`, and command to launch the app with `%1` as argument.

#### Command-Line Argument Handling
- In `App.xaml.cs`, override `OnStartup` to parse command-line arguments.
- If argument matches `ssf2mm:` schema, extract download URL, mod type, and mod ID.
- Trigger mod download/install flow automatically (with user confirmation for security).

#### Single Instance Handling (Optional, but recommended)
- Prevent multiple instances, or forward protocol URL to running instance.

---

### Phase 2: UI/UX Improvements

#### Update Sandbox Resource Description
- In the Resources tab, update the Sandbox entry to:
  > “Sandbox is a popular SSF2 Beta modpack from version 1.3.”

#### Add News Tab
- Add a new sidebar tab: “News”.
- News page displays articles (title, date, content).
- Add the first article:
  - **Title:** “Introducing SSF2 Mod Manager”
  - **Content:** “Welcome to the first public release of SSF2 Mod Manager! Manage, install, and update your SSF2 mods with ease. 1-Click GameBanana integration coming soon.”
- Store read/unread state (e.g., in user settings or a local file).
- Show a red notification badge with the count of unread articles on the Resources tab.

#### Taskbar Icon Size
- Use a larger PNG (e.g., 128x128 or 256x256) for the WPF `Window.Icon` property.
- Ensure the icon is embedded as a resource and referenced with a pack URI.

#### Implement Themes
- Add a theme system (at least “Dark” and “Light”).
- Store user’s theme preference (e.g., in settings).
- Allow switching themes via a menu or settings page.
- Refactor XAML to use DynamicResource for all colors/brushes.
- Provide two ResourceDictionary files: `ThemeDark.xaml` and `ThemeLight.xaml`.

---

## Relevant files
- `App.xaml`, `App.xaml.cs` — Startup, theme loading, protocol handling
- `MainWindow.xaml`, `MainWindow.xaml.cs` — UI, sidebar, news tab, icon
- `Services/ModManagerService.cs`, `Services/GameBananaApiClient.cs` — Download/install logic
- `Resources/ThemeDark.xaml`, `Resources/ThemeLight.xaml` — Theme definitions
- Settings/user config file — Store theme and news read state

---

## Verification
- Registering the protocol creates the correct registry keys.
- Clicking a `ssf2mm:` link in browser launches the app and triggers mod install.
- News tab appears, shows articles, and unread badge updates as expected.
- Taskbar icon is crisp and large.
- Theme switching works and persists across restarts.

---

## Decisions
- Protocol name: `ssf2mm:` (can be changed if GameBanana requests)
- News articles stored locally (JSON or similar)
- Theme system uses ResourceDictionaries for easy expansion

---

## Further Considerations
- For protocol registration, consider a menu option to “Register 1-Click Handler” for users without admin rights.
- For single instance, use a named mutex or IPC to forward protocol URLs.
- For themes, allow for future expansion (custom colors, more themes).

---

Let me know if you want to proceed with this plan or want any adjustments before implementation!
