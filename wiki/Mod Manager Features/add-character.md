---
title: Add a character
summary: Clone a fighter you have, or import a character file, then put them on the select screen.
order: 41
---

**Make Mods**, then **Character**. A new fighter is either a clone of someone already in this build, or a character file you already have.

## Clone a character

**Clone a character** copies a fighter so you can change them without editing the original.

- **Character to clone.** The fighter the copy starts from.
- **Display name.** The name people see, and the folder the pack is stored in. Spaces are fine. `Clone Dee` stays `Clone Dee` on disk.
- **Character name (linkages).** The id used inside the files. The game turns the display name into this id by keeping letters, numbers, and underscores. `Clone Dee` becomes `Clone_Dee`. If the box goes red, that id is already taken or it is not a usable name. A name that starts with a number gets a `c` in front of the id.
- **Keep using clone's assets.** On, the new fighter still uses the original's pictures and sounds. Off, the copy gets its own files so you can replace them.
- **Do not convert to FLA.** On, the manager skips making an editable FLA. Leave it off if you want to open the character in an editor afterward.
- **Clone character.** This builds the pack and registers the fighter. It can take a minute.

## Import a character

**Import a character** is for a file you already exported or downloaded as a loose character, not a finished GameBanana package.

**Browse** takes `.swf` and `.ssf` files. The box can list more than one. **Import** brings them in. **Do not convert to FLA** is the same switch as on clone.

A character that is already a `.ssfe` or a zip from GameBanana does not go through this form. Use **Download Mods**, or **Make Mods** → **Package mods** → **Install a mod archive**.

## Put them on the screen

The pack shows up under Characters in **Manage Mods**. The checkbox has to be on or the game will not load them.

**Character select** on the Character screen opens the select-screen editor. Drag the new fighter onto a cell. An imported or cloned fighter is not placed for you. If the grid is full, add a row in that editor or move someone else. See [Character select editor](../SSF2 Shell Features/character-select-editor.md).

A recolor is not a new character. That is [Palette Maker](../SSF2 Shell Features/palette-maker.md).
