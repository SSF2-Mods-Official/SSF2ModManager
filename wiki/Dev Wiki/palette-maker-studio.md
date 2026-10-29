---
title: Palette maker studio
summary: The hidden studio bar changes music, time of day, and stage. Recolor effects is not booted.
order: 10
group: Palette
---

# Palette maker studio

## Info

| Palette maker studio |
| --- |
| Hidden bar | Show UI, Change time of day (rest area only), Change music, Change stage, Go to rest area |
| Paste box | `ImportCodePastePopup` panel at `-210, -140` |
| Recolor effects | Toolbar `RE` removed; `CostumeFxPaletteRuntime.boot` is not called |
| Final smash tool | Removed; Giga studio still uses `FinalSmashMeter` |
| Related | [Costume recolor](costume-recolor.md) |

The studio bar sits on the bottom of the screen while the palette UI is hidden. Change stage opens the stage select screen and reloads the studio on the chosen stage.

## Contents

1. [How it works](#how-it-works)
2. [Partner pairing](#partner-pairing)
3. [Clipboard export](#clipboard-export)
4. [Color groups](#color-groups)
5. [Color sheet](#color-sheet)
6. [Files](#files)
7. [Rebuild](#rebuild)
8. [Check](#check)
9. [Fixed](#fixed)
10. [References](#references)

## How it works

`m_subMenu` is placed at half the stage width and half the stage height. `openImportCodePastePopup` adds `ImportCodePastePopup` to `m_container`, above the unique-sprite strip. Its panel is placed at `-210, -140`. While `costumeSelectorDisplay` is on the stage, `stage_CLICK` and `stage_MOUSE_MOVE` return, so the portraits and unique sprites underneath do not take the click.

Change music opens the jukebox through `openMatchJukebox`. `applyCharacterStudio` loads that character's saved stage and song from `studioByCharacter` in `ssf2_palette_maker_settings`. With no saved stage, `homeStageFor` picks that character's stage. When a series has several stages, each character gets a different one. Sandbag uses `restarea`. Bowser uses `finaldestination` and plays `bgm_bowserscastle`, including a saved `mushroomkingdom3` stage that has no song. Mario uses `kingdom1`. Yoshi uses `yoshisstory`. Peach uses `galaxytours`. Waluigi uses `emeraldcave`. Samus uses `phase8`. Zero Suit Samus uses `crateria`. Naruto uses `konohavillage`. Ganondorf uses `palaceoftwilight`. Zelda uses `hylianskies`. Sora uses `theworldthatneverwas`. A vanilla id missing from that map gets its own leftover stage. A stage change clears that character's song, so the new stage plays its own music. `updateCharacterFrame` sends the character from x `434.25` to x `-24.4` only while the stage is `restarea`. `restAreaMod` places the trophy portrait on every stage. On any stage other than `restarea`, the portrait stays hidden through the walk-in. `anchorPortraitOnce` places it after `portraitHoldReady` sees the character idle, with vertical speed within 2, and that height held for 8 frames. The portrait sits 12 pixels to the right of that standing spot, 32 pixels to the right for Dedede, and 6 pixels to the left for Ichigo. `studioSpawnNudge` then moves Bowser 56 pixels left, Dedede 32 pixels left, Ichigo 91 pixels left, and Ganondorf, Chibi-Robo, Captain Falcon, and Tails 16 pixels left, and holds Samus 8 pixels forward and 40 pixels up. The aerial start point is not used. The portrait is parented above the foreground and added as a camera target. `matchRestAreaZoom` sets `MinZoomHeight` to `300`. `nudgeStudioCamera` then moves the camera 24 pixels to the right, and pulls that shift back so the character stays 40 pixels inside the view. On `restarea` the bar is Show UI, Change time of day, Change music, Change stage. On any other stage those two rest-area buttons are omitted and `layoutHiddenUiButtons` packs the rest from x `12`. Go to rest area reloads the studio on `restarea`. The stage-select Back click is caught on the stage in the capture phase and returns to the studio. `onStudioStageSelect` waits one frame and leaves stage select up when `ctx.consume` is set, so Upload Stage BG keeps that click.

`TOOL_IDS` does not include `RE` or `FS`. `setRecolorEffectsMode` forces the mode off. `CostumeFxPaletteRuntime.boot` is not called, so effect recolor does not run in a match. The Final Smash toolbar path is not in this tree (`FinalSmashRequest` is absent). Ctrl+F does not freeze the fighter. The freeze button and the grab+taunt chord still do. The character select No Recolor slot stays on. `cssShowNoRecolorWhenNoBase` is always true. `ShortcutsPopup` masks the binding list and shows a thumb when the rows are taller than the panel.

| Function | What it does | Log |
| --- | --- | --- |
| `isMagnifierBlocked` | The submenu box blocks the magnifier. A hover whose box is on the fighter, `PixelArt`, `PixelArtPartner`, or a unique sprite does not. The pixel sample skips a sprite whose box misses the cursor | None |
| `costumeMenuOpen` | The minimap and click-to-select wait only while `isOnscreen()` is true. A parent check is not used | None |
| Settings | `ToolSettingsPopup` is 232×248 at y `18`, clamped to the stage. Debug logging is not a setting. Select highlight is not a toggle. A saved on state is ignored | None |
| `manageMenuMappings` | Studio does not call `pollPad`. Stick and keys drive the fighter; the studio UI is mouse/click. Costume locker still polls | None |
| `cssShowNoRecolorWhenNoBase` | The character select No Recolor slot stays on | None |
| `ShortcutsPopup` | The binding list is masked. A thumb shows when the rows are taller than the panel | None |
| `onStudioStageSelect` | Cancels the normal start and keeps the chosen stage id | None |
| `finishStudioStagePick` | Ends the studio match, saves the stage id, and clears the song when the stage changed | `[Palette Maker] studio stage` |
| `openStudioJukebox` | Opens the match jukebox | None |
| `rememberStudioSong` | Saves the jukebox track into the studio settings | `[Palette Maker] studio song` |
| `restAreaMod` | Places the trophy portrait. Rest-area layout runs only for `restarea` | None |
| `anchorPortraitOnce` | The portrait stays hidden until `portraitHoldReady`. That is idle, vertical speed within 2, and the same height for 8 frames. It then sits 12 pixels right of that spot, 32 for Dedede, and 6 left for Ichigo. Bowser moves 56 left, Dedede 32 left, Ichigo 91 left, and Ganondorf, Chibi-Robo, Captain Falcon, and Tails 16 left. Samus is held 8 forward and 40 up | `[Palette Maker] studio portrait` |
| `homeStageFor` | One stage per vanilla character. Sandbag is `restarea`. Mario is `kingdom1`. Yoshi is `yoshisstory`. Peach is `galaxytours`. Waluigi is `emeraldcave`. Samus is `phase8`. Zero Suit Samus is `crateria`. Bowser is `finaldestination` with `bgm_bowserscastle`. Naruto is `konohavillage`. Ganondorf is `palaceoftwilight`. Zelda is `hylianskies`. Sora is `theworldthatneverwas` | None |
| `applyCharacterStudio` | Loads the character's saved stage and song, or the series home stage | `[Palette Maker] studio home` |
| `saveCharacterStudio` | Stores the stage and song the player chose for that character | None |
| `matchRestAreaZoom` | Sets the closest zoom to `300`, or `560` for Giga Bowser | `[Palette Maker] studio zoom min=` |
| `nudgeStudioCamera` | Moves the camera 24 pixels right on any stage other than `restarea`, and keeps the character 40 pixels inside the view | None |
| `layoutHiddenUiButtons` | Packs the bar from x `12`, skipping hidden buttons | None |
| `updateCharacterFrame` | Wraps x from `434.25` to `-24.4` only on `restarea` | None |
| `parkTelepads` | Moves a telepad clip when the stage has one | `[Palette Maker] studio pads moved=` |
| `keepStudioMatch` | On `gameEnded`, clears `GameEnded` and `GameEndedExit` so a KO does not open the training menu | `[Palette Maker] studio match kept` |

## Partner pairing

Save on a linked form opens the costume palette menu on the partner. After a pick, `performSaveCostume` writes the opened form with `pairedPalette` and writes the partner file at the slot that shares that form's game costume index. The write uses the name the click already accepted and runs before the menu closes. Closing the menu without a pick does not save.

| Function | What it does | Log |
| --- | --- | --- |
| `saveCostume_CLICK` | Linked: next frame opens the pair list. A failed list does not write | `[Palette Maker] pair call` |
| `openPairList` | Save calls this function. It is set in the `PaletteMakerMenu` constructor so the parent click does not late-bind a protected method | `[Palette Maker] pair call v5` |
| `openPartnerPairing` | Opens the costume palette menu on the partner. Sets `pendingSaveAfterPair` and `pendingSaveName` on this menu after `setCharacter` | `[Palette Maker] pair flag=` |
| `_onPairingClickContainer` | Loads the palette-menu pick into `partnerPaletteData`, writes when save started the pair, then closes the menu | `[Palette Maker] pair save do=` |
| `CostumeNetIndex.partnerFileIndex` | Partner file slot for the opened file index. Builtin alts are `catalogSlotOrigin` on each form | None |
| `performSaveCostume` | Writes the opened form with `pairedPalette`. Writes the partner at `partnerFileIndex`. Reads `pendingSaveName` / `pendingSaveAfterPair` declared on `PaletteMakerMenuStudio` | `[Palette Maker] pair file=` / `pair write name=` |
| `pairedPaletteForSlot` | The opened form's stored pick for that game costume index | `[Palette Maker] pair apply` |

## Clipboard export

Copy to Clipboard stamps each costume with `character` (stats id) and `ssf2Version` (from `Version.getVersion()`, fallback `1.4.0.1`). Linked forms (Bowser/Giga Bowser, Zelda/Sheik) always copy a `{ "characters": { id: [entry], ... } }` pack so Color Vault can put each half in the matching vault file.

| Function | What it does | Log |
| --- | --- | --- |
| `CostumePackIO.copyCostumeToClipboard` | Builds the JSON pack; dual partners use the `characters` map | None |
| `CostumePackIO.currentSsf2CostumeVersion` | Game build string for `ssf2Version` | None |

## Color groups

The studio shows one form. Groups belong to that form. While a group is active, selecting another color of the same form adds it to the group. The studio does not build groups. `restorePersistedColorGroups` skips a saved group whose `autoGenerated` flag is set. Loaded groups keep membership, names, and enabled state. Swatch color and `layerReplacements` are session-only (`ColorGroupPersist`), so enable or reorder cannot re-bake another costume's paint. Switching recolors in the locker does not clear or reload groups. It does clear `activeGroup` and group chip selection so no group stays editing. Swatch `deselectAll` runs under `suppressSelectionSideEffects` so membership sync cannot empty a group. An X after the layer row confirms, then deletes every group.

| Function | What it does | Log |
| --- | --- | --- |
| `ColorGroupPersist.serializeMembership` | Saves membership and flags; omits swatch and layer paint | None |
| `ColorGroupPersist.applyMembershipOnly` | Restores membership and clears session paint | None |
| `ColorGroupPersist.wouldReapplySwatch` | True when enable would paint from a stored swatch | None |
| `ColorGroupPersist.membersForRestore` | Prefers resolved members; keeps saved members when none resolve | None |
| `ColorGroupPersist.clearSessionPaint` | Drops swatch and layer paint on a group | None |
| `applyColorGroupsForLoadedCostume` | Restores from save on open/import. Locker recolor picks do not call this | None |
| `_onClickContainer` | Locker pick: no-recolor paint, then costume pairs with `recordLayer` false. Clears group session paint. Deselects active groups. Live color groups stay | `[palette_maker] costume pairs=` |
| `resetStudioVisualToNoRecolor` | `setCostumeAPI(-2)`, `CostumeID=-1`, identity replacements, then `applyActivePalettes` | None |
| `clearGroupSessionPaintAfterCostumeLoad` | Clears swatch/layer paint on every live group after locker pairs | None |
| `deselectActiveGroupsOnCostumeSwitch` | Clears `activeGroup` and group chip selection; `deselectAll` under suppress | None |
| `StudioCostumeSwitch.suppressesSelectionOnDeselect` | Locker pick must suppress membership sync on deselect | None |
| `StudioCostumeSwitch.deselectsActiveGroupsOnPick` | Locker pick clears active group editing | None |
| `layoutGroupRow` | Places group chips and the clear-all X | None |
| `executeClearAllGroups` | After confirm, resets members and removes every group | None |
| `syncActiveGroupMembership` | Adds a newly selected color to the active group and removes a deselected color | None |
| `getOwnerGroupForBase` | The owner is the first enabled group of that same form | None |
| `restorePersistedColorGroups` | Loads membership via `ColorGroupPersist`; skips `autoGenerated` | None |

## Color sheet

Export is one form: one portrait, that form's unique sprites, and one palette row. An older side-by-side PNG still imports. The left bottom-row run writes the opened form. A second run after a transparent gap writes the partner file when one is loaded.

| Function | What it does | Log |
| --- | --- | --- |
| `paletteStripsFromBottomRow` | Splits the bottom row on a transparent gap | `[Palette Maker] color sheet import opened=` |

## Files

| What | Path |
| --- | --- |
| Skin draft | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/SkinDraft.as` |
| Paste box | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/ImportCodePastePopup.as` |
| Stage button and reload | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/PaletteMakerMenu.as` |
| Locker blur | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/CostumeSelectorChrome.as` |
| Home stages | `SSF2 Shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/StudioHomeStages.as` |
| Jukebox | `SSF2 Shell/src/mods/music_player/src/MusicPlayerMod.as` |
| Boot | `SSF2 Shell/src/mods/palette_maker/src/PaletteMakerMod.as` |
| Rebuild bat | `SSF2 Shell Base/shell/src/mods/palette_maker/rebuild.bat` |
| Persist helper | `SSF2 Shell Base/shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/ColorGroupPersist.as` |
| Persist fixture | `SSF2 Shell Base/shell/src/mods/palette_maker/fixture/run-color-group-persist-fixture.ps1` |

## Rebuild

Double-click `rebuild.bat` in the palette_maker source folder, or run `tools/build-mod.ps1 palette_maker` from Shell Base. Quit the game fully before testing. See [Where things are](where-things-are.md).

## Check

`powershell -NoProfile -ExecutionPolicy Bypass -File shell\src\mods\palette_maker\fixture\run-color-group-persist-fixture.ps1` from Shell Base prints `fixture OK`.

`powershell -NoProfile -ExecutionPolicy Bypass -File shell\src\mods\palette_maker\fixture\run-studio-costume-switch-fixture.ps1` from Shell Base prints `fixture OK`.

## Fixed

### Locker pick deleted hair group and left no-recolor

With a live color group selected, picking a costume called `deselectAll` without `suppressSelectionSideEffects`. `syncActiveGroupMembership` stripped every member, `revertColorsToSessionBaseline` painted orphans from `baseCostumeData`, and the empty group was removed. `_onClickContainer` now suppresses selection side effects on that deselect, writes costume pairs with `recordLayer` false, clears group session paint after the pairs, and deselects active groups without deleting them.

## References

1. [Costume recolor](costume-recolor.md) — how a palette is applied to a costume.
2. [Palette cache](palette-cache.md) — the local sprite cache the studio uses.
3. [Unique sprite strip](unique-sprite-strip.md) — the scrollbar beside the portrait.
