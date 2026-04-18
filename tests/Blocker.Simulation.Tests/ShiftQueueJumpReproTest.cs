using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class ShiftQueueJumpReproTest
{
    public ShiftQueueJumpReproTest()
    {
        Constants.Reset();
    }

    private GameState CreateState(int w = 15, int h = 15)
    {
        var state = new GameState(new Grid(w, h));
        state.Players.Add(new Player { Id = 0, TeamId = 0, MaxPopulation = 50 });
        state.Players.Add(new Player { Id = 1, TeamId = 1, MaxPopulation = 50 });
        return state;
    }

    [Fact]
    public void ShiftQueue_JumpExecutes_WhenIdle()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        // Shift+queue a jump
        state.ProcessCommands([new Command(
            0, CommandType.Jump, [jumper.Id],
            TargetPos: new GridPos(10, 7),
            Direction: Direction.Right,
            Queue: true)]);

        // After ProcessCommands, jump should have executed immediately (block was idle)
        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    [Fact]
    public void ShiftQueue_JumpExecutes_WithEnemyInPath()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        state.ProcessCommands([new Command(
            0, CommandType.Jump, [jumper.Id],
            TargetPos: new GridPos(5, 7),
            Direction: Direction.Right,
            Queue: true)]);

        // Enemy should be dead
        Assert.DoesNotContain(enemy, state.Blocks);
        // Jumper should have moved
        Assert.NotEqual(new GridPos(3, 7), jumper.Pos);
    }

    [Fact]
    public void ShiftQueue_JumpThroughTickAdvance()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        // Queue shift-jump, then advance tick
        state.Tick([new Command(
            0, CommandType.Jump, [jumper.Id],
            TargetPos: new GridPos(8, 7),
            Direction: Direction.Right,
            Queue: true)]);

        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    [Fact]
    public void ShiftQueue_JumpDirectionRecomputed_IfJumperMovedPastTarget()
    {
        // Repro: user shift-clicks a jump at (5,7) from (3,7) -> Direction.Right.
        // Before the queued jump fires, the jumper has moved to (7,7) (past the
        // target). With the old code, the stored Direction.Right + CellDistance
        // |5-7|=2 would jump the jumper to (9,7), overshooting.
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(7, 7));

        // Simulate a queued jump whose command was built from an earlier position.
        state.ProcessCommands([new Command(
            0, CommandType.Jump, [jumper.Id],
            TargetPos: new GridPos(5, 7),
            Direction: Direction.Right, // stale
            Queue: true)]);

        // Jumper should have jumped TOWARD the target (left), not past it.
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
    }

    [Fact]
    public void ShiftQueue_JumpSkipsIfJumperAtTarget()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 7));
        int hpBefore = jumper.Hp;

        state.ProcessCommands([new Command(
            0, CommandType.Jump, [jumper.Id],
            TargetPos: new GridPos(5, 7),
            Direction: Direction.Right,
            Queue: true)]);

        // No-op: jumper already at target. Shouldn't move, shouldn't lose HP.
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
        Assert.Equal(hpBefore, jumper.Hp);
    }
}
