---
title: Configure
summary: Pick the starting menu, the match rules, and who is playing.
order: 20
---

**Configure** is where you decide what the game opens into. **Launch** writes those choices and starts SSF2 Shell. **Save & Close** keeps them without starting. **Remember** uses this setup the next time you hit Play.

**Keep open** leaves this window up after launch. **Fullscreen** starts the game in fullscreen.

While the game is already running, **Apply to game** pushes the current match into that session, and **Goto menu** jumps to the menu you picked.

## Starting menu

The first dropdown is the screen the game opens on.

| Menu | What it opens |
| --- | --- |
| Home | The normal main menu. The boot intro plays first unless you skip it. |
| Group | Group menu. |
| Online | Online menu. |
| Data | Data menu. |
| Options | Options. |
| Controls | Controls. |
| Vault | Vault. You then pick Play intro or Palette Maker. |
| Solo | Solo menu. |
| Events | The event list, or one event if you pick it below. |
| Versus | Versus character select. |
| Training | Training character select. |
| Classic | Classic. |
| All-Star | All-Star. |
| Break the Targets | Break the Targets. |
| Home-Run Contest | Home-Run Contest. |
| Multi-Man | Multi-Man. |
| Crystal Smash | Crystal Smash. |
| Race to the Finish | Race to the Finish. |
| Arena | Arena. |
| Build Info | A check of this Shell build. **Change build** points at a Shell folder. **Check again** re-reads it. A file that fails the expansion check should not be run. |

**Load match immediately** shows up on the match menus. When it is on, the game skips the character select screen and starts the match you set below.

## Boot intro

This only matters when the starting menu is Home.

| Choice | What plays |
| --- | --- |
| Default | The normal intro. |
| Skip | No intro. |
| Intro 1, 2, 3 | That stock intro. |
| Random | One intro from the random pool. Check the intros you want in the pool. |

Custom intros you add in **Make Mods** show in the list under this. They are stored with the game and can be picked here.

## Vault

When the starting menu is Vault:

- **Play intro** picks Vault intro 1, 2, or 3.
- **Palette Maker** opens the recolor studio. **Palette character** is who is on screen when it opens.

## Event

When the starting menu is Events, pick the event.

Some events let you choose a fighter in the game. The window says so. The others have **Override character**. Turn that on and pick a character if you want to replace the fighter the event scripted.

## Match

These fields show for Versus, Training, and the other match modes.

- **Stage.** The stage the match uses.
- **Mode.** The ruleset for that mode.
- **Difficulty.** Easy, Normal, Hard, or Insane. Used where the mode has a CPU difficulty.
- **Hazards.** Stage hazards on or off.
- **Time** and the minute box. Turn time on and set the limit.
- **Stock** and the lives box. Turn stocks on and set how many.
- **Score limit.** Used by modes that end on score.

**Special Smash** can stack. Each box is its own modifier: Mini, Mega, Slow, Lightning, Vampire, Vengeance, Freeze, Egg, Dramatic, Turbo, Invisible, Metal, Light, Heavy, SSF1.

Home-Run Contest can **Override sandbag character** and pick who the sandbag is.

## Players

Four rows, P1 through P4. P3 and P4 can be set to **none** so that slot is empty.

- **Character.** Who that slot plays.
- **Human.** A person is holding that controller. Off means CPU.
- **CPU.** The CPU level when Human is off.
- **Team.** Team number for team modes.
- **Tag.** The nametag for that slot. **(none)** clears it. **Add new…** creates one. **Manage tags…** deletes tags. Tags are the same list the game uses on the character select screen.

## Music

When the game is open, this window can also change what is playing.

- **Current in-game music** is the match song. **Set to default** clears the override.
- **Current menu music** is the menu song. **Set to default** restores the menu's own song. **Manage playlist** edits the menu playlist.
