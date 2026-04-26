namespace Blocker.Simulation.Core;

public class VisibilityMap
{
    private readonly bool[] _visible;
    private readonly bool[] _explored;

    public int Width { get; }
    public int Height { get; }

    public VisibilityMap(int width, int height)
    {
        Width = width;
        Height = height;
        _visible = new bool[width * height];
        _explored = new bool[width * height];
    }

    public bool IsVisible(int x, int y) => _visible[y * Width + x];
    public bool IsVisible(GridPos pos) => IsVisible(pos.X, pos.Y);

    public bool IsExplored(int x, int y) => _explored[y * Width + x];
    public bool IsExplored(GridPos pos) => IsExplored(pos.X, pos.Y);

    public void ClearVisible() => Array.Clear(_visible, 0, _visible.Length);

    public void SetVisible(int x, int y)
    {
        int i = y * Width + x;
        _visible[i] = true;
        _explored[i] = true;
    }

    /// <summary>Mark every cell as explored (used on game-over reveal).</summary>
    public void RevealAll()
    {
        Array.Fill(_explored, true);
        Array.Fill(_visible, true);
    }

    /// <summary>
    /// Returns true if the cell at (x,y) has explored terrain data suitable
    /// for pathfinding: the cell has been explored, AND its terrain was passable
    /// when last visible. Unknown cells (unexplored) return false.
    /// </summary>
    public bool IsExploredPassable(int x, int y) => _explored[y * Width + x];

    public bool[] ExploredArray => _explored;
}