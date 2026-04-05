namespace Blocker.Game.Editor;

[Flags]
public enum SymmetryMode
{
    None = 0,
    LeftRight = 1,
    TopBottom = 2,
    DiagonalTLBR = 4,
    DiagonalTRBL = 8
}

public class SymmetryMirror
{
    public SymmetryMode Mode { get; set; } = SymmetryMode.None;
    public Dictionary<int, int> SlotMirrorMap { get; } = new();

    public List<(int X, int Y, int Slot)> GetMirroredPositions(int x, int y, int slot, int mapWidth, int mapHeight)
    {
        var results = new HashSet<(int X, int Y, int Slot)> { (x, y, slot) };

        if (Mode == SymmetryMode.None)
            return results.ToList();

        var current = results.ToList();
        foreach (var pos in current)
        {
            if (Mode.HasFlag(SymmetryMode.LeftRight))
            {
                int mx = mapWidth - 1 - pos.X;
                int mirrorSlot = GetMirrorSlot(pos.Slot);
                results.Add((mx, pos.Y, mirrorSlot));
            }
            if (Mode.HasFlag(SymmetryMode.TopBottom))
            {
                int my = mapHeight - 1 - pos.Y;
                int mirrorSlot = GetMirrorSlot(pos.Slot);
                results.Add((pos.X, my, mirrorSlot));
            }
            if (Mode.HasFlag(SymmetryMode.DiagonalTLBR))
            {
                // Swap X↔Y; only valid if mirrored coords are in bounds
                int dx = pos.Y;
                int dy = pos.X;
                if (dx >= 0 && dx < mapWidth && dy >= 0 && dy < mapHeight)
                {
                    int mirrorSlot = GetMirrorSlot(pos.Slot);
                    results.Add((dx, dy, mirrorSlot));
                }
            }
            if (Mode.HasFlag(SymmetryMode.DiagonalTRBL))
            {
                int mx = mapWidth - 1 - pos.Y;
                int my = mapHeight - 1 - pos.X;
                if (mx >= 0 && mx < mapWidth && my >= 0 && my < mapHeight)
                {
                    int mirrorSlot = GetMirrorSlot(pos.Slot);
                    results.Add((mx, my, mirrorSlot));
                }
            }
        }

        // Combined symmetries (e.g., LR+TB = 4-way)
        if (Mode.HasFlag(SymmetryMode.LeftRight) && Mode.HasFlag(SymmetryMode.TopBottom))
        {
            var pass2 = results.ToList();
            foreach (var pos in pass2)
            {
                int mx = mapWidth - 1 - pos.X;
                int my = mapHeight - 1 - pos.Y;
                int mirrorSlot = GetMirrorSlot(GetMirrorSlot(pos.Slot));
                results.Add((mx, my, mirrorSlot));
            }
        }

        return results.ToList();
    }

    private int GetMirrorSlot(int sourceSlot)
    {
        return SlotMirrorMap.TryGetValue(sourceSlot, out int target) ? target : sourceSlot;
    }
}
