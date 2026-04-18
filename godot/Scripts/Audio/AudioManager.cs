using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Audio;

/// <summary>
/// Consumes VisualEvents from the simulation and plays sound effects.
/// Audio files are configured via AudioConfig resource in the inspector.
/// Missing/null streams are silently skipped.
/// Game bible Section 17.1.
/// </summary>
public partial class AudioManager : Node
{
    [Export] public AudioConfig? Config { get; set; }

    private GameState? _gameState;
    private int _controllingPlayer;

    // Audio stream pool for overlapping sounds
    private const int PoolSize = 16;
    private readonly AudioStreamPlayer[] _pool = new AudioStreamPlayer[PoolSize];
    private int _poolIndex;

    // Private events only play for the local player (same logic as EffectManager)
    private static readonly HashSet<VisualEventType> PrivateEvents = new()
    {
        VisualEventType.CommandMoveIssued,
        VisualEventType.CommandRootIssued,
        VisualEventType.CommandUprootIssued,
        VisualEventType.CommandWallIssued,
        VisualEventType.WallConverted,
        // Formation events are private
        VisualEventType.BuilderNestFormed,
        VisualEventType.SoldierNestFormed,
        VisualEventType.StunnerNestFormed,
        VisualEventType.SupplyFormed,
        VisualEventType.StunTowerFormed,
        VisualEventType.SoldierTowerFormed,
        VisualEventType.BuilderNestDissolved,
        VisualEventType.SoldierNestDissolved,
        VisualEventType.StunnerNestDissolved,
        VisualEventType.SupplyDissolved,
        VisualEventType.StunTowerDissolved,
        VisualEventType.SoldierTowerDissolved,
        VisualEventType.BlockSpawned,
        VisualEventType.BlockRooted,
        VisualEventType.BlockUprooted,
    };

    // Selection tracking - fire sound on newly selected blocks
    private readonly HashSet<int> _prevSelectedIds = new();

    // Track last processed tick to avoid playing sounds multiple times per tick
    // (VisualEvents persist until next tick clears them, but _Process runs every frame)
    private int _lastProcessedTick = -1;

    public void SetGameState(GameState state) => _gameState = state;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

    public override void _Ready()
    {
        // Create audio player pool
        for (int i = 0; i < PoolSize; i++)
        {
            var player = new AudioStreamPlayer { Bus = "Master" };
            AddChild(player);
            _pool[i] = player;
        }

        GD.Print($"[AudioManager] Ready");
    }

    public override void _Process(double delta)
    {
        if (_gameState == null) return;

        // Only process events once per tick (events persist until next tick clears them)
        if (_gameState.TickNumber == _lastProcessedTick) return;
        _lastProcessedTick = _gameState.TickNumber;

        // Consume visual events and play sounds
        foreach (var evt in _gameState.VisualEvents)
            PlayEventSound(evt);
    }

    /// <summary>
    /// Called by GameManager each frame with current selected block IDs.
    /// Plays selection sound based on unit type of newly selected blocks.
    /// </summary>
    public void OnSelectionChanged(HashSet<int> currentSelectedIds)
    {
        if (_gameState == null || Config == null) return;

        // Find first newly selected block owned by local player
        Block? newlySelected = null;
        foreach (var id in currentSelectedIds)
        {
            if (_prevSelectedIds.Contains(id)) continue;

            var block = _gameState.GetBlock(id);
            if (block == null || block.PlayerId != _controllingPlayer) continue;

            newlySelected = block;
            break;
        }

        if (newlySelected != null)
        {
            var stream = newlySelected.Type switch
            {
                BlockType.Builder => Config.SelectBuilder,
                BlockType.Soldier => Config.SelectSoldier,
                BlockType.Stunner => Config.SelectStunner,
                BlockType.Warden => Config.SelectWarden,
                BlockType.Jumper => Config.SelectJumper,
                BlockType.Wall => Config.SelectWall,
                _ => null
            };
            Play(stream);
        }

        _prevSelectedIds.Clear();
        foreach (var id in currentSelectedIds)
            _prevSelectedIds.Add(id);
    }

    private void PlayEventSound(VisualEvent evt)
    {
        if (Config == null) return;

        // Private events only play for the local player
        if (PrivateEvents.Contains(evt.Type))
        {
            if (!evt.PlayerId.HasValue || evt.PlayerId.Value != _controllingPlayer)
                return;
        }

        // Handle spawn events with per-type audio
        if (evt.Type == VisualEventType.BlockSpawned)
        {
            PlaySpawnSound(evt);
            return;
        }

        // Map event type to configured audio stream
        var stream = evt.Type switch
        {
            // Commands
            VisualEventType.CommandMoveIssued => Config.MoveCommand,
            VisualEventType.CommandRootIssued => Config.RootStart,
            VisualEventType.CommandUprootIssued => Config.UprootStart,
            VisualEventType.CommandWallIssued => Config.WallConvert,

            // Block state transitions
            VisualEventType.BlockRooted => Config.RootComplete,
            VisualEventType.BlockUprooted => Config.UprootComplete,
            VisualEventType.WallConverted => Config.WallConvert,
            VisualEventType.WallDamaged => Config.WallConvert,
            VisualEventType.WallDestroyed => Config.WallConvert,
            VisualEventType.BlockDied => Config.Death,

            // Formations formed
            VisualEventType.BuilderNestFormed => Config.BuilderNestFormed,
            VisualEventType.SoldierNestFormed => Config.SoldierNestFormed,
            VisualEventType.StunnerNestFormed => Config.StunnerNestFormed,
            VisualEventType.SupplyFormed => Config.SupplyFormed,
            VisualEventType.StunTowerFormed => Config.StunTowerFormed,
            VisualEventType.SoldierTowerFormed => Config.SoldierTowerFormed,

            // Formations dissolved
            VisualEventType.BuilderNestDissolved => Config.BuilderNestDissolved,
            VisualEventType.SoldierNestDissolved => Config.SoldierNestDissolved,
            VisualEventType.StunnerNestDissolved => Config.StunnerNestDissolved,
            VisualEventType.SupplyDissolved => Config.SupplyDissolved,
            VisualEventType.StunTowerDissolved => Config.StunTowerDissolved,
            VisualEventType.SoldierTowerDissolved => Config.SoldierTowerDissolved,

            // Combat
            VisualEventType.StunRayFired => Config.StunFire,
            VisualEventType.StunRayHit => Config.StunHit,
            VisualEventType.BlastRayFired => Config.BlastFire,
            VisualEventType.PushWaveFired => Config.PushWave,
            VisualEventType.SelfDestructed => Config.SelfDestruct,
            VisualEventType.TowerFired => Config.TowerFire,

            // Abilities
            VisualEventType.JumpExecuted => Config.JumpLaunch,
            VisualEventType.JumpLanded => Config.JumpLand,
            VisualEventType.MagnetPulled => Config.MagnetPull,

            // Game events
            VisualEventType.PlayerEliminated => Config.PlayerEliminated,
            VisualEventType.PopCapWarning => Config.PopCapWarning,

            _ => null
        };

        Play(stream);
    }

    private void PlaySpawnSound(VisualEvent evt)
    {
        if (Config == null || _gameState == null) return;
        if (!evt.BlockId.HasValue) return;

        var block = _gameState.GetBlock(evt.BlockId.Value);
        if (block == null) return;

        var stream = block.Type switch
        {
            BlockType.Builder => Config.SpawnBuilder,
            BlockType.Soldier => Config.SpawnSoldier,
            BlockType.Stunner => Config.SpawnStunner,
            BlockType.Warden => Config.SpawnWarden,
            BlockType.Jumper => Config.SpawnJumper,
            _ => null
        };

        Play(stream);
    }

    /// <summary>
    /// Play win or loss fanfare. Call from GameManager based on local player outcome.
    /// </summary>
    public void PlayGameEndFanfare(bool localPlayerWon)
    {
        Play(localPlayerWon ? Config?.Win : Config?.Loss);
    }

    private void Play(AudioStream? stream)
    {
        if (stream == null) return;

        // Find next player in pool
        var player = _pool[_poolIndex];
        _poolIndex = (_poolIndex + 1) % PoolSize;

        player.Stream = stream;
        player.VolumeDb = Mathf.LinearToDb(Config?.MasterVolume ?? 1.0f);
        player.Play();
    }
}
