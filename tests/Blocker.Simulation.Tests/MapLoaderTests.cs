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
    public void LoadTwoLayerMap_ParsesGroundTypes()
    {
        var map = """
            .f#
            o.p
            ~=.
            ---
            ...
            ...
            ...
            """;

        var state = MapLoader.Load(Dedent(map));

        Assert.Equal(3, state.Grid.Width);
        Assert.Equal(3, state.Grid.Height);
        Assert.Equal(GroundType.Normal, state.Grid[0, 0].Ground);
        Assert.Equal(GroundType.Boot, state.Grid[1, 0].Ground);
        Assert.Equal(TerrainType.Terrain, state.Grid[2, 0].Terrain);
        Assert.Equal(GroundType.Overload, state.Grid[0, 1].Ground);
        Assert.Equal(GroundType.Proto, state.Grid[2, 1].Ground);
        Assert.Equal(TerrainType.BreakableWall, state.Grid[0, 2].Terrain);
        Assert.Equal(TerrainType.FragileWall, state.Grid[1, 2].Terrain);
    }

    [Fact]
    public void LoadTwoLayerMap_ParsesBlocks()
    {
        var map = """
            ...
            ...
            ...
            ---
            b.B
            ...
            s.S
            """;

        var state = MapLoader.Load(Dedent(map));

        Assert.Equal(4, state.Blocks.Count);

        var b0 = state.GetBlockAt(new GridPos(0, 0));
        Assert.NotNull(b0);
        Assert.Equal(BlockType.Builder, b0.Type);
        Assert.Equal(0, b0.PlayerId);

        var b1 = state.GetBlockAt(new GridPos(2, 0));
        Assert.NotNull(b1);
        Assert.Equal(BlockType.Builder, b1.Type);
        Assert.Equal(1, b1.PlayerId);

        var s0 = state.GetBlockAt(new GridPos(0, 2));
        Assert.NotNull(s0);
        Assert.Equal(BlockType.Soldier, s0.Type);
        Assert.Equal(0, s0.PlayerId);
        Assert.Equal(Constants.SoldierMaxHp, s0.Hp);
    }

    [Fact]
    public void LoadMap_CreatesPlayers()
    {
        var map = """
            ...
            ---
            b.B
            """;

        var state = MapLoader.Load(Dedent(map));

        Assert.Equal(2, state.Players.Count);
        Assert.Contains(state.Players, p => p.Id == 0);
        Assert.Contains(state.Players, p => p.Id == 1);
    }

    [Fact]
    public void LoadTestMapFile_Succeeds()
    {
        var path = Path.Combine(FindRepoRoot(), "maps", "test-small.txt");
        if (!File.Exists(path)) return; // Skip if running from different directory

        var state = MapLoader.LoadFromFile(path);

        Assert.True(state.Grid.Width > 0);
        Assert.True(state.Grid.Height > 0);
        Assert.True(state.Blocks.Count > 0);
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

    [Fact]
    public void Block_PopCosts_MatchConstants()
    {
        var builder = new Block { Type = BlockType.Builder };
        var wall = new Block { Type = BlockType.Wall };
        var soldier = new Block { Type = BlockType.Soldier };
        var stunner = new Block { Type = BlockType.Stunner };

        Assert.Equal(1, builder.PopCost);
        Assert.Equal(0, wall.PopCost);
        Assert.Equal(1, soldier.PopCost);
        Assert.Equal(3, stunner.PopCost);
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

    private static string Dedent(string text)
    {
        var lines = text.Split('\n');
        int minIndent = lines
            .Where(l => l.Trim().Length > 0)
            .Min(l => l.Length - l.TrimStart().Length);
        return string.Join('\n', lines.Select(l => l.Length >= minIndent ? l[minIndent..] : l));
    }

    private static string FindRepoRoot()
    {
        var dir = Directory.GetCurrentDirectory();
        while (dir != null)
        {
            if (Directory.Exists(Path.Combine(dir, "maps")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return Directory.GetCurrentDirectory();
    }
}
