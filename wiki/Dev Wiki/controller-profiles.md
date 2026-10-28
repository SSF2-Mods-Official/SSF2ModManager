---
title: Controller profiles
summary: The nametag on a character-select box, and the profile list on Controls.
order: 30
group: Match
---

# Controller profiles

## Info

| Controller profiles |
| --- |
| Mod | `controller_profiles` |
| Nametag | `ProfileTags.selectName` |
| Log | `[controller_profiles] selectName failed:` |
| Related | [Manage character select](manage-character-select.md) |

`selectName` writes the profile name onto that player's box. A menu with fewer than four player rows skips the missing rows.

## Contents

1. [How it works](#how-it-works)
2. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `selectName` | Writes the name when `PlayerSettings` has that row. A short list is left alone. | `[controller_profiles] selectName failed:` |
| `ControllerProfilesMod.onChip` | If `PlayerChips` has an empty slot, it cancels `BEFORE_CHIP_UPDATE` so `Chip.checkGrab` does not read `Held` on null. | `[controller_profiles] chip slot empty` |


## References

1. [Manage character select](manage-character-select.md) — the grid these names sit on.
2. Wiki — the Info, Contents, and References shape.
