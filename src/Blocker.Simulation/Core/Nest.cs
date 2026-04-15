namespace Blocker.Simulation.Core;

public enum NestType
{
    Builder,  // 3 Builders orthogonal to center
    Soldier,  // 3 Builders + 2 Walls (5-block cross)
    Stunner   // 3 Soldiers + 2 Walls (5-block cross)
}

/// <summary>
/// An active nest formation that spawns units.
/// Game bible Section 6.
/// </summary>
public class Nest
{
    public int Id { get; init; }
    public NestType Type { get; set; }
    public int PlayerId { get; init; }
    public GridPos Center { get; init; }

    /// <summary>Block IDs of all members (builders/soldiers/walls).</summary>
    public List<int> MemberIds { get; init; } = [];

    /// <summary>Spawn progress in ticks. When it reaches the threshold, a unit spawns.</summary>
    public int SpawnProgress { get; set; }

    /// <summary>Whether spawning is paused (e.g., stunned member).</summary>
    public bool IsPaused { get; set; }

    /// <summary>Tearing down countdown. >0 means dissolution in progress (Section 7).</summary>
    public int TeardownTimer { get; set; }
    public bool IsTearingDown => TeardownTimer > 0;

    public int GetSpawnTicks(GroundType ground)
    {
        int baseTicks = Type switch
        {
            NestType.Builder => ground == GroundType.Overload
                ? Constants.SpawnTicksWarden
                : Constants.SpawnTicksBuilder,
            NestType.Soldier => ground == GroundType.Overload
                ? Constants.SpawnTicksJumper
                : Constants.SpawnTicksSoldier,
            NestType.Stunner => Constants.SpawnTicksStunner,
            _ => Constants.SpawnTicksBuilder
        };

        if (ground == GroundType.Proto)
            baseTicks *= Constants.ProtoSpawnMultiplier;

        return baseTicks;
    }

    public Blocks.BlockType GetSpawnBlockType(GroundType ground) => Type switch
    {
        NestType.Builder => ground == GroundType.Overload
            ? Blocks.BlockType.Warden
            : Blocks.BlockType.Builder,
        NestType.Soldier => ground == GroundType.Overload
            ? Blocks.BlockType.Jumper
            : Blocks.BlockType.Soldier,
        NestType.Stunner => Blocks.BlockType.Stunner,
        _ => Blocks.BlockType.Builder
    };
}
