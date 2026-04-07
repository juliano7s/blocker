using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// A* pathfinding on the grid. Orthogonal movement only (4-directional).
/// Returns the next step toward a target, or null if no path exists.
/// </summary>
public static class PathfindingSystem
{
    private const int MaxSearchNodes = 500;

    // Per-thread reusable collections — zero allocations after first call per thread.
    // Production is single-threaded (one sim); [ThreadStatic] handles xUnit parallelism.
    [ThreadStatic]
    private static PriorityQueue<GridPos, int>? _openSet;
    [ThreadStatic]
    private static Dictionary<GridPos, GridPos>? _cameFrom;
    [ThreadStatic]
    private static Dictionary<GridPos, int>? _gScore;

    /// <summary>
    /// Find the next cell to step to when moving from <paramref name="from"/> toward <paramref name="target"/>.
    /// Returns null if no path exists. The returned cell is guaranteed to be passable and unoccupied
    /// (or is the target itself).
    /// </summary>
    public static GridPos? GetNextStep(GameState state, GridPos from, GridPos target)
    {
        if (from == target) return null;

        // If target is orthogonally adjacent and reachable, go directly
        if (from.ManhattanDistance(target) == 1 && CanMoveTo(state, target))
            return target;

        // A* search (orthogonal only) — reuse per-thread collections
        (_openSet ??= new PriorityQueue<GridPos, int>()).Clear();
        (_cameFrom ??= new Dictionary<GridPos, GridPos>()).Clear();
        (_gScore ??= new Dictionary<GridPos, int>()).Clear();

        _gScore[from] = 0;
        _openSet.Enqueue(from, Heuristic(from, target));

        int nodesExpanded = 0;

        while (_openSet.Count > 0 && nodesExpanded < MaxSearchNodes)
        {
            var current = _openSet.Dequeue();
            nodesExpanded++;

            if (current == target)
                return ReconstructFirstStep(current, from);

            int currentG = _gScore[current];

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = current + offset;

                if (!state.Grid.InBounds(neighbor)) continue;

                // Allow moving to target even if occupied (attack move)
                if (neighbor != target && !CanMoveTo(state, neighbor)) continue;

                int tentativeG = currentG + 1;

                if (!_gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    _cameFrom[neighbor] = current;
                    _gScore[neighbor] = tentativeG;
                    int fScore = tentativeG + Heuristic(neighbor, target);
                    _openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        // No path found — fall back to greedy step (best effort)
        return GreedyStep(state, from, target);
    }

    private static GridPos? ReconstructFirstStep(GridPos current, GridPos start)
    {
        var step = current;
        while (_cameFrom!.TryGetValue(step, out var prev))
        {
            if (prev == start)
                return step;
            step = prev;
        }
        return null;
    }

    /// <summary>Manhattan distance heuristic (admissible for 4-directional movement).</summary>
    private static int Heuristic(GridPos a, GridPos b) => a.ManhattanDistance(b);

    private static bool CanMoveTo(GameState state, GridPos pos)
    {
        if (!state.Grid.InBounds(pos)) return false;
        var cell = state.Grid[pos];
        return cell.IsPassable && !cell.BlockId.HasValue;
    }

    /// <summary>Greedy fallback: try axis-aligned steps toward target.</summary>
    private static GridPos? GreedyStep(GameState state, GridPos from, GridPos target)
    {
        int dx = Math.Sign(target.X - from.X);
        int dy = Math.Sign(target.Y - from.Y);
        int adx = Math.Abs(target.X - from.X);
        int ady = Math.Abs(target.Y - from.Y);

        // Try primary axis first, then secondary — no allocation needed
        if (adx >= ady)
        {
            if (dx != 0 && CanMoveTo(state, new GridPos(from.X + dx, from.Y)))
                return new GridPos(from.X + dx, from.Y);
            if (dy != 0 && CanMoveTo(state, new GridPos(from.X, from.Y + dy)))
                return new GridPos(from.X, from.Y + dy);
        }
        else
        {
            if (dy != 0 && CanMoveTo(state, new GridPos(from.X, from.Y + dy)))
                return new GridPos(from.X, from.Y + dy);
            if (dx != 0 && CanMoveTo(state, new GridPos(from.X + dx, from.Y)))
                return new GridPos(from.X + dx, from.Y);
        }

        return null;
    }
}
