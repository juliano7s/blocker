using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class CommandQueueTests
{
    public CommandQueueTests()
    {
        Constants.Reset();
    }

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

    // --- Auto-queue tests ---

    [Fact]
    public void AutoQueue_MoveWhileRooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Rooting, builder.State);

        // Immediate move while rooting — should auto-queue
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(7, 7))]);
        Assert.Single(builder.CommandQueue);
        Assert.Equal(CommandType.Move, builder.CommandQueue.Peek().Type);
        Assert.Equal(new GridPos(7, 7), builder.CommandQueue.Peek().TargetPos);
    }

    [Fact]
    public void AutoQueue_ReplacesPreviousAutoQueue()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);

        // Auto-queue first move
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(7, 7))]);
        Assert.Single(builder.CommandQueue);

        // Auto-queue second move — should replace first
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(8, 8))]);
        Assert.Single(builder.CommandQueue);
        Assert.Equal(new GridPos(8, 8), builder.CommandQueue.Peek().TargetPos);
    }

    [Fact]
    public void AutoQueue_RootWhileRooting_IsImmediateToggle()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Rooting, builder.State);

        // Root again while rooting — should toggle to uprooting immediately (not queue)
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Uprooting, builder.State);
        Assert.Empty(builder.CommandQueue);
    }

    [Fact]
    public void AutoQueue_RootWhileUprooting_IsImmediateToggle()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting, then toggle to uprooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Uprooting, builder.State);

        // Root again while uprooting — should toggle back to rooting immediately
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Rooting, builder.State);
        Assert.Empty(builder.CommandQueue);
    }

    [Fact]
    public void AutoQueue_MoveDuringImmobileCooldown()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));
        jumper.Cooldown = 5; // Immobile cooldown (MobileCooldown is false by default)

        // Immediate move while on immobile cooldown — should auto-queue
        state.ProcessCommands([new Command(0, CommandType.Move, [jumper.Id], new GridPos(7, 5))]);
        Assert.Single(jumper.CommandQueue);
        Assert.Equal(CommandType.Move, jumper.CommandQueue.Peek().Type);
    }

    [Fact]
    public void AutoQueue_RootDuringImmobileCooldown()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));
        jumper.Cooldown = 5; // Immobile cooldown (MobileCooldown is false by default)

        // Root while on immobile cooldown — should auto-queue (not immediate toggle)
        state.ProcessCommands([new Command(0, CommandType.Root, [jumper.Id])]);
        Assert.Single(jumper.CommandQueue);
        Assert.Equal(CommandType.Root, jumper.CommandQueue.Peek().Type);
    }

    [Fact]
    public void NoAutoQueue_DuringMobileCooldown()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));
        jumper.Cooldown = 3;
        jumper.MobileCooldown = true; // Mobile cooldown — can still move

        // Immediate move during mobile cooldown — should execute directly
        state.ProcessCommands([new Command(0, CommandType.Move, [jumper.Id], new GridPos(7, 5))]);
        Assert.Empty(jumper.CommandQueue);
        Assert.Equal(new GridPos(7, 5), jumper.MoveTarget);
    }

    [Fact]
    public void AutoQueue_MoveDroppedWhenRooted()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);

        // Auto-queue move
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(7, 7))]);
        Assert.Single(builder.CommandQueue);

        // Complete rooting — block is now Rooted
        builder.State = BlockState.Rooted;
        builder.RootProgress = Constants.RootTicks;

        // Process queue — move should be dropped (rooted block can't move)
        state.ProcessCommands([]);
        Assert.Empty(builder.CommandQueue);
        Assert.Null(builder.MoveTarget);
    }

    [Fact]
    public void AutoQueue_MoveExecutesAfterUprooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting, then toggle to uprooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);
        Assert.Equal(BlockState.Uprooting, builder.State);

        // Auto-queue move while uprooting
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(7, 7))]);
        Assert.Single(builder.CommandQueue);

        // Complete uprooting — block is now Mobile
        builder.State = BlockState.Mobile;
        builder.RootProgress = 0;

        // Process queue — move should execute
        state.ProcessCommands([]);
        Assert.Equal(new GridPos(7, 7), builder.MoveTarget);
        Assert.Empty(builder.CommandQueue);
    }

    [Fact]
    public void ShiftQueue_StillWorksDuringRooting()
    {
        var state = CreateState();
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Start rooting
        state.ProcessCommands([new Command(0, CommandType.Root, [builder.Id])]);

        // Shift+queue multiple moves
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(3, 3), Queue: true)]);
        state.ProcessCommands([new Command(0, CommandType.Move, [builder.Id], new GridPos(1, 1), Queue: true)]);

        Assert.Equal(2, builder.CommandQueue.Count);
    }
}
