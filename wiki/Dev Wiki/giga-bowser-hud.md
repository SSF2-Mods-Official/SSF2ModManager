---
title: Giga Bowser HUD
summary: The player portrait uses the live Giga Bowser palette.
order: 16
---

# Giga Bowser HUD

## Info

| Giga Bowser HUD |
| --- |
| Portrait | `HealthBox.charHead` |
| Live block | `paletteSwapPA` |
| Related | Where things are |

`paintLivePortrait` restores the cached portrait and applies the live `paletteSwapPA` for Bowser and Giga Bowser. Where things are lists the palette maker sources.

## Contents

1. [How it works](#how-it-works)
2. [Swatches](#swatches)
3. [Clipboard](#clipboard)
4. [References](#references)

## How it works

Bowser's `applyGigaBowserPalette` stores the Giga block with `setPaletteSwapData`. That call does not paint the portrait. `setStats` loads the Giga thumbnail and paints whatever `paletteSwapPA` is stored. `activateFinalForm` then calls `reapplyCostume`, and `CostumeName` is `NormalStatsID` while transformed, so the portrait is painted with Bowser's palette. The next stance paint puts the stored Giga palette back on the portrait.

| Function | What it does | Log |
| --- | --- | --- |
| `paintLivePortrait(statsName, portrait, paletteSwapPA)` | Restores the cached portrait pixels, then applies the live `paletteSwapPA` when the stats name is `bowser` or `gigabowser`. The cache is a Dictionary on `LinkedFormPaint`. | `[Palette Maker] form hud pa stats=` |
| `BowserExt.applyGigaBowserPalette` | Stores the Giga `paletteSwap` and `paletteSwapPA` for the current costume index. | |
| `Character.activateFinalForm` | `reapplyCostume` paints the portrait from `CostumeName`. | |

## Swatches

`formSwatchLists` keeps Bowser's colors and Giga Bowser's colors as two lists, including a color that is in both. `placeFormSwatches` draws the opened name, then that list, then the partner name, then the partner list. The name is 13 pixels, not a swatch row. A partner swatch has `altForm` set, and that edit writes `partnerPaletteData`. `updateMagHoverPaletteHighlight` highlights the partner swatch when the pointer is on the partner portrait or a partner sprite. `swatchListsForOpened` puts the opened character's colors on the opened row, so Sheik's row is the scan alt list. Hovering that swatch isolates the partner portrait. The opened portrait stays on the opened palette. Zelda's portrait does not take Sheik's `paletteSwapPA`.

| Function | What it does | Log |
| --- | --- | --- |
| `formSwatchLists(baseColors, altColors)` | Returns both lists. A shared color stays in each list. | None |
| `placeFormSwatches` | Packs the two lists with a 13-pixel name and no empty swatch row | None |
| `updateMagHoverPaletteHighlight` | A partner portrait or partner sprite hit highlights the partner swatch. Sheik's opened row is the scan alt list. A partner swatch hover isolates the partner portrait | None |
| `swatchListsForOpened` | The opened row is that character's colors. Sheik uses the scan alt list. Zelda uses the scan base list | None |
| `hitUsesPartnerSwatch` | The partner portrait and a partner linkage use the partner swatch | None |
| `applyLinkedPortraitPalettes` | The opened portrait uses the opened `paletteSwapPA`. The partner portrait uses the partner `paletteSwapPA` | None |

## Clipboard

Copy for Bowser writes a `characters` object with `bowser` and `gigabowser`. Each name holds that form's costume.

| Function | What it does | Log |
| --- | --- | --- |
| `copyCostumeToClipboard` | Bowser's clipboard is `{ characters: { bowser: [...], gigabowser: [...] } }` | None |


## References

1. Where things are — palette maker sources and the docs folder.
