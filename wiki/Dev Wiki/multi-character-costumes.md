---
title: Multi-character costumes
summary: Linked forms edit one character at a time. Save pairs both files at the same game costume index.
order: 60
group: Characters
---

# Multi-character costumes

## Info

| Multi-character costumes |
| --- |
| Pair check | `PaletteMakerUtils.isFormLinked` |
| Partner | `getLinkedFormPartner` |
| Studio | One form on screen. Portrait, swatches, and unique sprites are that form only |
| Save | Linked: costume palette menu first, then `performSaveCostume`. Other forms save immediately |
| Pair | Save opens the PR popup list. Pick loads `partnerPaletteData`, then save runs |
| Giga studio | Picker icon `gigabowser`. Spawns Bowser with unlimited final and keeps Final Smash on. The default costume is Giga 1, palette 0 |

Zelda, Sheik, Bowser, and Giga Bowser each keep their own palette. The studio shows one form. Save writes the picked partner palette onto the opened costume and into the partner file at the same game costume index.

## Contents

1. [How it works](#how-it-works)
2. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `getCharsCompatibleWithCostumes` | Inserts `gigabowser` after Bowser so Giga has a stock icon | — |
| `displayNameFor` | Vault hover and locker title. `gigabowser` is Giga Bowser | — |
| `LinkedFormPaint.spawnStatsName` | `gigabowser` spawns as `bowser` so Final Smash can run | — |
| `maintainGigaStudioForm` | Skips entrance. After revival and a short delay, `forceAttack("special")` | `[Palette Maker] giga studio final` |
| Studio PA | Giga does not show a portrait | — |
| `matchRestAreaZoom` | Giga uses a farther `MinZoomHeight` so the stage fits | `[Palette Maker] studio zoom min=` |
| `buildGigaBrowseCostumeList` | Giga locker lists stock Giga palettes and vault customs. No metal/team from Bowser | — |
| `PaletteMakerMenu` show | Loads `partnerPaletteData` for save and clipboard. Does not draw a partner portrait or alt swatches | — |
| `saveCostume` | Writes one character file. `atIndex` replaces that slot. A slot past the end is padded so the partner keeps the opened form's game costume index | `[Palette Maker] save char= index=` |
| `saveCostume_CLICK` | Linked form: sets `pendingSaveAfterPair` and opens the costume palette menu. Else calls `performSaveCostume` | — |
| `openPartnerPairing` | Opens the costume palette menu on the partner | `[Palette Maker] pair menu partner=` |
| `_onPairingClickContainer` | Loads the palette-menu pick into `partnerPaletteData`, then calls `performSaveCostume` when save started the pair | `[Palette Maker] pair partner=` |
| `performSaveCostume` | Writes `currentPaletteData` with `pairedPalette`, and `partnerPaletteData` at `CostumeNetIndex.partnerFileIndex` | `[Palette Maker] pair file=` |
| `exportCostume_CLICK` | Clipboard still packs both costumes at the paired index | — |
| `shouldCarryCostumeOnTransform` | Zelda and Sheik return false. Bowser and Giga return false. Sonic still carries into Super Sonic. Inject stays off for Bowser and Giga | `[Palette Maker] carry from= to= result=` |
| `onAfterStancePaint` | When the costume name matches the stats name, stance paint applies that slot from the Sonic file. The auto-form scan cannot block that apply. Bowser and Giga stay on the stock transform path | `[palette_maker] stance enter stats=` |
| `livePaintBlock` | The opened form keeps its block. Sheik, Zelda, and Giga Bowser keep their own block when it has a recolor. An identity partner block is not painted, so the opened form's no-recolor palette does not replace the transform | `[Palette Maker] giga live index=` |
| `onAfterTransform` | Down special uses `CostumeID`. A studio pick sets that to `getSelectedCostume()`, the same game index on the other form. `-2` is the base, so an unpicked slot stays the base. Giga Bowser's default palette is Giga 1 | `[Palette Maker] form costume stats=` |
| `engineFormLinked` | Bowser and Giga return false. `activateFinalForm` keeps stock `reapplyCostume` | — |
| `fighterForm` | Live form follows `CurrentStatsName` when the fighter transforms | `[Palette Maker] live form=` |
| `filterCoverToForm` | Unique strip keeps the opened form. Giga Bowser keeps Giga names only | — |


## References

1. [Palette maker studio](palette-maker-studio.md) — the studio bar and home stages.
2. [Costume recolor](costume-recolor.md) — how a saved costume is applied in game.
3. [Giga Bowser HUD](giga-bowser-hud.md) — match HUD paint after Final Smash.
