namespace Blocker.Simulation.Core;

/// <summary>
/// An active push wave traveling across the grid.
/// Game bible Section 9.
/// </summary>
public class PushWave
{
    public int Id { get; init; }
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
}
