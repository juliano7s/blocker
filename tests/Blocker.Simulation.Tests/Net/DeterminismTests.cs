using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;
using Blocker.Simulation.Systems;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class DeterminismTests
{
    private static GameState CreateStandardState()
    {
        var grid = new Grid(20, 20);
        var state = new GameState(grid);
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });

        // Add some units
        state.AddBlock(BlockType.Builder, 0, new GridPos(2, 2));
        state.AddBlock(BlockType.Builder, 0, new GridPos(3, 2));
        state.AddBlock(BlockType.Builder, 1, new GridPos(17, 17));
        state.AddBlock(BlockType.Builder, 1, new GridPos(16, 17));
        
        return state;
    }

    [Fact]
    public void Consecutive_Games_Have_Identical_Initial_Hashes()
    {
        // This test would have failed before the fix because IDs would keep incrementing.
        var state1 = CreateStandardState();
        var hash1 = StateHasher.Hash(state1);

        var state2 = CreateStandardState();
        var hash2 = StateHasher.Hash(state2);

        Assert.Equal(hash1, hash2);
        
        // Also verify specific block IDs start at 1 for both
        Assert.Equal(1, state1.Blocks[0].Id);
        Assert.Equal(1, state2.Blocks[0].Id);
    }

    [Fact]
    public void Simulation_Is_Deterministic_Over_Multiple_Ticks()
    {
        var stateA = CreateStandardState();
        var stateB = CreateStandardState();

        // 1. Initial match
        Assert.Equal(StateHasher.Hash(stateA), StateHasher.Hash(stateB));

        // 2. Generate some identical "random-ish" commands
        var commands = new List<Command>();
        for (int i = 0; i < 10; i++)
        {
            commands.Add(new Command(0, CommandType.Move, new List<int> { 1 }, new GridPos(5, 5)));
            commands.Add(new Command(1, CommandType.Move, new List<int> { 3 }, new GridPos(15, 15)));
        }

        // 3. Run for many ticks
        for (int i = 0; i < 100; i++)
        {
            stateA.Tick(commands);
            stateB.Tick(commands);
            
            // Should be identical EVERY tick
            Assert.Equal(StateHasher.Hash(stateA), StateHasher.Hash(stateB));
        }
    }

    [Fact]
    public void Tower_Iteration_Is_Deterministic()
    {
        // This test specifically checks that TowerSystem doesn't suffer from Dictionary iteration order issues.
        var stateA = CreateStandardState();
        var stateB = CreateStandardState();

        // 1. Create a Stunner in both
        stateA.AddBlock(BlockType.Stunner, 0, new GridPos(10, 10));
        stateB.AddBlock(BlockType.Stunner, 0, new GridPos(10, 10));
        var stunnerA = stateA.GetBlockAt(new GridPos(10, 10))!;
        var stunnerB = stateB.GetBlockAt(new GridPos(10, 10))!;
        stunnerA.State = BlockState.Rooted;
        stunnerB.State = BlockState.Rooted;

        // 2. Add arms in order: Up, Right, Down, Left in BOTH so they get same IDs.
        //    The non-determinism we want to test is the ITERATION order of these arms.
        var dirs = new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left };
        foreach (var d in dirs)
        {
            stateA.AddBlock(BlockType.Builder, 0, stunnerA.Pos + d.ToOffset());
            stateB.AddBlock(BlockType.Builder, 0, stunnerB.Pos + d.ToOffset());
        }
        
        // Mark all as rooted
        foreach (var b in stateA.Blocks) b.State = BlockState.Rooted;
        foreach (var b in stateB.Blocks) b.State = BlockState.Rooted;

        // 3. Form towers
        TowerSystem.CreateTower(stateA, stunnerA);
        TowerSystem.CreateTower(stateB, stunnerB);

        // 4. Trigger firing cycle (add enemy)
        stateA.AddBlock(BlockType.Soldier, 1, new GridPos(10, 5));
        stateB.AddBlock(BlockType.Soldier, 1, new GridPos(10, 5));

        // 5. Run several ticks
        for (int i = 0; i < 20; i++)
        {
            stateA.Tick();
            stateB.Tick();

            var hashA = StateHasher.Hash(stateA);
            var hashB = StateHasher.Hash(stateB);

            if (hashA != hashB)
            {
                throw new Exception($"Desync at tick {i}. HashA: {hashA}, HashB: {hashB}.");
            }
        }
    }

    [Fact]
    public void Simulation_Is_Isolated_From_Previous_Runs()
    {
        // Run one game to "dirty" any potential global state
        var stateDirty = CreateStandardState();
        for (int i = 0; i < 50; i++) stateDirty.Tick();

        // Start a fresh game
        var stateFresh = CreateStandardState();
        var hashFresh = StateHasher.Hash(stateFresh);

        // This hash should be exactly the same as a hash from a completely "clean" run
        // (We can compare it against another fresh one created immediately after)
        var stateControl = CreateStandardState();
        var hashControl = StateHasher.Hash(stateControl);

        Assert.Equal(hashControl, hashFresh);
    }
}
