using Godot;

namespace Blocker.Game.Audio;

/// <summary>
/// Configurable audio mapping resource. Edit in Godot inspector to assign
/// .ogg files to each sound event. Leave null for events you want silent.
/// Game bible Section 17.1.
/// </summary>
[GlobalClass]
public partial class AudioConfig : Resource
{
    [ExportGroup("Master")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float MasterVolume { get; set; } = 1.0f;

    [ExportGroup("Selection")]
    [Export] public AudioStream? SelectBuilder { get; set; }
    [Export] public AudioStream? SelectSoldier { get; set; }
    [Export] public AudioStream? SelectStunner { get; set; }
    [Export] public AudioStream? SelectWarden { get; set; }
    [Export] public AudioStream? SelectJumper { get; set; }
    [Export] public AudioStream? SelectWall { get; set; }

    [ExportGroup("Commands")]
    [Export] public AudioStream? MoveCommand { get; set; }
    [Export] public AudioStream? RootStart { get; set; }
    [Export] public AudioStream? UprootStart { get; set; }
    [Export] public AudioStream? WallConvert { get; set; }

    [ExportGroup("Spawning")]
    [Export] public AudioStream? SpawnBuilder { get; set; }
    [Export] public AudioStream? SpawnSoldier { get; set; }
    [Export] public AudioStream? SpawnStunner { get; set; }
    [Export] public AudioStream? SpawnWarden { get; set; }
    [Export] public AudioStream? SpawnJumper { get; set; }

    [ExportGroup("Block Lifecycle")]
    [Export] public AudioStream? RootComplete { get; set; }
    [Export] public AudioStream? UprootComplete { get; set; }
    [Export] public AudioStream? Death { get; set; }

    [ExportGroup("Formations: Formed")]
    [Export] public AudioStream? BuilderNestFormed { get; set; }
    [Export] public AudioStream? SoldierNestFormed { get; set; }
    [Export] public AudioStream? StunnerNestFormed { get; set; }
    [Export] public AudioStream? SupplyFormed { get; set; }
    [Export] public AudioStream? StunTowerFormed { get; set; }
    [Export] public AudioStream? SoldierTowerFormed { get; set; }

    [ExportGroup("Formations: Dissolved")]
    [Export] public AudioStream? BuilderNestDissolved { get; set; }
    [Export] public AudioStream? SoldierNestDissolved { get; set; }
    [Export] public AudioStream? StunnerNestDissolved { get; set; }
    [Export] public AudioStream? SupplyDissolved { get; set; }
    [Export] public AudioStream? StunTowerDissolved { get; set; }
    [Export] public AudioStream? SoldierTowerDissolved { get; set; }

    [ExportGroup("Combat")]
    [Export] public AudioStream? StunFire { get; set; }
    [Export] public AudioStream? StunHit { get; set; }
    [Export] public AudioStream? BlastFire { get; set; }
    [Export] public AudioStream? PushWave { get; set; }
    [Export] public AudioStream? SelfDestruct { get; set; }
    [Export] public AudioStream? TowerFire { get; set; }

    [ExportGroup("Soldier Mechanics")]
    [Export] public AudioStream? SoldierCombo { get; set; }
    [Export] public AudioStream? SoldierArmLost { get; set; }

    [ExportGroup("Surround")]
    [Export] public AudioStream? SurroundTrapped { get; set; }
    [Export] public AudioStream? SurroundKilled { get; set; }

    [ExportGroup("Abilities")]
    [Export] public AudioStream? JumpLaunch { get; set; }
    [Export] public AudioStream? JumpLand { get; set; }
    [Export] public AudioStream? MagnetPull { get; set; }

    [ExportGroup("Nuggets")]
    [Export] public AudioStream? NuggetMineCommand { get; set; }
    [Export] public AudioStream? NuggetHealCommand { get; set; }
    [Export] public AudioStream? NuggetFortifyCommand { get; set; }
    [Export] public AudioStream? NuggetMiningStarted { get; set; }
    [Export] public AudioStream? NuggetFreed { get; set; }
    [Export] public AudioStream? NuggetCaptured { get; set; }
    [Export] public AudioStream? NuggetRefineConsumed { get; set; }
    [Export] public AudioStream? NuggetHealConsumed { get; set; }
    [Export] public AudioStream? NuggetFortifyConsumed { get; set; }

    [ExportGroup("UI: Buttons")]
    [Export] public AudioStream? UIClick { get; set; }
    [Export] public AudioStream? UIHover { get; set; }
    [Export] public AudioStream? UIStartGame { get; set; }

    [ExportGroup("UI: HUD")]
    [Export] public AudioStream? UIToggleOn { get; set; }
    [Export] public AudioStream? UIToggleOff { get; set; }
    [Export] public AudioStream? UICommandClick { get; set; }
    [Export] public AudioStream? UIBlueprintClick { get; set; }

    [ExportGroup("UI: Chat")]
    [Export] public AudioStream? UIChatSend { get; set; }
    [Export] public AudioStream? UIChatReceive { get; set; }

    [ExportGroup("UI: Feedback")]
    [Export] public AudioStream? UIError { get; set; }
    [Export] public AudioStream? UIReadyUp { get; set; }
    [Export] public AudioStream? UIUnready { get; set; }
    [Export] public AudioStream? UISurrender { get; set; }

    [ExportGroup("Game State")]
    [Export] public AudioStream? PlayerEliminated { get; set; }
    [Export] public AudioStream? PopCapWarning { get; set; }
    [Export] public AudioStream? Win { get; set; }
    [Export] public AudioStream? Loss { get; set; }
}
