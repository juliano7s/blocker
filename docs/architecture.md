# Blocker — Godot Architecture

> This document defines how the game bible maps to Godot 4 + C#. It is the technical foundation for the reimplementation.

---

## 1. Layer Separation

Three strictly separated layers. No game logic in Godot scripts — only presentation and input mapping.

```
┌─────────────────────────────────────┐
│  Godot Layer (Rendering + Input)    │  Scenes, shaders, particles, UI, audio
│  Scripts read simulation state      │  Godot InputEvent → Command
├─────────────────────────────────────┤
│  Simulation Layer (Pure C#)         │  Zero Godot deps — deterministic, testable
│  Grid, blocks, combat, formations,  │  Integer math only, no floats in sim
│  spawning, push, abilities, ticks   │  All game rules live here
├─────────────────────────────────────┤
│  Networking Layer (Pure C#)         │  Lockstep sync, command relay
│  WebSocket/UDP client, hash verify  │  Replay recording (planned)
└─────────────────────────────────────┘
```

**Hard rules:**
- `Blocker.Simulation` has zero `using Godot;` statements. Ever.
- Godot scripts never mutate `GameState` — only `Tick()` does.
- All timing expressed in ticks, never wall-clock time, inside the simulation.
- No floats in simulation math. Positions are integer grid coordinates.

---

## 2. Simulation Layer — `Blocker.Simulation`

A standalone .NET class library. Testable with xUnit outside the engine.

### 2.1 Core Types

```
GameState           — The entire game state. Single source of truth.
Grid                — 2D array of Cells. grid[y][x], row-major, origin top-left.
Cell                — Ground type + optional Block reference.
Block               — Type, owner, position, prevPosition, state (mobile/rooting/rooted/uprooted),
                      HP (Soldier/Jumper), stun timer, cooldown timer, command queue.
Player              — ID, team ID, color, population count, max population.
Team                — ID, member player IDs.
```

### 2.2 Tick Engine

```csharp
public class GameState
{
    public Grid Grid { get; }
    public List<Block> Blocks { get; }
    public List<Player> Players { get; }
    public List<Formation> Formations { get; }
    public List<Nest> Nests { get; }
    public List<VisualEvent> VisualEvents { get; }
    public int TickNumber { get; }

    /// Advance simulation by one tick. All game rules resolve here.
    public void Tick(List<Command> commands);

    /// Command entry point and queue processing.
    public void ProcessCommands(List<Command> commands);

    /// FNV-1a hash of full state for desync detection.
    public ulong GetStateHash();
}
```

Tick resolution order (`GameState.Tick`):

1. Clear push and jump flags
2. Formations — `RootingSystem`, `NestSystem`, `FormationSystem`
3. Towers — `TowerSystem` (Stun/Blast fire)
4. Stun — `StunSystem` (Advance rays, apply effects)
5. Cooldown decay — Jumper jump, Stunner fire, Warden pull
6. Push — `PushSystem` (Fire waves + advance)
7. Commands — `ProcessCommands` executes or queues actions
8. Warden ZoC — `WardenSystem.UpdateZoC`
9. Snap `prevPos` — for rendering interpolation
10. Movement — `PathfindingSystem.GetNextStep` + `TryMoveBlock`
11. Combat — `CombatSystem` (Surrounding + Soldier kills)
12. Spawning — `NestSystem.TickSpawning`
13. Elimination check — `EliminationSystem`
14. Increment tick

### 2.3 Command System

```csharp
public record Command(
    int PlayerId,
    CommandType Type,
    List<int> BlockIds,
    GridPos? TargetPos = null,
    Direction? Direction = null,
    bool Queue = false,
    BlockType? UnitType = null
);

public enum CommandType
{
    Move, Root, ConvertToWall, FireStunRay, SelfDestruct,
    CreateTower, TogglePush, MagnetPull, Jump, AttackMove,
    Surrender, ToggleSpawn
}
```

Commands are processed in `GameState.ProcessCommands`. Immediate commands clear the `Block.CommandQueue`; queued commands (Shift+action) append to it.

### 2.4 Visual Events

One-shot notifications emitted by the simulation for the rendering layer.

```csharp
public enum VisualEventType
{
    BlockMoved, BlockDied, BlockSpawned, BlockRooted, BlockUprooted,
    WallConverted, StunRayFired, StunRayHit, BlastRayFired,
    PushWaveFired, JumpExecuted, JumpLanded, MagnetPulled,
    SelfDestructed, FormationFormed, FormationDissolved,
    NestSpawned, TowerFired, PlayerEliminated, GameOver,
    CommandMoveIssued, CommandRootIssued, CommandUprootIssued,
    WallDamaged, WallDestroyed
}
```

### 2.5 Project Structure (Simulation)

```
src/Blocker.Simulation/
├── Core/
│   ├── GameState.cs          — Top-level state + Tick() + Command processing
│   ├── Grid.cs               — 2D grid logic
│   ├── Cell.cs               — Ground + block reference
│   ├── Block.cs              — Unit state + CommandQueue property
│   ├── Player.cs             — Player + team info
│   ├── VisibilityMap.cs      — Fog of war explored/visible state
│   ├── SimulationTicker.cs   — Accumulator math & timing logic
│   ├── SimulationConfig.cs   — Configurable constants
│   ├── Constants.cs          — Static accessor for active config
│   └── Direction.cs          — Cardinal direction enum/helpers
├── Blocks/
│   ├── BlockType.cs          — Unit type enum
│   └── BlockState.cs         — Mobile/Rooting/Rooted/Uprooting enum
├── Systems/                  — Pure functional game rule modules
│   ├── CombatSystem.cs       ├── EliminationSystem.cs  ├── ExplosionSystem.cs
│   ├── FormationSystem.cs    ├── JumperSystem.cs       ├── NestSystem.cs
│   ├── PathfindingSystem.cs  ├── PushSystem.cs         ├── RootingSystem.cs
│   ├── StunSystem.cs         ├── TowerSystem.cs        └── WardenSystem.cs
├── Commands/
│   ├── Command.cs            — Command & QueuedCommand records
│   └── CommandSerializer.cs  — Binary serialization for net/replay
├── Maps/
│   ├── MapLoader.cs          — JSON/Text parser
│   ├── MapData.cs            — Map record definition
│   └── SlotAssignment.cs     — Map slot mapping
└── Net/
    ├── LockstepCoordinator.cs— Lockstep sync management
    ├── StateHasher.cs        — FNV-1a hashing
    └── Protocol.cs           — Binary wire protocol
```

---

## 3. Godot Layer

### 3.1 Tick Runner

`TickRunner.cs` and `MultiplayerTickRunner.cs` drive the simulation. They use `SimulationTicker` to manage the timing accumulator.

### 3.2 Rendering

`GridRenderer.cs` (partial class) handles all 2D drawing. 
- `GridRenderer.Blocks.cs`: Block visuals, stripes, rooting.
- `GridRenderer.Formations.cs`: Formation borders and diamonds.
- `GridRenderer.Effects.cs`: Ray visuals and grid effects.
- `GridRenderer.Selection.cs`: Selection boxes and paths.

`EffectManager.cs` consumes `VisualEvents` to spawn one-shot scenes like `DeathEffect.tscn`.

### 3.3 Selection and Input

`SelectionManager.cs` is the primary input gateway, split into:
- `SelectionManager.Input.cs`: Captures Mouse/Key events.
- `SelectionManager.Commands.cs`: Builds and sends `Command` objects to the simulation.
- `SelectionManager.Drawing.cs`: Gizmos and previews.

### 3.4 Project Structure (Godot)

```
godot/
├── Scenes/
│   ├── Main.tscn                    — Entry point
│   ├── MapEditor.tscn               — Level editor
│   └── Game/                        — Container for gameboard scenes
├── Scripts/
│   ├── Game/
│   │   ├── TickRunner.cs            — Local tick driver
│   │   └── GameManager.cs           — Setup & teardown
│   ├── Rendering/
│   │   ├── GridRenderer.cs          — Partial class drawing engine
│   │   ├── SpriteFactory.cs         — Sprite/texture management
│   │   └── EffectManager.cs         — VisualEvent consumer
│   ├── Input/
│   │   ├── SelectionManager.cs      — Partial class input hub
│   │   └── SelectionState.cs        — UI selection logic
│   └── UI/
│       ├── HudOverlay.cs            — HUD & command card
│       └── MinimapPanel.cs          — Overview navigation
├── Assets/
│   ├── Maps/                        — Map JSON files
│   ├── Shaders/                     — Visual shaders
│   └── Audio/                       — Sound files
└── Maps/                            — Map storage
```

---

## 4. Data Life Cycle (The Handshakes)

### 4.1 Input → Simulation
1. **Trigger**: User performs action.
2. **Translate**: `SelectionManager.Commands.cs` builds a `Command`.
3. **Buffer**: `LockstepCoordinator` handles local/network delay.
4. **Execute**: `GameState.ProcessCommands` validates and clears/appends to `Block.CommandQueue`.

### 4.2 Simulation → Rendering
1. **Trigger**: `System` resolves a rule and emits a `VisualEvent`.
2. **Consume**: `EffectManager` polls `GameState.VisualEvents`.
3. **Present**: Godot spawns a scene or `GridRenderer` updates a property.

---

*This document is the architecture authority. When implementation diverges, update whichever is wrong — but discuss first.*
