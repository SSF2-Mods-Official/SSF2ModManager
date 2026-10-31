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
| Studio | Zelda and Sheik also spawn the other form as player 2 on the same costume index, at player 1's spawn. Both are team 1 and team attack is off. The training HUD sets player 2 to CPU; the studio sets them human again so player 1's pad moves both. Player 1's bindings are copied onto pad 2 until the studio closes. Start stays on player 1. Portrait, swatches, and unique sprites stay the opened form |
| Save | Zelda, Sheik, Bowser, and Giga Bowser open a pair list of local open slots plus Base. Base saves this costume and writes `pairBase` on the partner so the other form can pair with it |
| Pair | The pair list skips vault indexes. A filled local slot leaves the list |
| Giga studio | Picker icon `gigabowser`. Spawns Bowser with unlimited final and keeps Final Smash on. The default costume is Giga 1, palette 0 |

Zelda, Sheik, Bowser, and Giga Bowser each keep their own palette. Zelda and Sheik load a second fighter, the other form, on that same costume index and at player 1's spawn. Both are team 1, team attack is off, and player 1's pad drives both after the studio clears the training HUD's CPU. Start stays on player 1. Save writes the picked partner palette onto the opened costume and into the partner file at the same game costume index. A vault file keeps only that character's colors.

## Contents

1. [How it works](#how-it-works)
2. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `getCharsCompatibleWithCostumes` | Inserts `gigabowser` after Bowser so Giga has a stock icon | — |
| `displayNameFor` | Vault hover and locker title. `gigabowser` is Giga Bowser | — |
| `LinkedFormPaint.spawnStatsName` | `gigabowser` spawns as `bowser` so Final Smash can run | — |
| `LinkedFormPaint.studioMirrorPartner` | Zelda returns Sheik and Sheik returns Zelda. Bowser and Giga return nothing, so those studios stay one player | — |
| `startGame` | A mirror partner starts a two-player training match. Player 2 is that form, human, costume `-1`, team `1`. Player 1 is team `1`. `LevelData.teamDamage` is false | `[Palette Maker] pair puppet partner=` |
| `bindStudioMirrorInput` | Copies pad 1's bindings onto pad 2 and points `ModHost.mirrorInput` at pad 1. Leaving the studio restores pad 2 | `[Palette Maker] pair puppet pad` |
| `syncStudioPartnerCostume` | Copies player 1's `CostumeID` onto player 2. That index is the other form's side of the pair | `[Palette Maker] pair puppet costume=` |
| `syncStudioPartnerBody` | Training assigns team 2 and calls `setHumanControl(false)` on player 2. This sets both fighters to team 1, turns team attack off, calls `setHumanControl(true, 1)` so player 1's pad moves player 2, and moves player 2 onto player 1's position and respawn | `[Palette Maker] pair puppet team=1 attack=0 human=1` |
| `pairedPaletteForSlot` | The transform reads a nested partner palette when the costume has one. Otherwise it reads the partner file at the same index. A `pairBase` slot is not a palette | — |
| `Controller.getControlStatus` | When `ModHost.mirrorInput` is set, any other controller's status read returns player 1's pad with `START` cleared. `IsDown` for start on that controller is false. Pad 2 also has pad 1's bindings for the match | — |
| `maintainGigaStudioForm` | Skips entrance. After revival and a short delay, `forceAttack("special")` | `[Palette Maker] giga studio final` |
| Studio PA | Giga does not show a portrait | — |
| `matchRestAreaZoom` | Giga uses a farther `MinZoomHeight` so the stage fits | `[Palette Maker] studio zoom min=` |
| `buildGigaBrowseCostumeList` | Giga locker lists stock Giga palettes and vault customs. No metal/team from Bowser | — |
| `PaletteMakerMenu` show | Loads `partnerPaletteData` for save and clipboard. Does not draw a partner portrait or alt swatches | — |
| `saveCostume` | Writes one character file. `atIndex` replaces that slot. A slot past the end is padded so the partner keeps the opened form's game costume index | `[Palette Maker] save char= index=` |
| `saveCostume_CLICK` | Zelda, Sheik, Bowser, and Giga Bowser always open the pair list. `FormPair.opensPairList` is true for those four. `isLinkedFormPair` follows that, and also a link whose `cssSelectable` is set. `openPairFileIndexes` skips vault indexes, below the online count | `[Palette Maker] pair open=` |
| `openPartnerPairing` | Opens the partner list filtered to `pairOnlyFileIndexes`. A save also offers Base | `[Palette Maker] pair menu partner=` |
| `buildPairBaseChoice` | The Base tile. Choosing it runs `saveWithTemporaryBase` | `[Palette Maker] pair base choice` |
| `writePairBaseSlot` | Writes a `pairBase` slot and does not favorite it. The palette menu and shield cycle skip it. `getCostume` fills an empty slot from `getCostume(name, null, -1)`, and a transform applies that base recolor | `[Palette Maker] pair base recolor` |
| `setOnlineImportCostumes` | The Color Vault grid uses `CostumeNetIndex.visibleVaultCostumes`, so `pairBase` tiles are not shown. Each other tile keeps its catalog index | `[Palette Maker] vault hide base` |
| `CostumeNetIndex.shouldKeepVaultEntry` | Color Vault load keeps every `pairBase` slot. A repeated costume name still drops. Zelda, Sheik, Bowser, and Giga Bowser online lists stay the same length | `[PaletteMakerUtils] Loaded Color Vault` |
| `_onPairingClickContainer` | Loads the palette-menu pick into `partnerPaletteData`, then calls `performSaveCostume` when save started the pair | `[Palette Maker] pair partner=` |
| `performSaveCostume` | Writes `currentPaletteData` with `pairedPalette`, and `partnerPaletteData` at `CostumeNetIndex.partnerFileIndex` | `[Palette Maker] pair file=` |
| `applySavedCostumeSlot` | After save, sets `CostumeID` to the game index for that file so a transform uses the saved slot. `-2` stays the base until this runs | `[Palette Maker] pair slot id=` |
| `exportCostume_CLICK` | A costume that already has a saved partner copies that partner and skips the pair list. `pairChooseBase` copies this costume only. An unsaved name still opens the pair list | `[Palette Maker] pair export saved` |
| `ensureLinkedFormParity` | Does nothing. It must not insert costumes | — |
| `removeFavoriteIndex` | Drops that catalog index from the opened form and from the linked partner. It does not add the partner's costumes | `[Palette Maker] pair unfav` |
| `mirrorLinkedFavorites` | A Zelda, Sheik, Bowser, or Giga Bowser palette-menu pick is also a pick on the other form at that file index. A missing or base partner slot is filled from `pairedPalette` | `[Palette Maker] pair fav` |
| `CostumePackIO.deleteCostumeObjects` | Deletes or unfavorites the same catalog indexes on the linked partner. Online indexes leave the favorites list. Local indexes are removed from both files and later indexes shift the same way | `[Palette Maker] pair remove` |
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
