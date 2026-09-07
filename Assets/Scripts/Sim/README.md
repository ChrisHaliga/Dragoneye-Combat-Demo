# Dragoneye.Sim

The fight, whole, as plain objects. No engine, no network, no scene: `noEngineReferences` is set,
the assembly references only `Combat`, `Hex` and `Hex.Systems`, and the build check refuses any
source in this folder that so much as names `UnityEngine`, `Unity.Netcode`, `Dragoneye.Game`,
`Dragoneye.UI` or `Dragoneye.Data`.

That is the whole point of it. A fight that can be run in a console with nothing watching is a
fight that no view can break. The arrow points one way: the game references this, this references
the rules, and nothing here can reach back.

## What is here

| Type | What it is |
|---|---|
| `Fight` | The fight: the creatures in it, whose turn it is, and every rule for what happens when one of them does something. Four partial files -- the core, the clash, the swing at somebody walking past, and the computer's turn. |
| `FightCreature` | One creature: what it is (fixed for the fight) and what is left of it (changed only by the fight). |
| `ElementPool` | A creature's elements: the published ledger everybody reads, and the private one with commitments in flight. |
| `TurnQueue` | The initiative order, the round, and how the fight ended. |
| `FightBoard` | Every question about the board -- routes, reach, lines -- over an `IGridRules` and an `IOccupancy`. |
| `IOccupancy` | Who is standing where, by id. The fight answers it from its own table; a client answers it from replicated state. |
| `ShotLines` | A line traced, both halves: the walls the map knows about and the bodies the fight does. |
| `ThreatGeometry` | Bearings, what a facing watches, and what leaving a tile provokes. |
| `IFightListener` | Everything the fight has to say: the record, its questions, a wall changing, a kill's worth. |
| `CombatEvent` / `CombatEventCodec` | One thing that happened, and its packing to ints for the wire. |
| `ICreatureBrain` / `BasicBrain` | What runs the computer's creatures, and the shipped one. |

## How it is driven

A `Fight` is built from a `HexMap`, its `IGridRules`, an `IElementMatchup`, `Dice`, an
`ICreatureBrain` and an `IFightListener`. Creatures are `Add`ed, `Begin()` opens it, and from
then on everything is a method call by creature id: `Move`, `UseSkill`, `EndTurn`, `AnswerClash`,
`AnswerOpportunity`, `SetWall`, `Finish`.

**It has no clock.** The fight resolves as fast as it is decided. The only thing it ever waits
for is a person's answer -- it asks through the listener and waits for `AnswerClash` or
`AnswerOpportunity` -- and a computer creature's turn is a `Step()` the caller takes when it
likes: one decision per call, so a server can take one a frame and a test can take them until the
fight is over. A brain that throws, or keeps asking for what it cannot do, loses its turn and not
the match.

Nothing the fight says returns a value it then depends on. It writes its record, asks its
questions, and reads the answers back through its own methods -- so a listener that never answers
leaves a fight that is waiting, never a fight that is wrong.

## The other end

On the server, `CombatDirector` (in `Game`) owns one `Fight`, is its listener, carries orders in
from the command postboxes by creature id, and after every order copies the fight's state out into
the replicated components the views read. That copy is one-way. Nothing on a `NetworkBehaviour` is
read back into the fight, and nothing in this folder knows the director exists.

In the harness, `FightChecks` builds fights from a map, the shipped table and a scripted listener,
and runs them to the end: a whole fight between two computer sides, a person's clash asked and
answered later, a swing at somebody walking past taken and declined, a wall coming down under a
creature, a brain that throws. If a check there ever needed a scene, the fight would have grown a
dependency it must not have.
