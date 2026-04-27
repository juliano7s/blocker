using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Commands;

public enum CommandType
{
    Move,
    Root,         // Toggle root/uproot
    ConvertToWall,
    FireStunRay,  // Stunner fires stun ray in direction
    SelfDestruct, // Rooted Stunner self-destructs (stun blast all 8 dirs)
    CreateTower,  // Rooted Stunner/Soldier creates tower (T key)
    TogglePush,   // Rooted Builder toggles push mode (G key + direction)
    MagnetPull,   // Rooted Warden pulls nearby enemies (D key)
    Jump,         // Jumper jumps in cardinal direction (F key + direction)
    AttackMove,         // Move toward target, engage enemies en route (A key + click)
    Surrender,          // Player-level: marks the issuing player as eliminated. BlockIds is empty.
    ToggleSpawn,        // Player-level: toggle spawn for a unit type. UnitType field required.
    MineNugget,         // Builder mines an unmined nugget
    HealWithNugget,     // Nugget heals a damaged soldier/jumper
    FortifyWithNugget,  // Nugget fortifies walls
    ToggleRefine,       // Nest-level: toggle nugget refining on/off. BlockIds = nest members.
}

public record Command(
    int PlayerId,
    CommandType Type,
    List<int> BlockIds,
    GridPos? TargetPos = null,
    Direction? Direction = null,
    bool Queue = false,  // Shift+action: append to queue instead of clearing
    BlockType? UnitType = null   // used by ToggleSpawn
);

/// <summary>
/// A queued command for a single block. Processed one per tick in step 9.
/// </summary>
public record QueuedCommand(
    CommandType Type,
    GridPos? TargetPos = null,
    Direction? Direction = null
);
