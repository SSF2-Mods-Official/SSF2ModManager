---
title: Stream debug
summary: Launch Debug finds asconfig.json beside the game folder or in the Mod Manager repo next to it.
order: 120
group: Build
---

# Stream debug

## Info

| Stream debug |
| --- |
| Lookup | `resolveDevFolderAt` |
| Project | `runtime/shell-dev/asconfig.json` |
| Related | Wiki |

Launch Debug needs the shell-dev project that sits beside the game, or in the Mod Manager repo next to Shell Base.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [References](#references)

## How it works

`resolveDevFolderAt` checks the debug folder, then `runtime/shell-dev` next to it. When the debug folder is `SSF2 Shell Base/Build`, it also checks each sibling of that repo for `SSF2 Mod Manager Release/runtime/shell-dev/asconfig.json`. The Launch SSF2 button uses this lookup only when Super debug is checked. Cursor loaded the copy in `.vscode/extensions/ssf2-shell.ssf2-stream-0.8.21`.

| Function | What it does | Log line |
| --- | --- | --- |
| `resolveDevFolderAt` | Returns the folder that contains `asconfig.json`. | `SSF2: asconfig.json not found beside <debug folder>` |
| `Remove-ExtDirLink` | Removes `.as3mxml-unpackaged-anes` before F5 links it at the unpacked ANEs. A real folder is deleted. A junction is removed with `rmdir`. | `AIR debug overlay ready.` |
| `activate` | Opens the stream tab on startup when `launchOnVscStartup` is on. `debugLaunch.js` loads `ssf2mmPaths.js` from the same `out` folder. | `Activating extension ssf2-shell.ssf2-stream failed` when that file is missing |
| `runPrepareCopy` | Runs `prepare_air_debug.ps1` with `prepareLaunchSpec` before Launch SSF2. Lines go to Output: SSF2 Prepare. | `[ssf2-stream] prepare exit=` |
| `resolveBuildRoot` | Tails `data/ssf2mm/game.log` from the debug folder when that folder is the game. Otherwise it uses `LauncherInstallFolder` in the Mod Manager settings. The watch starts before the SWF session returns. | `[ssf2-stream] Watching` |
| `GameProfilerSession.Port` | The Mod Manager profiler listens on 7945. Port 7935 is the SWF debugger. | `Address already in use: bind` when the profiler still holds 7935 |
| Launch SWF `program` | F5 runs ADL on `SSF2 Shell Base/Build/SSF2-app.xml`. The ANE folder is the junction `.as3mxml-unpackaged-anes` at the repo root, which has no spaces. `toolchain/aflex_lite/runtimes` is a junction to the Next2Flash Release runtimes, so ADL can start the player. | `Process exited with code 3` |
| `compile_debug.ps1` | Compiles Bootstrap with `--debug` from `SSF2 Shell/data/engine/booter`. The player then connects to the debugger. F5 does not reveal Output: SSF2 Game. | `Process exited with code 0` and `without establishing connection` when the SWF has no debug info |

## Files

| What | Path |
| --- | --- |
| `resolveDevFolderAt` | `tools/ssf2-stream-extension/src/devFolder.ts` |
| `asconfig.json` | `SSF2 Mod Manager Release/runtime/shell-dev/asconfig.json` |
| `Remove-ExtDirLink` | `SSF2 Mod Manager Release/runtime/shell-dev/tools/prepare_air_debug.ps1` |
| `prepareLaunchSpec` | `tools/ssf2-stream-extension/src/prepareLaunch.ts` |


## References

1. Wiki — where these pages live.
