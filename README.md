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
| [`Assets/Scripts/Hex/README.md`](Assets/Scripts/Hex/README.md) | Coordinates, walls and areas, pathfinding, sight, rendering |
| [`Assets/Scripts/Scenarios/README.md`](Assets/Scripts/Scenarios/README.md) | The test mode's scenarios, the oracle, and the map recipes |
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

Eleven assemblies. The boundaries are not documentation — they are separate DLLs, so a layering
mistake is a compile error rather than something a review has to catch.

```
                    Combat        ← the rules. References nothing. No engine types at all.
                      ↑
                    Data          ← authored ScriptableObjects. Answers the questions Combat asks.
                      ↑
   Hex ─→ Hex.Systems ─→ Hex.Rendering
            ↑
        Scenarios         ← fights written down, and what they prove. Rules and grid only.
                      ↑
   Settings        Camera
                      ↑
                     UI           ← the widgets both halves draw: stat blocks, chips, the rules page
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
| `Dragoneye.Scenarios` | `Scripts/Scenarios` | Combat, Hex, Hex.Systems |
| `Dragoneye.Camera` | `Scripts/Camera` | Settings, Input System, Cinemachine |
| `Dragoneye.UI` | `Scripts/UI` | Combat, Data, Input System |
| `Dragoneye.Multiplayer` | `Scripts/Multiplayer` | Combat, Data, UI, Settings, Hex, Hex.Systems, Netcode, UGS |
| `Dragoneye.Game` | `Scripts/Game` | all of the above |

`Dragoneye.Game` references `Dragoneye.Multiplayer` and not the other way round. That is what lets
the draft board host the session controls: the board is a Game thing, the session is a Multiplayer
thing, and the arrow points the way it does deliberately. If you find yourself wanting Multiplayer
to reach into Game, the design has gone wrong somewhere — put the shared thing lower instead.

`Dragoneye.UI` exists because nothing about drawing a stat block is multiplayer, and nine arena
views were reaching into `Dragoneye.Multiplayer` to get at one. Anything the menus and the arena
both draw lives there.

Namespaces follow folders inside `Game`: `Scripts/Game/Combat` is `Dragoneye.Game.Combat` and
`Scripts/Game/Creatures` is `Dragoneye.Game.Creatures`. The root folder stays `Dragoneye.Game`.

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

`ClaudeCode → Set Up Everything` is the one `[MenuItem]` that stays, deliberately. The steps live
in separate files under `Assets/Editor` because each was written for one change, but none of them
is ever the right one to run alone. Six menu entries only ever raised the question of which were
stale.

A change that needs the editor once — a new asset, a dead component to strip from a scene — ships
as a **named, disposable automation** under the same menu root, named for the job it does. Run it
once, delete its file, commit. **One is waiting now:** `ClaudeCode → Author The Map Choices` writes
the water terrain, binds it into the arena's palette and strips the dead flourish component from
the arena, then wants `Assets/Editor/MapChoicesSetup.cs` deleted. A fresh clone does not need it;
Set Up Everything does the same authoring.

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

### Change what a clash does

An attack on an enemy is a contest, not a subtraction. The attacker's skill commits its element and
spends it; the defender is asked what they answer with, told nothing about what is coming, and
commits one of their own; then both are revealed and the outcome scales the effect.

| Want to change | Where |
|---|---|
| Which element answers which | `ElementMatchups.asset`, authored by `ElementMatchupSetup` |
| What win / tie / loss do to the effect | `ClashRules.Scale` |
| How many elements advantage costs | `ClashRules.CommitmentFor` |
| What a defender may answer with | `ClashRules.AnswersFor` |
| The whole sequence and its concealment | `ClashSequence` |
| How a computer defender chooses | `ClashDefence.Choose` |

**`ClashSequence` is in `Dragoneye.Combat` and must stay there.** It is the pause in an attack made
into an object: who may answer, how many elements, whether an answer was legal, and what the two
commitments come to are all decided in it, on whichever machine is running the fight. `ClashCommands`
is a postbox — it carries a `DefenceRequest` one way and a list of elements back, and decides
nothing. Anything decided there would be decided a second time, differently, the first time somebody
changed a rule, and a fight that resolves one way on the host and another on a client is the hardest
bug this project could grow.

### Change facing

Facing is stored on `CreatureState` and written by exactly two things: moving turns you the way you
walked, attacking turns you toward whoever you swung at. There is no turn action, deliberately —
DE-006 is explicit, and a creature that could move and then turn for free would have one.

The facing rides in the move *intent* (`UnitCommands.RequestMove(hex, facing)`) rather than
following as a second order, because two orders can be interrupted between and that would be a free
turn for anybody who timed it.

`FacingRules.IsFlank` decides which arrivals count; `AreaGeometry.Direction` turns the offset between
two cells into one of six, from area centres in integer geometry, so two areas of one tile have a
bearing and every machine computes the same one. Both are pure and both are tested, including the
boundary tiebreak: a bearing exactly on a boundary belongs to the lower-numbered direction.

### Add a wall, or a map

Maps are `MapRecipe`s in `Scripts/Scenarios/Maps.cs`: a radius of ground, tiles by terrain name,
walls by tile and ray. The ones a host can choose are listed in `MapLibrary` — the Field, the
Mansion, the Islands, the Ruins — and that list is protocol: the lobby sends the index, so append
to it and never reorder it. The host picks in the lobby's setup bar (solo play uses the same pick),
`ChosenMap` rebuilds the arena from the recipe when the scene loads, and the scene's authored asset
(`Assets/Settings/Hex/Ruins.asset`, written by `ArenaMapSetup`) is there to lend its terrain
palette. Terrain names are the three in `ShippedTerrain`: grass; stone, which nobody walks on or
sees through and is drawn raised; water, which nobody walks on but everybody sees over and is drawn
sunk. A new terrain is a spec there and a name in `Ground`; the editor step writes the asset.

A map is a mirror image unless it says otherwise. `Symmetry.MirroredEastWest` completes a recipe
from its eastern half, and the harness's `MapChecks` hold every `MapChoice` marked symmetric to it,
and every spawn anchor to standable ground on the side it belongs to. The Ruins are the one map
that is not, kept as they were.

A wall sits on a **ray** (tile centre to one of the twelve points round it, numbered clockwise
from North: even rays end at edge midpoints, odd at corners) or on a **half-edge** (`SetEdge` sets
both halves, which is how a door-sized gap is left: leave the edge out). Rays that block movement
cut the tile into areas, and a piece narrower than ninety degrees is nowhere anybody can stand, so
a tile never has more than four. A vertical wall is rays 0 and 6; a horizontal one is rays 3 and 9
and then along the flat North edge of the tile beyond, which is how a rectangular room sits on a
hex grid — the Ruins recipe has helpers for both.

Put doors and anything that will one day open on half-edges: those never change a tile's areas.
Movement and sight are separate flags (`WallFlags`), so a hedge and a curtain are both one wall.

### Add a scenario

A scenario is a fight that plays itself in the test mode and checks what happened. They live in
`Scripts/Scenarios` -- `WallScenarios`, `CombatScenarios` and `MapScenarios` -- and are listed in
`ScenarioLibrary`. One is a map recipe, actors with orders grouped by turn, optional wall changes
mid-fight, a seed, and checks. Anything the dice decide is predicted by an `Oracle` that replays
the rules from the same seed, so a check compares the prediction with the run rather than hoping.
[`Scripts/Scenarios/README.md`](Assets/Scripts/Scenarios/README.md) walks through writing one.

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

### Change how a fight is shown

The simulation and the screen are separate, and the screen is behind. The server plays the fight
at its own pace and writes every consequence down as a `CombatEvent` (`FightRecord.Say`), packed
to ints by `CombatEventCodec` and sent to everyone by `CombatAnnouncer`. Each client queues them in
`CombatPlayback`, which applies one at a time to a `PresentedFight` — positions, health, armour,
AP, elements shown and spent, who fell — and waits the beat `PresentationPacing` gives it: a walk
takes as long as the token needs to walk the server's own route, a shot flies its distance, and
holding **Space** plays at four times speed. The server may be five turns ahead; nothing the client
draws knows that.

The board is shown the same way. The arena keeps two of it: `ArenaMap.Map`, which the fight is
played on and which changes the instant a wall does, and `ArenaMap.Shown`, which is what is drawn
and pointed at and changes only when playback reaches that `WallChanged` event. Renderers are
handed the drawn one through `IHexMapSource` and cannot reach the other. Rules, routes and prices
read `Map`; anything that answers where something is on screen reads `Shown`.

`Shown` (the static, not the board) is what the HUD reads: the presented creature, the presented
active turn, whether playback has caught up. Displays — tokens, cards, the log, floating text, the turn bar, the camera — read
presented state. Controls — the action bar, the End Turn pips, the reach and shot previews — read
the live actor, deliberately: a click is priced against what the server will actually accept.
Prompts (a clash answer, a swing at a passer-by) are built only once playback has caught up, so
nobody is asked about a state they have not seen.

To add something a fight can do: a `CombatEventKind`, a factory on `CombatEvent`, its packing, its
`PresentedFight` case, its beat, and a `PresentationChecks` case in the harness. A creature that
dies leaves the board (`ServerLeaveBoard`) but stays as a hidden object, so the event that shows it
falling still has a token to hide.

### Change the UI

UI Toolkit. Markup in `Assets/UI/*.uxml`, styles in `Assets/UI/*.uss`, bound by plain C# classes in
`Scripts/Multiplayer` (menus) and `Scripts/Game/Combat` (the arena HUD).

The arena HUD is four panels and a band. `PartyPanelView` draws the party as floating cards on the
left, each a face with what it is holding down one edge and what is left of it along the bottom.
`TurnBarView` draws the order of play across the top out of the same card, larger and named for
whoever is acting. `CombatLogView` is the last line above the bar, or the whole record when it is
opened. `CreatureCardView` is the inspector, which only opens when somebody asks for it: right-click
a creature or a portrait and choose Inspect. It closes on any click outside it unless it is pinned.

The bottom band is `TurnControlsView` over `SkillBarView`: action points, then health with End Turn
beside it, then the bar of actions. Slots are fixed: Move, then the creature's skills, then its
items, on keys 1 through 9 and 0. A slot carries an icon and its key and nothing else — what a skill
costs is written against the cursor once it is armed, and its name appears over the slot on hover.
A slot that cannot be afforded is greyed rather than disabled, because a disabled button never sees
the pointer and so can never say what it is. Right-clicking one offers Inspect, which opens the
skill in full. `SkillIcons` gives each slot a plate — the sprite authored on the `SkillAsset` if
there is one, otherwise a glyph drawn in the element's colour.

Two EditMode tests hold the markup to the views: every element a view looks up by name exists, and
every class the markup asks for has a rule somewhere. The second also runs in `build.sh`, and
`uifit.py` holds the bottom band under a share of a 720p screen.

The character creator is four pages in `CharacterCreatorScreen`: name, face and species; class,
with every skill each level brings; attributes and kit; the sheet, to confirm. `uifit.py` in the
harness adds up each page's columns against the stage, so a page that grows past the screen fails
a build rather than a playtest.

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

**Camera** — the wheel zooms and a right-drag turns. Raw scroll is converted to *notches* before
being scaled (a Windows wheel reports 120 per detent, other setups report 1), so one notch means the
same thing everywhere; the default is five notches from closest to furthest. Dragging does not zoom.

**Levels** — start at 1, cap at 20. A level costs `2^level` experience. Killing a creature is worth
its level. Multiple levels resolve in one pass (`Progression.Resolve`) so a character out of a long
fight is asked what it becomes once, not once per level.

**Clashes.** An attack on an enemy commits the skill's element and the defender answers with one of
their own. From the defender's side: **win** — no damage, and the element stays in hand; **tie** — no
damage, and the element is gone; **lose** — damage, and the element is gone. So an attack gets
through only by winning, which makes attacking a war of attrition: you throw elements to drain a hand
and the hits land once there is nothing left to answer with. The attacker never gets theirs back. A
defender holding nothing, or choosing to take the hit, leaves the attack unopposed. Skills aimed at
yourself or an ally never clash.

**Odds** are shown to the attacker on hover, to the defender on every option, and used by the
computer — the same function in all three, because a player shown one set of numbers and beaten by a
creature working from another has been lied to. They read only public state: how big a hand is
(total less what is outstanding — exact, everybody watched the spends), which of it has a name, and
which skills a creature has been seen using. **Knowing nothing is not the same as knowing they hold
nothing**, and conflating the two is what once made every option read as a certain loss.

**Facing and advantage.** Five sectors of six are a creature's front; only the one directly behind
is not. (DE-006 offered rear-three-of-six and it played badly — with half the board counting as a
flank, advantage turned up so often it read as random.) An attack arriving from behind
gives the defender disadvantage. Nothing currently grants *advantage* — DE-006 named a shield as its
example and it was taken back out, because doubling a player's element burn for as long as an item is
equipped is a cost they never chose. The rule and the authoring flag both remain, so an item can
grant it. Either state means committing *two* elements instead of one — advantage keeps the better result, disadvantage the worse — so an edge is
never free, and the two cancel on the same side. A side required to commit two while holding one
commits the one.

**Presentation** — a walk is 0.3 s a tile, a shot 0.14 s plus 0.07 s a tile, and Space held plays
four times faster (`PresentationPacing`). A walk that has not finished 2.5 s after it was due is
let go, so a stuck token cannot stall the fight for everybody watching.

**Armour** is a pool above health, not a reduction: none 0, light 4, medium 8, heavy 16, a shield
+4. Every blow wears it down first and it never comes back -- `CombatRules.Absorb` is the one place
that arithmetic lives. The floating combat text shows what the armour held and what got through.

---

## Verifying a change

Unity's own compile is the ground truth, but it is slow and it will happily let you break a layering
rule that the *shipped* build would catch. There is a harness in the session scratchpad that closes
most of that gap in a few seconds:

```bash
bash scratchpad/build.sh
```

For the game as a whole there is the **test mode** on the main menu: a list of scenarios, each a
fight that plays itself on a known map with a known seed and reads its own checks -- flanks, shots
over bodies and hedges, swings at passers-by, armour, healing, a kill, walls coming down under a
creature, the initiative order, and the opponent left to play both sides. `Run all` plays the lot, moving on three
seconds after each report, and comes back to the list with every result -- **Copy report** puts
that whole run on the clipboard, failing checks and traces included. A scenario's checks say in advance what the dice
will do, by replaying the rules from the seed, so a failure is the board disagreeing with the
rules, and the report's trace says where.

`build.sh` does six things:

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
5. **Checks whether an editor step is owed**, by reading the components every file in
   `Assets/Editor` adds and looking for each in the scenes.
6. **Checks the harness is fresh**: every source it compiles is the same file as the project's.

There is also a .NET console harness (`scratchpad/harness/`) that compiles the pure sources and
runs twenty-eight check suites over them — rules, progression, pool pricing, brain decisions, draft
queries, hex placement, camera maths, the presented fight against the events that build it, every
walk's geometry against the walls it crosses, and every map a host can pick.

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

### Concealment is ordering, not access control

The one thing DE-005 exists for is that a defender cannot see what is coming. The way that stops
being true is not a dramatic mistake — it is somebody adding a convenient field to the prompt in a
year's time.

So there is exactly one type that crosses the gap, `DefenceRequest`, and it has no room for the
attacker's skill or element. `ClashSequence` holds the attacker's commitment from the start and
simply does not hand it out: `TryReveal` refuses until the defender has answered. Nothing above has
to remember to withhold anything.

The test walks the prompt by reflection rather than by naming its fields, so a field added later is
covered the day it is added — and there is a control beside it, a deliberately leaky prompt the same
walk is asserted to catch. A concealment test that passes because it saw nothing reads as a
guarantee and is a blank stare.

### The world reads the mouse; the HUD does not get a say unless you ask

`HexPointer` reads the pointer straight off the Input System, which has never heard of UI Toolkit.
So a click on a skill button was *also* a click on whatever tile happened to be drawn behind it, and
the turn went on walking there. `PickingMode.Ignore` on the HUD root does not help — that is about
which element UI Toolkit hands the event to, not about whether the Input System also saw it.

Anything that acts on a raw pointer press has to ask `PointerOverUi.AtScreenPoint` first.

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
- **The camera follows whoever is acting**, and lets go the moment you pan -- for that turn only,
  so the next one brings its creature back into view. Turning and zooming do not break it. If it
  reads as fighting you, the ease and what counts as breaking it are both in `TurnCameraFocus`.
- **No skill breaks a wall yet.** Walls change mid-fight through `CombatDirector.ServerSetWall`
  -- the test mode's wall-break scenario does exactly that, replicated by `WallCommands`, with every
  creature on the tile carried to the ground it stood on -- but nothing a creature can do calls it,
  and `Wall.Integrity` is data nothing reads. A breaching skill is a skill kind and one call.
- **The AI does not seek cover** and does not price a low wall between it and a target beyond the
  hit chance it is handed.
- **Running all scenarios reloads the arena between them through the network scene manager.** A
  reload of the scene that is already loaded is a path no match takes; if NGO refuses it, run the
  scenarios one at a time until it is seen working.
- **A scenario spectates.** Nobody controls an actor, so the HUD shows every turn as somebody
  else's, and the outcome banner has no return to make: the report is the way out.
- **The cutaway shader has not been seen in a running editor.** It is plain URP forward code with a
  depth pass; if it fails to compile the wall material falls back to lit stone and walls hide
  creatures behind them. The masonry it scores into the faces, and the tiles' shadowed skirts and
  relief, are in the same position: written to the URP contract, not yet looked at.
- **The map is the host's to pick.** Everybody else in the lobby sees the card change and cannot
  change it.
- **A fight's playback is not skippable, only faster.** Space holds it at four times speed; there
  is no jump to the live state. It is one call in `CombatPlayback` if a playtest wants it.
