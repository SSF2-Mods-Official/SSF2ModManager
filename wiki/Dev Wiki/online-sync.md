---
title: Online sync
summary: What an online room shares, and how match music, camera background and costumes stay the same for every SSF2 Shell player without sending new fields.
order: 50
group: Mods
---

# Online sync

## Info

| Online sync |
| --- |
| Engine | `net/MultiplayerManager.as`, `mgn/net/MGNClient.as`, `controllers/GameSettings.as` (decompiled `engine2/changed`) |
| Mods | `music_player`, `camera_background`, `palette_maker` |
| Shared code | `_shared/src/ssf2mm/shared/MusicOverrideCodec.as` |
| New fields sent | None in match or player settings. Probe: one top-level `ssf2mm` key in room data. |
| Related | [Built-in mods](built-in-mods.md), [Music player](music-player.md), [Costume recolor](costume-recolor.md) |

The host's match settings are the only thing every player receives, so shared choices travel inside them.

## Contents

1. [What vanilla sends](#what-vanilla-sends)
2. [How it works](#how-it-works)
3. [Room data probe](#room-data-probe)
4. [Music override format](#music-override-format)
5. [Costumes](#costumes)
6. [Files](#files)
7. [Check](#check)
8. [References](#references)

## What vanilla sends

All traffic goes through the MGN WebSocket lobby (`MGNClient`, `wss://prod1.mgn.supersmashflash.com/`).

| From | What | Where |
| --- | --- | --- |
| Host | Match settings: `levelData` (stage, `musicOverride`, `randSeed`, rules), `items` | `MultiplayerManager.toWaitingRoom` exports them; `StageData.startOnlineMatch` sends them with `sendMatchSettings` |
| Each player | Their own `playerSettings` (character, costume index, team) | The three `sendMatchReadySignal` calls in `StageData` |
| Server to everyone, host included | Both, at `startMatch` | `MultiplayerManager.handlemgn_MATCH_START` merges them, seeds RNG from `levelData.randSeed`, then calls `Game.importSettings` |

`GameSettings.importSettings` copies each known field, then fires `SETTINGS_APPLY` with `{settings, data}`. The fields are already assigned when the hook runs. An unknown key inside `levelData` or `playerSettings` would hit the sealed-class check, so nothing new is added there.

Only the host reaches Stage Select. Its `STAGE_SELECT` hook runs before `toWaitingRoom` exports the settings, so whatever a mod writes to `LevelData` there goes to every player. Online is `GameMode` 4 (`ONLINE`) or 14 (`ONLINE_ARENA`).

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `MusicModMatchMusic.writeHostTrack` (host, from `onStageSelect`) | Online, always writes a track into `LevelData.musicOverride`: the jukebox claim, or one weighted playlist pick made there. | `[music_player] net music host pick=<id> value=<s>` |
| `CameraBackgroundMod.writeHostBg` (host, from `onStageSelect`) | Online with a locked background, adds `bg=<stageId>` to the same string. With no background locked nothing is added, and each player keeps their own pick. | `[camera_background] net bg host=<id> value=<s>` |
| `MusicModMatchMusic.onSettingsApply` (every client) | Splits the raw `data.musicOverride` and writes only the track to `settings.musicOverride`. A track this client lacks is cleared, so the stock stage track plays from the shared seed (`Main.RandMusicIndex`). | `[music_player] net music raw=<s> track=<id> local=<id\|stock>` |
| `MusicModMatchMusic.chooseMatchTrackId` | In an online match (`GameMode` 4 or 14) with no usable host track it returns null: no local jukebox claim and no random playlist pick, so players without the host's song all hear the same stock song. | none |
| `CameraBackgroundMod.onSettingsApply` (every client) | Reads `bg` from the raw `data.musicOverride` (not the rewritten field, so hook order does not matter). Sets the `backgroundStage` flag when that stage is installed. | `[camera_background] net bg from=musicOverride id=<id> applied=<bool>` |
| `ColorVaultSync.refreshForOnline` (`ONLINE_MENU_SHOW`) | Drops the cached Color Vault repo index and reruns the preload (unchanged characters are skipped), so everyone online has the latest catalog. At most once every 10 minutes, because the repo index comes from the rate-limited `api.github.com`. | `[palette_maker] online menu: refreshing Color Vault catalog` |
| `PaletteMakerUtils.resolvePlayerSettingsCostumes` | Before the online match loads, fetches the catalog for any match character that was never synced (`hasOnlineCostumeCount` false). | `[palette_maker] net costume resolve players=<n> localId=<id> unsynced=<names>` |
| `PaletteMakerUtils.applyResolvedCostumeToSetting` | For every remote player, applies the rule in [Costumes](#costumes). The engine door `palette.applyResolvedCostumeToSetting` still calls it with two arguments, which leaves the costume alone. | `[palette_maker] net costume char=<name> idx=<from>-><to> online=<n>` |

A vanilla player that receives `trackId|bg=stageId`, or a pack-only track id, plays no music at all. It does not crash: vanilla `ResourceManager.queueResources` traces `Resource could not be found` and skips the id, and `GameController.startMatch` gets a null `musicResource`, so nothing plays. The [room data probe](#room-data-probe) tests a channel that would keep `musicOverride` to vanilla track ids only.

## Room data probe

The host sends `setRoomData` with `{version, protocol, matchSettings}` (`StageData.startOnlineMatch`). Vanilla reads that object only by name: `version` and `protocol` in `OnlineMenu`, `matchSettings` in `MultiplayerManager.handlemgn_MATCH_START`, `GameController` and `MatchResultsMenu`. A new key next to `matchSettings` is therefore ignored by vanilla. The room list reaches clients as a JSON string, which suggests the server stores the object untouched. A key inside `matchSettings` or `playerSettings` is not safe, because those are imported into sealed classes.

| Function | What it does | Log line |
| --- | --- | --- |
| `StageData.startOnlineMatch` (engine) | Builds the room data object, runs `BEFORE_SEND_ROOM_DATA` with `{roomData}`, then sends it. | none |
| `MultiplayerManager.handlemgn_MATCH_START` (engine) | Runs `ROOM_DATA_RECEIVED` with `{roomData}` before it reads `matchSettings`, on every client including the host. | none |
| `MusicModMatchMusic.onBeforeSendRoomData` (host) | Adds `ssf2mm = {v:1, probe:"hello"}`. | `[music_player] net room send ssf2mm=<json>` |
| `MusicModMatchMusic.onRoomDataReceived` (every client) | Lists the keys that arrived. | `[music_player] net room recv keys=<list> ssf2mm=<json\|none>` |

If `ssf2mm` arrives on a Shell guest, the host's track and background move into that key and `musicOverride` only ever holds a track vanilla has. If it is stripped, the next step is a stage-based list every Shell player shares, or no sharing.

## Music override format

`MusicOverrideCodec` (`ssf2mm.shared`):

| Value | `decode` gives |
| --- | --- |
| `bgm_x` | track `bgm_x`, no bg |
| `bgm_x\|bg=finaldestination` | track `bgm_x`, bg `finaldestination` |
| `\|bg=finaldestination` | no track, bg `finaldestination` |
| empty or `null` | nothing |

`withTrack(s, track)` and `withBg(s, bg)` replace one part and keep the other, so `music_player` and `camera_background` can run their `STAGE_SELECT` handlers in either order. Offline values never carry `|`, so old replays decode as a plain track.

## Costumes

The costume is sent as a plain index. The merged list on every PC is the stock list, then the whole Color Vault catalog for that character in repo order (`ColorVaultSync.commitVaultCatalog`, catalog indexes `0..onlineCount-1`), then locally made costumes. The palette menu is only a favorites file of those indexes. Stock and vault picks therefore land on the same costume everywhere.

| Case | Result on the other player's screen |
| --- | --- |
| Stock costume | Same costume |
| Color Vault costume, both synced | Same costume |
| Character never synced on the receiver | Fetched before the match loads, then same costume |
| Locally made costume (catalog index `>= onlineCount`) | Base costume. Without the rule the receiver would show its own local costume at that index. |
| Your own locally made costume | Your costume (the rule only applies to other players) |

`CostumeNetIndex.resolve(costume, mergedList, stockLength, onlineCount, isLocal)` walks the list the same way `ResourceManager.getCostume` does (team entries skipped, the base entry skipped for indexes 0 and up) to find the entry, and returns -1 when that entry is a remote player's local costume.

`PlayerSetting` has no `colorVaultId` field, so player settings online and in replays carry only vanilla keys. The `colorVaultId` tag on costume entries in the local vault list stays: the vault browser's owned check, import de-duplication (`CostumePackIO`) and `getMergedCostumeIndexByColorVaultId` use it.

Different expansion costume sets would shift stock counts between players. That case is not handled.

## Files

- `shell/src/mods/_shared/src/ssf2mm/shared/MusicOverrideCodec.as`, fixture `_shared/fixture/MusicOverrideCodecFixture`
- `shell/src/mods/music_player/src/MusicModMatchMusic.as`
- `shell/src/mods/camera_background/src/CameraBackgroundMod.as`
- `shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/CostumeNetIndex.as`, fixture `palette_maker/fixture/CostumeNetIndexFixture`
- `shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/PaletteMakerUtils.as`, `ColorVaultSync.as`, `shell/src/mods/palette_maker/src/PaletteMakerMod.as`

Run a fixture with `run-music-override-fixture.ps1` (in `_shared/fixture`) or `run-costume-net-fixture.ps1` (in `palette_maker/fixture`).

## Check

1. Both fixtures write `OK`.
2. Two Shell clients in one room. The host locks a camera background, picks a jukebox song and a Color Vault costume; the other player picks a locally made costume.
3. Both `game.log` files show the same `net music ... track=` and `net bg ... id=` values.
4. The host's screen shows the other player in the base costume; the other player's screen shows the host's vault costume.
5. Room data probe: the guest's `game.log` has `net room recv keys=...` with `ssf2mm={"v":1,"probe":"hello"}`. A vanilla guest in the same room plays the match normally.


## References

1. [Built-in mods](built-in-mods.md): the mods, the engine contract and how to rebuild them.
2. [Music player](music-player.md): match music order, claims and playlists.
3. [Costume recolor](costume-recolor.md): how the costume list and palettes are built.
