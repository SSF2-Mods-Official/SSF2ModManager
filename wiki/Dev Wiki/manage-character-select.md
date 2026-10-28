---
title: Manage character select
summary: Make Mods opens Versus character select and the roster editor.
order: 10
group: Characters
---

# Manage character select

## Info

| Manage character select |
| --- |
| Button | Make Mods `BtnManageCss_Click` |
| Launch screen | `cssedit` |
| Menu key | `LaunchScreen.menuKey` → `versus_css` |
| Editor event | `css_editor.open` |
| Log | `cssedit menu ready` |
| Related | Where things are |

Manage Character Select writes `data/ssf2mm/state/launch.json` with `screen` `cssedit`. The stream opens Versus character select. The editor enters live edit when that grid is ready.

## Contents

1. [How it opens](#how-it-opens)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Rebuild](#rebuild)
5. [Check](#check)
6. [References](#references)

## How it opens

`BtnManageCss_Click` calls `ShellDirectLaunchService.Launch` with `ScreenCssEdit`. `MmDirectMod` (in the `manager_bridge` pack) reads `launch.json` and leaves any screen other than `css` or `sss` to `Ssf2StreamMod`. The stream has already stored `cssedit`.

`LaunchScreen.menuKey("cssedit")` returns `versus_css`. `isCssScreen` uses that key, so `tickWaitHome` sets phase `open_css` and `showCssMenu` opens `vsMenu`. When the grid is ready, `notifyCssEdit` logs `cssedit menu ready` and emits `css_editor.open`.

`CssEditorMod.onCssEditOpen` calls `enterLiveEdit` on `css_editor.open`. The editor sprite is not on the display list, so its own `ENTER_FRAME` does not run. If the menu is not on screen yet, the wait is `m_host.stage`. The log is `cssedit open tries=`.

A game that is already running receives the same `cssedit` screen on `applyLaunch`. The command is not rewritten to `css`.

## How it works

| Function | What it does | Log |
| --- | --- | --- |
| `LaunchScreen.menuKey` | Maps `cssedit` to `versus_css`. Other screens stay as written. | |
| `isCssScreen` | Uses `menuKey`, so `cssedit` takes the character-select open path. | `home settled — opening CSS/mode select` |
| `showCssMenu` | Opens `vsMenu` for `versus_css`. | `opened CSS vsMenu (reset+show)` |
| `notifyCssEdit` | Emits `css_editor.open` after the grid is ready. | `cssedit menu ready` |
| `onCssEditOpen` | Calls `enterLiveEdit` on the host event. A late menu waits on `m_host.stage`. | `cssedit open tries=` |
| `onCssMenuShow` | Lays the saved grid onto Versus, Training, Classic, All-Star, Online, and the event character menu. | `[css_editor] show ` |
| `CssGridLayout.pinReadyPrompt` | After the grid fit, calls `updateGameIsReady` and moves a ready prompt whose stage y is outside 8–320 back onto the screen. | `[css_editor] ready prompt name=` |

## Files

| What | Path |
| --- | --- |
| Screen map | `data/ssf2mm/ssf2_stream/src/LaunchScreen.as` |
| Open path | `Ssf2StreamMod.tickWaitHome`, `notifyCssEdit` |
| Editor | `SSF2 Shell/src/mods/menu_editor/src/CssEditorMod.as` |
| Button | `MakeModsWindow.BtnManageCss_Click` |
| Fixture | `data/ssf2mm/ssf2_stream/fixture` |

## Rebuild

Compile `Built-in: manager_bridge` and `Built-in: menu_editor` from Make Mods → **Compile** or SSF2 Stream **Compile**. The paths are on Where things are. A running game keeps the old swf until a full quit.

## Check

Full quit, then Make Mods → Manage Character Select. The log has `opened CSS vsMenu`, `cssedit menu ready`, and `cssedit open tries=`. The edit chrome is on the grid.

The fixture, from `ssf2_stream/fixture`: `run-fixture.ps1`. `%APPDATA%\LaunchScreenFixture\Local Store\launch-screen-fixture.txt` says `OK`.


## References

1. Where things are — the compile command, and that a running game keeps the old swf.
2. [Character select portrait](character-select-portrait.md) — the player-box portrait on the grid this button opens.
3. Wiki — the Info, Contents, and References shape.
