# Portraits

Drop images in here and run **ClaudeCode → Set Up Everything**. That is the whole workflow.

## Where they go

One folder per species, named exactly as the species is named in the game:

    Assets/Art/Portraits/Human/
    Assets/Art/Portraits/Beast/
    Assets/Art/Portraits/Giantkin/
    Assets/Art/Portraits/Goblinoid/
    Assets/Art/Portraits/Any/       <- offered to every species

A folder whose name matches no species is treated the same as `Any`, as are loose files in this
folder. So a set of shared faces does not have to be copied into every species folder.

`.png`, `.jpg` and `.jpeg` are picked up. The setup step also fixes the import settings -- a file
dropped into a Unity project is a texture until somebody says otherwise, and a texture cannot be
drawn as a portrait.

## What the game does with them

Square images work best. They are drawn cropped to a circle on the board token and cropped to fill
a rectangle on the character sheet, so keep the face away from the corners. 256x256 is plenty.

## Why the file name matters

A portrait's id is derived from its species and its file name, so it is the same on every machine
without anybody maintaining a list of numbers. That is what lets a character carry its face across
the network as a single integer instead of an image.

The trade: **renaming a file changes its id**, and characters wearing that portrait fall back to
their initial. Adding, removing and reorganising are all safe -- only renaming is not.

## NPCs

Premade creatures pick their portrait directly, on the creature asset's `Portrait` field, from any
sprite in the project. They are not restricted to this folder, though it is the obvious place to
keep them.
