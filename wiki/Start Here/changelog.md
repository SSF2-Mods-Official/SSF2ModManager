---
title: Changelog
summary: The same posts as News in the Mod Manager.
order: 15
---

## SSF2 Beta Mod Manager v0.9.3

2026-10-28

Setup builds from the 64-bit installer, the 64-bit portable, the 32-bit installer, and the 32-bit portable. It names which copy it found. Another version of the game is refused.

[v0.9.3 release](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases/tag/v0.9.3)

If you already have the Mod Manager, open **News** and click **Check for updates**, or restart the Mod Manager.

## SSF2 Shell v0.9.2

2026-10-28

The shell builds from the 64-bit installer, the 64-bit portable, the 32-bit installer, and the 32-bit portable. A 32-bit copy starts on that game's Adobe AIR, with Discord and rumble. A 64-bit copy keeps the 64-bit Discord and rumble files.

[v0.9.2 release](https://github.com/SSF2-Mods-Official/SSF2-Shell-Base/releases/tag/v0.9.2)

## SSF2 Beta Mod Manager v0.9.2

2026-10-28

The mini player can show the bars on a PC that does not have ffmpeg. The song still plays the same way. Setup now shows a percent and a plain sentence for each step, so you can see what it is doing.

## SSF2 Beta Mod Manager v0.9

2026-10-28

This is a fresh 0.9. Delete the old Mod Manager and download this one.

Setup shows one step at a time, and the main window stays hidden until setup is complete. Next2Flash is saved in a folder next to the Mod Manager. News has a Check for updates button. When the manager itself updates, it says it will restart, then it quits and starts again.

SSF2 Shell 0.9 keeps the waiting-room song separate from the camera background, sends that background to the other player in an online match, and ships with debug off. F5 still turns lazy debug on.

## SSF2 Beta Mod Manager v1.1.2

2026-10-27

Setup on another PC could build SSF2 Shell and then stop at Play. The manager listed vanilla files that missed a hash, and the game then said `engine2.swf` was modified and would not start.

v1.1.2 drops the vanilla file hash check. The Shell also loads `engine2.swf` without that pin. Expansion mods that fail the security check still do not start.

Check Updates installs this manager, then rebuilds SSF2 Shell. Accept the Shell rebuild so Play uses the new booter.

## SSF2 Beta Mod Manager v1.1.1

2026-10-27

### The revamp

SSF2 Beta Mod Manager no longer mods the copy of the game you installed. It builds **SSF2 Shell**, a moddable game, from your own **SSF2 Beta 1.4.0.1**. Play starts the Shell. The vanilla install stays where it is.

Three downloads do this. You only run the manager. It fetches the other two.

- **SSF2 Beta Mod Manager** is the launcher. It browses GameBanana, turns mods on and off, and Make Mods lives here.
- **SSF2 Shell** is the game. The manager downloads [SSF2 Shell Base](https://github.com/SSF2-Mods-Official/SSF2-Shell-Base) and builds the Shell next to the manager. A later Shell update rebuilds it and keeps your mods and settings.
- **Next2Flash** compiles and decompiles. Setup downloads it, including portable Python and Java. You do not install those yourself. Event scripts compile with it. The game classes come from the Shell's own event file.

The manager does not ship SSF2. You still need Beta 1.4.0.1 on the PC.

### Setup

Setup opens the first time you launch. You can open it again from **Settings → Run setup**.

1. **Next2Flash.** Download it, or pick a folder you already have.
2. **Your game.** Pick the folder that contains `SSF2.swf`.
3. **SSF2 Shell.** The manager downloads the patches and builds the Shell. The first build takes about a minute.
4. **Debugging** is optional. It sets up VS Code or Cursor for ActionScript.

If `SSF2.swf` or the mappings file is not the 1.4.0.1 copy, the manager names those files and asks once for that folder. Continue builds anyway. At Play, vanilla files in the Shell that do not match are listed once. An expansion mod that fails the security check still does not start.

### What you can do

**Play** starts SSF2 Shell. **Configure** chooses the screen, match, or event it opens into, plus the boot intro and music.

**Download Mods** browses GameBanana. 1-Click still uses `ssf2mm:` links. Mods made for 0.9b stay hidden unless you ask for them. **Manage Mods** turns packs on and off. Changes apply the next time the game starts.

**Make Mods** covers events, costumes, music, boot intros, stages, and characters. Costume editing includes Palette Maker. The original Palette Maker is by VAL. IDK is by David (stariwinkle).

**JPEXS** is on the sidebar. It opens the copy that came with Next2Flash.

### Updates

**Check Updates** looks at the manager, Next2Flash, and SSF2 Shell together and asks once. Yes copies only the files that changed. A Shell you built from source is left alone. A Next2Flash source checkout is left alone.

Settings stay in `%AppData%\SSF2ModManager\`. Player data for the Shell (playlists, controller profiles) stays in `SSF2 Shell\expansions\user`.

## SSF2 Mod Manager v1.0.2

2026-06-14

### GameBanana 1-Click — please update

**Sorry — v1.0.0 and v1.0.1 do not work with GameBanana 1-Click.** Those builds could not handle the URL format the site sends (broken `https//` scheme and file matching). **You need v1.0.2** for 1-Click install.

**v1.0.2** adds full support:

- **`/mmdl/{fileId}` download URLs** (what GameBanana sends for 1-Click)
- Malformed launcher URLs like `ssf2mm://https//gamebanana.com/mmdl/...`
- File matching by ID from the mod API
- **Stays on Browse Mods** after a successful 1-Click install (does not jump to Installed)

```text
ssf2mm:https://gamebanana.com/mmdl/1708765,Mod,679407
```

Manual install from Browse still worked on older versions — only 1-Click needed this update.

### Other fixes

- **Filters stick** when you enable/disable mods on the Installed page
- **Update downloads** show the progress bar again
- **Remote news sync** — new articles download from GitHub on startup (Refresh on the News page forces a sync)
- **Unread badge** on the News sidebar button; **Mark all read** on the News page
- **Missing mod folder** — enabling a mod whose files were deleted now shows a popup with an option to re-download from GameBanana

### Getting the update

Download the latest zip from [GitHub Releases](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases) or use the in-app update check.

## SSF2 Mod Manager v1.0.1

2026-06-11

### What's improved in v1.0.1

This is a small polish release after **v1.0.0**. No new major features — just fixes and quality-of-life improvements.

### Themed popup windows

Install and settings dialogs (including **Target Version** when you install a mod) now respect your chosen theme instead of staying on the default dark blue look.

### 1-Click install docs

Documentation now matches what GameBanana sends for `ssf2mm:` links: the middle field is **ModelName** (usually `Mod`), not the mod's category name like Maps or Characters.

### Other fixes

- **Cancel** works again on the Target Version picker and other popup dialogs
- `run.bat` closes any already-running instance before launching a new dev build

### Getting the update

Download the latest zip from [GitHub Releases](https://github.com/SSF2-Mods-Official/SSF2ModManager/releases) and replace your existing folder, or let the in-app update check point you there.

Thanks for using SSF2 Mod Manager!

## SSF2 Mod Manager v1.0.0 is here

2026-06-07

### Welcome to the first release

**SSF2 Mod Manager v1.0.0** is now available. This is the first public release of our mod manager for Super Smash Flash 2.

If you have been installing mods by hand, this app is meant to make that easier: browse GameBanana, install mods, keep backups, and switch between different SSF2 builds from one window.

### What you can do

- **Browse mods** from GameBanana without leaving the app
- **Install, enable, disable, update, and uninstall** mods
- **Manage multiple SSF2 versions** (Beta, 9b, Sandbox, and more) with separate mod lists
- **Automatic backups** before mod files are changed
- **1-Click install** support via `ssf2mm:` links (when enabled on GameBanana)
- **12 themes** and English / Spanish UI

### Getting started

1. Download and extract the latest release zip.
2. Run **SSF2 Mod Manager.exe** (no .NET install required).
3. Open **Settings** and add your SSF2 folder(s).
4. Go to **Browse Mods**, pick a mod, and click **Install**.

Your mod data is saved in `%AppData%\SSF2ModManager\`.

### A quick note

Mods change game files. SSF2 Mod Manager creates backups, but you should still keep a copy of your vanilla SSF2 folder.

Thanks for trying v1.0.0 — have fun modding!
