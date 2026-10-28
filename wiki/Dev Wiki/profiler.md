---
title: Profiler
summary: The Log window graphs SSF2 frame time, memory, and named spans from an AIR telemetry session.
order: 110
group: Build
---

# Profiler

## Info

| Profiler |
| --- |
| Where | Log window, the strip above the text |
| Listens | `127.0.0.1:7935` |
| Measures | `.enter` frame deltas, `.mem.total`, `.mem.private`, and `startTiming` spans |
| In-game pack | The game looks up a class named `AirTelemetry`. That class is not in the shell source |
| Starts | The Log window listens. The strip stays empty until that class connects |
| Spike | A frame slower than 68 ms. One frame at 30 fps is 34 ms |

The session follows [hxtelemetry](https://github.com/jcward/hxtelemetry): one `.enter` per frame, memory only when the kilobyte value changes, and named spans from `startTiming` / `endTiming`. SSF2 is Adobe AIR, so there is no Haxe C++ call-stack dump.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [The strip](#the-strip)
4. [Match frame](#match-frame)
5. [Latest run](#latest-run)
6. [Files](#files)
7. [Check](#check)
8. [References](#references)

## How to use it

Open **Log** in the launcher, then start a match. The strip stays empty until a class named `AirTelemetry` has loaded. That class is not in the shell source. The strip shows fps and the slowest recent frame. A red bar is a frame over 68 ms. Drag the slider under the bars, or click a bar, to read that frame. The line under the graph names the spans inside the selected frame. The right end of the slider follows the newest frame. Scroll the log up to read older lines. When the view is at the bottom, new lines stay in view.

## How it works

`StageData.telemetryClass` looks up a class named `AirTelemetry` through [Match frame](match-frame.md) `TelemetryLookup.resolve`. That class is not in the shell source. A held lookup returns null. After the mod SWFs finish, one call runs `getDefinitionByName`. `GameProfilerListener` listens on loopback. `GameProfilerSession.Read` turns the event list into frames. `GameProfilerSession.SpikeText` is the row for one frame: it adds spans that share a name and skips a span of 0 ms. `GameProfilerMath.WindowStart` is the first bar drawn when the strip shows 160 bars around the selected frame. `GameProfilerSession.SampleLine` is the one line the Log window appends to `game.log` for that batch.

| Function | What it does | What you see |
| --- | --- | --- |
| `StageData.lookupAirTelemetry` | `getDefinitionByName("AirTelemetry")`. The class is not in the shell source | The strip stays empty until that class loads |
| `GameProfilerSession.Read` | Groups spans that arrived before each `.enter` | The bars and the memory line |
| `GameProfilerSession.SpikeText` | Frame delta, then each span. Spans that share a name are added. A span of 0 ms is skipped | The line under the graph for the selected frame |
| `GameProfilerMath.WindowStart` | First visible bar so the selected frame sits in the 160-bar window | The bars around the slider position |
| `StageData.telemetryClass` | One `AirTelemetry` lookup after mod SWFs finish. Later frames use the cache | No spans when the pack is off |
| `StageData.performAllEvents` | Starts `match.draw`, then `stage.invalidate` | `match.draw` on the next frame |
| `StageData.renderComplete` | Ends `match.draw` and times `PERFORMALL` as `match` | `match` on that same frame |
| `StageData.PERFORMALL` | Times `match.stage`, `match.chars`, and `match.hits` inside the tick | Those names on a slow frame |
| `FrameBudget.afterTick` | Sweeps after a tick under 16 ms. See [Match frame](match-frame.md) | `match.gc` on that frame |
| `GameProfilerSession.SampleLine` | fps, slowest frame, Flash MB, private MB, and the spike text when a frame is over 68 ms | `[profiler] sample` in `game.log` |

## The strip

The header is fps, the slowest recent frame, Flash heap MB, process private MB, and CPU. The key under the header reads: green bars are 34 ms or under, yellow bars are through 68 ms, red bars are a hitch, and the purple line is private memory. `Main.FRAMERATE` is 30, so one on-time frame is about 33 ms. The slider selects one frame. `GameProfilerMath.WindowStart` keeps that frame inside the 160 bars, and the newest frames sit on the right while the slider is at the end. The outlined bar is the selection. While no events have arrived the strip says SSF2 has not connected.

## Match frame

`match.draw` is the time from `performAllEvents` to `renderComplete`. That is the display-list render after `stage.invalidate`. `match` is the whole `PERFORMALL` call. `match.stage` is the span around the stage update. `SpikeText` adds spans that share a name. `match.chars` and `match.hits` are the spans `StageData.PERFORMALL` starts around the character tick and the hit tests. A red frame with no name after the milliseconds means none of these spans ran.

## Latest run

**Only latest run** is checked when the Log window opens. `LogRunSlice.Latest` keeps the text after the last run marker in each file. The SSF2 marker is `[Bootstrap] loading engine1`. The Mod Manager marker is `ModManagerService initialized`. The debug file is already one process: `DevFileLog.Reset` clears it at startup, and the marker there is `[OnStartup]`. A file with no marker stays whole. **Copy all** copies that filtered text.

## Files

| File | Role |
| --- | --- |
| `SSF2 Shell/expansions/mods/profiler/src/AirTelemetry.as` | `.enter`, `.mem`, `startTiming`, `endTiming`, socket flush |
| `SSF2 Shell/expansions/mods/profiler/src/ProfilerMod.as` | Calls `advanceFrame` on `ENTER_FRAME` |
| `src/Services/GameProfiler.cs` | `GameProfilerSession` and the listener on port 7935 |
| `SSF2 Shell/data/engine/decompiled/changed/com/mcleodgaming/ssf2/engine/StageData.as` | `match`, `match.draw`, `match.stage`, `match.chars`, `match.hits` |
| `src/Views/LauncherLogWindow.xaml` | The strip, the slider, and the log |
| `src/Services/LogRunSlice.cs` | `Latest` keeps the lines after the last run marker |

## Check

```
dotnet test "src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj" --filter GameProfiler
```

The session test feeds a `.enter` of 84 ms with a `paint.raster` span of 70 ms and checks the spike row. Two `match.stage` spans in one frame add into one number. `WindowStart` for 400 frames, selected at the last frame, with 160 bars, is 240. The ActionScript fixture collects the paint frame with no socket and no window.


## References

1. [Where things are](where-things-are.md) — `game.log` is the text under this strip.
2. [Match frame](match-frame.md) — `telemetryClass` caches a missing `AirTelemetry`, and the input hook skips its object when nobody is listening.
