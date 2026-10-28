---
title: Clone a character
summary: Clone a fighter already in the Shell, then open the FLA the clone writes. Convert to FLA does the same library pass on a data file.
order: 55
group: Characters
---

# Clone a character

## Info

| Clone a character |
| --- |
| Clone | Make Mods → Clone character |
| Convert | Make Mods → Modding Tools → Convert to FLA |
| Rename | `CharacterCloneService.CloneAsync` |
| Folder | `ShellCharacterCloneService.CloneIntoShellAsync` |
| FLA | `JpexsCliService.ExportFlaAsync` with `characterPretty` |
| Related | [Character import](character-import.md), [Convert a stage to FLA](convert-stage-fla.md) |

Clone character copies a fighter that is already playable, gives it a new id, and writes an expansion folder plus a CS6 FLA. Convert to FLA does not rename anything. It turns one file under `data/` into a FLA under `expansions/decompiled`.

## Contents

1. [How to clone](#how-to-clone)
2. [Renaming](#renaming)
3. [Expansion folder](#expansion-folder)
4. [Clone FLA](#clone-fla)
5. [Convert to FLA](#convert-to-fla)
6. [Character library](#character-library)
7. [How it works](#how-it-works)
8. [Files](#files)
9. [Check](#check)
10. [References](#references)

## How to clone

Pick a character that is already on the roster. The new name has to differ from that source. `ExpansionFolderName` builds the folder from the display name. `ExpansionGameId` is that folder with spaces and punctuation turned into underscores. A name that starts with a digit gets a `c` in front. The source id and the new id must not match, or the clone stops.

`ListPlayableCharacters` finds the source SWF on disk. A missing file stops the clone. An expansion folder that already uses the new name is deleted and written again. The work copy lives in a temp folder named `ssf2mm-shell-clone-` and is deleted when the clone finishes.

**Keep using clone's assets** is optional. When it is on, `keepOldAssets` stores the source id. [Clone old assets](clone-old-assets.md) is that path. The display name on the new fighter is the name you typed, not the source name.

## Renaming

`CloneAsync` reads the source file. A file that is not already an FWS, CWS, or ZWS SWF is unwrapped with `SwfPackage.ToSwf` first. The rewrite prefers an in-memory FFDec dump. If that dump cannot run, the same plan is applied by converting the SWF to XML and back.

`BuildCloneReplacementPlan` renames the fighter id, the stats class, the linkage ids, the final-smash cut-in, and the thumbnail. It also renames the `{id}_fla` package and the Ext class. An export in this SWF whose name starts with the old id is renamed. A class that is not in this SWF stays, unless `CollectTakenSymbolNames` already has that name from another loaded fighter. A stock lookup that is not a symbol class in this SWF stays. `zamus` becomes `zamus2`. `zamus_head` becomes `zamus2_head`. `ZamusExt` becomes `zamus2Ext`. `MetroidSymbolCSS` stays.

The display name field in the SWF is set to the name you typed. The `register("guid")` pool entry is given a new guid. If either field cannot be found, the progress line says so and the clone continues.

`SetupForReFlash2` stays false for a Shell clone. The ReFlash 2 folder pack is not what Make Mods writes.

## Expansion folder

`InstallExpansionFromTemplate` copies `expansions/template` into `expansions/<folder>`. Template SWFs other than `assets.swf` and `assets_<id>.swf` are deleted. The renamed fighter is copied in as `<id>.swf`. `RenameTemplateAssets` renames `assets_buu` files to `assets_<id>`. If `assets_<id>.swf` exists and `assets.swf` does not, the named file is copied to `assets.swf`. If the template had no `assets.swf`, the clone looks in `expansions/template`, then `buu`, then `hero`.

`css_meta.json` gets `displayName` and `id`. A meta file that was already in the template is kept, and those two fields are overwritten. `ApplyOldAssets` adds `oldassets` and `oldassetid` only when the checkbox was on.

`WriteLiveCostumesJsonAsync` writes `costumes.json` from the source fighter. `IsDataCharacterFile` is true only for a SWF under `data/character`. A stock fighter is the one that has a costumes block to copy. `CreditsService.Update` refreshes the credits list after the folder is in place.

## Clone FLA

Unless the clone was asked to skip it, `ExportFlaAsync` writes `expansions/<folder>/FLA/<id>.fla` at CS6. `characterPretty` is true. `stageLibrary` is false. The progress line is `Exporting FLA (<id>.fla)…`. A missing FLA is an error. The note on the result is `FLA: <path>`.

The export does not change the fighter SWF. It reads that SWF and writes the FLA beside it.

## Convert to FLA

Make Mods → Modding Tools → Convert to FLA lists every `.swf` and `.ssf` under `data/`. `BtnRunDecompile_Click` unwraps the chosen file with `SwfPackage.EnsurePlainSwf`, then calls `ConvertSwfToFlaAsync`.

A file whose relative path starts with `character/` sets `characterPretty`. A file under `data/stage` sets `stageLibrary` and follows [Convert a stage to FLA](convert-stage-fla.md). Any other data file gets a FLA with the character library layout and without the character pretty peel.

The output folder is `expansions/decompiled/` plus the path under `data/` with the extension removed. `DecompiledOutputDirectory` builds that path. An existing folder there is deleted first. The FLA name is the file name. Convert to FLA does not rename linkages and does not write an expansion fighter.

An engine folder is not this path. `FolderCompileService.IsEngineFolder` sends that file to `DecompileSwfAsync` instead.

Next2Flash has to be present. `IsAvailable` is false until the manager has downloaded it. The message is `Next2Flash could not be found. Open the manager once so it can download Next2Flash.`

## Character library

`ExportFlaAsync` runs the same steps for a clone FLA and for Convert to FLA.

FFDec exports one CS6 FLA into a temp folder named `ssf2mm-fla-`. `FlattenFlaExport` pulls a nested `{name}.fla/{name}.fla` up to one file. `FLA1` and `FLA2` sidecars are not kept. Empty Sound and MovieClip `.as` stubs are deleted. Scripts move to the common scripts folder. `ApplyFlaVanillaExportPatches` strips timeline `{id}_fla.*` AS3 export linkage so Animate does not type `stance` as `Idle_3` (Error #1034). Auto-declare is turned off. Scripts, sounds, and timelines are not rewritten.

Sounds are dumped from the SWF as FLV, converted to PCM WAV, and spliced back into the FLA. The progress line is `FLA: converting SWF sounds to WAV…`.

`ApplyFlaPrettyPass` then runs four steps. A step that reports failure is logged and the export continues.

1. `FlaCleanupSteps.Organize` applies `BuildFlaOrganizeBlueprint`. A clip on a labeled pose goes in `Animations/<label>/Graphics` or `Animations/<label>/Movieclips`. A bitmap on that clip goes in `Animations/<label>/Sprites`. A clip that is not on a labeled pose goes in `Shared Animations/<name>`. Its graphics and sprites go in `Graphics` and `Sprites` under that shared folder. A loose bitmap goes in `Shared Animations/Sprites`. Sounds go in `Audio`. `SmartLibraryName` strips the fighter id and a short prefix such as `bm_`, then splits underscores into words.
2. `FlaCleanupSteps.NamesAndOutlines` renames layers. With `characterPretty`, the Ground, Effects, and hitbox peel runs. Convert to FLA sets `characterPretty` only when the file is under `data/character`. A clone FLA always sets it.
3. `FlaCleanupSteps.FoldersAndLock` builds the remaining folders and locks them. The character peel runs here too when `characterPretty` is on.
4. `FlaCleanupSteps.Merge` merges box layers.

The log line for the whole export is `[JPEXS] timing total=` with `ffdec`, `pack`, `soundDump`, `wav`, `splice`, and `pretty`. Each pretty step logs `[JPEXS] timing pretty`.

## How it works

| Function | What it does | What you see |
| --- | --- | --- |
| `CloneIntoShellAsync` | Loads the roster, renames the source SWF, copies the template, writes costumes, then exports the FLA | `Renaming <id> → <newId> with JPEXS…` then `Exporting FLA (<id>.fla)…` |
| `CloneAsync` | Rewrites identifiers in memory, or through SWF XML when the in-memory dump cannot run | `Rewriting SWF identifiers in memory…` or `Converting SWF → XML via FFDec…` |
| `BuildCloneReplacementPlan` | Renames the id, the `{id}_fla` package, the Ext class, and exports in this SWF that start with the id | `zamus` becomes `zamus2`. A class that is not in this SWF stays |
| `CollectTakenSymbolNames` | Names already exported by other fighters. The plan renames a class in this SWF when that set already has it | A shared name is not left on the clone |
| `InstallExpansionFromTemplate` | Copies the template, drops template fighter SWFs, copies `<id>.swf`, renames `assets_buu` | `expansions/<folder>/<id>.swf` and `css_meta.json` |
| `ExpansionGameId` | Folder name with non-letters turned into underscores. A leading digit gains a `c` | `Best Mage` becomes `Best_Mage` |
| `WriteLiveCostumesJsonAsync` | Writes `costumes.json` from the source fighter | `Writing costumes.json…` |
| `ExportFlaAsync` | FFDec CS6 export, flatten, vanilla patches, WAV splice, pretty pass | `FLA: FFDec export starting…` then `FLA done` |
| `ApplyFlaVanillaExportPatches` | Strips `{id}_fla.*` timeline linkage and turns auto-declare off | Animate does not type `stance` as `Idle_3` |
| `ApplyFlaPrettyPass` | Organize, rename layers, folders and lock, merge box layers | `FLA: organize library` |
| `BuildFlaOrganizeBlueprintFromEntries` | Character layout uses `Shared Animations`. A stage file uses `Animations` only | A leftover clip is `Shared Animations/Leftover` |
| `ConvertSwfToFlaAsync` | Deletes the old decompiled folder and exports one data file | `Converting <file> to FLA…` then `Wrote <path>` |
| `BtnRunDecompile_Click` | Character path sets `characterPretty`. Stage path sets `stageLibrary`. An engine folder is decompiled as code | `Next2Flash could not be found` when the tool is missing |

## Files

| What | Path |
| --- | --- |
| `CloneIntoShellAsync`, `InstallExpansionFromTemplate`, `ExpansionGameId` | `src/Services/ShellCharacterCloneService.cs` |
| `CloneAsync`, `BuildCloneReplacementPlan` | `src/Services/CharacterCloneService.cs` |
| `ExportFlaAsync`, `ConvertSwfToFlaAsync`, `ApplyFlaPrettyPass`, `BuildFlaOrganizeBlueprintFromEntries` | `src/Services/JpexsCliService.cs` |
| Convert button | `src/Views/MakeModsWindow.xaml.cs` `BtnRunDecompile_Click` |
| Clone steps | `WizardStepProgress.CloneCharacterSteps` in `src/Services/CharacterPackageService.cs` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "Zamus2_RenamesIdExports|BuildFlaOrganizeBlueprint_MapsUniqueSharedAndAudio"
```

`Zamus2_RenamesIdExports` renames `zamus` exports and leaves a class that is not in the SWF. `BuildFlaOrganizeBlueprint_MapsUniqueSharedAndAudio` puts a leftover clip in `Shared Animations/Leftover` and its bitmap in `Shared Animations/Leftover/Sprites`.

## References

1. [Character import](character-import.md) — a file whose linkage is already playable is renamed with the same plan, then numbered `donkeykong2`.
2. [Clone old assets](clone-old-assets.md) — **Keep using clone's assets** writes `oldassets` and `oldassetid`.
3. [Convert a stage to FLA](convert-stage-fla.md) — a `data/stage` file keeps every clip under `Animations` and skips Shared Animations.
4. [Where things are](where-things-are.md) — the Shell folder these writes land in.
