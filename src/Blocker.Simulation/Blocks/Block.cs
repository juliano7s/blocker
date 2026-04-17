using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Blocks;

public class Block
{
    public int Id { get; init; }
    public BlockType Type { get; set; }
    public int PlayerId { get; init; }

    // Position
    public GridPos Pos { get; set; }
    public GridPos PrevPos { get; set; }

    // State machine
    public BlockState State { get; set; } = BlockState.Mobile;
    public int RootProgress { get; set; }    // 0 → RootTicks for rooting, RootTicks → 0 for uprooting

    // HP (Soldier, Jumper only)
    public int Hp { get; set; }

    // Stun
    public int StunTimer { get; set; }
    public bool IsStunned => StunTimer > 0;

    // Cooldowns
    public int Cooldown { get; set; }
    public bool IsOnCooldown => Cooldown > 0;

    // Movement
    public GridPos? MoveTarget { get; set; }
    public bool IsAttackMoving { get; set; }
    public int StuckTicks { get; set; }       // Ticks unable to move toward target

    // Push state
    public bool IsPushing { get; set; }
    public Direction? PushDirection { get; set; }
    public bool WasPushedThisTick { get; set; }
    public bool WasJumpedThisTick { get; set; }

    // Jumper state
    public bool HasCombo { get; set; }        // Can jump again immediately after killing
    public bool IsJumping { get; set; }       // Currently mid-jump (for animation)
    public bool MobileCooldown { get; set; }  // True when cooldown allows movement (Jumper combo, Stunner post-fire)

    // Command queue (Section 10.6)
    public Queue<QueuedCommand> CommandQueue { get; } = new();

    // Formation membership (set by formation system)
    public int? FormationId { get; set; }
    public bool IsInFormation => FormationId.HasValue;

    public bool IsFullyRooted => State == BlockState.Rooted;
    public bool IsMobile => State == BlockState.Mobile;
    public bool IsImmobile => Type == BlockType.Wall || State != BlockState.Mobile;

    public int PopCost => Type switch
    {
        BlockType.Builder => Constants.PopCostBuilder,
        BlockType.Wall => Constants.PopCostWall,
        BlockType.Soldier => Constants.PopCostSoldier,
        BlockType.Stunner => Constants.PopCostStunner,
        BlockType.Warden => Constants.PopCostWarden,
        BlockType.Jumper => Constants.PopCostJumper,
        _ => 0
    };

    public int MoveInterval => Type switch
    {
        BlockType.Soldier => Constants.SoldierMoveInterval,
        BlockType.Stunner => Constants.StunnerMoveInterval,
        _ => Constants.MoveInterval
    };

    /// <summary>
    /// Effective move interval considering Warden ZoC slow.
    /// Set by WardenSystem each tick.
    /// </summary>
    public int EffectiveMoveInterval { get; set; }
}
