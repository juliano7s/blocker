using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class CommandQueueTests
{
    private GameState CreateState()
    {
        var state = new GameState(new Grid(10, 10));
        state.Players.Add(new Player { Id = 0, TeamId = 0, MaxPopulation = 50 });
        return state;
    }

    [Fact]
    public void ImmediateCommand_ClearsQueue()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        builder.MoveTarget = new GridPos(2, 1); // Block is busy moving

        // Queue a waypoint while busy
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(5, 1), Queue: true)]);
        Assert.Single(builder.CommandQueue);

        // Immediate move clears queue
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(3, 1))]);
        Assert.Empty(builder.CommandQueue);
        Assert.Equal(new GridPos(3, 1), builder.MoveTarget);
    }

    [Fact]
    public void ShiftCommand_AppendsToQueue()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));

        // Set initial move
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(3, 1))]);

        // Shift+move queues waypoint
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(5, 1), Queue: true)]);
        Assert.Single(builder.CommandQueue);

        // Another shift+move
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(7, 1), Queue: true)]);
        Assert.Equal(2, builder.CommandQueue.Count);
    }

    [Fact]
    public void ConvertToWall_QueuesDuringRooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Rooting, builder.State);

        // Issue wall convert while still rooting — should auto-queue
        state.ProcessCommands([new Command(0, CommandType.ConvertToWall, [builder.Id])]);
        Assert.Single(builder.CommandQueue); // Queued, waiting for root

        // Complete rooting
        builder.State = BlockState.Rooted;
        builder.RootProgress = Constants.RootTicks;

        // Process queue — should now convert
        state.ProcessCommands([]);
        Assert.Equal(BlockType.Wall, builder.Type);
    }

    [Fact]
    public void QueuedMove_ExecutesWhenIdle()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));

        // Queue two moves
        builder.MoveTarget = new GridPos(3, 1); // Currently moving
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(5, 1), Queue: true)]);

        // Block is not idle (has move target), so queue not processed
        Assert.Single(builder.CommandQueue);

        // Simulate arrival
        builder.MoveTarget = null;
        state.ProcessCommands([]); // Process queue

        Assert.Equal(new GridPos(5, 1), builder.MoveTarget);
        Assert.Empty(builder.CommandQueue);
    }
}
