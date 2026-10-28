---
title: Resource cache
summary: Which loaded SWFs stay in memory, and which flush drops.
order: 90
group: Build
---

# Resource cache

## Info

| Resource cache |
| --- |
| Where | `ResourceManager` in engine2 |
| Kept | Menus, modes, audio, and expansions. A character, stage, or song stays when its id is in the load queue |
| Drop | `flushUnusedResources` unloads the others |
| Log | `[ResourceManager] flush dropped=` |

A character, stage, or song SWF stays decoded until `flushUnusedResources` unloads it.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

`ResourceManager.shouldDropResource` is the keep test. A menu, mode, audio clip, or expansion stays. A character, stage, or song stays when `queueHash` has its id. Anything else in that trio drops. `ResourceManager.load` runs the flush when the caller passes `flushUnused`. Match start queues the fighters and the stage, then loads with that flag. `GameController.destroyStageData` flushes after results leave, when the queue is empty, so the stage and the song drop. `endMatch` does not. `endGame` still reads `stageData` after `endMatch` returns. `Resource.unload` calls `SwfRelease.release`. That loads each SWF into a child of the current application domain and, on unload, calls `unloadAndStop`. `SwfRelease.forget` drops library-cache classes defined by that SWF. The fixture `SwfReleaseFixture` loads `Heavy` four times, caches each class under the wrong id, forgets them, and releases. Each `Heavy` holds a 16 MB bitmap and an 8 MB byte array. After release, the Flash heap has to come back within 6 MB of the start. `promoteResource` moves a queued id to the end of the list. `unloadOldResources` is the backup cap on a load that does not flush: it drops characters, stages, and songs down to the cap, from the front of the list.

| Function | What it does | What you see |
| --- | --- | --- |
| `ResourceManager.shouldDropResource` | True for a character, stage, or song that is not in the queue | The id is in the flush |
| `ResourceManager.flushUnusedResources` | Unloads every id `shouldDropResource` accepts | `[ResourceManager] flush dropped=` and one `Unloaded resource` line per id |
| `ResourceManager.load` | Flushes first when `flushUnused` is set. Otherwise runs the cap | The next match does not keep the previous stage |
| `GameController.destroyStageData` | Flushes after the stage is disposed. `endMatch` leaves `stageData` in place | `flush dropped=` when results close |
| `Resource.unload` | Calls `SwfRelease.release`: `unloadAndStop` on that loader. `collectUnloaded` runs `System.gc` on the next tick | `[ResourceManager] drop settled id=` with `flashSettled`, `loadedChars`, `poolMovies`, and `cacheKeys` |
| `ResourceManager.promoteResource` | Moves that id to the end of `resources` | The cap drops a different id than the one just queued |
| `ResourceManager.unloadOldResources` | Drops characters, stages, and songs from the front until each count is inside the cap | A load without `flushUnused` still sheds extras |

## Files

| File | Role |
| --- | --- |
| `SSF2 Shell Base/patches/engine2/patches/com/mcleodgaming/ssf2/util/ResourceManager.as.patch` | Keep test, flush, cap, promote |
| `SSF2 Shell Base/patches/engine2/added/com/mcleodgaming/ssf2/util/SwfRelease.as` | Load context and `release` |
| `SSF2 Shell Base/patches/engine2/patches/com/mcleodgaming/ssf2/controllers/GameController.as.patch` | Flush when the match display is gone |
| Match load | Passes `flushUnused` |
| `SSF2 Shell Base/shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/PaletteMakerMenu.as` | Rest-area match load passes `flushUnused` and keeps the queued studio character |

## Check

Play two matches with different fighters and stages. After results close, `game.log` has `[ResourceManager] flush dropped=` above 0. The second match's log names `Unloaded resource` for a fighter or stage from the first match.


## References

1. [Where things are](where-things-are.md) — engine2 is the SWF that contains `ResourceManager`.
