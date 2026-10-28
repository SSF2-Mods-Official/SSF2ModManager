---
title: My Jukebox
summary: The in-game song list. Change the match song or the menu song without leaving the match.
order: 10
---

My Jukebox is the music player built into SSF2 Shell. The song that plays in a match, and the song that plays on the menus, both go through it.

## How to open it

Any of these open the same screen:

- Options, then Sounds
- The button on the stage select screen
- **F3**
- Taunt while the game is paused
- Taunt and Grab together on a menu

**Manage Mods** also has **Open my jukebox**, which launches the game straight into that screen.

## What you can change

The list is every song in this build, including songs you added. Pick one for the match, or one for the menu. Clearing the override puts the stage's own song, or the menu's own song, back.

**Ctrl+Shift+M** mutes the jukebox.

A song change from a menu replaces whatever the jukebox was playing. If you pick a song from the Mod Manager while this screen is closed, the game still switches and shows the song banner.

The stage's camera background is separate. Changing the background does not change the song, and changing the song does not change the background. See [Camera background](camera-background.md).

## Add a song

**Make Mods**, then **Music**, then **Upload songs**. This puts an MP3 into the build so My Jukebox and the stage playlists can use it.

1. **Browse** and pick an MP3. Other audio types are not accepted on this form.
2. **Track id** is the song's id inside the game. Type something short with no spaces, like `choir`. The manager stores it as `bgm_choir`. If you already type the `bgm_` prefix, it is kept. Two songs cannot share an id.
3. **Song name** is the title people see in the list. If you leave it blank, the track id is the title.
4. **Artist** is the credit line. It can be blank.
5. **Upload**.

Upload copies a stock music file, swaps in your MP3, and resamples the audio to 44.1 kHz so Flash can play it. The new file lands in the game's sound folder as `bgm_` plus your id, with a `.swf` extension. The manager also registers that id, so the game can find the song. You do not edit that list by hand.

When it finishes, the box says the song was uploaded. It is now in every song dropdown in Make Mods, and in My Jukebox the next time the game loads the music list.

If upload says the MP3 is missing, Browse again. If it says there is no template music file, this Shell is missing the stock `bgm_` songs under its sound folder, and Setup needs to rebuild the game.

## Put the song on a stage

Uploading does not change what a stage plays. The stage still has its own playlist until you add the new id.

**Manage stage playlists**:

1. Pick the stage.
2. Pick the song in the dropdown under the list.
3. **Add**. **Remove** drops the selected row.
4. **Save**.

Save writes that list into the stage file. The next match on that stage can pick the new song, and My Jukebox shows it with the rest of that stage's tracks. **Play here** previews the list in the manager's mini player. It does not save.

If Save says the stage file was not found, the list was stored in the manager only. The game playlist was left as it was. Point **Settings** at the Shell folder that actually has that stage, then save again.

## Series and custom playlists

**Manage series playlists** and **Manage custom playlists** are groups you build. They do not replace a stage's own list.

- **New** makes a playlist and saves it immediately. The name starts as "Playlist" plus a number.
- Pick a song and **Add**. **Remove** takes the selected song off. **Delete** removes the whole playlist.
- **Play here** sends that list to the mini player.

Series and custom are the same controls. Use series when the songs belong to one game or franchise. Use custom for anything else.

## Hear it

**Open in game My Jukebox** on the Music screen launches the game into the jukebox. The new song is in the list with the stock tracks.

**Miniplayer** on that same screen plays one song, or one of your playlists, through the manager's player without launching the game.

Song packs other people made show up under Sounds in **Manage Mods**. Turn the pack on and its tracks join this same list.
