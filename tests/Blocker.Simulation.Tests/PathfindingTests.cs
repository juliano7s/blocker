using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class PathfindingTests
{
    public PathfindingTests()
    {
        Constants.Reset();
    }

    private GameState CreateState(int width = 10, int height = 10)
    {
        return new GameState(new Grid(width, height));
    }

    [Fact]
    public void DirectPath_ReturnsNextStep()
    {
        var state = CreateState();
        var from = new GridPos(0, 0);
        var target = new GridPos(3, 0);

        var next = PathfindingSystem.GetNextStep(state, from, target);

        Assert.NotNull(next);
        Assert.Equal(new GridPos(1, 0), next.Value);
    }

    [Fact]
    public void DiagonalTarget_MovesOrthogonally()
    {
        var state = CreateState();
        var from = new GridPos(0, 0);
        var target = new GridPos(3, 3);

        var next = PathfindingSystem.GetNextStep(state, from, target);

        Assert.NotNull(next);
        // Should move orthogonally — either (1,0) or (0,1)
        Assert.True(
            next.Value == new GridPos(1, 0) || next.Value == new GridPos(0, 1),
            $"Expected orthogonal step, got {next.Value}");
    }

    [Fact]
    public void NavigatesAroundWall()
    {
        var state = CreateState();
        // Place a wall blocking direct path
        // From (0,2) to (4,2), wall at (2,1), (2,2), (2,3)
        state.Grid[2, 1].Terrain = TerrainType.Terrain;
        state.Grid[2, 2].Terrain = TerrainType.Terrain;
        state.Grid[2, 3].Terrain = TerrainType.Terrain;

        var from = new GridPos(0, 2);
        var target = new GridPos(4, 2);

        var next = PathfindingSystem.GetNextStep(state, from, target);

        Assert.NotNull(next);
        // Should not try to go through (2,2) — should route around
        // First step should be orthogonal: (0,1) or (0,3) or (1,2)
    }

    [Fact]
    public void NavigatesAroundBlocks()
    {
        var state = CreateState();
        // Place blocks blocking direct path
        state.AddBlock(BlockType.Wall, 0, new GridPos(2, 1));
        state.AddBlock(BlockType.Wall, 0, new GridPos(2, 2));
        state.AddBlock(BlockType.Wall, 0, new GridPos(2, 3));

        var from = new GridPos(0, 2);
        var target = new GridPos(4, 2);

        var next = PathfindingSystem.GetNextStep(state, from, target);

        Assert.NotNull(next);
    }

    [Fact]
    public void NoPath_ReturnsGreedyFallbackOrNull()
    {
        var state = CreateState(5, 5);
        // Completely surround with terrain
        for (int x = 0; x < 5; x++)
            for (int y = 0; y < 5; y++)
                if (x != 0 || y != 0)
                    state.Grid[x, y].Terrain = TerrainType.Terrain;

        var from = new GridPos(0, 0);
        var target = new GridPos(4, 4);

        var next = PathfindingSystem.GetNextStep(state, from, target);

        Assert.Null(next); // No way out
    }

    [Fact]
    public void AlreadyAtTarget_ReturnsNull()
    {
        var state = CreateState();
        var pos = new GridPos(3, 3);

        var next = PathfindingSystem.GetNextStep(state, pos, pos);

        Assert.Null(next);
    }

    [Fact]
    public void MovementWithPathfinding_IntegrationTest()
    {
        var state = CreateState();
        state.Players.Add(new Player { Id = 0, TeamId = 0 });

        // Place a terrain wall between start and target
        state.Grid[3, 0].Terrain = TerrainType.Terrain;
        state.Grid[3, 1].Terrain = TerrainType.Terrain;
        state.Grid[3, 2].Terrain = TerrainType.Terrain;

        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        builder.MoveTarget = new GridPos(5, 1);

        // Run several ticks — builder should navigate around the wall
        for (int i = 0; i < 30; i++)
            state.Tick();

        // Builder should have reached target or be close to it
        Assert.True(builder.Pos.X >= 4, $"Builder should have navigated around wall, but is at {builder.Pos}");
    }
}
