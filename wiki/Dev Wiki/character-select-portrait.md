---
title: Character select portrait
summary: The player-box portrait is mc_pa. A palette costume recolors that clip in place.
order: 11
---

# Character select portrait

## Info

| Character select portrait |
| --- |
| Portrait clip | `mc_pa` |
| Live fighter | `mc` |
| Paint | `paintPortrait` on frame 10 |
| Menu lookup | `PortraitBox.pick` |
| Log | `costume paint menu=select` |
| Related | [Costume recolor](costume-recolor.md) |

The portrait on a player box is the `*_charselect` clip. A palette confirm adds a new clip, and the paint parks that clip as `mc_pa` so a later costume cycle recolors it.

## Contents

1. [How a pick paints](#how-a-pick-paints)
2. [Functions](#functions)
3. [Files](#files)
4. [Rebuild](#rebuild)
5. [Check](#check)
6. [References](#references)

## How a pick paints

`css_sprite_preview` names the live fighter `mc` and parks the portrait as `mc_pa`. Unselect calls `clearPort` with the menu on the select hook and removes `mc_pa`. The log is `deselect port=`.

`paintPortrait` lets a clip still before frame 10 play its slide-in (`portrait slide`), then colors the settled pose on frame 10 (`portrait settle`) through the `cssPortraitPaint` door.

A palette confirm calls `changePic` on the select-hook menu. That adds a fresh charselect named `mc` on top of the fighter. `menuForClip` asks `PortraitBox.pick`. When `CurrentCharacterSelectMenu`'s boxes do not contain the clip, the pick uses the select-hook menu. The log is `costume paint menu=select`. `PortraitBox.park` names that clip `mc_pa`.

`claimPaletteCostumePick` runs only from `onEnterFrame` while `m_cssActive` is set, so a costume menu can open with no `costume pick hooked` line. Opening the grid logs `costume menu cssActive=` with `listen`, `feature`, `onscreen`, `click`, and `css`. A tile click logs `[palette_maker] costume confirm idx=`. A character-select confirm (`css=1`) does not call `onClickContainer`.

`applyCssCostumePick` writes `costume apply game=` with `live=1` when it uses the menu from `CSS_SELECT_CHARACTER`. That hook passes the menu that just called `changePic` (`costume menu noted game=1`). `costume replay enter` is the line just before that call. Back clears `CurrentCharacterSelectMenu`'s `m_game` (`costume gate game=0 pic=1`). `costume gate fail` is a throw while reading the menu.

The October 4 `engine2.swf` click still calls `changePic`. If `changePic` adds a clip while `mc_pa` is already there, that extra clip is removed (`costume drop extra`).

## Functions

| Function | What it does | Log |
| --- | --- | --- |
| `clearPort` | Removes `mc_pa` on unselect. | `deselect port=` |
| `paintPortrait` | Plays the slide, then colors frame 10. | `portrait slide`, `portrait settle` |
| `PortraitBox.pick` | Chooses the menu that owns the clip. | `costume paint menu=select` |
| `PortraitBox.park` | Names the new clip `mc_pa`. | |
| `applyCssCostumePick` | Uses the menu from `CSS_SELECT_CHARACTER`. | `costume apply game=`, `live=1` |
| `changePic` | The game adds the charselect clip. The pick calls it on the select-hook menu. | `costume replay enter`, `costume menu noted game=1` |

## Files

| What | Path |
| --- | --- |
| Fixture | `expansions/mods/css_sprite_preview/fixture` (`PortraitPaintFixture`) |
| Preview mod | `css_sprite_preview` |
| Palette confirm | `palette_maker` |

The engine `costumeSelectorDisplay` change is source-only until `engine2.swf` is rebuilt.

## Rebuild

Rebuild `css_sprite_preview` and `palette_maker`. The command is on Where things are. A running game keeps the old swf until a full quit.

## Check

Full quit, relaunch. Select a character, open the palette menu, pick a costume, then cycle costumes. The portrait and the fighter both change. After Back, the pick logs `costume paint menu=select`.

The fixture, from `css_sprite_preview/fixture`: `mxmlc -load-config+=air-config.xml`, then `adl PortraitPaintFixture-app.xml`. `%APPDATA%\PortraitPaintFixture\Local Store\portrait-paint-fixture.txt` says `OK`.


## References

1. Where things are — the compile command, and that a running game keeps the old swf.
2. [Costume recolor](costume-recolor.md) — `paletteSwap` and the base costume.
3. Wiki — the Info, Contents, and References shape.
