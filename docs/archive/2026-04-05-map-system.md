# Map System & Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the 2-player text-based map format with a sparse JSON format supporting up to 6 players, add an in-game map editor, and create a main menu for navigation.

**Architecture:** Split Cell's `GroundType` into `GroundType` (floor) + `TerrainType` (obstacles) to support independent layers. Add `MapData` as the pure C# interchange format between JSON files, the editor, and the simulation. Build the map editor as a Godot scene reusing `GridRenderer`. Add a main menu scene as the new entry point.

**Tech Stack:** C# / .NET 8, Godot 4.6, xUnit, System.Text.Json (Godot side only)

---

## File Map

### Simulation (pure C#, no Godot references)

| File | Action | Responsibility |
|------|--------|----------------|
| `src/Blocker.Simulation/Core/Cell.cs` | Modify | Split `GroundType` enum → `GroundType` + `TerrainType`, add `Terrain` field to `Cell` |
| `src/Blocker.Simulation/Maps/MapData.cs` | Create | `MapData`, `GroundEntry`, `TerrainEntry`, `UnitEntry` records |
| `src/Blocker.Simulation/Maps/SlotAssignment.cs` | Create | `SlotAssignment` record |
| `src/Blocker.Simulation/Maps/MapLoader.cs` | Modify | Add `Load(MapData, List<SlotAssignment>)` overload, update legacy loader for new cell model |
| `src/Blocker.Simulation/Systems/CombatSystem.cs` | Modify | Update wall destruction to use `TerrainType` |
| `tests/Blocker.Simulation.Tests/MapLoaderTests.cs` | Modify | Update existing tests for split types, add `MapData` loader tests |

### Godot (rendering, editor, UI)

| File | Action | Responsibility |
|------|--------|----------------|
| `godot/Scripts/Config/GameConfig.cs` | Modify | Update `GetGroundColor` for new enums |
| `godot/Scripts/Rendering/GridRenderer.cs` | Modify | Render ground + terrain as separate layers |
| `godot/Scripts/Rendering/GridRenderer.Blocks.cs` | Modify | `DrawTerrainWallBlock` uses `TerrainType` |
| `godot/Scripts/Maps/MapSerializer.cs` | Create | JSON ↔ `MapData` serialization |
| `godot/Scripts/Maps/MapFileManager.cs` | Create | List/save/load `.json` maps from disk |
| `godot/Scripts/Editor/EditorAction.cs` | Create | Undo/redo command interface + cell snapshot |
| `godot/Scripts/Editor/EditorActionStack.cs` | Create | Undo/redo stack management |
| `godot/Scripts/Editor/SymmetryMirror.cs` | Create | Symmetry coordinate transforms + slot mapping |
| `godot/Scripts/Editor/MapEditorScene.cs` | Create | Main editor controller: tools, painting, camera |
| `godot/Scripts/Editor/EditorToolbar.cs` | Create | Tool/slot/symmetry selection UI |
| `godot/Scripts/UI/MainMenu.cs` | Create | Main menu buttons + navigation |
| `godot/Scripts/UI/MapSelectScreen.cs` | Create | List maps, pick one |
| `godot/Scripts/UI/SlotConfigScreen.cs` | Create | Assign players to slots, start game |
| `godot/Scenes/MainMenu.tscn` | Create | Main menu scene (new main scene) |
| `godot/Scenes/MapEditor.tscn` | Create | Editor scene |
| `godot/Scenes/MapSelect.tscn` | Create | Map selection screen |
| `godot/Scenes/SlotConfig.tscn` | Create | Slot configuration screen |

---

## Task 1: Split GroundType into GroundType + TerrainType

**Files:**
- Modify: `src/Blocker.Simulation/Core/Cell.cs`
- Modify: `tests/Blocker.Simulation.Tests/MapLoaderTests.cs`

This is the foundational type change that everything else builds on.

- [ ] **Step 1: Write tests for the new cell model**

Add a new test class that validates the split type behavior:

```csharp
// In tests/Blocker.Simulation.Tests/CellTests.cs
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class CellTests
{
    [Fact]
    public void Cell_DefaultState_IsPassableNormalGround()
    {
        var cell = new Cell();
        Assert.Equal(GroundType.Normal, cell.Ground);
        Assert.Equal(TerrainType.None, cell.Terrain);
        Assert.True(cell.IsPassable);
        Assert.False(cell.IsNestZone);
    }

    [Fact]
    public void Cell_WithTerrain_IsNotPassable()
    {
        var cell = new Cell { Terrain = TerrainType.Terrain };
        Assert.False(cell.IsPassable);
    }

    [Fact]
    public void Cell_BootGroundWithBreakableWall_BothLayersIndependent()
    {
        var cell = new Cell
        {
            Ground = GroundType.Boot,
            Terrain = TerrainType.BreakableWall
        };
        Assert.True(cell.IsNestZone); // Ground layer still knows it's boot
        Assert.False(cell.IsPassable); // Terrain blocks passage
    }

    [Fact]
    public void Cell_WallDestroyed_RevealsGround()
    {
        var cell = new Cell
        {
            Ground = GroundType.Overload,
            Terrain = TerrainType.BreakableWall
        };
        // Simulate wall destruction
        cell.Terrain = TerrainType.None;
        Assert.True(cell.IsPassable);
        Assert.Equal(GroundType.Overload, cell.Ground); // Ground preserved
        Assert.True(cell.IsNestZone);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~CellTests" -v minimal`
Expected: compilation error — `TerrainType` does not exist.

- [ ] **Step 3: Implement the type split in Cell.cs**

Replace the contents of `src/Blocker.Simulation/Core/Cell.cs`:

```csharp
namespace Blocker.Simulation.Core;

public enum GroundType
{
    Normal,
    Boot,
    Overload,
    Proto
}

public enum TerrainType
{
    None,
    Terrain,
    BreakableWall,
    FragileWall
}

public class Cell
{
    public GroundType Ground { get; set; }
    public TerrainType Terrain { get; set; }

    /// <summary>Block ID occupying this cell, or null if empty.</summary>
    public int? BlockId { get; set; }

    public bool IsPassable => Terrain == TerrainType.None;

    public bool IsNestZone => Ground is GroundType.Boot or GroundType.Overload or GroundType.Proto;
}
```

- [ ] **Step 4: Run the new cell tests**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~CellTests" -v minimal`
Expected: 4 passed. (Other tests will fail due to downstream breakage — that's expected and fixed in subsequent steps.)

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Core/Cell.cs tests/Blocker.Simulation.Tests/CellTests.cs
git commit -m "feat: split GroundType into GroundType + TerrainType

Separates floor types (Normal/Boot/Overload/Proto) from obstacles
(Terrain/BreakableWall/FragileWall) so they can coexist on the same cell."
```

---

## Task 2: Update simulation code for split types

**Files:**
- Modify: `src/Blocker.Simulation/Maps/MapLoader.cs:90-99` — `ParseGround` now returns both types
- Modify: `src/Blocker.Simulation/Systems/CombatSystem.cs:247,259` — wall destruction uses `TerrainType`
- Modify: `tests/Blocker.Simulation.Tests/MapLoaderTests.cs:16-38` — update assertions

All code that references the old `GroundType.Terrain`, `GroundType.BreakableWall`, or `GroundType.FragileWall` must switch to `TerrainType`.

- [ ] **Step 1: Update MapLoader.ParseGround to return both types**

In `src/Blocker.Simulation/Maps/MapLoader.cs`, the `ParseGround` method currently returns `GroundType`. Change it to set both ground and terrain on the cell. Replace the ground-parsing loop (lines 39-47) and `ParseGround` method (lines 90-100):

Change the ground-parsing loop body at line 45 from:
```csharp
grid[x, y].Ground = ParseGround(c);
```
to:
```csharp
var (ground, terrain) = ParseCell(c);
grid[x, y].Ground = ground;
grid[x, y].Terrain = terrain;
```

Replace the `ParseGround` method with:
```csharp
private static (GroundType Ground, TerrainType Terrain) ParseCell(char c) => c switch
{
    'f' => (GroundType.Boot, TerrainType.None),
    'o' => (GroundType.Overload, TerrainType.None),
    'p' => (GroundType.Proto, TerrainType.None),
    '#' => (GroundType.Normal, TerrainType.Terrain),
    '~' => (GroundType.Normal, TerrainType.BreakableWall),
    '=' => (GroundType.Normal, TerrainType.FragileWall),
    _ => (GroundType.Normal, TerrainType.None)
};
```

- [ ] **Step 2: Update CombatSystem wall destruction**

In `src/Blocker.Simulation/Systems/CombatSystem.cs`, update `ResolvNeutralObstacles` (around line 247):

Change:
```csharp
if (cell.Ground != GroundType.FragileWall) continue;
```
to:
```csharp
if (cell.Terrain != TerrainType.FragileWall) continue;
```

Change (around line 259):
```csharp
cell.Ground = GroundType.Normal;
```
to:
```csharp
cell.Terrain = TerrainType.None;
```

Also add `using Blocker.Simulation.Core;` if not already present (it likely is via existing usings).

- [ ] **Step 3: Update existing tests that reference old GroundType values**

In `tests/Blocker.Simulation.Tests/MapLoaderTests.cs`, update `LoadTwoLayerMap_ParsesGroundTypes` (lines 32-38):

```csharp
Assert.Equal(GroundType.Normal, state.Grid[0, 0].Ground);
Assert.Equal(GroundType.Boot, state.Grid[1, 0].Ground);
Assert.Equal(TerrainType.Terrain, state.Grid[2, 0].Terrain);  // was GroundType.Terrain
Assert.Equal(GroundType.Overload, state.Grid[0, 1].Ground);
Assert.Equal(GroundType.Proto, state.Grid[2, 1].Ground);
Assert.Equal(TerrainType.BreakableWall, state.Grid[0, 2].Terrain);  // was GroundType.BreakableWall
Assert.Equal(TerrainType.FragileWall, state.Grid[1, 2].Terrain);    // was GroundType.FragileWall
```

In `tests/Blocker.Simulation.Tests/JumperTests.cs` (line 106), change:
```csharp
state.Grid[6, 7].Ground = GroundType.Terrain;
```
to:
```csharp
state.Grid[6, 7].Terrain = TerrainType.Terrain;
```

In `tests/Blocker.Simulation.Tests/PushTests.cs` (line 151), change:
```csharp
state.Grid[7, 7].Ground = GroundType.Terrain;
```
to:
```csharp
state.Grid[7, 7].Terrain = TerrainType.Terrain;
```

In `tests/Blocker.Simulation.Tests/PathfindingTests.cs` (lines 55-57, 94, 122-124), change all:
```csharp
state.Grid[x, y].Ground = GroundType.Terrain;
```
to:
```csharp
state.Grid[x, y].Terrain = TerrainType.Terrain;
```

- [ ] **Step 4: Run all simulation tests**

Run: `dotnet test tests/Blocker.Simulation.Tests -v minimal`
Expected: all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Maps/MapLoader.cs src/Blocker.Simulation/Systems/CombatSystem.cs tests/
git commit -m "refactor: update simulation code for GroundType/TerrainType split

MapLoader.ParseCell returns both types. CombatSystem wall destruction
uses TerrainType. All test assertions updated."
```

---

## Task 3: Update Godot rendering for split types

**Files:**
- Modify: `godot/Scripts/Config/GameConfig.cs:227-236` — `GetGroundColor` removes terrain cases
- Modify: `godot/Scripts/Rendering/GridRenderer.cs:256-257` — terrain check uses `TerrainType`
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs:780-824` — `DrawTerrainWallBlock` uses `TerrainType`

- [ ] **Step 1: Update GameConfig.GetGroundColor**

In `godot/Scripts/Config/GameConfig.cs`, replace the `GetGroundColor` method (lines 227-236):

```csharp
public Color GetGroundColor(GroundType ground) => ground switch
{
    GroundType.Boot => BootGroundColor,
    GroundType.Overload => OverloadGroundColor,
    GroundType.Proto => ProtoGroundColor,
    _ => NormalGroundColor
};
```

- [ ] **Step 2: Update GridRenderer._Draw cell background loop**

In `godot/Scripts/Rendering/GridRenderer.cs`, update the cell rendering loop (around lines 253-258). Change:

```csharp
DrawRect(rect, _config.GetGroundColor(cell.Ground));

// Draw terrain walls as inset blocks (after background, before grid lines)
if (cell.Ground is GroundType.Terrain or GroundType.BreakableWall or GroundType.FragileWall)
    DrawTerrainWallBlock(rect, cell.Ground);
```

to:

```csharp
DrawRect(rect, _config.GetGroundColor(cell.Ground));

// Draw terrain walls as inset blocks (after background, before grid lines)
if (cell.Terrain != TerrainType.None)
    DrawTerrainWallBlock(rect, cell.Terrain);
```

- [ ] **Step 3: Update DrawTerrainWallBlock signature and switch**

In `godot/Scripts/Rendering/GridRenderer.Blocks.cs`, change the method signature (line 780):

```csharp
private void DrawTerrainWallBlock(Rect2 cellRect, TerrainType terrain)
```

Update the switch expression (lines 790-810):

```csharp
var (fill, highlight, shadow, inner) = terrain switch
{
    TerrainType.Terrain => (
        new Color(0.25f, 0.25f, 0.28f),
        new Color(0.35f, 0.35f, 0.38f),
        new Color(0.12f, 0.12f, 0.14f),
        new Color(0.20f, 0.20f, 0.22f)
    ),
    TerrainType.BreakableWall => (
        new Color(0.32f, 0.32f, 0.35f),
        new Color(0.42f, 0.42f, 0.45f),
        new Color(0.18f, 0.18f, 0.20f),
        new Color(0.27f, 0.27f, 0.29f)
    ),
    _ => (
        new Color(0.38f, 0.38f, 0.40f),
        new Color(0.48f, 0.48f, 0.50f),
        new Color(0.24f, 0.24f, 0.26f),
        new Color(0.33f, 0.33f, 0.35f)
    )
};
```

Update the stripe condition (line 824):
```csharp
if (terrain == TerrainType.Terrain)
```

- [ ] **Step 4: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Config/GameConfig.cs godot/Scripts/Rendering/GridRenderer.cs godot/Scripts/Rendering/GridRenderer.Blocks.cs
git commit -m "refactor: update Godot rendering for TerrainType split

GetGroundColor only handles floor types. GridRenderer and
DrawTerrainWallBlock use TerrainType for obstacle rendering."
```

---

## Task 4: Create MapData and SlotAssignment records

**Files:**
- Create: `src/Blocker.Simulation/Maps/MapData.cs`
- Create: `src/Blocker.Simulation/Maps/SlotAssignment.cs`

- [ ] **Step 1: Write tests for MapData construction**

```csharp
// In tests/Blocker.Simulation.Tests/MapDataTests.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Xunit;

namespace Blocker.Simulation.Tests;

public class MapDataTests
{
    [Fact]
    public void MapData_CanBeConstructed()
    {
        var data = new MapData(
            Name: "Test",
            Width: 10,
            Height: 8,
            SlotCount: 2,
            Ground: [new GroundEntry(1, 2, GroundType.Boot)],
            Terrain: [new TerrainEntry(3, 4, TerrainType.Terrain)],
            Units: [new UnitEntry(0, 0, BlockType.Builder, 1)]
        );

        Assert.Equal("Test", data.Name);
        Assert.Equal(10, data.Width);
        Assert.Single(data.Ground);
        Assert.Single(data.Terrain);
        Assert.Single(data.Units);
    }

    [Fact]
    public void SlotAssignment_MapsSlotToPlayer()
    {
        var assignment = new SlotAssignment(SlotId: 1, PlayerId: 0);
        Assert.Equal(1, assignment.SlotId);
        Assert.Equal(0, assignment.PlayerId);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~MapDataTests" -v minimal`
Expected: compilation error — types don't exist.

- [ ] **Step 3: Create MapData.cs**

```csharp
// src/Blocker.Simulation/Maps/MapData.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Maps;

public record MapData(
    string Name,
    int Width,
    int Height,
    int SlotCount,
    List<GroundEntry> Ground,
    List<TerrainEntry> Terrain,
    List<UnitEntry> Units
);

public record GroundEntry(int X, int Y, GroundType Type);
public record TerrainEntry(int X, int Y, TerrainType Type);
public record UnitEntry(int X, int Y, BlockType Type, int SlotId);
```

- [ ] **Step 4: Create SlotAssignment.cs**

```csharp
// src/Blocker.Simulation/Maps/SlotAssignment.cs
namespace Blocker.Simulation.Maps;

public record SlotAssignment(int SlotId, int PlayerId);
```

- [ ] **Step 5: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~MapDataTests" -v minimal`
Expected: 2 passed.

- [ ] **Step 6: Commit**

```bash
git add src/Blocker.Simulation/Maps/MapData.cs src/Blocker.Simulation/Maps/SlotAssignment.cs tests/Blocker.Simulation.Tests/MapDataTests.cs
git commit -m "feat: add MapData and SlotAssignment records

Pure C# data structures for the new sparse map format.
MapData holds ground/terrain/unit layers. SlotAssignment maps
spawn slots to player IDs."
```

---

## Task 5: Add MapLoader.Load(MapData) overload

**Files:**
- Modify: `src/Blocker.Simulation/Maps/MapLoader.cs`
- Modify: `tests/Blocker.Simulation.Tests/MapLoaderTests.cs`

- [ ] **Step 1: Write tests for MapData-based loading**

Add to `tests/Blocker.Simulation.Tests/MapLoaderTests.cs`:

```csharp
[Fact]
public void LoadMapData_CreatesGridWithCorrectDimensions()
{
    var data = new MapData("Test", 20, 15, 2, [], [], []);
    var assignments = new List<SlotAssignment>
    {
        new(SlotId: 1, PlayerId: 0),
        new(SlotId: 2, PlayerId: 1)
    };

    var state = MapLoader.Load(data, assignments);

    Assert.Equal(20, state.Grid.Width);
    Assert.Equal(15, state.Grid.Height);
}

[Fact]
public void LoadMapData_AppliesGroundAndTerrain()
{
    var data = new MapData("Test", 10, 10, 2,
        Ground: [new GroundEntry(1, 2, GroundType.Boot), new GroundEntry(3, 4, GroundType.Overload)],
        Terrain: [new TerrainEntry(5, 6, TerrainType.Terrain), new TerrainEntry(3, 4, TerrainType.BreakableWall)],
        Units: []
    );

    var state = MapLoader.Load(data, []);

    Assert.Equal(GroundType.Boot, state.Grid[1, 2].Ground);
    Assert.Equal(GroundType.Normal, state.Grid[0, 0].Ground); // default
    Assert.Equal(TerrainType.Terrain, state.Grid[5, 6].Terrain);
    // Overload ground with breakable wall on top
    Assert.Equal(GroundType.Overload, state.Grid[3, 4].Ground);
    Assert.Equal(TerrainType.BreakableWall, state.Grid[3, 4].Terrain);
}

[Fact]
public void LoadMapData_PlacesUnitsWithSlotMapping()
{
    var data = new MapData("Test", 10, 10, 2,
        Ground: [],
        Terrain: [],
        Units: [
            new UnitEntry(1, 1, BlockType.Builder, 1),
            new UnitEntry(8, 8, BlockType.Soldier, 2)
        ]
    );
    var assignments = new List<SlotAssignment>
    {
        new(SlotId: 1, PlayerId: 0),
        new(SlotId: 2, PlayerId: 3)
    };

    var state = MapLoader.Load(data, assignments);

    Assert.Equal(2, state.Blocks.Count);
    var b = state.GetBlockAt(new GridPos(1, 1));
    Assert.NotNull(b);
    Assert.Equal(BlockType.Builder, b.Type);
    Assert.Equal(0, b.PlayerId); // Slot 1 → Player 0

    var s = state.GetBlockAt(new GridPos(8, 8));
    Assert.NotNull(s);
    Assert.Equal(BlockType.Soldier, s.Type);
    Assert.Equal(3, s.PlayerId); // Slot 2 �� Player 3
}

[Fact]
public void LoadMapData_CreatesPlayersFromAssignments()
{
    var data = new MapData("Test", 10, 10, 3,
        Ground: [],
        Terrain: [],
        Units: [
            new UnitEntry(1, 1, BlockType.Builder, 1),
            new UnitEntry(5, 5, BlockType.Builder, 2),
            new UnitEntry(8, 8, BlockType.Builder, 3)
        ]
    );
    var assignments = new List<SlotAssignment>
    {
        new(1, 0), new(2, 1), new(3, 2)
    };

    var state = MapLoader.Load(data, assignments);

    Assert.Equal(3, state.Players.Count);
    Assert.Contains(state.Players, p => p.Id == 0);
    Assert.Contains(state.Players, p => p.Id == 1);
    Assert.Contains(state.Players, p => p.Id == 2);
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~MapLoaderTests.LoadMapData" -v minimal`
Expected: compilation error — `MapLoader.Load(MapData, ...)` doesn't exist.

- [ ] **Step 3: Implement Load(MapData, List\<SlotAssignment\>)**

Add this method to `src/Blocker.Simulation/Maps/MapLoader.cs`:

```csharp
/// <summary>
/// Load a map from structured MapData with slot-to-player assignments.
/// </summary>
public static GameState Load(MapData data, List<SlotAssignment> assignments)
{
    var grid = new Grid(data.Width, data.Height);
    var state = new GameState(grid);

    // Apply ground layer
    foreach (var entry in data.Ground)
    {
        if (grid.InBounds(entry.X, entry.Y))
            grid[entry.X, entry.Y].Ground = entry.Type;
    }

    // Apply terrain layer
    foreach (var entry in data.Terrain)
    {
        if (grid.InBounds(entry.X, entry.Y))
            grid[entry.X, entry.Y].Terrain = entry.Type;
    }

    // Build slot → player lookup
    var slotToPlayer = assignments.ToDictionary(a => a.SlotId, a => a.PlayerId);

    // Place units
    foreach (var entry in data.Units)
    {
        if (!grid.InBounds(entry.X, entry.Y)) continue;
        if (!slotToPlayer.TryGetValue(entry.SlotId, out int playerId)) continue;

        state.AddBlock(entry.Type, playerId, new GridPos(entry.X, entry.Y));
    }

    // Create players from assignments
    foreach (var assignment in assignments)
    {
        if (state.Players.All(p => p.Id != assignment.PlayerId))
            state.Players.Add(new Player { Id = assignment.PlayerId, TeamId = assignment.PlayerId });
    }

    return state;
}
```

- [ ] **Step 4: Run all MapLoader tests**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~MapLoaderTests" -v minimal`
Expected: all pass (both legacy and new tests).

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Maps/MapLoader.cs tests/Blocker.Simulation.Tests/MapLoaderTests.cs
git commit -m "feat: add MapLoader.Load(MapData, assignments) overload

Loads maps from structured MapData with slot-to-player mapping.
Supports independent ground/terrain/unit layers. Legacy text
loader preserved for test compatibility."
```

---

## Task 6: Create MapSerializer (JSON ↔ MapData)

**Files:**
- Create: `godot/Scripts/Maps/MapSerializer.cs`

This runs on the Godot side and uses `System.Text.Json` to serialize/deserialize JSON ↔ `MapData`.

- [ ] **Step 1: Create MapSerializer.cs**

```csharp
// godot/Scripts/Maps/MapSerializer.cs
using System.Text.Json;
using System.Text.Json.Serialization;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;

namespace Blocker.Game.Maps;

public static class MapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(MapData data)
    {
        var json = new JsonMapFile
        {
            Meta = new JsonMeta
            {
                Name = data.Name,
                Version = 1,
                Width = data.Width,
                Height = data.Height,
                Slots = data.SlotCount
            },
            Ground = data.Ground.Select(g => new JsonGroundEntry { X = g.X, Y = g.Y, Type = g.Type }).ToList(),
            Terrain = data.Terrain.Select(t => new JsonTerrainEntry { X = t.X, Y = t.Y, Type = t.Type }).ToList(),
            Units = data.Units.Select(u => new JsonUnitEntry { X = u.X, Y = u.Y, Type = u.Type, Slot = u.SlotId }).ToList()
        };
        return JsonSerializer.Serialize(json, Options);
    }

    public static MapData Deserialize(string json)
    {
        var file = JsonSerializer.Deserialize<JsonMapFile>(json, Options)
            ?? throw new JsonException("Failed to deserialize map file");

        return new MapData(
            Name: file.Meta.Name,
            Width: file.Meta.Width,
            Height: file.Meta.Height,
            SlotCount: file.Meta.Slots,
            Ground: file.Ground.Select(g => new GroundEntry(g.X, g.Y, g.Type)).ToList(),
            Terrain: file.Terrain.Select(t => new TerrainEntry(t.X, t.Y, t.Type)).ToList(),
            Units: file.Units.Select(u => new UnitEntry(u.X, u.Y, u.Type, u.Slot)).ToList()
        );
    }

    // Internal JSON DTOs
    private class JsonMapFile
    {
        public JsonMeta Meta { get; set; } = new();
        public List<JsonGroundEntry> Ground { get; set; } = [];
        public List<JsonTerrainEntry> Terrain { get; set; } = [];
        public List<JsonUnitEntry> Units { get; set; } = [];
    }

    private class JsonMeta
    {
        public string Name { get; set; } = "";
        public int Version { get; set; } = 1;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Slots { get; set; }
    }

    private class JsonGroundEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public GroundType Type { get; set; }
    }

    private class JsonTerrainEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public TerrainType Type { get; set; }
    }

    private class JsonUnitEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public BlockType Type { get; set; }
        public int Slot { get; set; }
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Maps/MapSerializer.cs
git commit -m "feat: add MapSerializer for JSON map format

Serializes/deserializes MapData to/from sparse JSON using
System.Text.Json. Uses camelCase naming and string enums."
```

---

## Task 7: Create MapFileManager (list/save/load)

**Files:**
- Create: `godot/Scripts/Maps/MapFileManager.cs`

- [ ] **Step 1: Create MapFileManager.cs**

```csharp
// godot/Scripts/Maps/MapFileManager.cs
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.Maps;

/// <summary>
/// Manages loading/saving map JSON files from the maps directory.
/// </summary>
public static class MapFileManager
{
    public const string MapsDirectory = "user://Maps";

    /// <summary>
    /// Ensures the maps directory exists. Call once at startup.
    /// </summary>
    public static void EnsureDirectoryExists()
    {
        if (!DirAccess.DirExistsAbsolute(MapsDirectory))
            DirAccess.MakeDirRecursiveAbsolute(MapsDirectory);
    }

    /// <summary>
    /// Lists all .json map files in the maps directory. Returns file names without path.
    /// </summary>
    public static List<string> ListMaps()
    {
        var maps = new List<string>();
        var dir = DirAccess.Open(MapsDirectory);
        if (dir == null) return maps;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                maps.Add(fileName);
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
        maps.Sort();
        return maps;
    }

    /// <summary>
    /// Saves a MapData to a .json file in the maps directory.
    /// </summary>
    public static void Save(MapData data, string fileName)
    {
        EnsureDirectoryExists();
        var json = MapSerializer.Serialize(data);
        var path = MapsDirectory.PathJoin(fileName);
        var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PrintErr($"Failed to save map to {path}: {FileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(json);
        file.Close();
        GD.Print($"Map saved to {path}");
    }

    /// <summary>
    /// Loads a MapData from a .json file in the maps directory.
    /// </summary>
    public static MapData? Load(string fileName)
    {
        var path = MapsDirectory.PathJoin(fileName);
        if (!FileAccess.FileExists(path))
        {
            GD.PrintErr($"Map file not found: {path}");
            return null;
        }

        var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"Failed to open map: {path}: {FileAccess.GetOpenError()}");
            return null;
        }
        var json = file.GetAsText();
        file.Close();
        return MapSerializer.Deserialize(json);
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Maps/MapFileManager.cs
git commit -m "feat: add MapFileManager for map file I/O

Lists, saves, and loads .json map files from user://Maps
directory using Godot's FileAccess API."
```

---

## Task 8: Create undo/redo system (EditorAction + EditorActionStack)

**Files:**
- Create: `godot/Scripts/Editor/EditorAction.cs`
- Create: `godot/Scripts/Editor/EditorActionStack.cs`

- [ ] **Step 1: Create EditorAction.cs**

```csharp
// godot/Scripts/Editor/EditorAction.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Game.Editor;

/// <summary>
/// Snapshot of a single cell's state before/after an edit.
/// </summary>
public record CellSnapshot(int X, int Y, GroundType Ground, TerrainType Terrain, BlockType? UnitType, int? UnitSlot);

/// <summary>
/// A reversible editor action. Stores before/after snapshots of all affected cells.
/// </summary>
public class EditorAction
{
    public List<CellSnapshot> Before { get; } = [];
    public List<CellSnapshot> After { get; } = [];
}
```

- [ ] **Step 2: Create EditorActionStack.cs**

```csharp
// godot/Scripts/Editor/EditorActionStack.cs
namespace Blocker.Game.Editor;

/// <summary>
/// Manages undo/redo stacks for the map editor.
/// </summary>
public class EditorActionStack
{
    private readonly List<EditorAction> _undoStack = [];
    private readonly List<EditorAction> _redoStack = [];
    private const int MaxDepth = 200;

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>
    /// Pushes a completed action onto the undo stack and clears redo.
    /// </summary>
    public void Push(EditorAction action)
    {
        _undoStack.Add(action);
        _redoStack.Clear();
        if (_undoStack.Count > MaxDepth)
            _undoStack.RemoveAt(0);
    }

    /// <summary>
    /// Pops the last action from the undo stack and returns it.
    /// The caller is responsible for applying the Before snapshots.
    /// </summary>
    public EditorAction? Undo()
    {
        if (_undoStack.Count == 0) return null;
        var action = _undoStack[^1];
        _undoStack.RemoveAt(_undoStack.Count - 1);
        _redoStack.Add(action);
        return action;
    }

    /// <summary>
    /// Pops the last action from the redo stack and returns it.
    /// The caller is responsible for applying the After snapshots.
    /// </summary>
    public EditorAction? Redo()
    {
        if (_redoStack.Count == 0) return null;
        var action = _redoStack[^1];
        _redoStack.RemoveAt(_redoStack.Count - 1);
        _undoStack.Add(action);
        return action;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
```

- [ ] **Step 3: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Editor/EditorAction.cs godot/Scripts/Editor/EditorActionStack.cs
git commit -m "feat: add editor undo/redo system

EditorAction stores before/after cell snapshots.
EditorActionStack manages undo/redo with 200-action depth limit."
```

---

## Task 9: Create SymmetryMirror

**Files:**
- Create: `godot/Scripts/Editor/SymmetryMirror.cs`

- [ ] **Step 1: Create SymmetryMirror.cs**

```csharp
// godot/Scripts/Editor/SymmetryMirror.cs
namespace Blocker.Game.Editor;

[Flags]
public enum SymmetryMode
{
    None = 0,
    LeftRight = 1,       // Mirror across vertical center
    TopBottom = 2,       // Mirror across horizontal center
    DiagonalTLBR = 4,   // Mirror across TL-BR diagonal
    DiagonalTRBL = 8    // Mirror across TR-BL diagonal
}

/// <summary>
/// Computes mirrored positions and slot mappings for symmetry painting.
/// </summary>
public class SymmetryMirror
{
    public SymmetryMode Mode { get; set; } = SymmetryMode.None;

    /// <summary>
    /// Maps source slot → mirrored slot for each symmetry axis.
    /// Key is source slot (1-6), value is the mirror target slot.
    /// </summary>
    public Dictionary<int, int> SlotMirrorMap { get; } = new();

    /// <summary>
    /// Given a position, returns all mirrored positions (including the original).
    /// </summary>
    public List<(int X, int Y, int Slot)> GetMirroredPositions(int x, int y, int slot, int mapWidth, int mapHeight)
    {
        var results = new HashSet<(int X, int Y, int Slot)> { (x, y, slot) };

        if (Mode == SymmetryMode.None)
            return results.ToList();

        // Generate all mirror combinations
        var current = results.ToList();
        foreach (var pos in current)
        {
            if (Mode.HasFlag(SymmetryMode.LeftRight))
            {
                int mx = mapWidth - 1 - pos.X;
                int mirrorSlot = GetMirrorSlot(pos.Slot);
                results.Add((mx, pos.Y, mirrorSlot));
            }
            if (Mode.HasFlag(SymmetryMode.TopBottom))
            {
                int my = mapHeight - 1 - pos.Y;
                int mirrorSlot = GetMirrorSlot(pos.Slot);
                results.Add((pos.X, my, mirrorSlot));
            }
            if (Mode.HasFlag(SymmetryMode.DiagonalTLBR))
            {
                // Only works well on square-ish maps; swap x/y
                if (pos.Y < mapWidth && pos.X < mapHeight)
                {
                    int mirrorSlot = GetMirrorSlot(pos.Slot);
                    results.Add((pos.Y, pos.X, mirrorSlot));
                }
            }
            if (Mode.HasFlag(SymmetryMode.DiagonalTRBL))
            {
                int mx = mapWidth - 1 - pos.Y;
                int my = mapHeight - 1 - pos.X;
                if (mx >= 0 && mx < mapWidth && my >= 0 && my < mapHeight)
                {
                    int mirrorSlot = GetMirrorSlot(pos.Slot);
                    results.Add((mx, my, mirrorSlot));
                }
            }
        }

        // Second pass: apply combined symmetries (e.g., LR+TB = 4-way)
        if (Mode.HasFlag(SymmetryMode.LeftRight) && Mode.HasFlag(SymmetryMode.TopBottom))
        {
            var pass2 = results.ToList();
            foreach (var pos in pass2)
            {
                int mx = mapWidth - 1 - pos.X;
                int my = mapHeight - 1 - pos.Y;
                int mirrorSlot = GetMirrorSlot(GetMirrorSlot(pos.Slot));
                results.Add((mx, my, mirrorSlot));
            }
        }

        return results.ToList();
    }

    private int GetMirrorSlot(int sourceSlot)
    {
        return SlotMirrorMap.TryGetValue(sourceSlot, out int target) ? target : sourceSlot;
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Editor/SymmetryMirror.cs
git commit -m "feat: add SymmetryMirror for editor symmetry painting

Supports LR, TB, and diagonal mirror modes with combinable flags.
Maps slot IDs through configurable mirror mappings."
```

---

## Task 10: Create MainMenu scene and script

**Files:**
- Create: `godot/Scripts/UI/MainMenu.cs`
- Create: `godot/Scenes/MainMenu.tscn`

- [ ] **Step 1: Create MainMenu.cs**

```csharp
// godot/Scripts/UI/MainMenu.cs
using Godot;

namespace Blocker.Game.UI;

public partial class MainMenu : Control
{
    public override void _Ready()
    {
        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -150, OffsetRight = 150,
            OffsetTop = -100, OffsetBottom = 100,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both
        };
        vbox.AddThemeConstantOverride("separation", 16);
        AddChild(vbox);

        var title = new Label
        {
            Text = "BLOCKER",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 48);
        vbox.AddChild(title);

        vbox.AddChild(new HSeparator());

        var playTestBtn = new Button { Text = "Play Test", CustomMinimumSize = new Vector2(0, 50) };
        playTestBtn.Pressed += OnPlayTestPressed;
        vbox.AddChild(playTestBtn);

        var playVsAiBtn = new Button { Text = "Play vs AI", CustomMinimumSize = new Vector2(0, 50) };
        playVsAiBtn.Pressed += OnPlayVsAiPressed;
        vbox.AddChild(playVsAiBtn);

        var editorBtn = new Button { Text = "Map Editor", CustomMinimumSize = new Vector2(0, 50) };
        editorBtn.Pressed += OnMapEditorPressed;
        vbox.AddChild(editorBtn);
    }

    private void OnPlayTestPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    private void OnPlayVsAiPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
    }

    private void OnMapEditorPressed()
    {
        GetTree().ChangeSceneToFile("res://Scenes/MapEditor.tscn");
    }
}
```

- [ ] **Step 2: Create MainMenu.tscn**

```tscn
[gd_scene format=3]

[ext_resource type="Script" path="res://Scripts/UI/MainMenu.cs" id="1_menu"]

[node name="MainMenu" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_menu")
```

- [ ] **Step 3: Update project.godot to set MainMenu as the main scene**

In `godot/project.godot`, find the line:
```
run/main_scene="res://Scenes/Main.tscn"
```
Change it to:
```
run/main_scene="res://Scenes/MainMenu.tscn"
```

- [ ] **Step 4: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/UI/MainMenu.cs godot/Scenes/MainMenu.tscn godot/project.godot
git commit -m "feat: add main menu scene

Three buttons: Play Test (current gameplay), Play vs AI (map select),
Map Editor. Set as the new main scene."
```

---

## Task 11: Create MapSelect screen

**Files:**
- Create: `godot/Scripts/UI/MapSelectScreen.cs`
- Create: `godot/Scenes/MapSelect.tscn`

- [ ] **Step 1: Create MapSelectScreen.cs**

```csharp
// godot/Scripts/UI/MapSelectScreen.cs
using Blocker.Game.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class MapSelectScreen : Control
{
    private ItemList _mapList = null!;

    public override void _Ready()
    {
        MapFileManager.EnsureDirectoryExists();

        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.05f, AnchorBottom = 0.95f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both
        };
        vbox.AddThemeConstantOverride("separation", 12);
        AddChild(vbox);

        // Header row
        var header = new HBoxContainer();
        vbox.AddChild(header);

        var backBtn = new Button { Text = "< Back" };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        header.AddChild(backBtn);

        var title = new Label { Text = "Select Map", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 32);
        header.AddChild(title);

        // Spacer to balance the back button
        header.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });

        // Map list
        _mapList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300)
        };
        _mapList.ItemActivated += OnMapActivated;
        vbox.AddChild(_mapList);

        // Start button
        var startBtn = new Button { Text = "Select", CustomMinimumSize = new Vector2(0, 50) };
        startBtn.Pressed += OnSelectPressed;
        vbox.AddChild(startBtn);

        RefreshMapList();
    }

    private void RefreshMapList()
    {
        _mapList.Clear();
        var maps = MapFileManager.ListMaps();
        foreach (var map in maps)
            _mapList.AddItem(map);

        if (maps.Count == 0)
            _mapList.AddItem("(No maps found — create one in the editor)");
    }

    private void OnMapActivated(long index)
    {
        OnSelectPressed();
    }

    private void OnSelectPressed()
    {
        var selected = _mapList.GetSelectedItems();
        if (selected.Length == 0) return;

        var fileName = _mapList.GetItemText(selected[0]);
        if (fileName.StartsWith("(")) return; // placeholder text

        // Store selected map for the next screen
        MapSelection.SelectedMapFileName = fileName;
        GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
    }
}

/// <summary>
/// Static holder for passing the selected map between scenes.
/// </summary>
public static class MapSelection
{
    public static string? SelectedMapFileName { get; set; }
}
```

- [ ] **Step 2: Create MapSelect.tscn**

```tscn
[gd_scene format=3]

[ext_resource type="Script" path="res://Scripts/UI/MapSelectScreen.cs" id="1_select"]

[node name="MapSelect" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_select")
```

- [ ] **Step 3: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/UI/MapSelectScreen.cs godot/Scenes/MapSelect.tscn
git commit -m "feat: add map selection screen

Lists .json maps from user://Maps. Double-click or select+click
to proceed to slot configuration."
```

---

## Task 12: Create SlotConfig screen

**Files:**
- Create: `godot/Scripts/UI/SlotConfigScreen.cs`
- Create: `godot/Scenes/SlotConfig.tscn`

- [ ] **Step 1: Create SlotConfigScreen.cs**

```csharp
// godot/Scripts/UI/SlotConfigScreen.cs
using Blocker.Game.Config;
using Blocker.Game.Maps;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class SlotConfigScreen : Control
{
    private MapData? _mapData;
    private readonly Dictionary<int, string> _slotAssignments = new(); // slot → "Player" or "AI (inactive)"
    private VBoxContainer _slotContainer = null!;
    private int _playerSlot = 1;

    public override void _Ready()
    {
        if (MapSelection.SelectedMapFileName == null)
        {
            GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
            return;
        }

        _mapData = MapFileManager.Load(MapSelection.SelectedMapFileName);
        if (_mapData == null)
        {
            GD.PrintErr("Failed to load selected map");
            GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
            return;
        }

        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.1f, AnchorRight = 0.9f,
            AnchorTop = 0.05f, AnchorBottom = 0.95f,
            GrowHorizontal = GrowDirection.Both,
            GrowVertical = GrowDirection.Both
        };
        vbox.AddThemeConstantOverride("separation", 12);
        AddChild(vbox);

        // Header
        var header = new HBoxContainer();
        vbox.AddChild(header);

        var backBtn = new Button { Text = "< Back" };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
        header.AddChild(backBtn);

        var title = new Label
        {
            Text = $"Configure: {_mapData.Name}",
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        header.AddChild(title);
        header.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });

        // Map info
        var info = new Label { Text = $"Size: {_mapData.Width}x{_mapData.Height} — {_mapData.SlotCount} slots" };
        vbox.AddChild(info);

        vbox.AddChild(new HSeparator());

        // Slot list
        _slotContainer = new VBoxContainer();
        _slotContainer.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(_slotContainer);

        // Initialize slots: slot 1 = player, rest = AI
        for (int i = 1; i <= _mapData.SlotCount; i++)
        {
            _slotAssignments[i] = i == 1 ? "Player" : "AI (inactive)";
        }
        RebuildSlotList();

        vbox.AddChild(new HSeparator());

        // Start button
        var startBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50) };
        startBtn.Pressed += OnStartPressed;
        vbox.AddChild(startBtn);
    }

    private void RebuildSlotList()
    {
        foreach (var child in _slotContainer.GetChildren())
            child.QueueFree();

        for (int i = 1; i <= _mapData!.SlotCount; i++)
        {
            var row = new HBoxContainer();

            var label = new Label
            {
                Text = $"Slot {i}:",
                CustomMinimumSize = new Vector2(80, 0)
            };
            row.AddChild(label);

            var btn = new Button
            {
                Text = _slotAssignments[i],
                CustomMinimumSize = new Vector2(200, 40),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            int slot = i; // capture for lambda
            btn.Pressed += () => ToggleSlotAssignment(slot);
            row.AddChild(btn);

            _slotContainer.AddChild(row);
        }
    }

    private void ToggleSlotAssignment(int slot)
    {
        if (_slotAssignments[slot] == "Player")
        {
            _slotAssignments[slot] = "AI (inactive)";
            // Don't unset _playerSlot — they might click another
        }
        else
        {
            // Unassign player from previous slot
            foreach (var key in _slotAssignments.Keys.ToList())
            {
                if (_slotAssignments[key] == "Player")
                    _slotAssignments[key] = "AI (inactive)";
            }
            _slotAssignments[slot] = "Player";
            _playerSlot = slot;
        }
        RebuildSlotList();
    }

    private void OnStartPressed()
    {
        if (_mapData == null) return;

        // Build assignments — only the player slot gets a real player for now
        var assignments = new List<SlotAssignment>();
        for (int i = 1; i <= _mapData.SlotCount; i++)
        {
            // Player always gets ID 0; AI slots get IDs 1..N
            if (_slotAssignments[i] == "Player")
                assignments.Add(new SlotAssignment(i, 0));
            else
                assignments.Add(new SlotAssignment(i, i)); // AI placeholder
        }

        // Store for GameManager to pick up
        GameLaunchData.MapData = _mapData;
        GameLaunchData.Assignments = assignments;
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }
}

/// <summary>
/// Static holder for passing launch data to GameManager.
/// </summary>
public static class GameLaunchData
{
    public static MapData? MapData { get; set; }
    public static List<SlotAssignment>? Assignments { get; set; }
}
```

- [ ] **Step 2: Create SlotConfig.tscn**

```tscn
[gd_scene format=3]

[ext_resource type="Script" path="res://Scripts/UI/SlotConfigScreen.cs" id="1_config"]

[node name="SlotConfig" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
grow_horizontal = 2
grow_vertical = 2
script = ExtResource("1_config")
```

- [ ] **Step 3: Update GameManager to accept MapData launch data**

In `godot/Scripts/Game/GameManager.cs`, update the `_Ready` method to check for `GameLaunchData` before falling back to the legacy file-based loading. Replace the map loading section (around lines 31-46):

```csharp
// Load map — either from GameLaunchData (Play vs AI flow) or legacy file
GameState gameState;
if (GameLaunchData.MapData != null && GameLaunchData.Assignments != null)
{
    gameState = MapLoader.Load(GameLaunchData.MapData, GameLaunchData.Assignments);
    GD.Print($"Map loaded from launcher: {GameLaunchData.MapData.Name} " +
             $"{gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks");
    GameLaunchData.MapData = null;
    GameLaunchData.Assignments = null;
}
else
{
    var absolutePath = ProjectSettings.GlobalizePath(MapPath);
    if (!Godot.FileAccess.FileExists(MapPath) && !System.IO.File.Exists(absolutePath))
    {
        absolutePath = System.IO.Path.Combine(
            System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://"))!,
            "..", "maps", "test-small.txt"
        );
    }
    GD.Print($"Loading map from: {absolutePath}");
    gameState = MapLoader.LoadFromFile(absolutePath);
}
GD.Print($"Map loaded: {gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks, {gameState.Players.Count} players");
```

Add using at top:
```csharp
using Blocker.Game.UI;
```

- [ ] **Step 4: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/UI/SlotConfigScreen.cs godot/Scenes/SlotConfig.tscn godot/Scripts/Game/GameManager.cs
git commit -m "feat: add slot configuration screen and game launch flow

SlotConfigScreen lets player assign themselves to a slot.
GameManager picks up MapData from GameLaunchData when launched
via Play vs AI flow, falls back to legacy file loading."
```

---

## Task 13: Create Map Editor — core painting and camera

**Files:**
- Create: `godot/Scripts/Editor/MapEditorScene.cs`
- Create: `godot/Scenes/MapEditor.tscn`

This is the largest task — the main editor controller that handles tool selection, painting, unit placement, camera, and undo/redo.

- [ ] **Step 1: Create MapEditorScene.cs**

```csharp
// godot/Scripts/Editor/MapEditorScene.cs
using Blocker.Game.Config;
using Blocker.Game.Maps;
using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.Editor;

public enum EditorTool
{
    GroundPaint,
    TerrainPaint,
    UnitPlace,
    Eraser
}

public partial class MapEditorScene : Node2D
{
    private GridRenderer _gridRenderer = null!;
    private Camera2D _camera = null!;
    private GameState _editorState = null!;
    private EditorToolbar _toolbar = null!;
    private EditorActionStack _actionStack = new();
    private SymmetryMirror _symmetry = new();

    // Current editor state
    private EditorTool _currentTool = EditorTool.GroundPaint;
    private GroundType _selectedGround = GroundType.Boot;
    private TerrainType _selectedTerrain = TerrainType.Terrain;
    private BlockType _selectedBlock = BlockType.Builder;
    private int _activeSlot = 1;

    // Map metadata
    private string _mapName = "Untitled";
    private int _slotCount = 2;
    private string? _currentFileName;
    private bool _hasUnsavedChanges;

    // Drag painting state
    private bool _isDragging;
    private EditorAction? _currentDragAction;
    private HashSet<(int X, int Y)> _dragVisited = new();

    // Camera drag state
    private bool _isMiddleDragging;
    private Vector2 _middleDragStart;
    private Vector2 _cameraStartPos;

    // Map dimensions
    private int _mapWidth = 41;
    private int _mapHeight = 25;

    public override void _Ready()
    {
        // Create editor grid state
        _editorState = new GameState(new Grid(_mapWidth, _mapHeight));
        Constants.Initialize(GameConfig.CreateDefault().ToSimulationConfig());

        // GridRenderer
        _gridRenderer = new GridRenderer { Name = "GridRenderer" };
        AddChild(_gridRenderer);
        _gridRenderer.SetConfig(GameConfig.CreateDefault());
        _gridRenderer.SetGameState(_editorState);

        // Camera
        _camera = new Camera2D
        {
            Name = "EditorCamera",
            Zoom = new Vector2(1.3f, 1.3f)
        };
        AddChild(_camera);
        CenterCamera();

        // Toolbar (UI layer)
        _toolbar = new EditorToolbar { Name = "EditorToolbar" };
        var canvasLayer = new CanvasLayer { Name = "UILayer" };
        canvasLayer.AddChild(_toolbar);
        AddChild(canvasLayer);

        _toolbar.ToolSelected += OnToolSelected;
        _toolbar.GroundSelected += g => _selectedGround = g;
        _toolbar.TerrainSelected += t => _selectedTerrain = t;
        _toolbar.BlockSelected += b => _selectedBlock = b;
        _toolbar.SlotSelected += s => _activeSlot = s;
        _toolbar.SymmetryChanged += mode => _symmetry.Mode = mode;
        _toolbar.SlotMirrorChanged += (src, dst) => _symmetry.SlotMirrorMap[src] = dst;
        _toolbar.NewMapRequested += OnNewMap;
        _toolbar.SaveRequested += OnSave;
        _toolbar.LoadRequested += OnLoad;
        _toolbar.BackRequested += OnBack;
        _toolbar.MapNameChanged += name => { _mapName = name; _hasUnsavedChanges = true; };
        _toolbar.SlotCountChanged += count => { _slotCount = count; _hasUnsavedChanges = true; };
    }

    private void CenterCamera()
    {
        var gridPixelW = _mapWidth * GridRenderer.CellSize;
        var gridPixelH = _mapHeight * GridRenderer.CellSize;
        _camera.Position = new Vector2(gridPixelW * 0.5f, gridPixelH * 0.5f);
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        // Zoom
        if (@event is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.WheelUp)
            {
                _camera.Zoom *= 1.1f;
                GetViewport().SetInputAsHandled();
            }
            else if (mb.ButtonIndex == MouseButton.WheelDown)
            {
                _camera.Zoom *= 0.9f;
                _camera.Zoom = new Vector2(Mathf.Max(_camera.Zoom.X, 0.2f), Mathf.Max(_camera.Zoom.Y, 0.2f));
                GetViewport().SetInputAsHandled();
            }
            // Middle mouse drag for panning
            else if (mb.ButtonIndex == MouseButton.Middle)
            {
                if (mb.Pressed)
                {
                    _isMiddleDragging = true;
                    _middleDragStart = mb.GlobalPosition;
                    _cameraStartPos = _camera.Position;
                }
                else
                {
                    _isMiddleDragging = false;
                }
                GetViewport().SetInputAsHandled();
            }
            // Left click — paint/place
            else if (mb.ButtonIndex == MouseButton.Left)
            {
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _currentDragAction = new EditorAction();
                    _dragVisited.Clear();
                    ApplyToolAt(GetGridPos(mb.GlobalPosition));
                }
                else
                {
                    FinishDrag();
                }
                GetViewport().SetInputAsHandled();
            }
            // Right click — erase
            else if (mb.ButtonIndex == MouseButton.Right)
            {
                if (mb.Pressed)
                {
                    _isDragging = true;
                    _currentDragAction = new EditorAction();
                    _dragVisited.Clear();
                    var prevTool = _currentTool;
                    _currentTool = EditorTool.Eraser;
                    ApplyToolAt(GetGridPos(mb.GlobalPosition));
                    _currentTool = prevTool;
                }
                else
                {
                    FinishDrag();
                }
                GetViewport().SetInputAsHandled();
            }
        }

        // Mouse motion for drag painting and middle-drag panning
        if (@event is InputEventMouseMotion mm)
        {
            if (_isMiddleDragging)
            {
                var delta = (mm.GlobalPosition - _middleDragStart) / _camera.Zoom;
                _camera.Position = _cameraStartPos - delta;
                GetViewport().SetInputAsHandled();
            }
            else if (_isDragging)
            {
                var pos = GetGridPos(mm.GlobalPosition);
                if (Input.IsMouseButtonPressed(MouseButton.Right))
                {
                    var prevTool = _currentTool;
                    _currentTool = EditorTool.Eraser;
                    ApplyToolAt(pos);
                    _currentTool = prevTool;
                }
                else
                {
                    ApplyToolAt(pos);
                }
                GetViewport().SetInputAsHandled();
            }
        }

        // Keyboard shortcuts
        if (@event is InputEventKey key && key.Pressed)
        {
            // Ctrl+Z = undo, Ctrl+Shift+Z = redo
            if (key.CtrlPressed && key.Keycode == Key.Z)
            {
                if (key.ShiftPressed)
                    PerformRedo();
                else
                    PerformUndo();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private (int X, int Y) GetGridPos(Vector2 globalPos)
    {
        var localPos = _gridRenderer.ToLocal(GetGlobalMousePosition());
        int x = (int)(localPos.X / GridRenderer.CellSize);
        int y = (int)(localPos.Y / GridRenderer.CellSize);
        return (x, y);
    }

    private void ApplyToolAt((int X, int Y) pos)
    {
        var positions = _symmetry.GetMirroredPositions(pos.X, pos.Y, _activeSlot, _mapWidth, _mapHeight);

        foreach (var (mx, my, slot) in positions)
        {
            if (mx < 0 || mx >= _mapWidth || my < 0 || my >= _mapHeight) continue;
            if (_dragVisited.Contains((mx, my))) continue;
            _dragVisited.Add((mx, my));

            var cell = _editorState.Grid[mx, my];
            var existingBlock = _editorState.GetBlockAt(new GridPos(mx, my));

            // Capture before state
            var before = new CellSnapshot(mx, my, cell.Ground, cell.Terrain,
                existingBlock?.Type, existingBlock != null ? GetSlotForBlock(existingBlock) : null);

            // Apply tool
            switch (_currentTool)
            {
                case EditorTool.GroundPaint:
                    cell.Ground = _selectedGround;
                    break;
                case EditorTool.TerrainPaint:
                    cell.Terrain = _selectedTerrain;
                    break;
                case EditorTool.UnitPlace:
                    if (existingBlock != null)
                        _editorState.RemoveBlock(existingBlock);
                    _editorState.AddBlock(_selectedBlock, slot, new GridPos(mx, my));
                    break;
                case EditorTool.Eraser:
                    cell.Ground = GroundType.Normal;
                    cell.Terrain = TerrainType.None;
                    if (existingBlock != null)
                        _editorState.RemoveBlock(existingBlock);
                    break;
            }

            // Capture after state
            var afterBlock = _editorState.GetBlockAt(new GridPos(mx, my));
            var after = new CellSnapshot(mx, my, cell.Ground, cell.Terrain,
                afterBlock?.Type, afterBlock != null ? GetSlotForBlock(afterBlock) : null);

            _currentDragAction?.Before.Add(before);
            _currentDragAction?.After.Add(after);
        }

        _hasUnsavedChanges = true;
        _gridRenderer.QueueRedraw();
    }

    private int GetSlotForBlock(Block block)
    {
        // In editor mode, PlayerId maps directly to slot
        return block.PlayerId;
    }

    private void FinishDrag()
    {
        _isDragging = false;
        if (_currentDragAction != null && _currentDragAction.Before.Count > 0)
            _actionStack.Push(_currentDragAction);
        _currentDragAction = null;
    }

    private void PerformUndo()
    {
        var action = _actionStack.Undo();
        if (action == null) return;
        ApplySnapshots(action.Before);
    }

    private void PerformRedo()
    {
        var action = _actionStack.Redo();
        if (action == null) return;
        ApplySnapshots(action.After);
    }

    private void ApplySnapshots(List<CellSnapshot> snapshots)
    {
        foreach (var snap in snapshots)
        {
            var cell = _editorState.Grid[snap.X, snap.Y];
            cell.Ground = snap.Ground;
            cell.Terrain = snap.Terrain;

            // Remove existing block
            var existing = _editorState.GetBlockAt(new GridPos(snap.X, snap.Y));
            if (existing != null)
                _editorState.RemoveBlock(existing);

            // Place block if snapshot had one
            if (snap.UnitType.HasValue && snap.UnitSlot.HasValue)
                _editorState.AddBlock(snap.UnitType.Value, snap.UnitSlot.Value, new GridPos(snap.X, snap.Y));
        }
        _gridRenderer.QueueRedraw();
    }

    private MapData BuildMapData()
    {
        var ground = new List<GroundEntry>();
        var terrain = new List<TerrainEntry>();
        var units = new List<UnitEntry>();

        for (int y = 0; y < _mapHeight; y++)
        {
            for (int x = 0; x < _mapWidth; x++)
            {
                var cell = _editorState.Grid[x, y];
                if (cell.Ground != GroundType.Normal)
                    ground.Add(new GroundEntry(x, y, cell.Ground));
                if (cell.Terrain != TerrainType.None)
                    terrain.Add(new TerrainEntry(x, y, cell.Terrain));

                var block = _editorState.GetBlockAt(new GridPos(x, y));
                if (block != null)
                    units.Add(new UnitEntry(x, y, block.Type, block.PlayerId));
            }
        }

        return new MapData(_mapName, _mapWidth, _mapHeight, _slotCount, ground, terrain, units);
    }

    private void LoadMapIntoEditor(MapData data)
    {
        _mapWidth = data.Width;
        _mapHeight = data.Height;
        _mapName = data.Name;
        _slotCount = data.SlotCount;

        _editorState = new GameState(new Grid(_mapWidth, _mapHeight));

        foreach (var g in data.Ground)
        {
            if (_editorState.Grid.InBounds(g.X, g.Y))
                _editorState.Grid[g.X, g.Y].Ground = g.Type;
        }
        foreach (var t in data.Terrain)
        {
            if (_editorState.Grid.InBounds(t.X, t.Y))
                _editorState.Grid[t.X, t.Y].Terrain = t.Type;
        }
        foreach (var u in data.Units)
        {
            if (_editorState.Grid.InBounds(u.X, u.Y))
                _editorState.AddBlock(u.Type, u.SlotId, new GridPos(u.X, u.Y));
        }

        // Ensure players exist for rendering colors
        var slotIds = data.Units.Select(u => u.SlotId).Distinct().OrderBy(id => id);
        foreach (int sid in slotIds)
        {
            if (_editorState.Players.All(p => p.Id != sid))
                _editorState.Players.Add(new Player { Id = sid, TeamId = sid });
        }

        _gridRenderer.SetGameState(_editorState);
        _actionStack.Clear();
        CenterCamera();
        _hasUnsavedChanges = false;
        _toolbar.SetMapName(_mapName);
        _toolbar.SetSlotCount(_slotCount);
    }

    private void OnNewMap(int width, int height, int slots)
    {
        _mapWidth = width;
        _mapHeight = height;
        _slotCount = slots;
        _mapName = "Untitled";
        _currentFileName = null;

        _editorState = new GameState(new Grid(_mapWidth, _mapHeight));
        _gridRenderer.SetGameState(_editorState);
        _actionStack.Clear();
        CenterCamera();
        _hasUnsavedChanges = false;
        _toolbar.SetMapName(_mapName);
        _toolbar.SetSlotCount(_slotCount);
    }

    private void OnSave()
    {
        var data = BuildMapData();
        var fileName = _currentFileName ?? $"{_mapName.ToLower().Replace(' ', '-')}.json";
        MapFileManager.Save(data, fileName);
        _currentFileName = fileName;
        _hasUnsavedChanges = false;
    }

    private void OnLoad(string fileName)
    {
        var data = MapFileManager.Load(fileName);
        if (data == null) return;
        _currentFileName = fileName;
        LoadMapIntoEditor(data);
    }

    private void OnBack()
    {
        // TODO: warn about unsaved changes
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
    }

    private void OnToolSelected(EditorTool tool)
    {
        _currentTool = tool;
    }
}
```

- [ ] **Step 2: Create MapEditor.tscn**

```tscn
[gd_scene format=3]

[ext_resource type="Script" path="res://Scripts/Editor/MapEditorScene.cs" id="1_editor"]

[node name="MapEditor" type="Node2D"]
script = ExtResource("1_editor")
```

- [ ] **Step 3: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs godot/Scenes/MapEditor.tscn
git commit -m "feat: add map editor scene with painting and undo/redo

Supports ground/terrain/unit painting with symmetry mirroring.
Left-click paints, right-click erases, Ctrl+Z/Ctrl+Shift+Z for
undo/redo. Middle-drag pans, scroll zooms."
```

---

## Task 14: Create EditorToolbar UI

**Files:**
- Create: `godot/Scripts/Editor/EditorToolbar.cs`

This is the UI overlay for the editor — tool buttons, slot selector, symmetry toggles, file operations.

- [ ] **Step 1: Create EditorToolbar.cs**

```csharp
// godot/Scripts/Editor/EditorToolbar.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Editor;

public partial class EditorToolbar : Control
{
    // Signals
    [Signal] public delegate void ToolSelectedEventHandler(int tool);
    [Signal] public delegate void GroundSelectedEventHandler(int ground);
    [Signal] public delegate void TerrainSelectedEventHandler(int terrain);
    [Signal] public delegate void BlockSelectedEventHandler(int block);
    [Signal] public delegate void SlotSelectedEventHandler(int slot);
    [Signal] public delegate void SymmetryChangedEventHandler(int mode);
    [Signal] public delegate void SlotMirrorChangedEventHandler(int src, int dst);
    [Signal] public delegate void NewMapRequestedEventHandler(int width, int height, int slots);
    [Signal] public delegate void SaveRequestedEventHandler();
    [Signal] public delegate void LoadRequestedEventHandler(string fileName);
    [Signal] public delegate void BackRequestedEventHandler();
    [Signal] public delegate void MapNameChangedEventHandler(string name);
    [Signal] public delegate void SlotCountChangedEventHandler(int count);

    // C# event wrappers for cleaner subscription from MapEditorScene
    public event Action<EditorTool>? ToolSelected;
    public event Action<GroundType>? GroundSelected;
    public event Action<TerrainType>? TerrainSelected;
    public event Action<BlockType>? BlockSelected;
    public event Action<int>? SlotSelected;
    public event Action<SymmetryMode>? SymmetryChanged;
    public event Action<int, int>? SlotMirrorChanged;
    public event Action<int, int, int>? NewMapRequested;
    public event Action? SaveRequested;
    public event Action<string>? LoadRequested;
    public event Action? BackRequested;
    public event Action<string>? MapNameChanged;
    public event Action<int>? SlotCountChanged;

    private LineEdit _mapNameEdit = null!;
    private SpinBox _slotCountSpin = null!;

    public override void _Ready()
    {
        // Top bar
        var topBar = new HBoxContainer
        {
            AnchorRight = 1.0f,
            OffsetBottom = 40,
            GrowHorizontal = GrowDirection.Both
        };
        topBar.AddThemeConstantOverride("separation", 8);
        AddChild(topBar);

        var backBtn = new Button { Text = "< Menu" };
        backBtn.Pressed += () => BackRequested?.Invoke();
        topBar.AddChild(backBtn);

        var newBtn = new Button { Text = "New" };
        newBtn.Pressed += ShowNewMapDialog;
        topBar.AddChild(newBtn);

        var saveBtn = new Button { Text = "Save" };
        saveBtn.Pressed += () => SaveRequested?.Invoke();
        topBar.AddChild(saveBtn);

        var loadBtn = new Button { Text = "Load" };
        loadBtn.Pressed += ShowLoadDialog;
        topBar.AddChild(loadBtn);

        topBar.AddChild(new VSeparator());

        topBar.AddChild(new Label { Text = "Name:" });
        _mapNameEdit = new LineEdit { Text = "Untitled", CustomMinimumSize = new Vector2(150, 0) };
        _mapNameEdit.TextChanged += text => MapNameChanged?.Invoke(text);
        topBar.AddChild(_mapNameEdit);

        topBar.AddChild(new Label { Text = "Slots:" });
        _slotCountSpin = new SpinBox { MinValue = 1, MaxValue = 6, Value = 2, CustomMinimumSize = new Vector2(70, 0) };
        _slotCountSpin.ValueChanged += val => SlotCountChanged?.Invoke((int)val);
        topBar.AddChild(_slotCountSpin);

        // Left sidebar
        var sidebar = new VBoxContainer
        {
            AnchorBottom = 1.0f,
            OffsetTop = 50,
            OffsetRight = 180,
            GrowVertical = GrowDirection.Both
        };
        sidebar.AddThemeConstantOverride("separation", 4);
        AddChild(sidebar);

        // Ground tools
        sidebar.AddChild(new Label { Text = "— Ground —" });
        AddToolButton(sidebar, "Normal", () => { ToolSelected?.Invoke(EditorTool.GroundPaint); GroundSelected?.Invoke(GroundType.Normal); });
        AddToolButton(sidebar, "Boot", () => { ToolSelected?.Invoke(EditorTool.GroundPaint); GroundSelected?.Invoke(GroundType.Boot); });
        AddToolButton(sidebar, "Overload", () => { ToolSelected?.Invoke(EditorTool.GroundPaint); GroundSelected?.Invoke(GroundType.Overload); });
        AddToolButton(sidebar, "Proto", () => { ToolSelected?.Invoke(EditorTool.GroundPaint); GroundSelected?.Invoke(GroundType.Proto); });

        sidebar.AddChild(new HSeparator());

        // Terrain tools
        sidebar.AddChild(new Label { Text = "— Terrain —" });
        AddToolButton(sidebar, "Solid Wall", () => { ToolSelected?.Invoke(EditorTool.TerrainPaint); TerrainSelected?.Invoke(TerrainType.Terrain); });
        AddToolButton(sidebar, "Breakable", () => { ToolSelected?.Invoke(EditorTool.TerrainPaint); TerrainSelected?.Invoke(TerrainType.BreakableWall); });
        AddToolButton(sidebar, "Fragile", () => { ToolSelected?.Invoke(EditorTool.TerrainPaint); TerrainSelected?.Invoke(TerrainType.FragileWall); });

        sidebar.AddChild(new HSeparator());

        // Unit tools
        sidebar.AddChild(new Label { Text = "— Units —" });
        AddToolButton(sidebar, "Builder", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Builder); });
        AddToolButton(sidebar, "Soldier", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Soldier); });
        AddToolButton(sidebar, "Stunner", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Stunner); });
        AddToolButton(sidebar, "Warden", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Warden); });
        AddToolButton(sidebar, "Jumper", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Jumper); });
        AddToolButton(sidebar, "Wall", () => { ToolSelected?.Invoke(EditorTool.UnitPlace); BlockSelected?.Invoke(BlockType.Wall); });

        sidebar.AddChild(new HSeparator());

        AddToolButton(sidebar, "Eraser", () => ToolSelected?.Invoke(EditorTool.Eraser));

        sidebar.AddChild(new HSeparator());

        // Slot selector
        sidebar.AddChild(new Label { Text = "— Slot —" });
        for (int i = 1; i <= 6; i++)
        {
            int slot = i;
            AddToolButton(sidebar, $"Slot {i}", () => SlotSelected?.Invoke(slot));
        }

        sidebar.AddChild(new HSeparator());

        // Symmetry toggles
        sidebar.AddChild(new Label { Text = "— Symmetry —" });
        var symLR = new CheckBox { Text = "Left/Right" };
        var symTB = new CheckBox { Text = "Top/Bottom" };
        var symD1 = new CheckBox { Text = "Diag TL-BR" };
        var symD2 = new CheckBox { Text = "Diag TR-BL" };

        void UpdateSymmetry()
        {
            var mode = SymmetryMode.None;
            if (symLR.ButtonPressed) mode |= SymmetryMode.LeftRight;
            if (symTB.ButtonPressed) mode |= SymmetryMode.TopBottom;
            if (symD1.ButtonPressed) mode |= SymmetryMode.DiagonalTLBR;
            if (symD2.ButtonPressed) mode |= SymmetryMode.DiagonalTRBL;
            SymmetryChanged?.Invoke(mode);
        }

        symLR.Toggled += _ => UpdateSymmetry();
        symTB.Toggled += _ => UpdateSymmetry();
        symD1.Toggled += _ => UpdateSymmetry();
        symD2.Toggled += _ => UpdateSymmetry();

        sidebar.AddChild(symLR);
        sidebar.AddChild(symTB);
        sidebar.AddChild(symD1);
        sidebar.AddChild(symD2);
    }

    private static void AddToolButton(Container parent, string text, Action onPressed)
    {
        var btn = new Button { Text = text, CustomMinimumSize = new Vector2(0, 30) };
        btn.Pressed += onPressed;
        parent.AddChild(btn);
    }

    public void SetMapName(string name) => _mapNameEdit.Text = name;
    public void SetSlotCount(int count) => _slotCountSpin.Value = count;

    private void ShowNewMapDialog()
    {
        var dialog = new AcceptDialog { Title = "New Map" };
        var vbox = new VBoxContainer();
        dialog.AddChild(vbox);

        var widthSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 60, Prefix = "Width:" };
        var heightSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 40, Prefix = "Height:" };
        var slotsSpin = new SpinBox { MinValue = 1, MaxValue = 6, Value = 2, Prefix = "Slots:" };

        vbox.AddChild(widthSpin);
        vbox.AddChild(heightSpin);
        vbox.AddChild(slotsSpin);

        dialog.Confirmed += () =>
        {
            NewMapRequested?.Invoke((int)widthSpin.Value, (int)heightSpin.Value, (int)slotsSpin.Value);
        };

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(300, 200));
    }

    private void ShowLoadDialog()
    {
        var maps = Maps.MapFileManager.ListMaps();
        if (maps.Count == 0)
        {
            var alert = new AcceptDialog { DialogText = "No maps found. Save one first." };
            AddChild(alert);
            alert.PopupCentered();
            return;
        }

        var dialog = new AcceptDialog { Title = "Load Map" };
        var list = new ItemList { CustomMinimumSize = new Vector2(300, 200) };
        foreach (var m in maps) list.AddItem(m);
        dialog.AddChild(list);

        dialog.Confirmed += () =>
        {
            var sel = list.GetSelectedItems();
            if (sel.Length > 0)
                LoadRequested?.Invoke(list.GetItemText(sel[0]));
        };

        AddChild(dialog);
        dialog.PopupCentered(new Vector2I(350, 300));
    }
}
```

- [ ] **Step 2: Verify build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Editor/EditorToolbar.cs
git commit -m "feat: add editor toolbar UI

Ground/terrain/unit tool buttons, slot selector, symmetry toggles,
new/save/load/back actions, map name and slot count controls."
```

---

## Task 15: Run all tests and verify full build

**Files:** None (verification only)

- [ ] **Step 1: Run all simulation tests**

Run: `dotnet test tests/Blocker.Simulation.Tests -v minimal`
Expected: all tests pass.

- [ ] **Step 2: Build full solution**

Run: `dotnet build blocker.sln`
Expected: Build succeeded with no errors.

- [ ] **Step 3: Verify no remaining GroundType.Terrain references**

Search for any leftover references to the old combined enum:
```bash
grep -rn "GroundType\.Terrain\|GroundType\.BreakableWall\|GroundType\.FragileWall" src/ godot/Scripts/ tests/
```
Expected: no matches.

- [ ] **Step 4: Commit any remaining fixes**

If step 3 found issues, fix and commit. Otherwise, this task is done.

---

## Task 16: Create a sample map file for testing

**Files:**
- Create: a sample map via the editor, or manually write a test `.json` file

- [ ] **Step 1: Create a test map JSON file**

Create `godot/Assets/Maps/test-2player.json` to verify the full pipeline:

```json
{
  "meta": {
    "name": "Test 2-Player",
    "version": 1,
    "width": 21,
    "height": 15,
    "slots": 2
  },
  "ground": [
    { "x": 3, "y": 7, "type": "boot" },
    { "x": 4, "y": 7, "type": "boot" },
    { "x": 5, "y": 7, "type": "boot" },
    { "x": 15, "y": 7, "type": "boot" },
    { "x": 16, "y": 7, "type": "boot" },
    { "x": 17, "y": 7, "type": "boot" },
    { "x": 10, "y": 7, "type": "overload" }
  ],
  "terrain": [
    { "x": 10, "y": 3, "type": "terrain" },
    { "x": 10, "y": 4, "type": "terrain" },
    { "x": 10, "y": 10, "type": "terrain" },
    { "x": 10, "y": 11, "type": "terrain" }
  ],
  "units": [
    { "x": 2, "y": 7, "type": "builder", "slot": 1 },
    { "x": 3, "y": 6, "type": "builder", "slot": 1 },
    { "x": 3, "y": 8, "type": "builder", "slot": 1 },
    { "x": 18, "y": 7, "type": "builder", "slot": 2 },
    { "x": 17, "y": 6, "type": "builder", "slot": 2 },
    { "x": 17, "y": 8, "type": "builder", "slot": 2 }
  ]
}
```

Note: This file goes in `godot/Assets/Maps/` for the Godot resource system. However, for `MapFileManager` which uses `user://Maps`, you'll need to copy it there or create it through the editor. For now, create it in the project directory as a reference/test fixture.

- [ ] **Step 2: Add a serialization round-trip test**

Add to `tests/Blocker.Simulation.Tests/MapDataTests.cs`:

```csharp
[Fact]
public void MapData_RoundTrip_PreservesAllLayers()
{
    var original = new MapData(
        Name: "RoundTrip Test",
        Width: 30,
        Height: 20,
        SlotCount: 3,
        Ground: [
            new GroundEntry(1, 2, GroundType.Boot),
            new GroundEntry(5, 5, GroundType.Overload)
        ],
        Terrain: [
            new TerrainEntry(3, 4, TerrainType.Terrain),
            new TerrainEntry(5, 5, TerrainType.BreakableWall)
        ],
        Units: [
            new UnitEntry(0, 0, BlockType.Builder, 1),
            new UnitEntry(29, 19, BlockType.Soldier, 3)
        ]
    );

    // Load into GameState and verify layers are independent
    var assignments = new List<SlotAssignment> { new(1, 0), new(2, 1), new(3, 2) };
    var state = MapLoader.Load(original, assignments);

    // Boot ground at (1,2) — no terrain
    Assert.Equal(GroundType.Boot, state.Grid[1, 2].Ground);
    Assert.Equal(TerrainType.None, state.Grid[1, 2].Terrain);

    // Overload ground with breakable wall at (5,5)
    Assert.Equal(GroundType.Overload, state.Grid[5, 5].Ground);
    Assert.Equal(TerrainType.BreakableWall, state.Grid[5, 5].Terrain);

    // Impassable terrain at (3,4) on normal ground
    Assert.Equal(GroundType.Normal, state.Grid[3, 4].Ground);
    Assert.Equal(TerrainType.Terrain, state.Grid[3, 4].Terrain);
    Assert.False(state.Grid[3, 4].IsPassable);
}
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests --filter "FullyQualifiedName~MapData" -v minimal`
Expected: all pass.

- [ ] **Step 4: Commit**

```bash
git add godot/Assets/Maps/test-2player.json tests/Blocker.Simulation.Tests/MapDataTests.cs
git commit -m "feat: add sample map file and round-trip test

test-2player.json demonstrates the sparse 3-layer format.
MapData round-trip test verifies independent ground/terrain layers."
```

---

## Summary

| Task | What it does | Dependencies |
|------|-------------|--------------|
| 1 | Split GroundType/TerrainType in Cell | — |
| 2 | Update simulation code for split types | 1 |
| 3 | Update Godot rendering for split types | 1 |
| 4 | Create MapData + SlotAssignment records | 1 |
| 5 | Add MapLoader.Load(MapData) overload | 2, 4 |
| 6 | Create MapSerializer (JSON ↔ MapData) | 4 |
| 7 | Create MapFileManager (file I/O) | 6 |
| 8 | Create undo/redo system | 1 |
| 9 | Create SymmetryMirror | — |
| 10 | Create MainMenu scene | — |
| 11 | Create MapSelect screen | 7 |
| 12 | Create SlotConfig screen + GameManager integration | 5, 7 |
| 13 | Create Map Editor core (painting + camera) | 8, 9 |
| 14 | Create EditorToolbar UI | 13 |
| 15 | Verify full build + tests | All |
| 16 | Sample map + round-trip test | 5, 6 |

Tasks 1-5 are sequential (type split → simulation updates → data structures → loader).
Tasks 6-9 can be parallelized after task 4.
Tasks 10-14 can partially overlap (10 is independent; 11-12 need 7; 13-14 need 8-9).
Tasks 15-16 are final verification.
