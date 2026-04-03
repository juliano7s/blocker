using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class JumperTests
{
    private GameState CreateState(int width = 15, int height = 15)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

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

    [Fact]
    public void Jump_GrantsComboOnKill()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.True(jumper.HasCombo);
        Assert.Equal(Constants.JumperJumpCooldown, jumper.Cooldown);
    }

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
    public void Jump_Miss_DiesAtZeroHp()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        jumper.Hp = 1;

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.DoesNotContain(jumper, state.Blocks);
    }

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
        state.Grid[6, 7].Ground = GroundType.Terrain;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(new GridPos(5, 7), jumper.Pos);
    }

    [Fact]
    public void Jump_ComboAllowsImmediateSecondJump()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(1, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(3, 7)); // Kill to get combo

        JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(jumper.HasCombo);
        Assert.True(jumper.IsOnCooldown);

        // Second jump should work despite cooldown (combo)
        var result = JumperSystem.Jump(state, jumper, Direction.Right);
        Assert.True(result);
        Assert.True(jumper.Pos.X > 6);
    }

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

    [Fact]
    public void Jump_DoesNotKillFriendly()
    {
        var state = CreateState();
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(3, 7));
        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));

        JumperSystem.Jump(state, jumper, Direction.Right);

        // Friendly should still exist — jumper stops before it (can't pass through)
        Assert.Contains(friendly, state.Blocks);
    }

    [Fact]
    public void ConsumeCombo_ClearsFlag()
    {
        var jumper = new Block { Type = BlockType.Jumper };
        jumper.HasCombo = true;

        JumperSystem.ConsumeCombo(jumper);

        Assert.False(jumper.HasCombo);
    }
}
