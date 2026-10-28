---
title: Boot intro
summary: Skipping the custom boot intro shows the title menu.
order: 40
---

# Boot intro

## Info

| Boot intro |
| --- |
| Mod | `SSF2 Shell/data/ssf2mm/ssf2_stream` |
| Skip | `endCustomBootIntro` |
| Title | `showTitleAfterBootSkip` |
| Related | Where things are |

Skipping the custom boot intro removes the video and shows the title menu.

## Contents

1. [How it works](#how-it-works)
2. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `endCustomBootIntro` | Removes the video layer and shows the title. | `[ssf2_stream] custom intro skip → title` |
| `showTitleAfterBootSkip` | Calls `titleMenu.show`. | `[ssf2_stream] showTitleAfterBootSkip ok` |
| `requestSkipToTitle` | Clears an attract intro and shows the title. | `[attract] attract cancel → title` |
| `updateDiscordPresence` | Ignores a missing `updateActivity` method so `titleMenu` returns. The catch does not call `GameLog`. | |
| `log` | Writes `game.log` and traces the same line. | `[ssf2_stream] custom intro skip → title` |


## References

1. Where things are — where the shell and the coding folder live.
