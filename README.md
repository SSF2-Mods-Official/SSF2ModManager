# SSF2 Mod Manager

A Windows mod manager for **Super Smash Flash 2**. Browse and install GameBanana mods with backups and conflict detection, keep several SSF2 builds with their own mod lists, and set up **SSF2 Shell**, a moddable build of SSF2 Beta 1.4.0.1.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- Browse, search and install SSF2 mods from GameBanana
- Enable, disable, update and uninstall mods, with automatic backups
- Conflict detection when mods share files
- Multiple SSF2 builds, each with its own mod list
- 1-Click installs from GameBanana (`ssf2mm:` links)
- Guided setup for SSF2 Shell and an optional debugging environment
- Themes, English and Spanish, in-app news

## Install

1. Download `SSF2ModManager-win-x64.zip` from [Releases](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases).
2. Extract it anywhere and run `SSF2ModManager.exe`. No .NET install is needed.

Requires Windows 10 or 11 (64-bit). SSF2 Shell also needs your own copy of SSF2 Beta **1.4.0.1**.

## Setup

The setup window opens on first launch. Run it again any time from **Settings → Run setup**.

1. **Next2Flash**: downloads the [Next2Flash](https://github.com/SSF2-Mods-Official/Next2Flash) toolchain, or uses one you already have.
2. **SSF2 Beta 1.4.0.1**: pick your game folder. Other versions are rejected.
3. **SSF2 Shell**: downloads the latest [SSF2 Shell Base](https://github.com/SSF2-Mods-Official/SSF2-Shell-Base) patches and builds SSF2 Shell next to the manager. When a new release comes out, the manager offers a rebuild that keeps your mods and settings.
4. **Debugging (optional)**: installs VS Code if no editor is found, adds the ActionScript and SSF2 Stream extensions, and configures them.

## Using it

1. Go to **Browse Mods**, pick a mod, and click **Install**.
2. Manage mods on the **Installed** page.
3. To mod another SSF2 folder, use **Settings → Add Version**.

Settings and mod data are stored in `%AppData%\SSF2ModManager\`. Player data for SSF2 Shell (playlists, controller profiles) is in `SSF2 Shell\expansions\user`.

## 1-Click install

The manager registers the `ssf2mm:` link type on first launch (per user, no admin). GameBanana links look like:

```text
ssf2mm:https://gamebanana.com/mmdl/1708765,Mod,679407
```

The parts are the download link, the item type, and the mod ID used to look up the mod.

If links don't open the manager, run `SSF2ModManager.exe --register-protocol` from the copy you want to use. `--diagnostics` shows the current registration.

## Command line

```powershell
SSF2ModManager.exe --help
SSF2ModManager.exe --version
SSF2ModManager.exe --check-updates
SSF2ModManager.exe --install="ssf2mm:https://..."
SSF2ModManager.exe --enable="Mod Name"
SSF2ModManager.exe --disable="Mod Name"
SSF2ModManager.exe --uninstall="Mod Name"
```

Add `--verbose` for detailed logging. The log is `%AppData%\SSF2ModManager\ssf2mm-debug.log`.

## Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/SSF2-Mods-Official/SSF2ModManager.git
cd SSF2ModManager
dotnet build SSF2ModManager.sln -c Release
dotnet test SSF2ModManager.sln
.\scripts\run.bat
```

| Folder | Contents |
|--------|----------|
| `src/` | WPF app, tests, helper tools |
| `tools/ssf2-stream-extension/` | SSF2 Stream VS Code extension |
| `scripts/` | `run.bat` plus release, dependency, content and cleanup scripts ([details](scripts/README.md)) |

`.\scripts\run.bat publish` builds the release zip at `dist\SSF2ModManager-win-x64.zip`. The app version comes from `version.txt` and `src/Services/AppInfo.cs`.

## Disclaimer

Mods change game files. The manager makes backups, but keep a copy of your original SSF2 folder. SSF2 Mod Manager is a fan project and is not affiliated with McLeodGaming.

## License

MIT. See [LICENSE](LICENSE).
