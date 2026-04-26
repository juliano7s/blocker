using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class PathfindingSystem
{
    private const int MaxSearchNodes = 500;

    [ThreadStatic]
    private static PriorityQueue<GridPos, int>? _openSet;
    [ThreadStatic]
    private static Dictionary<GridPos, GridPos>? _cameFrom;
    [ThreadStatic]
    private static Dictionary<GridPos, int>? _gScore;

    public static GridPos? GetNextStep(GameState state, GridPos from, GridPos target)
        => GetNextStep(state, from, target, exploredMap: null);

    public static GridPos? GetNextStep(GameState state, GridPos from, GridPos target, VisibilityMap? exploredMap)
    {
        if (from == target) return null;

        if (from.ManhattanDistance(target) == 1 && CanMoveTo(state, target, exploredMap))
            return target;

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

                if (neighbor != target && !CanMoveTo(state, neighbor, exploredMap)) continue;

                int tentativeG = currentG + 1;

                if (!_gScore.TryGetValue(neighbor, out int existingG) || tentativeG < existingG)
                {
                    _cameFrom[neighbor] = current;
                    _gScore[neighbor] = tentativeG;
                    int dx1 = neighbor.X - target.X;
                    int dy1 = neighbor.Y - target.Y;
                    int dx2 = from.X - target.X;
                    int dy2 = from.Y - target.Y;
                    int cross = Math.Abs(dx1 * dy2 - dx2 * dy1);
                    int fScore = (tentativeG + Heuristic(neighbor, target)) * 1000 + cross;
                    _openSet.Enqueue(neighbor, fScore);
                }
            }
        }

        // No explored path found — greedy step ignores explored constraint
        // (the unit "guesses" toward the target in the fog)
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

    private static int Heuristic(GridPos a, GridPos b) => a.ManhattanDistance(b);

    private static bool CanMoveTo(GameState state, GridPos pos, VisibilityMap? exploredMap = null)
    {
        if (!state.Grid.InBounds(pos)) return false;

        if (exploredMap != null)
        {
            if (exploredMap.IsExplored(pos))
            {
                var cell = state.Grid[pos];
                return cell.IsPassable && !cell.BlockId.HasValue;
            }
            // Optimistic navigation: assume unexplored cells are passable
            return true;
        }

        var c = state.Grid[pos];
        return c.IsPassable && !c.BlockId.HasValue;
    }

    private static GridPos? GreedyStep(GameState state, GridPos from, GridPos target)
    {
        int dx = Math.Sign(target.X - from.X);
        int dy = Math.Sign(target.Y - from.Y);
        int adx = Math.Abs(target.X - from.X);
        int ady = Math.Abs(target.Y - from.Y);

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