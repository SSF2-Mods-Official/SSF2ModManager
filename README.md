# SSF2 Mod Manager

A Windows desktop mod manager for **Super Smash Flash 2**. Browse GameBanana mods, install with backups and conflict detection, manage multiple SSF2 builds, and use 1-Click installs from GameBanana.

![.NET 8](https://img.shields.io/badge/.NET-8.0-blue) ![Platform](https://img.shields.io/badge/platform-Windows-lightgrey) ![License](https://img.shields.io/badge/license-MIT-green)

## Features

- Browse and search SSF2 mods on GameBanana
- Install, enable, disable, update, and uninstall mods
- Multiple SSF2 builds (versions) with separate mod lists
- Automatic file backups before deploying mods
- Conflict detection when mods share `.ssf` files
- `info.json` metadata support inside mod archives
- **1-Click install** via `ssf2mm:` protocol links
- 12 UI themes, English + Spanish localization
- In-app news and tutorial walkthrough

## Requirements

- **Windows 10/11**
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) (or use the self-contained publish build)

## Quick start

### From source

```powershell
git clone https://github.com/SSF2-Mods-Official/SSF2ModManager.git
cd SSF2ModManager
dotnet build -c Release
.\bin\Release\net8.0-windows\SSF2ModManager.exe
```

Or double-click `run.bat` (builds Release and launches).

### First launch

1. Open **Settings** → **Add Version** and point to your SSF2 folder.
2. Go to **Browse Mods**, pick a mod, and click **Install**.
3. Manage installed mods on the **Installed** page.

Mod data is stored in `%AppData%\SSF2ModManager\`.

## 1-Click install (`ssf2mm:`)

GameBanana can launch the manager with a URL like:

```text
ssf2mm:https://files.gamebanana.com/...,Character,12345
```

The app registers the `ssf2mm:` protocol on first launch (per-user, no admin required).

### Verify the protocol works

**1. Check registration**

```powershell
.\bin\Release\net8.0-windows\SSF2ModManager.exe --diagnostics
```

You should see `Registered (points to current executable)` and the command path.

**2. Re-register if needed**

```powershell
.\bin\Release\net8.0-windows\SSF2ModManager.exe --register-protocol
```

**3. Test from PowerShell** (replace with a real GameBanana download URL + mod ID)

```powershell
.\scripts\verify-protocol.ps1 -ArchiveUrl "https://files.gamebanana.com/mmdownload/..." -ModType "Character" -ModId 12345
```

Or manually:

```powershell
Start-Process ".\bin\Release\net8.0-windows\SSF2ModManager.exe" -ArgumentList 'ssf2mm:https://files.gamebanana.com/mmdownload/...,Character,12345'
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
.\scripts\publish.ps1
```

Output zip: `dist\SSF2ModManager-win-x64.zip`

### What’s in the release zip?

A normal **.NET 8 framework-dependent** app — not a single `.exe`. You need the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) installed once on the PC.

| File / folder | Purpose |
|---------------|---------|
| `SSF2ModManager.exe` | App launcher |
| `SSF2ModManager.dll` | Application code |
| `Newtonsoft.Json.dll`, `SharpCompress.dll`, `Markdig.dll`, `ZstdSharp.dll` | NuGet dependencies (JSON, archives, news markdown) |
| `Languages/` | English + Spanish UI strings |
| `Themes/` | 12 UI themes |
| `*.runtimeconfig.json`, `*.deps.json` | .NET runtime metadata (required) |

That’s **~20 files** total — expected for this type of build. A single-file `.exe` is possible but much larger and still extracts dependencies at runtime.

**App version** is shown in the sidebar (e.g. `v1.0.0`) and comes from `Services/AppInfo.cs` / the project version — not from GameBanana.

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
dotnet test
dotnet build -c Release
```

See `SSF2ModManager.Tests/README.md` for test details.

## Disclaimer

Mods modify game files. SSF2 Mod Manager creates backups, but you install mods at your own risk. Always keep a copy of your vanilla SSF2 folder.

## License

MIT — see [LICENSE](LICENSE).
