---
title: Browse mods
summary: The launcher's Download Mods page lists SSF2 mods from GameBanana and hides 0.9b-only mods unless asked.
order: 10
group: Mods
---

# Browse mods

## Info

| Browse mods |
| --- |
| Page | `Views/DownloadModsPage` |
| Feed | `GameBananaApiClient.BrowseIndexAsync` (game 5789), `SearchCategoryAsync`, `GetModDetailsAsync` |
| Filter | `BrowseModFilter.MentionsNineB`, `BrowseModFilter.KindFor` |
| Install | `LauncherWindow.ReceiveOneClickAsync`, the same path as a `ssf2mm:` link |
| Installed | `GameBananaInstalls.InstalledModIds` for the page's Shell; see [GameBanana installs](gamebanana-installs.md). An installed card shows the **Installed** badge and hides **Download** (`DownloadVisibility`). The launcher calls `DownloadModsPage.RefreshInstalled` after a 1-Click import. |
| Related | [One-click install](one-click-install.md), Launcher and shell |

Download Mods lists every SSF2 mod on GameBanana, newest first, and hides mods made only for 0.9b.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Check](#check)
4. [References](#references)

## How to use it

Pick a category or **All**. The search box fills the rest of that row. **Show semi-supported mods (not .ssfe)** is the next line and starts on. **Show unsupported mods** is the line under it and starts off. Press **Load more** for the next page. Each card shows the preview, submitter, category, version, likes, views, downloads, update date and the start of the description. **Download** runs the 1-Click import with its download progress and file list.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `BrowseIndexAsync(page, 50)` | `Mod/Index?_aFilters[Generic_Game]=5789`, 50 records a page, newest first. On 2026-10-15 the whole game was 160 mods, 76 of them mention 9B, so 84 can show. Records have `_sName`, `_sVersion`, `_aRootCategory`, `_aSubmitter`, `_aPreviewMedia`, likes and views, and no description. The old `Game/5789/Subfeed` returns 15 a page whatever `_nPerpage` says, and its 220 count includes questions, requests and threads. | none |
| `DownloadModsPage.LoadMoreAsync` | Each load reads a page, fetches its details, and repeats until 200 more mods are shown or the feeds end. A row stays hidden until its details load or fail, so a 9B mod never flashes in. | `[browse] load pages=<n> shown=<n> more=<0/1>` |
| `GetModDetailsAsync` | `Mod/<id>?_csvProperties=_sText,_nDownloadCount,_aFiles`, run 8 at a time after a page loads. `_sText` is the description (HTML); `_sDescription` is usually empty. | `[browse] details mods=<n> failed=<n>` |
| `SearchCategoryAsync` | Category feeds with a real id: Events 49482, Music 49485 (empty on GB today), Characters 16597, Stages/Maps 6672, General/Game files 4259. Tutorials and Intros stay at 0 and filter the game feed. **All** also reads every feed with an id; a mod already listed takes that feed's kind. | none |
| `BrowseModFilter.KindFor` | Maps the root category to a filter kind: Characters → character, Maps → stage, Sounds or Music → music, anything else → mod. | none |
| `BrowseModFilter.MentionsNineB` | True when the name, version or description says `9b` as its own token (`0.9b`, `v0.9B`, `V.9b`, `9B/FMC`). Any mention hides the mod, even next to `Sandbox` or `Beta`. CR7 (720946) is hidden by its description's `(0.9b)`. | none |
| `DownloadModsPage.ApplyFilter` | Applies category, search and the 9B toggle. The status shows the count and how many 9B mods are hidden. | `[browse] shown=<n> hidden9b=<n> rows=<n>` |
| `DownloadModsPage` → `ReceiveOneClickAsync` | Builds `ssf2mm:https://gamebanana.com/mmdl/<fileId>,Mod,<modId>` for the newest file and hands it to the launcher. | `[oneclick] receive file=<id>` |


## References

1. [One-click install](one-click-install.md): the `ssf2mm:` link path, which also leaves 0.9b files out.
2. Launcher and shell: where the Download Mods page opens and which Shell it installs into.
