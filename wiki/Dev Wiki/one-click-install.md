---
title: One-click install
summary: A GameBanana ssf2mm link opens the launcher, then imports a 2.0 pack or loose beta files into the shell.
order: 20
group: Mods
---

# One-click install

## Info

| One-click install |
| --- |
| Receive | `LauncherWindow.ReceiveOneClick` |
| Classify | `OneClickReceive.ClassifyDirectory` |
| Document class | `SwfPackage.DocumentClass` |
| Song ids | `OneClickReceive.PlanSongIds` |
| Related | Launcher shell |

`IsOneClickLaunch` is a `ssf2mm:` argument. `ParseCliOptions` does not put that argument in Install, so startup opens the launcher only.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

The link is `ssf2mm:https://gamebanana.com/mmdl/{fileId},Mod,{modId}`. `ClassifyDirectory` reads the archive.

A zip or an `.ssfe` with `ssf2mm-pack.json` `"format": "ssf2mm-pack"` is a Mod Manager 2.0 pack. `ClassifyArchive` reads it. `InstallAsync` calls `ModPackageService.DeployExtracted`, which copies each folder into the expansions folder for that mod's type.

Any other archive is loose files. `SwfPackage.DocumentClass` is the SymbolClass name stored for character id 0. `Main` is a beta file. `MainTimeline`, and a name that ends with `.MainTimeline`, is 0.9b and is left off the list. Olympus Coliseum is `chaosshrine_fla.MainTimeline`.

A path folder `9b`, `0.9b`, or `v.9b` is left off the list. When the archive has a `sandbox` folder, only files under that folder are listed. `Own Slot` starts checked. A folder whose name starts with `Over` is the same mod written over a stock stage, and it starts unchecked.

A song is a beta file whose id starts with `bgm_`. `PlanSongIds` keeps that id when `data/sound` does not already have it. When the id is taken, the song is the next free id, and `CharacterCloneService.RewritePairsAsync` writes that id into the stage that names the old one. `PlaceSongAsync` writes the song file's `guid` into mappings. `bgm_mygetaway` keeps `tboz-rugrats-my-getaway`.

| Function | What it does | Log line |
| --- | --- | --- |
| `IsOneClickLaunch` | A `ssf2mm:` argument is a 1-click launch. | — |
| `DirectDownloadUrl` | `/mmdl/{fileId}` becomes `https://gamebanana.com/dl/{fileId}`. | — |
| `DownloadAsync` | Reads that file and reports bytes received. | `[oneclick] download total=9211095` |
| `FormatDownload` | The overlay text for those bytes. | — |
| `ReceiveOneClick` | Shows `FormatDownload` on the launcher with a **Cancel** button, then the confirm list. Cancel stops `DownloadAsync` through its token and deletes the partial file. Download Mods calls this too. | `[oneclick] receive file=1768124`, `[oneclick] canceled file=<id>` |
| `ReceiptIntro` | The confirm list's opening text: "Choose what to install into SSF2 Shell." It explains an unchecked stage only when an `Over` copy is listed (row note "Replaces a built-in stage"; the own-slot row says "Adds a new stage"), then the skipped 0.9b and other-version counts. | — |
| `NothingToInstallMessage` | When a download is not a pack and has no listed item, the launcher shows this error instead of an empty confirm list. It counts the 0.9b files and the unrecognized files and says the Shell needs a Beta 1.4.0.1 version of the mod. | `[oneclick] nothing to install nineb=<n> unknown=<n>` |
| `TempNote` | Names the temporary folder `%TEMP%\ssf2mm-oneclick-<guid>` and says the download and anything left unchecked are deleted. The download overlay, `ReceiptIntro`, `NothingToInstallMessage` ("has been deleted"), and the canceled status all show it. `DownloadAsync` deletes the folder on cancel or error; `ReceiveOneClick` deletes it in `finally`. | `[oneclick] canceled file=<id>` |
| `DocumentClass` | SymbolClass character id 0. | — |
| `SwfPackage.RegisterCalls` | Reads the DoABC constant pool and returns each `register("<key>", "<value>")` the document class makes (value null when it is not a string). A stage has `register("stage", …)`, a song `register("music", "bgm_…")`, and `register("id", …)` is the real id. Fountain of Dreams' `DAT212.ssf` is `MenuStages` with `register("id","menu_stages")` and a `stage_txt` clip. | — |
| `ClassifyFile` | A beta file with a non-`Main` document class (`MenuStages`, `Misc2`, `Mappings`) replaces a game file and is not listed. Otherwise `register("stage")` is a stage, `register("music")` a song, and the id is `register("id")`; the old string heuristics run only when a file has no `register` calls. | `[oneclick] kind=stage id=draculascastle selected=1` |
| `ClassifyAlts` | After `ClassifyArchive`, the launcher passes the build's character ids (`TakenCharacterIdsAsync`). Files for a character the build already has become one **Alt** item per folder; `Vanilla` and `Announcer` folders are left out. See [Character alts](character-alts.md). | `[oneclick] alts groups=<n> vanilla=<n> announcer=<n>` |
| `ClassifyDirectory` | Pack, stage, character, or song. Leaves out a 9b folder and prefers sandbox. | `[oneclick] kind=stage id=triplebaka selected=1` |
| `PlanSongIds` | A taken `bgm_` id becomes that id plus the next free number, starting at 2. | — |
| `InstallAsync` | A pack calls `ModPackageService.DeployExtracted`. A stage calls [Stage import](stage-import.md) `ImportIntoShellAsync`. A character calls [Character import](character-import.md) `ImportIntoShellAsync`. A song is copied to `data/sound`. | `[oneclick] installed stages=1 characters=0 songs=2` |
| `IsTimelineDocument` | `MainTimeline` or a name ending in `.MainTimeline` is left out. | `[oneclick]` does not list Olympus Coliseum. |
| `ReadSongGuid` | The string stored after the `guid` key in the song file. | — |
| `RunFfdecAsync` | Starts the FFDec worker with `n2f ffdec -- --worker`. | Without `--`, n2f logs `unrecognized arguments: --worker`. |

## Files

| What | Path |
| --- | --- |
| `IsOneClickLaunch` | `src/Services/ProtocolService.cs` |
| `DirectDownloadUrl`, `FormatDownload`, `DownloadAsync` | `src/Services/OneClickReceive.cs` |
| `ReceiveOneClick` | `src/Views/LauncherWindow.xaml.cs` |
| `ClassifyDirectory`, `PlanSongIds`, `InstallAsync`, `NothingToInstallMessage` | `src/Services/OneClickReceive.cs` |
| `DocumentClass`, `IsTimelineDocument` | `src/Services/SwfPackage.cs` |
| `RunFfdecAsync` | `src/Services/JpexsCliService.cs` |
| `RewritePairsAsync` | `src/Services/CharacterCloneService.cs` |
| Confirm list | `src/Views/OneClickReceiveWindow.xaml` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter OneClickReceive
```

`IsOneClickLaunch` is true for `ssf2mm://https//gamebanana.com/mmdl/1768124,Mod,698679`. `DirectDownloadUrl` for that file is `https://gamebanana.com/dl/1768124`. `FormatDownload` at half of 9 MB says `Downloading… 50% (4.3 MB / 8.6 MB)`. `DocumentClass` says `Main` and `MainTimeline`. `ClassifyDirectory` checks Own Slot and leaves Over unchecked, and it lists the sandbox file when a `v.9b` folder is also present. `PlanSongIds` says `bgm_joggingcountdown2` when `bgm_joggingcountdown` is taken. `NothingToInstallMessage` is null when an item is listed and names the 0.9b count when only 0.9b files were found.

`OneClickRealArchiveTests.FountainOfDreams_149685` classifies `%TEMP%\gbprobe\149685\fod.rar` (GameBanana file 387381) when it is present. The items are exactly `Song bgm_bloodytears` and `Stage draculascastle`.


## References

1. Launcher shell — the window that receives the link.
2. [Stage import](stage-import.md) — a taken stage id becomes the next free id.
3. [Character import](character-import.md) — a taken linkage is renamed and the display name stays.
4. [Where things are](where-things-are.md) — the shell folder the import writes into.
5. [Character alts](character-alts.md) — files for a character the build already has.
6. [GameBanana installs](gamebanana-installs.md) — names, previews, and the per-build install record.
