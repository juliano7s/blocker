using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class RootingTests
{
    private GameState CreateState(int width = 10, int height = 10)
    {
        return new GameState(new Grid(width, height));
    }

    [Fact]
    public void ToggleRoot_MobileBuilder_StartsRooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        var result = RootingSystem.ToggleRoot(builder);

        Assert.True(result);
        Assert.Equal(BlockState.Rooting, builder.State);
        Assert.Equal(0, builder.RootProgress);
        Assert.Null(builder.MoveTarget); // Movement cleared
    }

    [Fact]
    public void RootingProgress_CompletesAfterRootTicks()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        RootingSystem.ToggleRoot(builder);

        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);

        Assert.Equal(BlockState.Rooted, builder.State);
        Assert.Equal(Constants.RootTicks, builder.RootProgress);
    }

    [Fact]
    public void ToggleRoot_RootedBuilder_StartsUprooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        RootingSystem.ToggleRoot(builder);
        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);

        var result = RootingSystem.ToggleRoot(builder);

        Assert.True(result);
        Assert.Equal(BlockState.Uprooting, builder.State);
    }

    [Fact]
    public void Uprooting_CompletesAfterProgressReachesZero()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Fully root
        RootingSystem.ToggleRoot(builder);
        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);
        Assert.Equal(BlockState.Rooted, builder.State);

        // Start uprooting
        RootingSystem.ToggleRoot(builder);
        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);

        Assert.Equal(BlockState.Mobile, builder.State);
        Assert.Equal(0, builder.RootProgress);
    }

    [Fact]
    public void ConvertToWall_FullyRootedBuilder_BecomesWall()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        RootingSystem.ToggleRoot(builder);
        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);

        var result = RootingSystem.ConvertToWall(state, builder);

        Assert.True(result);
        Assert.Equal(BlockType.Wall, builder.Type);
    }

    [Fact]
    public void ConvertToWall_MobileBuilder_Fails()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        var result = RootingSystem.ConvertToWall(state, builder);

        Assert.False(result);
        Assert.Equal(BlockType.Builder, builder.Type);
    }

    [Fact]
    public void ConvertToWall_Soldier_Fails()
    {
        var state = CreateState();
        var soldier = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
        RootingSystem.ToggleRoot(soldier);
        for (int i = 0; i < Constants.RootTicks; i++)
            RootingSystem.Tick(state);

        var result = RootingSystem.ConvertToWall(state, soldier);

        Assert.False(result);
        Assert.Equal(BlockType.Soldier, soldier.Type);
    }

    [Fact]
    public void Wall_CannotRoot()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(5, 5));

        var result = RootingSystem.ToggleRoot(wall);

        Assert.False(result);
    }

    [Fact]
    public void Command_Root_WorksThroughTick()
    {
        var state = CreateState();
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        var commands = new List<Command>
        {
            new(0, CommandType.Root, [builder.Id])
        };

        state.Tick(commands);

        Assert.Equal(BlockState.Rooting, builder.State);
    }

    [Fact]
    public void Command_Move_SetsMoveTarget()
    {
        var state = CreateState();
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        var target = new GridPos(8, 5);

        var commands = new List<Command>
        {
            new(0, CommandType.Move, [builder.Id], target)
        };

        state.Tick(commands);

        // Block should have started moving (may have moved one cell already)
        Assert.True(builder.MoveTarget.HasValue || builder.Pos == target);
    }
}
