---
title: Performance
summary: What runs every frame in a match, the hook fast path, and the rules that keep engine patches and built-in mods cheap.
order: 60
group: Build
---

# Performance

## Info

| Performance |
| --- |
| Engine | `modhost/ModHost.as`, `engine/Character.as`, `engine/StageData.as`, `util/Vcam.as`, `engine/AI.as`, `platforms/Platform.as`, `enemies/Enemy.as`, `engine/HitBoxSprite.as` (decompiled `engine2/changed`) |
| Mods | `palette_maker`, `manager_bridge`, `controller_profiles` |
| Frame budget | 60 fps, about 16 ms for the whole frame |
| Related | [Built-in mods](built-in-mods.md) |

The engine calls into the mod host from code that runs every frame for every fighter, so a call with no mod attached must cost nothing.

## Contents

1. [Rules](#rules)
2. [How it works](#how-it-works)
3. [Hot paths](#hot-paths)
4. [Check](#check)
5. [References](#references)

## Rules

- A per-frame call site checks `ModHost.hasHook(id)` before it builds a context object, and `ModHost.hasPatch(id)` before it builds an args array for `door` or `applyPatch`.
- No `GameLog.write` in a per-frame path. Every line opens, appends and closes the log file.
- No `new Point`, `new Dictionary`, `JSON.stringify` or filter array inside an `ENTER_FRAME` handler; keep them in fields.
- No file `exists` or `modificationDate` check more than twice a second.
- Menu-only listeners attach on the menu's show hook and detach on hide or at `MATCH_START`.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `ModHost.hasHook` | `HookGate.listening` on the listener list: true when one or more mods listen. | none |
| `ModHost.door` | Returns the default straight away when no patch is set, so no closure is made. | none |
| `ModHost.runHook` | One dictionary lookup when nobody listens. The caller's object literal is the cost, which is why call sites check `hasHook` first. | `[ModHost] hook <id> #<i> error: ...` |
| `Character.updatePaletteSwap` | `BEFORE_STANCE_PAINT` and `AFTER_STANCE_PAINT` objects are only built when a mod listens; `PALETTE_SHOULD_COMBINE_FORM_PALETTE` only runs when patched. | none |
| `Character.jumpIsPressed`, `jumpIsHeld` | `RESERVE_JUMP2_AS_SWITCH` only runs when patched. | none |
| `StageData.nextFrameAllEffects` | Calls `Utils.advanceEffectFrame` directly unless `advanceEffectFrame` is patched. | none |
| `Vcam` update, `AI` decide, `Platform.PERFORMALL`, `Enemy.PERFORMALL`, `HitBoxSprite` constructor | `CAMERA_UPDATE`, `AI_DECIDE`, `PLATFORM_STEP`, `ENEMY_AI_STEP`, `HITBOX_CREATE` only build their object when a mod listens. | none |

## Hot paths

| Where | How often | State |
| --- | --- | --- |
| `CostumeFxPaletteRuntime.tickLive` (`palette_maker`) | Every frame while any player has an effect-recolor costume | `buildProjectileFxAliasMap` is cached per stats name and cleared by `resetMatchState` or a costume change. `uidOwnersChanged` rebuilds the owner map only when a player or costume differs. A stage layer is walked only when it gained children, a hook forces it, or the 30-frame safety pass runs. |
| `StreamCursor.onCursorEnterFrame` (`manager_bridge`) | Every third frame | Off during matches; one `Point`; `pointer.json` checked at most every 500 ms. |
| `ProfileCssHooks.onStagePencil` (`controller_profiles`) | Every click | `MOUSE_DOWN` only, so the name list opens once per click. |
| `ControllerProfilesMod.onFrame` | Every frame | Attached only while Character Select or Controls is shown. |
| `StreamCmdChannel.onCmdTimer` (`manager_bridge`) | Every 500 ms | The song id is compared before any label or JSON is built. |
| `ResourceManager.getCostume` | Each projectile spawn and color filter update | Builds the merged costume list twice per call. Not cached yet. |

## Check

1. Play a match with no optional mods on. `game.log` has no `[ModHost] hook` errors.
2. `shellbase.py verify` prints `verify OK` after the engine change.

## References

1. [Built-in mods](built-in-mods.md): the mods named here and how to rebuild them.
