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
        // Reset all to base interval, then apply stunner cooldown slow
        foreach (var block in state.Blocks)
        {
            block.EffectiveMoveInterval = block.MoveInterval;
            if (block.IsOnCooldown && block.Type == BlockType.Stunner)
                block.EffectiveMoveInterval *= 3;
        }

        // Find all rooted, non-stunned Wardens and apply slow to enemies in range
        foreach (var warden in state.Blocks)
        {
            if (warden.Type != BlockType.Warden) continue;
            if (!warden.IsFullyRooted) continue;
            if (warden.IsStunned) continue;

            foreach (var target in state.Blocks)
            {
                if (!state.AreEnemies(target, warden)) continue; // Friendly / teammate
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
            if (!state.AreEnemies(warden, block)) continue; // Friendly / teammate
            if (block.Pos.ChebyshevDistance(warden.Pos) <= Constants.WardenZocRadius)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Magnet Pull: a fully rooted Warden pulls all enemy mobile blocks
    /// within Chebyshev radius as close as possible in a single action.
    /// Diagonal movement is allowed. Cooldown applies.
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
            .Where(t => state.AreEnemies(t, warden) && !t.IsImmobile && t.Type != BlockType.Wall
                        && t.Pos.ChebyshevDistance(warden.Pos) <= Constants.WardenPullRadius)
            .OrderBy(t => t.Pos.ChebyshevDistance(warden.Pos))
            .ToList();

        foreach (var target in targets)
        {
            var originPos = target.Pos;
            var current = target.Pos;

            // Step toward warden as far as possible (diagonal allowed)
            while (true)
            {
                var dx = warden.Pos.X - current.X;
                var dy = warden.Pos.Y - current.Y;
                if (dx == 0 && dy == 0) break;

                var step = new GridPos(Math.Sign(dx), Math.Sign(dy));
                var next = current + step;

                if (!state.Grid.InBounds(next) || !state.Grid[next].IsPassable || state.Grid[next].BlockId.HasValue)
                    break;

                current = next;
            }

            if (current == originPos) continue;

            // Move directly from origin to final position (single move, no intermediate events)
            if (state.Grid.InBounds(originPos) && state.Grid[originPos].BlockId == target.Id)
                state.Grid[originPos].BlockId = null;

            target.PrevPos = originPos;
            target.Pos = current;
            target.WasPulledThisTick = true;
            state.Grid[current].BlockId = target.Id;

            state.VisualEvents.Add(new VisualEvent(VisualEventType.BlockMoved, current, target.PlayerId, BlockId: target.Id));
            pulledAny = true;
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
