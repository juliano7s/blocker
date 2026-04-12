# Blocker.Simulation

Pure C# class library. Zero Godot dependencies. Deterministic, portable, testable with xUnit.

## Determinism — non-negotiable

- No floats. All positions are integer grid coordinates (`GridPos`).
- No `System.Random`. Seeded deterministic RNG only if needed.
- All player actions flow through the `Command` system.
- Tick resolution order follows game bible Section 14 exactly — don't reorder systems in `GameState.Tick()`.

## Hostility vs ownership

Two distinct concepts when checking block relationships:

- **Ownership** (`block.PlayerId`): who controls the block. Used for command authorization, tower arming, nest spawning, formation membership.
- **Hostility** (`state.AreEnemies(a, b)` / `state.AreAllies(a, b)`): team-aware. Used for combat, ZoC, jumper movement blocking, stun rays, attack-move targeting, tower scanning.

If you're adding a new system, ask: "does this care about who owns it, or who's friendly?" Pick the right check.

## Player-level actions go through Commands

Surrender is a `CommandType.Surrender` processed in `GameState.ProcessCommands`, not a wire-level protocol message. This keeps it deterministic — every peer applies it at the exact same tick. Future player-level actions (e.g., GG, forfeit timer) should follow the same pattern.

## Push waves are team-blind

`PushSystem` affects all mobile blocks regardless of team. This is intentional per the game bible — push has no friend/foe distinction.
