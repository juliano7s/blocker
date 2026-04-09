namespace Blocker.Simulation.Core;

public enum RayType
{
    Stun,   // Stuns first enemy, kills walls
    Blast   // Kills non-wall, non-formation enemies
}

/// <summary>
/// An active ray traveling across the grid.
/// Rays advance one cell per advance interval, hit the first target, then expire.
/// </summary>
public class Ray
{
    private static int _nextId;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public RayType Type { get; init; }
    public int PlayerId { get; init; }
    public GridPos Origin { get; init; }
    public Direction Direction { get; init; }

    /// <summary>Current head position of the ray.</summary>
    public GridPos HeadPos { get; set; }

    /// <summary>How many cells the ray has traveled.</summary>
    public int Distance { get; set; }

    /// <summary>Max range in cells.</summary>
    public int Range { get; init; }

    /// <summary>Ticks between advances.</summary>
    public int AdvanceInterval { get; init; }

    /// <summary>Tick counter for advance timing.</summary>
    public int TickCounter { get; set; }

    /// <summary>Whether this ray is part of a radial explosion (self-destruct).</summary>
    public bool IsExplosion { get; init; }

    /// <summary>Whether the ray has hit something and is done.</summary>
    public bool IsExpired { get; set; }

    /// <summary>Remaining visual fade ticks after expiry.</summary>
    public int FadeTicks { get; set; }

    /// <summary>Reset the ID counter. Use only in tests.</summary>
    public static void ResetIdCounter() => _nextId = 0;
}
