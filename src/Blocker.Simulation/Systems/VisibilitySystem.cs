using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class VisibilitySystem
{
    public static void Tick(GameState state)
    {
        if (!Constants.FogOfWarEnabled) return;

        foreach (var player in state.Players)
        {
            int teamId = state.GetTeamFor(player.Id);
            if (!state.VisibilityMaps.ContainsKey(teamId))
                state.VisibilityMaps[teamId] = new VisibilityMap(state.Grid.Width, state.Grid.Height);
        }

        foreach (var vm in state.VisibilityMaps.Values)
            vm.ClearVisible();

        foreach (var block in state.Blocks)
        {
            if (block.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(block.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            int radius = Constants.GetLosRadius(block.Type);
            if (radius <= 0) continue;
            RevealFrom(state, block.Pos, radius, vm);
        }

        foreach (var nest in state.Nests)
        {
            if (nest.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(nest.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            RevealFrom(state, nest.Center, Constants.NestLosRadius, vm);
        }

        foreach (var tower in state.Towers)
        {
            if (tower.PlayerId < 0) continue;
            int teamId = state.GetTeamFor(tower.PlayerId);
            if (!state.VisibilityMaps.TryGetValue(teamId, out var vm)) continue;
            var center = state.GetBlock(tower.CenterId);
            if (center == null) continue;
            RevealFrom(state, center.Pos, Constants.TowerLosRadius, vm);
        }
    }

    private static void RevealFrom(GameState state, GridPos origin, int radius, VisibilityMap vm)
    {
        var grid = state.Grid;
        int ox = origin.X, oy = origin.Y;

        if (grid.InBounds(ox, oy))
            vm.SetVisible(ox, oy);

        int minX = Math.Max(0, ox - radius);
        int maxX = Math.Min(grid.Width - 1, ox + radius);
        int minY = Math.Max(0, oy - radius);
        int maxY = Math.Min(grid.Height - 1, oy + radius);

        for (int y = minY; y <= maxY; y++)
        {
            for (int x = minX; x <= maxX; x++)
            {
                if (x == ox && y == oy) continue;
                if (HasLineOfSight(state, ox, oy, x, y))
                    vm.SetVisible(x, y);
            }
        }
    }

    /// <summary>
    /// Bresenham line-of-sight from (x0,y0) to (x1,y1).
    /// Intermediate cells (not origin, not target) are checked for opacity.
    /// The target cell is revealed even when opaque — you see what blocks you.
    /// </summary>
    private static bool HasLineOfSight(GameState state, int x0, int y0, int x1, int y1)
    {
        int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
        int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;
        int cx = x0, cy = y0;

        while (true)
        {
            if (cx == x1 && cy == y1) return true;

            if ((cx != x0 || cy != y0) && IsOpaque(state, cx, cy))
                return false;

            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; cx += sx; }
            if (e2 <= dx) { err += dx; cy += sy; }
        }
    }

    private static bool IsOpaque(GameState state, int x, int y)
    {
        var cell = state.Grid[x, y];
        if (cell.Terrain != TerrainType.None) return true;
        if (cell.BlockId.HasValue)
        {
            var block = state.GetBlock(cell.BlockId.Value);
            if (block?.Type == BlockType.Wall) return true;
        }
        return false;
    }
}