using Blocker.Simulation.Blocks;

namespace Blocker.Simulation.Core;

/// <summary>
/// Static accessor for the active SimulationConfig.
/// Initialized once at game start. Defaults match game bible Section 18.
/// </summary>
public static class Constants
{
    private static SimulationConfig _config = new();

    public static SimulationConfig Config => _config;

    public static void Initialize(SimulationConfig config) => _config = config;

    public static void Reset() => _config = new();

    // --- Compatibility accessors ---

    // Grid
    public static int DefaultGridWidth => _config.Grid.DefaultWidth;
    public static int DefaultGridHeight => _config.Grid.DefaultHeight;

    // Rooting
    public static int GetRootTicks(BlockType type) => _config.GetRootTicks(type);
    public static int GetUprootTicks(BlockType type) => _config.GetUprootTicks(type);
    public static int RootTicks => _config.Builder.RootTicks;
    public static int UprootTicks => _config.Builder.UprootTicks;

    // Spawning
    public static int SpawnTicksBuilder => _config.Builder.SpawnTicks;
    public static int SpawnTicksSoldier => _config.Soldier.SpawnTicks;
    public static int SpawnTicksStunner => _config.Stunner.SpawnTicks;
    public static int SpawnTicksWarden => _config.Warden.SpawnTicks;
    public static int SpawnTicksJumper => _config.Jumper.SpawnTicks;
    public static int ProtoSpawnMultiplier => _config.Economy.ProtoSpawnMultiplier;

    // Movement
    public static int MoveInterval => _config.Builder.MoveInterval;
    public static int SoldierMoveInterval => _config.Soldier.MoveInterval;
    public static int StunnerMoveInterval => _config.Stunner.MoveInterval;

    // Stun
    public static int StunDuration => _config.Stunner.StunDuration;
    public static int StunCooldown => _config.Stunner.StunCooldown;
    public static int StunRange => _config.Stunner.StunRange;
    public static int StunRayFade => _config.Ray.StunRayFade;
    public static int StunUnitRayAdvanceInterval => _config.Stunner.UnitRayAdvanceInterval;
    public static int StunTowerRayAdvanceInterval => _config.Tower.StunTowerRayAdvanceInterval;

    // Towers
    public static int StunTowerFireInterval => _config.Tower.StunTowerFireInterval;
    public static int StunTowerRange => _config.Tower.StunTowerRange;
    public static int SoldierTowerFireInterval => _config.Tower.SoldierTowerFireInterval;
    public static int SoldierTowerRange => _config.Tower.SoldierTowerRange;

    // Blast
    public static int BlastUnitRayAdvanceInterval => _config.Ray.BlastUnitRayAdvanceInterval;
    public static int BlastTowerRayAdvanceInterval => _config.Tower.BlastTowerRayAdvanceInterval;

    // Push
    public static int PushWaveInterval => _config.Push.WaveInterval;
    public static int PushRange => _config.Push.Range;
    public static int PushKnockback => _config.Push.Knockback;
    public static int PushWaveAdvanceInterval => _config.Push.WaveAdvanceInterval;
    public static int PushWaveFade => _config.Push.WaveFade;

    // Soldier
    public static int SoldierExplodeRange => _config.Soldier.ExplodeRange;
    public static int SoldierMaxHp => _config.Soldier.Hp;

    // Population
    public static int SupplyPopCap => _config.Economy.SupplyPopCap;
    public static int SupplyMembers => _config.Economy.SupplyMembers;

    // Neutral obstacles
    public static int FragileWallSoldierThreshold => _config.Combat.FragileWallSoldierThreshold;

    // Warden
    public static int WardenZocRadius => _config.Warden.ZocRadius;
    public static int WardenPullRadius => _config.Warden.PullRadius;
    public static int WardenPullCooldown => _config.Warden.PullCooldown;

    // Jumper
    public static int JumperJumpRange => _config.Jumper.JumpRange;
    public static int JumperJumpCooldown => _config.Jumper.JumpCooldown;
    public static int JumperMaxHp => _config.Jumper.Hp;

    // Movement give-up
    public static int MoveGiveUpTicks => _config.Combat.MoveGiveUpTicks;

    // Death effects
    public static int DeathEffectTicks => _config.DeathEffectTicks;

    // Formation teardown
    public static int TeardownTicks => _config.TeardownTicks;

    // Population costs
    public static int PopCostBuilder => _config.Builder.PopCost;
    public static int PopCostWall => _config.Wall.PopCost;
    public static int PopCostSoldier => _config.Soldier.PopCost;
    public static int PopCostStunner => _config.Stunner.PopCost;
    public static int PopCostWarden => _config.Warden.PopCost;
    public static int PopCostJumper => _config.Jumper.PopCost;

    // Nugget
    public static int NuggetMiningTicks => _config.Nugget.MiningTicks;
    public static int NuggetMoveInterval => _config.Nugget.MoveInterval;
    public static int PopCostNugget => _config.Nugget.PopCost;
    public static int NuggetRefineRadius => _config.Nugget.RefineRadius;
    public static int FortifiedWallHp => _config.Nugget.FortifiedWallHp;
    public static int FortifiedWallCount => _config.Nugget.FortifiedWallCount;
}
