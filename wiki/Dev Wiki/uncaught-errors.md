---
title: Uncaught errors
summary: A live match stays up when a script error is thrown. Menu and black-screen errors still leave the match.
order: 100
group: Build
---

# Uncaught errors

## Info

| Uncaught errors |
| --- |
| Policy | `handleUncaughtStorm` |
| Mod | `manager_bridge` |
| Related | [Stream debug](stream-debug.md) |

A live match logs an uncaught error and stays on the stage. It does not return to the title after a count of errors.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [References](#references)

## How it works

`handleUncaughtStorm` stays in the match when the error is not fatal. Fatal means the screen is not a live match, the error id is 1069, or the UI has been torn down. Those still run `runSimpleCrashRecovery`.

| Function | What it does | Log line |
| --- | --- | --- |
| `handleUncaughtStorm` | Logs a live-match error and returns. A fatal error tears the match down and opens the title. | `uncaught tolerate — stay in match` |

## Files

| What | Path |
| --- | --- |
| `handleUncaughtStorm` | `SSF2 Shell Base/shell/src/mods/manager_bridge/src/StreamCrash.as` |

## References

1. [Stream debug](stream-debug.md) — how F5 launches the game that runs this mod.
