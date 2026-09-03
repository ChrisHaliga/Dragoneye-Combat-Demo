# Multiplayer setup

Relay-backed sessions over Unity Gaming Services, driving Netcode for GameObjects.
Host creates a session and gets a 6-character join code; clients join by code. No port
forwarding — Relay traverses NAT.

## Scenes

Build order matters — **Bootstrap must be index 0**, and all three must be enabled. Netcode
resolves scenes by build index, so a missing one fails at runtime with a scene-not-found that is
easy to misread.

| Scene | Contents |
|---|---|
| **Bootstrap** | `NetworkManager` (+ `UnityTransport`), `Session Runner` (`SessionRunner` + `MatchFlow`). Nothing visible. Loads MainMenu on start. |
| **MainMenu** | Menu camera, `Session UI` — `UIDocument` (→ `SessionPanelSettings` + `SessionMenu.uxml`) + `MainMenuUI`, and `Draft Panel` — `UIDocument` + `DraftPanelView`. |
| **Arena** | Camera, light, ground, four `SpawnPoint`s. |

**Why the persistent objects live in Bootstrap and not MainMenu:** `NetworkManager.OnEnable` calls
`DontDestroyOnLoad` on itself unconditionally, and NGO does *not* destroy duplicates — it only
warns about "more than one NetworkManager in the DDOL scene". If MainMenu owned the
NetworkManager, returning to the menu after a match would load a second one alongside the
surviving original. A bootstrap scene that is never re-entered avoids the problem entirely.

If the `NetworkManager` is ever recreated from scratch, set **Server Listen Address** on
`UnityTransport` to `0.0.0.0`. It defaults to `127.0.0.1`, which only accepts loopback
connections. Relay overrides this in practice, so a wrong value here fails only on a
direct-connect fallback — which makes it an easy one to misdiagnose later.

## Match lifecycle

```
Bootstrap ──> MainMenu ──host clicks Start──> Arena ──session ends──> MainMenu
```

- **Match start.** `StartMatchAsync` locks the lobby, then `MatchFlow` (host only) calls
  `NetworkManager.SceneManager.LoadScene(Arena)`. The networked scene load *is* the start signal:
  it travels over Relay, ordered against all other gameplay traffic. Clients follow automatically.
  An earlier version broadcast a lobby session property instead — that is the slow, rate-limited,
  eventually-consistent channel and should carry pre-connection metadata only.
- **Spawning.** `AutoSpawnPlayerPrefabClientSide` is **off**. It fires on connect, which would drop
  every player into the world while they were still in the lobby, making ready-up meaningless.
  `MatchFlow` spawns players at `SpawnPoint`s on `OnLoadEventCompleted` instead.
- **Session end.** Host leaves, or you leave, or you are removed → `SessionRunner.SessionEnded` →
  `MatchFlow.ReturnToMenu()` shuts netcode down and loads MainMenu with a plain (non-networked)
  `SceneManager.LoadScene`. `ReturnToMenu` is idempotent because a host leaving fires both a
  netcode disconnect and a lobby `Deleted` event, and either can arrive first.
- **Leaving mid-match.** Escape in the Arena scene calls `MatchFlow.LeaveMatch()`, which leaves the
  session when there is one and otherwise just shuts netcode down. The input layer does not need
  to know which kind of match it is in.

There is **no host migration**. Host leaves, match over, everyone back to the menu. Deliberate for
the MVP; it constrains how much authoritative state can live only on the host.

## Singleplayer

The Singleplayer button calls `MatchFlow.StartSoloMatch()`: it restores the loopback transport
settings a previous Relay session may have overwritten, calls `NetworkManager.StartHost()`, and
loads the Arena. No sign-in, no session, no Relay, no join code, and nothing can connect to it.

Netcode still runs. That is the point rather than a compromise: a solo match then takes the exact
same path through spawning, ownership, the draft and the rules as a hosted one. A separate
netcode-free path would be a second implementation of those rules, free to drift from the
multiplayer one until a playtest caught it. The cost is a loopback socket nobody dials.

Sign-in still happens at boot so the multiplayer screens can prepopulate a name, but nothing on the
Singleplayer, Settings or Quit paths depends on it — they all work with the services unreachable.

## Menu structure

`MainMenuUI` owns which panel is visible; the screens themselves are plain classes bound to one
subtree each.

```
Home ──> Singleplayer  (straight to Arena)
     ──> Multiplayer ──> Host ──> Lobby
     │               └─> Join ──> Lobby
     ──> Test Mode    (disabled)
     ──> Settings
     ──> Quit
```

Everything needing Unity services lives in `SessionScreens`, which is what lets the rest of the
menu work offline. `SettingsScreen` reads and writes `Dragoneye.Settings.GameSettings`.

## How a session maps onto netcode

`CreateSessionAsync` / `JoinSessionByCodeAsync` allocate Relay, configure `UnityTransport`, and
start `NetworkManager.Singleton` as part of the same call. There is no separate
`StartHost` / `StartClient` step, and nothing in the scene should call those directly.

`WithRelayNetwork()` has no local shortcut: even two players on one machine round-trip through a
Relay server in a Unity data center. A successful local test therefore exercises the same path as
a test across the internet.

## Identity

- **Player ID** — anonymous sign-in caches a token in `PlayerPrefs`, keyed by auth *profile*.
  Builds use the default profile, so the account persists across launches.
- **Player name** — stored server-side on the UGS account. Mirrored to `PlayerPrefs` locally as a
  display cache so the name field is populated before sign-in completes; the server value wins.
- **Editor profiles** — `BuildInitializationOptions()` gives each editor process its own profile.
  Multiplayer Play Mode virtual players share `PlayerPrefs`, so without this they would all sign
  in as the same player and only the first could join a lobby. Side effect: each editor session
  creates a throwaway anonymous account and the name resets. Builds are unaffected.

## Editor tooling

One-off editor automation belongs under a **`ClaudeCode/`** menu root, so it is obvious at a
glance which menus are real tooling and which are disposable scaffolding.

`Assets/Editor/SceneSplitSetup.cs` (`ClaudeCode/Multiplayer/Split Into Bootstrap + MainMenu +
Arena`) generated the three scenes above. It is spent once it has run and its output is committed
— delete it then, the way `MultiplayerSceneSetup.cs` was deleted before it. Re-running it after
the scenes are hand-edited would overwrite that work.
