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

- **Windows 10/11 (64-bit)**
- **Release zip:** nothing else to install
- **Building from source:** [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

## Quick start

### Download (recommended)

Grab `SSF2ModManager-win-x64.zip` from [GitHub Releases](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases). Extract and run **`SSF2ModManager.exe`** — **no .NET install required**.

The release is a single portable executable (~70–90 MB). No .NET install required.

### From source (developers)

Requires [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```powershell
git clone https://github.com/SSF2-Mods-Official/SSF2ModManager.git
cd SSF2ModManager
dotnet build SSF2ModManager.sln -c Release
.\src\bin\Release\net8.0-windows\SSF2ModManager.exe
```

Or run `scripts\run.bat` (builds Release and launches).

### Repository layout

```
SSF2ModManager/
├── .github/          CI workflows
├── scripts/          build, clean, publish, run
├── src/              WPF app (all source code)
├── tests/            unit tests
├── README.md
├── LICENSE
└── SSF2ModManager.sln
```

### Clean up local build clutter

After building, `src/bin` and `src/obj` accumulate generated files. They are gitignored:

```powershell
.\scripts\clean.ps1
```

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
.\src\bin\Release\net8.0-windows\SSF2ModManager.exe --diagnostics
```

You should see `Registered (points to current executable)` and the command path.

**2. Re-register if needed**

```powershell
.\src\bin\Release\net8.0-windows\SSF2ModManager.exe --register-protocol
```

**3. Test from PowerShell** (replace with a real GameBanana download URL + mod ID)

```powershell
.\scripts\verify-protocol.ps1 -ArchiveUrl "https://files.gamebanana.com/mmdownload/..." -ModType "Character" -ModId 12345
```

Or manually:

```powershell
Start-Process ".\src\bin\Release\net8.0-windows\SSF2ModManager.exe" -ArgumentList 'ssf2mm:https://files.gamebanana.com/mmdownload/...,Character,12345'
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
.\scripts\clean.ps1    # optional — remove old bin/obj
.\scripts\publish.ps1
```

Output zip: `dist\SSF2ModManager-win-x64.zip`

### What’s in the release zip?

| File | Purpose |
|------|---------|
| `SSF2ModManager.exe` | Portable app + .NET 8 runtime + all dependencies (single file) |
| `README.txt` | Quick start for end users |

**~2 files, ~60–70 MB.** No separate DLLs, no runtime install.

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
