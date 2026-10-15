# SSF2 Mod Manager

A Windows launcher and mod manager for **Super Smash Flash 2**. It builds **SSF2 Shell**, a moddable copy of SSF2 Beta 1.4.0.1, then lets you download, make and manage mods for it.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- Guided setup: builds SSF2 Shell from your SSF2 Beta 1.4.0.1 and keeps it up to date
- Download Mods: browse and install SSF2 mods from GameBanana (mods that mention 0.9b are hidden unless you ask)
- 1-Click installs from GameBanana (`ssf2mm:` links)
- Manage Mods: turn SSF2 Shell mods on or off and change their settings
- Make Mods: events, costumes, music, boot intros, stages and characters, plus packaging and file tools
- Configure: start the game straight into a menu, match or event, and pick the boot intro and music
- Debug: launch SSF2 Shell with the debugger in VS Code (optional)
- Themes, English and Spanish, in-app news and docs

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

1. **Play** starts SSF2 Shell. **Configure** sets what it opens into.
2. **Download Mods**: pick a mod and click **Download**. Tick **Show 9B mods** to include mods made for 0.9b, which likely won't work.
3. **Manage Mods**: turn mods on or off. Changes apply the next time the game starts.
4. **Make Mods**: create or import your own content.
5. To use a different SSF2 Shell folder, click **Change** next to the folder.

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

SSF2 Shell is built in its own folder, and your SSF2 Beta 1.4.0.1 folder is only read. SSF2 Mod Manager is a fan project and is not affiliated with McLeodGaming.

## License

MIT. See [LICENSE](LICENSE).
