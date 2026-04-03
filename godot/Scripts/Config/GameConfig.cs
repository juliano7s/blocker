using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Config;

[GlobalClass]
public partial class GameConfig : Resource
{
    // ========== BALANCE: Per-Unit ==========

    [ExportGroup("Builder")]
    [Export] public int BuilderHp { get; set; } = 1;
    [Export] public int BuilderMoveInterval { get; set; } = 3;
    [Export] public int BuilderPopCost { get; set; } = 1;
    [Export] public int BuilderSpawnTicks { get; set; } = 140;
    [Export] public int BuilderRootTicks { get; set; } = 36;
    [Export] public int BuilderUprootTicks { get; set; } = 24;

    [ExportGroup("Soldier")]
    [Export] public int SoldierHp { get; set; } = 4;
    [Export] public int SoldierMoveInterval { get; set; } = 4;
    [Export] public int SoldierPopCost { get; set; } = 1;
    [Export] public int SoldierSpawnTicks { get; set; } = 260;
    [Export] public int SoldierRootTicks { get; set; } = 36;
    [Export] public int SoldierUprootTicks { get; set; } = 24;
    [Export] public int SoldierExplodeRange { get; set; } = 3;

    [ExportGroup("Stunner")]
    [Export] public int StunnerHp { get; set; } = 1;
    [Export] public int StunnerMoveInterval { get; set; } = 2;
    [Export] public int StunnerPopCost { get; set; } = 3;
    [Export] public int StunnerSpawnTicks { get; set; } = 360;
    [Export] public int StunnerRootTicks { get; set; } = 36;
    [Export] public int StunnerUprootTicks { get; set; } = 24;
    [Export] public int StunnerStunDuration { get; set; } = 160;
    [Export] public int StunnerStunCooldown { get; set; } = 140;
    [Export] public int StunnerStunRange { get; set; } = 5;
    [Export] public int StunnerRayAdvanceInterval { get; set; } = 2;

    [ExportGroup("Warden")]
    [Export] public int WardenHp { get; set; } = 1;
    [Export] public int WardenMoveInterval { get; set; } = 3;
    [Export] public int WardenPopCost { get; set; } = 2;
    [Export] public int WardenSpawnTicks { get; set; } = 220;
    [Export] public int WardenRootTicks { get; set; } = 36;
    [Export] public int WardenUprootTicks { get; set; } = 24;
    [Export] public int WardenZocRadius { get; set; } = 4;
    [Export] public int WardenPullRadius { get; set; } = 4;
    [Export] public int WardenPullCooldown { get; set; } = 140;

    [ExportGroup("Jumper")]
    [Export] public int JumperHp { get; set; } = 3;
    [Export] public int JumperMoveInterval { get; set; } = 3;
    [Export] public int JumperPopCost { get; set; } = 2;
    [Export] public int JumperSpawnTicks { get; set; } = 300;
    [Export] public int JumperRootTicks { get; set; } = 36;
    [Export] public int JumperUprootTicks { get; set; } = 24;
    [Export] public int JumperJumpRange { get; set; } = 5;
    [Export] public int JumperJumpCooldown { get; set; } = 120;

    [ExportGroup("Wall")]
    [Export] public int WallPopCost { get; set; } = 0;

    // ========== BALANCE: Global ==========

    [ExportGroup("Economy")]
    [Export] public int SupplyPopCap { get; set; } = 7;
    [Export] public int SupplyMembers { get; set; } = 3;
    [Export] public int ProtoSpawnMultiplier { get; set; } = 5;

    [ExportGroup("Combat")]
    [Export] public int FragileWallSoldierThreshold { get; set; } = 2;
    [Export] public int MoveGiveUpTicks { get; set; } = 30;

    [ExportGroup("Towers")]
    [Export] public int StunTowerFireInterval { get; set; } = 16;
    [Export] public int StunTowerRange { get; set; } = 4;
    [Export] public int StunTowerRayAdvanceInterval { get; set; } = 2;
    [Export] public int SoldierTowerFireInterval { get; set; } = 12;
    [Export] public int SoldierTowerRange { get; set; } = 5;
    [Export] public int BlastTowerRayAdvanceInterval { get; set; } = 1;

    [ExportGroup("Push")]
    [Export] public int PushWaveInterval { get; set; } = 8;
    [Export] public int PushRange { get; set; } = 4;
    [Export] public int PushKnockback { get; set; } = 3;
    [Export] public int PushWaveAdvanceInterval { get; set; } = 1;
    [Export] public int PushWaveFade { get; set; } = 6;

    [ExportGroup("Rays")]
    [Export] public int BlastUnitRayAdvanceInterval { get; set; } = 1;
    [Export] public int StunRayFade { get; set; } = 8;

    [ExportGroup("Timing")]
    [Export] public int DeathEffectTicks { get; set; } = 10;
    [Export] public int TeardownTicks { get; set; } = 24;

    // ========== VISUAL: Grid ==========

    [ExportGroup("Grid")]
    [Export] public int GridDefaultWidth { get; set; } = 41;
    [Export] public int GridDefaultHeight { get; set; } = 25;
    [Export] public float CellSize { get; set; } = 28f;
    [Export] public float GridLineWidth { get; set; } = 1f;
    [Export] public float BlockInset { get; set; } = 2f;
    [Export] public float BlockGlowSize { get; set; } = 2f;

    [ExportGroup("Grid Colors")]
    [Export] public Color GridLineColor { get; set; } = new(0.12f, 0.13f, 0.22f, 0.35f);
    [Export] public Color NormalGroundColor { get; set; } = new(0.05f, 0.06f, 0.12f);
    [Export] public Color BootGroundColor { get; set; } = new(0.06f, 0.14f, 0.08f);
    [Export] public Color OverloadGroundColor { get; set; } = new(0.12f, 0.06f, 0.18f);
    [Export] public Color ProtoGroundColor { get; set; } = new(0.07f, 0.08f, 0.14f);
    [Export] public Color TerrainGroundColor { get; set; } = new(0.18f, 0.18f, 0.20f);
    [Export] public Color BreakableWallGroundColor { get; set; } = new(0.22f, 0.20f, 0.18f);
    [Export] public Color FragileWallGroundColor { get; set; } = new(0.18f, 0.16f, 0.14f);
    [Export] public Color BackgroundColor { get; set; } = new(0.08f, 0.08f, 0.10f);

    // ========== VISUAL: Global Colors ==========

    [ExportGroup("Global Colors")]
    [Export] public Color StunRayColor { get; set; } = new(0.3f, 0.5f, 1f);
    [Export] public Color BlastRayColor { get; set; } = new(1f, 0.5f, 0.2f);
    [Export] public Color FrozenOverlayColor { get; set; } = new(0.55f, 0.78f, 1f);
    [Export] public Color FrozenBorderColor { get; set; } = new(0.55f, 0.82f, 1f);
    [Export] public Color FrostCrackColor { get; set; } = new(0.7f, 0.9f, 1f, 0.3f);
    [Export] public Color ThreatIndicatorColor { get; set; } = new(1f, 0.15f, 0.1f);
    [Export] public Color SelectionBorderColor { get; set; } = new(1f, 1f, 1f, 0.8f);

    // ========== VISUAL: Effects ==========

    [ExportGroup("Visual Effects")]
    [Export] public int DeathFragmentCount { get; set; } = 28;
    [Export] public float FragmentLifetime { get; set; } = 0.8f;
    [Export] public int WardenZocWaveCycleMs { get; set; } = 2500;
    [Export] public float SelectionDashLength { get; set; } = 4f;
    [Export] public float SelectionGapLength { get; set; } = 3f;

    // ========== PLAYERS ==========

    [ExportGroup("Players")]
    [Export] public PlayerPalette[] PlayerPalettes { get; set; } = [];

    public SimulationConfig ToSimulationConfig() => new()
    {
        Builder = new BuilderConfig
        {
            Hp = BuilderHp, MoveInterval = BuilderMoveInterval,
            PopCost = BuilderPopCost, SpawnTicks = BuilderSpawnTicks,
            RootTicks = BuilderRootTicks, UprootTicks = BuilderUprootTicks,
        },
        Soldier = new SoldierConfig
        {
            Hp = SoldierHp, MoveInterval = SoldierMoveInterval,
            PopCost = SoldierPopCost, SpawnTicks = SoldierSpawnTicks,
            RootTicks = SoldierRootTicks, UprootTicks = SoldierUprootTicks,
            ExplodeRange = SoldierExplodeRange,
        },
        Stunner = new StunnerConfig
        {
            Hp = StunnerHp, MoveInterval = StunnerMoveInterval,
            PopCost = StunnerPopCost, SpawnTicks = StunnerSpawnTicks,
            RootTicks = StunnerRootTicks, UprootTicks = StunnerUprootTicks,
            StunDuration = StunnerStunDuration, StunCooldown = StunnerStunCooldown,
            StunRange = StunnerStunRange, UnitRayAdvanceInterval = StunnerRayAdvanceInterval,
        },
        Warden = new WardenConfig
        {
            Hp = WardenHp, MoveInterval = WardenMoveInterval,
            PopCost = WardenPopCost, SpawnTicks = WardenSpawnTicks,
            RootTicks = WardenRootTicks, UprootTicks = WardenUprootTicks,
            ZocRadius = WardenZocRadius, PullRadius = WardenPullRadius,
            PullCooldown = WardenPullCooldown,
        },
        Jumper = new JumperConfig
        {
            Hp = JumperHp, MoveInterval = JumperMoveInterval,
            PopCost = JumperPopCost, SpawnTicks = JumperSpawnTicks,
            RootTicks = JumperRootTicks, UprootTicks = JumperUprootTicks,
            JumpRange = JumperJumpRange, JumpCooldown = JumperJumpCooldown,
        },
        Wall = new WallConfig { PopCost = WallPopCost },
        Economy = new EconomyConfig
        {
            SupplyPopCap = SupplyPopCap, SupplyMembers = SupplyMembers,
            ProtoSpawnMultiplier = ProtoSpawnMultiplier,
        },
        Combat = new CombatConfig
        {
            FragileWallSoldierThreshold = FragileWallSoldierThreshold,
            MoveGiveUpTicks = MoveGiveUpTicks,
        },
        Tower = new TowerConfig
        {
            StunTowerFireInterval = StunTowerFireInterval, StunTowerRange = StunTowerRange,
            StunTowerRayAdvanceInterval = StunTowerRayAdvanceInterval,
            SoldierTowerFireInterval = SoldierTowerFireInterval, SoldierTowerRange = SoldierTowerRange,
            BlastTowerRayAdvanceInterval = BlastTowerRayAdvanceInterval,
        },
        Push = new PushConfig
        {
            WaveInterval = PushWaveInterval, Range = PushRange,
            Knockback = PushKnockback, WaveAdvanceInterval = PushWaveAdvanceInterval,
            WaveFade = PushWaveFade,
        },
        Ray = new RayConfig
        {
            BlastUnitRayAdvanceInterval = BlastUnitRayAdvanceInterval,
            StunRayFade = StunRayFade,
        },
        Grid = new GridConfig
        {
            DefaultWidth = GridDefaultWidth, DefaultHeight = GridDefaultHeight,
        },
        DeathEffectTicks = DeathEffectTicks,
        TeardownTicks = TeardownTicks,
    };

    public PlayerPalette GetPalette(int playerId)
    {
        if (PlayerPalettes != null && playerId >= 0 && playerId < PlayerPalettes.Length
            && PlayerPalettes[playerId] != null)
            return PlayerPalettes[playerId];
        return _defaultPalette;
    }

    public Color GetGroundColor(GroundType ground) => ground switch
    {
        GroundType.Boot => BootGroundColor,
        GroundType.Overload => OverloadGroundColor,
        GroundType.Proto => ProtoGroundColor,
        GroundType.Terrain => TerrainGroundColor,
        GroundType.BreakableWall => BreakableWallGroundColor,
        GroundType.FragileWall => FragileWallGroundColor,
        _ => NormalGroundColor
    };

    private static readonly PlayerPalette _defaultPalette = PlayerPalette.FromBase(Colors.White);

    public static GameConfig CreateDefault()
    {
        var config = new GameConfig();
        config.PlayerPalettes =
        [
            PlayerPalette.FromBase(new Color(0.25f, 0.55f, 1.0f)),
            PlayerPalette.FromBase(new Color(0.95f, 0.25f, 0.2f)),
            PlayerPalette.FromBase(new Color(0.95f, 0.85f, 0.2f)),
            PlayerPalette.FromBase(new Color(0.2f, 0.85f, 0.35f)),
            PlayerPalette.FromBase(new Color(0.9f, 0.45f, 0.1f)),
            PlayerPalette.FromBase(new Color(0.65f, 0.25f, 0.9f)),
        ];
        return config;
    }
}
