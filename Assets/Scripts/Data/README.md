# Data

Authored content, and the answers to the questions Combat asks.

Every asset here exists to be edited by a designer without a recompile. None of it decides an
outcome: a `ClassAsset` holds a baseline, it does not know what a baseline is for. The rules that
use these numbers live in `Dragoneye.Combat`, which cannot see this assembly.

## The seam

Combat declares `IContentIndex` and never learns where the answers come from. `ContentCatalog`
implements it over ScriptableObjects. A test implements it over a list built in three lines. Both
are equally valid, and that is the point — the validator that runs on the host is the same code that
runs in the editor and in a test with no Unity present.

## Ids

Every asset carries a hand-assigned integer id. Ids cross the network and are written into saved
characters, so they are permanent once content ships. Zero is reserved on equipment to mean "nothing
equipped", which is why `OnValidate` refuses it.
