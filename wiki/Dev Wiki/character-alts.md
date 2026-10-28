---
title: Character alts
summary: Voice alts can keep the stock character SWF and play clips from a sounds folder; jump cycles every alt, and the alt name can show on the match HUD.
order: 80
group: Characters
---

# Character alts

## Info

| Character alts |
| --- |
| Engine | `util/CharacterAlts.as`, `util/ResourceManager.as`, `controllers/Chip.as`, `menus/CharacterSelectMenu.as`, `engine/StageData.as`, `engine/Character.as` |
| Manager | `CharacterAltsService`, `AltSoundEditService`, Make Mods Character Alts, `AltSoundPackService` |
| Data | `expansions/alts/alts.json`; sound-only folders under `expansions/alts/local/<character>/<name>/sounds/` |
| Example | Alternate Voices Plus (SWF UrlOverride); Make Mods local voice (stock SWF + `sounds/`) |
| Related | [One-click install](one-click-install.md), [GameBanana installs](gamebanana-installs.md), [Engine resources](resource-cache.md) |

An alt is another voice (or file set) for a character the game already has. **Make Mods** voice alts keep the stock character SWF and play replacements from `sounds/`. **GameBanana** alts may still swap the character SWF via `UrlOverride`.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Rebuild](#rebuild)
5. [Check](#check)
6. [References](#references)

## How to use it

**Download (GameBanana):** installs as **Alt of …** with a shrunk sound-pack SWF and `UrlOverride` (scope all costumes). Jump cycles stock, then each such alt.

**Make Mods → Character Alts:** Install exports sounds into `expansions/alts/local/<character>/<name>/sounds/` and writes `sounds/linkages.json`. There is **no** copied `character.swf` in that folder. `alts.json` uses `mode: "sounds"`. The game keeps the stock character and plays files from `sounds/` when the alt is active. Jump cycles the alt; costume-tied also selects that costume.

**Manage Mods → Alts → Edit:** name / scope / Pick in game…, plus sound editor (dropdown `*`, replaced count, preview, replace, restore, side-by-side, play both). Replace matches the new clip’s volume to the original, updates `sounds/` only (no SWF inject). **Remove** deletes the alt.

**Match HUD:** with no nametag, the damage counter shows the alt `name`.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `CharacterAltsService.InstallSounds` | Local voice alt: folder + `mode:sounds`, no `files` map / no character SWF copy. | `[alts] install … mode=sounds` |
| `CharacterAlts.apply` | SWF alts: `UrlOverride`. Sound-only: clear override, register linkage→file map. | `[alts] apply mode=sounds character=<id>`, `[alts] apply resource=…` |
| `CharacterAlts.tryGetAltSound` | Active sound-only alt: disk `Sound` only for **replaced** linkages (`replacements.json`); unreplaced stay in the stock SWF. Lazy-load (no preload of the whole folder). | `[alts] sounds replaced=<n>`, `[alts] sound load path=` |
| Costume change vs jump | Changing costume off a costume-tied jump pick clears that jump pick so the alt does not follow other costumes. | `[alts] clear jump pick costume mismatch p=<n>` |
| `ResourceManager.getLibrarySound` | Announcer hook, then `tryGetAltSound`, then stock library class. | — |
| `AltSoundEditService.ReplaceAsync` | Backup, volume-match to original, write `sounds/` + `replacements.json`. No SWF inject. | `[alts] sound replace …` |
| `Chip.checkControls` | Jump cycles every alt; costume-tied sets costume. | `[alts] cycle …` |
| `CharacterAlts.hudName` | Match HUD name when no nametag. | — |

## Files

| What | Path |
| --- | --- |
| Engine | `CharacterAlts.as`, `ResourceManager.as`, `Chip.as`, `CharacterSelectMenu.as`, `StageData.as`, `Character.as` |
| Manager | `CharacterAltsService.cs`, `AltSoundEditService.cs`, `MakeModsWindow`, `AltEditWindow` |
| Sound-only data | `…/<alt>/sounds/*.wav\|mp3`, `sounds/linkages.json`, `sounds/replacements.json`, `sounds/_originals/` |

## Rebuild

Compile engine2 (`SSF2_COMPILE_ENGINE=1`), copy into Base Build `data/engine/`. Never commit the decompiled engine.

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "CharacterAlts|AltSoundPack|AltSoundEdit|ExpansionMods"
```

Make Mods alt: no character `.swf` in the folder; jump plays custom VO from stock character. Replace: volume matches original; no inject. GameBanana SWF alts still use UrlOverride.


## References

1. [One-click install](one-click-install.md)
2. [GameBanana installs](gamebanana-installs.md)
3. [Engine resources](resource-cache.md)
