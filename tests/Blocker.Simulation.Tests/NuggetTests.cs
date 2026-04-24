using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
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

    // --- Part A: CombatSystem ---

    [Fact]
    public void CombatSystem_SkipsNuggets_NotKilledBySurrounding()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6));

        CombatSystem.Tick(state);

        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    // --- Part B: EliminationSystem ---

    [Fact]
    public void EliminationSystem_IgnoresNuggets()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
    }

    // --- Part C: StunSystem ---

    [Fact]
    public void StunRay_StopsAtNugget_DoesNotDestroy()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.NotNull(state.GetBlock(nugget.Id));
        Assert.Equal(0, nugget.StunTimer);
    }

    [Fact]
    public void StunRay_DecrementsFortifiedWallHp()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
        wall.FortifiedHp = 3;

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.NotNull(state.GetBlock(wall.Id));
        Assert.Equal(2, wall.FortifiedHp);
    }

    [Fact]
    public void StunRay_DestroysWallWhenFortificationDepleted()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
        wall.FortifiedHp = 0;

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.Null(state.GetBlock(wall.Id));
    }

    // --- Part D: JumperSystem ---

    [Fact]
    public void Jumper_StopsAtUnminedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(new GridPos(6, 5), jumper.Pos);
        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    [Fact]
    public void Jumper_DestroysMinedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 1, new GridPos(7, 5));
        nugget.NuggetState!.IsMined = true;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Null(state.GetBlock(nugget.Id));
    }

    // --- Part E: NuggetSystem — Mining ---

    [Fact]
    public void Mining_OneBuilder_AdvancesProgress()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        builder.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0; // Mining has started

        NuggetSystem.Tick(state);

        Assert.Equal(1, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_TwoBuilders_AdvancesFaster()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        var b2 = state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5));
        b1.MiningTargetId = nugget.Id;
        b2.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0;

        NuggetSystem.Tick(state);

        Assert.Equal(2, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_NonAdjacentBuilder_DoesNotCount()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 3)); // 2 cells away
        builder.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0;

        NuggetSystem.Tick(state);

        Assert.Equal(0, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_CompletesAndFreesNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        nugget.NuggetState!.MiningProgress = Constants.NuggetMiningTicks - 1;
        nugget.PlayerId = 0;

        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        builder.MiningTargetId = nugget.Id;

        NuggetSystem.Tick(state);

        Assert.True(nugget.NuggetState.IsMined);
        Assert.Equal(0, nugget.PlayerId);
        Assert.False(nugget.IsImmobile);
    }

    [Fact]
    public void Mining_ExclusiveToOneTeam()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        nugget.PlayerId = 0; // Team 0 started mining

        var b0 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        b0.MiningTargetId = nugget.Id;

        var b1 = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        b1.MiningTargetId = nugget.Id;

        NuggetSystem.Tick(state);

        Assert.Equal(1, nugget.NuggetState!.MiningProgress);
    }
}
