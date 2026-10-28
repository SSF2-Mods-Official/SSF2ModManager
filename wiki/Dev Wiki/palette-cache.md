---
title: Palette cache
summary: Costume sprite caches stay on the player's machine. A matching file hash is fresh.
order: 13
---

# Palette cache

## Info

| Palette cache |
| --- |
| Fresh | `characterFileHash` matches the `hash` stored in the cache |
| Folder | `Documents/SSF2 Custom Colors` |
| Built by | Setup launches the shell with `vaultAction` `cachewarm` |
| Marker | `expansions/state/palette-cache-warm.done` |
| Related | [Costume recolor](costume-recolor.md) |

The palette maker's sprite lists are cached on the player's machine. A character pack does not include them.

## Contents

1. [How it works](#how-it-works)
2. [Setup](#setup)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How it works

`characterFileHash` is an FNV-1a of the character file. `isDiscoverCacheFresh` and `isUniqueSpriteCacheFresh` are fresh when that hash equals the `hash` field and the file `version` is discover 3 or unique 14. Discover JSON also stores `alt`, the colors that belong only to the second form. An older version is a miss, so the next open scans again. A stored hash that does not match the file is a miss. A new modification time or a new file size does not schedule a warm. The effect cache stores the same hash.

An expansion character uses that same folder. The cache is not written into the character pack, because the effect cache stores sprite bitmaps. Installing the pack warms that character on this machine.

`startPaletteMakerFreshnessCheck` returns an empty list, so the manage-costumes grid does not ask to cache the roster. Costume gallery launches `vaultAction` `manage`. `openManageCostumesGrid` calls `vaultMenu.showManageCostumesGrid`, then `holdCostumeGrid` so the icon under the cursor is not opened. Make Mods → Palette maker is the editor.

`showPaletteMakerLoadingScreen` returns the clip to frame 1, then puts that character's series icon on `series`. The logo stays up at most 700ms, then `gotoAndPlay(34)` plays the closing animation. The cover comes off when that animation finishes.

| Function | What it does | Log |
| --- | --- | --- |
| `characterFileHash` | FNV-1a of the character file | None |
| `isDiscoverCacheFresh` | True when the discover JSON hash matches | `needsWarm?` when `CACHE_DEBUG` is on |
| `isUniqueSpriteCacheFresh` | True when the unique JSON hash matches | `needsWarm?` when `CACHE_DEBUG` is on |
| `startPaletteMakerFreshnessCheck` | Returns no pending characters, so the grid does not ask to cache | None |
| `openManageCostumesGrid` | Opens the manage-costumes character grid | `manage costumes grid` |
| `holdCostumeGrid` | Ignores the icon under the cursor for 400ms | `costume grid hold` |
| `showPaletteMakerLoadingScreen` | Returns to frame 1, then attaches that character's series icon | `loading screen series` |
| `startPaletteMakerPreload` | Walks characters whose hash misses. `exitWhenDone` quits when the walk finishes | `[PaletteMakerUtils] Palette Maker cache warm` |

## Setup

First-time setup launches the shell once after the shell exists, with `vaultAction` set to `cachewarm`. The manager does that only when `manager_bridge` and `palette_maker` `mod.swf` contain the warm, so an uncompiled shell is left alone. The game writes the discover, unique, and effect caches under `Documents/SSF2 Custom Colors`, writes `palette-cache-warm.signal`, and quits. The manager then writes `palette-cache-warm.done`. A later launch with the same character bytes does not run that walk again.

Installing a character pack warms that character into the same folder. The pack folder is not changed.

## Files

| What | Path |
| --- | --- |
| Hash and freshness | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/PaletteMakerUtils.as` |
| Cache JSON | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/SpriteCacheStore.as` |
| Warm | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/CacheWarmPreload.as` |
| Boot | `SSF2 Shell/src/mods/manager_bridge/src/StreamBootIntro.as` |
| Loading screen | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/PaletteLoadingScreen.as` |
| Manager | `src/Services/PaletteCacheWarmService.cs` |

## Check

`PaletteCacheWarmServiceTests` launches nothing when the shell folder has no game exe, and it does not add a file under the character pack.

## References

1. [Costume recolor](costume-recolor.md) — how a palette is applied.
2. Shell base — the setup that builds the shell before this warm.
