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
    TowerFired,
    PlayerEliminated,
    GameOver,
    PopCapWarning,

    // Formation formed (per-type for distinct audio)
    BuilderNestFormed,
    SoldierNestFormed,
    StunnerNestFormed,
    SupplyFormed,
    StunTowerFormed,
    SoldierTowerFormed,

    // Formation dissolved (per-type for distinct audio)
    BuilderNestDissolved,
    SoldierNestDissolved,
    StunnerNestDissolved,
    SupplyDissolved,
    StunTowerDissolved,
    SoldierTowerDissolved,

    // Command-issued (player clicks)
    CommandMoveIssued,
    CommandRootIssued,
    CommandUprootIssued,
    CommandWallIssued,
}

public readonly record struct VisualEvent(
    VisualEventType Type,
    GridPos Position,
    int? PlayerId = null,
    Direction? Direction = null,
    int? Range = null,
    int? BlockId = null
);
