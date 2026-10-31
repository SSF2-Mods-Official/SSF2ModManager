---
title: Built-in mods
summary: The twelve AS3 mods that ship with the Shell, what each one hooks, and the contract a cleanup has to keep.
order: 30
group: Build
---

# Built-in mods

## Info

| Built-in mods |
| --- |
| Source | `GitHub/SSF2 Shell Base/shell/src/mods/<id>` (source of truth) |
| Copies | `SSF2 Shell/src/mods`, mirrored by `work/mods_sync.ps1`. `Build/` gets only the compiled `mod.swf` files. |
| Output | `data/ssf2mm/<id>/mod.swf` |
| Build | `runtime/shell-dev/tools/build_mods.ps1 -Release` |
| Shared code | `shell/src/mods/_shared` (skipped by `build_mods.ps1`, on each mod's `source-path`) |
| Related | Shell Base, [Launcher and shell](launcher-shell.md), [Costume recolor](costume-recolor.md) |

Every mod is one `mod.swf` that `ModHost` loads at boot and that the Mod Menu can disable and enable live.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Mods](#mods)
4. [Engine contract](#engine-contract)
5. [Shared code](#shared-code)
6. [Rebuild](#rebuild)
7. [Check](#check)
8. [Open questions](#open-questions)
9. [References](#references)

## How to use it

Edit the mod under `SSF2 Shell Base/shell/src/mods/<id>`. Run `work/mods_sync.ps1` in the Shell Base repo. It mirrors the source to the dev tree, compiles every mod in release, and copies each `mod.swf` into `shell/data/ssf2mm` and `Build/data/ssf2mm`. Then boot the Shell, toggle the mod in the Mod Menu, and play a match.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `ModHost` boot | Creates each mod's `entryClass` and calls `onBoot(host)`, then `onBootStart(host)`. | `[ModHost] boot complete — mods=<n>` |
| `ModHost.applyLiveEnableState` | Any toggle in the Mod Menu calls `onBootEnd()` on every boot mod, wipes all patches, hooks, key listeners, class replacements and session flags, unloads every mod SWF and loads the enabled ones again. `removeHook`/`unpatch` in `onBootEnd` is not needed. Stage listeners, timers, `setTimeout` and ENTER_FRAME pumps a mod added itself are not wiped, so `onBootEnd` has to remove them or the old copy keeps running. | `[ModHost] setModEnabled <id>=<bool> — applying live`, `[ModHost] live enable state applied` |
| `host.patch(id, fn)` | Replaces an engine call. Patched functions run as `fn.apply(null, args)`. | none |
| `host.addHook(id, fn)` | Runs `fn(ctx)` on an engine event (`MENU_SHOW`, `MATCH_START`, `RUMBLE` and the rest in `ModHooks`). | none |
| `host.emit` / `host.on` | Mod-to-mod events such as `mod_menu.register`. | none |

## Mods

| Id | Entry class | What it does |
| --- | --- | --- |
| `camera_background` | `CameraBackgroundMod` | Shield on Stage Select locks another stage's camera backgrounds for the next match (`applyBackgroundStage` patch, session flag `backgroundStage`, stamped into replays). Compiles `MenuBgStageStub` from `menu_background/src` to run the overlay stage's scripted layers. |
| `controller_profiles` | `ControllerProfilesMod` | Nametag list on the Character Select edit button and a Profiles bar on Controls. Each name keeps its keyboard and controller bindings. Data: `expansions/user/controller_profiles.json` (the Mod Manager's Configure Launch tags read the same file). |
| `debug` | `DebugMod` | Backtick console (`ShellDebugConsole` replaces `DebugConsole`) and the hitbox test menu. |
| `expansions` | `ExpansionsMod` | Loads character, stage and event packs (`ExpansionResources.dispatch` behind `expansion_loader.rm`), GameBanana event downloads. |
| `manager_bridge` | `ManagerBridgeMod` | File protocol with the SSF2 Mod Manager (`Ssf2StreamMod`), attract mode, boot intros, crash reports. |
| `menu_background` | `MenuBackgroundMod` | Live stage behind the menus. |
| `menu_editor` | `MenuEditorMod` | Character Select editor (`CssEditorMod`) and Stage Select editor (`StageEditorMod`). |
| `mod_menu` | `ModMenuMod` | F1 menu that lists mods and the entries other mods register. |
| `music_player` | `MusicPlayerMod` | Jukebox, menu and match music, visualizers, online song packs. See [Music player](music-player.md). |
| `palette_maker` | `PaletteMakerMod` | Costume editor, color sheets, FX recolor in matches. |
| `replay_browser` | `ReplayBrowserMod` | Vault > Replays opens `ReplaySelectMenu` (folder list with stage and character previews) through the `VAULT_REPLAY` hook. `sanitizeMatchSettingsForImport` strips unknown `MatchSettings` keys before `importSettings` and restores `backgroundStage`. |
| `rumble` | `RumbleMod` | Controller rumble from the `RUMBLE` hook. |

## Engine contract

These names are read by the engine, by other mods, or by the Mod Manager. A cleanup keeps them.

| Kind | Names |
| --- | --- |
| Patches | `applyBackgroundStage`, `combinePaletteBlocks`, `applyCostumeMcName`, `advanceEffectFrame`, `isFragileTimelineClip`, `mergeCostumeList`, `palette.*` (48 doors), `playMusic`, pitch-shift patches, `api.spawnCharacter`, `applyMenuBackground`, `acceptCharacterGuid`, `getLibraryMC`, `loadRealExpansionUi`, `expansion_loader.rm`, `expansion_loader.vectorIcon`, `event_expansions.*`, `debug_hitbox.menu` |
| Class replacements | `EventMenu` to `ExpansionEventMenu`, `DebugConsole` to `ShellDebugConsole` |
| Called by name | `PaletteMakerMenu.warmUniqueSpriteCache*`, `DiscordCostumePresence.rememberLastActivity`, `HitBoxTestMenu`, `ExpansionEventOrchestrator.getSummary`, the getters on `MenuBgStageStub` and `MenuBgPlatformStub` |
| Mod events | `mod_menu.register`, `mod_menu.unregister`, `mod_menu.open`, `mod_menu.ready`, `css_editor.open`, `stage_editor.open`, `menu_background.preview`, `menu_background.clearPreview`, `camera_background.beginPick`, `camera_background.cancelPick` |
| Save keys | `css_editor_rows`, `css_editor_pages`, `css_editor_seen_expansions`, `stage_editor_grid`, `stage_editor_page_count`, `stage_editor_training`, `stage_editor_seen_expansions`, `menu_background_stage_id`, `menu_music_id`, `menu_music_loop`, `mod_menu.reopen`, `mod_menu.hotkey`. Files: `replay_folder.cfg` (app storage, replay_browser) |
| Bridge files | `expansions/state/`: `launch.json`, `boot.json`, `cmd.json`, `pointer.json`, `now-playing.json`, `catalog.json`, `cursor.json`, `stage.json`, `alert.json`, `last_error.txt`. `menu-music.json` is `expansions/user/` |

## Shared code

`shell/src/mods/_shared/src` is on every mod's `source-path` (`"source-path": ["src", "../_shared/src"]`). Only the classes a mod imports are compiled into its `mod.swf`. `ModHost.loadModSwf` loads each mod into its own child `ApplicationDomain`, so every mod has a private copy of a shared class and its statics; two mods never share one cache or one timer.

| Class (`ssf2mm.shared`) | What it does |
| --- | --- |
| `EngineRefs` | `cls(name)` looks up an engine class by name, caches it, and returns null instead of throwing. `ssf2(rel)` adds the `com.mcleodgaming.ssf2.` prefix. `gameLog(line)` writes one `game.log` line through `ModHost.appendGameLog`. |
| `JsonFile` | `read`, `write`, `readText`, `writeText`, `writeBytes`, `parse`. A missing or broken file reads as null; a write creates the parent folder. Nothing throws. |
| `GameBananaApi` | GameBanana category list, file lookup, download, unzip into the pack folder, `info.json` stamp. `expansions` (`GameBananaClient`) and `music_player` (`MusicOnline`) are thin subclasses that pass their log prefix. |
| `ZipReader` | `entries(zip, log)` (null when not a zip), `read(zip, entry, log)` for stored and deflated entries, `commonTopFolder`. |
| `SvgPath` | `draw(g, d, matrix)` traces an SVG path (M L H V C S Q T A Z, absolute and relative). `VectorIconSvg` uses it. |
| `LucideIcons` | 26 embedded Lucide SVGs (ISC, `icons/LICENSE-lucide.txt`) and `drawEmbeddedSvg`. `music_player` and `palette_maker` (`LucideIconRenderer`) draw from it. |
| `HsvWheel` | `rgbToHsv`, `hsvToRgb`, `parseHex` (-1 on bad input), `formatHex`, point-to-hue/sat/val, hue strip and SV square drawing. Every color picker uses it. |
| `VideoOverlay` | Full-screen video with skip: `play(stage, file, w, h, onDone, opts)`, `skip()`, `dispose()`. `onDone(reason)` fires once after cleanup with `complete`, `stop`, `watchdog`, `skip` or `fatal:<code>`. The boot intro and attract mode use it. |

Editor-only helpers stay in the mod: `menu_editor/src` has `GridRows`, `EditorSave` (`clearKeys` writes only keys that hold a value, because each `saveExtSet` runs `SaveData.saveGame()`), `OpenWaiter` (`stop()` from `onBootEnd`), `EditBar` and `EditorEngine`; `mod_menu/src/ModMenuScroll` holds the scroll math; `palette_maker` has `PopupBase` for its five popups.

Fixtures (AIR apps that write `OK` or `FAIL` lines; run each folder's `run-*.ps1`): `_shared/fixture` (`HsvWheelFixture`, `SharedFixture` for `JsonFile` and `ZipReader`, `MusicOverrideCodecFixture`), `palette_maker/fixture` also has `CostumeNetIndexFixture` (see [Online sync](online-sync.md)), `menu_editor/fixture` (`GridRows`), `mod_menu/fixture` (scroll), `palette_maker/fixture` (`combinePaletteBlocksStock` first-seen order), `manager_bridge/fixture` (launch screen), `music_player/fixture` (`MusicCacheFilter.keep`), `controller_profiles/fixture` (`ProfileStore` state file, legacy migration, `normalize`). `adl` prints "error while loading initial content" on exit every time; the result file is what counts.

## Rebuild

`powershell -NoProfile -ExecutionPolicy Bypass -File work\mods_sync.ps1` in the Shell Base repo. A failed compile prints `[build_mods] FAILED <id>` and the full output is in `work/mods_build.log`. One mod alone: `work\mod_build.ps1 -Id <id>` (log `work/mod_build_<id>.log`).

`"warnings": true` is on for the eight mods that compile only their own source (`camera_background`, `controller_profiles`, `manager_bridge`, `menu_background`, `menu_editor`, `mod_menu`, `music_player`, `rumble`). `build_mods.ps1` stops on any compiler warning, because the warning goes to stderr under `$ErrorActionPreference = "Stop"` and the log just ends after `Loading configuration file`. To see the warning, run `python <Next2Flash>/app/n2f.py asconfig --project asconfig.json` in the mod's dev-tree folder. `debug`, `expansions`, `palette_maker` and `replay_browser` put the decompiled engine on their source path, whose warnings would stop the build, so they keep `"warnings": false`.

Each mod uses one log line per failed critical step, through the mod's own log: `fail step=<method> err=<error>` (`menu_editor`), `<step> failed: <error>` (`manager_bridge` `runStep`, `palette_maker` `PaletteGuard`), `[ResourceManager] dispatch <op> failed` (`expansions`), `[debug] command <name> failed` (`debug`), `[music_player] ... failed` (`music_player`), `[camera_background] <step> failed: <error>`, `[controller_profiles] <step> failed: <error>`, `[replay_browser] <step> failed: <error>`. Frame loops log a repeated identical error once.

## Check

1. `mods_sync.ps1` prints `[build_mods] OK` for all twelve packs.
2. The Shell boots and `game.log` shows no new errors.
3. Each mod is disabled, enabled and disabled again in the Mod Menu without a second copy firing.
4. One match plays to the results screen.
5. `node --test` passes under each mod's `tests/` folder.
6. Each `fixture/run-*.ps1` writes `OK`.
7. `game.log` has no `failed` lines from the step guards.
8. Only one AIR copy of the game runs at a time. If `Build` is open, a launch of the dev Shell exits at once and writes nothing.


## Open questions

- `DiscordCostumePresence` uploads a costume preview PNG to `litterbox.catbox.moe` for the Discord status. It is not opt-in.

## References

1. Shell Base: how `Build/` is made and where `shell/data/ssf2mm` comes from.
2. [Launcher and shell](launcher-shell.md): how the Mod Manager starts the Shell and talks to `manager_bridge`.
3. [Costume recolor](costume-recolor.md): palette rules `palette_maker` uses.
4. [Music player](music-player.md): `music_player` class layout and log lines.
5. [Online sync](online-sync.md): what music_player, camera_background and palette_maker share in an online room.
