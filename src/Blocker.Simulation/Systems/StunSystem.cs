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
        var perpOffsets = GetPerpendicularOffsets(direction);

        var origins = new GridPos[]
        {
            stunner.Pos,
            stunner.Pos + perpOffsets.left,
            stunner.Pos + perpOffsets.right
        };

        foreach (var origin in origins)
        {
            // Start one cell ahead of the stunner
            var startPos = origin + dirOffset;
            if (!state.Grid.InBounds(startPos)) continue;

            var ray = new Ray
            {
                Type = RayType.Stun,
                PlayerId = stunner.PlayerId,
                Origin = origin,
                Direction = direction,
                HeadPos = startPos,
                Distance = 1,
                Range = Constants.StunRange,
                AdvanceInterval = Constants.StunUnitRayAdvanceInterval,
                FadeTicks = Constants.StunRayFade
            };

            state.Rays.Add(ray);
        }

        // Apply cooldown — stunner moves at 1/3 speed during cooldown
        stunner.Cooldown = Constants.StunCooldown;
        stunner.MoveTarget = null; // Stop current movement

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.StunRayFired, stunner.Pos, stunner.PlayerId,
            Direction: direction, Range: Constants.StunRange, BlockId: stunner.Id));

        return true;
    }

    /// <summary>
    /// Self-destruct: fully rooted Stunner fires stun blasts in all 8 directions, then dies.
    /// Soldier self-destruct fires kill blasts in all 8 directions (range 3), then dies.
    /// </summary>
    public static bool SelfDestruct(GameState state, Block block)
    {
        if (block.Type is not (BlockType.Stunner or BlockType.Soldier)) return false;
        if (!block.IsFullyRooted) return false;

        // Determine ray type and range based on block type
        var rayType = block.Type == BlockType.Soldier ? RayType.Blast : RayType.Stun;
        var range = block.Type == BlockType.Soldier ? Constants.SoldierExplodeRange : Constants.StunTowerRange;

        // Fire one ray in each of the 8 directions
        foreach (var offset in GridPos.AllOffsets)
        {
            var startPos = block.Pos + offset;
            if (!state.Grid.InBounds(startPos)) continue;

            var dir = OffsetToDirection(offset);

            var ray = new Ray
            {
                Type = rayType,
                PlayerId = block.PlayerId,
                Origin = block.Pos,
                Direction = dir,
                HeadPos = startPos,
                Distance = 1,
                Range = range,
                AdvanceInterval = block.Type == BlockType.Soldier
                    ? Constants.BlastUnitRayAdvanceInterval
                    : Constants.StunUnitRayAdvanceInterval,
                FadeTicks = Constants.StunRayFade
            };
            state.Rays.Add(ray);
        }

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.SelfDestructed, block.Pos, block.PlayerId, BlockId: block.Id));

        state.RemoveBlock(block);
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

            ray.TickCounter++;
            if (ray.TickCounter < ray.AdvanceInterval) continue;
            ray.TickCounter = 0;

            // Check what's at the current head position
            if (TryHitAt(state, ray))
            {
                ray.IsExpired = true;
                continue;
            }

            // Check if blocked by terrain/impassable
            if (!state.Grid.InBounds(ray.HeadPos) || !state.Grid[ray.HeadPos].IsPassable)
            {
                ray.IsExpired = true;
                continue;
            }

            // Advance
            var nextPos = ray.HeadPos + ray.Direction.ToOffset();
            ray.Distance++;

            if (ray.Distance > ray.Range || !state.Grid.InBounds(nextPos))
            {
                ray.IsExpired = true;
                continue;
            }

            ray.HeadPos = nextPos;
        }

        // Remove fully faded rays
        state.Rays.RemoveAll(r => r.IsExpired && r.FadeTicks <= 0);

        // Decay stun timers
        foreach (var block in state.Blocks)
        {
            if (block.StunTimer > 0)
                block.StunTimer--;

            if (block.Cooldown > 0)
                block.Cooldown--;
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

        // Don't hit friendly blocks
        if (block.PlayerId == ray.PlayerId) return false;

        switch (ray.Type)
        {
            case RayType.Stun:
                if (block.Type == BlockType.Wall)
                {
                    // Walls are killed by stun rays
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                    state.RemoveBlock(block);
                }
                else
                {
                    // Stun the target
                    block.StunTimer = Constants.StunDuration;
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.StunRayHit, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
                }
                return true; // Ray stops at first hit

            case RayType.Blast:
                // Blast kills non-wall, non-formation enemies. Stops at walls.
                if (block.Type == BlockType.Wall || block.IsInFormation)
                    return true; // Blocked, ray stops

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlastRayFired, ray.HeadPos, ray.PlayerId, BlockId: block.Id));
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

    /// <summary>Approximate a grid offset to the nearest cardinal direction.</summary>
    private static Direction OffsetToDirection(GridPos offset)
    {
        if (Math.Abs(offset.Y) >= Math.Abs(offset.X))
            return offset.Y < 0 ? Direction.Up : Direction.Down;
        return offset.X < 0 ? Direction.Left : Direction.Right;
    }
}
