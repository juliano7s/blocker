using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class PushTests
{
    private GameState CreateState(int width = 15, int height = 15)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    [Fact]
    public void TogglePush_RootedBuilder_Succeeds()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));
        builder.State = BlockState.Rooted;
        builder.RootProgress = Constants.RootTicks;

        var result = PushSystem.TogglePush(builder, Direction.Right);

        Assert.True(result);
        Assert.True(builder.IsPushing);
        Assert.Equal(Direction.Right, builder.PushDirection);
    }

    [Fact]
    public void TogglePush_MobileBuilder_Fails()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));

        var result = PushSystem.TogglePush(builder, Direction.Right);

        Assert.False(result);
    }

    [Fact]
    public void TogglePush_InFormation_Fails()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));
        builder.State = BlockState.Rooted;
        builder.RootProgress = Constants.RootTicks;
        builder.FormationId = 999;

        var result = PushSystem.TogglePush(builder, Direction.Right);

        Assert.False(result);
    }

    [Fact]
    public void TogglePush_ToggleOff()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));
        builder.State = BlockState.Rooted;
        builder.RootProgress = Constants.RootTicks;

        PushSystem.TogglePush(builder, Direction.Right);
        Assert.True(builder.IsPushing);

        PushSystem.TogglePush(builder, Direction.Right);
        Assert.False(builder.IsPushing);
    }

    [Fact]
    public void PushWave_DisplacesMobileBlock()
    {
        var state = CreateState();
        var pusher = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));
        pusher.State = BlockState.Rooted;
        pusher.RootProgress = Constants.RootTicks;
        pusher.IsPushing = true;
        pusher.PushDirection = Direction.Right;

        var target = state.AddBlock(BlockType.Builder, 1, new GridPos(7, 7));
        var startPos = target.Pos;

        // Tick until wave fires and advances to target
        for (int i = 0; i < 30; i++)
            state.Tick();

        Assert.True(target.Pos.X > startPos.X, $"Target should be pushed right, was at {startPos} now at {target.Pos}");
    }

    [Fact]
    public void PushWave_AffectsFriendly()
    {
        var state = CreateState();
        var pusher = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));
        pusher.State = BlockState.Rooted;
        pusher.RootProgress = Constants.RootTicks;
        pusher.IsPushing = true;
        pusher.PushDirection = Direction.Right;

        // Friendly block in the way
        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));
        var startPos = friendly.Pos;

        for (int i = 0; i < 30; i++)
            state.Tick();

        Assert.True(friendly.Pos.X > startPos.X, "Push should affect friendly blocks too");
    }

    [Fact]
    public void PushWave_StopsAtWall()
    {
        var state = CreateState();
        var pusher = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));
        pusher.State = BlockState.Rooted;
        pusher.RootProgress = Constants.RootTicks;
        pusher.IsPushing = true;
        pusher.PushDirection = Direction.Right;

        // Wall blocks the wave
        state.AddBlock(BlockType.Wall, 1, new GridPos(7, 7));

        // Builder beyond the wall should NOT be affected
        var beyond = state.AddBlock(BlockType.Builder, 1, new GridPos(9, 7));
        var startPos = beyond.Pos;

        for (int i = 0; i < 30; i++)
            state.Tick();

        Assert.Equal(startPos, beyond.Pos);
    }

    [Fact]
    public void PushWave_StopsAtTerrain()
    {
        var state = CreateState();
        var pusher = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));
        pusher.State = BlockState.Rooted;
        pusher.RootProgress = Constants.RootTicks;
        pusher.IsPushing = true;
        pusher.PushDirection = Direction.Right;

        state.Grid[7, 7].Ground = GroundType.Terrain;

        var target = state.AddBlock(BlockType.Builder, 1, new GridPos(8, 7));
        var startPos = target.Pos;

        for (int i = 0; i < 30; i++)
            state.Tick();

        Assert.Equal(startPos, target.Pos);
    }
}
