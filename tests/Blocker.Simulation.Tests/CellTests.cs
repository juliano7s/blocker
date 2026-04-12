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
        Assert.True(cell.IsNestZone);
        Assert.False(cell.IsPassable);
    }

    [Fact]
    public void Cell_WallDestroyed_RevealsGround()
    {
        var cell = new Cell
        {
            Ground = GroundType.Overload,
            Terrain = TerrainType.BreakableWall
        };
        cell.Terrain = TerrainType.None;
        Assert.True(cell.IsPassable);
        Assert.Equal(GroundType.Overload, cell.Ground);
        Assert.True(cell.IsNestZone);
    }

    [Fact]
    public void GridPos_ChebyshevDistance()
    {
        var a = new GridPos(0, 0);
        var b = new GridPos(3, 4);
        Assert.Equal(4, a.ChebyshevDistance(b));
    }

    [Fact]
    public void GridPos_ManhattanDistance()
    {
        var a = new GridPos(0, 0);
        var b = new GridPos(3, 4);
        Assert.Equal(7, a.ManhattanDistance(b));
    }
}
