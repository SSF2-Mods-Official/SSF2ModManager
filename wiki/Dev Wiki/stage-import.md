---
title: Stage import
summary: Import reads the stage id from the file. A taken id becomes the next free one. A clone can keep the source preview.
order: 20
group: Stages
---

# Stage import

## Info

| Stage import |
| --- |
| Import | `ShellStageCloneService.ImportIntoShellAsync` |
| Identity | `ShellStageCloneService.ReadStageImportIdentity` |
| Taken id | `ShellCharacterCloneService.NextFreeLinkage` |
| Preview | `stagepreview.png` beside the source SWF |
| Related | [Character import](character-import.md) |

`ImportIntoShellAsync` reads the stage id from the file. When that id is already playable, [Character import](character-import.md) `NextFreeLinkage` picks the next free id. The display name stays the one from the file. A clone can keep the source stage's `stagepreview.png`.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

The stage browse dialog selects one or more SWF files. `BtnRunImportStage_Click` calls `ImportIntoShellAsync` once per path, in order. `SwfPackage.IsStage` is true when the file contains `backgrounds`. `ReadStageImportIdentity` reads the `stage_` clip. A file with no stage clip uses the first plain `id` that does not start with `bgm_`. `NextFreeLinkage` compares that id to stage ids already on disk. A free id is copied under that id. A taken id is renamed before the folder is written.

Clone stage has the checkbox **Keep using clone's assets**. Import stage asks once, in one list, when a chosen file is a clone of a playable stage. A checked row copies that stage's `stagepreview.png`. An unchecked row uses a preview beside the imported file, or a generated label when the file has none.

| Function | What it does | What you see |
| --- | --- | --- |
| `SwfPackage.IsStage` | True when the file contains `backgrounds`. `statsName` or `jumpSpeed` in that file does not make it a character. | Boxing Ring imports as a stage. |
| `ReadStageImportIdentity` | The stage id is the first `stage_` clip other than `stage_effects`. A file with no stage clip uses the first plain `id` that does not start with `bgm_`, then a `_bg` name. Display name is `displayName` when that string sits between the record's `id` and `backgrounds`. Otherwise the display name is the id. | `stage_boxingring` returns linkage `boxingring`. A HUD `displayName` of `toUpperCase` stays out. |
| `NextFreeLinkage` | Returns the id when it is free. When it is taken, returns that id plus the next free number, starting at 2. | `battlefield` taken and `battlefield2` free returns `battlefield2`. |
| `PreviewBesideSwf` | Returns `stagepreview.png` in the same folder as the SWF when that file exists. | A stock stage with no png returns nothing. |
| `BtnRunImportStage_Click` | Opens one list of the clones in the batch. Each checked row is passed to `ImportIntoShellAsync` with the source preview. Cancel imports nothing. | Two Battlefield files can be checked separately. |
| `ImportIntoShellAsync` | A taken id is renamed with `CloneAsync`. The display name passed through is the file's display name. `keepSourcePreview` copies the source stage's png and does not write the generated label. | Last Destination stays Last Destination. The folder id is `LastDestination2`. |
| `CloneIntoShellAsync` | The checkbox copies the source stage's png. Unchecked writes the generated label. | `[progress] Keeping battlefield's preview.` |

## Files

| What | Path |
| --- | --- |
| `ImportIntoShellAsync`, `ReadStageImportIdentity`, `PreviewBesideSwf` | `src/Services/ShellStageCloneService.cs` |
| `IsStage`, `IsCharacter` | `src/Services/SwfPackage.cs` |
| Import button | `src/Views/MakeModsWindow.xaml.cs` `BtnBrowseStageFile_Click` and `BtnRunImportStage_Click` |
| Clone checkbox | `src/Views/MakeModsWindow.xaml` `ChkStageKeepOldAssets` |
| Import list | `src/Views/KeepCloneAssetsWindow.xaml` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "ReadStageImportIdentity|PreviewBesideSwf|SwfPackage_TellsACharacterFromAStage"
```

`ReadStageImportIdentity` says `boxingring` for a file whose first `id` is `bG` and whose clip is `stage_boxingring`. A HUD `displayName` of `toUpperCase` stays out. It still says `LastDestination` when the file has no stage clip and its `id` is `LastDestination`. `PreviewBesideSwf` returns `stagepreview.png` beside the SWF and returns nothing when that png is absent. `SwfPackage.IsStage` is true for `statsName` plus `backgrounds`, and for `jumpSpeed` plus `backgrounds`.


## References

1. [Character import](character-import.md) — the same next-free id and the one list for a batch of clones.
2. [Where things are](where-things-are.md) — the shell folder the import writes into.
