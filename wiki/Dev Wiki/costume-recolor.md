---
title: Costume recolor
summary: The base costume is getCostume with num -1. A recolor is a palette pair whose source and replacement differ.
order: 12
---

# Costume recolor

## Info

| Costume recolor |
| --- |
| Base costume | `getCostume(name, null, -1)` |
| Palette block | `paletteSwap`, `paletteSwapPA` |
| Applied by | `updateColorFilter` → `setPaletteSwap` |
| Source | `ResourceManager.as`, `InteractiveSprite.as` |
| Related | [Character select portrait](character-select-portrait.md) |
| Optional pack | `leftover_base_recolor` |

The base costume is the palette used when no alternate is selected. An active costume replaces only the colors listed in its own code.

## Contents

1. [How it works](#how-it-works)
2. [Palette block](#palette-block)
3. [Preview log](#preview-log)
4. [Leftover colors](#leftover-colors)
5. [Files](#files)
6. [Check](#check)
7. [References](#references)

## How it works

`ResourceManager.getCostume(charName, teamColor, num)` returns a clone of one costume. `num` of `-1` is the entry flagged `base`.

| Function | What it does | Log |
| --- | --- | --- |
| `getCostume(name, null, -1)` | Returns the costume flagged `base`. | None yet. |
| `updateColorFilter` | Loads that costume and calls `setPaletteSwap(paletteSwap, paletteSwapPA)`. | None yet. |
| `leftoverBaseRecolor` | After the clone, `getCostume` asks this patch to fill the costume. With the pack off, the costume is unchanged. | `[leftover_base] name= swap= pa=` |
| `applyPalette` | Copies `m_sprite.filters` onto the clip. | None yet. |
| `setPaletteSwap` | Stores the two blocks and paints the health box and the stance. | None yet. |

## Palette block

A block is `{ colors, replacements }` paired by index. A pair counts as a recolor when the source and the replacement differ.

If `paletteSwapPA` is missing and `paletteSwap` is set, `getCostume` copies `paletteSwap` onto `paletteSwapPA`.

`PaletteMakerUtils.buildEngineReplMapFromBlock` skips a pair whose RGB matches and whose replacement alpha is 0. `Utils.replacePaletteHelper` walks pairs in order and skips a pair whose two values are equal. A later pair still runs.

## Preview log

`applyCostumePreview` logs the frame and the art counts. It does not call `PaletteDeep.paintSample`. `paintSample` calls `countRgbHits`, which copies the portrait with `getVector` and walks every pixel for a `hits=` string. The recolor is `replacePaletteDeep`.

## Leftover colors

The pack **Leftover colors get recolored to base** lives in `expansions/mods/leftover_base_recolor`. Packs in that folder are not essential, so F1 can turn this one off. `enabledByDefault` is false. `info.json` has no `enabled` field, so that default is what the toggle starts from.

`LeftoverBase.mergeBlock` copies the active pairs, then appends a base pair when that source is a recolor on the base and not a recolor on the active block. An active pair wins. `LeftoverBase.fillFromBase` runs that merge on `paletteSwap` and `paletteSwapPA`. A costume flagged `base` is left as it is.

On boot the pack calls `LeftoverBase.scan`. That reads `costume_data` and every `Documents/SSF2 Custom Colors/<name>.json`, runs `fixLists`, and writes `leftover_base_recolor/<name>.json` in application storage for each character that gained pairs. The stock half of that JSON replaces `costume_data[name]` for the session. The files in Documents stay as they are. `COSTUME_LIST_MERGE` calls `applyList`, which puts the custom half onto the merged list `getCostume` clones. With the pack off, `onBoot` does not run, so the game keeps the original `costume_data` and the original custom JSON. The log is `[leftover_base] scan chars= fixed=` and `[leftover_base] apply name= customs=`.

`getCostume` still calls the `leftoverBaseRecolor` patch after the clone. That line is in the engine source until **Compile folder** rebuilds `engine2.swf`. The scan does not wait on that swf. Portraits still pass through `CSS_APPLY_COSTUME`.

## Files

| What | Path |
| --- | --- |
| `getCostume` | `SSF2 Shell/data/engine/decompiled/changed/com/mcleodgaming/ssf2/util/ResourceManager.as` |
| `updateColorFilter`, `applyPalette`, `setPaletteSwap` | `SSF2 Shell/data/engine/decompiled/changed/com/mcleodgaming/ssf2/engine/InteractiveSprite.as` |
| `buildEngineReplMapFromBlock` | `SSF2 Shell/data/ssf2mm/palette_maker/src/com/sbxmod/paletteMaker/PaletteMakerUtils.as` |
| Leftover pack | `SSF2 Shell/expansions/mods/leftover_base_recolor/` |
| Merge | `LeftoverBase.mergeBlock` in that pack's `src/LeftoverBase.as` |
| Fixture | `leftover_base_recolor/fixture` (`LeftoverBaseFixture`) |

`PaletteMakerMod` patches `palette.getCostume` (`ModPatches.PALETTE_GET_COSTUME`). Optional packs live in `expansions/mods`. Essential packs live in `data/ssf2mm`, with source in `src/mods`. Compile from Where things are.

## Check

From `leftover_base_recolor/fixture`, compile with `mxmlc -load-config+=air-config.xml -source-path=. -source-path=../src -output LeftoverBaseFixture.swf LeftoverBaseFixture.as`, then `adl -nodebug LeftoverBaseFixture-app.xml .`. `%APPDATA%\LeftoverBaseFixture\Local Store\leftover-base-fixture.txt` says `OK`. The fixture calls `fixLists` and `applyList`. Those are the functions the boot scan and `COSTUME_LIST_MERGE` call. The original costume object stays unchanged, which is the disabled path.

In the game, turn the pack on from F1. It is off until then. A full quit loads `leftover_base_recolor/mod.swf` and `data/ssf2mm/expansions/mod.swf`. `getMergedCostumeList` runs `COSTUME_LIST_MERGE`. The log `[leftover_base] scan chars= fixed=` has `fixed` above 0 when any costume gained a pair. Pick an alternate whose code skips a base recolor. The portrait and the fighter both use that JSON. A custom color also logs `[leftover_base] apply name= customs=` with a count above 0.

## References

1. Where things are — mod folders and the compile command.
2. [Character select portrait](character-select-portrait.md) — how a palette pick paints the portrait the player sees.
3. Wiki app and downloads — the Info, Contents, and References shape.
4. [Online sync](online-sync.md) — which costume index other players see online.
