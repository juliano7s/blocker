using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class EliminationTests
{
    private GameState CreateState(int width = 15, int height = 15)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    [Fact]
    public void NotEliminated_WithArmy()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Soldier, 0, new GridPos(3, 3));
        state.AddBlock(BlockType.Builder, 1, new GridPos(10, 10));

        EliminationSystem.Tick(state);

        Assert.False(state.Players[0].IsEliminated);
    }

    [Fact]
    public void NotEliminated_WithThreeBuilders()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Builder, 0, new GridPos(3, 3));
        state.AddBlock(BlockType.Builder, 0, new GridPos(4, 3));
        state.AddBlock(BlockType.Builder, 0, new GridPos(5, 3));

        EliminationSystem.Tick(state);

        Assert.False(state.Players[0].IsEliminated);
    }

    [Fact]
    public void NotEliminated_WithNest()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Builder, 0, new GridPos(3, 3));
        state.Nests.Add(new Nest { PlayerId = 0 });

        EliminationSystem.Tick(state);

        Assert.False(state.Players[0].IsEliminated);
    }

    [Fact]
    public void Eliminated_NoArmyNoNestsFewBuilders()
    {
        var state = CreateState();
        // Player 0 has only 2 builders, no army, no nests
        state.AddBlock(BlockType.Builder, 0, new GridPos(3, 3));
        state.AddBlock(BlockType.Builder, 0, new GridPos(4, 3));
        // Player 1 has army
        state.AddBlock(BlockType.Soldier, 1, new GridPos(10, 10));

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
        Assert.False(state.Players[1].IsEliminated);
    }

    [Fact]
    public void Eliminated_NoBlocksAtAll()
    {
        var state = CreateState();
        // Player 0 has nothing
        state.AddBlock(BlockType.Soldier, 1, new GridPos(10, 10));

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
    }

    [Fact]
    public void Eliminated_OnlyWalls_DontCount()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Wall, 0, new GridPos(3, 3));
        state.AddBlock(BlockType.Wall, 0, new GridPos(4, 3));
        state.AddBlock(BlockType.Wall, 0, new GridPos(5, 3));

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
    }

    [Fact]
    public void GetWinningTeam_OneTeamLeft()
    {
        var state = CreateState();
        state.Players[0].IsEliminated = true;

        var winner = EliminationSystem.GetWinningTeam(state);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void GetWinningTeam_GameInProgress()
    {
        var state = CreateState();

        var winner = EliminationSystem.GetWinningTeam(state);

        Assert.Null(winner);
    }

    [Fact]
    public void GetWinningTeam_SimultaneousElimination_MostBlocksWins()
    {
        var state = CreateState();
        state.Players[0].IsEliminated = true;
        state.Players[1].IsEliminated = true;

        // Player 1 has more blocks
        state.AddBlock(BlockType.Wall, 1, new GridPos(3, 3));
        state.AddBlock(BlockType.Wall, 1, new GridPos(4, 3));

        var winner = EliminationSystem.GetWinningTeam(state);

        Assert.Equal(1, winner);
    }

    [Fact]
    public void TeamMode_OnePlayerEliminated_TeamNotEliminated()
    {
        var state = new GameState(new Grid(15, 15));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 0 }); // Same team
        state.Players.Add(new Player { Id = 2, TeamId = 1 });

        // Player 0 eliminated, but player 1 (same team) still alive
        state.Players[0].IsEliminated = true;
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 2, new GridPos(10, 10));

        var winner = EliminationSystem.GetWinningTeam(state);

        Assert.Null(winner); // Game still in progress — team 0 has player 1 alive
    }

    [Fact]
    public void EliminationPersists_AlreadyEliminated()
    {
        var state = CreateState();
        state.Players[0].IsEliminated = true;

        // Even if player 0 somehow gets blocks, stay eliminated
        state.AddBlock(BlockType.Soldier, 0, new GridPos(3, 3));

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
    }
}
