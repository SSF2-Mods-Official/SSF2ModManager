---
title: Events
summary: Make a new event copies a stock or expansion script. The form edits the stage, music, match toggles, and fighter rows, then compiles the pack.
order: 10
group: Events
---

# Events

## Info

| Events |
| --- |
| Clone list | `EventAuthorService.ListSources` |
| Vanilla classes | `EventAuthorService.ListVanillaEvents` |
| Menu names | `ShellConfigureLaunchService.ListEvents` — stock names, then enabled expansion packs |
| File | `data/modes/event_mode.swf` |
| Related | [Where things are](where-things-are.md) |

Make a new event copies a stock or expansion script. The form is [Event wizard](event-wizard.md). Vanilla classes live in `event_mode.swf`. Their names look like `EventMode13_ComboMaker`. The clone list shows the menu name for that class.

## Contents

1. [How it works](#how-it-works)
2. [Files](#files)
3. [Check](#check)
4. [References](#references)

## How it works

`BtnNewEvent_Click` opens Make a new event and fills Clone from with `ListSources`. That list is every vanilla event, then the template and any `EventMode_*.as` pack under `expansions/events`. The name box stays empty.

Picking a clone runs `CmbEventSource_SelectionChanged`. A stock class is read by `ExportVanillaSourceAsync`. A pack is the `EventMode_*.as` file. `EventRecipe.Parse` reads the stage, music, stocks, minutes, hazards, team damage, and fighters. `ApplyEventRecipe` puts those on the form. `EventMenuText.Description` fills the description. The form does not show type, record, how you win, or items.

`CreateEventAsync` requires a name and a description. `EventCloneEdit.Apply` copies the script and replaces the fields the form shows. `CreateEvent` renames the class, strips `eventinfo`, and writes the pack list. `EventRecipe.WriteSource` is not called. Create stays in the manager. Create and test launches with `StartingMenu` `events` and `EventId`.

`ListVanillaEvents` reads printable strings out of `data/modes/event_mode.swf`. A class is a string `EventMode<number>_<id>` or `EventMode<number>Special_<id>`. `ListEvents` is the Configure events menu. It lists those stock menu names, then each enabled pack under `expansions/events`. The pack id and name come from `info.json`. `chooseCharacter` true hides the character override. `EventMode` and `EventMode_AllStarBaseMatch` are not events.

| Function | What it does | What you see |
| --- | --- | --- |
| `ListSources` | Vanilla events first, then the template and pack sources. | Clone from lists Troubled King through the celebration. |
| `RemoveMod` | Asks "Are you sure" on the visible window before it deletes any row except Essentials. | Are you sure you want to remove |
| `ListVanillaEvents` | One row per `EventMode<number>_<id>` class. Label is the menu name. `ClassName` is the class. | Combo Maker is `EventMode13_ComboMaker`. |
| `ListEvents` | Stock menu names from `event_mode.swf`, then each enabled pack in `expansions/events`. Id and name come from `info.json`. A pack with `enabled` false is left out. | Configure can select Bob-omb Destination. |
| `IdentityFromName` | Splits the name on anything that is not a letter or digit, capitalizes each piece, and joins them. A name that starts with a digit gets an `E` in front. The class is `EventMode_` plus that id. The pack folder is the id. | `Rise of King Koopa!` is id `RiseOfKingKoopa`, class `EventMode_RiseOfKingKoopa`, folder `RiseOfKingKoopa`. A second one is `RiseOfKingKoopa2`. |
| `CreateEvent` | With a blank id, calls `IdentityFromName`. It renames `class EventMode<number>_<id>` to the new class and writes it under `expansions/events/<folder>`. | The form does not ask for an id, a class, a pack folder, or a creator. |
| `CreateFromVanillaAsync` | The form does not call it. A stock clone uses `ExportVanillaSourceAsync`, then `CreateEvent` with the patched script. | |
| `EventRecipe.Parse` | Reads the stage, music, stocks, minutes, hazards, team damage, and fighters. The form does not show type, items, goal, or rank records. | The stage and fighter rows match the clone. |
| `EventRecipe.WriteSource` | Kept for tests. Make a new event does not call it. `EventCloneEdit.Apply` patches the cloned script. | A cloned boss call stays in the class. |
| `CreateEvent` then `CompileAsync` | `CreateEvent` copies the patched script, strips `eventinfo`, and writes the pack list. `CompileAsync` builds the swf. The cloned win condition stays in the script. | Create ends on the swf path. |

## Files

| What | Path |
| --- | --- |
| `ListSources`, `ListVanillaEvents`, `IdentityFromName`, `ExportVanillaSourceAsync`, `CreateEvent` | `src/Services/EventAuthorService.cs` |
| `EventRecipe.Parse`, `EventRecipe.WriteSource` | `src/Services/EventRecipe.cs` |
| `ListEvents` | `src/Services/ShellConfigureLaunchService.cs` |
| Clone from box | `src/Views/MakeModsWindow.xaml` `CmbEventSource` |
| Fill the box | `src/Views/MakeModsWindow.xaml.cs` `BtnNewEvent_Click` |
| Name and description | `src/Views/MakeModsWindow.xaml` `PanelNewEvent` |
| Create button | `src/Views/MakeModsWindow.xaml.cs` `BtnCreateEvent_Click` |

## Check

```
dotnet test src/tests/SSF2ModManager.Tests/SSF2ModManager.Tests.csproj --filter "ListSources_ListsVanillaEvents|IdentityFromName|EveryStockEvent_RemakesWithRecords|ListEvents_IncludesEnabledExpansionPacks"
```

`ListSources_ListsVanillaEvents` builds a small `event_mode.swf` with Combo Maker and an All-Star battle. The clone list contains both menu names and a pack `EventMode_*.as`. It does not contain `EventMode_AllStarBaseMatch`. `IdentityFromName_UsesTheName` says `Rise of King Koopa!` is `RiseOfKingKoopa`, and a taken folder becomes `RiseOfKingKoopa2`. `CreateEvent_RenamesTheVanillaClass` writes `EventMode_RiseOfKingKoopa`. `EveryStockEvent_RemakesWithRecords` parses every numbered event and every all-star special. `WriteSource` is covered by tests and is not what Make a new event calls. `ListEvents_IncludesEnabledExpansionPacks` puts Bob-omb Destination on the Configure list and leaves a disabled pack off it.


## References

1. [Event wizard](event-wizard.md) — the Make a new event form.
2. [Where things are](where-things-are.md) — compile paths and the shell folder.
