---
title: Add a stage
summary: Clone a stage, import a stage file, or add a background slot.
order: 42
---

**Make Mods**, then **Stage**. A new stage is a clone, an imported file, a background slot, or an asset swap. Those are four different things.

## Clone a stage

**Clone a stage** copies a stage you already have.

- **Stage to clone.** The stage the copy starts from.
- **Display name.** The name on the select screen, and the folder name. Spaces are kept.
- **Stage name (linkages).** The id inside the files. Same rule as characters: letters, numbers, and underscores. The display name is turned into that id. A red line means the id is taken or empty.
- **Keep using clone's assets.** On, the copy still uses the original stage's pictures. Off, it gets its own files.
- **Do not convert to FLA.** On, no editable FLA is written. Leave it off if you want to edit the stage afterward.
- **Clone stage.** Builds the pack and registers the stage.

## Import a stage

**Import a stage** is for a `.swf` or `.ssf` you already have. **Browse**, then **Import**. More than one file can sit in the box. **Do not convert to FLA** skips the editable copy.

A stage that arrived as `.ssfe` or a zip is installed from **Download Mods**, or from **Package mods** → **Install a mod archive**. This import form is for the stage file itself.

## Put it on the select screen

The pack is under Stages in **Manage Mods**. Leave it enabled.

**Stage select** on the Stage screen opens the grid editor. Drag the new stage onto a cell. It is not placed for you. See [Stage select editor](../SSF2 Shell Features/stage-select-editor.md).

## The other two buttons

**Create a stage background** does not copy a stage. You give it a png, gif, or mp4, a name, and a default stage. The slot plays that file behind the stage you picked. See [Stage backgrounds](../SSF2 Shell Features/stage-backgrounds.md).

**Stage asset swap** exports one stage's pictures into a folder. You edit the pngs, and that slot loads the original stage with your pictures instead. **Open folder** is where the pngs are. The stage SWF is not copied.
