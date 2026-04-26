# Fog of War — Complete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Full fog of war and shroud in Blocker — per-team LoS computation, state hash integration, fuzzy fog/shroud shader overlay, ghost blocks, effect/audio gating, minimap FoW, known-map pathfinding, tower targeting gate, and game-over map reveal.

**Architecture:** `VisibilitySystem` computes per-team visibility from all friendly units/walls/nests/towers using Bresenham LoS each tick (step 14.5, after spawning). `VisibilityMap` (visible + explored bool arrays) lives in `GameState.VisibilityMaps`. Explored maps are included in the state hash — any LoS divergence triggers desync detection immediately. The Godot layer reads visibility maps to drive a shader-based fog overlay, skip rendering enemy blocks in fog, gate visual effects and audio, and apply FoW to the minimap. Ghost block state (last-seen static units) is tracked in `GridRenderer.Fog.cs` only — purely visual, not hashed. Pathfinding uses the team's explored map so units only route through terrain they've seen.

**Tech Stack:** C# (Blocker.Simulation, Blocker.Game), xUnit, Godot 4 GLSL shader.

---

## Performance Budget

LoS computation per tick: for each vision source, scan a `(2r+1)^2` box and run Bresenham per cell. Worst case with 400 blocks (mostly r=5, some r=7): ~400 * 121 = ~48K Bresenham walks per tick. Each walk is ~5-10 integer ops per cell stepped. At 12 tps this is ~576K walks/sec — well within budget on any modern CPU.

Mitigation if it becomes a problem: incremental/cached visibility. Only recompute around units that moved, spawned, or died since last tick. The `ClearVisible()` + full recompute approach in this plan is the simplest correct implementation; optimize later if profiling shows a problem.

Hashing the explored arrays: `MixBoolArray` packs 32 bools per int32. For a 200x200 map with 6 teams: 6 * 40000 / 32 = 7500 FNV mix ops. Negligible.

---

## File Map

**Create:**
- `src/Blocker.Simulation/Core/VisibilityMap.cs`
- `src/Blocker.Simulation/Systems/VisibilitySystem.cs`
- `tests/Blocker.Simulation.Tests/VisibilityTests.cs`
- `godot/Assets/Shaders/fog_overlay.gdshader`
- `godot/Scripts/Rendering/GridRenderer.Fog.cs`

**Modify:**
- `src/Blocker.Simulation/Core/SimulationConfig.cs` — add `VisionConfig` record
- `src/Blocker.Simulation/Core/Constants.cs` — add LoS accessor methods, remove old `BuilderLineOfSight` const
- `src/Blocker.Simulation/Core/GameState.cs` — add `VisibilityMaps` property, call `VisibilitySystem.Tick()` at step 14.5
- `src/Blocker.Simulation/Net/StateHasher.cs` — hash explored maps
- `src/Blocker.Simulation/Maps/MapLoader.cs` — call `VisibilitySystem.Tick()` at end of `Load()`
- `src/Blocker.Simulation/Systems/TowerSystem.cs` — gate `ScanForEnemies` on visibility
- `src/Blocker.Simulation/Systems/PathfindingSystem.cs` — add known-map overload using explored terrain
- `godot/Scripts/Rendering/GridRenderer.cs` — add fog shader fields, `SetControllingPlayer()`, call `UpdateFog()`, fog-skip enemy blocks, call `DrawFogGhosts()`
- `godot/Scripts/Rendering/EffectManager.cs` — gate visual effects on team visibility
- `godot/Scripts/Game/GameManager.cs` — call `SetControllingPlayer()` on grid renderer, reveal map on game-over
- `godot/Scripts/Rendering/MinimapPanel.cs` — fog/shroud rendering on minimap
- `docs/game-bible.md` — add FoW section, remove stale §19 entry
- `docs/ADR.md` — add FoW architecture decision
- `docs/architecture.md` — add VisibilitySystem to tick order

---

### Task 1: LoS Constants

**Files:**
- Modify: `src/Blocker.Simulation/Core/SimulationConfig.cs`
- Modify: `src/Blocker.Simulation/Core/Constants.cs`

- [ ] **Step 1: Add VisionConfig record to SimulationConfig.cs**

Insert before `public record SimulationConfig` (line 132):

```csharp
public record VisionConfig
{
    public bool FogOfWarEnabled { get; init; } = true;
    public int BuilderLosRadius { get; init; } = 5;
    public int SoldierLosRadius { get; init; } = 5;
    public int StunnerLosRadius { get; init; } = 7;
    public int WardenLosRadius { get; init; } = 6;
    public int JumperLosRadius { get; init; } = 5;
    public int WallLosRadius { get; init; } = 2;
    public int NestLosRadius { get; init; } = 2;
    public int TowerLosRadius { get; init; } = 2;
}
```

Add property to `SimulationConfig` (after the `TeardownTicks` line):

```csharp
public VisionConfig Vision { get; init; } = new();
```

- [ ] **Step 2: Update Constants.cs**

Remove this line (around line 108):
```csharp
public const int BuilderLineOfSight = 5; // Chebyshev radius — future fog-of-war LOS
```

Add at the end of the `Constants` class, before the closing `}`:

```csharp
// Vision / Fog of War
public static bool FogOfWarEnabled => _config.Vision.FogOfWarEnabled;
public static int NestLosRadius => _config.Vision.NestLosRadius;
public static int TowerLosRadius => _config.Vision.TowerLosRadius;

public static int GetLosRadius(BlockType type) => type switch
{
    BlockType.Builder => _config.Vision.BuilderLosRadius,
    BlockType.Soldier => _config.Vision.SoldierLosRadius,
    BlockType.Stunner => _config.Vision.StunnerLosRadius,
    BlockType.Warden => _config.Vision.WardenLosRadius,
    BlockType.Jumper => _config.Vision.JumperLosRadius,
    BlockType.Wall => _config.Vision.WallLosRadius,
    _ => 0
};
```

- [ ] **Step 3: Build to verify no compile errors**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Commit**

```
feat(fog): add VisionConfig with per-unit LoS radii
```

---

### Task 2: VisibilityMap + GameState Property

**Files:**
- Create: `src/Blocker.Simulation/Core/VisibilityMap.cs`
- Modify: `src/Blocker.Simulation/Core/GameState.cs`
- Create: `tests/Blocker.Simulation.Tests/VisibilityTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/Blocker.Simulation.Tests/VisibilityTests.cs`:

```csharp
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class VisibilityTests
{
    [Fact]
    public void VisibilityMap_SetVisible_MarksExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);

        Assert.True(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_ClearVisible_PreservesExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);
        vm.ClearVisible();

        Assert.False(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_UnsetCells_NotVisible()
    {
        var vm = new VisibilityMap(10, 10);
        Assert.False(vm.IsVisible(0, 0));
        Assert.False(vm.IsExplored(0, 0));
    }
}
```

- [ ] **Step 2: Run tests — expect compile failure (VisibilityMap doesn't exist yet)**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~VisibilityTests" 2>&1 | tail -5
```

Expected: Build fails — `VisibilityMap` not found.

- [ ] **Step 3: Create VisibilityMap.cs**

Create `src/Blocker.Simulation/Core/VisibilityMap.cs`:

```csharp
namespace Blocker.Simulation.Core;

public class VisibilityMap
{
    private readonly bool[] _visible;
    private readonly bool[] _explored;

    public int Width { get; }
    public int Height { get; }

    public VisibilityMap(int width, int height)
    {
        Width = width;
        Height = height;
        _visible = new bool[width * height];
        _explored = new bool[width * height];
    }

    public bool IsVisible(int x, int y) => _visible[y * Width + x];
    public bool IsVisible(GridPos pos) => IsVisible(pos.X, pos.Y);

    public bool IsExplored(int x, int y) => _explored[y * Width + x];
    public bool IsExplored(GridPos pos) => IsExplored(pos.X, pos.Y);

    public void ClearVisible() => Array.Clear(_visible, 0, _visible.Length);

    public void SetVisible(int x, int y)
    {
        int i = y * Width + x;
        _visible[i] = true;
        _explored[i] = true;
    }

    /// <summary>Mark every cell as explored (used on game-over reveal).</summary>
    public void RevealAll()
    {
        Array.Fill(_explored, true);
        Array.Fill(_visible, true);
    }

    /// <summary>
    /// Returns true if the cell at (x,y) has explored terrain data suitable
    /// for pathfinding: the cell has been explored, AND its terrain was passable
    /// when last visible. Unknown cells (unexplored) return false.
    /// </summary>
    public bool IsExploredPassable(int x, int y) => _explored[y * Width + x];

    public bool[] ExploredArray => _explored;
}
```

- [ ] **Step 4: Add VisibilityMaps to GameState.cs**

After the `public List<VisualEvent> VisualEvents { get; } = [];` line, add:

```csharp
public Dictionary<int, VisibilityMap> VisibilityMaps { get; } = new();
```

- [ ] **Step 5: Run tests — expect all 3 pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~VisibilityTests" -v normal
```

Expected: 3 passed.

- [ ] **Step 6: Commit**

```
feat(fog): add VisibilityMap and GameState.VisibilityMaps
```

---

### Task 3: VisibilitySystem

**Files:**
- Create: `src/Blocker.Simulation/Systems/VisibilitySystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/VisibilityTests.cs`

- [ ] **Step 1: Add tests to VisibilityTests.cs**

Append these tests after the existing three. These cover the core LoS computation, wall/terrain blocking, team sharing, explored accumulation, nest vision, disable toggle, diagonal corner LoS, and asymmetric team visibility:

```csharp
// --- VisibilitySystem tests ---

private static GameState MakeState(int width = 20, int height = 20)
{
    Constants.Reset();
    return new GameState(new Grid(width, height));
}

private static void AddPlayer(GameState state, int playerId, int teamId)
{
    state.Players.Add(new Player { Id = playerId, TeamId = teamId });
}

[Fact]
public void VisibilitySystem_SingleBlock_RevealsRadius()
{
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    Assert.True(state.VisibilityMaps.ContainsKey(1));
    var vm = state.VisibilityMaps[1];

    Assert.True(vm.IsVisible(5, 5));   // Origin
    Assert.True(vm.IsVisible(9, 5));   // 4 cells right — within radius 5
    Assert.True(vm.IsVisible(5, 9));   // 4 cells down
    Assert.True(vm.IsVisible(9, 9));   // 4 diagonal (Chebyshev)
    Assert.False(vm.IsVisible(11, 5)); // 6 cells right — outside radius 5
}

[Fact]
public void VisibilitySystem_WallBlocksLoSBehindIt()
{
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
    // Wall at (2,0) should block (3,0) but not (0,2)
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Wall, 1, new GridPos(2, 0));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    var vm = state.VisibilityMaps[1];
    Assert.True(vm.IsVisible(2, 0));   // Wall itself visible
    Assert.False(vm.IsVisible(3, 0)); // Behind wall — blocked
    Assert.True(vm.IsVisible(0, 2));   // Perpendicular — unaffected
}

[Fact]
public void VisibilitySystem_TerrainBlocksLoS()
{
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
    state.Grid[2, 0].Terrain = TerrainType.Terrain; // Terrain cell at (2,0)

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    var vm = state.VisibilityMaps[1];
    Assert.True(vm.IsVisible(2, 0));   // Terrain cell itself visible
    Assert.False(vm.IsVisible(3, 0)); // Behind terrain — blocked
}

[Fact]
public void VisibilitySystem_TeamsShareVision()
{
    var state = MakeState();
    AddPlayer(state, 1, 99);
    AddPlayer(state, 2, 99);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(19, 19));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    // Single map for team 99
    Assert.Single(state.VisibilityMaps);
    Assert.True(state.VisibilityMaps.ContainsKey(99));
    var vm = state.VisibilityMaps[99];
    Assert.True(vm.IsVisible(0, 0));
    Assert.True(vm.IsVisible(19, 19));
}

[Fact]
public void VisibilitySystem_ExploredAccumulates_AfterUnitMoves()
{
    var state = MakeState();
    AddPlayer(state, 1, 1);
    var block = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);
    var vm = state.VisibilityMaps[1];
    Assert.True(vm.IsExplored(4, 4));

    // Teleport block to far corner
    state.Grid[0, 0].BlockId = null;
    block.Pos = new GridPos(19, 19);
    state.Grid[19, 19].BlockId = block.Id;

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);
    Assert.True(vm.IsExplored(4, 4));   // Still explored from tick 1
    Assert.False(vm.IsVisible(4, 4));  // No longer visible
    Assert.True(vm.IsVisible(19, 19)); // New position visible
}

[Fact]
public void VisibilitySystem_NestGrantsVision()
{
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.Nests.Add(new Nest { Id = 1, PlayerId = 1, Center = new GridPos(10, 10), Type = NestType.Builder });

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    var vm = state.VisibilityMaps[1];
    Assert.True(vm.IsVisible(10, 10));
    Assert.True(vm.IsVisible(12, 10));  // 2 cells right — within radius 2
    Assert.False(vm.IsVisible(13, 10)); // 3 cells right — outside radius 2
}

[Fact]
public void VisibilitySystem_FogOfWarDisabled_AllVisible()
{
    Constants.Initialize(new SimulationConfig
    {
        Vision = new VisionConfig { FogOfWarEnabled = false }
    });
    var state = MakeState();
    AddPlayer(state, 1, 1);

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    // With FoW disabled, VisibilityMaps stays empty — renderer treats everything visible
    Assert.Empty(state.VisibilityMaps);

    Constants.Reset();
}

[Fact]
public void VisibilitySystem_DiagonalCornerLoS()
{
    // A wall at (1,1) should block LoS from (0,0) to (2,2) via Bresenham,
    // but should NOT block LoS from (0,0) to (2,0) or (0,2).
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Stunner, 1, new GridPos(0, 0)); // r=7
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Wall, 1, new GridPos(1, 1));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    var vm = state.VisibilityMaps[1];
    Assert.True(vm.IsVisible(1, 1));   // Wall itself visible
    Assert.False(vm.IsVisible(2, 2)); // Directly behind wall on diagonal — blocked
    Assert.True(vm.IsVisible(2, 0));   // Not blocked — different line
    Assert.True(vm.IsVisible(0, 2));   // Not blocked — different line
}

[Fact]
public void VisibilitySystem_AsymmetricTeamVisibility()
{
    // Team 1 and Team 2 have blocks far apart — each sees only their own area
    var state = MakeState();
    AddPlayer(state, 1, 1);
    AddPlayer(state, 2, 2);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(19, 19));

    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    var vm1 = state.VisibilityMaps[1];
    var vm2 = state.VisibilityMaps[2];

    // Team 1 sees origin, not far corner
    Assert.True(vm1.IsVisible(0, 0));
    Assert.False(vm1.IsVisible(19, 19));

    // Team 2 sees far corner, not origin
    Assert.True(vm2.IsVisible(19, 19));
    Assert.False(vm2.IsVisible(0, 0));
}
```

- [ ] **Step 2: Run tests — expect compile failure (VisibilitySystem not found)**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~VisibilityTests" 2>&1 | tail -5
```

Expected: Compile errors — `VisibilitySystem` not found.

- [ ] **Step 3: Create VisibilitySystem.cs**

Create `src/Blocker.Simulation/Systems/VisibilitySystem.cs`:

```csharp
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class VisibilitySystem
{
    public static void Tick(GameState state)
    {
        if (!Constants.FogOfWarEnabled) return;

        foreach (var player in state.Players)
        {
            int teamId = state.GetTeamFor(player.Id);
            if (!state.VisibilityMaps.ContainsKey(teamId))
                state.VisibilityMaps[teamId] = new VisibilityMap(state.Grid.Width, state.Grid.Height);
        }

        foreach (var vm in state.VisibilityMaps.Values)
            vm.ClearVisible();

        foreach (var block in state.Blocks)
        {
            if (block.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(block.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            int radius = Constants.GetLosRadius(block.Type);
            if (radius <= 0) continue;
            RevealFrom(state, block.Pos, radius, vm);
        }

        foreach (var nest in state.Nests)
        {
            if (nest.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(nest.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            RevealFrom(state, nest.Center, Constants.NestLosRadius, vm);
        }

        foreach (var tower in state.Towers)
        {
            if (tower.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(tower.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            var center = state.GetBlock(tower.CenterId);
            if (center == null) continue;
            RevealFrom(state, center.Pos, Constants.TowerLosRadius, vm);
        }
    }

    private static void RevealFrom(GameState state, GridPos origin, int radius, VisibilityMap vm)
    {
        var grid = state.Grid;
        int ox = origin.X, oy = origin.Y;

        if (grid.InBounds(ox, oy))
            vm.SetVisible(ox, oy);

        int minX = Math.Max(0, ox - radius);
        int maxX = Math.Min(grid.Width - 1, ox + radius);
        int minY = Math.Max(0, oy - radius);
        int maxY = Math.Min(grid.Height - 1, oy + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (x == ox && y == oy) continue;
                if (HasLineOfSight(state, ox, oy, x, y))
                    vm.SetVisible(x, y);
            }
        }
    }

    /// <summary>
    /// Bresenham line-of-sight from (x0,y0) to (x1,y1).
    /// Intermediate cells (not origin, not target) are checked for opacity.
    /// The target cell is revealed even when opaque — you see what blocks you.
    /// </summary>
    private static bool HasLineOfSight(GameState state, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int cx = x0, cy = y0;

        while (true)
        {
            if (cx == x1 && cy == y1) return true;

            if ((cx != x0 || cy != y0) && IsOpaque(state, cx, cy))
                return false;

            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; cx += sx; }
            if (e2 <= dx) { err += dx; cy += sy; }
        }
    }

    private static bool IsOpaque(GameState state, int x, int y)
    {
        var cell = state.Grid[x, y];
        if (cell.Terrain != TerrainType.None) return true;
        if (cell.BlockId.HasValue)
        {
            var block = state.GetBlock(cell.BlockId.Value);
            if (block?.Type == BlockType.Wall) return true;
        }
        return false;
    }
}
```

- [ ] **Step 4: Run all VisibilityTests — expect all pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~VisibilityTests" -v normal
```

Expected: 12 tests passed.

- [ ] **Step 5: Commit**

```
feat(fog): add VisibilitySystem with Bresenham LoS + tests
```

---

### Task 4: GameState + MapLoader Integration

**Files:**
- Modify: `src/Blocker.Simulation/Core/GameState.cs`
- Modify: `src/Blocker.Simulation/Maps/MapLoader.cs`

- [ ] **Step 1: Add VisibilitySystem.Tick to GameState.Tick**

In `GameState.cs`, find the block at line ~767:

```csharp
        // Step 14: Spawning — nest timers and unit production
        NestSystem.TickSpawning(this);

        // Step 15: Death effects — Handled by Godot layer (VisualEvents -> EffectManager)
```

Insert after `NestSystem.TickSpawning(this);`:

```csharp
        // Step 14.5: Visibility — recompute per-team LoS after all movement and spawning
        VisibilitySystem.Tick(this);
```

Add `using Blocker.Simulation.Systems;` at the top of `GameState.cs` if not already present (check — other systems are called via static methods too; the using may already be there).

- [ ] **Step 2: Initialize visibility in MapLoader.Load**

In `MapLoader.cs`, find the `return state;` at the end of `Load()` (line ~60). Insert before it:

```csharp
        VisibilitySystem.Tick(state);
```

Add `using Blocker.Simulation.Systems;` at the top of `MapLoader.cs`.

- [ ] **Step 3: Build simulation**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Run full test suite — expect all pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ 2>&1 | tail -5
```

Expected: All tests pass (208+ existing + 12 new).

- [ ] **Step 5: Commit**

```
feat(fog): integrate VisibilitySystem into tick order (step 14.5) and MapLoader init
```

---

### Task 5: StateHasher — Hash Explored Maps

**Files:**
- Modify: `src/Blocker.Simulation/Net/StateHasher.cs`
- Modify: `tests/Blocker.Simulation.Tests/VisibilityTests.cs`

- [ ] **Step 1: Add hash tests to VisibilityTests.cs**

Append to `VisibilityTests.cs`:

```csharp
[Fact]
public void StateHasher_DifferentExploredMaps_DifferentHash()
{
    var state1 = MakeState();
    AddPlayer(state1, 1, 1);
    state1.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
    Blocker.Simulation.Systems.VisibilitySystem.Tick(state1);

    var state2 = MakeState();
    AddPlayer(state2, 1, 1);
    state2.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(19, 19));
    Blocker.Simulation.Systems.VisibilitySystem.Tick(state2);

    var hash1 = Blocker.Simulation.Net.StateHasher.Hash(state1);
    var hash2 = Blocker.Simulation.Net.StateHasher.Hash(state2);
    Assert.NotEqual(hash1, hash2);
}

[Fact]
public void StateHasher_SameExploredMaps_SameHash()
{
    var state1 = MakeState();
    AddPlayer(state1, 1, 1);
    state1.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));
    Blocker.Simulation.Systems.VisibilitySystem.Tick(state1);

    var state2 = MakeState();
    AddPlayer(state2, 1, 1);
    state2.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));
    Blocker.Simulation.Systems.VisibilitySystem.Tick(state2);

    var hash1 = Blocker.Simulation.Net.StateHasher.Hash(state1);
    var hash2 = Blocker.Simulation.Net.StateHasher.Hash(state2);
    Assert.Equal(hash1, hash2);
}
```

- [ ] **Step 2: Run new tests — expect fail (hash doesn't include visibility yet)**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "StateHasher_DifferentExploredMaps|StateHasher_SameExploredMaps" 2>&1 | tail -5
```

Expected: `StateHasher_DifferentExploredMaps_DifferentHash` fails — hashes are equal.

- [ ] **Step 3: Update StateHasher.cs**

In `StateHasher.Hash()`, find `return h;` at line ~149. Insert before it:

```csharp
        var visTeams = state.VisibilityMaps.Keys.OrderBy(k => k).ToArray();
        MixI32(ref h, visTeams.Length);
        foreach (var teamId in visTeams)
        {
            MixI32(ref h, teamId);
            MixBoolArray(ref h, state.VisibilityMaps[teamId].ExploredArray);
        }
```

Add `MixBoolArray` helper after `MixI32` at the bottom of the class:

```csharp
    private static void MixBoolArray(ref uint h, bool[] arr)
    {
        int packed = 0;
        int bits = 0;
        for (int i = 0; i < arr.Length; i++)
        {
            if (arr[i]) packed |= (1 << bits);
            bits++;
            if (bits == 32)
            {
                MixI32(ref h, packed);
                packed = 0;
                bits = 0;
            }
        }
        if (bits > 0) MixI32(ref h, packed);
    }
```

- [ ] **Step 4: Run hash tests — expect both pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "StateHasher_DifferentExploredMaps|StateHasher_SameExploredMaps" -v normal
```

Expected: 2 passed.

- [ ] **Step 5: Run full test suite**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ 2>&1 | tail -5
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
feat(fog): hash explored maps in StateHasher for desync detection
```

---

### Task 6: TowerSystem Visibility Gate

**Files:**
- Modify: `src/Blocker.Simulation/Systems/TowerSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/VisibilityTests.cs`

- [ ] **Step 1: Add tower test to VisibilityTests.cs**

Append to `VisibilityTests.cs`:

```csharp
[Fact]
public void TowerSystem_DoesNotFireAtFoggedEnemy()
{
    Constants.Initialize(new SimulationConfig());
    var state = MakeState(30, 30);
    AddPlayer(state, 1, 1);
    AddPlayer(state, 2, 2);

    // Stun tower for player 1: center (Stunner) at (5,5), arm (Builder) at (6,5)
    var center = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Stunner, 1, new GridPos(5, 5));
    center.State = Blocker.Simulation.Blocks.BlockState.Rooted;
    center.RootProgress = Constants.RootTicks;
    var arm = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(6, 5));
    arm.State = Blocker.Simulation.Blocks.BlockState.Rooted;
    arm.RootProgress = Constants.RootTicks;
    Blocker.Simulation.Systems.TowerSystem.CreateTower(state, center);

    // Enemy at (5, 25) — far beyond Stunner LoS radius 7, so out of visibility
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(5, 25));

    // Compute initial visibility (enemy is not visible to team 1)
    Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

    // Prime tower fire timer
    var tower = state.Towers.First();
    tower.FireTimer = Constants.StunTowerFireInterval + 1;

    state.VisualEvents.Clear();
    Blocker.Simulation.Systems.TowerSystem.Tick(state);

    Assert.DoesNotContain(state.VisualEvents, e =>
        e.Type == Blocker.Simulation.Core.VisualEventType.TowerFired);
}
```

- [ ] **Step 2: Run test — expect fail**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "TowerSystem_DoesNotFireAtFoggedEnemy" 2>&1 | tail -5
```

Expected: Test fails — tower fires at fogged enemy.

- [ ] **Step 3: Update TowerSystem.ScanForEnemies**

In `TowerSystem.cs`, find the `ScanForEnemies` method (line ~289). Replace the entire method with:

```csharp
    private static bool ScanForEnemies(GameState state, Block center, List<Direction> directions, int range)
    {
        VisibilityMap? visMap = null;
        if (Constants.FogOfWarEnabled)
        {
            int teamId = state.GetTeamFor(center.PlayerId);
            state.VisibilityMaps.TryGetValue(teamId, out visMap);
        }

        foreach (var dir in directions)
        {
            var offset = dir.ToOffset();
            var pos = center.Pos;

            for (int i = 0; i < range; i++)
            {
                pos = pos + offset;
                if (!state.Grid.InBounds(pos)) break;
                if (!state.Grid[pos].IsPassable) break;

                if (visMap != null && !visMap.IsVisible(pos)) continue;

                var block = state.GetBlockAt(pos);
                if (block == null) continue;

                if (state.AreEnemies(block, center))
                    return true;
            }
        }

        return false;
    }
```

Add `using Blocker.Simulation.Core;` at the top of `TowerSystem.cs` if `VisibilityMap` doesn't resolve (check — `Core` namespace may already be imported).

- [ ] **Step 4: Run tower test — expect pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "TowerSystem_DoesNotFireAtFoggedEnemy" -v normal
```

Expected: 1 passed.

- [ ] **Step 5: Run full test suite**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ 2>&1 | tail -5
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
feat(fog): tower ScanForEnemies skips fogged cells
```

---

### Task 7: Known-Map Pathfinding

**Files:**
- Modify: `src/Blocker.Simulation/Systems/PathfindingSystem.cs`
- Modify: `tests/Blocker.Simulation.Tests/VisibilityTests.cs`

**Design:** When FoW is enabled, units should only pathfind through cells they've explored. Unexplored cells are treated as impassable. This means units won't try to route through shortcuts they haven't discovered — they'll follow paths they know. If no explored path exists, the greedy fallback still tries to move toward the target (the unit "guesses" in the fog direction).

The key change: `PathfindingSystem.GetNextStep` already takes `GameState` — we add the unit's team VisibilityMap context. Since `GetNextStep` is called from `GameState.Tick` which iterates blocks, we pass the team's explored map alongside.

- [ ] **Step 1: Add known-map pathfinding tests**

Append to `VisibilityTests.cs`:

```csharp
[Fact]
public void Pathfinding_AvoidsUnexploredCells()
{
    // Unit at (0,5), target at (10,5). Direct path is through unexplored cells.
    // Explored corridor goes (0,5)→(0,0)→(10,0)→(10,5).
    // With FoW, pathfinder should use the explored corridor.
    var state = MakeState();
    AddPlayer(state, 1, 1);
    var block = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 5));

    // Manually set up explored corridor — only top row and columns 0, 10
    var vm = new VisibilityMap(20, 20);
    state.VisibilityMaps[1] = vm;
    for (int x = 0; x <= 10; x++) vm.SetVisible(x, 0); // top row explored
    for (int y = 0; y <= 5; y++) vm.SetVisible(0, y);   // left column explored
    for (int y = 0; y <= 5; y++) vm.SetVisible(10, y);  // right column explored
    vm.ClearVisible(); // keep explored, clear visible

    var target = new GridPos(10, 5);
    var step = Blocker.Simulation.Systems.PathfindingSystem.GetNextStep(state, block.Pos, target, vm);

    // Should step upward (toward explored corridor) rather than rightward (into unexplored)
    Assert.NotNull(step);
    Assert.Equal(new GridPos(0, 4), step.Value);
}

[Fact]
public void Pathfinding_IgnoresExploredMap_WhenFogDisabled()
{
    Constants.Initialize(new SimulationConfig
    {
        Vision = new VisionConfig { FogOfWarEnabled = false }
    });
    var state = MakeState();
    AddPlayer(state, 1, 1);
    state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 5));

    // No visibility maps at all when fog disabled
    var target = new GridPos(5, 5);
    var step = Blocker.Simulation.Systems.PathfindingSystem.GetNextStep(state, new GridPos(0, 5), target);

    // Should go directly rightward
    Assert.NotNull(step);
    Assert.Equal(new GridPos(1, 5), step.Value);

    Constants.Reset();
}
```

- [ ] **Step 2: Run tests — expect compile failure**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "Pathfinding_Avoids|Pathfinding_Ignores" 2>&1 | tail -5
```

Expected: Compile fails — overload doesn't exist.

- [ ] **Step 3: Add VisibilityMap-aware overload to PathfindingSystem.cs**

In `PathfindingSystem.cs`, add a new overload of `GetNextStep` that accepts an optional `VisibilityMap`. The existing signature stays unchanged (no explored map = full grid, for backward compatibility and when FoW is disabled).

Replace the entire `PathfindingSystem.cs` with:

```csharp
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class PathfindingSystem
{
    private const int MaxSearchNodes = 500;

    [ThreadStatic]
    private static PriorityQueue<GridPos, int>? _openSet;
    [ThreadStatic]
    private static Dictionary<GridPos, GridPos>? _cameFrom;
    [ThreadStatic]
    private static Dictionary<GridPos, int>? _gScore;

    public static GridPos? GetNextStep(GameState state, GridPos from, GridPos target)
        => GetNextStep(state, from, target, exploredMap: null);

    public static GridPos? GetNextStep(GameState state, GridPos from, GridPos target, VisibilityMap? exploredMap)
    {
        if (from == target) return null;

        if (from.ManhattanDistance(target) == 1 && CanMoveTo(state, target, exploredMap))
            return target;

        (_openSet ??= new PriorityQueue<GridPos, int>()).Clear();
        (_cameFrom ??= new Dictionary<GridPos, GridPos>()).Clear();
        (_gScore ??= new Dictionary<GridPos, int>()).Clear();

        _gScore[from] = 0;
        _openSet.Enqueue(from, Heuristic(from, target));

        int nodesExpanded = 0;

        while (_openSet.Count > 0 && nodesExpanded < MaxSearchNodes)
        {
            var current = _openSet.Dequeue();
            nodesExpanded++;

            if (current == target)
                return ReconstructFirstStep(current, from);

            int currentG = _gScore[current];

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = current + offset;

                if (!state.Grid.InBounds(neighbor)) continue;

                if (neighbor != target && !CanMoveTo(state, neighbor, exploredMap)) continue;

                int tentativeG = currentG + 1;

                if (!_gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    _cameFrom[neighbor] = current;
                    _gScore[neighbor] = tentativeG;
                    int dx1 = neighbor.X - target.X;
                    int dy1 = neighbor.Y - target.Y;
                    int dx2 = from.X - target.X;
                    int dy2 = from.Y - target.Y;
                    int cross = Math.Abs(dx1 * dy2 - dx2 * dy1);
                    int fScore = (tentativeG + Heuristic(neighbor, target)) * 1000 + cross;
                    _openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        // No explored path found — greedy step ignores explored constraint
        // (the unit "guesses" toward the target in the fog)
        return GreedyStep(state, from, target);
    }

    private static GridPos? ReconstructFirstStep(GridPos current, GridPos start)
    {
        var step = current;
        while (_cameFrom!.TryGetValue(step, out var prev))
        {
            if (prev == start)
                return step;
            step = prev;
        }
        return null;
    }

    private static int Heuristic(GridPos a, GridPos b) => a.ManhattanDistance(b);

    private static bool CanMoveTo(GameState state, GridPos pos, VisibilityMap? exploredMap = null)
    {
        if (!state.Grid.InBounds(pos)) return false;
        var cell = state.Grid[pos];
        if (!cell.IsPassable || cell.BlockId.HasValue) return false;
        if (exploredMap != null && !exploredMap.IsExplored(pos)) return false;
        return true;
    }

    private static GridPos? GreedyStep(GameState state, GridPos from, GridPos target)
    {
        int dx = Math.Sign(target.X - from.X);
        int dy = Math.Sign(target.Y - from.Y);
        int adx = Math.Abs(target.X - from.X);
        int ady = Math.Abs(target.Y - from.Y);

        if (adx >= ady)
        {
            if (dx != 0 && CanMoveTo(state, new GridPos(from.X + dx, from.Y)))
                return new GridPos(from.X + dx, from.Y);
            if (dy != 0 && CanMoveTo(state, new GridPos(from.X, from.Y + dy)))
                return new GridPos(from.X, from.Y + dy);
        }
        else
        {
            if (dy != 0 && CanMoveTo(state, new GridPos(from.X, from.Y + dy)))
                return new GridPos(from.X, from.Y + dy);
            if (dx != 0 && CanMoveTo(state, new GridPos(from.X + dx, from.Y)))
                return new GridPos(from.X + dx, from.Y);
        }

        return null;
    }
}
```

- [ ] **Step 4: Update the call site in GameState.Tick**

In `GameState.cs`, find where `PathfindingSystem.GetNextStep` is called during the movement step. It will look something like:

```csharp
var next = PathfindingSystem.GetNextStep(this, block.Pos, target);
```

Change it to pass the team's explored map when FoW is enabled:

```csharp
VisibilityMap? exploredMap = null;
if (Constants.FogOfWarEnabled && block.PlayerId >= 0)
{
    int teamId = GetTeamFor(block.PlayerId);
    VisibilityMaps.TryGetValue(teamId, out exploredMap);
}
var next = PathfindingSystem.GetNextStep(this, block.Pos, target, exploredMap);
```

**Important:** Search for ALL `PathfindingSystem.GetNextStep` calls in `GameState.cs` and apply the same pattern. There may be more than one (e.g., nugget auto-rally, heal pathfinding, fortify pathfinding). Each call should pass the owning block's team explored map.

- [ ] **Step 5: Run pathfinding tests — expect pass**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ --filter "Pathfinding_Avoids|Pathfinding_Ignores" -v normal
```

Expected: 2 passed.

- [ ] **Step 6: Run full test suite**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ 2>&1 | tail -5
```

Expected: All tests pass. Watch for regressions in existing pathfinding tests — the `null` exploredMap overload should keep existing behavior intact.

- [ ] **Step 7: Commit**

```
feat(fog): pathfinding uses explored map — units route through known terrain only
```

---

### Task 8: Fog Overlay Shader

**Files:**
- Create: `godot/Assets/Shaders/fog_overlay.gdshader`

- [ ] **Step 1: Create fog_overlay.gdshader**

```glsl
shader_type canvas_item;

// fog_data: R8 texture, one pixel per grid cell.
//   0.0 = shroud (unexplored, never seen) -> full black
//   ~0.5 = fog of war (explored, not currently visible) -> dark overlay
//   1.0 = visible (in LoS this tick) -> transparent
uniform sampler2D fog_data : filter_linear, repeat_disable;
uniform ivec2 grid_size;
uniform float cell_size = 28.0;
uniform float grid_padding = 140.0;

void fragment() {
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;

    // Outside grid area — fully dark (padding and beyond)
    if (local_pos.x < grid_padding || local_pos.y < grid_padding ||
        local_pos.x > total_size.x - grid_padding ||
        local_pos.y > total_size.y - grid_padding) {
        COLOR = vec4(0.0, 0.0, 0.0, 1.0);
        return;
    }

    vec2 grid_local = local_pos - grid_padding;
    vec2 cell_frac = grid_local / cell_size;
    vec2 tex_uv = cell_frac / vec2(grid_size);
    vec2 texel = 1.0 / vec2(grid_size);

    // 5-tap weighted sample: hardware bilinear + 4 neighbours at 0.6 texel offset
    // Widens the transition zone to ~2 cells for a soft edge
    float c = texture(fog_data, tex_uv).r;
    float r = texture(fog_data, tex_uv + vec2(texel.x * 0.6, 0.0)).r;
    float l = texture(fog_data, tex_uv - vec2(texel.x * 0.6, 0.0)).r;
    float d = texture(fog_data, tex_uv + vec2(0.0, texel.y * 0.6)).r;
    float u = texture(fog_data, tex_uv - vec2(0.0, texel.y * 0.6)).r;
    float fog_val = c * 0.4 + (r + l + d + u) * 0.15;

    // Map fog_val to overlay alpha:
    //   0.0 (shroud)  -> alpha 1.0, colour pure black
    //   0.5 (fog)     -> alpha 0.65, colour near-black with blue hint
    //   1.0 (visible) -> alpha 0.0, fully transparent
    float alpha;
    vec3 col;

    if (fog_val < 0.5) {
        float t = fog_val * 2.0;
        alpha = mix(1.0, 0.65, smoothstep(0.0, 1.0, t));
        col = vec3(0.0, 0.0, 0.01);
    } else {
        float t = (fog_val - 0.5) * 2.0;
        alpha = mix(0.65, 0.0, smoothstep(0.0, 1.0, t));
        col = vec3(0.0, 0.01, 0.04);
    }

    // Subtle per-pixel noise on the fog boundary for a hazy appearance
    float noise = fract(sin(dot(tex_uv * 300.0, vec2(12.9898, 78.233))) * 43758.5453);
    alpha = clamp(alpha + (noise - 0.5) * 0.06, 0.0, 1.0);

    COLOR = vec4(col, alpha);
}
```

- [ ] **Step 2: Commit**

```
feat(fog): add fog_overlay.gdshader with soft shroud/fog/visible transitions
```

---

### Task 9: FogOverlay Godot Rendering + Ghost Blocks

**Files:**
- Create: `godot/Scripts/Rendering/GridRenderer.Fog.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.cs`

- [ ] **Step 1: Create GridRenderer.Fog.cs**

```csharp
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

public partial class GridRenderer : Node2D
{
    private ColorRect? _fogRect;
    private ShaderMaterial? _fogMaterial;
    private ImageTexture? _fogTexture;
    private Image? _fogImage;

    // Ghost blocks: last-seen state of static units (walls, rooted, formation members).
    // Purely visual — not part of simulation state.
    private readonly Dictionary<GridPos, (BlockType Type, int PlayerId)> _fogGhosts = new();

    private int _controllingTeamId = -1;
    private VisibilityMap? _controllingVm;
    private int _fogLastTick = -1;

    /// <summary>
    /// Called by GameManager after SetGameState. Tells the renderer which team's
    /// perspective to render fog from.
    /// </summary>
    public void SetControllingPlayer(int playerId)
    {
        if (_gameState == null) return;
        _controllingTeamId = _gameState.GetTeamFor(playerId);
        InitFogOverlay();
    }

    private void InitFogOverlay()
    {
        _fogRect?.QueueFree();
        _fogRect = null;

        if (_gameState == null || !Constants.FogOfWarEnabled) return;

        var shader = GD.Load<Shader>("res://Assets/Shaders/fog_overlay.gdshader");
        _fogMaterial = new ShaderMaterial { Shader = shader };
        _fogRect = new ColorRect
        {
            Material = _fogMaterial,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 10, // Above blocks (~Z 0), below UI
        };
        AddChild(_fogRect);

        var grid = _gameState.Grid;
        _fogImage = Image.CreateEmpty(grid.Width, grid.Height, false, Image.Format.R8);
        _fogTexture = ImageTexture.CreateFromImage(_fogImage);

        _fogMaterial.SetShaderParameter("fog_data", _fogTexture);
        _fogMaterial.SetShaderParameter("grid_size", new Vector2I(grid.Width, grid.Height));
        _fogMaterial.SetShaderParameter("cell_size", CellSize);
        _fogMaterial.SetShaderParameter("grid_padding", GridPadding);

        float totalW = grid.Width * CellSize + GridPadding * 2f;
        float totalH = grid.Height * CellSize + GridPadding * 2f;
        _fogRect.Size = new Vector2(totalW, totalH);
        _fogRect.Position = Vector2.Zero;
    }

    private void UpdateFog()
    {
        if (_gameState == null) return;
        if (_gameState.TickNumber == _fogLastTick) return;
        _fogLastTick = _gameState.TickNumber;

        _gameState.VisibilityMaps.TryGetValue(_controllingTeamId, out _controllingVm);
        if (_controllingVm == null || !Constants.FogOfWarEnabled) return;

        var grid = _gameState.Grid;

        // Update ghost state: record last-seen static blocks in visible cells
        foreach (var block in _gameState.Blocks)
        {
            if (!_controllingVm.IsVisible(block.Pos)) continue;

            bool isStatic = block.Type == BlockType.Wall
                || block.IsFullyRooted
                || block.IsInFormation;

            if (isStatic)
                _fogGhosts[block.Pos] = (block.Type, block.PlayerId);
            else
                _fogGhosts.Remove(block.Pos);
        }

        // Clear ghosts in visible cells that are now empty (wall destroyed, unit killed)
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var pos = new GridPos(x, y);
                if (_controllingVm.IsVisible(pos) && !grid[pos].BlockId.HasValue)
                    _fogGhosts.Remove(pos);
            }
        }

        // Write fog texture
        if (_fogImage == null || _fogTexture == null) return;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                float val = _controllingVm.IsVisible(x, y) ? 1.0f
                    : _controllingVm.IsExplored(x, y) ? 128f / 255f
                    : 0.0f;
                _fogImage.SetPixel(x, y, new Color(val, 0f, 0f, 1f));
            }
        }

        _fogTexture.Update(_fogImage);
    }

    private void DrawFogGhosts()
    {
        if (_controllingVm == null || !Constants.FogOfWarEnabled) return;

        foreach (var (pos, ghost) in _fogGhosts)
        {
            if (!_controllingVm.IsExplored(pos) || _controllingVm.IsVisible(pos)) continue;

            var worldPos = GridToWorld(pos);
            var rect = new Rect2(
                worldPos.X - CellSize / 2f + BlockInset,
                worldPos.Y - CellSize / 2f + BlockInset,
                CellSize - BlockInset * 2,
                CellSize - BlockInset * 2
            );

            var baseColor = _config.GetPalette(ghost.PlayerId).Base;
            var ghostColor = new Color(
                baseColor.R * 0.50f,
                baseColor.G * 0.50f,
                baseColor.B * 0.55f,
                0.45f
            );

            DrawRect(rect, ghostColor);

            if (ghost.Type == BlockType.Wall)
            {
                DrawRect(rect, new Color(ghostColor.R, ghostColor.G, ghostColor.B, ghostColor.A * 0.6f), false, 1.5f);
            }
        }
    }

    /// <summary>Reveal entire map (called on game-over).</summary>
    public void RevealFog()
    {
        _controllingVm = null;
        _fogGhosts.Clear();

        if (_fogRect != null)
        {
            _fogRect.QueueFree();
            _fogRect = null;
        }

        _fogLastTick = -1;
    }

    /// <summary>
    /// Returns the controlling team's VisibilityMap, or null if FoW is disabled.
    /// Used by EffectManager and MinimapPanel to check visibility.
    /// </summary>
    public VisibilityMap? GetControllingVisibilityMap() => _controllingVm;
    public int ControllingTeamId => _controllingTeamId;
}
```

- [ ] **Step 2: Call UpdateFog in GridRenderer._Process**

In `GridRenderer.cs`, in `_Process()`, find the line `QueueRedraw();` near the end (around line 429). Insert before it:

```csharp
        UpdateFog();
```

- [ ] **Step 3: Call DrawFogGhosts in GridRenderer._Draw**

In `GridRenderer.cs`, in `_Draw()`, find the terrain wall loop (around line 494, ending with `}`). After the terrain wall loop ends, insert:

```csharp
        DrawFogGhosts();
```

This goes before the block drawing loop so ghosts appear behind live blocks.

- [ ] **Step 4: Add fog-skip for enemy blocks in the block drawing loop**

In `GridRenderer.cs`, in `_Draw()`, find the block viewport-cull continue (around line 511):

```csharp
            if (worldPos.X < blockViewMinX || worldPos.X > blockViewMaxX ||
                worldPos.Y < blockViewMinY || worldPos.Y > blockViewMaxY)
                continue;
```

After that continue, add:

```csharp
            if (Constants.FogOfWarEnabled && _controllingVm != null && block.PlayerId != -1)
            {
                int blockTeam = _gameState.GetTeamFor(block.PlayerId);
                if (blockTeam != _controllingTeamId && !_controllingVm.IsVisible(block.Pos))
                    continue;
            }
```

- [ ] **Step 5: Reset fog state in SetGameState**

In `GridRenderer.cs`, in `SetGameState()`, add to the reset block (after `_lastProcessedTick = -1;`):

```csharp
        _fogGhosts.Clear();
        _fogLastTick = -1;
        _controllingVm = null;
```

- [ ] **Step 6: Build Godot project**

```bash
cd /Users/juliano7s/workspace/blocker/godot && dotnet build Blocker.Game.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 7: Commit**

```
feat(fog): add FogOverlay shader rendering, ghost blocks, enemy block culling in GridRenderer
```

---

### Task 10: Gate Visual Effects and Audio on Visibility

**Files:**
- Modify: `godot/Scripts/Rendering/EffectManager.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Effects.cs`

**Design:** Without this, enemy deaths, spawns, stun rays, push waves, jump trails, and other effects would draw in fog, leaking enemy positions. We gate "global" (non-private) visual events on whether the event position is visible to the controlling team. Private events are already filtered by player ID.

For GridRenderer-drawn effects (rays, push waves, Warden ZoC), we add per-cell visibility checks.

- [ ] **Step 1: Gate EffectManager.SpawnEffects on visibility**

In `EffectManager.cs`, the `SpawnEffects` method already has a private-event filter. After that filter (around line 182, after the `if (IsPrivateEvent(evt)) { ... }` block), add a fog visibility gate for non-private events:

```csharp
        // Gate non-private events on team visibility — don't show enemy actions in fog
        if (!IsPrivateEvent(evt) && Constants.FogOfWarEnabled)
        {
            if (_gridRenderer != null)
            {
                var vm = _gridRenderer.GetControllingVisibilityMap();
                if (vm != null && !vm.IsVisible(pos))
                    return;
            }
        }
```

For this to work, `EffectManager` needs a reference to `GridRenderer`. Add a field and setter:

```csharp
    private GridRenderer? _gridRenderer;
    public void SetGridRenderer(GridRenderer renderer) => _gridRenderer = renderer;
```

- [ ] **Step 2: Wire GridRenderer in GameManager**

In `GameManager.cs`, after the line `_gridRenderer.SetEffectManager(_effectManager);` (around line 153), add:

```csharp
        _effectManager.SetGridRenderer(_gridRenderer);
```

- [ ] **Step 3: Gate GridRenderer ray drawing on visibility**

In `GridRenderer.Effects.cs`, in `DrawDirectionalRay()`, add a visibility check per cell. Inside the for loop (around line 212), after computing `cellPos`, add before the existing brightness computation:

```csharp
            if (Constants.FogOfWarEnabled && _controllingVm != null && !_controllingVm.IsVisible(cellPos))
                continue;
```

Do the same in `DrawExplosion()` — inside the nested for loops (around line 286), after computing `cx, cy` and the InBounds check:

```csharp
                if (Constants.FogOfWarEnabled && _controllingVm != null && !_controllingVm.IsVisible(new GridPos(cx, cy)))
                    continue;
```

And in `DrawPushWaves()` — inside the for loop over wave cells (around line 341), after computing `cellPos`:

```csharp
                if (Constants.FogOfWarEnabled && _controllingVm != null && !_controllingVm.IsVisible(cellPos))
                    continue;
```

- [ ] **Step 4: Gate Warden ZoC shader on visibility**

In `GridRenderer.Effects.cs`, in `UpdateWardenZoC()`, inside the active wardens loop, after the `if (block.Type != BlockType.Warden) continue;` check, add:

```csharp
            // Hide enemy Warden ZoC in fog
            if (Constants.FogOfWarEnabled && _controllingVm != null)
            {
                int wardenTeam = _gameState.GetTeamFor(block.PlayerId);
                if (wardenTeam != _controllingTeamId && !_controllingVm.IsVisible(block.Pos))
                    continue;
            }
```

This means enemy Warden ZoC pulses are only shown if the Warden's position is visible.

- [ ] **Step 5: Gate AudioManager on visibility**

The AudioManager has the same pattern as EffectManager — it consumes `VisualEvents`. It needs the same fog gate. In the AudioManager class, add:

```csharp
    private GridRenderer? _gridRenderer;
    public void SetGridRenderer(GridRenderer renderer) => _gridRenderer = renderer;
```

Then in the event processing method (find where it iterates `_gameState.VisualEvents` and calls the play logic), after the private event filter, add the same fog gate:

```csharp
        if (!IsPrivateEvent(evt) && Constants.FogOfWarEnabled)
        {
            if (_gridRenderer != null)
            {
                var vm = _gridRenderer.GetControllingVisibilityMap();
                if (vm != null && !vm.IsVisible(evt.Position))
                    return; // or continue, depending on loop structure
            }
        }
```

Wire it in `GameManager._Ready`:

```csharp
        _audioManager.SetGridRenderer(_gridRenderer);
```

- [ ] **Step 6: Build Godot project**

```bash
cd /Users/juliano7s/workspace/blocker/godot && dotnet build Blocker.Game.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 7: Commit**

```
feat(fog): gate visual effects and audio on team visibility — no enemy info leaks through fog
```

---

### Task 11: Minimap FoW

**Files:**
- Modify: `godot/Scripts/Rendering/MinimapPanel.cs`

**Design:** The minimap should respect fog of war: shroud = black, fog = dimmed, enemy blocks in fog are hidden. The minimap already draws ground, terrain, and blocks in a simple loop. We add a visibility map reference and gate the drawing.

- [ ] **Step 1: Add visibility state to MinimapPanel**

Add fields and setter:

```csharp
    private VisibilityMap? _visibilityMap;
    private int _controllingTeamId = -1;

    public void SetVisibility(VisibilityMap? vm, int teamId)
    {
        _visibilityMap = vm;
        _controllingTeamId = teamId;
    }
```

- [ ] **Step 2: Gate ground/terrain drawing on explored**

In `_Draw()`, inside the ground/terrain cell loop (the `for (int y ... for (int x ...` that draws ground colors), wrap the existing drawing with a fog check:

```csharp
                // Fog of war: unexplored cells are black, explored-but-not-visible cells are dimmed
                if (Constants.FogOfWarEnabled && _visibilityMap != null)
                {
                    if (!_visibilityMap.IsExplored(x, y))
                    {
                        // Shroud — skip drawing ground, the dark background shows through
                        continue;
                    }
                    if (!_visibilityMap.IsVisible(x, y))
                    {
                        // Fog — darken the color
                        color = color.Darkened(0.6f);
                    }
                }
```

Put this after the `color` has been assigned but before the `DrawRect` call. The `continue` for unexplored skips the ground drawing entirely (black shows through from the panel background).

- [ ] **Step 3: Gate block drawing on visibility**

In `_Draw()`, inside the block drawing loop (`foreach (var block in _gameState.Blocks)`), add at the top:

```csharp
            // Hide enemy blocks in fog
            if (Constants.FogOfWarEnabled && _visibilityMap != null && block.PlayerId != -1)
            {
                int blockTeam = _gameState.GetTeamFor(block.PlayerId);
                if (blockTeam != _controllingTeamId && !_visibilityMap.IsVisible(block.Pos))
                    continue;
            }
```

- [ ] **Step 4: Wire visibility updates in GameManager._Process**

In `GameManager._Process()`, after the existing `_hudBar.SetControllingPlayer(...)` call, add:

```csharp
            _hudBar.SetMinimapVisibility(
                _gridRenderer.GetControllingVisibilityMap(),
                _gridRenderer.ControllingTeamId);
```

This requires adding a `SetMinimapVisibility` pass-through on `HudBar` that delegates to the `MinimapPanel` inside it. Find how `HudBar` creates/holds the `MinimapPanel` and add:

```csharp
    public void SetMinimapVisibility(VisibilityMap? vm, int teamId)
    {
        _minimapPanel?.SetVisibility(vm, teamId);
    }
```

(The exact field name for the minimap panel may differ — check `HudBar.cs` for how it references the `MinimapPanel`.)

- [ ] **Step 5: Build Godot project**

```bash
cd /Users/juliano7s/workspace/blocker/godot && dotnet build Blocker.Game.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 6: Commit**

```
feat(fog): minimap respects fog of war — shroud, fog dimming, enemy block hiding
```

---

### Task 12: GameManager Wiring + Game-Over Reveal

**Files:**
- Modify: `godot/Scripts/Game/GameManager.cs`

- [ ] **Step 1: Call SetControllingPlayer on GridRenderer**

In `GameManager._Ready()`, after the grid renderer and selection manager are set up, add the fog wiring. Find where `_selectionManager.ControllingPlayer` is assigned (line 95 for MP, default 0 for SP). After the block where tick runners are configured (around line 114), add:

```csharp
        _gridRenderer.SetControllingPlayer(_selectionManager.ControllingPlayer);
```

- [ ] **Step 2: Keep controlling player in sync during _Process**

In `GameManager._Process()`, the controlling player is already synced to HUD and EffectManager. Add the grid renderer sync alongside them (around line 179):

```csharp
        _gridRenderer.SetControllingPlayer(_selectionManager.ControllingPlayer);
```

**Important:** `SetControllingPlayer` on `GridRenderer` calls `InitFogOverlay()` which creates the shader rect. To avoid re-creating it every frame, add a guard in `SetControllingPlayer`:

Actually, look at the implementation in Task 9 — `SetControllingPlayer` calls `InitFogOverlay()` which `QueueFree`s and recreates the rect. This is wrong for per-frame calls. Instead, modify the `SetControllingPlayer` in `GridRenderer.Fog.cs` to guard on change:

```csharp
    public void SetControllingPlayer(int playerId)
    {
        if (_gameState == null) return;
        int newTeamId = _gameState.GetTeamFor(playerId);
        if (newTeamId == _controllingTeamId && _fogRect != null) return;
        _controllingTeamId = newTeamId;
        InitFogOverlay();
    }
```

- [ ] **Step 3: Reveal map on game-over**

In `GameManager.cs`, in `ShowGameOverOverlayDeferred()` (line ~213), after `_gameOverShown = true;`, add:

```csharp
        _gridRenderer.RevealFog();
```

This removes the fog overlay and clears ghost state so the full map is visible in the game-over screen. Standard RTS UX.

- [ ] **Step 4: Build Godot project**

```bash
cd /Users/juliano7s/workspace/blocker/godot && dotnet build Blocker.Game.csproj 2>&1 | tail -10
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Commit**

```
feat(fog): wire controlling player, sync per-frame, reveal map on game-over
```

---

### Task 13: Documentation Updates

**Files:**
- Modify: `docs/game-bible.md`
- Modify: `docs/ADR.md`
- Modify: `docs/architecture.md`

- [ ] **Step 1: Add Fog of War section to game-bible.md**

The existing Section 15 is "Map System" (marked as deferred). Insert a new section before it — renumber as needed, or use 14.5 / 15 and push Map System to 16. The cleanest approach: insert after Section 14 (Tick Resolution Order), before the deferred Map System section.

```markdown
## 15. Fog of War and Shroud

### 15.1 Overview

- **Shroud**: Unexplored territory. Shown as solid black. The entire map starts in shroud.
- **Fog of War (FoW)**: Explored territory not currently in any friendly unit's LoS. Shows terrain and last-seen static entities (walls, rooted blocks, formation members) as semi-transparent ghosts. Hides mobile enemy units.
- **Visible**: Currently within at least one friendly unit's LoS. Shows full real-time state.

### 15.2 Vision Sources and Radii

All friendly units, walls, nests, and towers provide vision. Radius is Chebyshev distance. Line of sight is blocked by Wall blocks and Terrain cells (Terrain, BreakableWall, FragileWall). The blocking cell itself is always visible (you see what blocks you).

| Entity | LoS Radius |
|--------|-----------|
| Builder | 5 |
| Soldier | 5 |
| Stunner | 7 |
| Warden | 6 |
| Jumper | 5 |
| Wall | 2 |
| Nest | 2 |
| Tower | 2 |

Radii are tunable in `SimulationConfig.Vision`.

### 15.3 Team Vision

Teammates share a single visibility map. All units on the same team contribute to it. FFA: each player has their own map (team ID equals player ID).

### 15.4 Multiplayer Model

All clients compute full visibility for all teams identically (lockstep). Each client renders only its local team's perspective. Explored maps are included in the state hash — any LoS divergence triggers desync detection immediately.

### 15.5 Ghost Entities

In Fog of War areas, last-seen static entities are displayed as dim ghosts. Ghosts persist even if the entity has since been destroyed — until the player re-explores the cell and sees it is gone.

- **Shown as ghosts:** Walls, fully rooted blocks, formation members.
- **Not shown:** Mobile units (they vanish when they leave LoS).

### 15.6 Tower Targeting

Towers (Stun Tower, Soldier Tower) only fire when a hostile unit is in a **visible** cell within their scan range. They do not fire at enemies in fog or shroud.

### 15.7 Pathfinding

Units only pathfind through explored terrain. Unexplored cells are treated as impassable by A*. If no explored path exists, the greedy fallback moves toward the target regardless (the unit "guesses" into the fog). This means scouting matters — units won't discover shortcuts they haven't seen.

### 15.8 Information Hiding

Visual effects (lightning, rays, push waves, death animations, spawn effects) and audio are suppressed for events occurring in fogged cells. The minimap also respects fog of war: shroud is black, fog is dimmed, enemy blocks in fog are hidden. On game-over, the full map is revealed.
```

- [ ] **Step 2: Remove stale fog-of-war entry from Section 19 (Future/Maybe)**

In `docs/game-bible.md`, Section 19, find and remove this bullet:

```
- **Fog of War**: Proximity-based vision (only see cells adjacent to own blocks). Could be a game mode toggle.
```

- [ ] **Step 3: Add ADR entry to docs/ADR.md**

Prepend (ADR log is newest-first):

```markdown
## [2026-04-26] Fog of War Architecture

- **Context**: Blocker had perfect information — all players see everything. Adding FoW required fitting into a deterministic lockstep multiplayer model without network overhead and without breaking the state hash.
- **Decisions**:
  1. **Simulation layer holds visibility state.** Per-team `VisibilityMap` (visible + explored bool arrays) lives in `GameState.VisibilityMaps`. Computed by `VisibilitySystem` at tick step 14.5 (after all movement and spawning). Algorithm: Bresenham LoS, blocked by Wall blocks and Terrain cells.
  2. **Lockstep FoW.** All clients compute visibility for all teams identically. Each client renders only its local team's view. Explored maps are included in the state hash — LoS bugs desync immediately.
  3. **Rendering split.** Gameplay state (explored maps) is in simulation + hashed. Ghost block rendering (last-seen positions of static units) lives in `GridRenderer` only — purely visual, not hashed.
  4. **Known-map pathfinding.** A* treats unexplored cells as impassable. Greedy fallback ignores this constraint (units can guess into fog). Scouting becomes strategically important.
  5. **Full information gating.** Visual effects, audio, minimap, and rendering all respect team visibility. No information leaks through fog.
- **Impact**: Perfect information is gone; scouting and wall-based vision denial become strategic. Tower targeting gated on team visibility. Pathfinding uses only known terrain.
```

- [ ] **Step 4: Update docs/architecture.md tick resolution order**

In `docs/architecture.md`, Section 2.2 (Tick Engine), find the tick resolution order list. After step 12 (Spawning), add:

```
12.5. **Visibility** — `VisibilitySystem` recomputes per-team LoS
```

(Note: the architecture.md numbers don't exactly match the game bible — the key is inserting after spawning, before elimination.)

Also add `VisibilitySystem.cs` and `VisibilityMap.cs` to the project structure listing in Section 2.5.

- [ ] **Step 5: Run full simulation test suite one final time**

```bash
cd /Users/juliano7s/workspace/blocker && dotnet test tests/Blocker.Simulation.Tests/ 2>&1 | tail -5
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
feat(fog): update game bible, ADR, and architecture docs for fog of war
```

---

### Task 14: Smoke Test

- [ ] **Step 1: Open Godot and test locally**

Open the project at `godot/project.godot`. Start a local single-player match. Verify:

1. Game starts with the entire map black (shroud).
2. Your starting blocks reveal a radius around them immediately.
3. Moving a unit reveals new territory; old territory transitions to fog (darker, semi-transparent, not black).
4. Enemy blocks in fogged areas disappear (only your team's units visible in explored fog).
5. After seeing an enemy wall, it reappears as a dimmed ghost when you move away.
6. Tower does not fire at enemies it cannot see.
7. Border between fog/shroud/visible is fuzzy (not a hard pixel edge).
8. Minimap shows fog: black shroud, dimmed fog, hidden enemy blocks.
9. No visual effects (death explosions, stun rays, push waves, spawn lightning) appear in fogged areas.
10. No sounds play for events in fogged areas.
11. Units pathfind through explored territory, not through fog.
12. When a unit reaches the edge of explored territory, it moves into the fog (greedy fallback).
13. Tab-switching players shows different fog perspectives.
14. Game-over reveals the full map.

- [ ] **Step 2: Test with fog disabled**

Set `FogOfWarEnabled = false` in the config (or add a quick toggle). Verify the game behaves exactly as before — no fog, no shroud, no ghosts, full minimap, all effects visible.

---

## Self-Review

**Spec coverage:**

| Requirement | Task |
|---|---|
| Shroud = black, unexplored | Task 8 (shader maps 0.0 -> full black) |
| FoW = explored, semi-dark | Task 8 (0.5 -> 65% alpha overlay) |
| Fuzzy fog/shroud borders | Task 8 (5-tap blur + noise) |
| All units have LoS with their radius | Tasks 1+3 |
| Walls/nests/towers provide vision (radius 2) | Tasks 1+3 |
| Static blocks shown as ghosts in FoW | Task 9 (ghost tracking + DrawFogGhosts) |
| Ghosts persist even if entity destroyed | Task 9 (cleared only on re-explore) |
| Mobile enemies hidden in fog | Task 9 (skip draw) |
| Tower only fires at visible enemies | Task 6 |
| Team sharing (teammates share vision) | Task 3 (VisibilitySystem uses GetTeamFor) |
| Multiplayer: per-team, hashed | Tasks 4+5 |
| Pathfinding uses known map | Task 7 |
| Visual effects gated on visibility | Task 10 |
| Audio gated on visibility | Task 10 |
| Minimap FoW | Task 11 |
| Game-over map reveal | Task 12 |
| FogOfWarEnabled disable toggle | Tasks 1+3+9+10+11 |
| Game bible updated | Task 13 |
| ADR logged | Task 13 |
| Architecture doc updated | Task 13 |
| Stale §19 entry removed | Task 13 |
| Diagonal corner LoS tested | Task 3 |
| Asymmetric team visibility tested | Task 3 |
| Performance budget documented | Plan header |

**Placeholder scan:** None found. All code blocks are complete.

**Type consistency:**
- `VisibilityMap.IsVisible(GridPos)` — defined Task 2, used Tasks 3, 6, 9, 10, 11
- `VisibilityMap.ExploredArray` — defined Task 2, used Task 5
- `VisibilityMap.RevealAll()` — defined Task 2, available for future use
- `VisibilityMap.IsExplored(GridPos)` — defined Task 2, used Tasks 7, 9, 11
- `Constants.GetLosRadius(BlockType)` — defined Task 1, used Task 3
- `Constants.FogOfWarEnabled` — defined Task 1, used Tasks 3, 6, 7, 9, 10, 11, 12
- `GameState.VisibilityMaps` — defined Task 2, used Tasks 3, 5, 6, 7, 9, 10
- `GridRenderer._controllingVm` — set in `UpdateFog()` (Task 9), read in Tasks 9, 10
- `GridRenderer._controllingTeamId` — set in `SetControllingPlayer()` (Task 9), read in Tasks 9, 10, 11
- `GridRenderer.GetControllingVisibilityMap()` — defined Task 9, used Tasks 10, 11
- `GridRenderer.RevealFog()` — defined Task 9, used Task 12
- `PathfindingSystem.GetNextStep(state, from, target, exploredMap)` — defined Task 7, used Task 7
- `GridPos` is a `readonly record struct` — value equality confirmed, safe as Dictionary key
