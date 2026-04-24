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
