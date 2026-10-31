---
title: Costume recolor
summary: The base costume is getCostume with num -1. A recolor is a palette pair whose source and replacement differ.
order: 30
group: Characters
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
5. [Bowser cuff colors](#bowser-cuff-colors)
6. [Files](#files)
7. [Check](#check)
8. [References](#references)

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

`PaletteDeep.replacePaletteHelper` skips a pair whose two values are equal, and skips a pair whose RGB matches when the replacement alpha is 0. A later pair still runs.

## Preview log

`applyCostumePreview` logs the frame and the art counts. It does not call `PaletteDeep.paintSample`. `paintSample` calls `countRgbHits`, which copies the portrait with `getVector` and walks every pixel for a `hits=` string. The recolor is `replacePaletteDeep`.

## Leftover colors

The pack **Leftover colors get recolored to base** lives in `expansions/mods/leftover_base_recolor`. Packs in that folder are not essential, so F1 can turn this one off. `enabledByDefault` is false. `info.json` has no `enabled` field, so that default is what the toggle starts from.

`LeftoverBase.mergeBlock` copies the active pairs, then appends a base pair when that source is a recolor on the base and not a recolor on the active block. An active pair wins. `LeftoverBase.fillFromBase` runs that merge on `paletteSwap` and `paletteSwapPA`. A costume flagged `base` is left as it is.

On boot the pack calls `LeftoverBase.scan`. That reads `costume_data` and every `Documents/SSF2 Custom Colors/<name>.json`, runs `fixLists`, and writes `leftover_base_recolor/<name>.json` in application storage for each character that gained pairs. The stock half of that JSON replaces `costume_data[name]` for the session. The files in Documents stay as they are. `COSTUME_LIST_MERGE` calls `applyList`, which puts the custom half onto the merged list `getCostume` clones. With the pack off, `onBoot` does not run, so the game keeps the original `costume_data` and the original custom JSON. The log is `[leftover_base] scan chars= fixed=` and `[leftover_base] apply name= customs=`.

`getCostume` still calls the `leftoverBaseRecolor` patch after the clone. That line is in the engine source until **Compile folder** rebuilds `engine2.swf`. The scan does not wait on that swf. Portraits still pass through `CSS_APPLY_COSTUME`.

## Bowser cuff colors

The current Bowser sheet lists nine source colors the older costumes do not. `PaletteDeep.replacePaletteHelper` only replaces a listed pair, so a cuff pixel in one of those colors stays the source color.

| Source | Replacement used for a missing pair |
| --- | --- |
| `6D4811` | Blend of `A36717` and `4F340C` by source brightness |
| `352207` | `4F340C` darkened by the source brightness ratio |
| `605A59`, `3B3635` | `282322` lightened toward white by the source brightness gap |
| `7FFFC5`, `59B88B`, `397D5D`, `193327` | The gray results above, then `282322`, then `000000` |
| `4D97FF` | `006AFF` lightened toward white by the source brightness gap |

A pair that already replaces one of the nine stays. A blank slot, where the replacement still equals the source, is filled when the neighbor was recolored. A block whose pairs are all equal to their sources stays as it is. The fighter block copies `420060`, `A99963`, and `DB00BE` from that costume's portrait block when the fighter is missing them, and takes `180101` from `000000`.

That fill was applied once to `Documents/SSF2 Custom Colors/bowser.json`. The same pairs were written into Color Vault `bowser.as` for each catalog entry whose old palette matched that file. The catalog order stays put. A launch leaves the local file alone when [Online sync](online-sync.md) sees the same vault file size in `localVaultIndexMatches`. Opening the online menu runs `ColorVaultSync.refreshForOnline`.

## Files

| What | Path |
| --- | --- |
| `getCostume` | `SSF2 Shell/data/engine/decompiled/changed/com/mcleodgaming/ssf2/util/ResourceManager.as` |
| `updateColorFilter`, `applyPalette`, `setPaletteSwap` | `SSF2 Shell/data/engine/decompiled/changed/com/mcleodgaming/ssf2/engine/InteractiveSprite.as` |
| `replacePaletteHelper` | `SSF2 Shell Base/shell/src/mods/palette_maker/src/com/mcleodgaming/ssf2/util/PaletteDeep.as` |
| Leftover pack | `SSF2 Shell/expansions/mods/leftover_base_recolor/` |
| Merge | `LeftoverBase.mergeBlock` in that pack's `src/LeftoverBase.as` |
| Fixture | `leftover_base_recolor/fixture` (`LeftoverBaseFixture`) |

`PaletteMakerMod` patches `palette.getCostume` (`ModPatches.PALETTE_GET_COSTUME`). Optional packs live in `expansions/mods`. Essential packs live in `data/ssf2mm`, with source in `src/mods`. Compile from [Where things are](where-things-are.md).

## Check

From `leftover_base_recolor/fixture`, compile with `mxmlc -load-config+=air-config.xml -source-path=. -source-path=../src -output LeftoverBaseFixture.swf LeftoverBaseFixture.as`, then `adl -nodebug LeftoverBaseFixture-app.xml .`. `%APPDATA%\LeftoverBaseFixture\Local Store\leftover-base-fixture.txt` says `OK`. The fixture calls `fixLists` and `applyList`. Those are the functions the boot scan and `COSTUME_LIST_MERGE` call. The original costume object stays unchanged, which is the disabled path.

In the game, turn the pack on from F1. It is off until then. A full quit loads `leftover_base_recolor/mod.swf` and `data/ssf2mm/expansions/mod.swf`. `getMergedCostumeList` runs `COSTUME_LIST_MERGE`. The log `[leftover_base] scan chars= fixed=` has `fixed` above 0 when any costume gained a pair. Pick an alternate whose code skips a base recolor. The portrait and the fighter both use that JSON. A custom color also logs `[leftover_base] apply name= customs=` with a count above 0.

## References

1. [Where things are](where-things-are.md) — mod folders and the compile command.
2. [Character select portrait](character-select-portrait.md) — how a palette pick paints the portrait the player sees.
3. Wiki app and downloads — the Info, Contents, and References shape.
4. [Online sync](online-sync.md) — which costume index other players see online.
