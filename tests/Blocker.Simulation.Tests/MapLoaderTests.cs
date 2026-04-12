using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Xunit;

namespace Blocker.Simulation.Tests;

public class MapLoaderTests
{
    public MapLoaderTests()
    {
        Constants.Reset();
    }

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
        Assert.Equal(GroundType.Normal, state.Grid[0, 0].Ground);
        Assert.Equal(TerrainType.Terrain, state.Grid[5, 6].Terrain);
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
        Assert.Equal(0, b.PlayerId);

        var s = state.GetBlockAt(new GridPos(8, 8));
        Assert.NotNull(s);
        Assert.Equal(BlockType.Soldier, s.Type);
        Assert.Equal(3, s.PlayerId);
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

    [Fact]
    public void LoadMapData_RootedUnit_StartsRooted()
    {
        var data = new MapData("Test", 10, 10, 2,
            Ground: [],
            Terrain: [],
            Units: [
                new UnitEntry(1, 1, BlockType.Builder, 1, Rooted: true),
                new UnitEntry(5, 5, BlockType.Soldier, 1, Rooted: true),
                new UnitEntry(8, 8, BlockType.Stunner, 2, Rooted: false)
            ]
        );
        var assignments = new List<SlotAssignment> { new(1, 0), new(2, 1) };

        var state = MapLoader.Load(data, assignments);

        var builder = state.GetBlockAt(new GridPos(1, 1))!;
        Assert.Equal(BlockState.Rooted, builder.State);
        Assert.Equal(Constants.GetRootTicks(BlockType.Builder), builder.RootProgress);

        var soldier = state.GetBlockAt(new GridPos(5, 5))!;
        Assert.Equal(BlockState.Rooted, soldier.State);
        Assert.Equal(Constants.GetRootTicks(BlockType.Soldier), soldier.RootProgress);

        var stunner = state.GetBlockAt(new GridPos(8, 8))!;
        Assert.Equal(BlockState.Mobile, stunner.State);
        Assert.Equal(0, stunner.RootProgress);
    }

    [Fact]
    public void LoadMapData_RootedWall_IgnoresRootedFlag()
    {
        // Walls are always rooted via AddBlock — the Rooted flag should be a no-op
        var data = new MapData("Test", 10, 10, 1,
            Ground: [],
            Terrain: [],
            Units: [new UnitEntry(3, 3, BlockType.Wall, 1, Rooted: true)]
        );
        var assignments = new List<SlotAssignment> { new(1, 0) };

        var state = MapLoader.Load(data, assignments);

        var wall = state.GetBlockAt(new GridPos(3, 3))!;
        Assert.Equal(BlockState.Rooted, wall.State);
        Assert.Equal(Constants.RootTicks, wall.RootProgress);
    }
}
