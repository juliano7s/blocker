namespace Blocker.Simulation.Core;

/// <summary>
/// An active push wave traveling across the grid.
/// Game bible Section 9.
/// </summary>
public class PushWave
{
    private static int _nextId;

    public int Id { get; } = Interlocked.Increment(ref _nextId);
    public int PlayerId { get; init; }
    public GridPos Origin { get; init; }
    public Direction Direction { get; init; }

    /// <summary>Current head position.</summary>
    public GridPos HeadPos { get; set; }

    /// <summary>How many cells the wave has traveled.</summary>
    public int Distance { get; set; }

    /// <summary>Tick counter for advance timing.</summary>
    public int TickCounter { get; set; }

    public bool IsExpired { get; set; }
    public int FadeTicks { get; set; }

    /// <summary>Reset the ID counter. Use only in tests.</summary>
    public static void ResetIdCounter() => _nextId = 0;
}
