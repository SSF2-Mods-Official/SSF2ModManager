---
title: Add an event
summary: Clone an event, set the match, and compile it so it shows in the event list.
order: 43
---

**Make Mods**, then **Events**, then **Make a new event**. An event is a match the game scripts for you. You start from one that already exists and change the parts this form exposes.

## Start

**Clone from** is the event you copy. The rest of that event's script stays. This form replaces the match setup and the fighter rows.

- **Name.** The name on the event menu.
- **Description.** The text under that name.
- **Preview.** **Change preview** picks a png for the event's picture.

The creator name in **Settings** is written onto the event.

## Match

- **Stage.** The stage the match loads.
- **Stage background.** A camera background drawn behind that stage. **Stage picture** keeps the match stage's own picture. This is the same split as [Camera background](../SSF2 Shell Features/camera-background.md).
- **Music.** The song for the match. **Preview** plays it in the manager.
- **Stocks** and the lives box. **Minutes** and the time box. **Hazards**. **Team damage**.

## Fighters

Each row is one slot: the fighter copied from the source event, a costume, whether a human is playing, the CPU level, and that slot's stocks.

**Player chooses a character** lets the person playing pick a fighter in the game instead of using the scripted one. Configure shows the same choice when you launch that event.

## Create and compile

**Create** writes the event pack. **Create and test** writes it and launches the game into that event.

After it exists, the buttons change to **Save and compile** and **Compile and test**. Compile builds the pack the game loads. Test launches it.

**Compile an event** on the Events screen is the same build for a pack you already made. Pick the pack, then **Compile** or **Compile and test**.

The new event shows in the in-game Events list and in **Configure** when the starting menu is Events. It is also under Events in **Manage Mods**. If it is missing in the game, the pack is off, or it was created and never compiled.
