using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Advances root/uproot progress each tick.
/// Blocks transition: Mobile → Rooting → Rooted, or Rooted → Uprooting → Mobile.
/// </summary>
public static class RootingSystem
{
    public static void Tick(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.IsStunned) continue;

            switch (block.State)
            {
                case BlockState.Rooting:
                    block.RootProgress++;
                    if (block.RootProgress >= Constants.RootTicks)
                    {
                        block.RootProgress = Constants.RootTicks;
                        block.State = BlockState.Rooted;
                        state.VisualEvents.Add(new VisualEvent(
                            VisualEventType.BlockRooted, block.Pos, block.PlayerId, BlockId: block.Id));
                    }
                    break;

                case BlockState.Uprooting:
                    block.RootProgress--;
                    if (block.RootProgress <= 0)
                    {
                        block.RootProgress = 0;
                        block.State = BlockState.Mobile;
                        state.VisualEvents.Add(new VisualEvent(
                            VisualEventType.BlockUprooted, block.Pos, block.PlayerId, BlockId: block.Id));
                    }
                    break;
            }
        }
    }

    /// <summary>
    /// Toggle root/uproot for a block. Returns true if the action was valid.
    /// </summary>
    public static bool ToggleRoot(Block block)
    {
        if (block.Type == BlockType.Wall) return false;
        if (!CanRoot(block.Type)) return false;

        switch (block.State)
        {
            case BlockState.Mobile:
                block.State = BlockState.Rooting;
                block.RootProgress = 0;
                block.MoveTarget = null; // Stop moving
                return true;

            case BlockState.Rooting:
                // Cancel rooting — start uprooting
                block.State = BlockState.Uprooting;
                return true;

            case BlockState.Rooted:
                block.State = BlockState.Uprooting;
                return true;

            case BlockState.Uprooting:
                // Cancel uprooting — start rooting again
                block.State = BlockState.Rooting;
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// Convert a fully rooted Builder to a Wall. Irreversible.
    /// </summary>
    public static bool ConvertToWall(GameState state, Block block)
    {
        if (block.Type != BlockType.Builder) return false;
        if (!block.IsFullyRooted) return false;
        if (block.IsInFormation) return false;

        // Transform in place — keep position, change type
        block.Type = BlockType.Wall;
        block.State = BlockState.Rooted; // Walls are always "rooted"
        block.MoveTarget = null;
        block.IsPushing = false;
        block.PushDirection = null;

        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.WallConverted, block.Pos, block.PlayerId, BlockId: block.Id));
        return true;
    }

    private static bool CanRoot(BlockType type) =>
        type is BlockType.Builder or BlockType.Soldier or BlockType.Stunner
            or BlockType.Warden;
}
