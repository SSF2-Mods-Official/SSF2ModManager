---
title: Stage background
summary: A gif, png, or mp4 used as a stage background, with a default stage and no stage SWF copy.
order: 30
group: Stages
---

# Stage background

## Info

| Stage background |
| --- |
| Pack | `expansions/stage_backgrounds/<id>/` |
| Make Mods | `StageBackgroundStore.Create` |
| Slot | [Where things are](where-things-are.md) — stage select reads `mappings.stage` |
| Match flag | `stageBackgroundFile` |
| Related | [Where things are](where-things-are.md) |

A stage background is the uploaded file plus the stage that loads when its slot is picked. The stage SWF is not copied.

## Contents

1. [How to use it](#how-to-use-it)
2. [How it works](#how-it-works)
3. [Files](#files)
4. [Check](#check)
5. [References](#references)

## How to use it

Make Mods → Stage → **Create a stage background**. Pick a png, gif, or mp4, name the slot, and pick the default stage. The form shows a preview. The game adds a stage-select icon.

On the stage select screen, **Upload Stage BG** picks the file, asks for a name, then the next stage click is the default stage. From Palette Maker that click sets `ctx.consume`, and `onStudioStageSelect` leaves stage select up so the upload can finish. That writes the same folder. **Space BG** on that same bar locks the menu space art (`menu_bg_space`) as the match background. The session flag is `menu_space`.

Picking the new slot starts the default stage and draws the file behind the match. Shield, or **Set Stage BG**, on that slot locks the file as the background for the next stage you pick.

## How it works

| Function | What it does | Log line |
| --- | --- | --- |
| `StageBackgroundStore.Create` | Writes the folder, the asset, `info.json`, and `stagepreview.png`. No SWF, and `menu_stages` is not opened. | — |
| `StageBackgroundIcons.sync` | Places each pack on the stage grid from that folder. | — |
| `StageBackgroundIcons.decorate` | Centers `stagepreview.png` on the slot. `ensureSelectable` puts the default stage's file name on the pack id so `StageButton.button_CLICK` reaches `STAGE_SELECT`. | `[stage_background] resource id=… file=…` |
| `StageBackgroundPick.resolve` | Default stage id and asset path for a slot, or null. | — |
| `StageBackgroundCatalog.create` | In-game upload writes the same folder. The name typed after the file pick is `info.json` `name`. | — |
| `StageBackgroundIcons.onClick` | A normal click starts `defaultStage` with the file. Set Stage BG locks the file instead. | `[stage_background] click id=… row=…` then `slot=… default=… asset=…` |
| `CameraBackgroundMod.useMenuSpace` | **Space BG** sets the flag to `menu_space`. `onQueueResources` reads the host bg from `musicOverride` (`track\|bg=stageId`) before the local flag. Space paints `menu_bg_space` and hides the stage art only after that clip is on screen. | `[camera_background] space bg`, `[camera_background] net bg from=musicOverride id=` |
| `StageBackgroundIcons.startMatch` | Sets `LevelData.stage` to the pack's default stage. `StageBackgroundPlayer.remember` stores the file on the session flag. | `[stage_background] start stage=` |
| `StageBackgroundPlayer.fit` | Sizes the file to the camera background and sets `x` and `y` to half that size, because the background sprite's origin is the middle of the screen. | `[stage_background] play x=… y=…` |

`info.json` has `name`, `id`, `type` = `StageBackground`, `defaultStage`, `asset`, and `enabled`. A png is the preview. A gif uses its first frame. A video uses a thumbnail when the shell can read one.

Hovering the slot draws `stagepreview.png` centered in the stage preview, the same place a stage preview sits. The slot icon is centered the same way. A normal pick loads `defaultStage` (collisions, music, hazards) with the file already on. Set Stage BG, or a shield pick, stores the file and leaves the next stage as the match.

## Files

| File | What it is |
| --- | --- |
| `src/Services/StageBackgroundStore.cs` | Writes the pack |
| `SSF2 Shell/src/mods/menu_editor/src/StageBackgroundIcons.as` | Stage-select slot from the pack |
| `src/Views/MakeModsWindow.xaml` | Create a stage background |
| `SSF2 Shell/src/mods/_shared/src/ssf2mm/shared/StageBackgroundPick.as` | `resolve` |
| `SSF2 Shell/src/mods/_shared/src/ssf2mm/shared/StageBackgroundCatalog.as` | Read and write packs |
| `SSF2 Shell/src/mods/menu_editor/src/StageBackgroundPlayer.as` | png, gif, and mp4 behind the match |
| `SSF2 Shell/src/mods/menu_editor/src/StageEditBar.as` | Upload Stage BG |

## Check

`StageBackgroundStore.Create` from a png and a gif writes `defaultStage`, `asset`, and `stagepreview.png`, and does not write a `.swf`.

`StageBackgroundPick.resolve` returns the default stage and the asset for a known id, and null for an unknown id.


## References

1. [Where things are](where-things-are.md) — stage select, mappings, and the docs folder.
