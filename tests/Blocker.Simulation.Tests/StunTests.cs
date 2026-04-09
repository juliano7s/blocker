using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class StunTests
{
    public StunTests()
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
    public void FireStunRay_Creates3Rays()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        Assert.Equal(3, state.Rays.Count);
        Assert.All(state.Rays, r => Assert.Equal(RayType.Stun, r.Type));
        Assert.All(state.Rays, r => Assert.Equal(Direction.Right, r.Direction));
    }

    [Fact]
    public void FireStunRay_AppliesCooldown()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        Assert.Equal(Constants.StunCooldown, stunner.Cooldown);
        Assert.True(stunner.IsOnCooldown);
    }

    [Fact]
    public void FireStunRay_CantFireOnCooldown()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);
        var result = StunSystem.FireStunRay(state, stunner, Direction.Left);

        Assert.False(result);
        Assert.Equal(3, state.Rays.Count); // Only first batch
    }

    [Fact]
    public void StunRay_StunsFirstEnemy()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(3, 7));
        var target = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        // Advance until target is stunned
        for (int i = 0; i < 20 && !target.IsStunned; i++)
            StunSystem.Tick(state);

        Assert.True(target.IsStunned);
        // Stun duration minus 1 (decay happens same tick as hit)
        Assert.Equal(Constants.StunDuration - 1, target.StunTimer);
    }

    [Fact]
    public void StunRay_KillsWalls()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(3, 7));
        var wall = state.AddBlock(BlockType.Wall, 1, new GridPos(6, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.DoesNotContain(wall, state.Blocks);
    }

    [Fact]
    public void StunRay_DoesntHitFriendly()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(3, 7));
        var friendly = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 7));
        var enemy = state.AddBlock(BlockType.Builder, 1, new GridPos(7, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.False(friendly.IsStunned);
        Assert.True(enemy.IsStunned);
    }

    [Fact]
    public void StunRay_StopsAfterRange()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(2, 7));
        // Place enemy beyond range (range 5, enemy at distance 8)
        var farEnemy = state.AddBlock(BlockType.Builder, 1, new GridPos(11, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 30; i++)
            StunSystem.Tick(state);

        Assert.False(farEnemy.IsStunned);
        // All rays should have expired
        Assert.All(state.Rays, r => Assert.True(r.IsExpired));
    }

    [Fact]
    public void CooldownDecays_EachTick()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));

        StunSystem.FireStunRay(state, stunner, Direction.Right);
        Assert.Equal(Constants.StunCooldown, stunner.Cooldown);

        StunSystem.Tick(state);
        Assert.Equal(Constants.StunCooldown - 1, stunner.Cooldown);
    }

    [Fact]
    public void StunTimerDecays_EachTick()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(7, 7));
        target.StunTimer = 10;

        StunSystem.Tick(state);

        Assert.Equal(9, target.StunTimer);
    }

    [Fact]
    public void SelfDestruct_KillsStunner()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));
        stunner.State = BlockState.Rooted;
        stunner.RootProgress = Constants.RootTicks;

        ExplosionSystem.SelfDestruct(state, stunner);

        Assert.DoesNotContain(stunner, state.Blocks);
        Assert.True(state.Rays.Count > 0); // Rays were created
    }

    [Fact]
    public void SelfDestruct_RequiresFullyRooted()
    {
        var state = CreateState();
        var stunner = state.AddBlock(BlockType.Stunner, 0, new GridPos(7, 7));
        // Not rooted

        var result = ExplosionSystem.SelfDestruct(state, stunner);

        Assert.False(result);
        Assert.Contains(stunner, state.Blocks);
    }

    [Fact]
    public void StunnedBlock_CantMove()
    {
        var state = CreateState();
        state.Players.Add(new Player { Id = 2, TeamId = 2 });
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        builder.MoveTarget = new GridPos(8, 5);
        builder.StunTimer = 5;

        var startPos = builder.Pos;

        // Several ticks — builder shouldn't move while stunned
        for (int i = 0; i < 3; i++)
            state.Tick();

        Assert.Equal(startPos, builder.Pos);
    }
}
