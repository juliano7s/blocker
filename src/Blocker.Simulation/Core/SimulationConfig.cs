namespace Blocker.Simulation.Core;

public record BuilderConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 1;
    public int SpawnTicks { get; init; } = 140;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
}

public record SoldierConfig
{
    public int Hp { get; init; } = 4;
    public int MoveInterval { get; init; } = 4;
    public int PopCost { get; init; } = 1;
    public int SpawnTicks { get; init; } = 260;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int ExplodeRange { get; init; } = 3;
    public int ComboTicks { get; init; } = 60;
}

public record StunnerConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 2;
    public int PopCost { get; init; } = 3;
    public int SpawnTicks { get; init; } = 360;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int StunDuration { get; init; } = 160;
    public int StunCooldown { get; init; } = 140;
    public int StunRange { get; init; } = 5;
    public int UnitRayAdvanceInterval { get; init; } = 2;
}

public record WardenConfig
{
    public int Hp { get; init; } = 1;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 2;
    public int SpawnTicks { get; init; } = 220;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int ZocRadius { get; init; } = 4;
    public int PullRadius { get; init; } = 4;
    public int PullCooldown { get; init; } = 140;
}

public record JumperConfig
{
    public int Hp { get; init; } = 3;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 2;
    public int SpawnTicks { get; init; } = 300;
    public int RootTicks { get; init; } = 36;
    public int UprootTicks { get; init; } = 24;
    public int JumpRange { get; init; } = 5;
    public int JumpCooldown { get; init; } = 120;
}

public record WallConfig
{
    public int PopCost { get; init; } = 0;
}

public record NuggetConfig
{
    public int MiningTicks { get; init; } = 180;
    public int MoveInterval { get; init; } = 3;
    public int PopCost { get; init; } = 0;
    public int RefineRadius { get; init; } = 2;
    public int FortifiedWallHp { get; init; } = 3;
    public int FortifiedWallCount { get; init; } = 5;
    public int BuilderSpawnBonus { get; init; } = 100;
    public int SoldierSpawnBonus { get; init; } = 100;
    public int StunnerSpawnBonus { get; init; } = 100;
    public int WardenSpawnBonus { get; init; } = 100;
    public int JumperSpawnBonus { get; init; } = 100;
    public bool BuilderRequired { get; init; } = false;
    public bool SoldierRequired { get; init; } = false;
    public bool StunnerRequired { get; init; } = false;
    public bool WardenRequired { get; init; } = false;
    public bool JumperRequired { get; init; } = false;
}

public record EconomyConfig
{
    public int SupplyPopCap { get; init; } = 7;
    public int SupplyMembers { get; init; } = 3;
    public int ProtoSpawnMultiplier { get; init; } = 5;
}

public record CombatConfig
{
    public int FragileWallSoldierThreshold { get; init; } = 2;
    public int MoveGiveUpTicks { get; init; } = 12; // Real ticks (~1 second at 12 tps)
    public int SurroundKillMaxArea { get; init; } = 36;
    public int SurroundKillDelay { get; init; } = 60;
}

public record TowerConfig
{
    public int StunTowerFireInterval { get; init; } = 16;
    public int StunTowerRange { get; init; } = 4;
    public int StunTowerRayAdvanceInterval { get; init; } = 2;
    public int SoldierTowerFireInterval { get; init; } = 12;
    public int SoldierTowerRange { get; init; } = 5;
    public int BlastTowerRayAdvanceInterval { get; init; } = 2;
}

public record PushConfig
{
    public int WaveInterval { get; init; } = 8;
    public int Range { get; init; } = 4;
    public int Knockback { get; init; } = 3;
    public int WaveAdvanceInterval { get; init; } = 1;
    public int WaveFade { get; init; } = 6;
}

public record RayConfig
{
    public int BlastUnitRayAdvanceInterval { get; init; } = 1;
    public int StunRayFade { get; init; } = 8;
}

public record GridConfig
{
    public int DefaultWidth { get; init; } = 41;
    public int DefaultHeight { get; init; } = 25;
}

public record VisionConfig
{
    public bool FogOfWarEnabled { get; init; } = true;
    public int BuilderLosRadius { get; init; } = 5;
    public int SoldierLosRadius { get; init; } = 3;
    public int StunnerLosRadius { get; init; } = 2;
    public int WardenLosRadius { get; init; } = 5;
    public int JumperLosRadius { get; init; } = 3;
    public int NuggetLosRadius { get; init; } = 1;
    public int WallLosRadius { get; init; } = 2;
    public int NestLosRadius { get; init; } = 2;
    public int TowerLosRadius { get; init; } = 2;
    public int FormationLosRadius { get; init; } = 2;
}

public record SimulationConfig
{
    public BuilderConfig Builder { get; init; } = new();
    public SoldierConfig Soldier { get; init; } = new();
    public StunnerConfig Stunner { get; init; } = new();
    public WardenConfig Warden { get; init; } = new();
    public JumperConfig Jumper { get; init; } = new();
    public WallConfig Wall { get; init; } = new();
    public NuggetConfig Nugget { get; init; } = new();
    public EconomyConfig Economy { get; init; } = new();
    public CombatConfig Combat { get; init; } = new();
    public TowerConfig Tower { get; init; } = new();
    public PushConfig Push { get; init; } = new();
    public RayConfig Ray { get; init; } = new();
    public GridConfig Grid { get; init; } = new();
    public int DeathEffectTicks { get; init; } = 10;
    public int TeardownTicks { get; init; } = 24;
    public VisionConfig Vision { get; init; } = new();

    public int GetRootTicks(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.RootTicks,
        Blocks.BlockType.Soldier => Soldier.RootTicks,
        Blocks.BlockType.Stunner => Stunner.RootTicks,
        Blocks.BlockType.Warden => Warden.RootTicks,
        _ => Builder.RootTicks
    };

    public int GetUprootTicks(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.UprootTicks,
        Blocks.BlockType.Soldier => Soldier.UprootTicks,
        Blocks.BlockType.Stunner => Stunner.UprootTicks,
        Blocks.BlockType.Warden => Warden.UprootTicks,
        _ => Builder.UprootTicks
    };

    public int GetMoveInterval(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Soldier => Soldier.MoveInterval,
        Blocks.BlockType.Stunner => Stunner.MoveInterval,
        Blocks.BlockType.Warden => Warden.MoveInterval,
        Blocks.BlockType.Jumper => Jumper.MoveInterval,
        Blocks.BlockType.Nugget => Nugget.MoveInterval,
        _ => Builder.MoveInterval
    };

    public int GetPopCost(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Builder => Builder.PopCost,
        Blocks.BlockType.Wall => Wall.PopCost,
        Blocks.BlockType.Soldier => Soldier.PopCost,
        Blocks.BlockType.Stunner => Stunner.PopCost,
        Blocks.BlockType.Warden => Warden.PopCost,
        Blocks.BlockType.Jumper => Jumper.PopCost,
        Blocks.BlockType.Nugget => Nugget.PopCost,
        _ => 0
    };

    public int GetMaxHp(Blocker.Simulation.Blocks.BlockType type) => type switch
    {
        Blocks.BlockType.Soldier => Soldier.Hp,
        Blocks.BlockType.Jumper => Jumper.Hp,
        _ => 1
    };

    public int GetSpawnTicks(NestType nestType, GroundType ground)
    {
        int baseTicks = nestType switch
        {
            NestType.Builder => ground == GroundType.Overload
                ? Warden.SpawnTicks : Builder.SpawnTicks,
            NestType.Soldier => ground == GroundType.Overload
                ? Jumper.SpawnTicks : Soldier.SpawnTicks,
            NestType.Stunner => Stunner.SpawnTicks,
            _ => Builder.SpawnTicks
        };
        if (ground == GroundType.Proto)
            baseTicks *= Economy.ProtoSpawnMultiplier;
        return baseTicks;
    }

    public int GetNuggetSpawnBonus(Blocks.BlockType unitType) => unitType switch
    {
        Blocks.BlockType.Builder => Nugget.BuilderSpawnBonus,
        Blocks.BlockType.Soldier => Nugget.SoldierSpawnBonus,
        Blocks.BlockType.Stunner => Nugget.StunnerSpawnBonus,
        Blocks.BlockType.Warden => Nugget.WardenSpawnBonus,
        Blocks.BlockType.Jumper => Nugget.JumperSpawnBonus,
        _ => 0
    };

    public bool GetNuggetRequired(Blocks.BlockType unitType) => unitType switch
    {
        Blocks.BlockType.Builder => Nugget.BuilderRequired,
        Blocks.BlockType.Soldier => Nugget.SoldierRequired,
        Blocks.BlockType.Stunner => Nugget.StunnerRequired,
        Blocks.BlockType.Warden => Nugget.WardenRequired,
        Blocks.BlockType.Jumper => Nugget.JumperRequired,
        _ => false
    };
}
