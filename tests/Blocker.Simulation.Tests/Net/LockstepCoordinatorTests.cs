using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class LockstepCoordinatorTests
{
    private static GameState MakeTwoPlayerState()
    {
        Block.ResetIdCounter();   // FIX C — ensures both states get identical block IDs
        var grid = new Grid(10, 10);
        var state = new GameState(grid);
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        // 3 builders each — prevents elimination (requires < 3 builders with no army/nests)
        state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Builder, 0, new GridPos(2, 1));
        state.AddBlock(BlockType.Builder, 0, new GridPos(3, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(8, 8));
        state.AddBlock(BlockType.Builder, 1, new GridPos(7, 8));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 8));
        return state;
    }

    [Fact]
    public void Two_Players_Stay_In_Sync_Over_200_Ticks_Of_Empty_Input()
    {
        var state0 = MakeTwoPlayerState();
        var state1 = MakeTwoPlayerState();

        var relay0 = new FakeRelayClient(0);
        var relay1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(relay0, relay1);

        var coord0 = new LockstepCoordinator(0, state0, relay0, new HashSet<int> { 0, 1 });
        var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });

        coord0.StartGame();
        coord1.StartGame();

        // Drive 200 poll rounds. Each coordinator polls in turn; empty local input.
        for (int i = 0; i < 200; i++)
        {
            coord0.PollAdvance();
            coord1.PollAdvance();
        }

        Assert.Equal(state0.TickNumber, state1.TickNumber);
        Assert.Equal(StateHasher.Hash(state0), StateHasher.Hash(state1));
        Assert.True(state0.TickNumber >= 100, $"expected at least 100 ticks advanced, got {state0.TickNumber}");
    }

    [Fact]
    public void Commands_Scheduled_With_Input_Delay_Apply_To_Correct_Tick()
    {
        var state0 = MakeTwoPlayerState();
        var state1 = MakeTwoPlayerState();
        var relay0 = new FakeRelayClient(0);
        var relay1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(relay0, relay1);

        var coord0 = new LockstepCoordinator(0, state0, relay0, new HashSet<int> { 0, 1 });
        var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });
        coord0.StartGame();
        coord1.StartGame();

        // Player 0 issues a move command at local tick 0, which applies at tick 1 (input delay = 1).
        var block0Id = state0.Blocks.First(b => b.PlayerId == 0).Id;
        coord0.QueueLocalCommand(new Command(0, CommandType.Move, new List<int> { block0Id },
            TargetPos: new GridPos(5, 1)));

        for (int i = 0; i < 50; i++)
        {
            coord0.PollAdvance();
            coord1.PollAdvance();
        }

        // Both states should agree and the block should have begun moving.
        Assert.Equal(StateHasher.Hash(state0), StateHasher.Hash(state1));
        var b0 = state0.Blocks.First(b => b.Id == block0Id);
        var b1 = state1.Blocks.First(b => b.Id == block0Id);
        Assert.Equal(b0.Pos, b1.Pos);
        Assert.NotEqual(new GridPos(1, 1), b0.Pos); // moved
    }
}
