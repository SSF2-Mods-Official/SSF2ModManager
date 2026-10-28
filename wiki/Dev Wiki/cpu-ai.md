---
title: CPU AI
summary: The engine CPU is the original one. A mod hooks the decision.
order: 80
group: Build
---

# CPU AI

## Info

| CPU AI |
| --- |
| File | `SSF2 Shell Base/patches/engine2/patches/com/mcleodgaming/ssf2/engine/AI.as.patch` |
| Source | The original `AI.as` from SSF2 1.4.0.1 |
| Hook | `AI_DECIDE` at the end of `AI.getAction` |
| Levels | 0 to 9, set with `AI.setLevel` |

The engine CPU is the original one. `AI.getAction` still picks the action. After that it calls `AI_DECIDE` with `ai`, `action`, `player`, and `stage`. A changed `action` is written back to `m_action`.

## Contents

1. [How it works](#how-it-works)
2. [Check](#check)
3. [References](#references)

## How it works

| Function | What it does | What you see |
| --- | --- | --- |
| `AI.getAction` | Runs the original action, then `ModHost.runHook` on `AI_DECIDE` | The context is `ai`, `action`, `player`, and `stage` |
| `AI_DECIDE` | The hook object is `ai`, `action`, `player`, and `stage` | A mod changes `action`. That value is stored on `m_action` |

`CpuSkill` is not in the engine. The original file does not call it.

## Check

A match with no `AI_DECIDE` mod plays the original CPU. A mod that hooks `AI_DECIDE` can replace the keys for that frame.

## References

1. [Where things are](where-things-are.md) — where the engine sources live.
2. Change the game's code — decompile and compile `engine2.swf`.
