---
title: Match frame
summary: The shell calls on a fight frame, and when they do nothing.
order: 70
group: Build
---

# Match frame

## Info

| Match frame |
| --- |
| Runs | Every fight frame |
| Input | `Character.m_getKey` |
| Spans | `StageData.telemetryClass` |
| Official game | The 1.4.0.1 player SWF has neither call |

`Character.m_getKey` builds an input object only when a mod is listening, `StageData.telemetryClass` looks up [Profiler](profiler.md) once, and `FrameBudget.afterTick` asks AIR to collect only when the tick finished in under 16 ms.

## Contents

1. [How it works](#how-it-works)
2. [Frame slack](#frame-slack)
3. [Push apart](#push-apart)
4. [Check](#check)
5. [References](#references)

## How it works

`HookGate.listening` is false when the hook list is missing or empty. `ModHost.hasHook` uses that for `BEFORE_INPUT_APPLY`. `Character.m_getKey` calls `hasHook` before it builds the input object, then `ModHost.runHook`. `TelemetryLookup` starts held. `StageData.holdTelemetry` holds it again while mod SWFs reload. `StageData.releaseTelemetry` runs when the pending mod SWF count hits 0. The next `StageData.telemetryClass` call looks up `AirTelemetry` through `TelemetryLookup.resolve`. Later frames return the cached class and do not call `getTimer`.

| Function | What it does | What you see |
| --- | --- | --- |
| `HookGate.listening` | True when the list has one listener | `false` for null and for an empty list |
| `ModHost.hasHook` | `listening` for that hook id | No input object when `BEFORE_INPUT_APPLY` is empty |
| `Character.m_getKey` | Builds the input object only when `hasHook` is true | The key read still runs |
| `TelemetryLookup.resolve` | Returns the cached class. A hold, or a finished lookup, does not call the finder | `lookups` stays 1 after the first find |
| `StageData.holdTelemetry` | Clears the cache while mod SWFs reload | No spans during the reload |
| `StageData.releaseTelemetry` | Allows one lookup after the loads finish | Spans on the next match frame when the pack is on |
| `StageData.telemetryClass` | `resolve` with `getDefinitionByName("AirTelemetry")` | No `getTimer` on a frame that already knows |
| `FrameBudget.shouldCollect` | True when the tick used less than 16 ms | A 10 ms tick collects. A 16 ms tick does not |
| `FrameBudget.afterTick` | Calls `System.pauseForGCIfCollectionImminent(0.75)` when `shouldCollect` is true | `match.gc` on the profiler strip |
| `StageData.renderComplete` | Times `PERFORMALL`, then `afterTick` | The sweep runs after the fight tick |
| `InteractiveSprite.hitTest` | Compares the two sprites’ hitboxes for the requested box type | `Character.m_pushAwayOpponents` calls it with the last argument true |

## Frame slack

`StageData.renderComplete` times `PERFORMALL` with `getTimer`. `FrameBudget.shouldCollect` is true when that time is under 16. `FrameBudget.afterTick` then calls `System.pauseForGCIfCollectionImminent(0.75)`, so AIR sweeps only when a collection is already close. A tick of 16 ms or more does not ask. The sweep is the `match.gc` span. This path does not write `game.log`.

## Push apart

`InteractiveSprite.hitTest` reads `CurrentAnimation` before `getHitBoxes`. `Character.m_pushAwayOpponents` calls `hitTest` with the last argument true.

## Check

```
powershell -NoProfile -ExecutionPolicy Bypass -File "SSF2 Mod Manager/SSF2 Shell/data/engine/fixture/framegate/run-fixture.ps1"
```

The result file says `OK` when an empty hook list is false, a held lookup does not run, the second resolve does not call the finder, a 10 ms tick collects, and a 16 ms tick does not.

## References

1. [Profiler](profiler.md) — the pack that defines `AirTelemetry`. It starts off. F1 turns it on.
2. [Where things are](where-things-are.md) — an optional pack with `enabledByDefault: false` starts off.
