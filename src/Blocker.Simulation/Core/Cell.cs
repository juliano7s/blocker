namespace Blocker.Simulation.Core;

public enum GroundType
{
    Normal,
    Boot,
    Overload,
    Proto
}

public enum TerrainType
{
    None,
    Terrain,
    BreakableWall,
    FragileWall
}

public class Cell
{
    public GroundType Ground { get; set; }
    public TerrainType Terrain { get; set; }

    /// <summary>Block ID occupying this cell, or null if empty.</summary>
    public int? BlockId { get; set; }

    public bool IsPassable => Terrain == TerrainType.None;

    public bool IsNestZone => Ground is GroundType.Boot or GroundType.Overload or GroundType.Proto;
}
