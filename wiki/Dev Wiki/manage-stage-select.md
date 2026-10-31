---
title: Manage stage select
summary: Make Mods opens stage select and the stage editor.
order: 10
group: Stages
---

# Manage stage select

## Info

| Manage stage select |
| --- |
| Button | Make Mods `BtnOpenStageEditor_Click` |
| Launch screen | `sssedit` |
| Menu key | `LaunchScreen.menuKey` → `sss` |
| Editor event | `stage_editor.open` |
| Log | `sssedit menu ready` |
| Related | [Manage character select](manage-character-select.md) |

Manage stage select writes `expansions/state/launch.json` with `screen` `sssedit`. The stream opens stage select. The editor enters live edit when that grid is ready. [Manage character select](manage-character-select.md) does the same for Versus character select.

## Contents

1. [How it opens](#how-it-opens)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Rebuild](#rebuild)
5. [Check](#check)
6. [References](#references)

## How it opens

`BtnOpenStageEditor_Click` calls `ShellDirectLaunchService.Launch` with `ScreenSssEdit`. `MmDirectMod` (in the `manager_bridge` pack) leaves any screen other than `css` or `sss` to `Ssf2StreamMod`. The stream stores `sssedit`.

`LaunchScreen.menuKey("sssedit")` returns `sss`. `tryOpenMenuSoft` uses that key, so the stage-select aliases open `vsStageMenu`. When the menu opens, `notifyStageEdit` logs `sssedit menu ready` and emits `stage_editor.open`.

`StageEditorMod.onStageEditOpen` calls `enterLiveEdit` on `stage_editor.open`. The editor sprite is not on the display list, so its own `ENTER_FRAME` does not run. If the menu is not on screen yet, the wait is `m_host.stage`. The log is `sssedit open tries=`.

A game that is already running receives the same `sssedit` screen on `applyLaunch`. The command is not rewritten to `sss`.

## How it works

| Function | What it does | Log |
| --- | --- | --- |
| `LaunchScreen.menuKey` | Maps `sssedit` to `sss`. Other screens stay as written. | |
| `tryOpenMenuSoft` | Opens the menu for `menuKey`, so `sssedit` opens stage select. | `opened menu vsStageMenu` |
| `notifyStageEdit` | Emits `stage_editor.open` after the menu opens. | `sssedit menu ready` |
| `onStageEditOpen` | Calls `enterLiveEdit` on the host event. A late menu waits on `m_host.stage`. | `sssedit open tries=` |

## Files

| What | Path |
| --- | --- |
| Screen map | `data/ssf2mm/ssf2_stream/src/LaunchScreen.as` |
| Open path | `Ssf2StreamMod.tryOpenMenuSoft`, `notifyStageEdit` |
| Editor | `SSF2 Shell/src/mods/menu_editor/src/StageEditorMod.as` |
| Button | `MakeModsWindow.BtnOpenStageEditor_Click` |
| Fixture | `data/ssf2mm/ssf2_stream/fixture` |

## Rebuild

Compile `Built-in: manager_bridge` and `Built-in: menu_editor` from Make Mods → **Compile** or SSF2 Stream **Compile**. The paths are on [Where things are](where-things-are.md). A running game keeps the old swf until a full quit.

## Check

Full quit, then Make Mods → Open stage select editor. The log has `opened menu vsStageMenu`, `sssedit menu ready`, and `sssedit open tries=`. The edit chrome is on the grid.

The fixture, from `ssf2_stream/fixture`: `run-fixture.ps1`. `%APPDATA%\LaunchScreenFixture\Local Store\launch-screen-fixture.txt` says `OK`.

## References

1. [Manage character select](manage-character-select.md) — the same open path for Versus character select.
2. [Where things are](where-things-are.md) — the compile command, and that a running game keeps the old swf.
3. Wiki — the Info, Contents, and References shape.
