# CLAUDE.md — Blocker

## What is this

Blocker is a minimalist RTS where armies of blocks expand across a grid through formation-based tactics and spatial control. Up to 6 players, team-based, lockstep multiplayer. Reimplemented in Godot 4 + C# from a browser-based TypeScript original.

## Design authority

- **Game bible**: `docs/game-bible.md` — the authoritative game design reference. All mechanics, constants, visuals, and audio are defined here.
- **Architecture**: `docs/architecture.md` — how the game bible maps to Godot. Layer separation, project structure, data flow.
- **Migration strategy**: `docs/godot-migration-design.md` — why we moved to Godot, phased plan.

When code and docs disagree, figure out which is wrong and fix it — but discuss first.

**Keep the game bible current.** When we change a game mechanic, constant, visual, or behavior during implementation, update `docs/game-bible.md` to reflect the new design. The bible is the living spec — it should always describe the game we're actually building, not just the original design.

## Collaboration principles

- **Challenge everything.** Do not assume the docs or jjack's instructions are always right. If you see a better approach, say so. We're building the best game we can — no sacred cows.
- **No artificial limitations.** Assume we can do anything until proven otherwise. Propose the ideal solution first; we scale back only if there's a real constraint.
- **Brief summaries after changes.** After making edits, give a short summary of what changed and why.

## Architecture — hard rules

### Layer separation

Three layers, strictly separated:

1. **Simulation** (`src/Blocker.Simulation/`) — Pure C# class library. Zero `using Godot;` statements. Deterministic, portable, testable with xUnit.
2. **Godot** (`godot/`) — Rendering, input, UI, audio. Reads simulation state, never mutates it. All game logic lives in the simulation.
3. **Networking** (inside simulation) — Lockstep sync, command serialization, replay. Pure C#.

### Determinism contract

- No floats in simulation. All positions are integer grid coordinates.
- No system randomness. Seeded deterministic RNG only if needed.
- All player actions flow through the Command system.
- Tick resolution order follows game bible Section 14 exactly.

### Data flow

```
Player input (Godot InputEvent)
  → InputTranslator builds Command
  → Command queued for next tick (local) or sent to relay (networked)
  → Lockstep collects all players' commands for tick N
  → GameState.Tick(commands) advances simulation
  → Godot reads GameState + VisualEvents, renders
```

Godot polls GameState every frame for interpolation. VisualEvents are one-shot notifications consumed by EffectManager after each tick.

### Content pipeline

- **Procedural first**: Every visual has a code-drawn implementation. Game is fully playable with zero art assets.
- **Sprites optional**: PNGs in `godot/Assets/Sprites/` override procedural visuals when present.
- **Audio optional**: `.wav`/`.ogg` in `godot/Assets/Audio/`. Missing files = silence, not crashes.

## Project structure

```
blocker/
├── blocker.sln
├── src/Blocker.Simulation/          — Pure C# simulation (see architecture.md for full layout)
├── tests/Blocker.Simulation.Tests/  — xUnit tests
├── godot/                           — Godot 4 project
│   ├── project.godot
│   ├── Blocker.Game.csproj
│   ├── Scenes/                      — .tscn scene files
│   ├── Scripts/                     — Godot C# scripts
│   ├── Assets/                      — Sprites, shaders, audio, fonts
│   └── Resources/                   — .tres files
└── docs/                            — Game bible, architecture, migration
```

## Commands

- `dotnet test` — Run simulation unit tests (from repo root)
- `dotnet build` — Build simulation library
- Godot project opens at `godot/project.godot`

## Code conventions

- C# naming: PascalCase for public members, _camelCase for private fields
- No `using Godot;` in anything under `src/`
- Constants from game bible Section 18 live in `src/Blocker.Simulation/Core/Constants.cs`
- Types/enums updated before referencing them (see global CLAUDE.md)
- Prefer simple, direct code. No speculative abstractions.

## Testing

- Simulation tests use xUnit. Test determinism, combat rules, formation detection, spawning, commands.
- Replay determinism: record commands, replay, verify state hashes match.
- Manual playtesting for feel, visuals, responsiveness.

## Current phase

**Phase 2: Architecture Design** — complete. Starting Phase 3: Incremental Implementation.

Next milestone: Grid + Camera + Input — render a grid, scroll around, select cells.
