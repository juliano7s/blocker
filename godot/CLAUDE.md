# Godot Layer

Rendering, input, UI, audio. Reads simulation state, never mutates it.

## Layer boundary

No `GameState` mutation from Godot scripts. All game logic lives in `Blocker.Simulation`. Godot scripts read `GameState` and `VisualEvents` each frame for rendering/effects.

## Relay event lifecycle

The `RelayClient` outlives individual scenes (carried via `MultiplayerLaunchData.Relay` across scene changes). This creates a real bug pattern:

- **Always subscribe with stored delegate fields**, not anonymous lambdas.
- **Always unsubscribe in `_ExitTree`** — otherwise the dead scene's handlers fire on the next relay broadcast and call `CallDeferred` on freed nodes.
- `LockstepCoordinator.Detach()` unsubscribes the coordinator from relay events. Call it before scene changes when the relay survives.

## Scene handoff via statics

- `GameLaunchData` (MapData, Assignments, MultiplayerSession) — consumed by `GameManager._Ready`, cleared after use.
- `MultiplayerLaunchData` (Intent, Relay, JoinCode, RematchReattach, PendingRoomState) — survives across scenes for the relay lifecycle.
- `MapSelection.SelectedMapFileName` — set by MapSelectScreen, persists.

If you add a new cross-scene data flow, use the same pattern: set static, consume in `_Ready`, clear after consumption.

## DrainInbound pattern

Relay messages land on a `ConcurrentQueue` from a background receive thread. They must be drained on the main thread for Godot safety. Any scene holding a `RelayClient` needs a `Timer` calling `_relay.DrainInbound()` every frame (~16ms).
