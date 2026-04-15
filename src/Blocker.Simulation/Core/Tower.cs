namespace Blocker.Simulation.Core;

public enum TowerType
{
    Stun,    // Stunner center, fires stun rays
    Soldier  // Soldier center, fires blast rays
}

/// <summary>
/// An active tower formation. Game bible Sections 7.2, 7.3.
/// </summary>
public class Tower
{
    public int Id { get; init; }
    public TowerType Type { get; init; }
    public int PlayerId { get; init; }

    /// <summary>Block ID of the center unit (Stunner or Soldier).</summary>
    public int CenterId { get; init; }

    /// <summary>Builder arm block IDs mapped to their fire direction.</summary>
    public Dictionary<int, Direction> BuilderDirections { get; } = new();

    /// <summary>Ticks since last fire.</summary>
    public int FireTimer { get; set; }

    /// <summary>For Stun Tower: current direction index in the sweep cycle.</summary>
    public int SweepIndex { get; set; }

    /// <summary>For Stun Tower: whether a firing cycle is active.</summary>
    public bool IsFiring { get; set; }

    /// <summary>Teardown timer for voluntary dissolution.</summary>
    public int TeardownTimer { get; set; }
    public bool IsTearingDown => TeardownTimer > 0;

    public int FireInterval => Type switch
    {
        TowerType.Stun => Constants.StunTowerFireInterval,
        TowerType.Soldier => Constants.SoldierTowerFireInterval,
        _ => 16
    };

    public int Range => Type switch
    {
        TowerType.Stun => Constants.StunTowerRange,
        TowerType.Soldier => Constants.SoldierTowerRange,
        _ => 4
    };
}
