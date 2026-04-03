using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class TowerTests
{
    private GameState CreateState(int width = 15, int height = 15)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    private Block AddRootedBlock(GameState state, BlockType type, int playerId, GridPos pos)
    {
        var block = state.AddBlock(type, playerId, pos);
        block.State = BlockState.Rooted;
        block.RootProgress = Constants.RootTicks;
        return block;
    }

    // === Stun Tower Creation ===

    [Fact]
    public void StunTower_Created_WithStunnerAndBuilder()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        var builder = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7)); // Right

        var result = TowerSystem.CreateTower(state, stunner);

        Assert.True(result);
        Assert.Single(state.Towers);
        Assert.Equal(TowerType.Stun, state.Towers[0].Type);
        Assert.Equal(stunner.Id, state.Towers[0].CenterId);
        Assert.Single(state.Towers[0].BuilderDirections);
        Assert.Equal(Direction.Right, state.Towers[0].BuilderDirections[builder.Id]);
    }

    [Fact]
    public void StunTower_FailsWithoutAdjacentBuilder()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));

        var result = TowerSystem.CreateTower(state, stunner);

        Assert.False(result);
        Assert.Empty(state.Towers);
    }

    [Fact]
    public void StunTower_FailsIfNotRooted()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        var result = TowerSystem.CreateTower(state, stunner);

        Assert.False(result);
    }

    [Fact]
    public void StunTower_MembersGetFormationId()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        var builder = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        TowerSystem.CreateTower(state, stunner);

        Assert.Equal(state.Towers[0].Id, stunner.FormationId);
        Assert.Equal(state.Towers[0].Id, builder.FormationId);
    }

    // === Soldier Tower Creation ===

    [Fact]
    public void SoldierTower_Created_WithSoldierAndBuilder()
    {
        var state = CreateState();
        var soldier = AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(7, 6)); // Above

        var result = TowerSystem.CreateTower(state, soldier);

        Assert.True(result);
        Assert.Single(state.Towers);
        Assert.Equal(TowerType.Soldier, state.Towers[0].Type);
    }

    // === Arm Management ===

    [Fact]
    public void Tower_NewBuilderJoinsAutomatically()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        TowerSystem.CreateTower(state, stunner);
        Assert.Single(state.Towers[0].BuilderDirections);

        // Add another builder to the left
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 7));

        TowerSystem.Tick(state);

        Assert.Equal(2, state.Towers[0].BuilderDirections.Count);
    }

    [Fact]
    public void Tower_DissolvesWhenCenterKilled()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        var builder = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        TowerSystem.CreateTower(state, stunner);
        Assert.Single(state.Towers);

        state.RemoveBlock(stunner);
        TowerSystem.Tick(state);

        Assert.Empty(state.Towers);
        Assert.Null(builder.FormationId);
    }

    [Fact]
    public void Tower_DissolvesWhenNoBuilders()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        var builder = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        TowerSystem.CreateTower(state, stunner);

        state.RemoveBlock(builder);
        TowerSystem.Tick(state);

        Assert.Empty(state.Towers);
        Assert.Null(stunner.FormationId);
    }

    // === Firing ===

    [Fact]
    public void SoldierTower_FiresBlastWhenEnemyDetected()
    {
        var state = CreateState();
        var soldier = AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7)); // Right arm

        TowerSystem.CreateTower(state, soldier);

        // Place enemy in firing line (right of tower)
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7));

        // Tick enough for fire interval
        for (int i = 0; i < Constants.SoldierTowerFireInterval + 1; i++)
            TowerSystem.Tick(state);

        // Should have created blast rays
        Assert.True(state.Rays.Count > 0);
        Assert.Contains(state.Rays, r => r.Type == RayType.Blast);
    }

    [Fact]
    public void StunTower_FiresStunWhenEnemyDetected()
    {
        var state = CreateState();
        var stunner = AddRootedBlock(state, BlockType.Stunner, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7)); // Right arm

        TowerSystem.CreateTower(state, stunner);

        // Place enemy in firing line
        state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7));

        // Tick enough for detection + fire
        for (int i = 0; i < Constants.StunTowerFireInterval + 2; i++)
            TowerSystem.Tick(state);

        Assert.True(state.Rays.Count > 0);
        Assert.Contains(state.Rays, r => r.Type == RayType.Stun);
    }

    [Fact]
    public void Tower_DoesntFireWithoutEnemies()
    {
        var state = CreateState();
        var soldier = AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(7, 7));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(8, 7));

        TowerSystem.CreateTower(state, soldier);

        for (int i = 0; i < 50; i++)
            TowerSystem.Tick(state);

        Assert.Empty(state.Rays);
    }
}
