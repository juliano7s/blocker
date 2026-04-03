using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Warden Zone of Control and Magnet Pull.
/// Game bible Section 4.5.
/// </summary>
public static class WardenSystem
{
    /// <summary>
    /// Update effective move intervals for all blocks based on Warden ZoC.
    /// Enemies within Chebyshev radius of a rooted Warden move at half speed.
    /// Called at the start of each tick before movement.
    /// </summary>
    public static void UpdateZoC(GameState state)
    {
        // Reset all to base interval
        foreach (var block in state.Blocks)
            block.EffectiveMoveInterval = block.MoveInterval;

        // Find all rooted, non-stunned Wardens and apply slow to enemies in range
        foreach (var warden in state.Blocks)
        {
            if (warden.Type != BlockType.Warden) continue;
            if (!warden.IsFullyRooted) continue;
            if (warden.IsStunned) continue;

            foreach (var target in state.Blocks)
            {
                if (target.PlayerId == warden.PlayerId) continue; // Friendly
                if (target.IsImmobile) continue; // No point slowing immobile
                if (target.Pos.ChebyshevDistance(warden.Pos) > Constants.WardenZocRadius) continue;

                // Double the move interval (half speed)
                target.EffectiveMoveInterval = target.MoveInterval * 2;
            }
        }
    }

    /// <summary>
    /// Check if a block is inside any enemy Warden's ZoC.
    /// Used by JumperSystem to prevent jump initiation.
    /// </summary>
    public static bool IsInEnemyWardenZoC(GameState state, Block block)
    {
        foreach (var warden in state.Blocks)
        {
            if (warden.Type != BlockType.Warden) continue;
            if (!warden.IsFullyRooted) continue;
            if (warden.IsStunned) continue;
            if (warden.PlayerId == block.PlayerId) continue;
            if (block.Pos.ChebyshevDistance(warden.Pos) <= Constants.WardenZocRadius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Magnet Pull: a fully rooted Warden pulls all uprooted enemy blocks
    /// within Chebyshev radius toward itself. Cooldown applies.
    /// </summary>
    public static bool MagnetPull(GameState state, Block warden)
    {
        if (warden.Type != BlockType.Warden) return false;
        if (!warden.IsFullyRooted) return false;
        if (warden.IsOnCooldown) return false;
        if (warden.IsStunned) return false;

        bool pulledAny = false;

        // Collect and sort by distance ascending — closest move first, making room for farther blocks
        var targets = state.Blocks
            .Where(t => t.PlayerId != warden.PlayerId && !t.IsImmobile && t.Type != BlockType.Wall
                        && t.Pos.ChebyshevDistance(warden.Pos) <= Constants.WardenPullRadius)
            .OrderBy(t => t.Pos.ChebyshevDistance(warden.Pos))
            .ToList();

        foreach (var target in targets)
        {
            // Pull one cell toward the Warden
            var dx = warden.Pos.X - target.Pos.X;
            var dy = warden.Pos.Y - target.Pos.Y;

            // Move one step in the dominant axis (or orthogonal if tied)
            GridPos step;
            if (Math.Abs(dx) >= Math.Abs(dy))
                step = new GridPos(Math.Sign(dx), 0);
            else
                step = new GridPos(0, Math.Sign(dy));

            var newPos = target.Pos + step;
            if (state.Grid.InBounds(newPos) && state.Grid[newPos].IsPassable)
            {
                state.TryMoveBlock(target, newPos);
                pulledAny = true;
            }
        }

        if (pulledAny)
        {
            warden.Cooldown = Constants.WardenPullCooldown;
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.MagnetPulled, warden.Pos, warden.PlayerId,
                Range: Constants.WardenPullRadius, BlockId: warden.Id));
        }

        return pulledAny;
    }
}
