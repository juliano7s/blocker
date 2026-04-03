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
        Assert.Equal(GroundType.Terrain, state.Grid[2, 0].Ground);
        Assert.Equal(GroundType.Overload, state.Grid[0, 1].Ground);
        Assert.Equal(GroundType.Proto, state.Grid[2, 1].Ground);
        Assert.Equal(GroundType.BreakableWall, state.Grid[0, 2].Ground);
        Assert.Equal(GroundType.FragileWall, state.Grid[1, 2].Ground);
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
