---
title: Character select back
summary: Holding Back leaves the character select that is on screen. Versus returns to the group menu.
order: 21
group: Characters
---

# Character select back

## Info

| Character select back |
| --- |
| Hold | Back (`BUTTON1`) on a character select |
| Function | `Chip.checkGrab` |
| Versus | `VSMenu.backMain_CLICK` shows the group menu |
| Log | `[css] back hold` |
| Related | [Where things are](where-things-are.md) |

Holding Back on a character select leaves that screen. The chip calls `backMain_CLICK` on the menu that owns it. Versus returns to the group menu.

## Contents

1. [How it works](#how-it-works)
2. [References](#references)

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `Chip.checkGrab` | When the back timer finishes, it calls `backMain_CLICK` on `m_characterSelectMenu`, the menu that owns the chip. Versus then shows the group menu | `[css] back hold` |

## References

1. [Where things are](where-things-are.md) — where the engine and the shell live.
