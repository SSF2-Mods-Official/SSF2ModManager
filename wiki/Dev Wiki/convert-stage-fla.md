---
title: Convert a stage to FLA
summary: Stage Convert to FLA keeps every clip under Animations. The main clip stays in that folder.
order: 50
group: Stages
---

# Convert a stage to FLA

## Info

| Convert a stage to FLA |
| --- |
| Convert | Make Mods → Modding Tools → Convert to FLA |
| Stage file | `data/stage/….ssf` |
| Library | `BuildFlaOrganizeBlueprintFromEntries` with `stageLibrary` |
| Related | [Where things are](where-things-are.md) |

`IsStageDataSwf` is true when the path under `data` starts with `stage/`. Convert to FLA then keeps the library under Animations. A stage clone export uses the same layout.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

A stage library has one Animations folder. A clip that is not on a labeled pose goes in `Animations/<name>`, and its sprites go in `Animations/<name>/Sprites`. The main clip stays in that Animations folder. Sounds stay in Audio.

| Function | What it does | Log line |
| --- | --- | --- |
| `IsStageDataSwf` | True when the path under `data` starts with `stage/`. Convert to FLA passes `stageLibrary` for that path. A stage clone passes `stageLibrary` on the FLA export. | No log. The check is the `stage/` prefix. |
| `BuildFlaOrganizeBlueprintFromEntries` | With `stageLibrary`, loose clips use `Animations/` and the main clip is placed there too. | `[JPEXS] stage library host=Host folder=Animations` |

## Files

| What | Path |
| --- | --- |
| `IsStageDataSwf`, `BuildFlaOrganizeBlueprintFromEntries` | `src/Services/JpexsCliService.cs` |
| Convert button | `src/Views/MakeModsWindow.xaml.cs` `BtnRunDecompile_Click` |
| Stage clone export | `src/Services/ShellStageCloneService.cs` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "BuildFlaOrganizeBlueprint_Stage"
```

`BuildFlaOrganizeBlueprint_Stage_KeepsClipsUnderAnimations` puts the leftover clip and the main clip under `Animations/`. The blueprint has no `Shared Animations` folder.

## References

1. [Where things are](where-things-are.md) — Convert to FLA writes under `expansions/decompiled`.
2. [Stage import](stage-import.md) — a stage clone exports this same FLA layout.
