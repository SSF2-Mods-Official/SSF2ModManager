---
title: Stage asset swap
summary: A stage slot that loads an existing stage and swaps its bitmap images for pngs in a pack. No stage SWF is copied.
order: 46
---

# Stage asset swap

## Info

| Stage asset swap |
| --- |
| Pack | `expansions/stage_assets/<id>/` |
| Make Mods | `StageAssetSwapStore.Create` |
| Slot | [Stage background](stage-background.md) — stage select, not `mappings.stage` |
| Swap | `StageAssetSwap.Swap` |
| Related | [Stage background](stage-background.md) |

A stage asset swap is the source stage plus replacement pictures. The pack has no stage SWF.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How to use it

Make Mods → Stage → **Stage asset swap**. Pick the source stage and a name. Create writes the pack and lists each bitmap. **Open folder** opens `assets/`. Edit the pngs there. The next pick of that slot loads the source stage with those pictures.

## How it works

A bitmap in the stage file has no linkage name. The image tag starts with a character id. Shapes point at that number. `StageAssetSwap.List` records the number, the width, and the height. `StageAssetSwapStore.Create` writes one png per bitmap and does not copy a SWF. `StageAssetSlot.launch` on a stage-select pick, and `StageAssetLaunch.hold` on a direct CSS match, read the stock stage file, and `StageAssetSwap.swap` replaces each tag whose character id is in `assets.json` when the png is the same width and height. The resource `UrlOverride` points at that buffer so `Resource.load` plays it. `ExpansionStageResources.ensureStageClip` builds the stage clip from `stage_<sourceStage>`, because that is the class name inside the file. Collisions, music, and hazards stay the source stage's. `StageAssetCatalog.rememberName` writes the pack name onto `metadata.stage` so `MatchResultsMenu.displayMatchResults` can read it.

| Function | What it does | Log line |
| --- | --- | --- |
| `StageAssetSwap.List` | Character id, width, and height of each image tag. | — |
| `StageAssetSwap.Swap` | Replaces those tags' pixels in a buffer. The stock file is not written. | — |
| `StageAssetSwapStore.Create` | Writes `info.json`, `assets.json`, the pngs, and `stagepreview.png`. No `.swf`. | — |
| `StageAssetSlot.launch` | Builds that buffer from the pngs and loads it as the slot. | `[stage_asset] launch slot=… source=… images=…` |
| `StageAssetLaunch.hold` | A direct CSS match waits until that buffer is on the slot and `stage_asset.source.<id>` is set. | `[stage_asset] launch file=…` |
| `ExpansionStageResources.ensureStageClip` | Builds the stage clip. A swap slot uses `stage_<sourceStage>`. | `[stage_asset] clip id=… linkage=stage_…` |
| `StageAssetCatalog.rememberName` | Puts the pack name on `metadata.stage[slot id]` for the results screen. | `[stage_asset] name id=… name=…` |

`info.json` has `name`, `id`, `type` = `StageAssetSwap`, `sourceStage`, and `enabled`. `assets.json` lists `characterId`, `file`, `width`, and `height`.

## Files

| File | What it is |
| --- | --- |
| `src/Services/StageAssetSwap.cs` | `List` and `Swap` |
| `src/Services/StageAssetSwapStore.cs` | Writes the pack |
| `src/Views/MakeModsWindow.xaml` | Stage asset swap |
| `SSF2 Shell/src/mods/menu_editor/src/StageAssetSwap.as` | Replaces the bitmap tags in the buffer |
| `SSF2 Shell/src/mods/menu_editor/src/StageAssetSlot.as` | Loads the buffer for the slot |
| `SSF2 Shell/src/mods/_shared/src/ssf2mm/shared/StageAssetCatalog.as` | Reads the pack |

## Check

`StageAssetSwapStore.Create` from a SWF with two lossless bitmaps writes both pngs and `assets.json`, and does not write a `.swf`. `StageAssetSwap.Swap` puts the replacement pixel in the buffer.


## References

1. [Stage background](stage-background.md) — a stage-select slot that is a folder, not a stage SWF.
2. Where things are — the shell folder the pack is written into.
