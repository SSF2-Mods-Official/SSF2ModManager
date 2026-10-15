# SSF2 Mod Manager

A Windows desktop mod manager for **Super Smash Flash 2**. Browse GameBanana mods, install them with backups and conflict detection, manage multiple SSF2 builds, and use 1-Click installs from GameBanana.

It also sets up **SSF2 Shell**, a moddable build of SSF2 Beta 1.4.0.1 made on your PC from your own copy of the game and the patches in [SSF2 Shell Base](https://github.com/SSF2-Mods-Official/SSF2-Shell-Base).

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- Browse and search SSF2 mods on GameBanana
- Install, enable, disable, update, and uninstall mods
- Multiple SSF2 builds (versions) with separate mod lists
- Automatic file backups before deploying mods
- Conflict detection when mods share `.ssf` files
- `info.json` metadata support inside mod archives
- **1-Click install** via `ssf2mm:` protocol links
- Guided setup for SSF2 Shell and an optional debugging environment
- UI themes, English and Spanish localization
- In-app news and tutorial walkthrough

## Requirements

- Windows 10/11 (64-bit)
- SSF2 Beta **1.4.0.1** installed, for SSF2 Shell
- Building from source: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Quick start

### Download (recommended)

Get `SSF2ModManager-win-x64.zip` from [Releases](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases), extract it, and run **`SSF2ModManager.exe`**. No .NET install is needed.

### Setup

On first launch the setup window walks through four steps. You can run it again from **Settings → Run setup**.

1. **Next2Flash**: downloads the [Next2Flash](https://github.com/SSF2-Mods-Official/Next2Flash) toolchain (Python, Java, FFDec, AIR SDK), or points to one you already have.
2. **SSF2 Beta 1.4.0.1**: pick your game folder. Only 1.4.0.1 is accepted; it is checked by the `SSF2.swf` hash.
3. **SSF2 Shell**: downloads the latest SSF2 Shell Base `.ssfpatch` files and builds SSF2 Shell next to the manager. Later launches offer a rebuild when a new release is out; your mods and settings are kept.
4. **Debugging (optional)**: installs VS Code if no editor is found, adds the ActionScript and SSF2 Stream extensions, and writes the editor settings into the Shell folder.

### From source

```powershell
git clone https://github.com/SSF2-Mods-Official/SSF2ModManager.git
cd SSF2ModManager
dotnet build SSF2ModManager.sln -c Release
.\scripts\run.bat
```

### Repository layout

```
SSF2ModManager/
├── src/          # WPF app, tests, CLI tools
├── tools/        # SSF2 Stream VS Code extension
├── scripts/      # run.bat + release/ deps/ content/ clean/
└── SSF2ModManager.sln
```

Clean build clutter with `scripts\clean\clean.ps1`.

### First launch

1. Finish setup, or open **Settings** → **Add Version** and point to an SSF2 folder.
2. Go to **Browse Mods**, pick a mod, and click **Install**.
3. Manage installed mods on the **Installed** page.

Mod data is stored in `%AppData%\SSF2ModManager\`.
## 1-Click install (`ssf2mm:`)

GameBanana can launch the manager with a URL like:

```text
ssf2mm:{downloadUrl},{ModelName},{modId}
```

Example (Retro Arcade Stage | Sploder):

```text
ssf2mm:https://gamebanana.com/mmdl/1708765,Mod,679407
```

GameBanana sends `/mmdl/{fileId}` links for 1-Click install. The file ID in the path (`1708765`) is matched against the mod API.

| Field | Value | Meaning |
|-------|--------|---------|
| Download URL | `https://gamebanana.com/mmdl/1708765` | Direct file link (`/mmdl/{fileId}`) |
| ModelName | `Mod` | GameBanana ItemType / ModelName (usually `Mod`, not the submission category) |
| Mod ID | `679407` | Submission ID — used to fetch full mod info from the API |

The middle field is optional fallback metadata if the API lookup fails. The mod ID is what matters for install.

The app registers the `ssf2mm:` protocol on first launch (per-user, no admin required).

### Verify the protocol works

**1. Check registration**

```powershell
.\src\bin\Release\SSF2ModManager.exe --diagnostics
```

You should see `Registered (points to current executable)` and the command path.

**2. Re-register if needed**

```powershell
.\src\bin\Release\SSF2ModManager.exe --register-protocol
```

**3. Test from PowerShell** (replace with a real GameBanana download URL + mod ID)

```powershell
Start-Process ".\src\bin\Release\SSF2ModManager.exe" -ArgumentList '"ssf2mm:https://gamebanana.com/mmdl/1708765,Mod,679407"'
```

**4. Test from browser**

After registration, paste a full `ssf2mm:...` link into the address bar or click one on GameBanana. Windows should launch SSF2 Mod Manager and prompt to install.

**5. Registry check (optional)**

```powershell
Get-ItemProperty "HKCU:\Software\Classes\ssf2mm\shell\open\command"
```

### Troubleshooting

| Problem | Fix |
|---------|-----|
| Browser does nothing | Run `--register-protocol`; ensure only one app instance isn't blocking |
| Wrong executable opens | `--unregister-protocol` then `--register-protocol` from the build you want |
| Install fails | Add an SSF2 build in Settings first |
| Need logs | Run with `--verbose`; log file: `%AppData%\SSF2ModManager\ssf2mm-debug.log` |

## Publishing a release

```powershell
.\scripts\clean\clean.ps1     # optional — remove old bin/obj
.\scripts\run.bat publish     # or: .\scripts\release\publish.ps1
```

See `scripts/README.md` for how `run.bat` (dev) / `run.bat ship` / `run.bat publish` differ.

Output zip: `dist\SSF2ModManager-win-x64.zip`.

### What’s in the release zip?

The portable exe with its `runtime` folder (languages, news, themes, ffmpeg). No game files: setup builds SSF2 Shell on the user's PC.

**App version** (sidebar, e.g. `v1.0.0`) comes from `src/Services/AppInfo.cs` — not GameBanana.

## CLI (automation)

```powershell
SSF2ModManager.exe --help
SSF2ModManager.exe --version
SSF2ModManager.exe --check-updates
SSF2ModManager.exe --install="ssf2mm:https://..."
SSF2ModManager.exe --uninstall="Mod Name"
SSF2ModManager.exe --enable="Mod Name"
```

## Development

```powershell
dotnet test SSF2ModManager.sln
dotnet build SSF2ModManager.sln -c Release
```

## Disclaimer

Mods modify game files. SSF2 Mod Manager creates backups, but you install mods at your own risk. Always keep a copy of your vanilla SSF2 folder.

## License

MIT — see [LICENSE](LICENSE).
