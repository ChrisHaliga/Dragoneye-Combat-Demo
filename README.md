# Dragoneye Combat Demo

A hex-grid tactics game for Unity 6, playable solo or over the internet. Build a character, bring it
to an arena, and fight turn by turn against other players' characters and the computer's.

This document is the map. It covers how to get the project running, how it is put together and why,
and what to do when you want to change a particular part of it. The folder READMEs go deeper on
their own subjects:

| Read this | For |
|---|---|
| [`Assets/Scripts/Combat/README.md`](Assets/Scripts/Combat/README.md) | The rules layer and why it holds no engine types |
| [`Assets/Scripts/Data/README.md`](Assets/Scripts/Data/README.md) | Authored content and the seam it sits behind |
| [`Assets/Scripts/Hex/README.md`](Assets/Scripts/Hex/README.md) | Coordinates, layout, pathfinding, rendering |
| [`Assets/Scripts/Multiplayer/README.md`](Assets/Scripts/Multiplayer/README.md) | Sessions, Relay, scenes, the match lifecycle |
| [`Assets/Art/Portraits/README.md`](Assets/Art/Portraits/README.md) | Adding faces |
| [`Assets/Art/Elements/README.md`](Assets/Art/Elements/README.md) | The element runes |

---

## Getting it running

**Unity 6000.5.10f1.** Other 6.x versions will probably open it, but the editor path baked into the
verification harness is that one.

1. Open the project.
2. Run **ClaudeCode → Set Up Everything**.
3. Press Play. You will already be on **Bootstrap**, which is where the menu expects to start from.

Step 2 is not optional on a fresh clone, and it is safe to repeat. It is idempotent — running it on
an already-configured project re-saves the same scenes and rewrites the same assets. When something
is missing and you cannot see why, run it.

### The scenes

Build order matters. Netcode resolves scenes by build index, so a missing or reordered one fails at
runtime with a message that points nowhere useful.

| Index | Scene | Holds |
|---|---|---|
| 0 | **Bootstrap** | `NetworkManager`, `SessionRunner`, `MatchFlow`. Nothing visible. Loads MainMenu. |
| 1 | **MainMenu** | The menu `UIDocument` and the draft board `UIDocument`. |
| 2 | **Arena** | Camera, light, ground, the hex map, the HUD. |

Playing from MainMenu or Arena directly *appears* to work and then fails at the first thing that
needs the persistent objects, because they only exist in Bootstrap. That is why Set Up Everything
leaves you on Bootstrap when it finishes.

---

## The shape of it

Nine assemblies. The boundaries are not documentation — they are separate DLLs, so a layering
mistake is a compile error rather than something a review has to catch.

```
                    Combat        ← the rules. References nothing. No engine types at all.
                      ↑
                    Data          ← authored ScriptableObjects. Answers the questions Combat asks.
                      ↑
   Hex ─→ Hex.Systems ─→ Hex.Rendering
                      ↑
   Settings        Camera
                      ↑
                 Multiplayer      ← sessions, menus, character creation
                      ↑
                    Game          ← the arena: units, turns, the director, the HUD
```

| Assembly | Folder | May reference |
|---|---|---|
| `Dragoneye.Combat` | `Scripts/Combat` | **nothing**, and not even UnityEngine |
| `Dragoneye.Settings` | `Scripts/Settings` | nothing |
| `Dragoneye.Data` | `Scripts/Data` | Combat |
| `Dragoneye.Hex` | `Scripts/Hex` | nothing but the engine |
| `Dragoneye.Hex.Systems` | `Scripts/Hex/Systems` | Hex |
| `Dragoneye.Hex.Rendering` | `Scripts/Hex/Rendering` | Hex |
| `Dragoneye.Camera` | `Scripts/Camera` | Settings, Input System, Cinemachine |
| `Dragoneye.Multiplayer` | `Scripts/Multiplayer` | Combat, Data, Settings, Hex, Hex.Systems, Netcode, UGS |
| `Dragoneye.Game` | `Scripts/Game` | all of the above |

`Dragoneye.Game` references `Dragoneye.Multiplayer` and not the other way round. That is what lets
the draft board host the session controls: the board is a Game thing, the session is a Multiplayer
thing, and the arrow points the way it does deliberately. If you find yourself wanting Multiplayer
to reach into Game, the design has gone wrong somewhere — put the shared thing lower instead.

`Assets/Editor` has no asmdef. It is the predefined `Assembly-CSharp-Editor`, which automatically
sees everything else.

---

## Design philosophy

These are not aspirations. Each one is here because breaking it cost a day.

### Rules are pure; everything else is plumbing

`Dragoneye.Combat` sets `noEngineReferences: true`. There is no `Vector3`, no `Mathf`, no
`MonoBehaviour` in it, and there cannot be. Every question with a right answer — what does this move
cost, who goes first, how much damage got through, how many levels does this experience buy — is
answered there, by a static function, with no clock and no network and no scene.

What that buys is not tidiness. It is that **the host and the client run the same code and cannot
disagree**. When the UI prices a move at 3 AP and the server charges 3 AP, it is because both called
`ActionResolver`, not because two implementations happen to match today.

It also means the whole rules layer is testable in a plain console app with no Unity present, which
is exactly what the verification harness does.

### One answer per question

`ActionResolver` prices the hover label *and* decides what the click does. `ArenaBoard` answers
"what does this route cost" for the player, the server and the AI alike. Two functions that answer
the same question is how a UI ends up promising a move the server refuses.

When you add a rule, find the existing answer before you write a new one.

### Guarantees belong at runtime; editor steps are housekeeping

An editor step that has to be run for the game to be correct is a step that will not have been run.
This bit twice — a world cursor that was supposed to be invisible, and board tokens that were
supposed to be discs — and both times the symptom pointed at the art rather than at the step nobody
ran.

So: if something *must* be true, make it true in `Awake`. `FocusPoint` disables its own renderers.
`UnitView` builds its own token. The editor steps author content and wire scenes; they do not hold
up guarantees.

### The content is authored, not compiled

Species, classes, equipment, skills and premade creatures are all ScriptableObjects under
`Assets/Settings`. Retuning a number is editing an asset, not a recompile. `ContentCatalog`
implements `IContentIndex`, which is what Combat actually talks to — a test implements the same
interface over a three-line list.

Every asset carries a **hand-assigned integer id**. Ids cross the network and get written into saved
characters, so they are permanent once content ships.

### One editor menu

`ClaudeCode → Set Up Everything` is the only `[MenuItem]` in the project, deliberately. The steps
live in separate files under `Assets/Editor` because each was written for one change, but none of
them is ever the right one to run alone. Six menu entries only ever raised the question of which
were stale.

**A setup step that has done its job gets deleted.** Its output is committed; re-running it after
the scenes have been hand-edited would overwrite that work.

### Comments say why

The code says what it does. A comment that repeats the code is noise. The comments here exist to
record the reasoning that is not recoverable from reading — why the tiebreak in the initiative sort
is load-bearing, why the transport listen address matters, why a field is cleared before a rebuild.

---

## How to change things

### Add or retune a skill

Skills are `SkillAsset` ScriptableObjects in `Assets/Settings/Characters`.

- **Duplicate an existing one** (`SkillStrike.asset` is the simplest) and give it a fresh `m_Id`.
- Fields: element, AP cost, element cost, range, target (`Creature` / `Self`), effect
  (`Damage` / `Heal` / …), amount, and `m_LevelRequired`.
- Add it to the species or class that should know it, and to `ContentCatalog`'s skill list.
- A skill is hidden from a character until its level requirement is met — see `Loadout`, which
  filters on `spec.LevelRequired <= level`.

`OnValidate` forces range to 0 on a self-targeted skill, because a self skill with reach is a
contradiction the UI would have to special-case.

### Add a species

`SpeciesDefinition` in `Assets/Settings/Creatures`. Id, display name, attribute baseline, base AP,
and the skills it knows. Add it to `ContentCatalog`.

Then make a portrait folder for it: `Assets/Art/Portraits/<Display Name>/`. The folder is matched by
display name.

### Add a premade creature

`CreatureDefinition` in `Assets/Settings/Creatures`. Beyond the obvious stats it carries:

- `m_Portrait` — any sprite in the project, not just the portrait folder.
- `m_StartingPool` — its elements at level 1.
- `m_LevelUpPicks` — the elements it takes as the host raises its level. The host can set NPC levels
  from the draft board, and this list is what gets spent.
- `m_Skills` — filtered by level requirement at resolve time, same as a player character.

Add it to `CreatureCatalog`. `CharacterContentSetup` authors the twelve that ship; if you are adding
by hand, that file is the reference for what a complete creature looks like.

### Add a portrait

Drop a `.png`, `.jpg` or `.jpeg` into `Assets/Art/Portraits/<Species>/`. That is the whole workflow —
an `AssetPostprocessor` notices the folder changed and rebuilds `PortraitLibrary.asset` from it.
Loose files, and folders matching no species, go to everybody.

A portrait's id is an FNV-1a hash of its species and file name, so it is the same on every machine
without anyone maintaining a column of numbers. The trade: **renaming a file changes its id** and
orphans the characters wearing it, which fall back to their initial. Adding, removing and moving are
all safe.

Images are cropped to their **centre square** at every point of use — the board token maps the centre
square onto the disc, and every UI class uses `-unity-background-scale-mode: scale-and-crop`. Source
files are never modified. Keep the face away from the corners.

### Add or replace an element rune

Drop a square, transparent `.png` named after the element into `Assets/Art/Elements/` — `Geo.png`,
`Hydro.png`, `Pyro.png`, `Aero.png`, `Lux.png`, `Nyx.png`, `Arcana.png`. `ArtImporter` picks it up
and rebuilds `ElementIcons.asset`; there is no id to derive and nothing to orphan, because the enum
is permanent and the file name is the whole mapping.

An element with no file falls back to the flat coloured gem it used to be drawn as, tinted from
`ElementPalette`. That is not defensive padding — it is what a project sees before the library has
been built, and it means missing art looks like missing art rather than a hole.

`ElementPalette` is still the answer for **text**: a skill's cost is written in its element's colour,
and a rune cannot colour a word.

### Change the AI

`BasicBrain` is a state machine behind `ICreatureBrain`:

```
Idle ──enemy in reach & affordable──> Striking
     ──enemy in reach, no AP────────> Recovering    (a self skill: catch a breath)
     ──enemy out of reach──────────> Closing        (move, but only strictly closer)
```

`Assess(actor, target)` is a pure static function over a `BrainView` — no scene, no netcode — so a
new brain is a new implementation and nothing else changes. `CombatDirector` executes what it
returns, and waits for `UnitView.IsMoving` to settle plus a dwell before acting again, so a computer
turn can be watched rather than resolving in one frame.

The `Closing` state only accepts a destination strictly closer than where it started. Without that
check the AI paced back and forth between equidistant tiles until its AP ran out.

### Change the combat maths

All in `Dragoneye.Combat`:

| Want to change | File |
|---|---|
| Damage after armour, death, reach | `CombatRules` |
| What a click costs and whether it is legal | `ActionResolver` |
| Initiative | `TurnOrder` |
| Levels and experience | `Progression` |
| What an element costs from the pool | `ElementPricing` |
| Derived stats, armour reduction, skill filtering | `Loadout` |
| Attribute point-buy | `PointBuy` |

Add a check to the harness when you change one. They are cheap and they are the only thing standing
between a rules change and a playtest.

### Change the UI

UI Toolkit. Markup in `Assets/UI/*.uxml`, styles in `Assets/UI/*.uss`, bound by plain C# classes in
`Scripts/Multiplayer` (menus) and `Scripts/Game/Combat` (the arena HUD).

`MainMenuUI` owns which panel is visible; `MenuScreen` is the enum of them. Each screen is a plain
class bound to one subtree, so adding one means adding an enum member — which forces you to decide
where Back goes — and a class.

Depth comes from nine-sliced PNGs generated by `UiArtSetup`, because USS has no gradients or
shadows. Referenced through `url()`; the harness checks those resolve.

**Read the flexbox note below before you touch a stylesheet.** It is the single most expensive bug
class in this project.

### Change the networking

`SessionRunner` wraps UGS sessions; `MatchFlow` owns the lifecycle. Nothing in the scene should call
`StartHost` or `StartClient` directly — `CreateSessionAsync` / `JoinSessionByCodeAsync` allocate
Relay, configure the transport and start netcode as one call.

Solo play takes the same path: `StartSoloMatch()` restores loopback transport settings, starts a
host on a probed-free port and loads the Arena. Netcode still runs. That is the point rather than a
compromise — a solo match then exercises the same spawning, ownership, draft and rules as a hosted
one, instead of being a second implementation free to drift.

There is **no host migration**. Host leaves, match over.

---

## The numbers currently in force

Not a design document — just what the code says today, so you know where to look when it needs to
say something else.

**Action points** are stored in half-units (`Ap.UnitsPerPoint = 2`). Movement costs one half-unit per
tile along a route; skills cost whole points. Integers cover both, and a replay cannot drift the way
floats would across platforms. Base AP is a species property, currently 4 for all four.

**Elements** — seven, three letters each: Geo, Hyd, Pyr, Æro, Lux, Nyx, Arc. They cost
1 / 1 / 1 / 1 / 2 / 2 / 3 from the pool budget, so the rarer the element the more of your depth one
of it takes. Pool budget equals level.

**Attributes** start at **0** and are bought with 27 points. A step costs the value it leaves, except
the first, which costs one — so reaching 5 costs 11 and reaching 3 costs 4. Seven of the 27 buy the
first point of each attribute, which is why the budget is 27 and not 20: it is exactly what it now
costs to stand where every character used to start, so no spread that fitted before stops fitting.
Dumping an attribute to zero is a real choice that pays for something. The budget and the ceiling are
authored on `ContentCatalog`, not compiled.

**Levels** — start at 1, cap at 20. A level costs `2^level` experience. Killing a creature is worth
its level. Multiple levels resolve in one pass (`Progression.Resolve`) so a character out of a long
fight is asked what it becomes once, not once per level.

**Armour** is flat damage reduction: none 0, light 1, medium 2, heavy 4, shield +3. Reduction cannot
heal — `DamageAfter` floors at zero. The floating combat text shows the arithmetic (`-2 HP (5 - 3
armour)`) so a player can see why a hit landed as softly as it did.

---

## Verifying a change

Unity's own compile is the ground truth, but it is slow and it will happily let you break a layering
rule that the *shipped* build would catch. There is a harness in the session scratchpad that closes
most of that gap in a few seconds:

```bash
bash scratchpad/build.sh
```

It does four things:

1. **Compiles each assembly separately against only the references its own asmdef declares.** The
   reference lists are read out of the asmdef files rather than duplicated in the script — a
   hand-kept copy drifted once and the script passed while Unity would have failed, which defeats
   the whole point. `Dragoneye.Combat` is compiled against a reference set with no UnityEngine in
   it, so `noEngineReferences` is enforced here too.
2. **Parses every UXML as XML.** A stray `--` inside a comment makes the markup unparseable, and the
   only symptom at runtime is a panel that does not exist. Compiling C# could never catch it.
3. **Checks every `url()` in the stylesheets points at a real file.**
4. **Runs `uifit.py`**, which adds up the character creator's column heights out of the stylesheet
   and compares them against the stage body at 1280×720.

There is also a .NET console harness (`scratchpad/harness/`) that compiles the pure Combat sources
and runs fourteen check suites over them — rules, progression, pool pricing, brain decisions, draft
queries, hex placement, camera maths.

**These scripts are not committed.** They live in the session scratchpad. If you want them in the
repo — and they probably should be — say so and they can move to a `Tools/` folder.

---

## Traps that have actually been sprung

Every one of these cost real time. They are here so they cost it once.

### `GetComponent<T>() ?? AddComponent<T>()` is broken

Unity overloads `==` so that a destroyed or missing component compares equal to null. The `??`
operator does not use that overload — it does a reference check — so a "fake null" wins and you get
a component reference that throws `MissingComponentException` the moment you touch it.

This caused two separate silent failures, months apart. Use the explicit form:

```csharp
static T Ensure<T>(GameObject target) where T : Component
{
    var existing = target.GetComponent<T>();
    return existing == null ? target.AddComponent<T>() : existing;
}
```

### Flex items shrink by default, and in a column that shrinks their *height*

Three separate rounds of "the text doesn't fit its box" had this one cause. A column asking for more
height than the stage body has does not clip and does not scroll. It shrinks every card inside it —
while the text inside those cards keeps its own font size, and draws straight out of the frame.

Set `flex-shrink: 0` on anything whose height is meant to be honoured. And note that **`flex-basis`
overrides `width` on the main axis**: `.col { flex-basis: 0 }` is what collapsed the creator's
columns on top of each other, despite each having an explicit width.

`uifit.py` exists so this cannot regress silently again.

### Rebuilding a container every frame destroys the click target

`SkillBarView.Update()` used to rebuild the bar unconditionally. The element under the cursor at
pointer-down no longer existed at pointer-up, so no click ever completed and the skills appeared to
do nothing at all.

Redraw only when something actually changed. The same shape of bug returned in the portrait picker,
where the click handler rebuilt the row it had just been clicked in.

### An editor step you forgot to run looks like a content bug

"No portraits are installed for this species" sent someone digging through their art folder when the
truth was that the library had never been built. Two lessons, both applied: **make the message name
the actual cause**, and where possible **remove the step** — the portrait library now rebuilds itself
from an `AssetPostprocessor`.

### `runInBackground` off is a shipping bug, not an editor artefact

An unfocused Unity window stops ticking, which stalls netcode for everyone in the match. This looks
like a local testing quirk and is not — a built client would do the same. `MatchFlow.Awake` sets
`Application.runInBackground = true`.

### Hard-coded ports

Solo play bound port 7777, which on Windows can sit inside a Hyper-V or WSL reserved range, and the
failure reads as a netcode error rather than an OS one. `FreeLoopbackPort()` probes for a free one
instead.

---

## Known limitations

Flagged rather than fixed, deliberately:

- **Below about 1100px of window width** the character creator's fixed columns crush the sheet.
- **Renaming a portrait file** orphans characters wearing it (they fall back to their initial).
- **`StepsToReach` runs one route query per candidate tile** on hover — 37 of them at reach 3. Cached
  per hover, and fine at arena scale, but it is not a shape that would survive a bigger board.
- **No host migration.** Host leaves, match over, everyone back to the lobby.
