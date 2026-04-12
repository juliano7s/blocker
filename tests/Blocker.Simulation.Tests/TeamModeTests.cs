using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

/// <summary>
/// Locks in the M2 team-play behaviour: blocks owned by different players who
/// share a TeamId must be treated as allies by combat, ZoC, jumper movement,
/// stun rays, and tower targeting.
/// </summary>
public class TeamModeTests
{
    public TeamModeTests()
    {
        Constants.Reset();
    }

    /// <summary>
    /// Players 0 and 1 share team 0; player 2 is the lone enemy on team 1.
    /// Mirrors a 2v1 lobby for tests where the team mechanics matter.
    /// </summary>
    private static GameState TwoVsOneState()
    {
        var state = new GameState(new Grid(20, 20));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 0 });
        state.Players.Add(new Player { Id = 2, TeamId = 1 });
        return state;
    }

    [Fact]
    public void TeammateSoldiers_DontKillBuilderViaSurrounding()
    {
        // Player 0 builder surrounded on three sides by player 1 soldiers — same team,
        // so the surrounding-kill threshold (3 ortho enemy soldiers) must NOT trigger.
        var state = TwoVsOneState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6));

        CombatSystem.Tick(state);

        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void EnemySoldiers_StillKillBuilderInTeams()
    {
        // Three soldiers from team 1 still kill the team-0 target — confirms the
        // teammate exemption doesn't break standard cross-team combat.
        var state = TwoVsOneState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 2, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 2, new GridPos(6, 5));
        state.AddBlock(BlockType.Soldier, 2, new GridPos(5, 6));

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void WardenZoC_DoesNotSlowTeammates()
    {
        // A rooted Warden owned by player 0 should NOT slow player 1's soldier
        // (same team), but SHOULD slow player 2's soldier (enemy).
        var state = TwoVsOneState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(10, 10));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var ally = state.AddBlock(BlockType.Soldier, 1, new GridPos(11, 10));
        var enemy = state.AddBlock(BlockType.Soldier, 2, new GridPos(9, 10));

        WardenSystem.UpdateZoC(state);

        Assert.Equal(ally.MoveInterval, ally.EffectiveMoveInterval);
        Assert.Equal(enemy.MoveInterval * 2, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void Jumper_StoppedByTeammate_NotKilled()
    {
        // Jumper from player 0 jumps right; player 1 ally sits in the path.
        // Expectation: jump stops at the ally cell, ally is alive, no kill registered
        // (kills+combo path requires the obstacle to be an enemy).
        var state = TwoVsOneState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));
        jumper.Hp = Constants.JumperMaxHp;
        var ally = state.AddBlock(BlockType.Soldier, 1, new GridPos(7, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Contains(ally, state.Blocks);
        Assert.Equal(new GridPos(6, 5), jumper.Pos); // Landed in cell before the ally
        // Miss path applies cooldown — confirms it didn't combo through the ally.
        Assert.True(jumper.IsOnCooldown);
    }

    [Fact]
    public void StunRay_PassesThroughTeammate_HitsEnemyBehind()
    {
        // Player 0 fires a stun ray right. Player 1 ally is two cells over,
        // player 2 enemy four cells over. Stun ray should not stop at the ally,
        // and should hit the enemy.
        var state = TwoVsOneState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(5, 5));
        stunner.State = BlockState.Mobile;
        var ally = state.AddBlock(BlockType.Soldier, 1, new GridPos(7, 5));
        var enemy = state.AddBlock(BlockType.Soldier, 2, new GridPos(9, 5));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        // Pump enough ticks for the unit-ray to traverse the distance.
        for (int i = 0; i < 30; i++) StunSystem.Tick(state);

        // Ally untouched (no stun), enemy is stunned.
        Assert.Equal(0, ally.StunTimer);
        Assert.True(enemy.StunTimer > 0);
    }

    [Fact]
    public void GameMode_TeamForSlot()
    {
        Assert.Equal(0, GameMode.Ffa.TeamForSlot(0));
        Assert.Equal(3, GameMode.Ffa.TeamForSlot(3));

        Assert.Equal(0, GameMode.Teams.TeamForSlot(0));
        Assert.Equal(0, GameMode.Teams.TeamForSlot(1));
        Assert.Equal(1, GameMode.Teams.TeamForSlot(2));
        Assert.Equal(1, GameMode.Teams.TeamForSlot(3));
        Assert.Equal(2, GameMode.Teams.TeamForSlot(4));
        Assert.Equal(2, GameMode.Teams.TeamForSlot(5));
    }

    [Fact]
    public void GameMode_IsValidFor()
    {
        Assert.True(GameMode.Ffa.IsValidFor(2));
        Assert.True(GameMode.Ffa.IsValidFor(3));
        Assert.False(GameMode.Ffa.IsValidFor(1));

        Assert.True(GameMode.Teams.IsValidFor(2));
        Assert.True(GameMode.Teams.IsValidFor(4));
        Assert.True(GameMode.Teams.IsValidFor(6));
        Assert.False(GameMode.Teams.IsValidFor(3));
        Assert.False(GameMode.Teams.IsValidFor(5));
    }

    [Fact]
    public void EliminationSystem_TeamSurvives_WhenOneTeammateAlive()
    {
        // Team 0 has two players; player 0 has nothing, player 1 has units. Team 0
        // must still be considered active so the game doesn't declare team 1 winner.
        var state = TwoVsOneState();
        // Player 0: nothing
        // Player 1: 4 builders + 1 soldier (above the 3-builder elimination threshold)
        for (int i = 0; i < 4; i++)
            state.AddBlock(BlockType.Builder, 1, new GridPos(2 + i, 2));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(2, 4));
        // Player 2 (enemy team): also has units so it isn't eliminated
        state.AddBlock(BlockType.Soldier, 2, new GridPos(15, 15));

        EliminationSystem.Tick(state);
        var winner = EliminationSystem.GetWinningTeam(state);

        Assert.Null(winner); // Game still in progress, both teams have active players
    }

    [Fact]
    public void SurrenderCommand_EliminatesIssuingPlayer_AndEndsGameForLastTeam()
    {
        // 1v1: player 0 (team 0) vs player 2 (team 1). Player 0 surrenders → team 1 wins.
        // Each player needs ≥3 builders so EliminationSystem doesn't kill them on its own.
        var state = new GameState(new Grid(20, 20));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 2, TeamId = 1 });
        for (int i = 0; i < 3; i++) state.AddBlock(BlockType.Builder, 0, new GridPos(2 + i, 2));
        for (int i = 0; i < 3; i++) state.AddBlock(BlockType.Builder, 2, new GridPos(15 + i, 15));

        var surrender = new Commands.Command(0, Commands.CommandType.Surrender, new List<int>());
        state.Tick(new List<Commands.Command> { surrender });

        Assert.True(state.Players.First(p => p.Id == 0).IsEliminated);
        Assert.False(state.Players.First(p => p.Id == 2).IsEliminated);
        Assert.Equal(1, EliminationSystem.GetWinningTeam(state));
    }

    [Fact]
    public void SurrenderCommand_OneTeammate_DoesNotEndTeam()
    {
        // 2v1: player 0 surrenders, player 1 (same team) still alive → team 0 not eliminated.
        var state = TwoVsOneState();
        for (int i = 0; i < 3; i++) state.AddBlock(BlockType.Builder, 0, new GridPos(2 + i, 2));
        for (int i = 0; i < 3; i++) state.AddBlock(BlockType.Builder, 1, new GridPos(2 + i, 4));
        for (int i = 0; i < 3; i++) state.AddBlock(BlockType.Builder, 2, new GridPos(15 + i, 15));

        var surrender = new Commands.Command(0, Commands.CommandType.Surrender, new List<int>());
        state.Tick(new List<Commands.Command> { surrender });

        Assert.True(state.Players.First(p => p.Id == 0).IsEliminated);
        Assert.False(state.Players.First(p => p.Id == 1).IsEliminated);
        Assert.Null(EliminationSystem.GetWinningTeam(state)); // game continues
    }
}
