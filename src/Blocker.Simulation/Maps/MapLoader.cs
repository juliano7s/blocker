using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Maps;

public static class MapLoader
{
    /// <summary>
    /// Load a map from structured MapData with slot-to-player assignments.
    /// </summary>
    public static GameState Load(MapData data, List<SlotAssignment> assignments)
    {
        var grid = new Grid(data.Width, data.Height);
        var state = new GameState(grid);

        // Apply ground layer
        foreach (var entry in data.Ground)
        {
            if (grid.InBounds(entry.X, entry.Y))
                grid[entry.X, entry.Y].Ground = entry.Type;
        }

        // Apply terrain layer
        foreach (var entry in data.Terrain)
        {
            if (grid.InBounds(entry.X, entry.Y))
                grid[entry.X, entry.Y].Terrain = entry.Type;
        }

        // Build slot → player lookup
        var slotToPlayer = assignments.ToDictionary(a => a.SlotId, a => a.PlayerId);

        // Place units
        foreach (var entry in data.Units)
        {
            if (!grid.InBounds(entry.X, entry.Y)) continue;

            // Nuggets are neutral (SlotId -1) — they don't belong to any player slot
            int playerId;
            if (entry.Type == BlockType.Nugget)
                playerId = -1;
            else if (!slotToPlayer.TryGetValue(entry.SlotId, out playerId))
                continue;

            var block = state.AddBlock(entry.Type, playerId, new GridPos(entry.X, entry.Y));
            if (entry.Rooted && block.Type != BlockType.Wall)
            {
                block.State = BlockState.Rooted;
                block.RootProgress = Constants.GetRootTicks(block.Type);
            }
        }

        // Create players from assignments
        foreach (var assignment in assignments)
        {
            if (state.Players.All(p => p.Id != assignment.PlayerId))
                state.Players.Add(new Player { Id = assignment.PlayerId, TeamId = assignment.TeamId });
        }

        Blocker.Simulation.Systems.VisibilitySystem.Tick(state);
        return state;
    }
}
