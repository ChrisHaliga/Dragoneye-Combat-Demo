# Element runes

One file per element, named after it. Drop it in and you are done -- `ArtImporter` notices the
folder changed and rebuilds `ElementIcons.asset` from it.

    Geo.png  Hydro.png  Pyro.png  Aero.png  Lux.png  Nyx.png  Arcana.png

`.png`, `.jpg` and `.jpeg` are all picked up. The name is the whole mapping: the element enum is
permanent, so unlike a portrait there is no id to derive and nothing to orphan by renaming. A file
whose name matches no element is simply not used.

## What they replace

Every element used to be a plain coloured disc. Seven colours are seven things a player has to keep
straight from a legend; seven shapes are seven things they recognise by the second match. Colour
still does its half of the work, because the runes are coloured art -- but shape is what survives
being drawn at eighteen pixels next to six others.

An element with no file falls back to that coloured disc, tinted from `ElementPalette`. So missing
art looks like the old UI rather than a hole, and a project that has never built the library still
runs.

`ElementPalette` also stays the answer for **text**. A skill's cost is written in its element's
colour, and a rune cannot colour a word.

## What the art should be

Square, transparent, and cropped to the rune. These are drawn between 18 and 26 pixels across, so
anything with a wide transparent margin arrives looking like a speck in a box -- the source art
these came from was a 1920x2688 page with the glyph floating in the middle of it, and it had to be
cropped to its ink before it read at all.

256x256 is plenty.
