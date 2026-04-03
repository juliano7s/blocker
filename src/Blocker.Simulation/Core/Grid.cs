namespace Blocker.Simulation.Core;

public class Grid
{
    private readonly Cell[,] _cells;

    public int Width { get; }
    public int Height { get; }

    public Grid(int width, int height)
    {
        Width = width;
        Height = height;
        _cells = new Cell[height, width];

        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                _cells[y, x] = new Cell();
    }

    public Cell this[int x, int y]
    {
        get => _cells[y, x];
        set => _cells[y, x] = value;
    }

    public Cell this[GridPos pos]
    {
        get => _cells[pos.Y, pos.X];
        set => _cells[pos.Y, pos.X] = value;
    }

    public bool InBounds(int x, int y) =>
        x >= 0 && x < Width && y >= 0 && y < Height;

    public bool InBounds(GridPos pos) =>
        InBounds(pos.X, pos.Y);
}
