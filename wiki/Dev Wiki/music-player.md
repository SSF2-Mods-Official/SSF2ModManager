---
title: Music player
summary: My Jukebox mod: which class owns which part, what the mod patches, and the log lines.
order: 20
group: Match
---

# Music player

## Info

| Music player |
| --- |
| Mod id | `music_player` |
| Source | `shell/src/mods/music_player/src` in SSF2 Shell Base |
| Entry | `MusicPlayerMod` (coordinator) and `MusicPlayerUi` (jukebox view) |
| Patches | `playMusic`, plus the five pitch-shift calls through `PitchShiftBridge` |
| Log | `[music_player] ...` through `trace` (game log) |

My Jukebox is the built-in music mod: Options > Sounds, the Stage Select button, F3, Taunt while paused, and Taunt+Grab in menus all open the same jukebox view.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Rebuild](#rebuild)
4. [Check](#check)
5. [References](#references)

## How it works

`MusicPlayerMod` and `MusicPlayerUi` are thin owners. Each part lives in a helper class that holds a typed back-reference (`mod` or `ui`) and reads shared state through `internal` fields. `MusicCatalog` keeps its public API and forwards per-track metadata and the song cache to two helpers. Hooks are added and removed with the same `helper.method` closure, so `onBootEnd` still detaches everything.

| Function | What it does | Log line |
| --- | --- | --- |
| `MusicPlayerMod.onPatchedPlayMusic` | Routes an upload or folder track to local playback. Anything else goes to `SoundQueue.playMusicStock` | `[music_player] playMusic local step failed (<id>): <err>`, `[music_player] stock play failed: <err>` |
| `MusicModLocalPlayback.playLocalTrack` | Opens the file, transcodes non-Flash MP3 first, and loads it into a `Sound` | `[music_player] local load failed (<id>): <err>`, `[music_player] local play failed: <err>` |
| `MusicModMatchMusic.onModeStart` | Resets playback, `chooseMatchTrackId` (replay stamp, claim, override, playlist). `MusicOverrideCodec.decode` supplies the track. An empty track leaves the stage song. A write uses `withTrack`, so `|bg=` stays. | `[music_player] replay music missing — using stage default (<id>)`, `[music_player] MODE_START ensure failed <id>` |
| `MusicPlaylistStore.save` | Writes the playlist JSON to disk and to `saveExtSet` | `[music_player] playlist save serialize failed`, `... write failed`, `... saveExtSet failed` |
| `MusicUiOnline.installOnlineMod` | Downloads a GameBanana pack, then `MusicPacks.sync` | `[music_player] online install start failed (<modId>)`, `[music_player] online install sync failed` |
| `MusicSongCache.mergeStockList` | Reads the stock `data/ssf2mm/music_player/list.json` (vanilla only: 185 `tracks`, the per-stage `stages` lists, and `other`). Pack songs come from `expansions/songs/packs/<id>/list.json` through `MusicPacks`, never from the stock list | — |
| `MusicSongCache.mergeDiskCache` | Merges `data/ssf2mm/state/songcache.json` after the stock list. `MusicCacheFilter.keep(meta, stock)` keeps a cached track when it is stock or its pack `swf` / upload `file` exists. `MusicCacheFilter.listed` is false when that track has no sound file, no library sound, and no pack audio, and `MusicUiSongs.appendUnique` skips it, so a removed song does not stay in the list as `…` | `[music_player] songcache merge dropped=<n> (no stock entry, audio missing)` |
| `MusicModMatchMusic.onSongPicked` | Starts `playTrack` (async load) and claims the pick for the next match. The toast takes its title from `matchPickTitle(trackId)`, because `m_currentTitle` still names the previous song until the load finishes | `Match music: <title>` (MultiplayerManager notify) |
| `MusicPlayerMod.onBootEnd` | Stops playback, detaches buttons, disarms the pause-Taunt and Taunt+Grab frames, and removes every hook | — |
| `MusicModInput.onKeyEvent` | F3 opens My Jukebox. Ctrl+Shift+M calls `toggleBgmMute` | `[music_player] mute`, `[music_player] unmute vol=` |
| `MusicModTransport.managerPlay` | Manager sync plays `trackId` through `playTrack`. A `positionMs` above 0 is applied with `seekRatio` after the track loads. When the jukebox view is closed, the song HUD shows | `[music_player] managerPlay id= pos=` |
| `MusicModTransport.syncFromSoundQueue` | When SoundQueue's song id changes, the local channel stops and that id becomes the current track, so a menu change is the song the mini player follows | `[music_player] sync adopt id=` |
| `MusicModTransport.managerPause` | Pauses through `togglePause` when the jukebox is playing | `[music_player] managerPause` |
| `MusicModTransport.managerResume` | Plays again at `positionMs` through `seekRatio` or `togglePause` | `[music_player] managerResume pos=` |
| `MusicModTransport.managerSeek` | While playing, `seekRatio`. While paused, stores the position | `[music_player] managerSeek pos= paused=` |
| `MusicModTransport.managerState` | Track id, title, position, paused, and duration for `now-playing.json` | — |
| `MusicSeriesResolver.seriesKeyFromEntry` | Non-empty `icon` wins over `stageLogo`. Otherwise `stageLogo` yields `stage:{id}`. Jungle Island / Urban Champion / Venus use SSS franchise clips (`SuperMonkeyBallSymbolSSS`, `UrbanChampionSymbolSSS`, `GoldenSunSymbolSSS`). Desk uses `chibi_icon` | Desk → `icon:chibi_icon` |
| `MusicStageVisuals.extractLiveLogo` | Peels the logo child from a `stagePreviewer` frame clone. Rejects full-frame plates (those tint into a white rectangle) | `[music_player] extractLiveLogo id= step=frame\|host\|child\|reject` |
| `MusicStageVisuals.extractSelectLogo` | Same peel from `{id}_stage_select` when the previewer peel misses (Smashville / Steel Diver have no SymbolSSS export) | — |
| `MusicSeriesResolver.attachIcon` | `icon:…` from the library. `stage:…` via `extractLiveLogo` then `extractSelectLogo`. Smash ball last — never tint full `_stage_select` | — |
| `MusicUiGrid.getOrBuildTileArt` | Tile fill is `{id}_stage_select`. Other Stages hub uses `traininggrid` fill and `KEY_OTHER` | — |

## Fixed

### White rectangle instead of stage logo

Tinting a full `_stage_select` (or a mis-peeled preview plate) as the album symbol painted a white rectangle. `attachIcon` no longer uses that path; smash ball is the default when no SymbolSSS / logo peel exists. Jungle Island, Urban Champion, and Venus Lighthouse hardcode their SSS franchise symbols from the menus library.

## Files

| File | Role |
| --- | --- |
| `MusicPlayerMod.as` | Boot/teardown, hooks, `mod_menu.register`, `playMusic` patch |
| `MusicModTransport.as` | Queue, play/prev/next, pause/seek, repeat/shuffle, SoundQueue sync |
| `MusicModMatchMusic.as` | Match music: claim, MODE_START pick, replay stamp, match pause |
| `MusicModLocalPlayback.as` | Upload/folder playback through `flash.media.Sound` |
| `MusicModTrackLoader.as` | Register uploads/folders/legacy and load/unload bookkeeping |
| `MusicModMenuFlow.as` | Catalog warmup, launch screen, menu show/hide, attract BGM |
| `MusicModButtons.as` | Sounds and Stage Select jukebox buttons |
| `MusicModInput.as` | F3, Taunt while paused, Taunt+Grab chord |
| `MusicModNowPlaying.as` | Now-playing publish, BGM hold flag, song HUD |
| `MusicPlayerUi.as` | Jukebox open/close, chrome, tabs, paint |
| `MusicUiGrid.as`, `MusicUiSongs.as`, `MusicUiSongList.as` | Album grid, song context, song list rows |
| `MusicUiPlaylistEdit.as` | Add/remove songs, rename/delete/save as pack, uploads |
| `MusicUiInput.as` | Per-frame pad poll (`onFrame`), hold repeat, key handler |
| `MusicUiTransport.as` | Now-playing bar wiring and backgrounds |
| `MusicUiDialogs.as`, `MusicFilterListPicker.as`, `MusicTilePicker.as` | Modal overlays and pickers |
| `MusicUiTrackSettings.as`, `MusicUiTheme.as`, `MusicUiSearch.as`, `MusicUiScroll.as`, `MusicUiOnline.as` | Settings menus, theme colors, search, scrollbar, online tab |
| `MusicCatalog.as`, `MusicTrackMeta.as`, `MusicSongCache.as` | Stage roster, per-track metadata, `list.json` / `songcache.json` |

## Rebuild

`powershell -NoProfile -ExecutionPolicy Bypass -File "work\mod_build.ps1" -Id music_player` from SSF2 Shell Base prints `[build_mods] OK music_player`. Tests: `node --test` in `music_player\tests`.

## Check

A strict compile (`"strict": true` in a temp copy of `asconfig.json`) has zero errors. That proves every moved call site resolves, because the normal build runs with strict off.

`python -m unittest discover -s tools\tests` in Shell Base runs `test_music_list.py`, which fails when the stock `list.json` names a song or stage missing from the vanilla mappings.

By Stage and By Series: Desk `chibi_icon`, Venus `GoldenSunSymbolSSS`, Jungle Island `SuperMonkeyBallSymbolSSS`, Urban Champion `UrbanChampionSymbolSSS`. Smashville / Steel Diver peel a logo or fall back to the smash ball (no white rectangle). Other Stages hub is not a blank tile.


## References

1. [Built-in mods](built-in-mods.md) — the other bundled mods and how they load.
2. [Where things are](where-things-are.md) — repo and folder layout.
3. [Online sync](online-sync.md) — how the hosts match song reaches every player online.
