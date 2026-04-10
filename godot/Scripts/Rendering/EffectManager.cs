using Blocker.Game.Config;
using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Rendering;

/// <summary>
/// Consumes VisualEvents from the simulation and spawns GPU-accelerated visual effects.
/// Sits as a sibling of GridRenderer in the scene tree, sharing the same coordinate space.
/// Effects render on top of the grid via additive-blend shaders.
///
/// Two event categories:
///   - Command-issued (player clicks): move, root, uproot — fire once per command
///   - State transitions (simulation): death, spawn, formation, etc.
///
/// Effect parameters match game bible §16.5 Grid Lightning Effects table.
/// </summary>
public partial class EffectManager : Node2D
{
    private GameState? _gameState;
    private GameConfig? _config;
    private readonly List<GpuEffect> _effects = new();
    private const int MaxEffects = 60;

    // The local player — used to filter private vs global effects
    private int _controllingPlayer;

    // Selection tracking: detect newly selected blocks and fire one-shot effects
    private readonly HashSet<int> _prevSelectedIds = new();

    public void SetGameState(GameState state) => _gameState = state;
    public void SetConfig(GameConfig config) => _config = config;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

    public override void _Ready()
    {
        EffectFactory.Initialize();
    }

    public override void _Process(double delta)
    {
        if (_gameState == null) return;

        // 1. Consume new visual events → spawn effects
        foreach (var evt in _gameState.VisualEvents)
            SpawnEffects(evt);

        // 2. Update active effects (age + shader uniforms)
        float dtMs = (float)delta * 1000f;
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += dtMs;
            effect.Update();

            if (effect.Progress >= 1f && !effect.Looping)
            {
                effect.Destroy();
                _effects.RemoveAt(i);
            }
        }
    }

    // ─── Selection ───────────────────────────────────────────────────

    /// <summary>
    /// Called by GameManager each frame with current selected block IDs.
    /// Spawns SelectSquares on newly selected blocks — LOCAL PLAYER ONLY.
    /// Bible §16.5: Selection — 3 concentric squares, 350ms.
    /// </summary>
    public void OnSelectionChanged(HashSet<int> currentSelectedIds)
    {
        foreach (var id in currentSelectedIds)
        {
            if (_prevSelectedIds.Contains(id)) continue;

            // Newly selected — spawn effect (only for local player's blocks)
            if (_gameState == null) continue;
            var block = _gameState.GetBlock(id);
            if (block == null || block.PlayerId != _controllingPlayer) continue;

            var color = _config != null
                ? _config.GetPalette(block.PlayerId).Base
                : Colors.White;
            AddEffect(EffectFactory.SelectSquares(this, block.Pos, color));
        }

        _prevSelectedIds.Clear();
        foreach (var id in currentSelectedIds)
            _prevSelectedIds.Add(id);
    }

    // ─── Private vs Global ───────────────────────────────────────────

    /// <summary>
    /// Returns true for effects that should only be shown to the player
    /// who owns the affected units. Command-issued feedback (move trails,
    /// root bursts, uproot converges) is private — you don't see the
    /// enemy's input indicators.
    /// All state-transition events (deaths, spawns, combat, abilities)
    /// are global — everyone sees them.
    /// </summary>
    private static bool IsPrivateEvent(VisualEvent evt) => evt.Type switch
    {
        VisualEventType.CommandMoveIssued => true,
        VisualEventType.CommandRootIssued => true,
        VisualEventType.CommandUprootIssued => true,
        VisualEventType.FormationFormed => true,
        VisualEventType.WallConverted => true,
        VisualEventType.FormationDissolved => true,
        _ => false,
    };

    // ─── Event → Effect Mapping ─────────────────────────────────────
    // Parameters from game bible §16.5 Grid Lightning Effects table.

    private void SpawnEffects(VisualEvent evt)
    {
        var color = evt.PlayerId.HasValue && _config != null
            ? _config.GetPalette(evt.PlayerId.Value).Base
            : Colors.White;
        var pos = evt.Position;

        // ─── Private effects: local player only ─────────────────────
        // Command-issued feedback is private — you don't see enemy input indicators.
        if (IsPrivateEvent(evt))
        {
            // Only show if the event belongs to the local player
            if (!evt.PlayerId.HasValue || evt.PlayerId.Value != _controllingPlayer)
                return;
        }

        switch (evt.Type)
        {
            // ─── Command-issued effects (player clicks) ─────────────

            // Movement command: Backward trail, ~30 segs, 1200ms
            case VisualEventType.CommandMoveIssued:
                SpawnMoveCommandTrail(evt, color);
                break;

            // Root start: Outward from 4 edges, ~28 segs, 900ms
            case VisualEventType.CommandRootIssued:
                AddEffect(EffectFactory.LightningBurst(this, pos, color,
                    maxSegs: 18, duration: 900f, trail: 0.12f,
                    contProb: 0.78f, branchProb: 0.44f));
                break;

            // Uproot start: Same as root but fades outer-first, ~28 segs, 900ms
            case VisualEventType.CommandUprootIssued:
                AddEffect(EffectFactory.LightningConverge(this, pos, color,
                    maxSegs: 10, duration: 900f, trail: 0.12f));
                break;

            // ─── State transition effects ───────────────────────────

            // Death: Dashed tendrils exploding outward
            case VisualEventType.BlockDied:
                AddEffect(EffectFactory.DashedTendrils(this, pos, color,
                    duration: 800f));
                break;

            // Block spawn: type-specific (see SpawnBlockSpawnEffect)
            case VisualEventType.BlockSpawned:
                SpawnBlockSpawnEffect(pos, evt);
                break;

            // Root complete: Small cell perimeter confirmation
            case VisualEventType.BlockRooted:
                AddEffect(EffectFactory.CellPerimeter(this, pos, color,
                    duration: 600f, 0.10f));
                break;

            // Uproot complete: Small cross contracting inward, 4×3 segs, 600ms
            case VisualEventType.BlockUprooted:
                AddEffect(EffectFactory.CrossContract(this, pos, color,
                    duration: 600f, 1));
                break;

            // Wall convert: Short fast burst, ~14 segs, 450ms
            case VisualEventType.WallConverted:
                AddEffect(EffectFactory.LightningBurst(this, pos,
                    new Color(0.85f, 0.75f, 0.5f),
                    maxSegs: 14, duration: 450f, trail: 0.25f,
                    contProb: 0.66f, branchProb: 0.34f));
                break;

            // Formation complete: Clockwise spiral, ~40 segs, 1800ms
            case VisualEventType.FormationFormed:
                AddEffect(EffectFactory.SpiralTrace(this, pos, color,
                    duration: 1800f, maxSegs: 40));
                break;

            // Formation dissolved: Small cross contracting inward, 600ms
            case VisualEventType.FormationDissolved:
                AddEffect(EffectFactory.CrossContract(this, pos, Colors.White,
                    duration: 600f));
                break;

            // Jump: Core streak origin→landing (bible §16.9)
            case VisualEventType.JumpExecuted:
                SpawnJumpTrail(evt, color);
                break;

            // Self-destruct: Large explosion + shockwave
            case VisualEventType.SelfDestructed:
                AddEffect(EffectFactory.LightningBurst(this, pos, Color.Color8(200, 10, 10, 1),
                    maxSegs: 16, duration: 1000f, trail: 0.18f));
                break;

            // Stun ray hit: Dashed tendrils crackling into the target
            case VisualEventType.StunRayHit:
                AddEffect(EffectFactory.DashedTendrils(this, pos,
                    new Color(0.3f, 0.7f, 1f), duration: 800f,
                    tendrilCount: 6, minLen: 3, maxLen: 7));
                break;

            // Blast ray: Impact burst at blast position
            case VisualEventType.BlastRayFired:
                AddEffect(EffectFactory.CellPerimeter(this, pos,
                    new Color(1f, 0.5f, 0.2f), duration: 800f));
                break;

            // Push wave fire: Directional burst
            case VisualEventType.PushWaveFired:
                AddEffect(EffectFactory.LightningBurst(this, pos, color,
                    maxSegs: 20, duration: 800f, trail: 0.15f));
                break;

            // Magnet pull: Converging drain toward warden
            case VisualEventType.MagnetPulled:
                AddEffect(EffectFactory.LightningConverge(this, pos, color,
                    maxSegs: 28, duration: 700f, trail: 0.12f));
                break;

            // Player eliminated: Screen-wide shockwave
            case VisualEventType.PlayerEliminated:
                AddEffect(EffectFactory.SquareShockwave(this, pos, color,
                    maxRadius: 15, duration: 2000f));
                break;
        }
    }

    // ─── Move Command Trail ──────────────────────────────────────────
    // Bible: Backward trail from moving block, ~30 segs, 1200ms
    // Fires once when player issues move command, NOT on every cell step.

    private void SpawnMoveCommandTrail(VisualEvent evt, Color color)
    {
        if (!evt.BlockId.HasValue || _gameState == null) return;

        // Direction is toward the move target — trail goes opposite
        if (!evt.Direction.HasValue) return;
        var dir = evt.Direction.Value;
        var offset = dir.ToOffset();

        AddEffect(EffectFactory.LightningTrail(this, evt.Position, -offset.X, -offset.Y, color,
            duration: 500f, 5));
    }

    // ─── Jump Trail ──────────────────────────────────────────────────
    // Bible §16.9: Core streak from origin to landing, fading over 500ms + impact flash

    private void SpawnJumpTrail(VisualEvent evt, Color color)
    {
        if (!evt.BlockId.HasValue || _gameState == null) return;

        var jumper = _gameState.GetBlock(evt.BlockId.Value);
        if (jumper == null) return;

        var dir = evt.Direction;
        var range = evt.Range ?? 1;
        if (!dir.HasValue) return;

        var offset = dir.Value.ToOffset();
        // Reconstruct origin from landing position (PrevPos is already overwritten)
        var fromPos = jumper.Pos - new GridPos(offset.X * range, offset.Y * range);

        // Core streak: line from origin to landing
        AddEffect(EffectFactory.LineTrail(this, fromPos, jumper.Pos, color,
            duration: 500f));

        // Impact flash at landing: small burst
        AddEffect(EffectFactory.LightningBurst(this, jumper.Pos, color,
            maxSegs: 14, duration: 300f, trail: 0.15f));
    }

    // ─── Block Spawn ─────────────────────────────────────────────────

    private void SpawnBlockSpawnEffect(GridPos pos, VisualEvent evt)
    {
        var color = evt.PlayerId.HasValue && _config != null
            ? _config.GetPalette(evt.PlayerId.Value).Base
            : Colors.White;

        // Look up block type for type-specific spawn effects
        BlockType blockType = BlockType.Builder;
        if (evt.BlockId.HasValue && _gameState != null)
        {
            var block = _gameState.GetBlock(evt.BlockId.Value);
            if (block != null) blockType = block.Type;
        }

        switch (blockType)
        {
            // Builder spawn: 4 single-line arms, staggered, 800ms
            case BlockType.Builder:
                AddEffect(EffectFactory.StaggeredArms(this, pos, color));
                break;

            // Soldier spawn: 6 jittery arms from back edge, 1000ms
            case BlockType.Soldier:
                AddEffect(EffectFactory.JitterArms(this, pos, -1, 0, color,
                    duration: 1000f, armCount: 6, armLen: 4));
                break;

            // Stunner spawn: Fast branching from all edges, ~36 segs, 600ms
            case BlockType.Stunner:
                AddEffect(EffectFactory.LightningBurst(this, pos, color,
                    maxSegs: 36, duration: 600f, trail: 0.18f,
                    contProb: 0.88f, branchProb: 0.60f));
                break;

            // Warden spawn: Dashed tendrils
            case BlockType.Warden:
                AddEffect(EffectFactory.DashedTendrils(this, pos, color));
                break;

            // Jumper / Wall: Same as Builder
            default:
                AddEffect(EffectFactory.StaggeredArms(this, pos, color));
                break;
        }
    }

    // ─── Lifecycle ───────────────────────────────────────────────────

    private void AddEffect(GpuEffect effect)
    {
        if (_effects.Count >= MaxEffects)
        {
            _effects[0].Destroy();
            _effects.RemoveAt(0);
        }
        _effects.Add(effect);
    }
}
