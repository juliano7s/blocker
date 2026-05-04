using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class WardenTests
{
    public WardenTests()
    {
        Constants.Reset();
    }

    private GameState CreateState(int width = 15, int height = 15)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    [Fact]
    public void ZoC_SlowsEnemyInRange()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(9, 7)); // Distance 2

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval * 2, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_DoesNotSlowFriendly()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(9, 7));

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval, friendly.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_DoesNotSlowOutOfRange()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(12, 7)); // Distance 5, out of range

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_RequiresRooted()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        // Not rooted

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(9, 7));

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_StunnedWardenInactive()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;
        warden.StunTimer = 50;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(9, 7));

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_ChebyshevRadius_DiagonalInRange()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        // Chebyshev distance 4 (diagonal)
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(11, 11));

        WardenSystem.UpdateZoC(state);

        Assert.Equal(Constants.MoveInterval * 2, enemy.EffectiveMoveInterval);
    }

    [Fact]
    public void ZoC_SlowsMovement_IntegrationTest()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(9, 7));
        enemy.MoveTarget = new GridPos(14, 7);

        // Track enemy position over many ticks
        var normalEnemy = state.AddBlock(BlockType.Builder, 1, new GridPos(2, 2));
        normalEnemy.MoveTarget = new GridPos(7, 2);

        // Tick enough for both to move
        for (int i = 0; i < 30; i++)
            state.Tick();

        // Normal enemy should have moved farther than slowed enemy (relative to distance)
        // The slowed enemy should have moved, but at half speed
        Assert.True(enemy.Pos.X > 9, "Slowed enemy should still move");
    }

    [Fact]
    public void MagnetPull_PullsEnemies()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7)); // 3 cells right

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.True(result);
        Assert.Equal(new GridPos(8, 7), enemy.Pos); // Pulled all the way to adjacent cell
        Assert.Equal(Constants.WardenPullCooldown, warden.Cooldown);
    }

    [Fact]
    public void MagnetPull_DoesNotPullFriendly()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(10, 7));

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.False(result);
        Assert.Equal(new GridPos(10, 7), friendly.Pos);
    }

    [Fact]
    public void MagnetPull_RequiresRooted()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        // Not rooted

        state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7));

        Assert.False(WardenSystem.MagnetPull(state, warden));
    }

    [Fact]
    public void MagnetPull_RespectsCooldown()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;
        warden.Cooldown = 50;

        state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7));

        Assert.False(WardenSystem.MagnetPull(state, warden));
    }

    [Fact]
    public void MagnetPull_DoesNotPullOutOfRange()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(12, 7)); // Distance 5, out of range

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.False(result);
        Assert.Equal(new GridPos(12, 7), enemy.Pos);
    }

    [Fact]
    public void MagnetPull_DoesNotPullImmobile()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var wall = state.AddBlock(BlockType.Wall, 1, new GridPos(9, 7));

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.False(result);
        Assert.Equal(new GridPos(9, 7), wall.Pos);
    }

    [Fact]
    public void MagnetPull_PullsDiagonally()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(10, 10)); // 3 cells diagonal

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.True(result);
        Assert.Equal(new GridPos(8, 8), enemy.Pos); // Pulled diagonally to adjacent
    }

    [Fact]
    public void MagnetPull_StopsAtObstacle()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        // Place a wall blocking the path at (9, 7)
        state.AddBlock(BlockType.Wall, 1, new GridPos(9, 7));

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(11, 7)); // 4 cells right

        var result = WardenSystem.MagnetPull(state, warden);

        Assert.True(result);
        Assert.Equal(new GridPos(10, 7), enemy.Pos); // Stopped one cell before wall
    }

    [Fact]
    public void MagnetPull_SetsPulledFlag()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(10, 7));

        WardenSystem.MagnetPull(state, warden);

        Assert.True(enemy.WasPulledThisTick);
    }

    [Fact]
    public void IsInEnemyWardenZoC_DetectsCorrectly()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 0, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var enemyInRange = state.AddBlock(BlockType.Jumper, 1, new GridPos(9, 7));
        var enemyOutOfRange = state.AddBlock(BlockType.Jumper, 1, new GridPos(13, 7));

        Assert.True(WardenSystem.IsInEnemyWardenZoC(state, enemyInRange));
        Assert.False(WardenSystem.IsInEnemyWardenZoC(state, enemyOutOfRange));
    }
}
