---
title: Clone old assets
summary: A clone can keep the source character's stock, costumes, and Classic / All-Star art.
order: 100
group: Characters
---

# Clone old assets

## Info

| Clone old assets |
| --- |
| Clone | Keep using clone's assets |
| Import | One list for every clone in the batch |
| Meta | `css_meta.json` `oldassets`, `oldassetid` |
| Stock and modes | `tryGetLibraryMCPatch` |
| Costumes | `resolveCostumeStatsName` |
| Related | [Character import](character-import.md) |

A clone can keep the source character's stock, costumes, and Classic / All-Star art. The choice is written on the expansion's `css_meta.json`. [Character import](character-import.md) asks when a chosen file is a clone.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

Clone character has the checkbox **Keep using clone's assets**. Import character reads each file. A file whose linkage is already playable, or whose linkage is that id plus a number from 2 up, is a clone. One dialog lists those files, and **Check all** checks every row. A checked row writes `oldassets` true and `oldassetid` set to the source id, such as `mario`. The game then loads that id's stock, costume list, character-select portrait, and Classic / All-Star art.

| Function | What it does | What you see |
| --- | --- | --- |
| `CloneAssetSource` | Returns the source id when the linkage is already playable. `donkeykong2` returns `donkeykong` when `donkeykong` is playable. A linkage that matches neither stays empty. | `vegeta` with only `mario` playable returns nothing. |
| `ApplyOldAssets` | Sets `oldassets` to true and `oldassetid` to the source id on `css_meta.json`. | `"oldassets": true` and `"oldassetid": "mario"`. |
| `PickOwner` | Returns the preferred window when that window is visible. Otherwise returns another visible window. A window that has never been shown is skipped. | Make Mods is hosted, so the list is owned by the launcher. |
| `BtnRunImportCharacter_Click` | Opens one list of the clones in the batch. `PickOwner` owns that list. Each checked row is passed to `ImportIntoShellAsync`. Cancel imports nothing. | Two Mario files can be checked separately. |
| `SetRowsChecked` | Sets every row in the list to the Check all value. | Check all leaves every clone on the source character's art. |
| `tryGetLibraryMCPatch` | Rewrites a stock, icon, character-select, Classic, or All-Star library name to the source id. | `[ResourceManager] oldassets mario2_stock -> mario_stock` |
| `resolveCostumeStatsName` | Costume lookup uses `oldassetid`. | `[ResourceManager] oldassets costumes mario2 -> mario` |
| `getExpansionCharSelectMC` | Character select, stock, and Classic portraits use the source id's library clip. | `[ResourceManager] oldassets mario2_charselect -> mario_charselect` |
| `writeExpansionCssSidecars` | A later meta write keeps `oldassets` and `oldassetid`. | The two fields are still in `css_meta.json` after boot. |

## Files

| What | Path |
| --- | --- |
| `CloneAssetSource`, `ApplyOldAssets` | `src/Services/ShellCharacterCloneService.cs` |
| Clone checkbox | `src/MainWindow.xaml` and `src/Views/MakeModsWindow.xaml` |
| Import list, `PickOwner` | `src/Views/KeepCloneAssetsWindow.xaml` |
| Game lookup | `SSF2 Shell/src/mods/expansions/src/com/mcleodgaming/ssf2/util/ExpansionResources.as` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "CloneAssetSource|KeepOldAssets"
```

`CloneAssetSource` says `mario` is a clone of `mario`, `donkeykong2` is a clone of `donkeykong`, and `vegeta` is not a clone. `ApplyOldAssets` writes `oldassets` true and `oldassetid` `mario`. In the game, after a full quit, the log line is `[ResourceManager] oldassets mario2 id=mario`.


## References

1. [Character import](character-import.md) — the import that asks this question when a file is a clone.
2. Where things are — the expansion folder and the expansions pack.
