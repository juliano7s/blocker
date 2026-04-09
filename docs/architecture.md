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
│  WebSocket/UDP client, hash verify  │  Replay recording/playback
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
    public List<VisualEvent> VisualEvents { get; }  // Consumed by rendering each frame
    public int TickNumber { get; }

    /// Advance simulation by one tick. All game rules resolve here.
    /// Commands are validated before application.
    public void Tick(List<Command> commands);

    /// FNV-1a hash of full state for desync detection.
    public ulong GetStateHash();
}
```

Tick resolution follows the exact order from game bible Section 14:

1. Clear push flags
2. Formations — root/uproot progress, pattern detection, nest upgrades
3. Stun Towers — fire stun rays in sweep cycle
4. Soldier Towers — fire blast rays
5. Stun — advance ray heads, apply effects, decay cooldowns
6. Variant cooldowns — Jumper jump, Warden pull
7. Push fire — rooted Builders fire new push waves
8. Push waves — advance wave heads, displace blocks
9. Command queues — pop and execute front command per block
10. Warden ZoC — compute slow fields
11. Snap prevPos — store for interpolation
12. Movement — per-type intervals
13. Combat — surrounding + soldier kills + HP decay
14. Spawning — nest timers, spawn blocks
15. Death effects — emit VisualEvents for dying blocks
16. Increment tick

### 2.3 Command System

```csharp
public record Command(
    int PlayerId,
    CommandType Type,
    List<int> BlockIds,     // Target blocks
    GridPos? TargetPos,     // For move, jump aim, etc.
    Direction? Direction    // For stun ray, push, etc.
);

public enum CommandType
{
    Move, AttackMove, Root, Uproot, ConvertToWall,
    FireStunRay, TogglePush, SelfDestruct, MagnetPull,
    JumpAim, JumpExecute, CreateTower, AssignControlGroup,
    SelectControlGroup, BlueprintPlace
}
```

Commands are validated inside `Tick()` — invalid commands (wrong owner, dead block, on cooldown) are silently dropped. This is important for lockstep: all clients must process identical command lists, so validation must be deterministic.

### 2.4 Visual Events

The simulation emits `VisualEvent` structs each tick to inform rendering of cosmetic triggers. These are one-shot notifications — rendering consumes them and spawns effects.

```csharp
public record VisualEvent(
    VisualEventType Type,
    GridPos Position,
    int? PlayerId,
    Direction? Direction,
    int? Range,
    int? BlockId
);

public enum VisualEventType
{
    BlockMoved, BlockDied, BlockSpawned, BlockRooted, BlockUprooted,
    WallConverted, StunRayFired, StunRayHit, BlastRayFired,
    PushWaveFired, JumpExecuted, JumpLanded, MagnetPulled,
    SelfDestructed, FormationFormed, FormationDissolved,
    NestSpawned, TowerFired, PlayerEliminated, GameOver,
    CommandMoveIssued, CommandRootIssued, CommandUprootIssued
}
```

### 2.5 Project Structure

```
src/Blocker.Simulation/
├── Blocker.Simulation.csproj
├── Core/
│   ├── GameState.cs          — Top-level state + Tick()
│   ├── Grid.cs               — 2D grid, cell access
│   ├── Cell.cs               — Ground type + block ref
│   ├── Block.cs              — All block state
│   ├── Player.cs             — Player + team info
│   └── Constants.cs          — All game constants (Section 18)
├── Blocks/
│   ├── BlockType.cs          — Enum: Builder, Wall, Soldier, Stunner, Warden, Jumper
│   ├── Movement.cs           — Per-type move intervals, pathfinding
│   ├── Rooting.cs            — Root/uproot state machine
│   └── Health.cs             — HP tracking for Soldier/Jumper
├── Combat/
│   ├── Surrounding.cs        — 3-ortho, 2+2, 2+1diag rules
│   ├── SoldierKills.cs       — Adjacency kill thresholds
│   └── NeutralObstacles.cs   — Breakable/Fragile wall logic
├── Economy/
│   ├── NestDetection.cs      — Pattern matching for nest formation
│   ├── Spawning.cs           — Spawn timers, congestion BFS, pop cap
│   └── NestUpgrade.cs        — Auto-upgrade Builder→Soldier nest
├── Formations/
│   ├── FormationDetection.cs — Pattern matching for non-nest formations
│   ├── StunTower.cs          — Sweep fire cycle, ray logic
│   ├── SoldierTower.cs       — Simultaneous blast fire
│   ├── SupplyFormation.cs    — L-shape wall detection, pop cap bonus
│   └── Dissolution.cs        — Instant vs TearingDown paths
├── Abilities/
│   ├── StunRay.cs            — Ray advance, range, LOS blocking
│   ├── Push.cs               — Wave fire, chain push, two-cell hit zone
│   ├── Jump.cs               — Range, combo, HP loss, ZoC block
│   ├── MagnetPull.cs         — Chebyshev radius pull
│   └── SelfDestruct.cs       — Blast ray in 8 directions
├── Commands/
│   ├── CommandType.cs        — Command enum
│   ├── Command.cs            — Command record
│   ├── CommandQueue.cs       — Per-block queue, shift-append, auto-chain
│   └── CommandValidator.cs   — Deterministic validation
├── Maps/
│   ├── MapLoader.cs          — Parse text format → Grid
│   └── MapFormat.cs          — Cell char mappings (Section 15.2)
└── Net/
    ├── LockstepManager.cs    — Collect commands, wait for all players, advance
    ├── StateHash.cs          — FNV-1a hash of GameState
    ├── CommandSerializer.cs  — Binary serialization for network + replay
    └── ReplayRecorder.cs     — Record/playback command logs
```

---

## 3. Godot Layer

### 3.1 Tick Runner

Godot drives ticks from `_Process()`:

```csharp
public partial class TickRunner : Node
{
    private double _accumulator;
    private double _tickInterval;  // 1.0 / tickRate

    public override void _Process(double delta)
    {
        _accumulator += delta;
        while (_accumulator >= _tickInterval)
        {
            var commands = _inputTranslator.FlushCommands();
            _gameState.Tick(commands);
            _accumulator -= _tickInterval;
        }
    }
}
```

For networked play, the tick only fires when all players' commands for that tick have arrived.

### 3.2 Rendering

Renderers read `GameState` every frame, not every tick. This enables smooth interpolation.

```csharp
public partial class BlockRenderer : Node2D
{
    public override void _Process(double delta)
    {
        float t = (float)(_tickRunner.Accumulator / _tickRunner.TickInterval);
        foreach (var block in _gameState.Blocks)
        {
            var visual = GetOrCreateVisual(block.Id);
            visual.Position = Lerp(block.PrevPos, block.Pos, t) * CellSize;
            visual.UpdateAppearance(block);  // rooting state, HP, stun, etc.
        }
    }
}
```

Visual effects (lightning, death, stun rays) are handled by `EffectManager`, which consumes `GameState.VisualEvents` after each tick and spawns Godot particle/shader nodes.

### 3.3 Input Translation

```
Godot InputEvent
  → InputTranslator determines context (selection, ability, movement)
  → Builds Command(type, blockIds, target, direction)
  → Queues command for next tick
```

Selection state (which blocks are selected, control groups, drag rect) lives in the Godot layer — it's UI state, not simulation state.

### 3.4 Project Structure

```
godot/
├── project.godot
├── Blocker.Game.csproj              — References ../src/Blocker.Simulation
├── Scenes/
│   ├── Main.tscn                    — Entry scene, bootstraps game or menu
│   ├── Game/
│   │   ├── GameBoard.tscn           — Grid + blocks + effects container
│   │   └── GameCamera.tscn          — Camera2D with edge scroll, zoom
│   ├── Blocks/
│   │   ├── BlockVisual.tscn         — Base block scene (colored square)
│   │   ├── BuilderVisual.tscn       — Builder-specific inner detail
│   │   ├── SoldierVisual.tscn       — Spinning sword arms
│   │   ├── StunnerVisual.tscn       — Diamond spin
│   │   ├── WardenVisual.tscn        — Shield pulse
│   │   ├── JumperVisual.tscn        — Lava ball gradient
│   │   └── WallVisual.tscn          — Static square
│   ├── Effects/
│   │   ├── Lightning.tscn           — Grid lightning system
│   │   ├── DeathEffect.tscn         — Inflation + brick explosion
│   │   ├── StunRayEffect.tscn       — Traveling pulse wave
│   │   ├── PushWaveEffect.tscn      — Double chevron
│   │   ├── JumpTrailEffect.tscn     — Core streak + embers
│   │   ├── FrostOverlay.tscn        — Stun visual overlay
│   │   └── WardenZocEffect.tscn     — Expanding pulse ring
│   ├── UI/
│   │   ├── HUD.tscn                 — Pop display, block bar, selection info
│   │   ├── Minimap.tscn             — Bottom-right minimap
│   │   ├── MainMenu.tscn            — Title, play, settings
│   │   ├── Lobby.tscn               — Multiplayer lobby
│   │   └── Chat.tscn                — In-game chat overlay
│   └── Audio/
│       └── AudioManager.tscn        — AudioStreamPlayer pool
├── Scripts/
│   ├── Game/
│   │   ├── TickRunner.cs            — _Process tick accumulator
│   │   ├── GameManager.cs           — Setup, teardown, mode switching
│   │   └── InputTranslator.cs       — InputEvent → Command
│   ├── Rendering/
│   │   ├── GridRenderer.cs          — Draw grid lines, ground types
│   │   ├── BlockRenderer.cs         — Per-block visuals + interpolation
│   │   ├── EffectManager.cs         — Consumes VisualEvents, spawns effects
│   │   └── PostProcessing.cs        — Bloom, kill flash
│   ├── Input/
│   │   ├── SelectionManager.cs      — Click, drag, control groups
│   │   ├── CameraController.cs      — Edge scroll, zoom, arrow keys
│   │   └── CursorManager.cs         — OS-level cursor confinement
│   ├── UI/
│   │   ├── HudController.cs         — Population, selection info, keybind hints
│   │   ├── MinimapController.cs     — Minimap rendering + click-to-jump
│   │   ├── LobbyController.cs       — Lobby UI logic
│   │   └── ChatController.cs        — Chat messages
│   └── Audio/
│       ├── AudioManager.cs          — Event → sound mapping, pooling
│       └── MusicManager.cs          — Background music, adaptive layers
├── Assets/
│   ├── Sprites/                     — Optional PNGs (procedural fallback exists)
│   ├── Shaders/
│   │   ├── lightning.gdshader       — Multi-pass lightning bloom
│   │   ├── frost.gdshader           — Ice overlay for stunned blocks
│   │   ├── bloom.gdshader           — Post-processing bloom
│   │   └── glow.gdshader            — Radial glow for wardens, stunners
│   ├── Audio/                       — .wav/.ogg sound files
│   └── Fonts/
└── Resources/                       — .tres (themes, materials, palettes)
```

### 3.5 Content Pipeline

**Procedural first**: Every visual (blocks, effects, animations) has a code-drawn procedural implementation. The game is fully playable with zero art assets. This matches the game bible's "procedural fallback" strategy.

**Sprites as optional upgrade**: Drop PNGs in `Assets/Sprites/`. Block renderers check for sprite existence and use it if available, otherwise draw procedurally.

**Audio**: `.wav`/`.ogg` files in `Assets/Audio/`. `AudioManager` maps `VisualEventType` → audio streams. Missing audio files produce no sound (no crash).

**Shaders**: `.gdshader` files for lightning bloom, frost overlay, glow effects. These enhance the procedural visuals — they're not replacements.

---

## 4. Networking

### 4.1 Lockstep Flow

```
Tick N:
  1. Local player inputs → Command list for tick N+delay
  2. Send commands to relay server
  3. Server broadcasts all players' commands for tick N
  4. All clients receive complete command set
  5. Tick(commands) — identical on all clients
  6. Exchange state hashes — mismatch = pause + diagnostic
```

Input delay: 1 tick default, 2 ticks if RTT > 80ms.

### 4.2 Relay Server

Simple relay — receives commands from all clients, broadcasts to all. No game logic on server. Can be a lightweight WebSocket server (Node.js, C#, or Go — whatever's easiest to deploy).

### 4.3 Replay System

```csharp
public class ReplayRecorder
{
    // Records: simulation version, map, player setup, commands per tick
    public void RecordTick(int tick, List<Command> commands);
    public ReplayFile Save();
}

public class ReplayPlayer
{
    // Loads replay, re-simulates tick by tick
    public GameState Step();  // Advance one tick
    public void SeekTo(int tick);  // Re-simulate from start to tick N
}
```

Replays are just command logs. Determinism guarantees identical playback.

---

## 5. Map Format

Text-based format matching the original game's encoding (Section 15.2). Loaded by `MapLoader` in the simulation layer.

Maps are plain text files:
- One character per cell, one line per row
- Two-layer format: ground grid + `---` separator + unit grid
- Width/height inferred from grid dimensions

The Godot layer reads the parsed `Grid` from the simulation and renders ground types visually (colored cell backgrounds, terrain sprites, zone highlights).

Future: may add a Godot-native map editor that writes this format, or migrate to a binary format for large maps. The text format is the starting point.

---

## 6. Solution Structure

```
blocker/
├── blocker.sln                     — Solution: all three projects
├── docs/
│   ├── game-bible.md
│   ├── godot-migration-design.md
│   └── architecture.md             — This document
├── src/
│   └── Blocker.Simulation/
│       └── Blocker.Simulation.csproj
├── tests/
│   └── Blocker.Simulation.Tests/
│       └── Blocker.Simulation.Tests.csproj  — xUnit, references Simulation
├── godot/
│   └── Blocker.Game.csproj         — References ../src/Blocker.Simulation
└── CLAUDE.md
```

One `blocker.sln` at the root ties everything together. `dotnet test` runs simulation tests. Godot opens `godot/project.godot`.

---

*This document is the architecture authority. When implementation diverges, update whichever is wrong — but discuss first.*
