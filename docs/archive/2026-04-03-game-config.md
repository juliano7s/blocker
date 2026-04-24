# Game Config Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace scattered `Constants` fields and hardcoded colors with a centralized, inspector-editable configuration system.

**Architecture:** Pure C# `SimulationConfig` (with per-unit config records) in the simulation layer; Godot `GameConfig` Resource with `[Export]` groups bridging to inspector. `PlayerPalette` Resource for per-player, per-unit colors. `Constants.cs` becomes a thin static accessor. All existing behavior preserved with identical defaults.

**Tech Stack:** C# 12, Godot 4.6, xUnit

**Spec:** `docs/superpowers/specs/2026-04-03-game-config-design.md`

---

### Task 1: Create SimulationConfig and Per-Unit Config Records

**Files:**
- Create: `src/Blocker.Simulation/Core/SimulationConfig.cs`

This is the pure C# config that the simulation reads. All defaults match current `Constants.cs` values exactly.

- [ ] **Step 1: Create SimulationConfig.cs with all config records**

```csharp
// src/Blocker.Simulation/Core/SimulationConfig.cs
namespace Blocker.Simulation.Core;

public record BuilderConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 1;
    public int SpawnTicks { get; init; } = 140;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
}

public record SoldierConfig
{
    public int Hp { get; init; } = 4;
    public int MoveInterval { get; init; } = 4;
    public int PopCost { get; init; } = 1;
    public int SpawnTicks { get; init; } = 260;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int ExplodeRange { get; init; } = 3;
}

public record StunnerConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 2;
    public int PopCost { get; init; } = 3;
    public int SpawnTicks { get; init; } = 360;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int StunDuration { get; init; } = 160;
    public int StunCooldown { get; init; } = 140;
    public int StunRange { get; init; } = 5;
    public int UnitRayAdvanceInterval { get; init; } = 2;
}

public record WardenConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 2;
    public int SpawnTicks { get; init; } = 220;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int ZocRadius { get; init; } = 4;
    public int PullRadius { get; init; } = 4;
    public int PullCooldown { get; init; } = 140;
}

public record JumperConfig
{
    public int Hp { get; init; } = 3;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 2;
    public int SpawnTicks { get; init; } = 300;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int JumpRange { get; init; } = 5;
    public int JumpCooldown { get; init; } = 120;
}

public record WallConfig
{
    public int PopCost { get; init; } = 0;
}

public record EconomyConfig
{
    public int SupplyPopCap { get; init; } = 7;
    public int SupplyMembers { get; init; } = 3;
    public int ProtoSpawnMultiplier { get; init; } = 5;
}

public record CombatConfig
{
    public int FragileWallSoldierThreshold { get; init; } = 2;
    public int MoveGiveUpTicks { get; init; } = 30;
}

public record TowerConfig
{
    public int StunTowerFireInterval { get; init; } = 16;
    public int StunTowerRange { get; init; } = 4;
    public int StunTowerRayAdvanceInterval { get; init; } = 2;
    public int SoldierTowerFireInterval { get; init; } = 12;
    public int SoldierTowerRange { get; init; } = 5;
    public int BlastTowerRayAdvanceInterval { get; init; } = 1;
}

public record PushConfig
{
    public int WaveInterval { get; init; } = 8;
    public int Range { get; init; } = 4;
    public int Knockback { get; init; } = 3;
    public int WaveAdvanceInterval { get; init; } = 1;
    public int WaveFade { get; init; } = 6;
}

public record RayConfig
{
    public int BlastUnitRayAdvanceInterval { get; init; } = 1;
    public int StunRayFade { get; init; } = 8;
}

public record GridConfig
{
    public int DefaultWidth { get; init; } = 41;
    public int DefaultHeight { get; init; } = 25;
}

public record SimulationConfig
{
    public BuilderConfig Builder { get; init; } = new();
    public SoldierConfig Soldier { get; init; } = new();
    public StunnerConfig Stunner { get; init; } = new();
    public WardenConfig Warden { get; init; } = new();
    public JumperConfig Jumper { get; init; } = new();
    public WallConfig Wall { get; init; } = new();
    public EconomyConfig Economy { get; init; } = new();
    public CombatConfig Combat { get; init; } = new();
    public TowerConfig Tower { get; init; } = new();
    public PushConfig Push { get; init; } = new();
    public RayConfig Ray { get; init; } = new();
    public GridConfig Grid { get; init; } = new();
    public int DeathEffectTicks { get; init; } = 10;
    public int TeardownTicks { get; init; } = 24;

    /// <summary>Look up the root ticks for a given block type.</summary>
    public int GetRootTicks(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.RootTicks,
        Blocks.BlockType.Soldier => Soldier.RootTicks,
        Blocks.BlockType.Stunner => Stunner.RootTicks,
        Blocks.BlockType.Warden => Warden.RootTicks,
        _ => Builder.RootTicks // fallback
    };

    /// <summary>Look up the uproot ticks for a given block type.</summary>
    public int GetUprootTicks(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.UprootTicks,
        Blocks.BlockType.Soldier => Soldier.UprootTicks,
        Blocks.BlockType.Stunner => Stunner.UprootTicks,
        Blocks.BlockType.Warden => Warden.UprootTicks,
        _ => Builder.UprootTicks
    };

    /// <summary>Look up the move interval for a given block type.</summary>
    public int GetMoveInterval(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Soldier => Soldier.MoveInterval,
        Blocks.BlockType.Stunner => Stunner.MoveInterval,
        Blocks.BlockType.Warden => Warden.MoveInterval,
        Blocks.BlockType.Jumper => Jumper.MoveInterval,
        _ => Builder.MoveInterval
    };

    /// <summary>Look up the pop cost for a given block type.</summary>
    public int GetPopCost(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.PopCost,
        Blocks.BlockType.Wall => Wall.PopCost,
        Blocks.BlockType.Soldier => Soldier.PopCost,
        Blocks.BlockType.Stunner => Stunner.PopCost,
        Blocks.BlockType.Warden => Warden.PopCost,
        Blocks.BlockType.Jumper => Jumper.PopCost,
        _ => 0
    };

    /// <summary>Look up the max HP for a given block type.</summary>
    public int GetMaxHp(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Soldier => Soldier.Hp,
        Blocks.BlockType.Jumper => Jumper.Hp,
        _ => 1
    };

    /// <summary>Look up the spawn ticks for a nest type and ground.</summary>
    public int GetSpawnTicks(NestType nestType, GroundType ground)
    {
        int baseTicks = nestType switch
        {
            NestType.Builder => ground == GroundType.Overload
                ? Warden.SpawnTicks : Builder.SpawnTicks,
            NestType.Soldier => ground == GroundType.Overload
                ? Jumper.SpawnTicks : Soldier.SpawnTicks,
            NestType.Stunner => Stunner.SpawnTicks,
            _ => Builder.SpawnTicks
        };
        if (ground == GroundType.Proto)
            baseTicks *= Economy.ProtoSpawnMultiplier;
        return baseTicks;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj`
Expected: Build succeeded, 0 errors

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Simulation/Core/SimulationConfig.cs
git commit -m "feat: add SimulationConfig with per-unit config records"
```

---

### Task 2: Migrate Constants.cs to Static Config Holder

**Files:**
- Modify: `src/Blocker.Simulation/Core/Constants.cs`

Replace all `const` fields with a static `SimulationConfig` instance and accessor properties. Existing code using `Constants.XXX` continues to work via compatibility properties during migration.

- [ ] **Step 1: Rewrite Constants.cs**

Replace the entire file with:

```csharp
// src/Blocker.Simulation/Core/Constants.cs
using Blocker.Simulation.Blocks;

namespace Blocker.Simulation.Core;

/// <summary>
/// Static accessor for the active SimulationConfig.
/// Initialized once at game start. Defaults match game bible Section 18.
/// </summary>
public static class Constants
{
    private static SimulationConfig _config = new();

    /// <summary>The active simulation configuration.</summary>
    public static SimulationConfig Config => _config;

    /// <summary>Set the active config. Call once at game start before any simulation runs.</summary>
    public static void Initialize(SimulationConfig config) => _config = config;

    /// <summary>Reset to defaults. Use only in tests.</summary>
    public static void Reset() => _config = new();

    // --- Compatibility accessors (read from active config) ---

    // Grid
    public static int DefaultGridWidth => _config.Grid.DefaultWidth;
    public static int DefaultGridHeight => _config.Grid.DefaultHeight;

    // Rooting — per-unit via helper
    public static int GetRootTicks(BlockType type) => _config.GetRootTicks(type);
    public static int GetUprootTicks(BlockType type) => _config.GetUprootTicks(type);
    // Legacy global accessor (uses Builder defaults for backward compat during migration)
    public static int RootTicks => _config.Builder.RootTicks;

    // Spawning
    public static int SpawnTicksBuilder => _config.Builder.SpawnTicks;
    public static int SpawnTicksSoldier => _config.Soldier.SpawnTicks;
    public static int SpawnTicksStunner => _config.Stunner.SpawnTicks;
    public static int SpawnTicksWarden => _config.Warden.SpawnTicks;
    public static int SpawnTicksJumper => _config.Jumper.SpawnTicks;
    public static int ProtoSpawnMultiplier => _config.Economy.ProtoSpawnMultiplier;

    // Movement
    public static int MoveInterval => _config.Builder.MoveInterval;
    public static int SoldierMoveInterval => _config.Soldier.MoveInterval;
    public static int StunnerMoveInterval => _config.Stunner.MoveInterval;

    // Stun
    public static int StunDuration => _config.Stunner.StunDuration;
    public static int StunCooldown => _config.Stunner.StunCooldown;
    public static int StunRange => _config.Stunner.StunRange;
    public static int StunRayFade => _config.Ray.StunRayFade;
    public static int StunUnitRayAdvanceInterval => _config.Stunner.UnitRayAdvanceInterval;
    public static int StunTowerRayAdvanceInterval => _config.Tower.StunTowerRayAdvanceInterval;

    // Towers
    public static int StunTowerFireInterval => _config.Tower.StunTowerFireInterval;
    public static int StunTowerRange => _config.Tower.StunTowerRange;
    public static int SoldierTowerFireInterval => _config.Tower.SoldierTowerFireInterval;
    public static int SoldierTowerRange => _config.Tower.SoldierTowerRange;

    // Blast
    public static int BlastUnitRayAdvanceInterval => _config.Ray.BlastUnitRayAdvanceInterval;
    public static int BlastTowerRayAdvanceInterval => _config.Tower.BlastTowerRayAdvanceInterval;

    // Push
    public static int PushWaveInterval => _config.Push.WaveInterval;
    public static int PushRange => _config.Push.Range;
    public static int PushKnockback => _config.Push.Knockback;
    public static int PushWaveAdvanceInterval => _config.Push.WaveAdvanceInterval;
    public static int PushWaveFade => _config.Push.WaveFade;

    // Soldier
    public static int SoldierExplodeRange => _config.Soldier.ExplodeRange;
    public static int SoldierMaxHp => _config.Soldier.Hp;

    // Population
    public static int SupplyPopCap => _config.Economy.SupplyPopCap;
    public static int SupplyMembers => _config.Economy.SupplyMembers;

    // Neutral obstacles
    public static int FragileWallSoldierThreshold => _config.Combat.FragileWallSoldierThreshold;

    // Warden
    public static int WardenZocRadius => _config.Warden.ZocRadius;
    public static int WardenPullRadius => _config.Warden.PullRadius;
    public static int WardenPullCooldown => _config.Warden.PullCooldown;

    // Jumper
    public static int JumperJumpRange => _config.Jumper.JumpRange;
    public static int JumperJumpCooldown => _config.Jumper.JumpCooldown;
    public static int JumperMaxHp => _config.Jumper.Hp;

    // Movement give-up
    public static int MoveGiveUpTicks => _config.Combat.MoveGiveUpTicks;

    // Death effects
    public static int DeathEffectTicks => _config.DeathEffectTicks;

    // Formation teardown
    public static int TeardownTicks => _config.TeardownTicks;

    // Population costs
    public static int PopCostBuilder => _config.Builder.PopCost;
    public static int PopCostWall => _config.Wall.PopCost;
    public static int PopCostSoldier => _config.Soldier.PopCost;
    public static int PopCostStunner => _config.Stunner.PopCost;
    public static int PopCostWarden => _config.Warden.PopCost;
    public static int PopCostJumper => _config.Jumper.PopCost;
}
```

- [ ] **Step 2: Build and run tests**

Run: `dotnet build blocker.sln && dotnet test blocker.sln`
Expected: Build succeeded, all 140 tests pass. The compatibility accessors mean zero simulation/test code changes needed yet.

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Simulation/Core/Constants.cs
git commit -m "refactor: Constants.cs reads from SimulationConfig with compat accessors"
```

---

### Task 3: Create PlayerPalette Godot Resource

**Files:**
- Create: `godot/Scripts/Config/PlayerPalette.cs`

Per-player color palette with per-unit and per-formation colors. All defaults derived from Base color.

- [ ] **Step 1: Create PlayerPalette.cs**

```csharp
// godot/Scripts/Config/PlayerPalette.cs
using Godot;

namespace Blocker.Game.Config;

[GlobalClass]
public partial class PlayerPalette : Resource
{
    [ExportGroup("Base")]
    [Export] public Color Base { get; set; } = new(0.25f, 0.55f, 1.0f);

    // --- Per-unit colors ---

    [ExportGroup("Builder")]
    [Export] public Color BuilderFill { get; set; }
    [Export] public Color BuilderGradientLight { get; set; }
    [Export] public Color BuilderGradientDark { get; set; }

    [ExportGroup("Soldier")]
    [Export] public Color SoldierFill { get; set; }
    [Export] public Color SoldierArmsColor { get; set; }
    [Export] public Color SoldierArmsGlow { get; set; }
    [Export] public Color SoldierCenterDot { get; set; }

    [ExportGroup("Stunner")]
    [Export] public Color StunnerFill { get; set; }
    [Export] public Color StunnerBevelLight { get; set; }
    [Export] public Color StunnerBevelShadow { get; set; }
    [Export] public Color StunnerDiamondOuter { get; set; }
    [Export] public Color StunnerDiamondInner { get; set; }
    [Export] public Color StunnerGlow { get; set; }

    [ExportGroup("Warden")]
    [Export] public Color WardenFill { get; set; }
    [Export] public Color WardenRing { get; set; }
    [Export] public Color WardenInnerHighlight { get; set; }
    [Export] public Color WardenGlow { get; set; }
    [Export] public Color WardenZocColor { get; set; }

    [ExportGroup("Jumper")]
    [Export] public Color JumperCore { get; set; }
    [Export] public Color JumperBright { get; set; }
    [Export] public Color JumperDark { get; set; }
    [Export] public Color JumperPulseGlow { get; set; }

    [ExportGroup("Wall")]
    [Export] public Color WallFill { get; set; }
    [Export] public Color WallHighlight { get; set; }
    [Export] public Color WallShadow { get; set; }
    [Export] public Color WallInner { get; set; }

    // --- Per-formation colors ---

    [ExportGroup("Builder Nest")]
    [Export] public Color BuilderNestOutline { get; set; }
    [Export] public Color BuilderNestOutlineGlow { get; set; }
    [Export] public Color BuilderNestDiamond { get; set; }
    [Export] public Color BuilderNestSpawnBar { get; set; }

    [ExportGroup("Soldier Nest")]
    [Export] public Color SoldierNestOutline { get; set; }
    [Export] public Color SoldierNestOutlineGlow { get; set; }
    [Export] public Color SoldierNestDiamond { get; set; }
    [Export] public Color SoldierNestSpawnBar { get; set; }

    [ExportGroup("Stunner Nest")]
    [Export] public Color StunnerNestOutline { get; set; }
    [Export] public Color StunnerNestOutlineGlow { get; set; }
    [Export] public Color StunnerNestDiamond { get; set; }
    [Export] public Color StunnerNestSpawnBar { get; set; }

    [ExportGroup("Supply Formation")]
    [Export] public Color SupplyOutline { get; set; }
    [Export] public Color SupplyOutlineGlow { get; set; }
    [Export] public Color SupplyDiamond { get; set; }

    [ExportGroup("Stun Tower")]
    [Export] public Color StunTowerOutline { get; set; }
    [Export] public Color StunTowerOutlineGlow { get; set; }
    [Export] public Color StunTowerDiamond { get; set; }

    [ExportGroup("Soldier Tower")]
    [Export] public Color SoldierTowerOutline { get; set; }
    [Export] public Color SoldierTowerOutlineGlow { get; set; }
    [Export] public Color SoldierTowerDiamond { get; set; }

    // --- Effects ---

    [ExportGroup("Effects")]
    [Export] public Color PushWaveColor { get; set; }
    [Export] public Color DeathColor { get; set; }
    [Export] public Color DeathFragmentColor { get; set; }
    [Export] public Color RootingBracketColor { get; set; }
    [Export] public Color CornerTickColor { get; set; }

    /// <summary>
    /// Compute all derived colors from Base. Call after changing Base.
    /// Sets every color that is still at default (black with zero alpha).
    /// </summary>
    public void ComputeDefaults()
    {
        var b = Base;

        // Builder
        if (BuilderFill == default) BuilderFill = b;
        if (BuilderGradientLight == default) BuilderGradientLight = b.Lightened(0.28f);
        if (BuilderGradientDark == default) BuilderGradientDark = b.Darkened(0.18f);

        // Soldier
        if (SoldierFill == default) SoldierFill = b.Darkened(0.3f);
        if (SoldierArmsColor == default) SoldierArmsColor = new Color(1f, 0.78f, 0.2f);
        if (SoldierArmsGlow == default) SoldierArmsGlow = new Color(1f, 0.78f, 0.2f, 0.25f);
        if (SoldierCenterDot == default) SoldierCenterDot = new Color(1f, 0.78f, 0.2f);

        // Stunner
        if (StunnerFill == default) StunnerFill = b;
        if (StunnerBevelLight == default) StunnerBevelLight = b.Lightened(0.45f);
        if (StunnerBevelShadow == default) StunnerBevelShadow = b.Darkened(0.4f);
        if (StunnerDiamondOuter == default) StunnerDiamondOuter = b.Lightened(0.3f);
        if (StunnerDiamondInner == default) StunnerDiamondInner = new Color(1f, 1f, 1f, 0.5f);
        if (StunnerGlow == default) StunnerGlow = b;

        // Warden
        if (WardenFill == default) WardenFill = b with { A = 0.5f };
        if (WardenRing == default) WardenRing = b.Lightened(0.4f);
        if (WardenInnerHighlight == default) WardenInnerHighlight = b.Lightened(0.5f);
        if (WardenGlow == default) WardenGlow = b;
        if (WardenZocColor == default) WardenZocColor = b;

        // Jumper
        if (JumperCore == default) JumperCore = b;
        if (JumperBright == default) JumperBright = b.Lightened(0.5f);
        if (JumperDark == default) JumperDark = b.Darkened(0.3f);
        if (JumperPulseGlow == default) JumperPulseGlow = b;

        // Wall
        if (WallFill == default) WallFill = b.Darkened(0.5f);
        if (WallHighlight == default) WallHighlight = b.Darkened(0.2f);
        if (WallShadow == default) WallShadow = b.Darkened(0.7f);
        if (WallInner == default) WallInner = b.Darkened(0.35f);

        // Builder Nest
        if (BuilderNestOutline == default) BuilderNestOutline = b.Lightened(0.2f);
        if (BuilderNestOutlineGlow == default) BuilderNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (BuilderNestDiamond == default) BuilderNestDiamond = new Color(0.5f, 0.95f, 1f);
        if (BuilderNestSpawnBar == default) BuilderNestSpawnBar = new Color(0.4f, 0.7f, 1f, 0.35f);

        // Soldier Nest
        if (SoldierNestOutline == default) SoldierNestOutline = b.Lightened(0.2f);
        if (SoldierNestOutlineGlow == default) SoldierNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (SoldierNestDiamond == default) SoldierNestDiamond = new Color(1f, 0.88f, 0.3f);
        if (SoldierNestSpawnBar == default) SoldierNestSpawnBar = new Color(1f, 0.6f, 0.2f, 0.35f);

        // Stunner Nest
        if (StunnerNestOutline == default) StunnerNestOutline = b.Lightened(0.2f);
        if (StunnerNestOutlineGlow == default) StunnerNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (StunnerNestDiamond == default) StunnerNestDiamond = new Color(0.9f, 0.55f, 1f);
        if (StunnerNestSpawnBar == default) StunnerNestSpawnBar = new Color(0.8f, 0.3f, 1f, 0.35f);

        // Supply
        if (SupplyOutline == default) SupplyOutline = b.Lerp(new Color(0.55f, 0.55f, 0.55f), 0.5f);
        if (SupplyOutlineGlow == default) SupplyOutlineGlow = SupplyOutline with { A = 0.35f };
        if (SupplyDiamond == default) SupplyDiamond = Colors.White;

        // Stun Tower
        if (StunTowerOutline == default) StunTowerOutline = b.Lightened(0.25f);
        if (StunTowerOutlineGlow == default) StunTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        if (StunTowerDiamond == default) StunTowerDiamond = b.Lightened(0.6f);

        // Soldier Tower
        if (SoldierTowerOutline == default) SoldierTowerOutline = b.Lightened(0.25f);
        if (SoldierTowerOutlineGlow == default) SoldierTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        if (SoldierTowerDiamond == default) SoldierTowerDiamond = b.Lightened(0.6f);

        // Effects
        if (PushWaveColor == default) PushWaveColor = new Color(
            b.R * 0.3f + 0.1f, b.G * 0.3f + 0.6f, b.B * 0.3f + 0.6f);
        if (DeathColor == default) DeathColor = b;
        if (DeathFragmentColor == default) DeathFragmentColor = b;
        if (RootingBracketColor == default) RootingBracketColor = new Color(0.5f, 0.5f, 0.5f);
        if (CornerTickColor == default) CornerTickColor = b.Lightened(0.2f);
    }

    /// <summary>Create a palette with defaults computed from the given base color.</summary>
    public static PlayerPalette FromBase(Color baseColor)
    {
        var p = new PlayerPalette { Base = baseColor };
        p.ComputeDefaults();
        return p;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Config/PlayerPalette.cs
git commit -m "feat: add PlayerPalette resource with per-unit/formation colors"
```

---

### Task 4: Create GameConfig Godot Resource

**Files:**
- Create: `godot/Scripts/Config/GameConfig.cs`

Single inspector-editable resource combining all balance values and visual settings.

- [ ] **Step 1: Create GameConfig.cs**

```csharp
// godot/Scripts/Config/GameConfig.cs
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Config;

[GlobalClass]
public partial class GameConfig : Resource
{
    // ========== BALANCE: Per-Unit ==========

    [ExportGroup("Builder")]
    [Export] public int BuilderHp { get; set; } = 1;
    [Export] public int BuilderMoveInterval { get; set; } = 3;
    [Export] public int BuilderPopCost { get; set; } = 1;
    [Export] public int BuilderSpawnTicks { get; set; } = 140;
    [Export] public int BuilderRootTicks { get; set; } = 36;
    [Export] public int BuilderUprootTicks { get; set; } = 24;

    [ExportGroup("Soldier")]
    [Export] public int SoldierHp { get; set; } = 4;
    [Export] public int SoldierMoveInterval { get; set; } = 4;
    [Export] public int SoldierPopCost { get; set; } = 1;
    [Export] public int SoldierSpawnTicks { get; set; } = 260;
    [Export] public int SoldierRootTicks { get; set; } = 36;
    [Export] public int SoldierUprootTicks { get; set; } = 24;
    [Export] public int SoldierExplodeRange { get; set; } = 3;

    [ExportGroup("Stunner")]
    [Export] public int StunnerHp { get; set; } = 1;
    [Export] public int StunnerMoveInterval { get; set; } = 2;
    [Export] public int StunnerPopCost { get; set; } = 3;
    [Export] public int StunnerSpawnTicks { get; set; } = 360;
    [Export] public int StunnerRootTicks { get; set; } = 36;
    [Export] public int StunnerUprootTicks { get; set; } = 24;
    [Export] public int StunnerStunDuration { get; set; } = 160;
    [Export] public int StunnerStunCooldown { get; set; } = 140;
    [Export] public int StunnerStunRange { get; set; } = 5;
    [Export] public int StunnerRayAdvanceInterval { get; set; } = 2;

    [ExportGroup("Warden")]
    [Export] public int WardenHp { get; set; } = 1;
    [Export] public int WardenMoveInterval { get; set; } = 3;
    [Export] public int WardenPopCost { get; set; } = 2;
    [Export] public int WardenSpawnTicks { get; set; } = 220;
    [Export] public int WardenRootTicks { get; set; } = 36;
    [Export] public int WardenUprootTicks { get; set; } = 24;
    [Export] public int WardenZocRadius { get; set; } = 4;
    [Export] public int WardenPullRadius { get; set; } = 4;
    [Export] public int WardenPullCooldown { get; set; } = 140;

    [ExportGroup("Jumper")]
    [Export] public int JumperHp { get; set; } = 3;
    [Export] public int JumperMoveInterval { get; set; } = 3;
    [Export] public int JumperPopCost { get; set; } = 2;
    [Export] public int JumperSpawnTicks { get; set; } = 300;
    [Export] public int JumperRootTicks { get; set; } = 36;
    [Export] public int JumperUprootTicks { get; set; } = 24;
    [Export] public int JumperJumpRange { get; set; } = 5;
    [Export] public int JumperJumpCooldown { get; set; } = 120;

    [ExportGroup("Wall")]
    [Export] public int WallPopCost { get; set; } = 0;

    // ========== BALANCE: Global ==========

    [ExportGroup("Economy")]
    [Export] public int SupplyPopCap { get; set; } = 7;
    [Export] public int SupplyMembers { get; set; } = 3;
    [Export] public int ProtoSpawnMultiplier { get; set; } = 5;

    [ExportGroup("Combat")]
    [Export] public int FragileWallSoldierThreshold { get; set; } = 2;
    [Export] public int MoveGiveUpTicks { get; set; } = 30;

    [ExportGroup("Towers")]
    [Export] public int StunTowerFireInterval { get; set; } = 16;
    [Export] public int StunTowerRange { get; set; } = 4;
    [Export] public int StunTowerRayAdvanceInterval { get; set; } = 2;
    [Export] public int SoldierTowerFireInterval { get; set; } = 12;
    [Export] public int SoldierTowerRange { get; set; } = 5;
    [Export] public int BlastTowerRayAdvanceInterval { get; set; } = 1;

    [ExportGroup("Push")]
    [Export] public int PushWaveInterval { get; set; } = 8;
    [Export] public int PushRange { get; set; } = 4;
    [Export] public int PushKnockback { get; set; } = 3;
    [Export] public int PushWaveAdvanceInterval { get; set; } = 1;
    [Export] public int PushWaveFade { get; set; } = 6;

    [ExportGroup("Rays")]
    [Export] public int BlastUnitRayAdvanceInterval { get; set; } = 1;
    [Export] public int StunRayFade { get; set; } = 8;

    [ExportGroup("Timing")]
    [Export] public int DeathEffectTicks { get; set; } = 10;
    [Export] public int TeardownTicks { get; set; } = 24;

    // ========== VISUAL: Grid ==========

    [ExportGroup("Grid")]
    [Export] public int GridDefaultWidth { get; set; } = 41;
    [Export] public int GridDefaultHeight { get; set; } = 25;
    [Export] public float CellSize { get; set; } = 28f;
    [Export] public float GridLineWidth { get; set; } = 1f;
    [Export] public float BlockInset { get; set; } = 2f;
    [Export] public float BlockGlowSize { get; set; } = 2f;

    [ExportGroup("Grid Colors")]
    [Export] public Color GridLineColor { get; set; } = new(0.12f, 0.13f, 0.22f, 0.35f);
    [Export] public Color NormalGroundColor { get; set; } = new(0.05f, 0.06f, 0.12f);
    [Export] public Color BootGroundColor { get; set; } = new(0.06f, 0.14f, 0.08f);
    [Export] public Color OverloadGroundColor { get; set; } = new(0.12f, 0.06f, 0.18f);
    [Export] public Color ProtoGroundColor { get; set; } = new(0.07f, 0.08f, 0.14f);
    [Export] public Color TerrainGroundColor { get; set; } = new(0.18f, 0.18f, 0.20f);
    [Export] public Color BreakableWallGroundColor { get; set; } = new(0.22f, 0.20f, 0.18f);
    [Export] public Color FragileWallGroundColor { get; set; } = new(0.18f, 0.16f, 0.14f);
    [Export] public Color BackgroundColor { get; set; } = new(0.08f, 0.08f, 0.10f);

    // ========== VISUAL: Global Colors ==========

    [ExportGroup("Global Colors")]
    [Export] public Color StunRayColor { get; set; } = new(0.3f, 0.5f, 1f);
    [Export] public Color BlastRayColor { get; set; } = new(1f, 0.5f, 0.2f);
    [Export] public Color FrozenOverlayColor { get; set; } = new(0.55f, 0.78f, 1f);
    [Export] public Color FrozenBorderColor { get; set; } = new(0.55f, 0.82f, 1f);
    [Export] public Color FrostCrackColor { get; set; } = new(0.7f, 0.9f, 1f, 0.3f);
    [Export] public Color ThreatIndicatorColor { get; set; } = new(1f, 0.15f, 0.1f);
    [Export] public Color SelectionBorderColor { get; set; } = new(1f, 1f, 1f, 0.8f);

    // ========== VISUAL: Effects ==========

    [ExportGroup("Visual Effects")]
    [Export] public int DeathFragmentCount { get; set; } = 28;
    [Export] public float FragmentLifetime { get; set; } = 0.8f;
    [Export] public int WardenZocWaveCycleMs { get; set; } = 2500;
    [Export] public float SelectionDashLength { get; set; } = 4f;
    [Export] public float SelectionGapLength { get; set; } = 3f;

    // ========== PLAYERS ==========

    [ExportGroup("Players")]
    [Export] public PlayerPalette[] PlayerPalettes { get; set; } = [];

    /// <summary>Build a SimulationConfig from inspector values.</summary>
    public SimulationConfig ToSimulationConfig() => new()
    {
        Builder = new BuilderConfig
        {
            Hp = BuilderHp, MoveInterval = BuilderMoveInterval,
            PopCost = BuilderPopCost, SpawnTicks = BuilderSpawnTicks,
            RootTicks = BuilderRootTicks, UprootTicks = BuilderUprootTicks,
        },
        Soldier = new SoldierConfig
        {
            Hp = SoldierHp, MoveInterval = SoldierMoveInterval,
            PopCost = SoldierPopCost, SpawnTicks = SoldierSpawnTicks,
            RootTicks = SoldierRootTicks, UprootTicks = SoldierUprootTicks,
            ExplodeRange = SoldierExplodeRange,
        },
        Stunner = new StunnerConfig
        {
            Hp = StunnerHp, MoveInterval = StunnerMoveInterval,
            PopCost = StunnerPopCost, SpawnTicks = StunnerSpawnTicks,
            RootTicks = StunnerRootTicks, UprootTicks = StunnerUprootTicks,
            StunDuration = StunnerStunDuration, StunCooldown = StunnerStunCooldown,
            StunRange = StunnerStunRange, UnitRayAdvanceInterval = StunnerRayAdvanceInterval,
        },
        Warden = new WardenConfig
        {
            Hp = WardenHp, MoveInterval = WardenMoveInterval,
            PopCost = WardenPopCost, SpawnTicks = WardenSpawnTicks,
            RootTicks = WardenRootTicks, UprootTicks = WardenUprootTicks,
            ZocRadius = WardenZocRadius, PullRadius = WardenPullRadius,
            PullCooldown = WardenPullCooldown,
        },
        Jumper = new JumperConfig
        {
            Hp = JumperHp, MoveInterval = JumperMoveInterval,
            PopCost = JumperPopCost, SpawnTicks = JumperSpawnTicks,
            RootTicks = JumperRootTicks, UprootTicks = JumperUprootTicks,
            JumpRange = JumperJumpRange, JumpCooldown = JumperJumpCooldown,
        },
        Wall = new WallConfig { PopCost = WallPopCost },
        Economy = new EconomyConfig
        {
            SupplyPopCap = SupplyPopCap, SupplyMembers = SupplyMembers,
            ProtoSpawnMultiplier = ProtoSpawnMultiplier,
        },
        Combat = new CombatConfig
        {
            FragileWallSoldierThreshold = FragileWallSoldierThreshold,
            MoveGiveUpTicks = MoveGiveUpTicks,
        },
        Tower = new TowerConfig
        {
            StunTowerFireInterval = StunTowerFireInterval, StunTowerRange = StunTowerRange,
            StunTowerRayAdvanceInterval = StunTowerRayAdvanceInterval,
            SoldierTowerFireInterval = SoldierTowerFireInterval, SoldierTowerRange = SoldierTowerRange,
            BlastTowerRayAdvanceInterval = BlastTowerRayAdvanceInterval,
        },
        Push = new PushConfig
        {
            WaveInterval = PushWaveInterval, Range = PushRange,
            Knockback = PushKnockback, WaveAdvanceInterval = PushWaveAdvanceInterval,
            WaveFade = PushWaveFade,
        },
        Ray = new RayConfig
        {
            BlastUnitRayAdvanceInterval = BlastUnitRayAdvanceInterval,
            StunRayFade = StunRayFade,
        },
        Grid = new GridConfig
        {
            DefaultWidth = GridDefaultWidth, DefaultHeight = GridDefaultHeight,
        },
        DeathEffectTicks = DeathEffectTicks,
        TeardownTicks = TeardownTicks,
    };

    /// <summary>Get palette for a player ID, or a default white palette.</summary>
    public PlayerPalette GetPalette(int playerId)
    {
        if (PlayerPalettes != null && playerId >= 0 && playerId < PlayerPalettes.Length
            && PlayerPalettes[playerId] != null)
            return PlayerPalettes[playerId];
        return _defaultPalette;
    }

    /// <summary>Get the ground color for a given ground type.</summary>
    public Color GetGroundColor(Blocker.Simulation.Core.GroundType ground) => ground switch
    {
        GroundType.Boot => BootGroundColor,
        GroundType.Overload => OverloadGroundColor,
        GroundType.Proto => ProtoGroundColor,
        GroundType.Terrain => TerrainGroundColor,
        GroundType.BreakableWall => BreakableWallGroundColor,
        GroundType.FragileWall => FragileWallGroundColor,
        _ => NormalGroundColor
    };

    private static readonly PlayerPalette _defaultPalette = PlayerPalette.FromBase(Colors.White);

    /// <summary>Create a GameConfig with default palettes for 6 players.</summary>
    public static GameConfig CreateDefault()
    {
        var config = new GameConfig();
        config.PlayerPalettes =
        [
            PlayerPalette.FromBase(new Color(0.25f, 0.55f, 1.0f)),  // Blue
            PlayerPalette.FromBase(new Color(0.95f, 0.25f, 0.2f)),  // Red
            PlayerPalette.FromBase(new Color(0.95f, 0.85f, 0.2f)),  // Yellow
            PlayerPalette.FromBase(new Color(0.2f, 0.85f, 0.35f)),  // Green
            PlayerPalette.FromBase(new Color(0.9f, 0.45f, 0.1f)),   // Orange
            PlayerPalette.FromBase(new Color(0.65f, 0.25f, 0.9f)),  // Purple
        ];
        return config;
    }
}
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Config/GameConfig.cs
git commit -m "feat: add GameConfig resource with balance + visual exports"
```

---

### Task 5: Wire GameManager and Initialize Config

**Files:**
- Modify: `godot/Scripts/Game/GameManager.cs`

GameManager creates the GameConfig, initializes Constants, and passes config to renderers.

- [ ] **Step 1: Update GameManager to use GameConfig**

Add `[Export]` for GameConfig and wire it up in `_Ready()`:

```csharp
// At top of GameManager class, add:
using Blocker.Game.Config;
using Blocker.Simulation.Core;

// Add field:
[Export] public GameConfig Config { get; set; } = null!;

// In _Ready(), before loading the map, add:
Config ??= GameConfig.CreateDefault();
Constants.Initialize(Config.ToSimulationConfig());

// After setting up _gridRenderer, add:
_gridRenderer.SetConfig(Config);

// After setting up _hud, add:
_hud.SetConfig(Config);

// Update the background color line to use config:
RenderingServer.SetDefaultClearColor(Config.BackgroundColor);
```

Full updated `_Ready()` method:

```csharp
public override void _Ready()
{
    // Initialize config
    Config ??= GameConfig.CreateDefault();
    Constants.Initialize(Config.ToSimulationConfig());

    // Load map
    var absolutePath = ProjectSettings.GlobalizePath(MapPath);
    if (!Godot.FileAccess.FileExists(MapPath) && !System.IO.File.Exists(absolutePath))
    {
        absolutePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://"))!,
            "..", "maps", "test-small.txt"
        );
    }

    GD.Print($"Loading map from: {absolutePath}");
    var gameState = MapLoader.LoadFromFile(absolutePath);
    GD.Print($"Map loaded: {gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks, {gameState.Players.Count} players");
    foreach (var block in gameState.Blocks)
        GD.Print($"  Block id={block.Id} {block.Type} P{block.PlayerId} at {block.Pos}");

    // Set up grid renderer
    _gridRenderer = GetNode<GridRenderer>("GridRenderer");
    _gridRenderer.SetGameState(gameState);
    _gridRenderer.SetConfig(Config);

    // Set up camera
    _camera = GetNode<CameraController>("Camera");
    _camera.SetGridSize(gameState.Grid.Width, gameState.Grid.Height);

    // Set up selection
    _selectionManager = GetNode<SelectionManager>("SelectionManager");
    _selectionManager.SetGameState(gameState);

    // Set up tick runner
    _tickRunner = GetNode<TickRunner>("TickRunner");
    _tickRunner.SetGameState(gameState);
    _tickRunner.SetSelectionManager(_selectionManager);
    _gridRenderer.SetTickInterval((float)_tickRunner.TickInterval);

    // Set up HUD
    _hud = new HudOverlay();
    AddChild(_hud);
    _hud.SetGameState(gameState);
    _hud.SetConfig(Config);
    _hud.SetControllingPlayer(0);

    // Set background color from config
    RenderingServer.SetDefaultClearColor(Config.BackgroundColor);
}
```

- [ ] **Step 2: Build (will fail — SetConfig not yet added to renderers)**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: CS1061 errors for `SetConfig` — confirms the wiring is correct, renderers need updating next.

- [ ] **Step 3: Commit work in progress**

```bash
git add godot/Scripts/Game/GameManager.cs
git commit -m "feat: wire GameConfig into GameManager"
```

---

### Task 6: Update GridRenderer to Use GameConfig

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Effects.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Formations.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Selection.cs`

Replace all hardcoded colors and constants with reads from `GameConfig` and `PlayerPalette`.

- [ ] **Step 1: Update GridRenderer.cs — add config field, remove hardcoded colors**

Add at top of class:

```csharp
using Blocker.Game.Config;
```

Replace the color fields and constants with config-driven lookups:

```csharp
private GameConfig _config = GameConfig.CreateDefault();

public void SetConfig(GameConfig config) => _config = config;
```

Remove these static fields (they'll be read from `_config` instead):
- `NormalColor`, `BootColor`, `OverloadColor`, `ProtoColor`, `TerrainColor`, `BreakableWallColor`, `FragileWallColor`, `GridLineColor`
- `PlayerColors` array
- `SelectionBorderColor`
- `CellSize` and `GridLineWidth` constants

Add instance properties:
```csharp
public float CellSize => _config.CellSize;
public float GridLineWidth => _config.GridLineWidth;
```

Note: Since `CellSize` was previously `const`, all usages in partial files need to work with the instance property. The partials share the same `this` so they can access `_config` and `CellSize` directly.

Update `_Draw()`:
- `GetGroundColor(cell.Ground)` → `_config.GetGroundColor(cell.Ground)`
- `GridLineColor` → `_config.GridLineColor`
- `GridLineWidth` → `_config.GridLineWidth`
- `GetPlayerColor(block.PlayerId)` → keep method but change implementation
- Block glow: `color with { A = 0.15f }` stays (uses palette base)
- Selection: `SelectionBorderColor` → `_config.SelectionBorderColor`

Update `GetPlayerColor`:
```csharp
private Color GetPlayerColor(int playerId) => _config.GetPalette(playerId).Base;
```

Remove the old `GetGroundColor` method (use `_config.GetGroundColor` instead).

Update `GridToWorld` and `WorldToGrid` to use instance `CellSize`:
```csharp
public Vector2 GridToWorld(GridPos pos) =>
    new(pos.X * CellSize + CellSize * 0.5f, pos.Y * CellSize + CellSize * 0.5f);

public GridPos WorldToGrid(Vector2 world) =>
    new((int)Mathf.Floor(world.X / CellSize), (int)Mathf.Floor(world.Y / CellSize));
```

Note: These were previously `static`. They need to become instance methods since `CellSize` is now an instance property. Update all callers accordingly (the `SelectionManager` also calls `GridRenderer.GridToWorld` — check if it needs updating).

- [ ] **Step 2: Update GridRenderer.Blocks.cs — use palette colors**

For each block type drawing method, replace hardcoded colors with palette lookups:

```csharp
// Get palette for the block
var palette = _config.GetPalette(block.PlayerId);
```

**DrawWallBlock**: Use `palette.WallFill`, `palette.WallHighlight`, `palette.WallShadow`, `palette.WallInner`.

**DrawGradientBody**: Accept palette colors as parameters or have caller pass them. For Builder: use `palette.BuilderFill`, `palette.BuilderGradientLight`, `palette.BuilderGradientDark`. For Soldier: use `palette.SoldierFill` for darkened body.

**DrawSoldierAnimated**: Use `palette.SoldierArmsColor`, `palette.SoldierArmsGlow`, `palette.SoldierCenterDot`.

**DrawStunnerBody**: Use `palette.StunnerFill`, `palette.StunnerBevelLight`, `palette.StunnerBevelShadow`.

**DrawStunnerAnimated**: Use `palette.StunnerDiamondOuter`, `palette.StunnerDiamondInner`, `palette.StunnerGlow`.

**DrawWardenAnimated**: Use `palette.WardenFill`, `palette.WardenRing`, `palette.WardenInnerHighlight`, `palette.WardenGlow`.

**DrawJumperAnimated**: Use `palette.JumperCore`, `palette.JumperBright`, `palette.JumperDark`, `palette.JumperPulseGlow`.

**DrawFrozenOverlay**: Use `_config.FrozenOverlayColor`, `_config.FrozenBorderColor`, `_config.FrostCrackColor`.

**DrawThreatIndicators**: Use `_config.ThreatIndicatorColor`.

**DrawRootingVisual**: Use `palette.RootingBracketColor`.

Update `DrawBlockTypeIndicator` to pass the palette:
```csharp
var palette = _config.GetPalette(block.PlayerId);
```

- [ ] **Step 3: Update GridRenderer.Effects.cs — use config colors**

**DrawWardenZoC**: Use `palette.WardenZocColor` instead of `GetPlayerColor()`. Use `_config.WardenZocWaveCycleMs`. Read `Constants.WardenZocRadius` (still from Constants since it's a simulation value).

**DrawRays**: Use `_config.StunRayColor` and `_config.BlastRayColor` instead of hardcoded colors.

**DrawPushWaves**: Use `palette.PushWaveColor` instead of computing cyan tint.

**DrawDeathEffects**: Use `palette.DeathColor` and `palette.DeathFragmentColor`. Use `_config.DeathFragmentCount` and `_config.FragmentLifetime`.

- [ ] **Step 4: Update GridRenderer.Formations.cs — use palette formation colors**

Replace `GetFormationStyle` and `GetNestStyle` to read from palette:

```csharp
private (Color Outline, Color OutlineGlow, Color Diamond) GetFormationColors(
    FormationType type, int playerId)
{
    var palette = _config.GetPalette(playerId);
    return type switch
    {
        FormationType.Supply => (palette.SupplyOutline, palette.SupplyOutlineGlow, palette.SupplyDiamond),
        _ => (palette.StunTowerOutline, palette.StunTowerOutlineGlow, palette.StunTowerDiamond)
    };
}

private (Color Outline, Color OutlineGlow, Color Diamond, Color SpawnBar) GetNestColors(
    NestType type, int playerId)
{
    var palette = _config.GetPalette(playerId);
    return type switch
    {
        NestType.Builder => (palette.BuilderNestOutline, palette.BuilderNestOutlineGlow,
            palette.BuilderNestDiamond, palette.BuilderNestSpawnBar),
        NestType.Soldier => (palette.SoldierNestOutline, palette.SoldierNestOutlineGlow,
            palette.SoldierNestDiamond, palette.SoldierNestSpawnBar),
        NestType.Stunner => (palette.StunnerNestOutline, palette.StunnerNestOutlineGlow,
            palette.StunnerNestDiamond, palette.StunnerNestSpawnBar),
        _ => (palette.BuilderNestOutline, palette.BuilderNestOutlineGlow,
            palette.BuilderNestDiamond, palette.BuilderNestSpawnBar),
    };
}
```

Update `DrawFormations` to use `GetFormationColors` / `GetNestColors`.

Update `DrawNestProgress` to use the spawn bar color from palette.

- [ ] **Step 5: Update GridRenderer.Selection.cs — use config**

Replace `SelectionBorderColor` with `_config.SelectionBorderColor`.
Replace dash/gap lengths with `_config.SelectionDashLength` and `_config.SelectionGapLength`.

- [ ] **Step 6: Build to verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 7: Commit**

```bash
git add godot/Scripts/Rendering/
git commit -m "refactor: GridRenderer reads all colors/values from GameConfig"
```

---

### Task 7: Update HudOverlay to Use GameConfig

**Files:**
- Modify: `godot/Scripts/Rendering/HudOverlay.cs`

Remove duplicate `PlayerColors` array; read from `GameConfig`.

- [ ] **Step 1: Update HudOverlay.cs**

Add config field and remove duplicate colors:

```csharp
using Blocker.Game.Config;

// Add field:
private GameConfig _config = GameConfig.CreateDefault();
public void SetConfig(GameConfig config) => _config = config;

// Remove the static PlayerColors array.

// In HudDrawControl, pass config access through _hud:
// Replace: PlayerColors[pid] 
// With: _hud._config.GetPalette(pid).Base
```

Update all `PlayerColors[xxx]` references in `_Draw()` and `DrawBlockRatioBar()` to use `_hud._config.GetPalette(xxx).Base`.

- [ ] **Step 2: Build to verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/HudOverlay.cs
git commit -m "refactor: HudOverlay reads player colors from GameConfig"
```

---

### Task 8: Update SelectionManager GridToWorld Reference

**Files:**
- Modify: `godot/Scripts/Input/SelectionManager.cs` (if it references static GridRenderer.GridToWorld)

Since `GridToWorld` and `WorldToGrid` are now instance methods (CellSize is no longer const), check and update any callers outside GridRenderer.

- [ ] **Step 1: Check SelectionManager for static GridToWorld calls**

Read `godot/Scripts/Input/SelectionManager.cs` and find all `GridRenderer.GridToWorld` / `GridRenderer.WorldToGrid` / `GridRenderer.CellSize` references.

If found, update to either:
- Pass a reference to the GridRenderer instance, or
- Keep `CellSize` as a separate static default (28f) for input purposes, or
- Have SelectionManager get the CellSize from a shared config

The simplest fix: give SelectionManager a reference to GridRenderer (it already uses it indirectly via GameManager), or keep a static `DefaultCellSize` constant.

Recommended: Add a `_cellSize` field to SelectionManager set from config, and local `GridToWorld`/`WorldToGrid` methods.

- [ ] **Step 2: Build and fix any remaining compile errors**

Run: `dotnet build godot/Blocker.Game.csproj`
Fix any remaining references to the old static methods/constants.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/
git commit -m "fix: update SelectionManager for instance-based GridToWorld"
```

---

### Task 9: Update Tests

**Files:**
- Modify: All test files in `tests/Blocker.Simulation.Tests/`

Tests use `Constants.XXX` which now read from `SimulationConfig`. Since `Constants.Reset()` restores defaults, tests should continue to work. Add a `Constants.Reset()` call to test setup to ensure clean state.

- [ ] **Step 1: Add Constants.Reset() to test base setup**

If there's a shared test setup/fixture, add `Constants.Reset()` there. Otherwise, add it to each test class constructor or a shared helper.

Check if there's an existing test base class or xUnit fixture:

```csharp
// If tests use a constructor pattern:
public SomeTests()
{
    Block.ResetIdCounter();
    Constants.Reset(); // Add this
}
```

- [ ] **Step 2: Run all tests**

Run: `dotnet test blocker.sln`
Expected: All 140 tests pass. The compatibility accessors in Constants.cs mean no test code changes are needed beyond the Reset() call.

- [ ] **Step 3: Commit**

```bash
git add tests/
git commit -m "test: add Constants.Reset() to test setup for config isolation"
```

---

### Task 10: Final Build and Verification

- [ ] **Step 1: Full solution build**

Run: `dotnet build blocker.sln`
Expected: Build succeeded, 0 errors, 0 warnings

- [ ] **Step 2: Run all tests**

Run: `dotnet test blocker.sln`
Expected: All tests pass

- [ ] **Step 3: Verify no Godot imports in simulation**

Run: `grep -r "using Godot" src/Blocker.Simulation/`
Expected: No matches — simulation layer stays pure C#

- [ ] **Step 4: Verify GameConfig is visible in inspector**

Open Godot, open Main.tscn, select the Main node. The `Config` export should appear with all groups (Builder, Soldier, Stunner, Warden, Jumper, Wall, Economy, Combat, Towers, Push, Rays, Timing, Grid, Grid Colors, Global Colors, Visual Effects, Players).

Under Players, each palette should show per-unit color groups.

- [ ] **Step 5: Final commit**

```bash
git add -A
git commit -m "feat: centralized GameConfig with inspector-editable balance and visual settings"
```
