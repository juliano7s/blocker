namespace Blocker.Simulation.Core;

public enum VisualEventType
{
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
    GameOver
}

public readonly record struct VisualEvent(
    VisualEventType Type,
    GridPos Position,
    int? PlayerId = null,
    Direction? Direction = null,
    int? Range = null,
    int? BlockId = null
);
