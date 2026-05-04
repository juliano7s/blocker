using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Manages stun rays: advancing ray heads, applying stun/kill effects, cooldown decay.
/// Game bible Section 4.4, tick steps 5.
/// </summary>
public static class StunSystem
{
    /// <summary>
    /// Fire a stun ray from a Stunner in a cardinal direction.
    /// Creates 3 parallel rays (center + 2 perpendicular side rays).
    /// </summary>
    public static bool FireStunRay(GameState state, Block stunner, Direction direction)
    {
        if (stunner.Type != BlockType.Stunner) return false;
        if (stunner.IsStunned) return false;
        if (stunner.IsOnCooldown) return false;
        if (!stunner.IsMobile) return false; // Must be uprooted to fire unit ray

        var dirOffset = direction.ToOffset();

        // 3 parallel rays: center, left perpendicular, right perpendicular
        // Side rays travel one cell less to form a > arrow shape
        var perpOffsets = GetPerpendicularOffsets(direction);

        var origins = new (GridPos pos, int range)[]
        {
            (stunner.Pos, Constants.StunRange),
            (stunner.Pos + perpOffsets.left, Constants.StunRange - 1),
            (stunner.Pos + perpOffsets.right, Constants.StunRange - 1),
        };

        for (int r = 0; r < origins.Length; r++)
        {
            var (origin, range) = origins[r];
            bool isSideRay = r > 0;

            // Center ray starts one cell ahead (stunner occupies origin).
            // Side rays start AT the perpendicular cell beside the stunner so their
            // head is one step behind the center, forming the > arrow shape.
            var headPos = isSideRay ? origin : origin + dirOffset;
            int startDist = isSideRay ? 0 : 1;

            if (!state.Grid.InBounds(headPos)) continue;

            var ray = new Ray
            {
                Id = state.NextRayId(),
                Type = RayType.Stun,
                PlayerId = stunner.PlayerId,
                Origin = origin,
                Direction = direction,
                HeadPos = headPos,
                Distance = startDist,
                Range = range,
                AdvanceInterval = Constants.StunUnitRayAdvanceInterval,
                FadeTicks = Constants.StunRayFade
            };

            state.Rays.Add(ray);
        }

        // Apply cooldown — stunner moves at 1/3 speed during cooldown
        stunner.Cooldown = Constants.StunCooldown;
        stunner.MobileCooldown = true;

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.StunRayFired, stunner.Pos, stunner.PlayerId,
            Direction: direction, Range: Constants.StunRange, BlockId: stunner.Id));

        return true;
    }

    /// <summary>
    /// Advance all active rays, apply effects on hit, decay cooldowns.
    /// Called during tick step 5.
    /// </summary>
    public static void Tick(GameState state)
    {
        // Advance rays
        foreach (var ray in state.Rays)
        {
            if (ray.IsExpired)
            {
                ray.FadeTicks--;
                continue;
            }

            // Check what's at the current head position EVERY tick (not just advance ticks).
            // Otherwise mobile blocks can pass through a stationary ray head between advances.
            if (TryHitAt(state, ray))
            {
                ray.IsExpired = true;
                continue;
            }

            // If the ray's current head is sitting on a wall (e.g. fired directly into
            // an adjacent wall), damage it and expire.
            var headCell = state.Grid[ray.HeadPos];
            if (!headCell.IsPassable)
            {
                if (headCell.HitWall())
                {
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.WallDamaged, ray.HeadPos, ray.PlayerId));
                    if (headCell.Terrain == TerrainType.None)
                    {
                        state.VisualEvents.Add(new VisualEvent(
                            VisualEventType.WallDestroyed, ray.HeadPos, ray.PlayerId));
                    }
                }
                ray.IsExpired = true;
                continue;
            }

            ray.TickCounter++;
            if (ray.TickCounter < ray.AdvanceInterval) continue;
            ray.TickCounter = 0;

            // Advance
            var nextPos = ray.HeadPos + ray.Direction.ToOffset();
            ray.Distance++;

            if (ray.Distance > ray.Range || !state.Grid.InBounds(nextPos))
            {
                ray.IsExpired = true;
                continue;
            }

            // If the next cell is blocked by terrain/wall, damage breakable/fragile
            // walls and expire the ray there.
            var nextCell = state.Grid[nextPos];
            if (!nextCell.IsPassable)
            {
                if (nextCell.HitWall())
                {
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.WallDamaged, nextPos, ray.PlayerId));
                    if (nextCell.Terrain == TerrainType.None)
                    {
                        state.VisualEvents.Add(new VisualEvent(
                            VisualEventType.WallDestroyed, nextPos, ray.PlayerId));
                    }
                }
                ray.HeadPos = nextPos;
                ray.IsExpired = true;
                continue;
            }

            ray.HeadPos = nextPos;

            // Check hit at the new head immediately so a mobile victim standing at
            // the advance destination is caught on the same tick.
            if (TryHitAt(state, ray))
            {
                ray.IsExpired = true;
                continue;
            }
        }

        // Remove fully faded rays
        state.Rays.RemoveAll(r => r.IsExpired && r.FadeTicks <= 0);

        // Decay stun timers
        foreach (var block in state.Blocks)
        {
            if (block.StunTimer > 0)
                block.StunTimer--;
        }
    }

    /// <summary>
    /// Check if a ray hits a target at its current head position.
    /// Returns true if the ray should stop.
    /// </summary>
    private static bool TryHitAt(GameState state, Ray ray)
    {
        var block = state.GetBlockAt(ray.HeadPos);
        if (block == null) return false;

        // Nuggets block rays but are not harmed
        if (block.Type == BlockType.Nugget)
            return true; // Ray stops, nugget unharmed

        // Don't hit friendly blocks (or teammates).
        if (!state.AreEnemies(block.PlayerId, ray.PlayerId)) return false;

        switch (ray.Type)
        {
            case RayType.Stun:
                if (block.Type == BlockType.Wall)
                {
                    if (block.FortifiedHp > 0)
                    {
                        block.FortifiedHp--;
                        state.VisualEvents.Add(new VisualEvent(
                            VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                        return true; // Ray stops, wall survives
                    }

                    // Normal wall destruction
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.BlockDied, ray.HeadPos, block.PlayerId, BlockId: block.Id));
                    state.RemoveBlock(block);
                    return true; // Ray stops at walls
                }

                // Stun the target but keep going — skip if already hit by this ray
                ray.HitBlockIds ??= new HashSet<int>();
                if (!ray.HitBlockIds.Add(block.Id))
                    return false; // Already hit this block, don't re-stun

                block.StunTimer = Constants.StunDuration;
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                return false; // Penetrates non-wall blocks

            case RayType.Blast:
                // Blast kills non-wall, non-formation enemies. Stops at walls.
                if (block.Type == BlockType.Wall || block.IsInFormation)
                    return true; // Blocked, ray stops

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlastRayFired, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlockDied, ray.HeadPos, block.PlayerId, BlockId: block.Id));
                state.RemoveBlock(block);
                return true;
        }

        return false;
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
