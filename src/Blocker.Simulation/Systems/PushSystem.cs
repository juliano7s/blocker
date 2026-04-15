using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Push wave mechanics. Rooted Builders fire push waves that displace mobile blocks.
/// Game bible Section 9, tick steps 7-8.
/// </summary>
public static class PushSystem
{
    /// <summary>
    /// Toggle push mode on a rooted Builder.
    /// </summary>
    public static bool TogglePush(Block block, Direction? direction)
    {
        if (block.Type != BlockType.Builder) return false;
        if (!block.IsFullyRooted) return false;
        if (block.IsInFormation) return false;

        if (block.IsPushing && (direction == null || direction == block.PushDirection))
        {
            // Toggle off
            block.IsPushing = false;
            block.PushDirection = null;
            return true;
        }

        // Toggle on (or change direction)
        block.IsPushing = true;
        block.PushDirection = direction;
        return true;
    }

    /// <summary>
    /// Step 7: Fire new push waves from pushing builders.
    /// Step 8: Advance existing waves and displace blocks.
    /// </summary>
    public static void Tick(GameState state)
    {
        // Step 1: Clear push flags
        foreach (var block in state.Blocks)
            block.WasPushedThisTick = false;

        // Step 7: Fire new waves
        foreach (var block in state.Blocks)
        {
            if (!block.IsPushing) continue;
            if (!block.PushDirection.HasValue) continue;
            if (block.IsStunned) continue;

            // Fire wave every PushWaveInterval ticks
            if (state.TickNumber % Constants.PushWaveInterval != 0) continue;

            var dir = block.PushDirection.Value;
            var startPos = block.Pos + dir.ToOffset();
            if (!state.Grid.InBounds(startPos)) continue;

            var wave = new PushWave
            {
                Id = state.NextPushWaveId(),
                PlayerId = block.PlayerId,
                Origin = block.Pos,
                Direction = dir,
                HeadPos = startPos,
                Distance = 1,
                FadeTicks = Constants.PushWaveFade
            };

            state.PushWaves.Add(wave);
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.PushWaveFired, block.Pos, block.PlayerId,
                Direction: dir, Range: Constants.PushRange, BlockId: block.Id));
        }

        // Step 8: Advance waves
        foreach (var wave in state.PushWaves)
        {
            if (wave.IsExpired)
            {
                wave.FadeTicks--;
                continue;
            }

            wave.TickCounter++;
            if (wave.TickCounter < Constants.PushWaveAdvanceInterval) continue;
            wave.TickCounter = 0;

            // Check if blocked first
            if (IsBlocked(state, wave.HeadPos))
            {
                wave.IsExpired = true;
                continue;
            }

            // Check head position and next cell (two-cell hit zone)
            TryPushAt(state, wave, wave.HeadPos);
            var nextPos = wave.HeadPos + wave.Direction.ToOffset();
            if (state.Grid.InBounds(nextPos) && !IsBlocked(state, nextPos))
                TryPushAt(state, wave, nextPos);

            // Advance
            wave.HeadPos = nextPos;
            wave.Distance++;

            if (wave.Distance > Constants.PushRange || !state.Grid.InBounds(wave.HeadPos))
                wave.IsExpired = true;
        }

        // Remove faded waves
        state.PushWaves.RemoveAll(w => w.IsExpired && w.FadeTicks <= 0);
    }

    private static void TryPushAt(GameState state, PushWave wave, GridPos pos)
    {
        var block = state.GetBlockAt(pos);
        if (block == null) return;
        if (block.WasPushedThisTick) return;

        // Walls, rooted blocks, formations: block the wave
        if (block.Type == BlockType.Wall) return;
        if (block.IsImmobile) return;

        // Push the block (up to PushKnockback cells)
        var pushDir = wave.Direction.ToOffset();
        int pushed = 0;

        for (int i = 0; i < Constants.PushKnockback; i++)
        {
            var newPos = block.Pos + pushDir;
            if (!state.Grid.InBounds(newPos)) break;
            if (!state.Grid[newPos].IsPassable) break;

            // Chain push: if another mobile block is in the way, push it first
            var blocking = state.GetBlockAt(newPos);
            if (blocking != null)
            {
                if (blocking.IsImmobile || blocking.Type == BlockType.Wall)
                    break; // Can't push through
                if (!blocking.WasPushedThisTick)
                    TryPushAt(state, wave, newPos); // Recursive chain push

                // Check again if the cell is now free
                if (state.Grid[newPos].BlockId.HasValue)
                    break;
            }

            state.TryMoveBlock(block, newPos);
            pushed++;
        }

        if (pushed > 0)
            block.WasPushedThisTick = true;
    }

    private static bool IsBlocked(GameState state, GridPos pos)
    {
        if (!state.Grid.InBounds(pos)) return true;
        if (!state.Grid[pos].IsPassable) return true;

        var block = state.GetBlockAt(pos);
        if (block == null) return false;

        // Walls, rooted blocks, formations stop the wave
        return block.Type == BlockType.Wall || block.IsImmobile || block.IsInFormation;
    }
}
