---
title: Debug
summary: Open the editor and SSF2 Stream so you can run the game from a breakpoint.
order: 90
---

**Debug** is for editing SSF2 Shell, not for a normal match. Play still works if you never touch this.

## What the button does

**Debug** on the launcher does three things:

1. If the editor tools are missing, it installs them. That is the same optional step at the end of Setup. You can skip it during Setup and still get it the first time you press Debug.
2. It opens the editor you picked in **Settings** under **Debug editor**.
3. It opens the **SSF2 Stream** tab in that editor and starts a debug session.

The status line says "Opening editor + SSF2 Stream…" while that is happening. If the tools cannot be installed, a message says why and the game is not launched.

## SSF2 Stream

Stream is the tab that talks to the game.

- **Super Debug** in that tab uses the deeper debug path. Leave it off unless you need it. The normal Debug launch is enough to run the game from the editor.
- If you closed the tab, press `Ctrl+Shift+P` and run **SSF2 Stream: Open Panel**. **F4** opens it when that shortcut is still bound.
- **Auto-open Stream on launch** is in the Stream settings. Turn it off if you do not want the tab every time the editor starts.
- **F5** in the editor starts the debug launch from the project. That is the same session Debug is trying to open.

## What you need on disk

Debug looks for the shell-dev project that sits beside the game, with an `asconfig.json` in it. Setup's editor step is what puts the editor, the ActionScript extension, and Stream in place. If Debug says it cannot find the project, the Shell folder in **Settings** is not the build that has that project next to it.

The game you ship to other people is not a debug build. Debug is only this editor session on your machine.
