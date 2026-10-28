---
title: Character import
summary: Import keeps the display name. A taken linkage becomes the next free id, donkeykong then donkeykong2.
order: 27
---

# Character import

## Info

| Character import |
| --- |
| Import | `ShellCharacterCloneService.ImportIntoShellAsync` |
| Identity | `CharacterCloneService.ReadImportIdentity` |
| Taken linkage | `ShellCharacterCloneService.NextFreeLinkage` |
| Display name | `css_meta.json` `displayName` |
| Related | Where things are |

`ImportIntoShellAsync` reads the file's linkage and display name. When that linkage is already playable, `NextFreeLinkage` picks the next free id and `CharacterCloneService.CloneAsync` renames the linkage. The display name stays the one from the file.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

The character browse dialog selects one or more SWF files. `BtnRunImportCharacter_Click` calls `ImportIntoShellAsync` once per path, in order. `ReadImportIdentity` reads `linkage_id` and `displayName` from that file. `NextFreeLinkage` compares that linkage to roster ids and expansion folder ids. A free linkage is copied under that id. A taken linkage is renamed before the expansion folder is written. `css_meta.json` `displayName` is the file's display name.

| Function | What it does | What you see |
| --- | --- | --- |
| `NextFreeLinkage` | Returns the linkage when it is free. When it is taken, returns that id plus the next free number, starting at 2. | `donkeykong` taken and `donkeykong2` free returns `donkeykong2`. Both taken returns `donkeykong3`. |
| `SelectedImportPaths` | Keeps each chosen path once, in the order the dialog returned them. | A blank path is dropped. The same path twice is kept once. |
| `BtnRunImportCharacter_Click` | Calls `ImportIntoShellAsync` for each path from `SelectedImportPaths`. A clone in that batch is listed by [Clone old assets](clone-old-assets.md) before the copy. The next file sees folders written by the previous file. | Two `donkeykong` files, with `donkeykong` already playable, import as `donkeykong2` then `donkeykong3`. |
| `ImportIntoShellAsync` | A taken linkage is renamed with `CloneAsync`. The display name passed through is the file's display name. | Donkey Kong stays Donkey Kong. The folder id is `donkeykong2`. |
| `BuildCloneReplacementPlan` | Renames the fighter id, the `{id}_fla` package, the Ext class, and exports in this SWF whose names start with that id. Renames another class in this SWF only when `CollectTakenSymbolNames` already has that name. A stats string whose class is not in this SWF stays. | `zamus` becomes `zamus2`. `zamus_head` becomes `zamus2_head`. `ZamusExt` becomes `zamus2Ext`. `MetroidSymbolCSS` stays. `samus_dr0` stays unless the shell already exports it. |
| `SwfPackage.IsStage` | A file with `backgrounds` is a stage. Import character refuses it. | Boxing Ring is refused. Use [Stage import](stage-import.md). |

## Files

| What | Path |
| --- | --- |
| `ImportIntoShellAsync` | `src/Services/ShellCharacterCloneService.cs` |
| `CollectTakenSymbolNames` | `src/Services/ShellCharacterCloneService.cs` |
| `SymbolClassNames` | `src/Services/SwfPackage.cs` |
| `ReadImportIdentity` | `src/Services/CharacterCloneService.cs` |
| `SelectedImportPaths` | `src/Services/ShellCharacterCloneService.cs` |
| Import button | `src/Views/MakeModsWindow.xaml.cs` `BtnBrowseCharacterFile_Click` and `BtnRunImportCharacter_Click` |
| `IsStage` | `src/Services/SwfPackage.cs` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "NextFreeLinkage|SelectedImportPaths|Zamus2_RenamesIdExports"
```

`NextFreeLinkage` says `donkeykong` is `donkeykong2` when `donkeykong` is taken, and `donkeykong3` when `donkeykong2` is taken too. `SelectedImportPaths` keeps two different paths and drops a repeated path.

## References

1. Where things are — the shell folder the import writes into.
2. [Clone old assets](clone-old-assets.md) — the list that keeps a clone on the source character's stock, costumes, and Classic / All-Star art.
3. [Stage import](stage-import.md) — a file with `backgrounds` is imported there.
