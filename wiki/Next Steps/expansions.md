---
title: Add a character
summary: Clone or import a fighter in Make Mods, then enable Real Expansions.
order: 30
---

A **real expansion** is a custom fighter with its own Character Select icon. Use **Make Mods** on the launcher — do not drop folders by hand unless you already know the layout.

Finish [Set up Mod Manager](../Start Here/setup.md) first.

## 1 — Clone or import

1. On the launcher, click **Make Mods**
2. Open the **Character** section
3. **Clone** a stock fighter to edit, or **Import** a ready `.swf` / pack

Make Mods writes the folder under `SSF2 Shell\expansions\character\<id>\` with the files the game expects (`<id>.swf`, `assets.swf`, and optional announcer / costumes).

Sound packs (alts) are under the same Make Mods Character tools — see the Dev Wiki page Character alts if you are packing voices only.

## 2 — Turn it on in game

1. On the launcher, click **Manage Mods**
2. Enable **Real Expansions**
3. Click **Play** (or **Debug**), open Character Select

The new chip is added to the last row (before Random) when there is no saved CSS layout slot yet.

You usually do **not** need to edit `mappings.json`.

Engine script edits are separate — see [Change the game’s code](engine2.md).
