namespace Blocker.Simulation.Core;

public enum Direction
{
    Up,
    Right,
    Down,
    Left
}

public static class DirectionExtensions
{
    public static GridPos ToOffset(this Direction dir) => dir switch
    {
        Direction.Up => new(0, -1),
        Direction.Right => new(1, 0),
        Direction.Down => new(0, 1),
        Direction.Left => new(-1, 0),
        _ => throw new ArgumentOutOfRangeException(nameof(dir))
    };

    public static Direction Opposite(this Direction dir) => dir switch
    {
        Direction.Up => Direction.Down,
        Direction.Right => Direction.Left,
        Direction.Down => Direction.Up,
        Direction.Left => Direction.Right,
        _ => throw new ArgumentOutOfRangeException(nameof(dir))
    };
}
