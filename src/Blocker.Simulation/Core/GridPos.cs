namespace Blocker.Simulation.Core;

/// <summary>
/// Integer grid position. Row-major: Y = row, X = column. Origin at top-left.
/// </summary>
public readonly record struct GridPos(int X, int Y)
{
    public static GridPos operator +(GridPos a, GridPos b) => new(a.X + b.X, a.Y + b.Y);
    public static GridPos operator -(GridPos a, GridPos b) => new(a.X - b.X, a.Y - b.Y);

    /// <summary>Orthogonal neighbors: up, right, down, left.</summary>
    public static readonly GridPos[] OrthogonalOffsets =
    [
        new(0, -1), new(1, 0), new(0, 1), new(-1, 0)
    ];

    /// <summary>Diagonal neighbors only.</summary>
    public static readonly GridPos[] DiagonalOffsets =
    [
        new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)
    ];

    /// <summary>Convert an offset to the nearest cardinal direction.</summary>
    public Direction ToDirection()
    {
        if (Math.Abs(Y) >= Math.Abs(X))
            return Y < 0 ? Direction.Up : Direction.Down;
        return X < 0 ? Direction.Left : Direction.Right;
    }

    /// <summary>All 8 neighbors: orthogonal + diagonal.</summary>
    public static readonly GridPos[] AllOffsets =
    [
        new(0, -1), new(1, -1), new(1, 0), new(1, 1),
        new(0, 1), new(-1, 1), new(-1, 0), new(-1, -1)
    ];

    /// <summary>Chebyshev (chessboard) distance.</summary>
    public int ChebyshevDistance(GridPos other) =>
        Math.Max(Math.Abs(X - other.X), Math.Abs(Y - other.Y));

    /// <summary>Manhattan distance.</summary>
    public int ManhattanDistance(GridPos other) =>
        Math.Abs(X - other.X) + Math.Abs(Y - other.Y);

    public override string ToString() => $"({X}, {Y})";
}
