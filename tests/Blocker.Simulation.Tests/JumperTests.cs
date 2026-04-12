using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class JumperTests
{
    public JumperTests()
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

    // --- Basic movement ---

    [Fact]
    public void Jump_MovesToLandingPos()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        var result = JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.True(result);
        // Should jump full 5 cells to (8, 7)
        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_KillsEnemiesInPath()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        var enemy1 = state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));
        var enemy2 = state.AddBlock(BlockType.Builder, 1, new GridPos(7, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.DoesNotContain(enemy1, state.Blocks);
        Assert.DoesNotContain(enemy2, state.Blocks);
        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    // --- Stopping conditions ---

    [Fact]
    public void Jump_StopsAtWall()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Wall, 1, new GridPos(6, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should stop just before the wall at (5, 7)
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_StopsAtTerrain()
    {
        var state = CreateState();
        state.Grid[6, 7].Terrain = TerrainType.Terrain;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(new GridPos(5, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_StopsAtFormation()
    {
        var state = CreateState();
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 7));
        enemy.FormationId = 1; // Mark as in formation

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should stop before the formation block at (5, 7), enemy not killed
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
        Assert.Contains(enemy, state.Blocks);
    }

    [Fact]
    public void Jump_StopsAtRootedBlock()
    {
        var state = CreateState();
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 7));
        enemy.State = BlockState.Rooted;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should stop before the rooted block at (5, 7), enemy not killed
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
        Assert.Contains(enemy, state.Blocks);
    }

    [Fact]
    public void Jump_KillsFriendlyBlocksInPath()
    {
        var state = CreateState();
        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should kill the friendly block and continue through
        Assert.DoesNotContain(friendly, state.Blocks);
        Assert.True(jumper.HasCombo); // Kill grants combo
    }

    [Fact]
    public void Jump_StopsAtMapEdge()
    {
        var state = CreateState(10, 10);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(7, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Grid is 10 wide (0..9), so from 7, can go to 8 and 9, then edge
        Assert.Equal(new GridPos(9, 5), jumper.Pos);
    }

    // --- Breakable and fragile walls (game bible Section 5.3) ---

    [Fact]
    public void Jump_BreakableWall_ConvertsToFragile()
    {
        var state = CreateState();
        state.Grid[6, 7].Terrain = TerrainType.BreakableWall;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should stop before the breakable wall at (5, 7)
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
        // Breakable wall should convert to fragile wall
        Assert.Equal(TerrainType.FragileWall, state.Grid[6, 7].Terrain);
    }

    [Fact]
    public void Jump_FragileWall_DestroysIt()
    {
        var state = CreateState();
        state.Grid[6, 7].Terrain = TerrainType.FragileWall;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Should stop before the fragile wall at (5, 7)
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
        // Fragile wall should be destroyed (terrain cleared)
        Assert.Equal(TerrainType.None, state.Grid[6, 7].Terrain);
    }

    // --- Combo mechanics ---

    [Fact]
    public void Jump_GrantsComboOnKill()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.True(jumper.HasCombo);
        Assert.Equal(0, jumper.Cooldown); // No cooldown on kill — combo allows immediate re-jump
    }

    [Fact]
    public void Jump_ComboAllowsImmediateSecondJump()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(1, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(3, 7)); // Kill to get combo

        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);
        Assert.False(jumper.IsOnCooldown); // No cooldown with combo

        // Second jump should work immediately (combo)
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(result);
        Assert.True(jumper.Pos.X > 6);
    }

    [Fact]
    public void Jump_NoComboWhenHitsObstacleEvenWithKills()
    {
        // Bible: "If the jump kills at least one enemy and doesn't hit an obstacle"
        // If kill happens but also hits an obstacle (wall, terrain), no combo
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7)); // Enemy in path
        state.Grid[7, 7].Terrain = TerrainType.Terrain; // Obstacle behind enemy

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Jumper killed the enemy but hit terrain obstacle → no combo
        Assert.False(jumper.HasCombo);
    }

    [Fact]
    public void ConsumeCombo_ClearsFlagAndStartsCooldown()
    {
        var jumper = new Block { Type = BlockType.Jumper };
        jumper.HasCombo = true;

        JumperSystem.ConsumeCombo(jumper);

        Assert.False(jumper.HasCombo);
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);
    }

    // --- Miss mechanics (no kills) ---

    [Fact]
    public void Jump_Miss_LosesHp()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        Assert.Equal(Constants.JumperMaxHp, jumper.Hp); // 3

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(Constants.JumperMaxHp - 1, jumper.Hp);
        Assert.False(jumper.HasCombo);
    }

    [Fact]
    public void Jump_Miss_AppliesImmobileCooldown()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);
        Assert.False(jumper.HasCombo);
    }

    [Fact]
    public void Jump_Miss_DiesAtZeroHp()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        jumper.Hp = 1;

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.DoesNotContain(jumper, state.Blocks);
    }

    // --- Cooldown blocking ---

    [Fact]
    public void Jump_CooldownBlocksWithoutCombo()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        jumper.Cooldown = 50;

        var result = JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.False(result);
        Assert.Equal(new GridPos(3, 7), jumper.Pos);
    }

    // --- Warden ZoC ---

    [Fact]
    public void Jump_BlockedByWardenZoC()
    {
        var state = CreateState();
        var warden = state.AddBlock(BlockType.Warden, 1, new GridPos(7, 7));
        warden.State = BlockState.Rooted;
        warden.RootProgress = Constants.RootTicks;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 7)); // Within ZoC

        var result = JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.False(result);
        Assert.Equal(new GridPos(5, 7), jumper.Pos);
    }

    // --- Stunned jumper ---

    [Fact]
    public void Jump_StunnedJumperCannotJump()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        jumper.StunTimer = 100;

        var result = JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.False(result);
        Assert.Equal(new GridPos(3, 7), jumper.Pos);
    }

    // --- Direction tests ---

    [Fact]
    public void Jump_AllCardinalDirections()
    {
        // Test that jump works in all 4 cardinal directions
        var state = CreateState(20, 20);

        // Right
        var jumperR = state.AddBlock(BlockType.Jumper, 0, new GridPos(10, 10));
        JumperSystem.Jump(state, jumperR, Direction.Right);
        Assert.Equal(new GridPos(15, 10), jumperR.Pos);

        // Left
        var jumperL = state.AddBlock(BlockType.Jumper, 0, new GridPos(10, 10));
        JumperSystem.Jump(state, jumperL, Direction.Left);
        Assert.Equal(new GridPos(5, 10), jumperL.Pos);

        // Down
        var jumperD = state.AddBlock(BlockType.Jumper, 0, new GridPos(10, 10));
        JumperSystem.Jump(state, jumperD, Direction.Down);
        Assert.Equal(new GridPos(10, 15), jumperD.Pos);

        // Up
        var jumperU = state.AddBlock(BlockType.Jumper, 0, new GridPos(10, 10));
        JumperSystem.Jump(state, jumperU, Direction.Up);
        Assert.Equal(new GridPos(10, 5), jumperU.Pos);
    }

    // --- Grid cell consistency ---

    [Fact]
    public void Jump_UpdatesGridCells()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Old cell should be cleared
        Assert.Null(state.Grid[3, 7].BlockId);
        // New cell should contain jumper
        Assert.Equal(jumper.Id, state.Grid[8, 7].BlockId);
    }

    // --- Targeted range (click-to-cell) ---

    [Fact]
    public void Jump_TargetRange_ShorterThanMax()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        // Jump only 3 cells instead of max 5
        JumperSystem.Jump(state, jumper, Direction.Right, maxRange: 3);

        Assert.Equal(new GridPos(6, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_TargetRange_ClampedTo1()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        // Range 0 should clamp to 1
        JumperSystem.Jump(state, jumper, Direction.Right, maxRange: 0);

        Assert.Equal(new GridPos(4, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_TargetRange_CannotExceedMax()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        // Range 10 should clamp to 5
        JumperSystem.Jump(state, jumper, Direction.Right, maxRange: 10);

        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    // --- Combo + movement interaction (game bible Section 4.6) ---

    [Fact]
    public void Combo_CanJumpAgainImmediately()
    {
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7)); // Kill for combo

        // First jump kills enemy → combo
        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);
        Assert.Equal(0, jumper.Cooldown);

        // Second jump should succeed immediately with combo
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(result);
    }

    [Fact]
    public void Combo_MoveConsumesCombo_StartsMobileCooldown()
    {
        // After combo, issuing a move command consumes combo and starts mobile cooldown.
        // Jumper can still move but can't jump during cooldown.
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        // Jump kills enemy → combo
        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);

        // Consume combo via move
        JumperSystem.ConsumeCombo(jumper);
        Assert.False(jumper.HasCombo);
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);
        Assert.True(jumper.MobileCooldown); // Mobile cooldown — can still move
    }

    [Fact]
    public void Combo_CannotJumpAfterMoving()
    {
        // After consuming combo via move, jumper can't jump until cooldown expires
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        // Jump kills enemy → combo
        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);

        // Move consumes combo
        JumperSystem.ConsumeCombo(jumper);
        Assert.False(jumper.HasCombo);
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);

        // Can't jump during cooldown
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.False(result);
    }

    [Fact]
    public void Combo_CanMoveWhileComboNotConsumed()
    {
        // With active combo and no move issued, jumper should not be blocked from movement
        // (it simply hasn't moved yet — combo stays active)
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);
        Assert.Equal(0, jumper.Cooldown);

        // No move issued — combo stays, can still jump
        Assert.True(jumper.HasCombo);
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(result);
    }

    [Fact]
    public void Miss_ImmobileCooldown_BlocksMovement()
    {
        // After a miss (no kills), jumper is immobile during cooldown
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right); // Miss — no enemies
        Assert.False(jumper.HasCombo);
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);
        Assert.False(jumper.MobileCooldown); // NOT mobile — immobile cooldown

        // Set a move target and tick — jumper should NOT move
        jumper.MoveTarget = new GridPos(10, 7);
        state.Tick();
        // Jumper stays put (movement blocked by immobile cooldown)
        Assert.Equal(new GridPos(8, 7), jumper.Pos);
    }

    [Fact]
    public void ComboConsumed_MobileCooldown_AllowsMovement()
    {
        // After combo consumed by moving, jumper can still move during cooldown
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        JumperSystem.Jump(state, jumper, Direction.Right); // Kill → combo
        JumperSystem.ConsumeCombo(jumper); // Mobile cooldown

        Assert.True(jumper.MobileCooldown);
        Assert.True(jumper.IsOnCooldown);

        // Set a move target and tick — jumper SHOULD move (mobile cooldown)
        jumper.MoveTarget = new GridPos(10, 7);
        state.Tick();
        // Jumper should have moved (movement allowed during mobile cooldown)
        Assert.NotEqual(new GridPos(8, 7), jumper.Pos);
    }

    [Fact]
    public void Cooldown_DecaysOverTicks()
    {
        var state = CreateState(20, 15);
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right); // Miss
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);

        // Tick enough times for cooldown to expire
        for (int i = 0; i < Constants.JumperJumpCooldown; i++)
            state.Tick();

        Assert.False(jumper.IsOnCooldown);
        Assert.False(jumper.MobileCooldown);

        // Can jump again after cooldown expires
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(result);
    }
}
