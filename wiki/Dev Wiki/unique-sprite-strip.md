---
title: Unique sprite strip
summary: The unique-sprite scrollbar is on the left of the strip.
order: 40
group: Characters
---

# Unique sprite strip

## Info

| Unique sprite strip |
| --- |
| Scrollbar | Left of the sprites, width 18 |
| Rows | 2 visible |
| Related | [Costume recolor](costume-recolor.md) |

The strip under the portrait packs each unique sprite by its opaque bounds. `measureUniqueSpritePackSize` records the opaque box in `uniquePaContent` space, so the left rail does not shift the sprites under the mask. `fitUniquePaSprite` uses `stripHeightScale` so a tall sprite's height matches the 48-pixel row. `exportColorSheet` still draws that sprite.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [References](#references)

## How it works

`layoutAllUniquePaSprites` places the scrollbar at x `0`. It lays out for 90 frames. A pack width that moves by more than 8 pixels restarts that wait. The bar shows as soon as the pack width is above the 48-pixel floor and the rows still overflow. `anchorPortraitOnce` restarts the wait when the portrait lands. `afterSwatchRowScroll` puts the tool row above the swatches. `updateUniquePaPalettes` paints the strip again at layout frames 3, 12, and 45, and again when the mouse enters the strip. The sprite content and its mask start at x `20` (`UNIQUE_SCROLLBAR_WIDTH` plus 2). The pack width still stops before the portrait.

| Function | What it does | Log |
| --- | --- | --- |
| `layoutAllUniquePaSprites` | Packs the strip for 90 frames. A width change of more than 8 pixels restarts that wait. The scrollbar shows when the rows still overflow and the pack width is above the 48-pixel floor | `[Palette Maker] unique pack w=` |
| `updateUniquePaPalettes` | Paints the strip again at layout frames 3, 12, and 45, and when the mouse enters the strip | None |
| `spriteTooBigForStrip` | After `fitUniquePaSprite`, a sprite whose on-screen width is at least twice the median sprite stays off the strip. Lloyd's three sprites are `lloyd_rheaird0`, `lloyd_fs36`, and `lloyd_fsAtkPA0` (670×160). The wide one is the one that stays off. `exportColorSheet` still draws it | `[Palette Maker] unique size` |
| `measureUniqueSpritePackSize` | Measures the opaque box in the content sprite, to the right of the rail | None |
| `exportColorSheet` | One form's portrait, unique sprites, and palette row | — |
| `filterCoverToForm` | Drops the other linked form. Giga Bowser keeps a linkage only when the name is Giga (`giga`, `giga_bowser`, or `gbow_`). A Zelda prop whose name does not start with either form stays | — |
| `getUniqueSpritePackMaxWidth` | Width from the strip origin to the portrait, minus the rail. A missing portrait measures 48. `anchorPortraitOnce` restarts the strip layout when the portrait lands | None |
| `buildCharacterLinkageList` | The scan checks every linkage id except a name containing `old`. The Bowser file is scanned whole. `filterCoverToForm` keeps Giga names on Giga Bowser and drops them from Bowser. `sampleOpaqueColors` reads every pixel. One pixel keeps a color. A color enters the palette from the representative sprite that added it | `[Palette Maker] unique colors added` |
| `fitUniquePaSprite` | `stripHeightScale` sets a tall sprite's height to 48, the same cap as the sprites around it | None |
| `selectUniqueStrip` | One walk keeps a sprite that adds a color. A count over that short list drops a sprite that has no color of its own. Later sprites are checked first so an earlier sprite can keep the letter. `collectPaColors` is the portrait picture sample. The portrait swatch list is not that set. A loaded cache is passed through this again. Unique cache version is 29 | `[PMCache] unique select` |
| `darkenExceptColors` | Ctrl+Q and the toolbar scan button toggle the strip. Turning it off paints `currentPaletteData`, the active costume, not the base block. `ownedColorsForStrip` gives a shared color to the first sprite that lists it. Later sprites wash that color. A portrait color stays washed. A pixel stays bright when its source color is within Manhattan 16 of a color that sprite owns, or of the palette color that owned color was repainted to. `washOwnedTowardWhite` sends the other pixels toward white when that owned color is dark, and toward black when it is light. An empty owned list leaves the sprite unchanged. `drawOwnedRing` draws a red ring 3 pixels outside the kept pixels | None |
| `paintUniqueSpritesForSheet` | `giga_bowser`, `gigabowser`, and `gbow_` use the Giga palette. A `bowser_` linkage stays on the opened Bowser palette. `sheik_` and `sdairland` use Sheik. `zelda_` uses Zelda. A Zelda linkage does not become Sheik when an extra id is `zelda`. A sprite is painted with that form's body block, or with the fighter's live swap when the editor block is identity. A thrown linkage id does not skip the other sprites | `[Palette Maker] paint form=` |
| `liveBodySwap` | When the on-screen form is Giga and there is no partner block, the costume is `GigaStockPalettes.costume` at `gigaPaletteIndex`. Costume `-1` is index 0. The studio character is not `BowserExt` | `[Palette Maker] giga live index=` |
| `setUniqueScanStatus` | The game root and the loading screen say `Scanning unique sprites` with the linkage count while that walk runs | None |
| `sampleDisplayColorAt` | Skips a unique sprite when `uniqueRowIsVisible` is false. A row outside the 2-row window does not highlight. The toolbar title shows the highlighted palette swatch as `#RRGGBB`, including a portrait swatch under the pointer. Ctrl+C copies that hex | None |
| `HoverDim.paint` | Hovering a swatch multiplies every portrait and unique-strip pixel by 0.1, then copies back pixels whose RGB is that swatch's body or portrait replacement. The hovered color stays bright. The fighter stays on the active costume. `scheduleHoverDim` waits one frame on `Ssf2.Main.Root`. MovieClips on the strip stay bright | None |


## References

1. [Costume recolor](costume-recolor.md) — the palette those sprites are painted with.
