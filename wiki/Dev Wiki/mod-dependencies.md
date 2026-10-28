---
title: Mod dependencies
summary: A mod lists the GameBanana mods it needs, and a download installs those too.
order: 40
group: Mods
---

# Mod dependencies

## Info

| Mod dependencies |
| --- |
| Field | `dependencies` in `info.json` |
| Ids | GameBanana mod ids, such as `714453` from `https://gamebanana.com/mods/714453` |
| Write | `ModDependencies.Write` |
| Install | `ModManagerService.InstallModAsync` |

A creator lists the other GameBanana mods this one needs. Downloading it also downloads those mods.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How to use it

In Make Mods, open Edit mod info. Type a mod name. Double-click a GameBanana row to add it, or press Enter on an id or a `gamebanana.com/mods/` link. Save info.json, then package the zip. The new-event form does not ask for this. When someone installs that zip, each listed id is downloaded if that build does not already have it.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `TxtDepSearch_TextChanged` | After a short pause, `SearchModsAsync` lists SSF2 mods for the typed name. | |
| `ModDependencies.Parse` | Turns a pasted id or link into unique mod ids. A link uses the number after `mods/`. A whole token of digits is an id. | |
| `ModDependencies.Write` | Stores those ids as a JSON array on `dependencies`. | |
| `InstallModAsync` | After the mod is saved, downloads each listed id that this build does not have. A loop of the same id is skipped. | `Installing dependency` |

## Files

| What | Path |
| --- | --- |
| Parse and write | `src/Services/ModDependencies.cs` |
| Edit mod info | `src/Views/MakeModsWindow.xaml` |
| Download | `src/Services/ModManagerService.cs` |

## Check

`ModDependenciesTests` parses `714453`, a GameBanana link, and a repeated id.

## References

1. [Event wizard](event-wizard.md) — events do not set dependencies. Packaging does.
