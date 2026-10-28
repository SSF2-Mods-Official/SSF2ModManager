---
title: Make a small mod
summary: Copy the template, compile, turn it on in Manage Mods.
order: 20
---

A **small mod** is a folder under `SSF2 Shell\expansions\mods\`. The game loads `mod.swf`. You edit the files in `src\`.

Finish [Set up Mod Manager](../Start Here/setup.md), then click **Make Mods** on the launcher once so the editor tools are ready.

## 1 — Copy the template

1. Go to `SSF2 Shell\expansions\mods\`
2. Copy `_template`
3. Paste it as a new folder, e.g. `my_first_mod`

Folders that start with `_` are ignored. The new name must **not** start with `_`.

## 2 — Name it

Open `mod.json` in your new folder. Set:

- `id` — same as the folder name (`my_first_mod`)
- `name` — what you want to see in the list (`My First Mod`)

Save.

## 3 — Compile

Use **Make Mods** → **Modding Tools** → **Compile** and pick `Mod: my_first_mod`. In Visual Studio Code, SSF2 Stream **Compile** lists the same mods.

Wait for it to finish. That builds `mod.swf`.

## 4 — Turn it on

1. On the launcher, click **Manage Mods**
2. Enable your pack
3. Click **Play** or **Debug** to try it

If it does not show up, check that the folder name matches `id`, and that the compile finished.

## Optional: say hello in the log

In `src\ExampleMod.as` you can add something like:

```
host.addHook("MATCH_START", function(ctx:Object):void {
    trace("My mod: match started!");
});
```

Then compile again, use **Debug**, start a match, and look at the editor Output / Stream log.

Getting the pack to show up in Manage Mods is enough for a first test.
