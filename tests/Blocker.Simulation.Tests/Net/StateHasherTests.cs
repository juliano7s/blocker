using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class StateHasherTests
{
    private static GameState MakeState()
    {
        var grid = new Grid(8, 8);
        var state = new GameState(grid);
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));
        return state;
    }

    [Fact]
    public void Same_State_Same_Hash()
    {
        var a = MakeState();
        var b = MakeState();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Position_Change_Changes_Hash()
    {
        var a = MakeState();
        var b = MakeState();
        var block = b.Blocks[0];
        b.TryMoveBlock(block, new GridPos(2, 1));
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Insertion_Order_Does_Not_Matter()
    {
        var grid = new Grid(8, 8);
        var a = new GameState(grid);
        a.Players.Add(new Player { Id = 0, TeamId = 0 });
        a.Players.Add(new Player { Id = 1, TeamId = 1 });
        a.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        a.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));

        var grid2 = new Grid(8, 8);
        var b = new GameState(grid2);
        b.Players.Add(new Player { Id = 1, TeamId = 1 });
        b.Players.Add(new Player { Id = 0, TeamId = 0 });
        b.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        b.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));

        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Known_Fnv1a_Offset()
    {
        var state = MakeState();
        var hash = StateHasher.Hash(state);
        Assert.NotEqual(0u, hash);
    }

    [Fact]
    public void SpawnDisabled_Change_Changes_Hash()
    {
        var a = MakeState();
        var b = MakeState();

        b.Players[0].SpawnDisabled.Add(BlockType.Soldier);

        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void SpawnDisabled_Same_Content_Same_Hash()
    {
        var a = MakeState();
        var b = MakeState();

        a.Players[0].SpawnDisabled.Add(BlockType.Builder);
        b.Players[0].SpawnDisabled.Add(BlockType.Builder);

        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }
}
