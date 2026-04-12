using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Jumper jump mechanics: leap up to 5 cells cardinal, kill enemies in path.
/// Combo on kill, HP loss on miss. Warden ZoC blocks jumping.
/// Game bible Section 4.6.
/// </summary>
public static class JumperSystem
{
    /// <summary>
    /// Execute a jump in a cardinal direction.
    /// Returns true if the command was consumed (even if jump failed).
    /// </summary>
    public static bool Jump(GameState state, Block jumper, Direction direction, int? maxRange = null)
    {
        if (jumper.Type != BlockType.Jumper) return false;
        if (jumper.IsStunned) return false;
        if (jumper.Type == BlockType.Wall) return false;

        // Can't jump while on cooldown UNLESS we have combo
        if (jumper.IsOnCooldown && !jumper.HasCombo) return false;

        // Warden ZoC blocks jump initiation
        if (WardenSystem.IsInEnemyWardenZoC(state, jumper)) return false;

        var offset = direction.ToOffset();
        var pos = jumper.Pos;
        int kills = 0;
        bool hitObstacle = false;
        GridPos landingPos = pos;
        int range = Math.Max(1, Math.Min(maxRange ?? Constants.JumperJumpRange, Constants.JumperJumpRange));

        // Travel up to range cells
        for (int i = 1; i <= range; i++)
        {
            var nextPos = pos + new GridPos(offset.X * i, offset.Y * i);

            if (!state.Grid.InBounds(nextPos)) break;

            // Check terrain
            if (state.Grid[nextPos].Terrain == TerrainType.Terrain)
            {
                hitObstacle = true;
                break;
            }

            // Breakable/Fragile wall: hit and stop
            if (state.Grid[nextPos].HitWall())
            {
                hitObstacle = true;
                break;
            }

            if (!state.Grid[nextPos].IsPassable) break;

            var blockAtPos = state.GetBlockAt(nextPos);
            if (blockAtPos != null)
            {
                // Walls, formations, rooted blocks, friendly blocks stop the jump
                if (blockAtPos.Type == BlockType.Wall || blockAtPos.IsInFormation || blockAtPos.IsImmobile)
                {
                    hitObstacle = true;
                    break;
                }

                if (state.AreAllies(blockAtPos, jumper))
                {
                    break; // Can't pass through friendlies / teammates
                }

                // Kill enemies in path
                state.RemoveBlock(blockAtPos);
                kills++;
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlockDied, nextPos, blockAtPos.PlayerId,
                    BlockId: blockAtPos.Id));
            }

            landingPos = nextPos;
        }

        // Move jumper to landing position
        if (landingPos != jumper.Pos)
        {
            // Vacate old cell
            if (state.Grid.InBounds(jumper.Pos) && state.Grid[jumper.Pos].BlockId == jumper.Id)
                state.Grid[jumper.Pos].BlockId = null;

            jumper.PrevPos = jumper.Pos;
            jumper.Pos = landingPos;
            state.Grid[landingPos].BlockId = jumper.Id;

            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.JumpExecuted, landingPos, jumper.PlayerId,
                Direction: direction, Range: (landingPos - jumper.PrevPos).ManhattanDistance(new GridPos(0, 0)),
                BlockId: jumper.Id));
        }

        // Combo: if killed at least 1 and didn't hit an obstacle
        if (kills > 0 && !hitObstacle)
        {
            // Grant combo — can jump again immediately, NO cooldown yet
            jumper.HasCombo = true;
        }
        else
        {
            // Miss: lose 1 HP, apply immobile cooldown, no combo
            jumper.HasCombo = false;
            jumper.Hp--;
            jumper.Cooldown = Constants.JumperJumpCooldown;

            if (jumper.Hp <= 0)
            {
                state.RemoveBlock(jumper);
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlockDied, landingPos, jumper.PlayerId,
                    BlockId: jumper.Id));
            }
        }

        return true;
    }

    /// <summary>
    /// Consume combo on move command. Jumper becomes mobile but can't jump until cooldown expires.
    /// </summary>
    public static void ConsumeCombo(Block jumper)
    {
        if (jumper.Type != BlockType.Jumper) return;
        jumper.HasCombo = false;
        jumper.Cooldown = Constants.JumperJumpCooldown;
        jumper.MobileCooldown = true; // Can still move, just can't jump
    }
}
