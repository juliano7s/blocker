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
    [ExportCategory("Selection (per unit type)")]
    [Export] public AudioStream? SelectBuilder { get; set; }
    [Export] public AudioStream? SelectSoldier { get; set; }
    [Export] public AudioStream? SelectStunner { get; set; }
    [Export] public AudioStream? SelectWarden { get; set; }
    [Export] public AudioStream? SelectJumper { get; set; }
    [Export] public AudioStream? SelectWall { get; set; }

    [ExportCategory("Command Sounds (Private - local player only)")]
    [Export] public AudioStream? MoveCommand { get; set; }
    [Export] public AudioStream? RootStart { get; set; }
    [Export] public AudioStream? UprootStart { get; set; }

    [ExportCategory("Block State Transitions")]
    [Export] public AudioStream? RootComplete { get; set; }
    [Export] public AudioStream? UprootComplete { get; set; }
    [Export] public AudioStream? WallConvert { get; set; }
    [Export] public AudioStream? Death { get; set; }

    [ExportCategory("Spawns (per unit type)")]
    [Export] public AudioStream? SpawnBuilder { get; set; }
    [Export] public AudioStream? SpawnSoldier { get; set; }
    [Export] public AudioStream? SpawnStunner { get; set; }
    [Export] public AudioStream? SpawnWarden { get; set; }
    [Export] public AudioStream? SpawnJumper { get; set; }

    [ExportCategory("Formations Formed (per type)")]
    [Export] public AudioStream? BuilderNestFormed { get; set; }
    [Export] public AudioStream? SoldierNestFormed { get; set; }
    [Export] public AudioStream? StunnerNestFormed { get; set; }
    [Export] public AudioStream? SupplyFormed { get; set; }
    [Export] public AudioStream? StunTowerFormed { get; set; }
    [Export] public AudioStream? SoldierTowerFormed { get; set; }

    [ExportCategory("Formations Dissolved (per type)")]
    [Export] public AudioStream? BuilderNestDissolved { get; set; }
    [Export] public AudioStream? SoldierNestDissolved { get; set; }
    [Export] public AudioStream? StunnerNestDissolved { get; set; }
    [Export] public AudioStream? SupplyDissolved { get; set; }
    [Export] public AudioStream? StunTowerDissolved { get; set; }
    [Export] public AudioStream? SoldierTowerDissolved { get; set; }

    [ExportCategory("Combat")]
    [Export] public AudioStream? StunFire { get; set; }
    [Export] public AudioStream? StunHit { get; set; }
    [Export] public AudioStream? BlastFire { get; set; }
    [Export] public AudioStream? PushWave { get; set; }
    [Export] public AudioStream? SelfDestruct { get; set; }
    [Export] public AudioStream? TowerFire { get; set; }

    [ExportCategory("Special Abilities")]
    [Export] public AudioStream? JumpLaunch { get; set; }
    [Export] public AudioStream? JumpLand { get; set; }
    [Export] public AudioStream? MagnetPull { get; set; }

    [ExportCategory("Game Events")]
    [Export] public AudioStream? PlayerEliminated { get; set; }
    [Export] public AudioStream? PopCapWarning { get; set; }
    [Export] public AudioStream? Win { get; set; }
    [Export] public AudioStream? Loss { get; set; }

    [ExportCategory("Master Volume")]
    [Export(PropertyHint.Range, "0,1,0.05")] public float MasterVolume { get; set; } = 1.0f;
}
