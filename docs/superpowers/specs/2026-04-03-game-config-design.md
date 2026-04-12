# Game Config — Centralized Inspector-Editable Configuration

## Goal

Replace scattered `const` fields and hardcoded colors with a single inspector-editable configuration system. All balance values (unit stats, timings, combat) and all visual values (colors, sizes, effects) become tweakable from the Godot inspector without recompiling.

## Architecture

### Layer Separation

The simulation layer stays pure C# with zero Godot dependency.

```
GameConfig (Godot Resource, [Export] properties)
  → ToSimulationConfig() builds pure C# config
  → SimulationConfig passed to GameState at startup
  → GridRenderer reads GameConfig directly for visual values
```

### Files

| File | Layer | Purpose |
|------|-------|---------|
| `src/Blocker.Simulation/Core/SimulationConfig.cs` | Simulation | Plain C# config class with all balance values and defaults |
| `src/Blocker.Simulation/Core/UnitConfigs.cs` | Simulation | Per-unit config record structs (BuilderConfig, SoldierConfig, etc.) |
| `godot/Scripts/Config/GameConfig.cs` | Godot | Godot Resource with [Export] groups for everything |
| `godot/Scripts/Config/PlayerPalette.cs` | Godot | Godot Resource with per-unit and per-formation colors |

### Constants.cs Migration

`Constants.cs` becomes a static holder with a `SimulationConfig` instance:

```csharp
public static class Constants
{
    public static SimulationConfig Config { get; private set; } = new();
    public static void Initialize(SimulationConfig config) => Config = config;
}
```

All existing references like `Constants.RootTicks` become `Constants.Config.Builder.RootTicks` (or similar per-unit lookup). A helper `Constants.GetUnitConfig(BlockType)` returns the right sub-config.

### Per-Unit Rooting

Root/uproot ticks move from global constants to per-unit configs. Each unit type defines its own `RootTicks` and `UprootTicks`. Default values match current globals (36 and 24) but can be overridden per unit.

---

## SimulationConfig Structure

All values are plain C# with defaults matching current `Constants.cs`.

### Per-Unit Configs

Each unit type is a record struct:

```
BuilderConfig:
  Hp = 1, MoveInterval = 3, PopCost = 1, SpawnTicks = 140
  RootTicks = 36, UprootTicks = 24

SoldierConfig:
  Hp = 4, MoveInterval = 4, PopCost = 1, SpawnTicks = 260
  RootTicks = 36, UprootTicks = 24
  ExplodeRange = 3

StunnerConfig:
  Hp = 1, MoveInterval = 2, PopCost = 3, SpawnTicks = 360
  RootTicks = 36, UprootTicks = 24
  StunDuration = 160, StunCooldown = 140, StunRange = 5
  UnitRayAdvanceInterval = 2

WardenConfig:
  Hp = 1, MoveInterval = 3, PopCost = 2, SpawnTicks = 220
  RootTicks = 36, UprootTicks = 24
  ZocRadius = 4, PullRadius = 4, PullCooldown = 140

JumperConfig:
  Hp = 3, MoveInterval = 3, PopCost = 2, SpawnTicks = 300
  RootTicks = 36, UprootTicks = 24
  JumpRange = 5, JumpCooldown = 120

WallConfig:
  PopCost = 0
```

### Global Configs

```
EconomyConfig:
  SupplyPopCap = 7, SupplyMembers = 3, ProtoSpawnMultiplier = 5

CombatConfig:
  FragileWallSoldierThreshold = 2, MoveGiveUpTicks = 30

TowerConfig:
  StunTowerFireInterval = 16, StunTowerRange = 4
  SoldierTowerFireInterval = 12, SoldierTowerRange = 5
  StunTowerRayAdvanceInterval = 2
  BlastTowerRayAdvanceInterval = 1

PushConfig:
  WaveInterval = 8, Range = 4, Knockback = 3
  WaveAdvanceInterval = 1, WaveFade = 6

RayConfig:
  BlastUnitRayAdvanceInterval = 1
  StunRayFade = 8
```

---

## GameConfig (Godot Resource)

A `[GlobalClass]` Godot Resource with `[ExportGroup]` and `[ExportSubgroup]` organizing all values. Exposed as an `[Export]` property on `GameManager`.

### Inspector Layout

```
GameConfig
├── [Group: Builder]
│   HP, MoveInterval, PopCost, SpawnTicks, RootTicks, UprootTicks
├── [Group: Soldier]
│   HP, MoveInterval, PopCost, SpawnTicks, RootTicks, UprootTicks, ExplodeRange
├── [Group: Stunner]
│   HP, MoveInterval, PopCost, SpawnTicks, RootTicks, UprootTicks
│   StunDuration, StunCooldown, StunRange, UnitRayAdvanceInterval
├── [Group: Warden]
│   HP, MoveInterval, PopCost, SpawnTicks, RootTicks, UprootTicks
│   ZocRadius, PullRadius, PullCooldown
├── [Group: Jumper]
│   HP, MoveInterval, PopCost, SpawnTicks, RootTicks, UprootTicks
│   JumpRange, JumpCooldown
├── [Group: Wall]
│   PopCost
├── [Group: Economy]
│   SupplyPopCap, SupplyMembers, ProtoSpawnMultiplier
├── [Group: Combat]
│   FragileWallSoldierThreshold, MoveGiveUpTicks
├── [Group: Towers]
│   StunTower: FireInterval, Range, RayAdvanceInterval
│   SoldierTower: FireInterval, Range
│   BlastTowerRayAdvanceInterval
├── [Group: Push]
│   WaveInterval, Range, Knockback, WaveAdvanceInterval, WaveFade
├── [Group: Rays]
│   BlastUnitRayAdvanceInterval, StunRayFade
├── [Group: Grid]
│   DefaultWidth, DefaultHeight, CellSize, GridLineWidth
│   GridLineColor, NormalGroundColor, BootGroundColor, OverloadGroundColor
│   ProtoGroundColor, TerrainGroundColor, BreakableWallColor, FragileWallColor
├── [Group: Global Colors]
│   StunRayColor, BlastRayColor
│   FrozenOverlayColor, FrozenBorderColor, FrostCrackColor
│   ThreatIndicatorColor, ThreatGlowColor
│   SelectionBorderColor
├── [Group: Visual]
│   BlockInset, BlockGlowSize, DeathEffectTicks, DeathFragmentCount, FragmentLifetime
│   WardenZocWaveCycleMs, SelectionDashLength, SelectionGapLength
├── [Group: Players]
│   PlayerPalettes (PlayerPalette[], 6 entries)
```

---

## PlayerPalette (Godot Resource)

Each player gets a `PlayerPalette` resource. All colors auto-derive from `Base` by default but can be individually overridden.

### Implementation: Defaults from Base

Each color property has its default value set in `_computeDefaults()`, called when `Base` changes. Since Godot `[Export]` Color is a value type (not nullable), defaults are computed eagerly and baked into the exported properties. Setting `Base` recomputes all colors that haven't been manually changed (tracked via a private `HashSet<string>` of manually-edited property names, or simply: all defaults are computed once at resource creation / when Base is set via a button/method).

Simplest viable approach: all colors get sensible defaults computed from Base in the constructor. Tweaking individual colors just overwrites them. A `RecomputeFromBase()` tool button resets all derived colors from the current Base.

```csharp
[Export] public Color Base { get; set; } = new(0.25f, 0.55f, 1.0f);
[Export] public Color BuilderFill { get; set; }  // initialized to Base in constructor
[Export] public Color BuilderGradientLight { get; set; }  // initialized to Base.Lightened(0.28f)
```

### Per-Unit Colors

```
[Builder]
  Fill, GradientLight, GradientDark

[Soldier]
  Fill, ArmsColor, ArmsGlow, CenterDot

[Stunner]
  Fill, BevelLight, BevelShadow, DiamondOuter, DiamondInner, Glow

[Warden]
  Fill, Ring, InnerHighlight, Glow, ZocColor

[Jumper]
  Core, Bright, Dark, PulseGlow

[Wall]
  Fill, Highlight, Shadow, Inner
```

### Per-Formation Colors

```
[Builder Nest]
  Outline, OutlineGlow, Diamond, SpawnBar

[Soldier Nest]
  Outline, OutlineGlow, Diamond, SpawnBar

[Stunner Nest]
  Outline, OutlineGlow, Diamond, SpawnBar

[Supply Formation]
  Outline, OutlineGlow, Diamond

[Stun Tower]
  Outline, OutlineGlow, Diamond

[Soldier Tower]
  Outline, OutlineGlow, Diamond
```

### Per-Player Effects

```
[Effects]
  PushWaveColor, DeathColor, DeathFragmentColor
  RootingBracketColor, CornerTickColor
```

### Default Derivation

When a palette is created with just `Base` set, all other colors compute from it:

| Color | Default Derivation |
|-------|--------------------|
| BuilderFill | Base |
| BuilderGradientLight | Base.Lightened(0.28f) |
| BuilderGradientDark | Base.Darkened(0.18f) |
| SoldierFill | Base.Darkened(0.3f) |
| SoldierArmsColor | Gold (1, 0.78, 0.2) |
| StunnerDiamondOuter | Base.Lightened(0.3f) |
| WallFill | Base.Darkened(0.5f) |
| WallHighlight | Base.Darkened(0.2f) |
| WardenFill | Base with A=0.5 |
| WardenRing | Base.Lightened(0.4f) |
| JumperCore | Base |
| JumperBright | Base.Lightened(0.5f) |
| JumperDark | Base.Darkened(0.3f) |
| NestOutline | Base.Lightened(0.2f) |
| DeathColor | Base |
| PushWaveColor | Cyan-tinted Base |
| ZocColor | Base |

Full derivation table follows the current rendering logic. The point: setting just `Base` produces the current look. Individual overrides let you diverge.

---

## Migration Impact

### Simulation Code Changes

All references to `Constants.XXX` must update:
- Global constants → `Constants.Config.Xxx.Value`
- Per-unit constants → `Constants.Config.GetUnit(blockType).Value` or specific like `Constants.Config.Soldier.ExplodeRange`
- `Block.RootTicks` → reads from the config for its type instead of global `Constants.RootTicks`

### Rendering Code Changes

- `GridRenderer` receives `GameConfig` reference from `GameManager`
- All hardcoded colors replaced with reads from `GameConfig` or `PlayerPalette`
- Helper methods like `GetPlayerColor(int playerId)` → `_config.GetPalette(playerId).BuilderFill` etc.

### Test Changes

- Tests currently reference `Constants.XXX` — update to new paths
- Tests can construct `SimulationConfig` with custom values for testing edge cases

---

## What This Does NOT Change

- Simulation remains pure C#, zero Godot dependency
- GameState still owns all game logic
- Rendering still reads state, never mutates it
- No .tres file shipped — all defaults in code
