using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class NestTests
{
    private GameState CreateState(int width = 10, int height = 10)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0, MaxPopulation = 50 });
        state.Players.Add(new Player { Id = 1, TeamId = 1, MaxPopulation = 50 });
        return state;
    }

    private void SetGroundType(GameState state, GridPos pos, GroundType ground)
    {
        state.Grid[pos].Ground = ground;
    }

    private Block AddRootedBlock(GameState state, BlockType type, int playerId, GridPos pos)
    {
        var block = state.AddBlock(type, playerId, pos);
        block.State = BlockState.Rooted;
        block.RootProgress = Constants.RootTicks;
        return block;
    }

    // === Builder Nest Detection ===

    [Fact]
    public void BuilderNest_ThreeRootedBuildersOnBootZone_FormsNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4)); // Above
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5)); // Right
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6)); // Below

        NestSystem.DetectNests(state);

        Assert.Single(state.Nests);
        Assert.Equal(NestType.Builder, state.Nests[0].Type);
        Assert.Equal(center, state.Nests[0].Center);
        Assert.Equal(0, state.Nests[0].PlayerId);
    }

    [Fact]
    public void BuilderNest_TwoBuildersOnly_NoNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));

        NestSystem.DetectNests(state);

        Assert.Empty(state.Nests);
    }

    [Fact]
    public void BuilderNest_NotOnNestZone_NoNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        // Normal ground — not a nest zone

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);

        Assert.Empty(state.Nests);
    }

    [Fact]
    public void BuilderNest_MobileBuildersNotCounted()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        state.AddBlock(BlockType.Builder, 0, new GridPos(5, 6)); // Mobile — doesn't count

        NestSystem.DetectNests(state);

        Assert.Empty(state.Nests);
    }

    [Fact]
    public void BuilderNest_MixedOwnership_NoNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 1, new GridPos(5, 6)); // Enemy

        NestSystem.DetectNests(state);

        Assert.Empty(state.Nests);
    }

    // === Soldier Nest Detection ===

    [Fact]
    public void SoldierNest_ThreeBuildersAndTwoWalls_FormsNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        // 3 Builders orthogonal
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));
        // 2 Walls diagonal
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(6, 4));
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(4, 6));

        NestSystem.DetectNests(state);

        Assert.Single(state.Nests);
        Assert.Equal(NestType.Soldier, state.Nests[0].Type);
    }

    // === Stunner Nest Detection ===

    [Fact]
    public void StunnerNest_ThreeSoldiersAndTwoWalls_FormsNest()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        // 3 Soldiers orthogonal
        AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Soldier, 0, new GridPos(5, 6));
        // 2 Walls diagonal
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(6, 4));
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(4, 6));

        NestSystem.DetectNests(state);

        Assert.Single(state.Nests);
        Assert.Equal(NestType.Stunner, state.Nests[0].Type);
    }

    // === Auto-upgrade ===

    [Fact]
    public void BuilderNest_AutoUpgradesToSoldierNest_WhenWallsAppear()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        Assert.Equal(NestType.Builder, state.Nests[0].Type);

        // Add walls at diagonals
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(6, 4));
        AddRootedBlock(state, BlockType.Wall, 0, new GridPos(4, 6));

        NestSystem.DetectNests(state);
        Assert.Equal(NestType.Soldier, state.Nests[0].Type);
        Assert.Equal(5, state.Nests[0].MemberIds.Count);
    }

    // === Spawning ===

    [Fact]
    public void BuilderNest_SpawnsBuilder_AfterSpawnTicks()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        int initialBlockCount = state.Blocks.Count;

        // Tick until spawn
        for (int i = 0; i < Constants.SpawnTicksBuilder; i++)
            NestSystem.TickSpawning(state);

        Assert.Equal(initialBlockCount + 1, state.Blocks.Count);
        var spawned = state.Blocks[^1];
        Assert.Equal(BlockType.Builder, spawned.Type);
        Assert.Equal(0, spawned.PlayerId);
    }

    [Fact]
    public void BuilderNest_OnOverload_SpawnsWarden()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Overload);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);

        for (int i = 0; i < Constants.SpawnTicksWarden; i++)
            NestSystem.TickSpawning(state);

        var spawned = state.Blocks[^1];
        Assert.Equal(BlockType.Warden, spawned.Type);
    }

    [Fact]
    public void BuilderNest_OnProto_TakesFiveTimesLonger()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Proto);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        int initialBlockCount = state.Blocks.Count;

        // Not enough ticks at normal rate
        for (int i = 0; i < Constants.SpawnTicksBuilder; i++)
            NestSystem.TickSpawning(state);

        Assert.Equal(initialBlockCount, state.Blocks.Count); // Not yet

        // Complete the rest (5x total)
        for (int i = 0; i < Constants.SpawnTicksBuilder * 4; i++)
            NestSystem.TickSpawning(state);

        Assert.Equal(initialBlockCount + 1, state.Blocks.Count);
    }

    [Fact]
    public void Spawning_PausedWhenMemberStunned()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);

        // Advance partway
        for (int i = 0; i < 50; i++)
            NestSystem.TickSpawning(state);

        int progressBefore = state.Nests[0].SpawnProgress;

        // Stun a member
        b1.StunTimer = 10;
        NestSystem.TickSpawning(state);

        Assert.Equal(progressBefore, state.Nests[0].SpawnProgress); // No progress
    }

    [Fact]
    public void Spawning_BlockedByPopCap()
    {
        var state = CreateState();
        state.Players[0].MaxPopulation = 3; // Low cap
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        int initialBlockCount = state.Blocks.Count;

        // Try to spawn — pop is already 3 (the 3 builders) and cap is 3
        for (int i = 0; i < Constants.SpawnTicksBuilder + 10; i++)
            NestSystem.TickSpawning(state);

        Assert.Equal(initialBlockCount, state.Blocks.Count); // Nothing spawned
    }

    // === Nest Dissolution ===

    [Fact]
    public void Nest_DissolvedWhenMemberKilled()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        var b2 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        var b3 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        Assert.Single(state.Nests);
        Assert.NotNull(b1.FormationId);

        // Kill one member
        state.RemoveBlock(b1);

        NestSystem.DetectNests(state);
        Assert.Empty(state.Nests);
        Assert.Null(b2.FormationId);
        Assert.Null(b3.FormationId);
    }

    [Fact]
    public void Nest_MembersHaveFormationId()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        var b2 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        var b3 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);

        var nestId = state.Nests[0].Id;
        Assert.Equal(nestId, b1.FormationId);
        Assert.Equal(nestId, b2.FormationId);
        Assert.Equal(nestId, b3.FormationId);
    }

    // === Congestion ===

    [Fact]
    public void Spawning_FindsAlternateCellWhenCenterOccupied()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        // Occupy center
        state.AddBlock(BlockType.Wall, 0, center);

        NestSystem.DetectNests(state);

        for (int i = 0; i < Constants.SpawnTicksBuilder; i++)
            NestSystem.TickSpawning(state);

        // Should have spawned at an adjacent cell (4,5 is the only free ortho neighbor)
        var spawned = state.Blocks.FindAll(b => b.Type == BlockType.Builder && b.State == BlockState.Mobile);
        Assert.Single(spawned);
        Assert.Equal(new GridPos(4, 5), spawned[0].Pos);
    }

    // === Teardown ===

    [Fact]
    public void Nest_VoluntaryUproot_StartsTeardown()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        Assert.Single(state.Nests);

        // Player uproots one member
        RootingSystem.ToggleRoot(b1);
        Assert.Equal(BlockState.Uprooting, b1.State);

        // Nest should enter teardown but not dissolve immediately
        NestSystem.DetectNests(state);
        Assert.Single(state.Nests);
        Assert.True(state.Nests[0].IsTearingDown);
    }

    [Fact]
    public void Nest_TeardownCompletesAfterTimer()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        var b2 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        var b3 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);
        RootingSystem.ToggleRoot(b1); // Start uprooting

        // Tick teardown to completion
        for (int i = 0; i < Constants.TeardownTicks + 1; i++)
            NestSystem.DetectNests(state);

        Assert.Empty(state.Nests);
        Assert.Null(b2.FormationId);
        Assert.Null(b3.FormationId);
    }

    [Fact]
    public void Nest_TeardownCancelledWhenReRooted()
    {
        var state = CreateState();
        var center = new GridPos(5, 5);
        SetGroundType(state, center, GroundType.Boot);

        var b1 = AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 4));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(6, 5));
        AddRootedBlock(state, BlockType.Builder, 0, new GridPos(5, 6));

        NestSystem.DetectNests(state);

        // Start uprooting
        RootingSystem.ToggleRoot(b1);
        NestSystem.DetectNests(state);
        Assert.True(state.Nests[0].IsTearingDown);

        // Cancel — re-root (toggles back to rooting)
        RootingSystem.ToggleRoot(b1);
        Assert.Equal(BlockState.Rooting, b1.State);
        // Manually complete the rooting
        b1.State = BlockState.Rooted;
        b1.RootProgress = Constants.RootTicks;

        NestSystem.DetectNests(state);
        Assert.Single(state.Nests);
        Assert.False(state.Nests[0].IsTearingDown);
    }
}
