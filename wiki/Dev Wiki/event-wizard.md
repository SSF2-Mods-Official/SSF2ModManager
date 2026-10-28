---
title: Event wizard
summary: Make an event by copying a stock or expansion script. The form edits stage, music, match toggles, and fighter rows.
order: 52
---

# Event wizard

## Info

| Event wizard |
| --- |
| Form | `PanelNewEvent` |
| Clone | `CmbEventSource_SelectionChanged` |
| Stage background | Pack list `stageBackground`, then session flag `backgroundStage` |
| Menu portrait | Pack list `previewCharacter` and `previewCostume` |
| Creator | Settings `EventCreator` |
| Preview | `previews/<id>.png` |
| Test | `BtnCompileTest_Click` |

Make a new event copies a stock or expansion event. The name box stays empty. Compile an event stays on the compile screen.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How to use it

Open Make Mods, Events, Make a new event. The form is Clone from, Name, Description, a preview picture, Stage, Stage background, Music, Stocks, Minutes, Hazards, Team damage, up to four fighter rows, and Player chooses a character. Type, record, how you win, and items are not on this form.

Clone from lists the stock events, then any expansion script named `EventMode_*.as`. The name box stays empty. The description box shows that event's menu text from `event_mode.swf`, or `info.json` `description` for an expansion pack. Name and description are both required. A name already used by a stock or expansion event is refused. Picking a clone copies that script. Create keeps the copy and changes only the fields above. Create compiles and stays in the manager. Create and test launches that event.

Stage background lists vanilla stages, expansion stages, and background packs. The match stage stays the stage above it. A stage id is `stageBackground` on the pack list and in `info.json`, and that id becomes the camera background (`backgroundStage`). Stage picture writes no `stageBackground`. A background pack is stored on `info.json` as `pack:` plus the pack id and is left off the pack list. Music uses the same names as the launcher mini player. Preview calls `PlayInManager`. `LauncherMusicUi.PreviewQueue` plays that one track inside the song list already on the mini player. A chosen song is written as `levelData.musicOverride` on the line after the stage. A clone that had no music line still gets one.

Player chooses a character is a checkbox. Picking a clone does not change it. Create writes that checkbox as `chooseCharacter` on the pack list. `info.json` from Create does not include it. When the checkbox is off and fighter 1 names a character, the pack list sets `previewCharacter` and `previewCostume` from that row. `-1` is the base costume. Hover stops the previewer on `TroubledKing`, where `player_info_loader` exists, clears that loader, and adds `paLoader.getPortrait(id, "event")`. The loader stays at depth 5. Shape 147 at depth 4 has clip depth 7, so that mask still clips the portrait to the banner. A fighter of `(player)`, or Player chooses a character, stops the previewer on `BattleRevolution` and draws this event's preview picture on top of that frame. A locked fighter with no `previewCharacter` also stops on that frame. The PSB pack list names bowser, donkeykong, pichu, lloyd, and mario for the events that already lock them. Melee King, Meteo Campaign 9B, Nintendo All-Stars, and The Devil Within still choose a character. `info.json` `chooseCharacter` and `previewCharacter` win over the SWF list when those keys are present. Creator is the Creator name in Settings.

The form shows a preview image. Change preview replaces it. Create writes `previews/<id>.png`, using that file or a placeholder.

Make a new event copies a stock or expansion event and keeps that script. The form edits the stage, music, stocks, minutes, hazards, team damage, the Player chooses a character checkbox, and each fighter's name, costume, human, level, and stocks. Type, record, how you win, and items are not on the form. Heavier changes belong in the script.

A fighter row is the character, costume, human, level, and stocks. An empty costume is `-1`, the base costume. `FillEventPicks` fills the match stage, stage background, and music. Fighter rows have no stage box.

Create writes and compiles the pack and stays in the manager. Create and test does that and launches the game into that event, with `EventId` set. Compile an event compiles the pack you picked. Compile and test compiles it and starts that event. The preview box shows the named placeholder that is saved when you do not pick a picture.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `CmbEventSource_SelectionChanged` | Shows `EventLoadOverlay`, reads the cached script or exports it, then fills the fields. | `Reading` plus the class name |
| `ExportVanillaSourceAsync` | Copies `AppData/event-scripts/<swf stamp>/<class>.as` when that file exists. Otherwise exports that class from `event_mode.swf` and stores the copy. | |
| `ApplyEventRecipe` | Writes the parsed event into the form and leaves the name box alone. | |
| `EventMenuText.Description` | The stock blurb is the string after that event's title in `event_mode.swf`. An expansion pack uses `info.json` `description`. | |
| `BtnEventMusicPreview_Click` | Sends the chosen song to `PlayInManager`. `PreviewQueue` keeps the mini player's song list and plays that track in it. | |
| `FillEventPicks` | Fills the match stage, stage background, and music. Fighter rows have no stage box. | |
| `EventNames.IsTaken` | True when the trimmed name matches an existing event name or id. | |
| `EventPreview.Write` | Writes `previews/<id>.png` from the chosen file, or a placeholder png. | |
| `EventObject` | Writes the pack list. `stageBackground` for a stage id. `chooseCharacter` from the checkbox. `previewCharacter` and `previewCostume` from fighter 1 when that checkbox is off. Creator is Settings `EventCreator`. | |
| `CreateEvent` | Writes `info.json` with `id`, `name`, `creator`, `description`, `type` `Event`, `enabled` true, and `stageBackground` when one is chosen. `chooseCharacter` stays on the pack list. | `Created` plus the folder |
| `EventPack_PSBEvents.buildEventList` | Names the locked fighter. `koopaKing` is bowser, both jungle events are donkeykong, `pichuJV5` is pichu, both swordsmen events are lloyd, `troubleKing` is mario. The other four still choose a character. | |
| `showPreviewCharacter` | Stops on `TroubledKing`. `takeEventPortrait` calls `paLoader.getPortrait(id, "event")`. `placeEventPortrait` clears `player_info_loader` and adds that clip. The loader stays at depth 5, under the depth-4 mask. | `[event_expansions] preview char=` |
| `showSelectCharacterFrame` | Stops the previewer on `BattleRevolution`, then `placePreviewBitmap` draws this event's preview picture on top. | `[event_expansions] preview frame=BattleRevolution` |
| `WriteSource` | Kept for tests. Make a new event uses `EventCloneEdit.Apply`. | |
| `applyPackInfo` | Copies `info.json` `stageBackground` when the SWF left it off. `chooseCharacter` and `previewCharacter` in that file win over the SWF list. | `[event_expansions] stageBackground id=` |
| `applyStageBackground` | Sets session flag `backgroundStage` before the match and before character select. | `[event_expansions] backgroundStage id=` |
| `startEventById` | Create and test switches to the expansions list and starts that id. | `[event_expansions] startEventById id=` |
| `xpStartEventMatch` | Builds the match after `applyStageBackground`. | |
| `EventRankText` | Not on this form. Rank cuts stay in the cloned script. | |
| `EventItemList.Split` | Not on this form. Item rules stay in the cloned script. | |
| `BtnCompileTest_Click` | Compiles the pack on the compile screen, then `Launch` with `StartingMenu` `events` and `EventId`. | `Starting event` |
| `EventAuthorService.FindApi` | Compiles with Next2Flash `mxmlc`. The API is the `SSF2CustomMatch.as` folder exported from the shell's `data/modes/event_mode.swf`. Next2Flash does not ship those scripts. | — |
| `CreateEventAsync` | Requires a new name and a description, then compiles. Create stays here. Create and test calls `Launch` with `StartingMenu` `events` and `EventId`. | `Starting event` plus the id |
| `WriteLaunch` | Writes `expansions/state/launch.json` with `screen` and `eventId`. | |
| `readLaunch` | On boot, reads `expansions/state/launch.json` and starts that screen. | `onBoot screen=` |
| `onMatchRetry` | Match retry disposes the camera overlay and leaves `backgroundStage` set. `onMatchEnd` does not clear it, because `endMatch` runs that hook on a retry. The events menu clears it. | `[camera_background] match retry keep id=` |
| `EventCloneEdit.Apply` | Copies the cloned script and replaces the stage, music, match toggles, and fighter rows the form shows. A new song is `levelData.musicOverride` after the stage line. | A Mother Brain clone still contains `getDefinitionByName("MotherBrain")` |
| `ReadFighter` | An empty costume box is `-1`, the base costume. | |

## Files

| What | Path |
| --- | --- |
| Form | `src/Views/MakeModsWindow.xaml` |
| Creator name | `src/Views/SettingsWindow.xaml` |
| Fill and create | `src/Views/MakeModsWindow.xaml.cs` |
| Clone edit | `src/Services/EventCloneEdit.cs` |
| Menu text | `src/Services/EventMenuText.cs` |
| Pack | `src/Services/EventAuthorService.cs` |
| Preview file | `src/Services/EventPreview.cs` |
| Event start | `ExpansionEventMenu.xpStartEventMatch` |

## Check

`EventCloneEditTests` checks that changing a fighter and the stage leaves the cloned boss call in the script, and that a song is written when the clone has no music line. `EventMenuTextTests` checks the menu blurb, including a curly apostrophe, and an expansion `info.json` description. Change Clone from and the name box stays empty. The description box shows that event's text. Preview plays on the mini player. Compile an event stays on that screen. Set Creator name in Settings, make an event, and the pack list `creator` is that name. `EventFormTextTests` checks that `EventPreview.Write` writes a png and that the pack list carries `stageBackground`, `previewCharacter`, and `previewCostume`. Its record and item tests cover helpers this form does not call.


## References

1. [Mod dependencies](mod-dependencies.md) — GameBanana mods chosen while packaging, not on this form.
2. [Events](events.md) — event packs in the game.
3. [Music player](music-player.md) — the names Preview uses.
