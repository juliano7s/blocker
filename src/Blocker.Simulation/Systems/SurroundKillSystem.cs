using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class SurroundKillSystem
{
    public static void Tick(GameState state)
    {
        var toKill = new List<Block>();

        foreach (var block in state.Blocks)
        {
            if (!CanBeTrapped(block)) continue;
            if (!PassesPreFilter(state, block)) continue;

            int? trappedBy = null;
            bool isTrapped = IsEncircled(state, block, out trappedBy);

            if (isTrapped)
            {
                if (block.TrapTicks == 0)
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.SurroundTrapped, block.Pos, block.PlayerId, BlockId: block.Id));

                block.TrapTicks++;
                block.TrappedByPlayerId = trappedBy;

                if (block.TrapTicks >= Constants.SurroundKillDelay + 1)
                    toKill.Add(block);
            }
            else
            {
                block.TrapTicks = 0;
                block.TrappedByPlayerId = null;
            }
        }

        foreach (var block in toKill)
        {
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.SurroundKilled, block.Pos, block.TrappedByPlayerId, BlockId: block.Id));
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.BlockDied, block.Pos, block.PlayerId, BlockId: block.Id));
            state.RemoveBlock(block);
        }
    }

    private static bool CanBeTrapped(Block block)
    {
        if (block.Type == BlockType.Wall) return false;
        if (block.Type == BlockType.Nugget) return false;
        if (!block.IsMobile) return false;
        return true;
    }

    private static bool PassesPreFilter(GameState state, Block block)
    {
        int impassableCount = 0;
        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var pos = block.Pos + offset;
            if (!state.Grid.InBounds(pos))
            {
                // Map edge is passable (escape route) — so this neighbor is not impassable
                continue;
            }
            if (IsCellImpassable(state, pos, block.PlayerId))
                impassableCount++;
        }
        return impassableCount >= 2;
    }

    private static bool IsEncircled(GameState state, Block target, out int? trappedByPlayerId)
    {
        trappedByPlayerId = null;
        int maxArea = Constants.SurroundKillMaxArea;

        var visited = new HashSet<GridPos>();
        var queue = new Queue<GridPos>();
        queue.Enqueue(target.Pos);
        visited.Add(target.Pos);

        int? lastAttackerPlayer = null;

        while (queue.Count > 0)
        {
            if (visited.Count > maxArea)
            {
                return false;
            }

            var current = queue.Dequeue();

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = current + offset;

                if (!state.Grid.InBounds(neighbor))
                    return false; // Reached map edge — not encircled

                if (visited.Contains(neighbor))
                    continue;

                if (IsCellImpassable(state, neighbor, target.PlayerId))
                {
                    visited.Add(neighbor);
                    // Track attacker for attribution
                    var blocker = state.GetBlockAt(neighbor);
                    if (blocker != null && state.AreEnemies(target.PlayerId, blocker.PlayerId))
                        lastAttackerPlayer = blocker.PlayerId;
                    continue; // Wall of encirclement — don't expand through
                }

                visited.Add(neighbor);
                queue.Enqueue(neighbor);
            }
        }

        // Flood-fill completed without reaching edge or exceeding cap — encircled
        trappedByPlayerId = lastAttackerPlayer;
        return true;
    }

    private static bool IsCellImpassable(GameState state, GridPos pos, int victimPlayerId)
    {
        var cell = state.Grid[pos];

        // Neutral terrain is impassable
        if (cell.Terrain != TerrainType.None)
            return true;

        var block = state.GetBlockAt(pos);
        if (block == null)
            return false;

        // Nuggets are impassable
        if (block.Type == BlockType.Nugget)
            return true;

        // Enemy blocks (from attacker's team) are always impassable
        if (state.AreEnemies(victimPlayerId, block.PlayerId))
            return true;

        // Victim's own team: immobile blocks are impassable (used against them)
        if (!state.AreEnemies(victimPlayerId, block.PlayerId))
        {
            if (block.IsImmobile)
                return true;
        }

        return false;
    }
}
