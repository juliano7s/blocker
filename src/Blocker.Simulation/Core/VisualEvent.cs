namespace Blocker.Simulation.Core;

public enum VisualEventType
{
    // State transitions (simulation-driven)
    BlockMoved,
    BlockDied,
    BlockSpawned,
    BlockRooted,
    BlockUprooted,
    WallConverted,
    StunRayFired,
    StunRayHit,
    BlastRayFired,
    PushWaveFired,
    JumpExecuted,
    JumpLanded,
    MagnetPulled,
    SelfDestructed,
    FormationFormed,
    FormationDissolved,
    NestSpawned,
    TowerFired,
    PlayerEliminated,
    GameOver,

    // Command-issued (player clicks)
    CommandMoveIssued,
    CommandRootIssued,
    CommandUprootIssued,
}

public readonly record struct VisualEvent(
    VisualEventType Type,
    GridPos Position,
    int? PlayerId = null,
    Direction? Direction = null,
    int? Range = null,
    int? BlockId = null
);
