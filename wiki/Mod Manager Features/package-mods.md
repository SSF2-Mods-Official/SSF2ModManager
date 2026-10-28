---
title: Package and upload a mod
summary: Write the mod's info, seal an .ssfe, and submit that file on GameBanana.
order: 44
---

**Make Mods**, then **Package mods**. Packaging makes the file other people install. The manager does not send it to GameBanana. You upload that file on the site yourself.

Downloaded mods are left off these lists. Only packs you made on this machine are packaged from here.

## Edit the info first

**Edit info** writes `info.json` for one pack. That file is the name, creator, and description Download Mods and Manage Mods show.

Pick the mod in the dropdown.

- **Name.** Required.
- **Creator.** Your name on the mod.
- **Type.** Character, Stage, Event, and the other types this manager knows. Pick the one that matches what you made. GameBanana uses Map for a stage. The type here should still say Stage.
- **Version.** A version string, such as `1.0`.
- **Description.** What the mod is.
- **Dependencies.** Other GameBanana mods this one needs. Type part of a mod's name, wait for the list, and double-click a row. Enter also adds a number, or a `gamebanana.com/mods/` link. Each dependency shows as a chip. Click the chip to remove it.

**Save info.json** writes those fields into the pack's folder. The log line is the folder it wrote.

## Package

**Package mods** is the file you will upload.

Check every pack that should go in. One pack keeps its own name. More than one asks for a pack name and a description, then writes a single file.

**Package** opens a save dialog.

- **.ssfe** is the one to upload. It is a sealed package. Source, fixtures, and rebuild scripts are left out, so people get the mod without your project files.
- **.zip** is a plain archive of the same packs. Source and `build.bat` are left out of the zip too. Use this only if you specifically need a zip. Download Mods prefers `.ssfe`.

## Upload on GameBanana

1. Open [GameBanana](https://gamebanana.com/) and sign in.
2. Submit a mod for **Super Smash Flash 2**.
3. Set the category to match the pack: Character, Map for a stage, Event, or a general mod. Those are the same groups **Download Mods** browses.
4. Attach the `.ssfe` as the download. Put the name, version, and description you saved in the info.
5. If the mod needs another mod, list that mod the same way you did under Dependencies. Someone who downloads yours from this manager also gets the dependencies you saved.

After GameBanana publishes it, **Download Mods** can list it, and the page's download button installs the `.ssfe`. A one-click link from the site opens this manager and installs it the same way.

## Install a package you were sent

**Install a mod archive** takes `.ssfe`, `.zip`, `.rar`, and `.7z`. That is how you test the file you just packaged, or install one someone sent you, without going through the site.
