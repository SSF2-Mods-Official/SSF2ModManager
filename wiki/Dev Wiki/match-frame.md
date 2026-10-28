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
| Official game | The 1.4.0.1 player SWF has no `m_getKey` hook |

`Character.m_getKey` builds an input object only when a mod is listening.

## Contents

1. [How it works](#how-it-works)
2. [Push apart](#push-apart)
3. [Check](#check)
4. [References](#references)

## How it works

`HookGate.listening` is false when the hook list is missing or empty. `ModHost.hasHook` uses that for `BEFORE_INPUT_APPLY`. `Character.m_getKey` calls `hasHook` before it builds the input object, then `ModHost.runHook`.

| Function | What it does | What you see |
| --- | --- | --- |
| `HookGate.listening` | True when the list has one listener | `false` for null and for an empty list |
| `ModHost.hasHook` | `listening` for that hook id | No input object when `BEFORE_INPUT_APPLY` is empty |
| `Character.m_getKey` | Builds the input object only when `hasHook` is true | The key read still runs |
| `InteractiveSprite.hitTest` | Compares the two sprites’ hitboxes for the requested box type | `Character.m_pushAwayOpponents` calls it with the last argument true |

## Push apart

`InteractiveSprite.hitTest` reads `CurrentAnimation` before `getHitBoxes`. `Character.m_pushAwayOpponents` calls `hitTest` with the last argument true.

## Check

```
powershell -NoProfile -ExecutionPolicy Bypass -File "SSF2 Shell Base/tools/fixtures/engine/framegate/run-fixture.ps1"
```

The result file says `OK` when a null hook list is false, an empty hook list is false, and a list with one listener is true.

## References

1. [Where things are](where-things-are.md) — an optional pack with `enabledByDefault: false` starts off.
