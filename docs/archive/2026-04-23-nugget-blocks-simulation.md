# Nugget Blocks — Plan A: Simulation Core

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement all nugget block simulation logic — data model, mining, consumption, capture, auto-rally, combat interactions, and nest integration — fully covered by xUnit tests.

**Architecture:** Nuggets are a new `BlockType.Nugget` with an isolated `NuggetState` component on the `Block` class. A new `NuggetSystem` handles mining progression, consumption, capture, and auto-rally in the tick pipeline. Existing combat systems are patched to handle nuggets correctly (skip in combat, stop stun rays, destroy via blast rays).

**Tech Stack:** Pure C# (.NET 8), xUnit tests. Zero Godot dependencies.

**Spec:** `docs/superpowers/specs/2026-04-23-nugget-blocks-design.md`

---

### Task 1: Data Model — Types and Block Extensions

**Files:**
- Modify: `src/Blocker.Simulation/Blocks/BlockType.cs`
- Create: `src/Blocker.Simulation/Blocks/NuggetType.cs`
- Create: `src/Blocker.Simulation/Blocks/NuggetState.cs`
- Modify: `src/Blocker.Simulation/Blocks/Block.cs`
- Modify: `src/Blocker.Simulation/Blocks/BlockState.cs` (if needed)

- [ ] **Step 1: Add Nugget to BlockType enum**

In `src/Blocker.Simulation/Blocks/BlockType.cs`:

```csharp
public enum BlockType
{
    Builder,
    Wall,
    Soldier,
    Stunner,
    Warden,
    Jumper,
    Nugget
}
```

- [ ] **Step 2: Create NuggetType enum**

Create `src/Blocker.Simulation/Blocks/NuggetType.cs`:

```csharp
namespace Blocker.Simulation.Blocks;

public enum NuggetType
{
    Standard
}
```

- [ ] **Step 3: Create NuggetState class**

Create `src/Blocker.Simulation/Blocks/NuggetState.cs`:

```csharp
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Blocks;

public class NuggetState
{
    public NuggetType Type { get; init; } = NuggetType.Standard;
    public bool IsMined { get; set; }
    public int MiningProgress { get; set; }
    public int? HealTargetId { get; set; }
    public GridPos? FortifyTargetPos { get; set; }
}
```

- [ ] **Step 4: Add nugget fields to Block class**

In `src/Blocker.Simulation/Blocks/Block.cs`, add:

```csharp
// Nugget state (only non-null for BlockType.Nugget)
public NuggetState? NuggetState { get; set; }

// Builder mining state — ID of nugget being mined
public int? MiningTargetId { get; set; }

// Wall fortification — stun ray hits before destruction (0 = normal wall)
public int FortifiedHp { get; set; }
```

Update `PopCost` switch to include Nugget:

```csharp
BlockType.Nugget => Constants.PopCostNugget,
```

Update `MoveInterval` switch to include Nugget:

```csharp
BlockType.Nugget => Constants.NuggetMoveInterval,
```

Update `IsImmobile` to account for unmined nuggets:

```csharp
public bool IsImmobile => Type == BlockType.Wall
    || (Type == BlockType.Nugget && NuggetState is { IsMined: false })
    || State != BlockState.Mobile;
```

- [ ] **Step 5: Build to verify no compile errors**

Run: `dotnet build src/Blocker.Simulation/`
Expected: Build succeeds (there will be missing constants — added in Task 2)

- [ ] **Step 6: Commit**

```bash
git add src/Blocker.Simulation/Blocks/
git commit -m "feat(sim): add Nugget BlockType, NuggetState, NuggetType, and Block extensions"
```

---

### Task 2: Constants and Configuration

**Files:**
- Modify: `src/Blocker.Simulation/Core/SimulationConfig.cs`
- Modify: `src/Blocker.Simulation/Core/Constants.cs`

- [ ] **Step 1: Add NuggetConfig record**

In `src/Blocker.Simulation/Core/SimulationConfig.cs`, add after `WallConfig`:

```csharp
public record NuggetConfig
{
    public int MiningTicks { get; init; } = 180;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 0;
    public int RefineRadius { get; init; } = 3;
    public int FortifiedWallHp { get; init; } = 3;
    public int FortifiedWallCount { get; init; } = 5;
    public int BuilderSpawnBonus { get; init; } = 100;
    public int SoldierSpawnBonus { get; init; } = 100;
    public int StunnerSpawnBonus { get; init; } = 100;
    public int WardenSpawnBonus { get; init; } = 100;
    public int JumperSpawnBonus { get; init; } = 100;
    public bool BuilderRequired { get; init; } = false;
    public bool SoldierRequired { get; init; } = false;
    public bool StunnerRequired { get; init; } = false;
    public bool WardenRequired { get; init; } = false;
    public bool JumperRequired { get; init; } = false;
}
```

Add to `SimulationConfig`:

```csharp
public NuggetConfig Nugget { get; init; } = new();
```

Add to `SimulationConfig.GetPopCost`:

```csharp
Blocks.BlockType.Nugget => Nugget.PopCost,
```

Add to `SimulationConfig.GetMoveInterval`:

```csharp
Blocks.BlockType.Nugget => Nugget.MoveInterval,
```

- [ ] **Step 2: Add Constants accessors**

In `src/Blocker.Simulation/Core/Constants.cs`, add:

```csharp
// Nugget
public static int NuggetMiningTicks => _config.Nugget.MiningTicks;
public static int NuggetMoveInterval => _config.Nugget.MoveInterval;
public static int PopCostNugget => _config.Nugget.PopCost;
public static int NuggetRefineRadius => _config.Nugget.RefineRadius;
public static int FortifiedWallHp => _config.Nugget.FortifiedWallHp;
public static int FortifiedWallCount => _config.Nugget.FortifiedWallCount;
```

- [ ] **Step 3: Add helper methods for per-unit-type nugget config**

In `SimulationConfig`, add:

```csharp
public int GetNuggetSpawnBonus(Blocks.BlockType unitType) => unitType switch
{
    Blocks.BlockType.Builder => Nugget.BuilderSpawnBonus,
    Blocks.BlockType.Soldier => Nugget.SoldierSpawnBonus,
    Blocks.BlockType.Stunner => Nugget.StunnerSpawnBonus,
    Blocks.BlockType.Warden => Nugget.WardenSpawnBonus,
    Blocks.BlockType.Jumper => Nugget.JumperSpawnBonus,
    _ => 0
};

public bool GetNuggetRequired(Blocks.BlockType unitType) => unitType switch
{
    Blocks.BlockType.Builder => Nugget.BuilderRequired,
    Blocks.BlockType.Soldier => Nugget.SoldierRequired,
    Blocks.BlockType.Stunner => Nugget.StunnerRequired,
    Blocks.BlockType.Warden => Nugget.WardenRequired,
    Blocks.BlockType.Jumper => Nugget.JumperRequired,
    _ => false
};
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build`
Expected: Build succeeds

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Core/SimulationConfig.cs src/Blocker.Simulation/Core/Constants.cs
git commit -m "feat(sim): add NuggetConfig with mining, refine, and fortification constants"
```

---

### Task 3: GameState Core Changes

**Files:**
- Modify: `src/Blocker.Simulation/Core/GameState.cs`
- Create: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write test — AddBlock creates nugget with correct defaults**

Create `tests/Blocker.Simulation.Tests/NuggetTests.cs`:

```csharp
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class NuggetTests
{
    public NuggetTests()
    {
        Constants.Reset();
    }

    private GameState CreateState(int width = 20, int height = 20)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0, MaxPopulation = 50 });
        state.Players.Add(new Player { Id = 1, TeamId = 1, MaxPopulation = 50 });
        return state;
    }

    [Fact]
    public void AddNugget_CreatesUnminedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));

        Assert.Equal(BlockType.Nugget, nugget.Type);
        Assert.Equal(-1, nugget.PlayerId);
        Assert.NotNull(nugget.NuggetState);
        Assert.False(nugget.NuggetState!.IsMined);
        Assert.Equal(0, nugget.NuggetState.MiningProgress);
        Assert.True(nugget.IsImmobile);
    }

    [Fact]
    public void MinedNugget_IsMobile()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState = new NuggetState { IsMined = true };

        Assert.False(nugget.IsImmobile);
    }

    [Fact]
    public void Nugget_HasZeroPopCost()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));

        Assert.Equal(0, nugget.PopCost);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "NuggetTests"`
Expected: Fail — `AddBlock` doesn't handle Nugget type yet

- [ ] **Step 3: Update GameState.AddBlock for Nugget**

In `src/Blocker.Simulation/Core/GameState.cs`, modify `AddBlock`:

```csharp
public Block AddBlock(BlockType type, int playerId, GridPos pos)
{
    var block = new Block
    {
        Id = _nextBlockId++,
        Type = type,
        PlayerId = playerId,
        Pos = pos,
        PrevPos = pos,
        State = type == BlockType.Wall ? BlockState.Rooted : BlockState.Mobile,
        RootProgress = type == BlockType.Wall ? Constants.RootTicks : 0,
        Hp = type switch
        {
            BlockType.Soldier => Constants.SoldierMaxHp,
            BlockType.Jumper => Constants.JumperMaxHp,
            _ => 0
        },
        NuggetState = type == BlockType.Nugget ? new NuggetState() : null
    };
    Blocks.Add(block);
    _blockById[block.Id] = block;
    Grid[pos].BlockId = block.Id;
    return block;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "NuggetTests"`
Expected: All 3 pass

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Core/GameState.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): GameState.AddBlock creates nuggets with NuggetState; initial tests"
```

---

### Task 4: VisualEvents for Nuggets

**Files:**
- Modify: `src/Blocker.Simulation/Core/VisualEvent.cs`

- [ ] **Step 1: Add nugget visual event types**

In `src/Blocker.Simulation/Core/VisualEvent.cs`, add to the enum:

```csharp
// Nugget events
NuggetMiningStarted,
NuggetFreed,
NuggetCaptured,
NuggetRefineConsumed,
NuggetHealConsumed,
NuggetFortifyConsumed,

// Command-issued (nugget)
CommandMineIssued,
CommandHealIssued,
CommandFortifyIssued,
```

- [ ] **Step 2: Build to verify**

Run: `dotnet build`
Expected: Build succeeds

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Simulation/Core/VisualEvent.cs
git commit -m "feat(sim): add nugget visual event types"
```

---

### Task 5: Combat System Exclusions — PlayerId=-1 Audit

This task patches every system that interacts with blocks via `AreEnemies`, combat, or movement to handle nuggets correctly. Each patch has a test.

**Files:**
- Modify: `src/Blocker.Simulation/Systems/CombatSystem.cs`
- Modify: `src/Blocker.Simulation/Systems/EliminationSystem.cs`
- Modify: `src/Blocker.Simulation/Systems/StunSystem.cs`
- Modify: `src/Blocker.Simulation/Systems/JumperSystem.cs`
- Modify: `src/Blocker.Simulation/Core/GameState.cs` (attack-move)
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write test — nuggets immune to surrounding**

In `NuggetTests.cs`:

```csharp
[Fact]
public void CombatSystem_SkipsNuggets_NotKilledBySurrounding()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;

    // 3 enemy soldiers orthogonal — would kill any other block
    state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
    state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5));
    state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6));

    CombatSystem.Tick(state);

    Assert.NotNull(state.GetBlock(nugget.Id));
}
```

- [ ] **Step 2: Run test — verify it fails**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "CombatSystem_SkipsNuggets"`
Expected: Fail — nugget gets killed

- [ ] **Step 3: Patch CombatSystem to skip nuggets**

In `CombatSystem.cs`, add skip at start of Pass 1 and Pass 2 loops:

```csharp
if (block.Type == BlockType.Nugget) continue;
```

Add at the top of `ShouldDieFromSurrounding`:
```csharp
if (target.Type == BlockType.Nugget) return false;
```

Add at the top of `GetSoldiersNeededToKill`:
```csharp
if (target.Type == BlockType.Nugget) return 0;
```

- [ ] **Step 4: Run test — verify it passes**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "CombatSystem_SkipsNuggets"`
Expected: Pass

- [ ] **Step 5: Write test — nuggets excluded from elimination counts**

```csharp
[Fact]
public void EliminationSystem_IgnoresNuggets()
{
    var state = CreateState();
    // Player 0 has only a mined nugget — should still be eliminated
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;

    EliminationSystem.Tick(state);

    Assert.True(state.Players[0].IsEliminated);
}
```

- [ ] **Step 6: Patch EliminationSystem**

In `EliminationSystem.Tick`, add skip in the block-counting loop:

```csharp
if (block.Type == BlockType.Nugget) continue;
```

- [ ] **Step 7: Run test — verify pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "EliminationSystem_IgnoresNuggets"`
Expected: Pass

- [ ] **Step 8: Write test — stun ray stops at nugget without destroying it**

```csharp
[Fact]
public void StunRay_StopsAtNugget_DoesNotDestroy()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));

    // Fire stun ray toward nugget
    var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
    StunSystem.FireStunRay(state, stunner, Direction.Right);

    // Advance rays until they reach the nugget
    for (int i = 0; i < 20; i++)
        StunSystem.Tick(state);

    Assert.NotNull(state.GetBlock(nugget.Id));
    Assert.Equal(0, nugget.StunTimer);
    // Rays should be expired (stopped at nugget)
    Assert.All(state.Rays, r => Assert.True(r.IsExpired));
}
```

- [ ] **Step 9: Patch StunSystem.TryHitAt for nuggets**

In `StunSystem.TryHitAt`, add before the friendly check:

```csharp
// Nuggets block rays but are not affected
if (block.Type == BlockType.Nugget)
    return true; // Ray stops, nugget unharmed
```

- [ ] **Step 10: Run test — verify pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "StunRay_StopsAtNugget"`
Expected: Pass

- [ ] **Step 11: Write test — stun ray decrements fortified wall HP**

```csharp
[Fact]
public void StunRay_DecrementsFortifiedWallHp()
{
    var state = CreateState();
    var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
    wall.FortifiedHp = 3;

    var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
    StunSystem.FireStunRay(state, stunner, Direction.Right);

    for (int i = 0; i < 20; i++)
        StunSystem.Tick(state);

    Assert.NotNull(state.GetBlock(wall.Id));
    Assert.Equal(2, wall.FortifiedHp);
}

[Fact]
public void StunRay_DestroysWallWhenFortificationDepleted()
{
    var state = CreateState();
    var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
    wall.FortifiedHp = 0; // Normal wall

    var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
    StunSystem.FireStunRay(state, stunner, Direction.Right);

    for (int i = 0; i < 20; i++)
        StunSystem.Tick(state);

    Assert.Null(state.GetBlock(wall.Id));
}
```

- [ ] **Step 12: Patch StunSystem.TryHitAt for fortified walls**

In the `RayType.Stun` case for `block.Type == BlockType.Wall`, add fortification check before destroying:

```csharp
if (block.Type == BlockType.Wall)
{
    if (block.FortifiedHp > 0)
    {
        block.FortifiedHp--;
        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
        return true; // Ray stops, wall survives
    }
    // Normal wall destruction
    state.VisualEvents.Add(new VisualEvent(
        VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
    state.VisualEvents.Add(new VisualEvent(
        VisualEventType.BlockDied, ray.HeadPos, block.PlayerId, BlockId: block.Id));
    state.RemoveBlock(block);
    return true;
}
```

- [ ] **Step 13: Run fortified wall tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FortifiedWall"`
Expected: Both pass

- [ ] **Step 14: Write test — jumper stops at unmined nugget, destroys mined nugget**

```csharp
[Fact]
public void Jumper_StopsAtUnminedNugget()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));
    var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

    JumperSystem.Jump(state, jumper, Direction.Right);

    // Jumper should have stopped before the nugget (at 6,5)
    Assert.Equal(new GridPos(6, 5), jumper.Pos);
    Assert.NotNull(state.GetBlock(nugget.Id));
}

[Fact]
public void Jumper_DestroysMined_Nugget()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, 1, new GridPos(7, 5));
    nugget.NuggetState!.IsMined = true;

    var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

    JumperSystem.Jump(state, jumper, Direction.Right);

    Assert.Null(state.GetBlock(nugget.Id));
}
```

- [ ] **Step 15: Patch JumperSystem.Jump for nuggets**

In `JumperSystem.Jump`, in the block-in-path check, add nugget handling before the existing immobile/wall checks:

```csharp
if (blockAtPos != null)
{
    // Unmined nuggets are obstacles — stop the jump
    if (blockAtPos.Type == BlockType.Nugget && blockAtPos.NuggetState is { IsMined: false })
    {
        hitObstacle = true;
        break;
    }

    // Walls, formations, rooted blocks stop the jump
    if (blockAtPos.Type == BlockType.Wall || blockAtPos.IsInFormation || blockAtPos.IsImmobile)
    {
        hitObstacle = true;
        break;
    }

    // Kill all mobile blocks in path (friendlies too) — includes mined nuggets
    state.RemoveBlock(blockAtPos);
    kills++;
    state.VisualEvents.Add(new VisualEvent(
        VisualEventType.BlockDied, nextPos, blockAtPos.PlayerId,
        BlockId: blockAtPos.Id));
}
```

- [ ] **Step 16: Run jumper tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "Jumper_StopsAtUnmined|Jumper_DestroysMined_Nugget"`
Expected: Both pass

- [ ] **Step 17: Patch attack-move to ignore nuggets**

In `GameState.cs` step 12 (attack-move pause), add nugget check:

```csharp
if (other != null && AreEnemies(other, block) && other.Type != BlockType.Wall && other.Type != BlockType.Nugget)
```

- [ ] **Step 18: Run full test suite to verify no regressions**

Run: `dotnet test`
Expected: All tests pass

- [ ] **Step 19: Commit**

```bash
git add src/Blocker.Simulation/Systems/ src/Blocker.Simulation/Core/GameState.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): combat exclusions for nuggets — stun ray stops, blast destroys, fortified walls"
```

---

### Task 6: NuggetSystem — Mining

**Files:**
- Create: `src/Blocker.Simulation/Systems/NuggetSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write mining tests**

```csharp
[Fact]
public void Mining_OneBuilder_AdvancesProgress()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
    builder.MiningTargetId = nugget.Id;
    nugget.PlayerId = 0; // Mining has started

    NuggetSystem.Tick(state);

    Assert.Equal(1, nugget.NuggetState!.MiningProgress);
}

[Fact]
public void Mining_TwoBuilders_AdvancesFaster()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
    var b2 = state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5));
    b1.MiningTargetId = nugget.Id;
    b2.MiningTargetId = nugget.Id;
    nugget.PlayerId = 0;

    NuggetSystem.Tick(state);

    Assert.Equal(2, nugget.NuggetState!.MiningProgress);
}

[Fact]
public void Mining_NonAdjacentBuilder_DoesNotCount()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 3)); // 2 cells away
    builder.MiningTargetId = nugget.Id;
    nugget.PlayerId = 0;

    NuggetSystem.Tick(state);

    Assert.Equal(0, nugget.NuggetState!.MiningProgress);
}

[Fact]
public void Mining_CompletesAndFreesNugget()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    nugget.NuggetState!.MiningProgress = Constants.NuggetMiningTicks - 1;
    nugget.PlayerId = 0;

    var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
    builder.MiningTargetId = nugget.Id;

    NuggetSystem.Tick(state);

    Assert.True(nugget.NuggetState.IsMined);
    Assert.Equal(0, nugget.PlayerId);
    Assert.False(nugget.IsImmobile);
}

[Fact]
public void Mining_ExclusiveToOneTeam()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    nugget.PlayerId = 0; // Team 0 started mining

    // Team 0 builder mining
    var b0 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
    b0.MiningTargetId = nugget.Id;

    // Team 1 builder also trying to mine — should not count
    var b1 = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
    b1.MiningTargetId = nugget.Id;

    NuggetSystem.Tick(state);

    // Only team 0's builder should count
    Assert.Equal(1, nugget.NuggetState!.MiningProgress);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "Mining_"`
Expected: Fail — NuggetSystem doesn't exist yet

- [ ] **Step 3: Create NuggetSystem with mining logic**

Create `src/Blocker.Simulation/Systems/NuggetSystem.cs`:

```csharp
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class NuggetSystem
{
    public static void Tick(GameState state)
    {
        TickMining(state);
        TickConsumption(state);
        TickCapture(state);
        TickAutoRally(state);
    }

    private static void TickMining(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: false }) continue;
            if (block.PlayerId == -1) continue; // No one is mining

            // Count adjacent builders with MiningTargetId pointing to this nugget
            // Only count builders from the nugget's current PlayerId (exclusive mining)
            int minerCount = 0;
            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = state.GetBlockAt(block.Pos + offset);
                if (neighbor != null
                    && neighbor.Type == BlockType.Builder
                    && neighbor.MiningTargetId == block.Id
                    && neighbor.PlayerId == block.PlayerId)
                {
                    minerCount++;
                }
            }

            if (minerCount == 0) continue;

            block.NuggetState.MiningProgress += minerCount;

            if (block.NuggetState.MiningProgress >= Constants.NuggetMiningTicks)
            {
                block.NuggetState.IsMined = true;
                block.NuggetState.MiningProgress = Constants.NuggetMiningTicks;

                // Clear mining targets on all builders that were mining this nugget
                foreach (var b in state.Blocks)
                {
                    if (b.MiningTargetId == block.Id)
                        b.MiningTargetId = null;
                }

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.NuggetFreed, block.Pos, block.PlayerId, BlockId: block.Id));

                // Set auto-rally target
                SetAutoRallyTarget(state, block);
            }
        }
    }

    private static void TickConsumption(GameState state)
    {
        // Implemented in Task 8-9
    }

    private static void TickCapture(GameState state)
    {
        // Implemented in Task 7
    }

    private static void TickAutoRally(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: true }) continue;
            if (block.MoveTarget.HasValue) continue;
            if (block.NuggetState.HealTargetId.HasValue) continue;
            if (block.NuggetState.FortifyTargetPos.HasValue) continue;

            SetAutoRallyTarget(state, block);
        }
    }

    private static void SetAutoRallyTarget(GameState state, Block nugget)
    {
        GridPos? nearest = null;
        int bestDist = int.MaxValue;
        int bestNestId = int.MaxValue;

        foreach (var nest in state.Nests)
        {
            if (nest.PlayerId != nugget.PlayerId) continue;
            int dist = nugget.Pos.ManhattanDistance(nest.Center);
            if (dist < bestDist || (dist == bestDist && nest.Id < bestNestId))
            {
                bestDist = dist;
                bestNestId = nest.Id;
                nearest = nest.Center;
            }
        }

        if (nearest.HasValue)
            nugget.MoveTarget = nearest.Value;
    }
}
```

- [ ] **Step 4: Run mining tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "Mining_"`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Systems/NuggetSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): NuggetSystem mining mechanics — progress, exclusivity, freed transition"
```

---

### Task 7: NuggetSystem — Capture

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write capture tests**

```csharp
[Fact]
public void Capture_EnemyBuilderAdjacent_FlipsOwnership()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;

    state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4)); // Enemy builder adjacent

    NuggetSystem.Tick(state);

    Assert.Equal(1, nugget.PlayerId);
}

[Fact]
public void Capture_Contested_DoesNotFlip()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;

    state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4)); // Enemy builder
    state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5)); // Friendly builder — contested

    NuggetSystem.Tick(state);

    Assert.Equal(0, nugget.PlayerId); // Still owned by player 0
}

[Fact]
public void Capture_NonBuilderAdjacent_DoesNotCapture()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
    nugget.NuggetState!.IsMined = true;

    state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4)); // Enemy soldier, not builder

    NuggetSystem.Tick(state);

    Assert.Equal(0, nugget.PlayerId);
}
```

- [ ] **Step 2: Implement TickCapture**

In `NuggetSystem.cs`, replace the empty `TickCapture`:

```csharp
private static void TickCapture(GameState state)
{
    foreach (var block in state.Blocks)
    {
        if (block.Type != BlockType.Nugget) continue;
        if (block.NuggetState is not { IsMined: true }) continue;

        bool hasEnemyBuilder = false;
        int enemyPlayerId = -1;
        bool hasFriendlyBuilder = false;

        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var neighbor = state.GetBlockAt(block.Pos + offset);
            if (neighbor == null || neighbor.Type != BlockType.Builder) continue;

            if (state.AreEnemies(neighbor.PlayerId, block.PlayerId))
            {
                hasEnemyBuilder = true;
                enemyPlayerId = neighbor.PlayerId;
            }
            else
            {
                hasFriendlyBuilder = true;
            }
        }

        if (hasEnemyBuilder && !hasFriendlyBuilder)
        {
            block.PlayerId = enemyPlayerId;
            block.MoveTarget = null;
            block.NuggetState.HealTargetId = null;
            block.NuggetState.FortifyTargetPos = null;

            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.NuggetCaptured, block.Pos, enemyPlayerId, BlockId: block.Id));

            SetAutoRallyTarget(state, block);
        }
    }
}
```

- [ ] **Step 3: Run capture tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "Capture_"`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Simulation/Systems/NuggetSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): nugget capture — instant flip by enemy builder, contest blocking"
```

---

### Task 8: NuggetSystem — Nest Refine Consumption

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs`
- Modify: `src/Blocker.Simulation/Core/Nest.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Add NuggetLoaded to Nest**

In `src/Blocker.Simulation/Core/Nest.cs`, add:

```csharp
public bool NuggetLoaded { get; set; }
```

- [ ] **Step 2: Write refine tests**

```csharp
private Block AddRootedBlock(GameState state, BlockType type, int playerId, GridPos pos)
{
    var block = state.AddBlock(type, playerId, pos);
    block.State = BlockState.Rooted;
    block.RootProgress = Constants.RootTicks;
    return block;
}

private Nest SetupBuilderNest(GameState state, int playerId, GridPos center)
{
    state.Grid[center].Ground = GroundType.Boot;
    AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(0, -1));
    AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(1, 0));
    AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(0, 1));
    NestSystem.DetectNests(state);
    return state.Nests[0];
}

[Fact]
public void NestRefine_NuggetWithinRadius_Consumed()
{
    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

    // Place mined nugget within 3 Chebyshev distance
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(12, 10));
    nugget.NuggetState!.IsMined = true;

    NuggetSystem.Tick(state);

    Assert.Null(state.GetBlock(nugget.Id)); // Consumed
}

[Fact]
public void NestRefine_NuggetOutsideRadius_NotConsumed()
{
    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

    // Place mined nugget beyond radius
    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(14, 10)); // 4 distance
    nugget.NuggetState!.IsMined = true;

    NuggetSystem.Tick(state);

    Assert.NotNull(state.GetBlock(nugget.Id)); // Not consumed
}

[Fact]
public void NestRefine_AppliesSpawnBonus()
{
    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));
    nest.SpawnProgress = 0;

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(12, 10));
    nugget.NuggetState!.IsMined = true;

    NuggetSystem.Tick(state);

    // Default bonus is 100% = full spawn ticks added
    Assert.True(nest.SpawnProgress > 0);
}
```

- [ ] **Step 3: Implement TickConsumption — nest refine**

Replace `TickConsumption` in `NuggetSystem.cs`:

```csharp
private static void TickConsumption(GameState state)
{
    var toRemove = new List<int>();

    foreach (var block in state.Blocks)
    {
        if (block.Type != BlockType.Nugget) continue;
        if (block.NuggetState is not { IsMined: true }) continue;
        if (toRemove.Contains(block.Id)) continue;

        // Priority 1: Heal target reached
        if (TryConsumeHeal(state, block))
        {
            toRemove.Add(block.Id);
            continue;
        }

        // Priority 2: Fortify target reached
        if (TryConsumeFortify(state, block))
        {
            toRemove.Add(block.Id);
            continue;
        }

        // Priority 3: Near a friendly nest — auto-refine
        if (TryConsumeNestRefine(state, block))
        {
            toRemove.Add(block.Id);
            continue;
        }
    }

    foreach (var id in toRemove)
    {
        var block = state.GetBlock(id);
        if (block != null) state.RemoveBlock(block);
    }
}

private static bool TryConsumeNestRefine(GameState state, Block nugget)
{
    foreach (var nest in state.Nests)
    {
        if (nest.PlayerId != nugget.PlayerId) continue;

        int chebyshev = Math.Max(
            Math.Abs(nugget.Pos.X - nest.Center.X),
            Math.Abs(nugget.Pos.Y - nest.Center.Y));

        if (chebyshev > Constants.NuggetRefineRadius) continue;

        // Apply refine effect
        var ground = state.Grid[nest.Center].Ground;
        var spawnType = nest.GetSpawnBlockType(ground);
        int bonus = Constants.Config.GetNuggetSpawnBonus(spawnType);

        if (bonus > 0)
        {
            int spawnTicks = nest.GetSpawnTicks(ground);
            nest.SpawnProgress += spawnTicks * bonus / 100;
        }

        if (Constants.Config.GetNuggetRequired(spawnType))
            nest.NuggetLoaded = true;

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.NuggetRefineConsumed, nugget.Pos, nugget.PlayerId, BlockId: nugget.Id));

        return true;
    }

    return false;
}

private static bool TryConsumeHeal(GameState state, Block nugget)
{
    // Implemented in Task 9
    return false;
}

private static bool TryConsumeFortify(GameState state, Block nugget)
{
    // Implemented in Task 9
    return false;
}
```

- [ ] **Step 4: Run refine tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "NestRefine_"`
Expected: All pass

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Systems/NuggetSystem.cs src/Blocker.Simulation/Core/Nest.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): nugget nest refine consumption — radius check, spawn bonus application"
```

---

### Task 9: NuggetSystem — Heal and Fortify Consumption

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NuggetSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write heal tests**

```csharp
[Fact]
public void Heal_NuggetAdjacentToTarget_HealsToFull()
{
    var state = CreateState();
    var soldier = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
    soldier.Hp = 1; // Damaged

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.HealTargetId = soldier.Id;

    NuggetSystem.Tick(state);

    Assert.Equal(Constants.SoldierMaxHp, soldier.Hp);
    Assert.Null(state.GetBlock(nugget.Id)); // Consumed
}

[Fact]
public void Heal_NuggetNotAdjacent_DoesNotHeal()
{
    var state = CreateState();
    var soldier = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
    soldier.Hp = 1;

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 3)); // 2 cells away
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.HealTargetId = soldier.Id;

    NuggetSystem.Tick(state);

    Assert.Equal(1, soldier.Hp);
    Assert.NotNull(state.GetBlock(nugget.Id)); // Not consumed
}
```

- [ ] **Step 2: Write fortify tests**

```csharp
[Fact]
public void Fortify_NuggetAdjacentToWall_FortifiesFiveWalls()
{
    var state = CreateState();
    // Line of 6 walls
    for (int x = 3; x <= 8; x++)
        state.AddBlock(BlockType.Wall, 0, new GridPos(x, 5));

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.FortifyTargetPos = new GridPos(5, 5);

    NuggetSystem.Tick(state);

    Assert.Null(state.GetBlock(nugget.Id)); // Consumed

    // 5 walls should be fortified (target + 4 BFS)
    int fortifiedCount = state.Blocks.Count(b => b.Type == BlockType.Wall && b.FortifiedHp > 0);
    Assert.Equal(5, fortifiedCount);
}

[Fact]
public void Fortify_SetsCorrectHp()
{
    var state = CreateState();
    var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(5, 5));

    var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
    nugget.NuggetState!.IsMined = true;
    nugget.NuggetState.FortifyTargetPos = new GridPos(5, 5);

    NuggetSystem.Tick(state);

    Assert.Equal(Constants.FortifiedWallHp, wall.FortifiedHp);
}
```

- [ ] **Step 3: Implement TryConsumeHeal**

```csharp
private static bool TryConsumeHeal(GameState state, Block nugget)
{
    if (!nugget.NuggetState!.HealTargetId.HasValue) return false;

    var target = state.GetBlock(nugget.NuggetState.HealTargetId.Value);
    if (target == null)
    {
        nugget.NuggetState.HealTargetId = null;
        return false;
    }

    // Check adjacency
    bool adjacent = false;
    foreach (var offset in GridPos.OrthogonalOffsets)
    {
        if (nugget.Pos + offset == target.Pos)
        {
            adjacent = true;
            break;
        }
    }

    if (!adjacent) return false;

    // Heal to full HP
    int maxHp = target.Type switch
    {
        BlockType.Soldier => Constants.SoldierMaxHp,
        BlockType.Jumper => Constants.JumperMaxHp,
        _ => 0
    };

    if (maxHp > 0)
        target.Hp = maxHp;

    state.VisualEvents.Add(new VisualEvent(
        VisualEventType.NuggetHealConsumed, target.Pos, nugget.PlayerId, BlockId: nugget.Id));

    return true;
}
```

- [ ] **Step 4: Implement TryConsumeFortify**

```csharp
private static bool TryConsumeFortify(GameState state, Block nugget)
{
    if (!nugget.NuggetState!.FortifyTargetPos.HasValue) return false;

    var targetPos = nugget.NuggetState.FortifyTargetPos.Value;
    var targetWall = state.GetBlockAt(targetPos);
    if (targetWall == null || targetWall.Type != BlockType.Wall)
    {
        nugget.NuggetState.FortifyTargetPos = null;
        return false;
    }

    // Check adjacency to target wall
    bool adjacent = false;
    foreach (var offset in GridPos.OrthogonalOffsets)
    {
        if (nugget.Pos + offset == targetPos)
        {
            adjacent = true;
            break;
        }
    }

    if (!adjacent) return false;

    // BFS from target wall to find up to FortifiedWallCount connected walls
    var fortifyTargets = new List<Block> { targetWall };
    var visited = new HashSet<GridPos> { targetPos };
    var queue = new Queue<GridPos>();
    queue.Enqueue(targetPos);

    while (queue.Count > 0 && fortifyTargets.Count < Constants.FortifiedWallCount)
    {
        var pos = queue.Dequeue();
        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var neighbor = pos + offset;
            if (!visited.Add(neighbor)) continue;
            var wall = state.GetBlockAt(neighbor);
            if (wall == null || wall.Type != BlockType.Wall) continue;
            if (wall.PlayerId != nugget.PlayerId) continue;
            fortifyTargets.Add(wall);
            if (fortifyTargets.Count >= Constants.FortifiedWallCount) break;
            queue.Enqueue(neighbor);
        }
    }

    foreach (var wall in fortifyTargets)
        wall.FortifiedHp = Constants.FortifiedWallHp;

    state.VisualEvents.Add(new VisualEvent(
        VisualEventType.NuggetFortifyConsumed, targetPos, nugget.PlayerId, BlockId: nugget.Id));

    return true;
}
```

- [ ] **Step 5: Run heal and fortify tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "Heal_|Fortify_"`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/Blocker.Simulation/Systems/NuggetSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): nugget heal and fortify consumption — BFS wall selection, full HP restore"
```

---

### Task 10: Commands — MineNugget, HealWithNugget, FortifyWithNugget

**Files:**
- Modify: `src/Blocker.Simulation/Commands/Command.cs`
- Modify: `src/Blocker.Simulation/Core/GameState.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Add command types**

In `Command.cs`, add to `CommandType`:

```csharp
MineNugget,       // Builder mines an unmined nugget
HealWithNugget,   // Nugget heals a damaged soldier/jumper
FortifyWithNugget, // Nugget fortifies walls
```

- [ ] **Step 2: Write command test**

```csharp
[Fact]
public void MineCommand_BuilderMovesToNuggetAndMines()
{
    var state = CreateState();
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
    var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4)); // Already adjacent

    var cmd = new Command(0, CommandType.MineNugget, [builder.Id], TargetPos: new GridPos(5, 5));
    state.ProcessCommands([cmd]);

    Assert.Equal(nugget.Id, builder.MiningTargetId);
    Assert.Equal(0, nugget.PlayerId); // Ownership assigned
}
```

- [ ] **Step 3: Add command handling to GameState**

In `GameState.ExecuteCommand`, add cases:

```csharp
case CommandType.MineNugget:
    if (block.Type == BlockType.Builder && cmd.TargetPos.HasValue)
    {
        var targetNugget = GetBlockAt(cmd.TargetPos.Value);
        if (targetNugget != null && targetNugget.Type == BlockType.Nugget
            && targetNugget.NuggetState is { IsMined: false })
        {
            // Check exclusive mining — only allow if no other team is actively mining
            bool otherTeamMining = false;
            if (targetNugget.PlayerId != -1 && targetNugget.PlayerId != block.PlayerId)
            {
                foreach (var b in Blocks)
                {
                    if (b.MiningTargetId == targetNugget.Id && b.PlayerId != block.PlayerId)
                    {
                        otherTeamMining = true;
                        break;
                    }
                }
            }

            if (!otherTeamMining)
            {
                // Move to adjacent cell if not already adjacent
                bool isAdjacent = false;
                foreach (var offset in GridPos.OrthogonalOffsets)
                {
                    if (block.Pos == cmd.TargetPos.Value + offset)
                    {
                        isAdjacent = true;
                        break;
                    }
                }

                if (!isAdjacent)
                    block.MoveTarget = cmd.TargetPos.Value;

                block.MiningTargetId = targetNugget.Id;
                targetNugget.PlayerId = block.PlayerId;
            }
        }
    }
    break;

case CommandType.HealWithNugget:
    if (block.Type == BlockType.Nugget && block.NuggetState is { IsMined: true }
        && cmd.TargetPos.HasValue)
    {
        var healTarget = GetBlockAt(cmd.TargetPos.Value);
        if (healTarget != null
            && healTarget.Type is BlockType.Soldier or BlockType.Jumper
            && healTarget.PlayerId == block.PlayerId)
        {
            block.NuggetState.HealTargetId = healTarget.Id;
            block.NuggetState.FortifyTargetPos = null;
            block.MoveTarget = cmd.TargetPos.Value;
        }
    }
    break;

case CommandType.FortifyWithNugget:
    if (block.Type == BlockType.Nugget && block.NuggetState is { IsMined: true }
        && cmd.TargetPos.HasValue)
    {
        var fortifyTarget = GetBlockAt(cmd.TargetPos.Value);
        if (fortifyTarget != null
            && fortifyTarget.Type == BlockType.Wall
            && fortifyTarget.PlayerId == block.PlayerId)
        {
            block.NuggetState.FortifyTargetPos = cmd.TargetPos.Value;
            block.NuggetState.HealTargetId = null;
            block.MoveTarget = cmd.TargetPos.Value;
        }
    }
    break;
```

Also add to `TryExecuteCommand`:

```csharp
case CommandType.MineNugget:
case CommandType.HealWithNugget:
case CommandType.FortifyWithNugget:
    ExecuteCommand(block, cmd);
    return true;
```

- [ ] **Step 4: Clear MiningTargetId when builder gets a new command**

In `ExecuteCommand`, at the start of `CommandType.Move`:

```csharp
case CommandType.Move:
    block.MiningTargetId = null; // Cancel mining if moving elsewhere
    if (cmd.TargetPos.HasValue && block.IsMobile)
    // ... existing code
```

Also clear in the immediate-command path at the top of `ProcessCommands` — when `CommandQueue.Clear()` is called and a new command replaces the current one, `MiningTargetId` should be cleared for builders:

After `block.CommandQueue.Clear();`:
```csharp
if (block.Type == BlockType.Builder)
    block.MiningTargetId = null;
```

- [ ] **Step 5: Run command tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "MineCommand_"`
Expected: Pass

- [ ] **Step 6: Run full test suite**

Run: `dotnet test`
Expected: All pass

- [ ] **Step 7: Commit**

```bash
git add src/Blocker.Simulation/Commands/Command.cs src/Blocker.Simulation/Core/GameState.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): nugget commands — MineNugget, HealWithNugget, FortifyWithNugget"
```

---

### Task 11: NestSystem — Nugget Required and Spawn Bonus Integration

**Files:**
- Modify: `src/Blocker.Simulation/Systems/NestSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Write test — NuggetRequired blocks spawning**

```csharp
[Fact]
public void NestSystem_NuggetRequired_BlocksSpawnWithoutNugget()
{
    Constants.Initialize(new SimulationConfig
    {
        Nugget = new NuggetConfig { BuilderRequired = true }
    });

    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

    // Advance spawn progress to threshold
    var ground = state.Grid[nest.Center].Ground;
    nest.SpawnProgress = nest.GetSpawnTicks(ground) - 1;

    NestSystem.TickSpawning(state);

    // Should NOT spawn — no nugget loaded
    int blockCount = state.Blocks.Count(b => b.Type == BlockType.Builder && b.PlayerId == 0 && !b.IsInFormation);
    Assert.Equal(0, blockCount);
    // Progress should hold at threshold
    Assert.Equal(nest.GetSpawnTicks(ground), nest.SpawnProgress);
}

[Fact]
public void NestSystem_NuggetRequired_SpawnsWithNuggetLoaded()
{
    Constants.Initialize(new SimulationConfig
    {
        Nugget = new NuggetConfig { BuilderRequired = true }
    });

    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));
    nest.NuggetLoaded = true;

    var ground = state.Grid[nest.Center].Ground;
    nest.SpawnProgress = nest.GetSpawnTicks(ground) - 1;

    NestSystem.TickSpawning(state);

    // Should spawn
    int blockCount = state.Blocks.Count(b => b.Type == BlockType.Builder && b.PlayerId == 0 && !b.IsInFormation);
    Assert.Equal(1, blockCount);
    Assert.False(nest.NuggetLoaded); // Consumed
}
```

- [ ] **Step 2: Patch NestSystem.TickSpawning**

In `NestSystem.TickSpawning`, after the `SpawnDisabled` check and before `FindSpawnCell`, add:

```csharp
// Nugget-required check
if (Constants.Config.GetNuggetRequired(spawnType) && !nest.NuggetLoaded)
{
    nest.SpawnProgress = spawnTicks;
    continue;
}
```

After spawning succeeds (after `nest.SpawnProgress = 0;`), add:

```csharp
nest.NuggetLoaded = false;
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "NestSystem_Nugget"`
Expected: All pass

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Simulation/Systems/NestSystem.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): NestSystem nugget-required gating and spawn bonus integration"
```

---

### Task 12: Tick Integration, StateHasher, CommandSerializer

**Files:**
- Modify: `src/Blocker.Simulation/Core/GameState.cs`
- Modify: `src/Blocker.Simulation/Net/StateHasher.cs`
- Modify: `src/Blocker.Simulation/Net/CommandSerializer.cs`
- Modify: `tests/Blocker.Simulation.Tests/NuggetTests.cs`

- [ ] **Step 1: Add NuggetSystem.Tick to GameState.Tick**

In `GameState.Tick`, after step 8 (push waves) and before step 9 (commands):

```csharp
// Step 8.5: Nugget system — mining, consumption, capture, auto-rally
NuggetSystem.Tick(this);
```

Add `using Blocker.Simulation.Systems;` if not already present.

- [ ] **Step 2: Update StateHasher to include nugget state**

In `StateHasher.Hash`, in the blocks loop, after `MixI32(ref h, b.MoveTarget.Value.Y);`, add:

```csharp
// Nugget state
MixI32(ref h, b.NuggetState != null ? 1 : 0);
if (b.NuggetState != null)
{
    MixI32(ref h, b.NuggetState.IsMined ? 1 : 0);
    MixI32(ref h, b.NuggetState.MiningProgress);
}
MixI32(ref h, b.MiningTargetId ?? 0);
MixI32(ref h, b.FortifiedHp);
```

In the nests loop, after `MixI32(ref h, n.IsPaused ? 1 : 0);`, add:

```csharp
MixI32(ref h, n.NuggetLoaded ? 1 : 0);
```

- [ ] **Step 3: Verify CommandSerializer handles new CommandTypes**

The existing `CommandSerializer` serializes `CommandType` as a byte enum value. Since we added `MineNugget`, `HealWithNugget`, `FortifyWithNugget` to the enum, they'll automatically serialize correctly as long as enum values are sequential. Verify by checking the byte cast works:

```csharp
// In NuggetTests:
[Fact]
public void CommandSerializer_RoundTrips_MineNuggetCommand()
{
    var cmd = new Command(0, CommandType.MineNugget, [1, 2], TargetPos: new GridPos(5, 5));
    var tc = new Net.TickCommands(0, 10, [cmd]);

    var bytes = Net.CommandSerializer.Serialize(tc);
    var result = Net.CommandSerializer.Deserialize(bytes);

    Assert.Single(result.Commands);
    Assert.Equal(CommandType.MineNugget, result.Commands[0].Type);
    Assert.Equal(new GridPos(5, 5), result.Commands[0].TargetPos);
}
```

- [ ] **Step 4: Write integration test — full mining lifecycle**

```csharp
[Fact]
public void FullLifecycle_MineToRefine()
{
    var state = CreateState();
    var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));
    nest.SpawnProgress = 0;

    // Place nugget adjacent to a builder that's adjacent to nest
    var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(8, 10));
    var miner = state.AddBlock(BlockType.Builder, 0, new GridPos(8, 9));

    // Issue mine command
    var mineCmd = new Command(0, CommandType.MineNugget, [miner.Id], TargetPos: nugget.Pos);
    state.Tick([mineCmd]);

    Assert.Equal(nugget.Id, miner.MiningTargetId);

    // Tick until mining completes
    for (int i = 0; i < Constants.NuggetMiningTicks + 5; i++)
        state.Tick();

    Assert.True(nugget.NuggetState!.IsMined);
    Assert.NotNull(nugget.MoveTarget); // Auto-rally set

    // Tick until nugget reaches nest (within refine radius)
    for (int i = 0; i < 100; i++)
    {
        state.Tick();
        if (state.GetBlock(nugget.Id) == null) break; // Consumed
    }

    Assert.Null(state.GetBlock(nugget.Id)); // Should have been consumed
    Assert.True(nest.SpawnProgress > 0); // Bonus applied
}
```

- [ ] **Step 5: Run all tests**

Run: `dotnet test`
Expected: All pass

- [ ] **Step 6: Commit**

```bash
git add src/Blocker.Simulation/Core/GameState.cs src/Blocker.Simulation/Net/StateHasher.cs tests/Blocker.Simulation.Tests/NuggetTests.cs
git commit -m "feat(sim): integrate NuggetSystem into tick pipeline, update StateHasher, add lifecycle test"
```

---

### Self-Review Checklist

**Spec coverage:**
- [x] Data model (BlockType, NuggetState, NuggetType) — Task 1
- [x] Constants and config — Task 2
- [x] GameState core (AddBlock, IsImmobile, PopCost) — Task 3
- [x] VisualEvents — Task 4
- [x] Combat exclusions (CombatSystem, EliminationSystem) — Task 5
- [x] Stun ray stops at nugget — Task 5
- [x] Fortified wall HP — Task 5
- [x] Blast ray destroys nuggets — handled by existing code (blast kills non-wall non-formation)
- [x] Jumper interaction (unmined stops, mined destroyed) — Task 5
- [x] Attack-move ignores nuggets — Task 5
- [x] NuggetSystem mining — Task 6
- [x] NuggetSystem capture — Task 7
- [x] NuggetSystem nest refine consumption — Task 8
- [x] NuggetSystem heal consumption — Task 9
- [x] NuggetSystem fortify consumption — Task 9
- [x] Auto-rally — Task 6 (in NuggetSystem)
- [x] Commands — Task 10
- [x] NestSystem nugget-required — Task 11
- [x] Tick integration — Task 12
- [x] StateHasher — Task 12
- [x] CommandSerializer — Task 12
- [x] Determinism (no floats, integer %) — verified in config design

**Not in this plan (deferred to Plan B and C):**
- Map integration (MapData, MapSerializer, MapEditor)
- Godot rendering (block visuals, effects, SpriteFactory)
- Input/selection (right-click targeting, cursor changes)
- HUD (nugget count display)
- Audio hooks
- EffectManager integration

**Placeholder scan:** None found.

**Type consistency:** `NuggetState`, `NuggetType`, `MiningTargetId`, `FortifiedHp`, `NuggetLoaded` — used consistently across all tasks.
