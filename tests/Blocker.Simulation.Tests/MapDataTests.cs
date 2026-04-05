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
