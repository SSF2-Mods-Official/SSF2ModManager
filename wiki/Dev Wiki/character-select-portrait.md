---
title: Character select portrait
summary: The player-box portrait is mc_pa. A palette costume recolors that clip in place.
order: 20
group: Characters
---

# Character select portrait

## Info

| Character select portrait |
| --- |
| Portrait clip | `mc_pa` |
| Live fighter | `mc` |
| Clear | `CharacterSelectMenu.removePic` |
| Confirm | `CostumeSelectorDisplay.applyCssCostumePick` |
| Menu | `cssMenuForPick` |
| Log | `[palette_maker] costume apply game=` |
| Related | [Costume recolor](costume-recolor.md) |

The player-box portrait clip is `mc_pa`. `removePic` takes that clip off when the port is cleared. A character-select costume confirm calls `applyCssCostumePick`.

## Contents

1. [How a pick paints](#how-a-pick-paints)
2. [Functions](#functions)
3. [Files](#files)
4. [Rebuild](#rebuild)
5. [Check](#check)
6. [References](#references)

## How a pick paints

`CharacterSelectMenu.removePic` removes the child named `mc_pa`, and the child named `mc`, when a port is cleared.

A tile click logs `[palette_maker] costume confirm idx=`. When `m_cssPick` is set, that click calls `applyCssCostumePick` and does not call `onClickContainer`. `cssMenuForPick` uses `CurrentCharacterSelectMenu` when that menu has `changePic` and a match, and otherwise uses the menu stored by `noteCssMenu`.

`applyCssCostumePick` writes `[palette_maker] costume apply game=` with `live=1` when the chosen menu is the stored menu and not the current one. It sets `settings.costume`, logs `[palette_maker] costume replay enter`, and calls `changePic`. A throw while reading the menu logs `[palette_maker] costume gate fail`.

## Functions

| Function | What it does | Log |
| --- | --- | --- |
| `CharacterSelectMenu.removePic` | Removes `mc_pa` and `mc` when a port is cleared. | None |
| `cssMenuForPick` | Uses the current menu when it has a match, otherwise the menu from `noteCssMenu`. | None |
| `applyCssCostumePick` | Sets the slot costume and calls `changePic` on that menu. | `[palette_maker] costume apply game=` |

## Files

| What | Path |
| --- | --- |
| Clear | `SSF2 Shell Base/patches/engine2/patches/com/mcleodgaming/ssf2/menus/CharacterSelectMenu.as.patch` `removePic` |
| Confirm | `SSF2 Shell Base/shell/src/mods/palette_maker/src/com/sbxmod/paletteMaker/CostumeSelectorDisplay.as` |
| Menu | `CostumeSelectorNav.cssMenuForPick` |

## Rebuild

Rebuild `palette_maker`. A running game keeps the old swf until a full quit.

## Check

Full quit, relaunch. Select a character, open the palette menu, and pick a costume. The log has `[palette_maker] costume confirm idx=` and `[palette_maker] costume apply game=`.


## References

1. [Where things are](where-things-are.md) — the compile command, and that a running game keeps the old swf.
2. [Costume recolor](costume-recolor.md) — `paletteSwap` and the base costume.
3. Wiki — the Info, Contents, and References shape.
