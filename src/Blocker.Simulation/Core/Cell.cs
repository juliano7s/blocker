namespace Blocker.Simulation.Core;

public enum GroundType
{
    Normal,
    Boot,
    Overload,
    Proto,
    Terrain,
    BreakableWall,
    FragileWall
}

public class Cell
{
    public GroundType Ground { get; set; }

    /// <summary>Block ID occupying this cell, or null if empty.</summary>
    public int? BlockId { get; set; }

    public bool IsPassable => Ground != GroundType.Terrain
                           && Ground != GroundType.BreakableWall
                           && Ground != GroundType.FragileWall;

    public bool IsNestZone => Ground is GroundType.Boot or GroundType.Overload or GroundType.Proto;
}
