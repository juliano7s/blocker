using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Manages tower creation, arm management, and firing.
/// Game bible Sections 7.2 (Stun Tower), 7.3 (Soldier Tower).
/// </summary>
public static class TowerSystem
{
    /// <summary>
    /// Create a tower from a fully rooted Stunner or Soldier (T key).
    /// </summary>
    public static bool CreateTower(GameState state, Block center)
    {
        if (!center.IsFullyRooted) return false;
        if (center.IsInFormation) return false;

        var towerType = center.Type switch
        {
            BlockType.Stunner => TowerType.Stun,
            BlockType.Soldier => TowerType.Soldier,
            _ => (TowerType?)null
        };
        if (towerType == null) return false;

        // Find first adjacent rooted Builder (same owner, not in formation)
        Block? firstArm = null;
        Direction? armDir = null;

        // Sweep order: Up, Right, Down, Left
        foreach (var dir in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
        {
            var pos = center.Pos + dir.ToOffset();
            var block = state.GetBlockAt(pos);
            if (block != null && block.Type == BlockType.Builder
                && block.PlayerId == center.PlayerId
                && block.IsFullyRooted && !block.IsInFormation)
            {
                firstArm = block;
                armDir = dir;
                break;
            }
        }

        if (firstArm == null) return false;

        var tower = new Tower
        {
            Type = towerType.Value,
            PlayerId = center.PlayerId,
            CenterId = center.Id,
        };

        tower.BuilderDirections[firstArm.Id] = armDir!.Value;
        center.FormationId = tower.Id;
        firstArm.FormationId = tower.Id;

        state.Towers.Add(tower);
        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.FormationFormed, center.Pos, center.PlayerId));

        return true;
    }

    /// <summary>
    /// Validate towers, update arms, fire when ready. Called during tick steps 3-4.
    /// </summary>
    public static void Tick(GameState state)
    {
        var toRemove = new List<int>();

        foreach (var tower in state.Towers)
        {
            var center = state.GetBlock(tower.CenterId);

            // Center lost → dissolve
            if (center == null || center.PlayerId != tower.PlayerId)
            {
                DissolveTower(state, tower, toRemove);
                continue;
            }

            // Check teardown
            if (tower.IsTearingDown)
            {
                tower.TeardownTimer--;
                if (tower.TeardownTimer <= 0)
                {
                    DissolveTower(state, tower, toRemove);
                    continue;
                }
            }

            // Check if center is uprooting → start teardown
            if (center.State == BlockState.Uprooting && !tower.IsTearingDown)
                tower.TeardownTimer = Constants.TeardownTicks;

            // Update builder arms: remove dead/uprooted ones, add new adjacent ones
            UpdateArms(state, tower, center);

            // No builders left → dissolve
            if (tower.BuilderDirections.Count == 0)
            {
                DissolveTower(state, tower, toRemove);
                continue;
            }

            if (tower.IsTearingDown) continue; // Don't fire during teardown

            // Fire logic
            tower.FireTimer++;

            if (tower.Type == TowerType.Stun)
                TickStunTower(state, tower, center);
            else
                TickSoldierTower(state, tower, center);
        }

        state.Towers.RemoveAll(t => toRemove.Contains(t.Id));
    }

    private static void UpdateArms(GameState state, Tower tower, Block center)
    {
        // Remove arms that are no longer valid
        var deadArms = new List<int>();
        foreach (var (builderId, _) in tower.BuilderDirections)
        {
            var builder = state.GetBlock(builderId);
            if (builder == null || builder.PlayerId != tower.PlayerId
                || !builder.IsFullyRooted || builder.State == BlockState.Uprooting)
            {
                deadArms.Add(builderId);
            }
        }

        foreach (var id in deadArms)
        {
            tower.BuilderDirections.Remove(id);
            var block = state.GetBlock(id);
            if (block != null && block.FormationId == tower.Id)
                block.FormationId = null;
        }

        // Check for new adjacent rooted builders that can join
        foreach (var dir in new[] { Direction.Up, Direction.Right, Direction.Down, Direction.Left })
        {
            var pos = center.Pos + dir.ToOffset();
            var block = state.GetBlockAt(pos);
            if (block == null) continue;
            if (block.Type != BlockType.Builder) continue;
            if (block.PlayerId != tower.PlayerId) continue;
            if (!block.IsFullyRooted || block.IsInFormation) continue;

            // New arm joins
            tower.BuilderDirections[block.Id] = dir;
            block.FormationId = tower.Id;
        }
    }

    /// <summary>
    /// Stun Tower: sweep cycle through builder directions, fire 3 parallel stun rays.
    /// </summary>
    private static void TickStunTower(GameState state, Tower tower, Block center)
    {
        var directions = tower.BuilderDirections.Values.Distinct().ToList();
        if (directions.Count == 0) return;

        if (!tower.IsFiring)
        {
            // Scan for enemies in any builder direction
            if (ScanForEnemies(state, center, directions, tower.Range))
            {
                tower.IsFiring = true;
                tower.SweepIndex = 0;
                tower.FireTimer = tower.FireInterval; // Fire immediately
            }
            return;
        }

        // Firing cycle: fire at current sweep direction when timer is ready
        if (tower.FireTimer < tower.FireInterval) return;

        tower.FireTimer = 0;
        var dir = directions[tower.SweepIndex % directions.Count];

        FireTowerRays(state, center, dir, RayType.Stun, tower.Range, Constants.StunTowerRayAdvanceInterval);

        // Advance sweep
        tower.SweepIndex++;
        if (tower.SweepIndex >= directions.Count)
        {
            // Cycle complete — check if enemies remain
            tower.IsFiring = false;
            tower.SweepIndex = 0;
        }
    }

    /// <summary>
    /// Soldier Tower: when enemy detected, fire blast rays in ALL builder directions simultaneously.
    /// </summary>
    private static void TickSoldierTower(GameState state, Tower tower, Block center)
    {
        var directions = tower.BuilderDirections.Values.Distinct().ToList();
        if (directions.Count == 0) return;
        if (tower.FireTimer < tower.FireInterval) return;

        if (!ScanForEnemies(state, center, directions, tower.Range)) return;

        tower.FireTimer = 0;

        // Fire in all directions simultaneously
        foreach (var dir in directions)
            FireTowerRays(state, center, dir, RayType.Blast, tower.Range, Constants.BlastTowerRayAdvanceInterval);

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.TowerFired, center.Pos, center.PlayerId, BlockId: center.Id));
    }

    /// <summary>
    /// Fire 3 parallel rays (center + 2 perpendicular) from the tower center.
    /// </summary>
    private static void FireTowerRays(GameState state, Block center, Direction dir,
        RayType rayType, int range, int advanceInterval)
    {
        var dirOffset = dir.ToOffset();
        var perps = GetPerpendicularOffsets(dir);

        var origins = new[] { center.Pos, center.Pos + perps.left, center.Pos + perps.right };

        foreach (var origin in origins)
        {
            var startPos = origin + dirOffset;
            if (!state.Grid.InBounds(startPos)) continue;

            var ray = new Ray
            {
                Type = rayType,
                PlayerId = center.PlayerId,
                Origin = origin,
                Direction = dir,
                HeadPos = startPos,
                Distance = 1,
                Range = range,
                AdvanceInterval = advanceInterval,
                FadeTicks = Constants.StunRayFade
            };
            state.Rays.Add(ray);
        }

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.TowerFired, center.Pos, center.PlayerId,
            Direction: dir, Range: range, BlockId: center.Id));
    }

    /// <summary>
    /// Check if any enemy block exists in line of sight along any of the given directions.
    /// </summary>
    private static bool ScanForEnemies(GameState state, Block center, List<Direction> directions, int range)
    {
        foreach (var dir in directions)
        {
            var offset = dir.ToOffset();
            var pos = center.Pos;

            for (int i = 0; i < range; i++)
            {
                pos = pos + offset;
                if (!state.Grid.InBounds(pos)) break;
                if (!state.Grid[pos].IsPassable) break; // Blocked by terrain/walls

                var block = state.GetBlockAt(pos);
                if (block == null) continue;

                if (block.PlayerId != center.PlayerId)
                    return true; // Enemy found

                // Friendly block doesn't block LoS for scanning
            }
        }

        return false;
    }

    private static void DissolveTower(GameState state, Tower tower, List<int> toRemove)
    {
        var center = state.GetBlock(tower.CenterId);
        if (center != null && center.FormationId == tower.Id)
            center.FormationId = null;

        foreach (var (builderId, _) in tower.BuilderDirections)
        {
            var builder = state.GetBlock(builderId);
            if (builder != null && builder.FormationId == tower.Id)
                builder.FormationId = null;
        }

        toRemove.Add(tower.Id);

        var pos = center?.Pos ?? new GridPos(0, 0);
        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.FormationDissolved, pos, tower.PlayerId));
    }

    private static (GridPos left, GridPos right) GetPerpendicularOffsets(Direction dir) => dir switch
    {
        Direction.Up => (new GridPos(-1, 0), new GridPos(1, 0)),
        Direction.Down => (new GridPos(-1, 0), new GridPos(1, 0)),
        Direction.Left => (new GridPos(0, -1), new GridPos(0, 1)),
        Direction.Right => (new GridPos(0, -1), new GridPos(0, 1)),
        _ => (new GridPos(0, 0), new GridPos(0, 0))
    };
}
