using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Self-destruct explosions: rooted Stunner or Soldier explodes in all 8 directions.
/// Game bible §4.3 (Soldier self-destruct) and §4.4 (Stunner self-destruct).
/// </summary>
public static class ExplosionSystem
{
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

            var dir = offset.ToDirection();

            var ray = new Ray
            {
                Id = state.NextRayId(),
                Type = rayType,
                PlayerId = block.PlayerId,
                Origin = block.Pos,
                Direction = dir,
                HeadPos = startPos,
                Distance = 1,
                Range = range,
                IsExplosion = true,
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
}
