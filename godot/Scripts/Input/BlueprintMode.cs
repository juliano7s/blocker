using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

/// <summary>
/// Blueprint system: 6 formation templates with rotation, ghost preview, and unit assignment.
/// Keys 1-6 toggle blueprints. R rotates 90°. X clears ghosts.
/// Shift+click for multi-placement. Closest-first greedy unit assignment.
/// </summary>
public class BlueprintMode
{
    public enum BlueprintType
    {
        None = 0,
        BuilderNest = 1,   // 3 builders in cross + empty center
        SoldierNest = 2,   // 3 builders + 2 walls
        StunnerNest = 3,   // 3 soldiers + 2 walls
        Supply = 4,        // L-shaped 3 walls
        StunTower = 5,     // Stunner center + 1 builder
        SoldierTower = 6,  // Soldier center + 1 builder
    }

    public record struct BlueprintCell(GridPos Offset, string Role); // Role: "builder", "soldier", "stunner", "wall", "center"

    public BlueprintType ActiveType { get; private set; }
    public int Rotation { get; private set; } // 0, 1, 2, 3 = 0°, 90°, 180°, 270°
    private bool _manuallyRotated; // true once user presses R; stops auto-rotation

    public record struct PlacedGhost(BlueprintType Type, GridPos Position, int Rotation, float PlacedAt);
    public List<PlacedGhost> PlacedGhosts { get; } = [];

    public bool IsActive => ActiveType != BlueprintType.None;

    /// <summary>Toggle a blueprint type. Same key again deactivates.</summary>
    public void Toggle(BlueprintType type)
    {
        if (ActiveType == type)
            ActiveType = BlueprintType.None;
        else
        {
            ActiveType = type;
            Rotation = 0;
            _manuallyRotated = false;
        }
    }

    public void Deactivate()
    {
        ActiveType = BlueprintType.None;
        _manuallyRotated = false;
    }

    public void Rotate()
    {
        Rotation = (Rotation + 1) % 4;
        _manuallyRotated = true;
    }

    /// <summary>
    /// Auto-set rotation so the formation faces away from the nearest map edge.
    /// Only applies when the user hasn't manually rotated (pressed R).
    /// </summary>
    public void AutoRotate(GridPos hoverPos, int mapWidth, int mapHeight)
    {
        if (!IsActive || _manuallyRotated) return;

        int distLeft = hoverPos.X;
        int distRight = mapWidth - 1 - hoverPos.X;
        int distTop = hoverPos.Y;
        int distBottom = mapHeight - 1 - hoverPos.Y;

        int minDist = Math.Min(Math.Min(distLeft, distRight), Math.Min(distTop, distBottom));

        // Determine which direction the formation should face (away from closest edge)
        // Ties broken by picking the first match in priority order
        bool isNest = ActiveType is BlueprintType.BuilderNest or BlueprintType.SoldierNest or BlueprintType.StunnerNest;

        if (isNest)
        {
            // Nest open side at each rotation: rot0=LEFT, rot1=UP, rot2=RIGHT, rot3=DOWN
            // We want open side to face AWAY from closest edge
            if (distLeft == minDist) Rotation = 2;       // Face RIGHT (away from left edge)
            else if (distTop == minDist) Rotation = 3;    // Face DOWN (away from top edge)
            else if (distRight == minDist) Rotation = 0;  // Face LEFT (away from right edge)
            else Rotation = 1;                             // Face UP (away from bottom edge)
        }
        else
        {
            // Towers/Supply: primary extension at each rotation: rot0=RIGHT, rot1=DOWN, rot2=LEFT, rot3=UP
            // We want extension to face AWAY from closest edge
            if (distLeft == minDist) Rotation = 0;       // Face RIGHT
            else if (distTop == minDist) Rotation = 1;    // Face DOWN
            else if (distRight == minDist) Rotation = 2;  // Face LEFT
            else Rotation = 3;                             // Face UP
        }
    }

    public void ClearGhosts() => PlacedGhosts.Clear();

    /// <summary>Prune ghosts older than 15 seconds.</summary>
    public void PruneGhosts(float currentTime)
    {
        PlacedGhosts.RemoveAll(g => currentTime - g.PlacedAt > 15f);
    }

    /// <summary>Get the cell offsets for the active blueprint at current rotation.</summary>
    public List<BlueprintCell> GetCells()
    {
        return GetCells(ActiveType, Rotation);
    }

    public static List<BlueprintCell> GetCells(BlueprintType type, int rotation)
    {
        var cells = GetBaseCells(type);
        for (int r = 0; r < rotation; r++)
            cells = RotateCells(cells);
        return cells;
    }

    private static List<BlueprintCell> GetBaseCells(BlueprintType type) => type switch
    {
        // Builder Nest: 3 builders in orthogonal cross around center
        // The center is empty; builders at up, right, down (leaving left open for spawning)
        BlueprintType.BuilderNest =>
        [
            new(new(0, -1), "builder"),  // up
            new(new(1, 0), "builder"),   // right
            new(new(0, 1), "builder"),   // down
        ],

        // Soldier Nest: 3 builders (ortho cross) + 2 walls (diagonal)
        BlueprintType.SoldierNest =>
        [
            new(new(0, -1), "builder"),  // up
            new(new(1, 0), "builder"),   // right
            new(new(0, 1), "builder"),   // down
            new(new(1, -1), "wall"),     // top-right diagonal
            new(new(1, 1), "wall"),      // bottom-right diagonal
        ],

        // Stunner Nest: 3 soldiers (ortho) + 2 walls (diagonal)
        BlueprintType.StunnerNest =>
        [
            new(new(0, -1), "soldier"),  // up
            new(new(1, 0), "soldier"),   // right
            new(new(0, 1), "soldier"),   // down
            new(new(1, -1), "wall"),     // top-right diagonal
            new(new(1, 1), "wall"),      // bottom-right diagonal
        ],

        // Supply: L-shaped 3 walls
        BlueprintType.Supply =>
        [
            new(new(0, 0), "wall"),
            new(new(1, 0), "wall"),
            new(new(0, 1), "wall"),
        ],

        // Stun Tower: stunner center + builder in a direction
        BlueprintType.StunTower =>
        [
            new(new(0, 0), "stunner"),
            new(new(1, 0), "builder"),
        ],

        // Soldier Tower: soldier center + builder in a direction
        BlueprintType.SoldierTower =>
        [
            new(new(0, 0), "soldier"),
            new(new(1, 0), "builder"),
        ],

        _ => []
    };

    private static List<BlueprintCell> RotateCells(List<BlueprintCell> cells)
    {
        // 90° clockwise: (x,y) → (−y, x)
        return cells.Select(c => new BlueprintCell(
            new GridPos(-c.Offset.Y, c.Offset.X), c.Role)).ToList();
    }
}
