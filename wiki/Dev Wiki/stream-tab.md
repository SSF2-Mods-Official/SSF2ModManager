---
title: SSF2 Stream tab
summary: Debug start shows the SSF2 Stream tab in the first editor group.
order: 130
group: Build
---

# SSF2 Stream tab

## Info

| SSF2 Stream tab |
| --- |
| Open | `openStreamPanel` |
| Group | First editor group (`streamTabShowOptions`) |
| Log | `[ssf2-stream] stream tab column=1` |
| Related | [Where things are](where-things-are.md) |

Debug start shows the SSF2 Stream tab in the first editor group, then can focus the Debug Console.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `activate` | Loads `debugLaunch.js`, which loads `next2flash.js`. A missing file throws and the tab is never created. | `Cannot find module './next2flash'` |
| `streamTabShowOptions` | Returns column `1` and `preserveFocus` false. | `[ssf2-stream] stream tab column=1` |
| `openStreamPanel` | Focuses the editor group, then creates or reveals the tab with those options. | `[ssf2-stream] stream tab column=1` |
| `reveal` | `panel.reveal(1, false)` so the tab is the visible editor. | `[ssf2-stream] stream tab column=1` |

F4 runs `ssf2Stream.openPanel`, which calls `openStreamPanel`. A debug session does the same when `autoOpenOnDebug` is on. After the tab is visible, `focusDebugConsole` focuses the Debug Console, then `reveal(1, true)` keeps the tab in that group.

## Files

| What | Path |
| --- | --- |
| Open | `tools/ssf2-stream-extension/src/extension.ts` |
| Tab | `tools/ssf2-stream-extension/src/streamPanel.ts` |
| Column | `tools/ssf2-stream-extension/src/streamTab.ts` |

The running copy is the installed extension named in [Where things are](where-things-are.md). Its `out` folder has to include `next2flash.js`. Reload the editor after that copy is updated.

## Check

`node tools/ssf2-stream-extension/fixture/stream-tab-check.js` prints `OK`.


## References

1. [Where things are](where-things-are.md) — the installed SSF2 Stream extension and the coding folder.
