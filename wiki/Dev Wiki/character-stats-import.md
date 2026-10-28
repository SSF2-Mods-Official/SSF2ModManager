---
title: Character stats import
summary: CharacterData.importData copies cData onto m_ fields. A missing field is skipped. roll_delay alerts on the debug console.
order: 90
group: Characters
---

# Character stats import

## Info

| Character stats import |
| --- |
| Copy | `CharacterData.importData` |
| Caller at boot | `ExpansionResources.captureAndDisposeExpansionCss` |
| Deprecated field | `roll_delay` |
| Log | `[ResourceManager] expansion stats skip` |
| Related | Where things are |

`CharacterData.importData` copies each `cData` key onto `m_` plus that key. A key with no `m_` field is skipped. `roll_delay` still has `m_roll_delay`, and the debug alert for it runs before MenuController.init creates the console.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [References](#references)

## How it works

`Stats.createCharacterDataFrom` calls `importData` on `cData`, then `importAttacks`, `addProjectiles`, and `addItems`. `captureAndDisposeExpansionCss` calls `Stats.getStats(id, -1)` while the expansion SWF is loaded. `MenuController.init` runs after that capture returns. `Main.DEBUG` is true at boot.

| Function | What it does | Log |
| --- | --- | --- |
| `CharacterData.importData` | If `roll_delay` is present and `Main.DEBUG` is true, calls `MenuController.debugConsole.alert`. Then copies each key that has an `m_` field. `sounds` is copied key by key. Any other key sets the return flag false. | None in the engine. |
| `Stats.createCharacterDataFrom` | Builds one `CharacterData` from `cData`, attacks, projectiles, and items. | None. |
| `ExpansionResources.captureAndDisposeExpansionCss` | Calls `getStats`, captures the series icon, then unloads the fighter SWF. A throw from `getStats` is logged and the unload still runs. | `[ResourceManager] expansion stats skip id=` |

## Files

| What | Path |
| --- | --- |
| `importData` | `SSF2 Shell/expansions/decompiled/engine2/scripts/com/mcleodgaming/ssf2/engine/CharacterData.as` |
| Boot capture | `SSF2 Shell/src/mods/expansions/src/com/mcleodgaming/ssf2/util/ExpansionResources.as` |
| Console created | `MenuController.init` in `engine2/scripts/com/mcleodgaming/ssf2/controllers/MenuController.as` |


## References

1. Where things are — engine2 and the expansions pack.
2. Resource cache — the unload after the CSS capture.
