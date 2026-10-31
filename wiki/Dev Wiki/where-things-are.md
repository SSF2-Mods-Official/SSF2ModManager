---
title: Where things are
summary: Compile a mod or engine2, and where the wiki pages live.
order: 10
group: Build
---

# Where things are

## Info

| Where things are |
| --- |
| Engine edits | `SSF2 Shell Base/patches/engine2`, then `shellbase.py build` |
| Engine output | `SSF2 Shell Base/Build/data/engine/engine2.swf` |
| Built-in pack source | `SSF2 Shell Base/shell/src/mods/<pack id>` |
| Built-in pack output | `SSF2 Shell Base/shell/data/ssf2mm/<pack id>/mod.swf`, copied to `Build` |
| Optional mod source | `SSF2 Shell Base/Build/expansions/mods` |
| Runtime state | `SSF2 Shell Base/Build/expansions/state` |
| Player data | `SSF2 Shell Base/Build/expansions/user` |
| Wiki | `runtime/shell-dev/docs` |
| Related | Wiki |

Make Mods → **Modding Tools** decompiles a SWF and compiles a folder or a mod. Linkage names in a played character SWF are listed with JPEXS FFDec. Palette Maker has `shell/src/mods/palette_maker/rebuild.bat` for a double-click compile. An engine change is a patch under `SSF2 Shell Base/patches/engine2`. `tools/shellbase.py build` writes `Build/data/engine/engine2.swf`. Mod source is `SSF2 Shell Base/shell/src/mods`. `tools/build-mod.ps1` compiles it.

## Contents

1. [How to compile](#how-to-compile)
2. [How it works](#how-it-works)
3. [Where the files are](#where-the-files-are)
4. [Essential packs](#essential-packs)
5. [Runtime state](#runtime-state)
6. [Content packs](#content-packs)
7. [References](#references)

## How to compile

An engine edit is a file under `patches/engine2`. From `SSF2 Shell Base`, `python tools/shellbase.py build --vanilla <install> --n2f <Next2Flash>` writes `Build/data/engine/engine2.swf`. The class list stays the one in `patches/engine2/base.json`. Player updates always rebuild from vanilla; see [Shell rebuild](shell-rebuild.md).

One mod: `tools/build-mod.ps1 <id>` compiles `shell/src/mods/<id>` with that folder's `asconfig.json`. Palette Maker: double-click `shell/src/mods/palette_maker/rebuild.bat`, or run `tools/build-mod.ps1 palette_maker`. The swf lands in `shell/data/ssf2mm/<id>/mod.swf` and is copied to `Build/data/ssf2mm/<id>/mod.swf`. If `Next2Flash/toolchain/aflex_lite` has no AIR descriptor template, `build-mod.ps1` writes a minimal one under `templates/air`. The compile reads `shell/src/engine/resources`. A game that is already open keeps the old swf until a full quit.

## How it works

`engine` and `ssf2mm` stay `.swf`. Every other data folder is stored as `.ssf`. Convert to FLA shows those folders too.

| Function | What it does | Log line |
| --- | --- | --- |
| `ListDataFolderSwfs` | Reads every `.swf` and `.ssf` under `data`. The folder list is the first path segment. | No log. The check is the folder names. |
| `SwfPackage.EnsurePlainSwf` | Writes a plain SWF when the chosen file is an `.ssf`. A file that is already a SWF stays where it is. | No log. FFDec then opens that plain SWF. |
| `PressorService.CreateModdableBuildAsync` | Pressor copies a game folder and places each `data` `.ssf` at the category and `file` name stored in mappings. | `mappings {count}` |

## Where the files are

| What | Path |
| --- | --- |
| Game | The launcher folder is `SSF2 Shell Base/Build` |
| Engine SWFs | `SSF2 Shell Base/Build/data/engine/engine1.swf` and `engine2.swf` |
| Engine edits | `SSF2 Shell Base/patches/engine2`. `build` applies them. There is no decompiled tree to edit |
| Compiler library | `SSF2 Shell Base/shell/src/engine/resources` |
| Essential packs | `Build/data/ssf2mm/<id>` holds `mod.json` and `mod.swf`. Source is `shell/src/mods/<id>`. |
| Optional mods | `SSF2 Shell Base/Build/expansions/mods` |
| Content packs | `SSF2 Shell Base/Build/expansions/packs/<id>/pack.json` (registry `data/ssf2mm/index.json`) |
| This wiki | `runtime/shell-dev/docs` |
| Coding folder | `runtime/shell-dev` (`asconfig.json`). Visual Studio Code runs SSF2 Stream from `.vscode/extensions/ssf2-shell.ssf2-stream-0.8.21`. That copy's `resolveDevFolder` looks beside `SSF2 Shell` at `runtime/shell-dev`. |
| Super Debug | `launchSsf2Debug` passes `resolveAneExtdir`, the junction with no space. ADL drops `-extdir` when the path contains a space, and `titleMenu` then throws Error #3500 on `updateActivity`. The debug session opens the SSF2 Game output. |

`shell/src/engine/resources` holds `engine1-code.swc` and the link reports a mod compile uses. Do not ship that SWC with the game.

## Essential packs

`discoverModsIn("data/ssf2mm", true)` marks those packs essential. `isModEnabled` stays true, and the F1 row says the pack stays on. `discoverModsIn("expansions/mods", false)` does not. The log is `[ModHost] found essential` or `[ModHost] found mod`. `enabledByDefault` starts an optional pack. An `info.json` field `enabled: false` hides it. Built-in packs have no `info.json`. Manage Mods reads `mod.json` and saves settings there (`ExpansionModsService.BuiltInEntry`).

| Pack | Entry class | Parts |
| --- | --- | --- |
| `expansions` | `ExpansionsMod` | `EventExpansionsMod`, `ExpansionLoaderMod`, `RealExpansionsMod` |
| `menu_editor` | `MenuEditorMod` | `CssEditorMod`, `StageEditorMod` |
| `manager_bridge` | `ManagerBridgeMod` | `Ssf2StreamMod` |
| `debug` | `DebugMod` | `DebugConsoleMod`, `DebugHitboxMod` |
| `menu_background`, `mod_menu`, `music_player`, `palette_maker`, `rumble` | one class each | — |

A merged entry calls `onBoot`, `onBootStart` and `onBootEnd` on each part in order. The log is `[expansions] boot parts=3` (the same for each merged pack). Event names such as `css_editor.open` and patch keys such as `expansion_loader.vectorIcon` are not pack ids, so they kept their names.

## Runtime state

`ShellWorkspaceService.StateDirectory` is `expansions/state`. It holds the files the manager and the running game pass to each other: `boot.json`, `launch.json`, `cmd.json`, `catalog.json`, `pointer.json`, `cursor.json`, `stage.json`, `now-playing.json`, `alert.json`, `last_error.txt`, `air_trace.txt`, and `game.log`. The game side is `Ssf2StreamMod.bridgeDir`, the folder that contains that log. `data/ssf2mm/state` is not read or written. The player's own data lives in `expansions/user` (`ShellWorkspaceService.UserStateDirectory`, game side `ssf2mm.shared.UserStateFile`): `controller_profiles.json`, `playlists.json`, `menu-music.json` and `songcache.json`. `index.json` and `live.json` stay in `data/ssf2mm`; they are the content pack registry (see [Content packs](#content-packs)).

## Content packs

A content pack is a character, stage or music record that the manager installs. `ContentPackService.PackDir` is `expansions/packs/<id>`. `index.json` lists the ids, and `ApplyPacksToMappings` writes them into mappings and `live.json`. A music pack is only `pack.json`; its song is `data/sound/<id>.swf`. A character or stage pack has `content.swf`, and its mappings `file` is `expansions/packs/<id>/content.swf` (`ContentFileRelativeFromData`).

| Function | What it does | Log line |
| --- | --- | --- |
| `Resource.as` (engine2 `changed/`) | A `file` starting with `expansions/packs/` loads from the game folder. The check runs before the `ssf2mm/` and `expansions/` → `expansions/character/` rules. | No log. The check is the character or stage loading. |
| `GamePrepareService.AddExpansionPacksBranch` | Adds that branch when the manager patches `Resource.as`. | No log. `ContentPackLocationTests` checks it. |
| `ContentPackService.MigrateLegacyPacks` | Moves `data/ssf2mm/packs/<id>` into `expansions/packs` and rewrites `ssf2mm/packs/` file fields. Runs from `EnsureRoot`. | No log. The old folder is gone afterwards. |

`expansions/` is gitignored, so pack records do not ship with ssf2-shell-game.

## References

1. Wiki — **Open Docs** and the page shape.
2. Change the game's code — decompile and compile engine2.
3. Make a small mod — copy the template and turn it on.
4. [Launcher shell](launcher-shell.md) — the window title, Manage Mods card text, and News.
5. Pressor — compress and uncompress `.ssf`, and create a moddable build.
