using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class CombatTests
{
    public CombatTests()
    {
        Constants.Reset();
    }

    private GameState CreateState(int width = 10, int height = 10)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    [Fact]
    public void ThreeOrthoSoldiers_KillEnemyBuilder()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4)); // Above
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5)); // Right
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6)); // Below

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void TwoOrthoEnemyBuilders_DontTriggerSurroundingKill()
    {
        // Only Soldiers count as enemies for surrounding thresholds (Section 5.1)
        // Non-soldier enemies adjacent don't trigger surrounding kills
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        target.State = BlockState.Rooted;
        target.RootProgress = Constants.RootTicks;
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));

        CombatSystem.Tick(state);

        Assert.Contains(target, state.Blocks); // Rooted builder survives — enemy builders can't kill
    }

    [Fact]
    public void TwoOrthoSoldiers_PlusDiagonal_Kills()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4)); // Above
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5)); // Right
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 6)); // Diagonal

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void Overcrowding_TwoEnemies_TwoFriendlies_Kills()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4)); // Enemy above
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6)); // Enemy below
        state.AddBlock(BlockType.Builder, 0, new GridPos(4, 5)); // Friendly left
        state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5)); // Friendly right

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void OneSoldier_KillsMobileBuilder()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4)); // Adjacent

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void OneSoldier_DoesNotKillRootedBuilder()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        target.State = BlockState.Rooted;
        target.RootProgress = Constants.RootTicks;
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));

        CombatSystem.Tick(state);

        Assert.Contains(target, state.Blocks); // Rooted needs 2
    }

    [Fact]
    public void TwoSoldiers_KillRootedBuilder()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        target.State = BlockState.Rooted;
        target.RootProgress = Constants.RootTicks;
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 6)); // Diagonal — all 8 dirs for rooted

        CombatSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void Wall_ImmuneToSurrounding()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(5, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(4, 5));

        CombatSystem.Tick(state);

        Assert.Contains(wall, state.Blocks); // Immune
    }

    [Fact]
    public void SoldierLosesHp_WhenKillingEnemy()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5)); // Target
        var soldier = state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));

        Assert.Equal(Constants.SoldierMaxHp, soldier.Hp);

        CombatSystem.Tick(state);

        Assert.Equal(Constants.SoldierMaxHp - 1, soldier.Hp);
    }

    [Fact]
    public void MobileSoldiers_MutualKill()
    {
        var state = CreateState();
        var s0 = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
        var s1 = state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));

        CombatSystem.Tick(state);

        Assert.DoesNotContain(s0, state.Blocks);
        Assert.DoesNotContain(s1, state.Blocks);
    }

    [Fact]
    public void FriendlySoldiers_DontKillEachOther()
    {
        var state = CreateState();
        var s0 = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
        var s1 = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 4));

        CombatSystem.Tick(state);

        Assert.Contains(s0, state.Blocks);
        Assert.Contains(s1, state.Blocks);
    }
}
