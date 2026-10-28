---
title: Set up Mod Manager
summary: Download the manager, run Setup, build SSF2 Shell from your Beta 1.4.0.1.
order: 10
---

## 1 — Download

Get the latest build:

[https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/latest](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/latest)

Unzip it somewhere you can find (for example Documents) and open that folder. The zip is the manager only — it does **not** include a finished SSF2 Shell. Shell is built on this PC in Setup.

## 2 — Run the launcher

Double-click `SSF2ModManager.exe`.

You should see the launcher with **Play**, **Configure**, **Debug**, and the mod buttons. **Open Docs** opens this wiki.

On first launch (or when Shell / Next2Flash is missing), **Set up** opens.

## 3 — Setup (in order)

1. **Next2Flash** — click **Download**. That zip includes the portable Python and JRE used for compile and Convert to FLA.
2. **SSF2 Beta 1.4.0.1** — **Choose folder** on your installed copy (the folder with `SSF2.swf`). Only 1.4.0.1 works.
3. **SSF2 Shell** — click **Build**. The manager downloads Shell Base `.ssfpatch` files and builds a playable Shell beside the exe (or into the folder you set). Your mods are kept on a rebuild.
4. **Debugging (optional)** — VS Code + ActionScript tools for Make Mods.

## 4 — Install folder

On the launcher, **Install folder** should point at the SSF2 Shell you just built (or an existing Shell you use). Click **Change** if it is wrong.

## 5 — Play once

Click **Play**. SSF2 Shell should start like a normal game.

When you want to make mods, click **Make Mods**.

Next: [Launcher buttons](tasks.md)
