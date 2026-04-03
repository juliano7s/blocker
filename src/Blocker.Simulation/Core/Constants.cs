namespace Blocker.Simulation.Core;

/// <summary>
/// All game constants from game bible Section 18.
/// </summary>
public static class Constants
{
    // Grid defaults
    public const int DefaultGridWidth = 41;
    public const int DefaultGridHeight = 25;

    // Rooting
    public const int RootTicks = 36;
    public const int UprootTicks = 24;

    // Spawning
    public const int SpawnTicksBuilder = 140;
    public const int SpawnTicksSoldier = 260;
    public const int SpawnTicksStunner = 360;
    public const int SpawnTicksWarden = 220;
    public const int SpawnTicksJumper = 300;
    public const int ProtoSpawnMultiplier = 5;

    // Movement intervals (ticks between moves)
    public const int MoveInterval = 3;          // Builder, Warden, Jumper
    public const int SoldierMoveInterval = 4;
    public const int StunnerMoveInterval = 2;

    // Stun
    public const int StunDuration = 160;
    public const int StunCooldown = 140;
    public const int StunRange = 5;
    public const int StunRayFade = 8;
    public const int StunUnitRayAdvanceInterval = 2;
    public const int StunTowerRayAdvanceInterval = 2;

    // Stun Tower
    public const int StunTowerFireInterval = 16;
    public const int StunTowerRange = 4;

    // Soldier Tower
    public const int SoldierTowerFireInterval = 12;
    public const int SoldierTowerRange = 5;

    // Blast rays
    public const int BlastUnitRayAdvanceInterval = 1;
    public const int BlastTowerRayAdvanceInterval = 1;

    // Push
    public const int PushWaveInterval = 8;
    public const int PushRange = 4;
    public const int PushKnockback = 3;
    public const int PushWaveAdvanceInterval = 1;
    public const int PushWaveFade = 6;

    // Soldier
    public const int SoldierExplodeRange = 3;
    public const int SoldierMaxHp = 4;

    // Population
    public const int SupplyPopCap = 7;
    public const int SupplyMembers = 3;

    // Neutral obstacles
    public const int FragileWallSoldierThreshold = 2;

    // Warden
    public const int WardenZocRadius = 4;
    public const int WardenPullRadius = 4;
    public const int WardenPullCooldown = 140;

    // Jumper
    public const int JumperJumpRange = 5;
    public const int JumperJumpCooldown = 120;
    public const int JumperMaxHp = 3;

    // Death effects
    public const int DeathEffectTicks = 10;

    // Formation teardown
    public const int TeardownTicks = 24;

    // Population costs
    public const int PopCostBuilder = 1;
    public const int PopCostWall = 0;
    public const int PopCostSoldier = 1;
    public const int PopCostStunner = 3;
    public const int PopCostWarden = 2;
    public const int PopCostJumper = 2;
}
