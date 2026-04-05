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
}
