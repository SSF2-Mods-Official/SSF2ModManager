---
title: Change the game’s code
summary: Decompile engine2 from Modding Tools, edit a script, compile it back.
order: 10
---

`engine2.swf` is the game’s code (menus, fighters, modes). `engine1.swf` is the library the game loads at runtime. Compile uses a SWC built from those `engine1` scripts. You edit `engine2`.

Finish [Set up Mod Manager](../Start%20Here/setup.md).

## 1 — Decompile

1. On the launcher, click **Make Mods**
2. Click **Modding Tools**, then **Convert to FLA**
3. Folder: the engine folder (`data\engine`)
4. File: `engine2.swf`

The scripts are written to `SSF2 Shell\expansions\decompiled\engine2\scripts`. The same run writes `externs\engine1.swc` once, from `engine1.swf`. A later decompile leaves that folder in place.

## 2 — Edit

Open one `.as` file under `expansions\decompiled\engine2\scripts`. Change a small thing. Save.

## 3 — Compile

1. **Modding Tools** → **Compile folder**
2. Pick `engine2`
3. Click **Compile**

That writes `SSF2 Shell\data\engine\engine2.swf`. If the log shows red errors, fix that file and compile again.

## 4 — Test

Click **Play** or **Debug** on the launcher.

## Don’t

| Don’t | Do this |
| --- | --- |
| Save engine2 in JPEXS | **Compile folder** |
| Zip `engine1.swf` and hand it to the compiler | Let Convert to FLA build `externs\engine1.swc` |
| Compile over a game that is still open | Quit SSF2 first |
