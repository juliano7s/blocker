using Blocker.Simulation.Core;
using Xunit;

namespace Blocker.Simulation.Tests;

public class VisibilityTests
{
    [Fact]
    public void VisibilityMap_SetVisible_MarksExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);

        Assert.True(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_ClearVisible_PreservesExplored()
    {
        var vm = new VisibilityMap(10, 10);
        vm.SetVisible(3, 4);
        vm.ClearVisible();

        Assert.False(vm.IsVisible(3, 4));
        Assert.True(vm.IsExplored(3, 4));
    }

    [Fact]
    public void VisibilityMap_UnsetCells_NotVisible()
    {
        var vm = new VisibilityMap(10, 10);
        Assert.False(vm.IsVisible(0, 0));
        Assert.False(vm.IsExplored(0, 0));
    }

    // --- VisibilitySystem tests ---

    private static GameState MakeState(int width = 20, int height = 20)
    {
        Constants.Reset();
        return new GameState(new Grid(width, height));
    }

    private static void AddPlayer(GameState state, int playerId, int teamId)
    {
        state.Players.Add(new Player { Id = playerId, TeamId = teamId });
    }

    [Fact]
    public void VisibilitySystem_SingleBlock_RevealsRadius()
    {
        var state = MakeState();
        AddPlayer(state, 1, 1);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        Assert.True(state.VisibilityMaps.ContainsKey(1));
        var vm = state.VisibilityMaps[1];

        Assert.True(vm.IsVisible(5, 5));   // Origin
        Assert.True(vm.IsVisible(9, 5));   // 4 cells right — within radius 5
        Assert.True(vm.IsVisible(5, 9));   // 4 cells down
        Assert.True(vm.IsVisible(9, 9));   // 4 diagonal (Chebyshev)
        Assert.False(vm.IsVisible(11, 5)); // 6 cells right — outside radius 5
    }

    [Fact]
    public void VisibilitySystem_WallBlocksLoSBehindIt()
    {
        var state = MakeState();
        AddPlayer(state, 1, 1);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
        // Wall at (2,0) should block (3,0) but not (0,2)
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Wall, -1, new GridPos(2, 0));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        var vm = state.VisibilityMaps[1];
        Assert.True(vm.IsVisible(2, 0));   // Wall itself visible
        Assert.False(vm.IsVisible(3, 0)); // Behind wall — blocked
        Assert.True(vm.IsVisible(0, 2));   // Perpendicular — unaffected
    }

    [Fact]
    public void VisibilitySystem_TerrainBlocksLoS()
    {
        var state = MakeState();
        AddPlayer(state, 1, 1);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
        state.Grid[2, 0].Terrain = TerrainType.Terrain; // Terrain cell at (2,0)

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        var vm = state.VisibilityMaps[1];
        Assert.True(vm.IsVisible(2, 0));   // Terrain cell itself visible
        Assert.False(vm.IsVisible(3, 0)); // Behind terrain — blocked
    }

    [Fact]
    public void VisibilitySystem_TeamsShareVision()
    {
        var state = MakeState();
        AddPlayer(state, 1, 99);
        AddPlayer(state, 2, 99);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(19, 19));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        // Single map for team 99
        Assert.Single(state.VisibilityMaps);
        Assert.True(state.VisibilityMaps.ContainsKey(99));
        var vm = state.VisibilityMaps[99];
        Assert.True(vm.IsVisible(0, 0));
        Assert.True(vm.IsVisible(19, 19));
    }

    [Fact]
    public void VisibilitySystem_ExploredAccumulates_AfterUnitMoves()
    {
        var state = MakeState();
        AddPlayer(state, 1, 1);
        var block = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);
        var vm = state.VisibilityMaps[1];
        Assert.True(vm.IsExplored(4, 4));

        // Teleport block to far corner
        state.Grid[0, 0].BlockId = null;
        block.Pos = new GridPos(19, 19);
        state.Grid[19, 19].BlockId = block.Id;

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);
        Assert.True(vm.IsExplored(4, 4));   // Still explored from tick 1
        Assert.False(vm.IsVisible(4, 4));  // No longer visible
        Assert.True(vm.IsVisible(19, 19)); // New position visible
    }

    [Fact]
    public void VisibilitySystem_NestGrantsVision()
    {
        var state = MakeState();
        AddPlayer(state, 1, 1);
        state.Nests.Add(new Nest { Id = 1, PlayerId = 1, Center = new GridPos(10, 10), Type = NestType.Builder });

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        var vm = state.VisibilityMaps[1];
        Assert.True(vm.IsVisible(10, 10));
        Assert.True(vm.IsVisible(12, 10));  // 2 cells right — within radius 2
        Assert.False(vm.IsVisible(13, 10)); // 3 cells right — outside radius 2
    }

    [Fact]
    public void VisibilitySystem_FogOfWarDisabled_AllVisible()
    {
        var state = MakeState();
        Constants.Initialize(new SimulationConfig
        {
            Vision = new VisionConfig { FogOfWarEnabled = false }
        });
        AddPlayer(state, 1, 1);

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        // With FoW disabled, VisibilityMaps stays empty — renderer treats everything visible
        Assert.Empty(state.VisibilityMaps);

        Constants.Reset();
    }

    [Fact]
    public void VisibilitySystem_DiagonalCornerLoS()
    {
        // A wall at (1,1) should block LoS from (0,0) to (2,2) via Bresenham,
        // but should NOT block LoS from (0,0) to (2,0) or (0,2).
        var state = MakeState();
        AddPlayer(state, 1, 1);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Stunner, 1, new GridPos(0, 0)); // r=7
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Wall, -1, new GridPos(1, 1));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        var vm = state.VisibilityMaps[1];
        Assert.True(vm.IsVisible(1, 1));   // Wall itself visible
        Assert.False(vm.IsVisible(2, 2)); // Directly behind wall on diagonal — blocked
        Assert.True(vm.IsVisible(2, 0));   // Not blocked — different line
        Assert.True(vm.IsVisible(0, 2));   // Not blocked — different line
    }

    [Fact]
    public void VisibilitySystem_AsymmetricTeamVisibility()
    {
        // Team 1 and Team 2 have blocks far apart — each sees only their own area
        var state = MakeState();
        AddPlayer(state, 1, 1);
        AddPlayer(state, 2, 2);
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(19, 19));

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        var vm1 = state.VisibilityMaps[1];
        var vm2 = state.VisibilityMaps[2];

        // Team 1 sees origin, not far corner
        Assert.True(vm1.IsVisible(0, 0));
        Assert.False(vm1.IsVisible(19, 19));

        // Team 2 sees far corner, not origin
        Assert.True(vm2.IsVisible(19, 19));
        Assert.False(vm2.IsVisible(0, 0));
    }

    [Fact]
    public void StateHasher_DifferentExploredMaps_DifferentHash()
    {
        var state1 = MakeState();
        AddPlayer(state1, 1, 1);
        state1.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(0, 0));
        Blocker.Simulation.Systems.VisibilitySystem.Tick(state1);

        var state2 = MakeState();
        AddPlayer(state2, 1, 1);
        state2.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(19, 19));
        Blocker.Simulation.Systems.VisibilitySystem.Tick(state2);

        var hash1 = Blocker.Simulation.Net.StateHasher.Hash(state1);
        var hash2 = Blocker.Simulation.Net.StateHasher.Hash(state2);
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void StateHasher_SameExploredMaps_SameHash()
    {
        var state1 = MakeState();
        AddPlayer(state1, 1, 1);
        state1.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));
        Blocker.Simulation.Systems.VisibilitySystem.Tick(state1);

        var state2 = MakeState();
        AddPlayer(state2, 1, 1);
        state2.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(5, 5));
        Blocker.Simulation.Systems.VisibilitySystem.Tick(state2);

        var hash1 = Blocker.Simulation.Net.StateHasher.Hash(state1);
        var hash2 = Blocker.Simulation.Net.StateHasher.Hash(state2);
        Assert.Equal(hash1, hash2);
    }

    [Fact]
    public void TowerSystem_DoesNotFireAtFoggedEnemy()
    {
        Constants.Initialize(new SimulationConfig());
        var state = MakeState(30, 30);
        AddPlayer(state, 1, 1);
        AddPlayer(state, 2, 2);

        // Stun tower for player 1: center (Stunner) at (5,5), arm (Builder) at (6,5)
        var center = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Stunner, 1, new GridPos(5, 5));
        center.State = Blocker.Simulation.Blocks.BlockState.Rooted;
        center.RootProgress = Constants.RootTicks;
        var arm = state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 1, new GridPos(6, 5));
        arm.State = Blocker.Simulation.Blocks.BlockState.Rooted;
        arm.RootProgress = Constants.RootTicks;
        Blocker.Simulation.Systems.TowerSystem.CreateTower(state, center);

        // Enemy at (5, 25) — far beyond Stunner LoS radius 7, so out of visibility
        state.AddBlock(Blocker.Simulation.Blocks.BlockType.Builder, 2, new GridPos(5, 25));

        // Compute initial visibility (enemy is not visible to team 1)
        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);

        // Prime tower fire timer
        var tower = state.Towers.First();
        tower.FireTimer = Constants.StunTowerFireInterval + 1;

        state.VisualEvents.Clear();
        Blocker.Simulation.Systems.TowerSystem.Tick(state);

        Assert.DoesNotContain(state.VisualEvents, e =>
            e.Type == Blocker.Simulation.Core.VisualEventType.TowerFired);
    }
}