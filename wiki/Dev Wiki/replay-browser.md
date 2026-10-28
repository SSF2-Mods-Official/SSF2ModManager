---
title: Replay browser
summary: The vault replay list, extra folders, and the import that starts a replay.
order: 40
group: Match
---

# Replay browser

## Info

| Replay browser |
| --- |
| Menu | `ReplaySelectMenu` |
| Folders | `ReplayFiles.getReplayFolders` |
| Import | `ReplayFiles.sanitizeMatchSettingsForImport` |
| Log | `[replay_browser]` |
| Related | [Expansion event menu](expansion-event-menu.md) |

The replay menu lists `.ssfrec` files from the default replay folder and any folders added beside it.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [References](#references)

## How to use it

Opening the menu paints each row from the file name. Hovering a row reads that one file for the stage line and the thumbnail.

The buttons sit under the Vault title and to the left of the preview. **Manage Folders** and **Filter** open a centered list with a dark background. Each folder row has **View** and **Remove**. The default folder is not removed. **Rename** or **Remove**, then a row. **Cancel** leaves that mode. A marked row title is red and starts with `✓`. **Confirm** deletes the marked files and the `.png` next to each. The instruction is only on the bottom line.

When a replay ends, the next screen is this menu. **Space BG** on the stage select bar is the only place the menu space art is used as a match background.

## How it works

`makeEvents` does not read the replay files. `updateThumbnail` and `updateDescText` call `parseReplayMeta` for the hovered file. The result stays in `m_metadataCache` under `ReplayFiles.metaKey`.

`replay_folder.cfg` in application storage holds `{"extra":[...]}`. A plain path from an older build is one extra folder. `getReplayFolders` always includes the default folder, then each extra path that is not already listed.

`launchReplayFile` calls `sanitizeMatchSettingsForImport` before `CharacterSelectMenu.initReplay`. That copies `MatchSettings` through the `GameSettings` field list, `PlayerData` through the `PlayerSetting` field list, and `ItemSettingsObj` through `frequency` and `items`. A stage id that is empty, the string `null`, or unknown to `ResourceManager` is replaced with `battlefield`. A character id `ResourceManager` does not know is `exist` false. A `musicOverride` that is not a resource is removed. If no slot still has a character, `launchReplayFile` does not call `initReplay`.

| Function | What it does | Log line |
| --- | --- | --- |
| `ReplaySelectMenu.makeEvents` | Sets each row title from the file name. It does not call `parseReplayMeta`. | |
| `ReplaySelectMenu.loadReplayFiles` | Lists `.ssfrec` files in every folder from `getReplayFolders`. | `[replay_browser] folders n=` |
| `ReplayFiles.addExtraFolder` | Appends one folder path and writes `replay_folder.cfg`. | |
| `ReplayFiles.removeExtraFolder` | Drops one extra path. The default folder is not removed. | |
| `ReplaySelectMenu.renameBtn_CLICK` | Arms rename. The next row click edits that name. | |
| `ReplaySelectMenu.onRenameKeyBlock` | While the name field is open, `KEY_EVENT` is cancelled so menu keys do not type over it. | |
| `ReplaySelectMenu.placeActionButtons` | Puts the buttons under the vault bar. The block's right edge is 40px left of the preview art. | |
| `ReplaySelectMenu.showManageFolders` | Opens the folder list in the center. Each row has View and Remove. | |
| `ReplaySelectMenu.showFilterList` | Opens the type list in the center, on a dark plate. | |
| `ReplaySelectMenu.applyActionHint` | Writes the instruction on the bottom line only. | |
| `ReplaySelectMenu.setRemoveMark` | A marked title is red and starts with `✓`. Clearing the mark restores the white title. | `[replay_browser] remove pick` |
| `ReplaySelectMenu.cancelBtn_CLICK` | Leaves rename or remove and clears the red titles. | |
| `ReplaySelectMenu.replay_CLICK` | Stops the news button from dispatching another click on the same row. | |
| `ReplaySelectMenu.show` | Sets `m_backgroundID` to `space` before the stock backdrop paint. | |
| `ReplaySelectMenu.applyModeFilter` | Keeps the rows whose mode label matches the filter. An empty filter shows every file. | `[replay_browser] filter mode=` |
| `ReplayBrowserMod.onMenuShow` | After a replay, a vault open is replaced with this menu. | `[replay_browser] return to replay menu` |
| `ReplayFiles.sanitizePlayersForImport` | Drops player keys that are not fields on `PlayerSetting`. A slot with no character, or a character id `ResourceManager` does not know, is `exist` false. | `[replay_browser] drop empty player` |
| `ReplayFiles.sanitizeMatchSettingsForImport` | Drops unknown match keys. An empty, `null`, or missing stage becomes `battlefield`. A `musicOverride` that is not a resource is removed. | `[replay_browser] drop missing stage` |
| `ReplaySelectMenu.launchReplayFile` | Starts the replay after sanitize. If no slot has a character, it stays on this menu. | `[replay_browser] replay has no characters` |
| `MenuBackgroundMod.ensureAndPaint` | Skips a menu backdrop id `ResourceManager` does not know, logs that id once, clears the saved id, and paints the stock backdrop. | `[menu_background] skip unknown stage` |

## Files

| What | Path |
| --- | --- |
| `ReplaySelectMenu` | `SSF2 Shell Base/shell/src/mods/replay_browser/src/com/mcleodgaming/ssf2/menus/ReplaySelectMenu.as` |
| `ReplayFiles` | `SSF2 Shell Base/shell/src/mods/replay_browser/src/ssf2mm/replay/ReplayFiles.as` |
| `ensureAndPaint` | `SSF2 Shell Base/shell/src/mods/menu_background/src/MenuBackgroundMod.as` |


## References

1. [Expansion event menu](expansion-event-menu.md) — the replay list uses that scroller, and the wheel moves it one row.
