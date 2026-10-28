---
title: Two folders
summary: The game you play vs the folder the launcher opens for modding.
order: 30
---

## The game — `SSF2 Shell`

This is what **Play** starts.

| You will see | What it is |
| --- | --- |
| `Super Smash Flash 2 Beta.exe` | The game |
| `data\` | Stock characters, stages, music, menus |
| `data\engine\` | Game engine (`engine1.swf`, `engine2.swf`) |
| `data\ssf2mm\` | Built-in mods (`mod.json` and `mod.swf` each), `state\`, game log |
| `src\mods\` | Source for the built-in mods. Compile from **Make Mods** or SSF2 Stream. |
| `expansions\` | Optional mods, intros, content packs, custom characters, stages |

Drop optional mods into `expansions\mods\` and custom characters into `expansions\character\`. The game still launches if `expansions` is empty.

**Convert to FLA** on `engine2.swf` writes `expansions\decompiled\engine2`. That folder is for editing the game code. It is not a mod the player turns on.

## The coding folder — `runtime\shell-dev`

**Debug** opens this folder in your editor. The launcher does that for you — you do not pick it by hand.

## Quick rule

- **Play** → game
- **Debug** → editor + SSF2 Stream
- **Make / Download / Manage Mods** → full Mod Manager
