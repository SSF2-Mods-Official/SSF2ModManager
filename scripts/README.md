# Scripts

Top level: **`run.bat`** only. Everything else lives in a folder.

| Folder | Scripts | Purpose |
|--------|---------|---------|
| `release/` | `sync-dev.ps1`, `sync-release-exe.ps1`, `publish.ps1` | Dev / ship / zip |
| `deps/` | `fetch-native-tools.ps1`, `fetch-aflex-lite.ps1`, `package-ssf2-stream.ps1` | Toolchains |
| `content/` | `generate-news-index.ps1` | `news-index.json` |
| `clean/` | `clean.ps1` | Delete local build artifacts |

## Build entry points

| Command | What it does | When to use |
|---------|----------------|-------------|
| `.\scripts\run.bat` (double-click) | **`sync-dev.ps1`**: fast framework-dependent single-file → `SSF2 Mod Manager Release\`, refresh `runtime\` content, start | Daily coding |
| `.\scripts\run.bat ship` | **`sync-release-exe.ps1`**: self-contained ~200 MB exe (no .NET install needed) | Test what users get |
| `.\scripts\run.bat publish` | Full zip under `dist\` | GitHub release |
| `.\scripts\run.bat bin` | Build/start `src\bin\Release\` only | Escape hatch |

## What `sync-dev` updates under `runtime\`

| Path | Source | Notes |
|------|--------|-------|
| `Languages`, `News`, `Themes` | `src\…` | Always robocopy (newer files) |
| `extensions\ssf2-stream.vsix` | `tools\ssf2-stream-extension` | Rebuilt only if sources newer than VSIX |
| `ffmpeg` | `src\ffmpeg` | Copied only if missing |
| FFDec, JRE, Flex lite | Next2Flash `toolchain` | Not copied. Sibling repo or `NEXT2FLASH_HOME` |

FLA cleanup (organize, layer names, folders, box merge) runs inside the manager during clone/export. `runtime\jsfl` and `runtime\tools\FlaStepWizard.exe` are not shipped.
