namespace Blocker.Simulation.Maps;

/// <summary>
/// Per-slot configuration handed to MapLoader.Load.
/// TeamId allows team play (e.g. 2v2): two slots with the same TeamId share a team.
/// In FFA, TeamId == PlayerId so every player is on their own team.
/// </summary>
public record SlotAssignment(int SlotId, int PlayerId, int TeamId)
{
    /// <summary>FFA convenience: team = player. Used by single-player and tests.</summary>
    public SlotAssignment(int SlotId, int PlayerId) : this(SlotId, PlayerId, PlayerId) { }
}
