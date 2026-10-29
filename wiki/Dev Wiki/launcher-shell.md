---
title: Launcher shell
summary: Window title, Manage Mods card text, the News list, and the Discord button.
order: 40
group: Build
---

# Launcher shell

## Info

| Launcher shell |
| --- |
| Window | `LauncherWindow` |
| Client size | 640×360 (`StageBackdrop.ClientWidth` / `ClientHeight`, same as `Main.m_width` / `Main.m_height`) |
| Title | `AppInfo.ProductName` |
| Mods | `ManageModsWindow.BuildCard` |
| News | `NewsPage.ShowArticle` |
| Related | [Where things are](where-things-are.md) |

The launcher window title and the title-bar label are `SSF2 Beta Mod Manager`.

## Contents

1. [How it works](#how-it-works)
2. [Home](#home)
3. [Music player](#music-player)
4. [Mod Manager background](#mod-manager-background)
5. [Manage Mods text](#manage-mods-text)
6. [News](#news)
7. [Discord button](#discord-button)
8. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `BtnJpexs_Click` | Opens JPEXS from Next2Flash `ffdec.jar` with the bundled Java. The rail icon is `FfdecMarkGeometry`, a page with a lightning bolt | — |
| `LauncherUpdateService.Relaunch` | After a manager update, a prompt says the Mod Manager is updated and will now restart. The running exe is renamed to `.old`, any `.new` file is moved into place, and the new exe starts | `[update] relaunch` |
| `SetupWindow` | One step at a time. The main window stays hidden until the done step. | — |
| `NewsPage.BtnCheckUpdates` | Upper right of News. Runs the same check as the launcher | — |
| `CreditsService.StandingCredits` | Notes keep each person in `wiki/_static/credits.json`. The manager Credits page and the wiki [Credits](../Start Here/credits.md) page read that file. The wiki page opens from the Credits button at the top-right of the article, including the home page, and stays out of the sidebar. A launch rewrite keeps that Notes block. | — |
| `AppInfo.ProductName` | Window title and title-bar label | — |
| `BuildCard` | Mod description uses `TextPrimaryBrush` | — |
| `ShowArticle` | News click replaces the list with the full article | — |
| `BtnDiscord` | Discord mark from `DiscordMarkGeometry`, then the word Discord | — |
| `ListStages` | Menu background and launch stage lists: ids from `data/stage/*.swf` and `*.ssf` | — |
| `MusicLibraryCacheService.EnsureFastLibrary` | Footer mini player song list: AppData cache, else Shell `music_player/list.json`, else bundled `music-library-stock.json`; refreshes raw names; prefers `.swf`. `LoopChecked` skips a file whose loop was already read | — |
| `EnsureMiniQueueAsync` | Shows the saved cache on a background read, then refreshes the list off the window thread | — |
| `EnsurePreviewMp3Async` | Unwraps `.ssf`, then `BgmPreviewService.ExtractMp3` (fast); FFDec `-export sound` only if that fails | — |
| `AppendExpansionSongs` | Adds Music content packs under `expansions/packs` and loose `data/sound/bgm_*` `.swf`/`.ssf` that mappings missed | — |
| `AppMusicPlayerService` | Queue, seek, loop/shuffle, volume, visualizer; Repeat One + SeamlessLoop seeks to `LoopMs` | — |
| `ApplyLauncherSeekFromHit` | Seek bar: UI preview while dragging; `commit: true` only on release | — |
| `FitSsf2ClientSize` | Outer window so the client area is 640×360 | — |
| `ApplyLauncherBanner` | Window fill beside the rail. A custom file from `GetManagerBackgroundCustomPath` wins when the menu match is off: a gif loops frames, a video loops on `VidLauncherStage`, a still image pans. Otherwise a stage uses `StageBackdrop.LoadSnapshot`. None hides it | — |
| `StageBackdrop.ComposeBackground` | Stacks the `{stageId}_bg` sprite back to front: bitmap fills, nested sprites, then a solid fill. `LoadSnapshot` uses that image. `DecodeLargest` runs only when the sprite has nothing to draw | — |
| `BtnRailMusic_Click` | Off the home screen, moves `PlayerCard` into `MiniPlayerPopup`. Home hides the button and parks the card | — |
| `UpdateRailMusicVisualizer` | While a track plays, draws four bars in the rail icon. Bar height is the level times 16, and the top is `16 - height`. It still runs when the player card has no size | — |
| `DrawMiniBars` | Same bars on `LauncherMiniVisualizer` when that canvas has a size, on the home card and inside `MiniPlayerPopup`. Height is the level times the canvas height | — |
| `LauncherMusicUi.PreviewQueue` | One requested song plays inside the current queue, or the library when that queue is a single track. A longer list replaces the queue | — |
| `BtnRailExpand_Click` | Widens `SidebarColumn` from 52 to 112 and shows rail names | — |
| `ShowPageChrome` | Hides `HomeTiles` and shows `PageHost`. The rail and the preview bar stay | — |
| `ApplySectionAccent` | Section bar uses that page's home-tile color | — |
| `Shell_PreviewMouseMove` | A control `Tag`, or its `ToolTip` string, replaces `TxtStatus`. Moving off, or leaving the window, restores `PreviewIdleText` (product name and version). `ToolTipOpening` is handled so no popup shows | — |
| `AppMusicPlayerService.Volume` | Stored level. `OpenCurrent` and `MediaOpened` apply it again. `SetMiniPlayerVolume` writes it without reloading the banner | — |
| `RememberMiniSong` | Saves `LastPlayedSongId` when the track changes. `EnsureMiniQueueAsync` selects it on the next launch | — |
| `ManagerBackgroundMedia.Install` | Copies an upload to a new `manager-background-{id}` file and deletes older copies | — |
| `LoadMoreAsync` | Download Mods shows `TxtLoading` while the first page is empty | — |
| `BrowseModFilter.IsUnsupported` | Hides 0.9b, a modpack mention, and a download whose name or url ends in `.exe` or `.json`, unless Show unsupported mods is checked | `[browse] hiddenUnsupported=` |
| `ConfigureLaunchWindow` footer | Remember, Keep open, and Fullscreen share one short line with Launch and Save & Close so the form keeps the page | — |
| `LauncherMiniMarquee_Click` | Opens Make Mods → Music | — |
| `BtnMusic_Click` | In-app stage playlist and jukebox lists. `BtnOpenJukebox_Click` launches `ScreenJukebox` | — |
| `GetClockUseAmPm` | Sidebar clock format. Off is `HH:mm`, on is `h:mm tt` | — |
| `GetPauseMusicOnLaunch` | Pauses the mini player when a launch starts SSF2. Default on. Sync music calls `ClearCurrent` and leaves playback paused. A `now-playing.json` older than the launch is ignored, so the cleared song does not start again | — |
| `GetSyncMusicWithGame` | Default on. While SSF2 is running, `PollMusicSync` sets the mini player volume to 0 and follows `now-playing.json`. A menu change updates that file from My Jukebox. Play, pause, seek, and song changes go through `managerPlay`, `managerPause`, `managerResume`, and `managerSeek`. Closing SSF2 pauses the mini player and restores the volume | — |
| `ShellDirectLaunchService.GameStarting` | Raised just before `Process.Start` on the game exe | — |
| `StartLogoBounce` | After 5 idle minutes on the home screen, or F1, hides the home tiles and bounces `SSF2M.png` above them, left of the sidebar. A playing mini player stays. The preview bar says `Click to exit`. A click or any key except Shift+F2 calls `StopLogoBounce`. A corner runs a 10 second hue cycle and the bar says `whoooo it hit the corner!`. Another corner starts that cycle again. Shift+F2 calls `AimAtCorner` after the next wall so the following hit is a corner | — |
| `RoundClip_SizeChanged` | Clips the player card and the Play tile to their corner radius | — |
| `UpdateSidebarClock` | Right-rail clock, `HH:mm`, every 30 seconds | — |
| `MenuBackgroundStore.ReadStageId` | In-game menu stage from `data/ssf2mm/menu_background/mod.json` | — |
| `SwfQuickScan.TryReadMusicMeta` | Title, artist, and `musicLoop` ms from the SWF string pool | — |

## Home

The client is the game size, 640×360. The chosen stage fills the area beside the rail (`ImgLauncherStage`). Left column, top to bottom: the player, Manage Mods, Play at half the old height. Download Mods and Make Mods are equal tiles beside that stack. Configure is the same red as Play and the same width. Docs and Debug sit beside Configure. Big tiles put the icon on one line and the label under it, centered. Home tiles are full size. The border uses the tile color. The fill is the full rounded tile at half opacity with a dark layer under the colored border, and hover shows the solid color with no gap inside the border. The icon and label keep a black outer edge. The app font is `NimbuSanTBlaCon` (`Assets/Fonts/NimbusSansT-BlackCondensed.otf`) at normal weight. The right rail is News, Settings, Credits, Discord, Support (cafe icon), Open log, then Expand. Collapsed it is 52px with centered icons. `SidebarOutlineBrush` is white on dark themes and black on light themes. Rail, clock, and preview text use `TextPrimaryBrush`. Expand sets `SidebarColumn` to 112. `TxtVersion` sits above `TxtSidebarClock` at font size 9. The bar under the tiles uses `SidebarBrush`. `TxtStatus` shows `LauncherMusicUi.PreviewIdleText` (`SSF2 Beta Mod Manager` and `AppInfo.DisplayVersion`) until a control `Tag` or `ToolTip` string is under the pointer, and the text is centered. Moving off that control, or leaving the window, puts the title back. Tooltip popups do not open. `Progress` stays collapsed until `SetStatus` receives a fraction or `SetBusy(true)`.

## Music player

Off the home screen the rail shows a music note. While a track plays, `UpdateRailMusicVisualizer` draws four bars in that icon by setting each bar's height and its top. `DrawMiniBars` does the same on `LauncherMiniVisualizer` when that canvas has a size, which is the home card and the card after `BtnRailMusic_Click` moves it into `MiniPlayerPopup`. The rail bars still update when the card is hidden. Event Preview asks for one song. `PreviewQueue` leaves the chevron list in place and selects that song.

The player is a solid `CardBrush` card in the upper left. `RoundClip_SizeChanged` clips it to the corner radius so the visualizer cannot paint past the corners. Layout top-to-bottom: accent seek bar, full-width billboard (`DisplayLabel` marquee), then transport / chevron song list / speaker icon. `LauncherSongPick` draws only the chevron. Clicking the billboard runs `LauncherMiniMarquee_Click`. Make Mods → Music edits the cached stage playlist and `UserPlaylists`, can `PlayInManager` into this player, and **Open My Jukebox in game** calls `BtnOpenJukebox_Click`. Volume opens a popup from `BtnLauncherMiniVolume`. `AppMusicPlayerService.Volume` is applied again in `OpenCurrent` and on `MediaOpened`, because opening a file resets the player, and `SetMiniPlayerVolume` stores it. Scrub updates the bar only while dragging; `ApplyLauncherSeekFromHit(..., commit: true)` runs on mouse-up. `WarmMiniPlayerAsync` fills the combo from the saved cache on first render, then refreshes names off the window thread. `RememberMiniSong` writes `MusicLibraryCache.LastPlayedSongId` when the track changes, and `EnsureMiniQueueAsync` selects that song the next time the app opens. Previews use in-process MP3 extract; neighbors prefetch after play. Repeat One resumes at the file `musicLoop` (`LoopMs`); Repeat All advances the queue.

Settings shows the Shell folder (`TxtShellFolder`) and Next2Flash (`TxtNext2Flash`). **Don't show file paths** (`GetBlurFilePaths`) replaces those with SSF2 Shell and Next2Flash. The language picker is hidden. **Sync music with the game** is on by default. Launching clears the mini player's song. While SSF2 is open the mini player volume is 0 and it follows the song My Jukebox reports, including a menu change. Picking a song sends `managerPlay` and shows the song HUD when the jukebox view is closed. Play, pause, and a released scrub go through the jukebox. Closing SSF2 pauses the mini player and restores the volume.

## Mod Manager background

Settings → Menu background has **Use as Mod Manager background**. Checked: the window background uses that stage id. Unchecked: **Mod Manager background** picks another stage, or **Upload** copies a picture or video to a new `manager-background-{id}` file and that file is the background. A still image is loaded from its bytes so a previous file cannot stay on screen. A gif or video loops. A still image pans. None, with no custom file, leaves the theme background. A stage id stacks the `{stageId}_bg` sprite (`StageBackdrop.ComposeBackground`) and pans that image behind the tiles, stopping at the sidebar. `DecodeLargest` is only the fallback when that sprite draws nothing. The stage’s vector timeline still plays only in the game.


## Manage Mods text

Each mod is one row from `BuildCard`. Essentials skip the preview column. The section name is only the crumb. Menu background has no Save buttons; the dropdown calls `SaveMod`. The music player row drops a leading `PS3-style`. The home uses four columns: launch tiles for `ScreenCssEdit`, `ScreenSssEdit`, `ScreenVaultCostumes`, and `ScreenJukebox`, then the sections. Description text uses `TextSecondaryBrush`. Solarized body text is `#073642`. Sunset secondary text is `#4A3330`.

## News

The News page lists each article as a preview: title, date, and excerpt. **Check for updates** sits at the upper right and runs `OfferProductUpdatesAsync`. A click calls `ShowArticle`, hides the list, and shows the full article. **Back** returns to the list.

## Discord button

The title-bar Discord button draws `DiscordMarkGeometry` in the button foreground, then the word Discord.

## References

1. [Where things are](where-things-are.md) — which packs are essential and which are optional.
