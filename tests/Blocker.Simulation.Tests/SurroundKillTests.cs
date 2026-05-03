using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class SurroundKillTests
{
    public SurroundKillTests()
    {
        Constants.Initialize(new SimulationConfig
        {
            Combat = new CombatConfig { SurroundKillDelay = 0 }
        });
    }

    private GameState CreateState(int width = 10, int height = 10)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        return state;
    }

    private GameState CreateTeamState()
    {
        var state = new GameState(new Grid(10, 10));
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        state.Players.Add(new Player { Id = 2, TeamId = 1 }); // Ally of player 1
        return state;
    }

    [Fact]
    public void FullySurrounded_InstantKill_WhenDelayIsZero()
    {
        // 3x3 ring of enemy builders around a single target
        //  B B B
        //  B t B
        //  B B B
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Surround with player 1 builders
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void NotSurrounded_NoKill()
    {
        // Gap in the ring
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        // Left side open

        SurroundKillSystem.Tick(state);

        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void SurroundedByWallsAndEnemies_Kills()
    {
        // Corner scenario: target at (0,0), enemy on right and below, map edges on top and left
        // Actually map edge = escape route, so let's use terrain walls
        var state = CreateState();
        // Put target at (1,1), surround with terrain on top and left, enemies on right and bottom
        state.Grid[0, 1].Terrain = TerrainType.Terrain; // Left
        state.Grid[1, 0].Terrain = TerrainType.Terrain; // Top
        state.Grid[0, 0].Terrain = TerrainType.Terrain; // Top-left corner

        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 1)); // Right
        state.AddBlock(BlockType.Builder, 1, new GridPos(1, 2)); // Below
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 2)); // Diagonal fill

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void MapEdge_IsEscapeRoute()
    {
        // Target at map edge with enemies on other sides — should survive
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(0, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(1, 5)); // Right
        state.AddBlock(BlockType.Builder, 1, new GridPos(0, 4)); // Above
        state.AddBlock(BlockType.Builder, 1, new GridPos(0, 6)); // Below

        SurroundKillSystem.Tick(state);

        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void VictimImmobileBlocks_CountAsWall()
    {
        // Victim's own rooted builder helps trap them
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));

        // Victim's own wall on left
        state.AddBlock(BlockType.Wall, 0, new GridPos(0, 1));
        // Terrain above
        state.Grid[1, 0].Terrain = TerrainType.Terrain;
        state.Grid[0, 0].Terrain = TerrainType.Terrain;
        state.Grid[2, 0].Terrain = TerrainType.Terrain;
        // Enemy blocks on right and below
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(1, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(0, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 2));

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void VictimMobileBlocks_ArePassable()
    {
        // Victim's own mobile builder does NOT trap them
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Builder, 0, new GridPos(0, 1)); // Friendly mobile — passable

        // Terrain above
        state.Grid[1, 0].Terrain = TerrainType.Terrain;
        state.Grid[0, 0].Terrain = TerrainType.Terrain;
        // Enemy blocks on right and below
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(1, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(0, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 0));

        SurroundKillSystem.Tick(state);

        // Target should survive because friendly mobile block provides escape route
        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void DelayedKill_RequiresMultipleTicks()
    {
        Constants.Initialize(new SimulationConfig
        {
            Combat = new CombatConfig { SurroundKillDelay = 3 }
        });

        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        // Tick 1, 2, 3: alive
        SurroundKillSystem.Tick(state);
        Assert.Contains(target, state.Blocks);
        Assert.Equal(1, target.TrapTicks);

        SurroundKillSystem.Tick(state);
        Assert.Contains(target, state.Blocks);
        Assert.Equal(2, target.TrapTicks);

        SurroundKillSystem.Tick(state);
        Assert.Contains(target, state.Blocks);
        Assert.Equal(3, target.TrapTicks);

        // Tick 4: dies (TrapTicks reaches delay + 1)
        SurroundKillSystem.Tick(state);
        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void TrapTimer_ResetsWhenFreed()
    {
        Constants.Initialize(new SimulationConfig
        {
            Combat = new CombatConfig { SurroundKillDelay = 5 }
        });

        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        var blocker = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);
        SurroundKillSystem.Tick(state);
        Assert.Equal(2, target.TrapTicks);

        // Remove a blocker — ring breaks
        state.RemoveBlock(blocker);

        SurroundKillSystem.Tick(state);
        Assert.Equal(0, target.TrapTicks);
        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void WallsImmune_ToSurroundKill()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.Contains(wall, state.Blocks);
    }

    [Fact]
    public void RootedVictimBlock_IsImmobile_NotKilledButFormsWall()
    {
        // A rooted victim block is immobile — it's part of the wall, not a target
        var state = CreateState();
        var rooted = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        rooted.State = BlockState.Rooted;
        rooted.RootProgress = 36;

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        // Rooted block is not killed (it's immobile, CanBeTrapped returns false)
        Assert.Contains(rooted, state.Blocks);
    }

    [Fact]
    public void TeamAllies_FormEncirclement()
    {
        var state = CreateTeamState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Player 1 blocks on some sides
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));

        // Player 2 (ally of player 1) blocks on other sides
        state.AddBlock(BlockType.Builder, 2, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 2, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 2, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 2, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void LargeArea_ExceedsCap_NoKill()
    {
        // Create a large open area — flood fill exceeds cap
        var state = CreateState(20, 20);
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(10, 10));

        // Put enemies far away — they don't form a tight ring
        state.AddBlock(BlockType.Builder, 1, new GridPos(10, 9));
        state.AddBlock(BlockType.Builder, 1, new GridPos(11, 10));

        SurroundKillSystem.Tick(state);

        Assert.Contains(target, state.Blocks);
    }

    [Fact]
    public void NuggetsAreImpassable()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));

        // Surround with mix of enemies and nuggets
        state.Grid[0, 0].Terrain = TerrainType.Terrain;
        state.Grid[1, 0].Terrain = TerrainType.Terrain;
        state.Grid[2, 0].Terrain = TerrainType.Terrain;
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(2, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(1, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(0, 2));
        // Nugget on left
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(0, 1));
        nugget.NuggetState = new NuggetState();

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }

    [Fact]
    public void MultipleTrappedBlocks_AllDie()
    {
        var state = CreateState();
        // Two targets in same pocket
        var target1 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));
        var target2 = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 6));

        // Surround the 2-cell pocket
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 7));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 7));

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target1, state.Blocks);
        Assert.DoesNotContain(target2, state.Blocks);
    }

    [Fact]
    public void KillAttribution_TracksAttackerPlayer()
    {
        Constants.Initialize(new SimulationConfig
        {
            Combat = new CombatConfig { SurroundKillDelay = 1 }
        });

        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.Equal(1, target.TrappedByPlayerId);
    }

    [Fact]
    public void SurroundKilled_VisualEvent_Emitted()
    {
        var state = CreateState();
        state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.Contains(state.VisualEvents, e => e.Type == VisualEventType.SurroundTrapped);
        Assert.Contains(state.VisualEvents, e => e.Type == VisualEventType.SurroundKilled);
    }

    [Fact]
    public void AnySoldierType_CanFormEncirclement()
    {
        var state = CreateState();
        var target = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 5));

        // Mix of block types forming the ring
        state.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));
        state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Warden, 1, new GridPos(6, 4));
        state.AddBlock(BlockType.Jumper, 1, new GridPos(4, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 1, new GridPos(4, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 6));
        state.AddBlock(BlockType.Builder, 1, new GridPos(6, 6));

        SurroundKillSystem.Tick(state);

        Assert.DoesNotContain(target, state.Blocks);
    }
}
