namespace Blocker.Simulation.Core;

public enum FormationType
{
    Supply  // 3 Walls in L-shape → +7 pop cap
}

/// <summary>
/// A non-nest formation (Supply, etc.). Game bible Section 7.
/// </summary>
public class Formation
{
    public int Id { get; init; }
    public FormationType Type { get; init; }
    public int PlayerId { get; init; }
    public List<int> MemberIds { get; init; } = [];

    /// <summary>Tearing down countdown. >0 means dissolution in progress (Section 7).</summary>
    public int TeardownTimer { get; set; }
    public bool IsTearingDown => TeardownTimer > 0;
}
