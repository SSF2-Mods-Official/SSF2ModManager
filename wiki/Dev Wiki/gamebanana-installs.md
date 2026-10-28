---
title: GameBanana installs
summary: Each 1-Click import records its GameBanana mod id in the build it went into, and Browse Mods marks those mods Installed.
order: 30
group: Mods
---

# GameBanana installs

## Info

| GameBanana installs |
| --- |
| Record | `GameBananaInstalls` (`src/Services/GameBananaInstalls.cs`) |
| File | `<shell>/expansions/user/gamebanana-installs.json` |
| Confirm | `OneClickReceiveWindow` (name box, stage preview picker) |
| File list | `GameBananaApiClient.GetRawFileListAsync` (`apiv13/File/<fileId>/RawFileList`) |
| Related | [One-click install](one-click-install.md), [Browse mods](browse-mods.md) |

A 1-Click import remembers which GameBanana mod it came from, per Shell build.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How to use it

Download a mod from Browse Mods or a `ssf2mm:` link. Loose beta files (not a Mod Manager pack) show a **Name** box per item. A single stage starts with the GameBanana mod name (Fountain of Dreams, not the prettified id of `draculascastle`). A stage also shows a **Preview** list: the PNGs in the archive and the GameBanana screenshots. After the import, Browse Mods shows **Installed** on that mod for the loaded build. Another build has its own record.

## How it works

The expansion folder name is still the game id (`expansions/stages/<id>`, `expansions/character/<id>`), because the engine loads by that name. The GameBanana id goes into that folder's `info.json` as `gamebanana` and into the install record.

| Function | What it does | Log line |
| --- | --- | --- |
| `GetRawFileListAsync` | Reads the plain-text path list for a GameBanana file before download. | `[oneclick] rawlist file=<id> files=<n> game=<n>` |
| `OneClickReceive.RawListHasGameFiles` | False when the list is non-empty and has no `.ssf`, `.swf`, `.zip`, `.rar`, `.7z`, or `ssf2mm-pack.json`. The launcher then shows "Can't install this mod" without downloading. | — |
| `OneClickReceive.DefaultDisplayName` | The name box's start value. One stage or one character in the download takes the GameBanana mod name; otherwise the name the file stores; a song keeps its id. | — |
| `OneClickReceive.PreviewCandidates` | PNGs in the archive, then the GameBanana screenshots (`_aPreviewMedia`). A chosen screenshot is downloaded and saved as `stagepreview.png`. | `[oneclick] preview stage=<id> from=<archive|gamebanana|none>` |
| `OneClickReceive.NameStage` | The in-game stage name is `StageSelectMenu.displayName(id)`: remembered name, then mappings `metadata.stage[id].name`, then the prettified id. So the confirmed name goes into `stage[id].name`. A new entry points at `expansions/stages/<id>/<id>.swf` with the `guid` the stage registers. `info.json` `name` gets it too (the manager's stage list). | `[oneclick] stage name id=<id> entry=existing|new guid=<0|1>` |
| `OneClickReceive.NamePair` | A character whose file stores a display name the user changed is rewritten with `RewritePairsAsync` (stored → confirmed). Never when the stored name is the id (Fountain of Dreams' stage stores only `draculascastle`); rewriting it would rename the linkage. | `[oneclick] rewrite Character id=<id> pairs=1 rename=1`, `[oneclick] rename skip … reason=no-display-name-in-file` |
| `InstallAsync` | After each import, `StampGameBananaId` writes `gamebanana` into `info.json`, `SavePreview` writes the picked image as `stagepreview.png` (JPG converted), and `GameBananaInstalls.Record` saves the build's record. | `[oneclick] installed stages=1 characters=0 songs=1` |
| `GameBananaInstalls.Record` | Adds `{modId, fileId, name, at, items:[{kind,id}]}` to the build's record. | `[gbinstalls] record mod=<id> items=<n>` |
| `GameBananaInstalls.InstalledModIds` | Mod ids whose recorded items still exist in this build (`expansions/stages/<id>`, `expansions/character/<id>`, `data/sound/<id>.swf`). | `[gbinstalls] installed=<n> shell=<path>` |
| `DownloadModsPage` | Marks a card **Installed** when its mod id is in `InstalledModIds` for the launcher's current Shell. | — |

## Files

| What | Path |
| --- | --- |
| Install record | `src/Services/GameBananaInstalls.cs` |
| Name, preview, raw list | `src/Services/OneClickReceive.cs`, `src/Services/GameBananaApiClient.cs` |
| Confirm window | `src/Views/OneClickReceiveWindow.xaml(.cs)` |
| Installed badge | `src/Views/DownloadModsPage.xaml(.cs)` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter GameBananaInstalls
```

`Record` then `InstalledModIds` returns the mod id while `expansions/stages/<id>` exists and drops it once that folder is deleted. `DefaultDisplayName` for Fountain of Dreams' one stage is the mod name. A second `Record` (Alternate Voices Plus after Fountain of Dreams) keeps both mods.


## References

1. [One-click install](one-click-install.md) — download, classify, and import.
2. [Browse mods](browse-mods.md) — the page that shows Installed.
3. [Where things are](where-things-are.md) — `expansions/user` and the build folders.
