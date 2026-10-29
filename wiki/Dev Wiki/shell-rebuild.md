---
title: Shell rebuild
summary: Manager updates always rebuild from vanilla. A target-hash miss is not “already patched.”
order: 25
group: Build
---

# Shell rebuild

## Info

| Shell rebuild |
| --- |
| Entry | `ShellBaseService.BuildAsync` → `shellbase.py build` |
| Input | Vanilla `SSF2.swf` + Next2Flash + `.ssfpatch` |
| Output | Shell folder overwrite (mods/state kept) |
| Related | Where things are |

A Shell update never patches the old Shell again. It decompiles the vanilla game, applies the pack’s patches, checks `base.json` target hashes, then copies the new build into the Shell folder.

## Contents

1. [How it works](#how-it-works)
2. [Four vanilla builds](#four-vanilla-builds)
3. [Fixed](#fixed)
4. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `engine_for` | Picks `patches/engine2` or `patches/vanilla/<sha>` for this `SSF2.swf` hash | `vanilla sha=` |
| `cmd_build` | Decompile → apply patches → match `target` hashes → compile `engine2.swf` | `engine2 source classes=… mismatched=` |
| `N2f.commit` | Short git HEAD when the Next2Flash folder is a checkout; empty for a release zip | Hint text uses `release zip (no git)` when empty |

## Four vanilla builds

SSF2 Beta 1.4.0.1 ships in more than one installer/portable layout. Each known `SSF2.swf` sha has its own patch set under `patches/vanilla/<sha>`, plus the capture set in `patches/engine2`. The 32-bit Program Files install is `0f13237a…`.

## Fixed

### Update failed with “Next2Flash release decompiles differently”

Symptom: Check Updates rebuild fails after a Shell release. Dialog blames Next2Flash.

Cause: `StageData.as` / `ModHost.as` were edited (profiler removal) without refreshing `base.json` `target` hashes on engine2 and every vanilla variant. Zip installs also have no git commit, so the old hint always said `release`.

Function: `cmd_build` target check. Log: `source mismatch com/mcleodgaming/ssf2/engine/StageData.as` (and ModHost).

## References

1. [Where things are](where-things-are.md) — where patches and build output live
2. [Built-in mods](built-in-mods.md) — packing mods into the Shell
