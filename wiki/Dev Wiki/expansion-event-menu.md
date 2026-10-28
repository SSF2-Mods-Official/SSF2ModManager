---
title: Expansion event menu
summary: Grab or taunt on the event menu switches the list between vanilla events and expansion packs.
order: 20
group: Events
---

# Expansion event menu

## Info

| Expansion event menu |
| --- |
| Menu | `ExpansionEventMenu` |
| Toggle | `listToggleHelper` |
| Log | `[event_expansions] listToggle mode=` |
| Related | [Events](events.md) |

Grab or taunt on the event list switches between the vanilla events and the expansion packs. The scroll wheel moves that list one row at a time. The [replay list](replay-browser.md) uses the same scroller, and its wheel does the same.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

`EventMenuHand.checkControls` calls `listToggleHelper` when grab or taunt is pressed. `listToggleHelper` rumbles with `ModHost.triggerMenuRumble` and calls `setListMode`. The mode flips between `vanilla` and `expansions`.

| Function | What it does | Log line |
| --- | --- | --- |
| `listToggleHelper` | Flips the list and rumbles the pad that pressed the button. | `[event_expansions] listToggle mode=expansions` |
| `setListMode` | Shows that list. | The same line, after the mode has changed. |
| `paintEventRows` | Fills each row. It does not call `EventButton.makeEvents`, and it does not turn `mouseChildren` on. The click target stays the row. An installed download row is dimmed and `mouseEnabled` is false. | `Error #2094` at `EventButton/button_CLICK` when `makeEvents` is used |
| `markInstalled` | A download row whose GameBanana mod id is already in an event pack's `info.json` is named with `(installed)` and is not a download. | `[event_expansions] download owned n=` |
| `checkUpdates` | Compares each installed `gbFileId` with the latest GameBanana file. A newer file adds one "Yes — download N updates" row. | `[event_expansions] browseDownloads ok` |
| `onListWheel` | One notch moves the event list one row. The window is 11 rows. Scroll up moves toward the first row. | |
| `ReplaySelectMenu.onListWheel` | One notch moves the replay list one row. The window is 5 rows. | |

## Files

| What | Path |
| --- | --- |
| `listToggleHelper` | `SSF2 Shell Base/shell/src/mods/expansions/src/com/mcleodgaming/ssf2/menus/ExpansionEventMenu.as` |
| `ReplaySelectMenu.onListWheel` | `SSF2 Shell Base/shell/src/mods/replay_browser/src/com/mcleodgaming/ssf2/menus/ReplaySelectMenu.as` |

## Check

Open Download new events. A pack that is already installed is dimmed, its name ends with `(installed)`, and selecting it does not start a download. `game.log` shows `[event_expansions] download owned n=` with that count. If GameBanana has a newer file, the first row is `Yes — download N updates`. Selecting that row replaces the installed files.


## References

1. [Events](events.md) — making an event pack in the Mod Manager.
2. [Replay browser](replay-browser.md) — the replay list that uses this scroller.
