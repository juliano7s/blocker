using Blocker.Simulation.Core;

namespace Blocker.Simulation.Blocks;

public class NuggetState
{
    public NuggetType Type { get; init; } = NuggetType.Standard;
    public bool IsMined { get; set; }
    public int MiningProgress { get; set; }
    public int? HealTargetId { get; set; }
    public GridPos? FortifyTargetPos { get; set; }
}
