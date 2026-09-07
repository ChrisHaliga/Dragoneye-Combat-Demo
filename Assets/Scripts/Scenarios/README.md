# Scenarios

Fights written down, and what they prove. This is the test mode's content and the arena's map
recipes, in one assembly that references only the rules and the grid -- no engine components, no
netcode, no assets -- so every scenario can be read and checked by the harness and by NUnit without
a scene.

| Type | What it is |
|---|---|
| `Scenario` | A map recipe, the actors on it with their scripts, events the world does to itself mid-fight, a seed, and the checks |
| `Actor` | A premade by id, a side, a starting cell and facing, and orders grouped by turn (`Then`, `NextTurn`), or `RunByTheGame()` |
| `Order` | `Move(cell)` or `Use(skillId, targetKey)` -- the same two decisions a brain makes |
| `ScenarioEvent` | A wall segment changing at the start of a named actor's turn in a given round |
| `IScenarioWorld` | What the checks read: each actor's state, its skills as it holds them, the grid, the matchup table, the trace |
| `TraceEntry` | One thing the fight announced, by actor key: an order, a turn, a move, a shot, a clash, a fall, a wall |
| `Oracle` | The rules replayed ahead of the fight with the fight's own dice, so a check can say what *will* happen |
| `ScenarioCheck` | One claim and whether it held |
| `Maps` | The Ruins and the open hexagon, as `MapRecipe`s. The editor step writes the Ruins asset from here |
| `ScenarioLibrary` | Every scenario the test mode offers |

## How a scenario runs

`MatchFlow.StartScenario` starts a solo host with no draft and loads the arena. `MatchSpawner`
sees a scenario pending and hands over to `ScenarioRunner`, which rebuilds the arena's map from
the recipe, spawns the actors in order (which is the order initiative ties break in), gives the
director the scenario's seed and a `ScriptedBrain`, and listens to everything the fight announces.
The fight is the real one: the same director, conductors, turn runner, announcer and HUD a match
uses. When every script has run out -- or the match ends, or the rounds allowed run out -- the
runner stops the fight where it stands and reads the checks against the world a frame later. The
fight is stopped rather than left running: every turn after the last order is a creature with
nothing to do passing to the next, and a board still playing behind a finished report is worse
than no report. Stopping is not winning, so nothing claims a victory that did not happen --
`TurnState.HasWinner` is what a check asks about a side actually winning.

The report shows on the HUD. `Run all` queues the library and, three seconds after each report,
loads the next one by itself; the last one in a run stays up until it is dismissed. **Copy report**
on the test mode screen puts the whole run on the clipboard: a line per scenario, and for anything
that failed, its failing checks and the trace of what the fight announced.

## Prediction and the actual run

A scenario does not hard-code what a roll will do. Its checks build an `Oracle` from the same seed
and replay the rules in the order the fight consumes rolls: a shot rolls first, a computer defender
rolls once per element it puts up, a creature offered a swing rolls once on whether to take it.
The oracle calls the rules the server calls -- `SkillRules.Hits`, `ClashSequence`,
`ClashDefenceOdds.ChooseAnswer`, `Opportunity.Takes`, `CombatRules.Absorb` -- so what it says will
happen is what the rules say, and the check is whether the plumbing between the rules and the
board agreed. A failure is a real disagreement, and the trace on the report says where.

The oracle keeps its attackers where they stand: it does not walk into range, so a scenario that
wants a shot from the doorway orders the walk and the shot separately.

## Writing one

1. Pick a map: `Maps.Ruins()` for walls, `Maps.Open()` for none. Terrain names are `Ground.Grass`
   and `Ground.Stone`; a new terrain is a new name, bound to an asset in `ScenarioRunner` and
   `ArenaMapSetup`.
2. Place actors with `Premade` ids and `Skills` ids. Two actors never share a cell; nobody starts
   on stone or in a sliver.
3. Script each actor's turns. A refused order ends the turn, so what should happen after a refusal
   goes in the next turn.
4. Write the checks with `ScenarioCheck.That` and `Equal`, using an `Oracle` for anything the dice
   decide and `Reading` for the trace.
5. Add it to `ScenarioLibrary.Build`. The harness (`ScenarioChecks`) and `ScenarioLibraryTests`
   read every scenario for keys nobody has, cells nobody can stand on and scripts longer than the
   rounds allowed.
