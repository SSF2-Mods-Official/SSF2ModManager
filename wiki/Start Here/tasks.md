---
title: Launcher buttons
summary: Play, Configure, Debug, and the Mod Manager buttons.
order: 20
---

Finish [Set up Mod Manager](setup.md) first.

Everything important starts from the **launcher** (`SSF2ModManager.exe`). You do not need to press F5 or run editor tasks yourself.

## Play

Starts SSF2 normally (same as the game exe). Use this to just play.

## Configure

Opens launch settings in the launcher (starting menu, intro, vault, match). **Launch** writes those into the game and starts SSF2. It does not open the editor.

## Debug

Opens your editor and **SSF2 Stream**, then starts a debug session.

In Stream you can turn on **Super Debug** if you want the deeper debug path. That setting lives in Stream — not in the launcher.

## Make Mods

Sets up the editor tools (VS Code if needed, ActionScript extension, SSF2 Stream, Shell settings), then opens the full Mod Manager on the Make side (clone / add characters and more).

## Download Mods

Opens the full Mod Manager on Download (browse mods).

## Manage Mods

Opens the full Mod Manager on Manage (installed mods).

## Open Docs

Opens this wiki in its own window. The list on the left is the pages. **Edit** and **New tutorial** are in that window.

## Check for updates

Looks at GitHub for a newer Mod Manager build and downloads what you need. Turn on **Update automatically** if you want this on startup.

## SSF2 Stream tab

When the launcher opens your editor, the **SSF2 Stream** tab should open by itself.

If you closed it:

1. Press `Ctrl+Shift+P` (Command Palette)
2. Run **SSF2 Stream: Open Panel**

(Or press **F4** if that shortcut is still bound.)

To stop it from opening every time: in the Stream tab open settings and turn off **Auto-open Stream on launch**.
