# Combat

The rules. Everything here decides an outcome, and nothing here can draw, replicate or wait.

`noEngineReferences` is set on the assembly definition, so "Combat holds no engine types" is a
compile error rather than a convention. There is no `Vector3`, no `Mathf`, no `MonoBehaviour` and no
`ScriptableObject` in this assembly, and there cannot be. `references` is empty for the same reason:
Combat depends on nothing, and the compiler is what enforces it.

## What that buys

- Every rule is testable without a scene, a network or a clock.
- Two peers running the same inputs reach the same answer, because there is nothing platform- or
  frame-dependent to diverge on.
- A rule cannot quietly start reading a transform or a `Time.deltaTime`.

## What lives elsewhere

| Wants | Goes in |
|---|---|
| An authored asset holding numbers | Data |
| Anything drawn or clicked | View |
| Anything replicated | Net |
| Anything that wires the above together | App |

Combat states a question and something outside answers it. Grid distance, terrain cost and element
matchups are all seams for that reason: designer-tunable answers live in Data, while the logic that
must resolve identically for both players stays here.

## Numbers

AP is stored in **half-units** — 3 AP is 6. Movement costs one half-unit per tile and skills cost
whole AP, so integers cover both and a replay cannot drift the way floats would across platforms.
`Ap` is the type that carries this; it exists so the conversion happens in one place rather than at
every call site.
