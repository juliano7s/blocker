using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;
using Xunit;

namespace Blocker.Simulation.Tests;

public class FormationTests
{
    public FormationTests()
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

    private Block AddRootedWall(GameState state, int playerId, GridPos pos)
    {
        var block = state.AddBlock(BlockType.Wall, playerId, pos);
        block.State = BlockState.Rooted;
        block.RootProgress = Constants.RootTicks;
        return block;
    }

    // === Supply Formation Detection ===

    [Fact]
    public void Supply_LShapeWalls_FormsFormation()
    {
        var state = CreateState();
        // L-shape: corner at (5,5), right arm (6,5), down arm (5,6)
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        AddRootedWall(state, 0, new GridPos(5, 6));

        FormationSystem.DetectFormations(state);

        Assert.Single(state.Formations);
        Assert.Equal(FormationType.Supply, state.Formations[0].Type);
        Assert.Equal(0, state.Formations[0].PlayerId);
    }

    [Fact]
    public void Supply_StraightLine_NoFormation()
    {
        var state = CreateState();
        // Straight line — not an L
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        AddRootedWall(state, 0, new GridPos(7, 5));

        FormationSystem.DetectFormations(state);

        Assert.Empty(state.Formations);
    }

    [Fact]
    public void Supply_TwoWallsOnly_NoFormation()
    {
        var state = CreateState();
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));

        FormationSystem.DetectFormations(state);

        Assert.Empty(state.Formations);
    }

    [Fact]
    public void Supply_MixedOwnership_NoFormation()
    {
        var state = CreateState();
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        AddRootedWall(state, 1, new GridPos(5, 6)); // Different player

        FormationSystem.DetectFormations(state);

        Assert.Empty(state.Formations);
    }

    [Fact]
    public void Supply_BuilderNotWall_NoFormation()
    {
        var state = CreateState();
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        // A rooted builder can't substitute for a wall in supply formation
        var b = state.AddBlock(BlockType.Builder, 0, new GridPos(5, 6));
        b.State = BlockState.Rooted;
        b.RootProgress = Constants.RootTicks;

        FormationSystem.DetectFormations(state);

        Assert.Empty(state.Formations);
    }

    // === Population Cap ===

    [Fact]
    public void PopCap_NoSupply_ZeroCap()
    {
        var state = CreateState();

        FormationSystem.DetectFormations(state);

        Assert.Equal(0, state.Players[0].MaxPopulation);
    }

    [Fact]
    public void PopCap_OneSupply_SevenCap()
    {
        var state = CreateState();
        AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        AddRootedWall(state, 0, new GridPos(5, 6));

        FormationSystem.DetectFormations(state);

        Assert.Equal(Constants.SupplyPopCap, state.Players[0].MaxPopulation);
    }

    [Fact]
    public void PopCap_TwoSupplies_FourteenCap()
    {
        var state = CreateState();
        // Supply 1
        AddRootedWall(state, 0, new GridPos(2, 2));
        AddRootedWall(state, 0, new GridPos(3, 2));
        AddRootedWall(state, 0, new GridPos(2, 3));
        // Supply 2
        AddRootedWall(state, 0, new GridPos(7, 7));
        AddRootedWall(state, 0, new GridPos(8, 7));
        AddRootedWall(state, 0, new GridPos(7, 8));

        FormationSystem.DetectFormations(state);

        Assert.Equal(Constants.SupplyPopCap * 2, state.Players[0].MaxPopulation);
    }

    [Fact]
    public void PopCap_DissolvedWhenMemberKilled()
    {
        var state = CreateState();
        var w1 = AddRootedWall(state, 0, new GridPos(5, 5));
        AddRootedWall(state, 0, new GridPos(6, 5));
        AddRootedWall(state, 0, new GridPos(5, 6));

        FormationSystem.DetectFormations(state);
        Assert.Equal(Constants.SupplyPopCap, state.Players[0].MaxPopulation);

        // Kill one member
        state.RemoveBlock(w1);

        FormationSystem.DetectFormations(state);
        Assert.Equal(0, state.Players[0].MaxPopulation);
    }

    [Fact]
    public void Supply_MembersHaveFormationId()
    {
        var state = CreateState();
        var w1 = AddRootedWall(state, 0, new GridPos(5, 5));
        var w2 = AddRootedWall(state, 0, new GridPos(6, 5));
        var w3 = AddRootedWall(state, 0, new GridPos(5, 6));

        FormationSystem.DetectFormations(state);

        Assert.NotNull(w1.FormationId);
        Assert.Equal(w1.FormationId, w2.FormationId);
        Assert.Equal(w1.FormationId, w3.FormationId);
    }

    // === All 4 L-shape orientations ===

    [Theory]
    [InlineData(1, 0, 0, 1)]   // right + down
    [InlineData(1, 0, 0, -1)]  // right + up
    [InlineData(-1, 0, 0, 1)]  // left + down
    [InlineData(-1, 0, 0, -1)] // left + up
    public void Supply_AllLOrientations_Detected(int dx1, int dy1, int dx2, int dy2)
    {
        var state = CreateState();
        var corner = new GridPos(5, 5);
        AddRootedWall(state, 0, corner);
        AddRootedWall(state, 0, corner + new GridPos(dx1, dy1));
        AddRootedWall(state, 0, corner + new GridPos(dx2, dy2));

        FormationSystem.DetectFormations(state);

        Assert.Single(state.Formations);
    }
}
