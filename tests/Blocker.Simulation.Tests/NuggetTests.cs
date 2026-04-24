using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class NuggetTests
{
    public NuggetTests()
    {
        Constants.Reset();
    }

    private GameState CreateState(int width = 20, int height = 20)
    {
        var state = new GameState(new Grid(width, height));
        state.Players.Add(new Player { Id = 0, TeamId = 0, MaxPopulation = 50 });
        state.Players.Add(new Player { Id = 1, TeamId = 1, MaxPopulation = 50 });
        return state;
    }

    [Fact]
    public void AddNugget_CreatesUnminedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));

        Assert.Equal(BlockType.Nugget, nugget.Type);
        Assert.Equal(-1, nugget.PlayerId);
        Assert.NotNull(nugget.NuggetState);
        Assert.False(nugget.NuggetState!.IsMined);
        Assert.Equal(0, nugget.NuggetState.MiningProgress);
        Assert.True(nugget.IsImmobile);
    }

    [Fact]
    public void MinedNugget_IsMobile()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState = new NuggetState { IsMined = true };

        Assert.False(nugget.IsImmobile);
    }

    [Fact]
    public void Nugget_HasZeroPopCost()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));

        Assert.Equal(0, nugget.PopCost);
    }

    // --- Part A: CombatSystem ---

    [Fact]
    public void CombatSystem_SkipsNuggets_NotKilledBySurrounding()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(6, 5));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 6));

        CombatSystem.Tick(state);

        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    // --- Part B: EliminationSystem ---

    [Fact]
    public void EliminationSystem_IgnoresNuggets()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        EliminationSystem.Tick(state);

        Assert.True(state.Players[0].IsEliminated);
    }

    // --- Part C: StunSystem ---

    [Fact]
    public void StunRay_StopsAtNugget_DoesNotDestroy()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.NotNull(state.GetBlock(nugget.Id));
        Assert.Equal(0, nugget.StunTimer);
    }

    [Fact]
    public void StunRay_DecrementsFortifiedWallHp()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
        wall.FortifiedHp = 3;

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.NotNull(state.GetBlock(wall.Id));
        Assert.Equal(2, wall.FortifiedHp);
    }

    [Fact]
    public void StunRay_DestroysWallWhenFortificationDepleted()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(7, 5));
        wall.FortifiedHp = 0;

        var stunner = state.AddBlock(BlockType.Stunner, 1, new GridPos(5, 5));
        StunSystem.FireStunRay(state, stunner, Direction.Right);

        for (int i = 0; i < 20; i++)
            StunSystem.Tick(state);

        Assert.Null(state.GetBlock(wall.Id));
    }

    // --- Part D: JumperSystem ---

    [Fact]
    public void Jumper_StopsAtUnminedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(7, 5));
        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Equal(new GridPos(6, 5), jumper.Pos);
        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    [Fact]
    public void Jumper_DestroysMinedNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 1, new GridPos(7, 5));
        nugget.NuggetState!.IsMined = true;

        var jumper = state.AddBlock(BlockType.Jumper, 0, new GridPos(5, 5));

        JumperSystem.Jump(state, jumper, Direction.Right);

        Assert.Null(state.GetBlock(nugget.Id));
    }

    // --- Part E: NuggetSystem — Mining ---

    [Fact]
    public void Mining_OneBuilder_AdvancesProgress()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        builder.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0; // Mining has started

        NuggetSystem.Tick(state);

        Assert.Equal(1, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_TwoBuilders_AdvancesFaster()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var b1 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        var b2 = state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5));
        b1.MiningTargetId = nugget.Id;
        b2.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0;

        NuggetSystem.Tick(state);

        Assert.Equal(2, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_NonAdjacentBuilder_DoesNotCount()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 3)); // 2 cells away
        builder.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0;

        NuggetSystem.Tick(state);

        Assert.Equal(0, nugget.NuggetState!.MiningProgress);
    }

    [Fact]
    public void Mining_CompletesAndFreesNugget()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        nugget.NuggetState!.MiningProgress = Constants.NuggetMiningTicks - 1;
        nugget.PlayerId = 0;

        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        builder.MiningTargetId = nugget.Id;

        NuggetSystem.Tick(state);

        Assert.True(nugget.NuggetState.IsMined);
        Assert.Equal(0, nugget.PlayerId);
        Assert.False(nugget.IsImmobile);
    }

    [Fact]
    public void Mining_ExclusiveToOneTeam()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        nugget.PlayerId = 0; // Team 0 started mining

        var b0 = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        b0.MiningTargetId = nugget.Id;

        var b1 = state.AddBlock(BlockType.Builder, 1, new GridPos(6, 5));
        b1.MiningTargetId = nugget.Id;

        NuggetSystem.Tick(state);

        Assert.Equal(1, nugget.NuggetState!.MiningProgress);
    }

    // --- Part F: NuggetSystem — Capture ---

    [Fact]
    public void Capture_EnemyBuilderAdjacent_FlipsOwnership()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));

        NuggetSystem.Tick(state);

        Assert.Equal(1, nugget.PlayerId);
    }

    [Fact]
    public void Capture_Contested_DoesNotFlip()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        state.AddBlock(BlockType.Builder, 1, new GridPos(5, 4));
        state.AddBlock(BlockType.Builder, 0, new GridPos(6, 5));

        NuggetSystem.Tick(state);

        Assert.Equal(0, nugget.PlayerId);
    }

    [Fact]
    public void Capture_NonBuilderAdjacent_DoesNotCapture()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 5));
        nugget.NuggetState!.IsMined = true;

        state.AddBlock(BlockType.Soldier, 1, new GridPos(5, 4));

        NuggetSystem.Tick(state);

        Assert.Equal(0, nugget.PlayerId);
    }

    // --- Part J: Commands ---

    [Fact]
    public void MineCommand_BuilderMovesToNuggetAndMines()
    {
        var state = CreateState();
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));
        var builder = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4)); // Already adjacent

        var cmd = new Command(0, CommandType.MineNugget, [builder.Id], TargetPos: new GridPos(5, 5));
        state.ProcessCommands([cmd]);

        Assert.Equal(nugget.Id, builder.MiningTargetId);
        Assert.Equal(0, nugget.PlayerId);
    }

    // --- Helpers ---

    private Block AddRootedBlock(GameState state, BlockType type, int playerId, GridPos pos)
    {
        var block = state.AddBlock(type, playerId, pos);
        block.State = BlockState.Rooted;
        block.RootProgress = Constants.RootTicks;
        return block;
    }

    private Nest SetupBuilderNest(GameState state, int playerId, GridPos center)
    {
        state.Grid[center].Ground = GroundType.Boot;
        AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(0, -1));
        AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(1, 0));
        AddRootedBlock(state, BlockType.Builder, playerId, center + new GridPos(0, 1));
        NestSystem.DetectNests(state);
        return state.Nests[0];
    }

    // --- Part G: NuggetSystem — Nest Refine ---

    [Fact]
    public void NestRefine_NuggetWithinRadius_Consumed()
    {
        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(12, 10));
        nugget.NuggetState!.IsMined = true;

        NuggetSystem.Tick(state);

        Assert.Null(state.GetBlock(nugget.Id));
    }

    [Fact]
    public void NestRefine_NuggetOutsideRadius_NotConsumed()
    {
        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(14, 10));
        nugget.NuggetState!.IsMined = true;

        NuggetSystem.Tick(state);

        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    [Fact]
    public void NestRefine_AppliesSpawnBonus()
    {
        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));
        nest.SpawnProgress = 0;

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(12, 10));
        nugget.NuggetState!.IsMined = true;

        NuggetSystem.Tick(state);

        Assert.True(nest.SpawnProgress > 0);
    }

    // --- Part H: Heal ---

    [Fact]
    public void Heal_NuggetAdjacentToTarget_HealsToFull()
    {
        var state = CreateState();
        var soldier = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
        soldier.Hp = 1;

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
        nugget.NuggetState!.IsMined = true;
        nugget.NuggetState.HealTargetId = soldier.Id;

        NuggetSystem.Tick(state);

        Assert.Equal(Constants.SoldierMaxHp, soldier.Hp);
        Assert.Null(state.GetBlock(nugget.Id));
    }

    [Fact]
    public void Heal_NuggetNotAdjacent_DoesNotHeal()
    {
        var state = CreateState();
        var soldier = state.AddBlock(BlockType.Soldier, 0, new GridPos(5, 5));
        soldier.Hp = 1;

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 3));
        nugget.NuggetState!.IsMined = true;
        nugget.NuggetState.HealTargetId = soldier.Id;

        NuggetSystem.Tick(state);

        Assert.Equal(1, soldier.Hp);
        Assert.NotNull(state.GetBlock(nugget.Id));
    }

    // --- Part I: Fortify ---

    [Fact]
    public void Fortify_NuggetAdjacentToWall_FortifiesFiveWalls()
    {
        var state = CreateState();
        for (int x = 3; x <= 8; x++)
            state.AddBlock(BlockType.Wall, 0, new GridPos(x, 5));

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
        nugget.NuggetState!.IsMined = true;
        nugget.NuggetState.FortifyTargetPos = new GridPos(5, 5);

        NuggetSystem.Tick(state);

        Assert.Null(state.GetBlock(nugget.Id));

        int fortifiedCount = state.Blocks.Count(b => b.Type == BlockType.Wall && b.FortifiedHp > 0);
        Assert.Equal(5, fortifiedCount);
    }

    [Fact]
    public void Fortify_SetsCorrectHp()
    {
        var state = CreateState();
        var wall = state.AddBlock(BlockType.Wall, 0, new GridPos(5, 5));

        var nugget = state.AddBlock(BlockType.Nugget, 0, new GridPos(5, 4));
        nugget.NuggetState!.IsMined = true;
        nugget.NuggetState.FortifyTargetPos = new GridPos(5, 5);

        NuggetSystem.Tick(state);

        Assert.Equal(Constants.FortifiedWallHp, wall.FortifiedHp);
    }

    // --- Part K: NestSystem — Nugget Required ---

    [Fact]
    public void NestSystem_NuggetRequired_BlocksSpawnWithoutNugget()
    {
        Constants.Initialize(new SimulationConfig
        {
            Nugget = new NuggetConfig { BuilderRequired = true }
        });

        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

        var ground = state.Grid[nest.Center].Ground;
        nest.SpawnProgress = nest.GetSpawnTicks(ground) - 1;

        NestSystem.TickSpawning(state);

        int freeBuilders = state.Blocks.Count(b => b.Type == BlockType.Builder && b.PlayerId == 0 && !b.IsInFormation);
        Assert.Equal(0, freeBuilders);
        Assert.Equal(nest.GetSpawnTicks(ground), nest.SpawnProgress);
    }

    [Fact]
    public void NestSystem_NuggetRequired_SpawnsWithNuggetLoaded()
    {
        Constants.Initialize(new SimulationConfig
        {
            Nugget = new NuggetConfig { BuilderRequired = true }
        });

        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));
        nest.NuggetLoaded = true;

        var ground = state.Grid[nest.Center].Ground;
        nest.SpawnProgress = nest.GetSpawnTicks(ground) - 1;

        NestSystem.TickSpawning(state);

        int freeBuilders = state.Blocks.Count(b => b.Type == BlockType.Builder && b.PlayerId == 0 && !b.IsInFormation);
        Assert.Equal(1, freeBuilders);
        Assert.False(nest.NuggetLoaded);
    }

    // --- Part L: Full Lifecycle ---

    [Fact]
    public void FullLifecycle_MineToRefine()
    {
        var state = CreateState();
        var nest = SetupBuilderNest(state, 0, new GridPos(10, 10));

        // Place nugget adjacent to a miner builder
        var nugget = state.AddBlock(BlockType.Nugget, -1, new GridPos(5, 5));

        // Place builder adjacent and assign mining
        var miner = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 4));
        miner.MiningTargetId = nugget.Id;
        nugget.PlayerId = 0;

        // Set progress to 1 tick away from completion
        nugget.NuggetState!.MiningProgress = Constants.NuggetMiningTicks - 1;

        // Tick to complete mining
        NuggetSystem.Tick(state);

        Assert.True(nugget.NuggetState.IsMined);
        Assert.Null(miner.MiningTargetId);
        // Auto-rally should have set a move target toward the nest
        Assert.True(nugget.MoveTarget.HasValue);

        // Manually place nugget within refine radius of nest
        state.Grid[nugget.Pos].BlockId = null;
        nugget.Pos = new GridPos(10, 12); // Chebyshev distance 2 from nest center (10,10)
        state.Grid[nugget.Pos].BlockId = nugget.Id;
        nugget.MoveTarget = null; // Arrived

        // Tick again — nugget should be consumed by nest refine
        int progressBefore = nest.SpawnProgress;
        NuggetSystem.Tick(state);

        Assert.Null(state.GetBlock(nugget.Id));
        Assert.True(nest.SpawnProgress > progressBefore);
    }
}
