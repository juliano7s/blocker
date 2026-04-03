using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Draws the grid: cell backgrounds by ground type, grid lines, and block placeholders.
/// Reads simulation GameState — never mutates it.
/// </summary>
public partial class GridRenderer : Node2D
{
	public const float CellSize = 28f;
	public const float GridLineWidth = 1f;

	private GameConfig _config = GameConfig.CreateDefault();
	private GameState? _gameState;
	private float _tickInterval = 1f / 12f; // Updated by GameManager

	public void SetTickInterval(float interval) => _tickInterval = interval;
	public void SetConfig(GameConfig config) => _config = config;

	// Per-block visual positions in world coords — driven at constant speed toward actual pos
	private readonly Dictionary<int, Vector2> _visualPositions = new();

	// Selected block IDs — set each frame by GameManager
	private HashSet<int> _selectedIds = [];

	// SelectionBorderColor now comes from _config.SelectionBorderColor

	public void SetSelectedIds(IReadOnlyList<Blocker.Simulation.Blocks.Block> selected)
	{
		_selectedIds = [.. selected.Select(b => b.Id)];
	}

	// Death effect tracking
	private record struct DeathEffect(Vector2 Pos, Color Color, float StartTime, float Duration);
	private record struct Fragment(Vector2 Pos, Vector2 Velocity, float Rotation, float RotSpeed, Color Color, float StartTime);
	private readonly List<DeathEffect> _deathEffects = [];
	private readonly List<Fragment> _fragments = [];

	// Ground colors, grid line color, and player colors now come from _config

	public void SetGameState(GameState state)
	{
		_gameState = state;
		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_gameState != null)
		{
			// Consume visual events for death effects
			float now = (float)Time.GetTicksMsec() / 1000f;
			foreach (var evt in _gameState.VisualEvents)
			{
				if (evt.Type == VisualEventType.BlockDied)
				{
					var worldPos = GridToWorld(evt.Position);
					var color = evt.PlayerId.HasValue ? GetPlayerColor(evt.PlayerId.Value) : Colors.White;
					float duration = Constants.DeathEffectTicks / 12f; // ~0.83s at 12 tps
					_deathEffects.Add(new DeathEffect(worldPos, color, now, duration));

					// Spawn fragments
					int fragCount = _config.DeathFragmentCount;
					for (int i = 0; i < fragCount; i++)
					{
						float angle = i * Mathf.Tau / fragCount + (i * 0.37f); // pseudo-random spread
						float speed = 30f + (i % 7) * 15f;
						_fragments.Add(new Fragment(
							worldPos,
							new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * speed,
							0, 3f + (i % 5) * 1.4f,
							color, now));
					}
				}
			}

			// Clean up expired effects
			_deathEffects.RemoveAll(e => now - e.StartTime > e.Duration);
			_fragments.RemoveAll(f => now - f.StartTime > _config.FragmentLifetime);

			// Smooth visual positions: move each block toward its actual grid pos at constant speed
			float dt = (float)delta;
			foreach (var block in _gameState.Blocks)
			{
				var target = GridToWorld(block.Pos);
				if (!_visualPositions.TryGetValue(block.Id, out var vp))
				{
					// New block — snap immediately
					_visualPositions[block.Id] = target;
					continue;
				}
				// Speed = 1 cell per (MoveInterval * tickInterval) seconds
				float speed = CellSize / (block.EffectiveMoveInterval * _tickInterval);
				_visualPositions[block.Id] = vp.MoveToward(target, speed * dt);
			}

			// Remove positions for blocks that have died
			var liveIds = _gameState.Blocks.Select(b => b.Id).ToHashSet();
			foreach (var id in _visualPositions.Keys.Except(liveIds).ToList())
				_visualPositions.Remove(id);
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_gameState == null) return;

		var nestMemberIds = _gameState.Nests.SelectMany(n => n.MemberIds).ToHashSet();
		var grid = _gameState.Grid;

		// Draw cell backgrounds
		for (int y = 0; y < grid.Height; y++)
		{
			for (int x = 0; x < grid.Width; x++)
			{
				var cell = grid[x, y];
				var rect = new Rect2(x * CellSize, y * CellSize, CellSize, CellSize);
				DrawRect(rect, _config.GetGroundColor(cell.Ground));
			}
		}

		// Draw grid lines
		for (int x = 0; x <= grid.Width; x++)
		{
			var from = new Vector2(x * CellSize, 0);
			var to = new Vector2(x * CellSize, grid.Height * CellSize);
			DrawLine(from, to, _config.GridLineColor, GridLineWidth);
		}
		for (int y = 0; y <= grid.Height; y++)
		{
			var from = new Vector2(0, y * CellSize);
			var to = new Vector2(grid.Width * CellSize, y * CellSize);
			DrawLine(from, to, _config.GridLineColor, GridLineWidth);
		}

		// Draw blocks using smooth visual positions (constant-speed interpolation)
		foreach (var block in _gameState.Blocks)
		{
			var color = _config.GetPalette(block.PlayerId).Base;
			var inset = 2f;

			var worldPos = _visualPositions.TryGetValue(block.Id, out var vp)
				? vp
				: GridToWorld(block.Pos);

			var rect = new Rect2(
				worldPos.X - CellSize / 2f + inset,
				worldPos.Y - CellSize / 2f + inset,
				CellSize - inset * 2,
				CellSize - inset * 2
			);

			// Subtle glow behind block for luminous feel
			DrawRect(rect.Grow(2f), color with { A = 0.15f });
			DrawRect(rect, color);

			// Draw type indicator
			DrawBlockTypeIndicator(block, rect, color);

			// Rooting visual: corner anchors — suppressed when in formation/nest
			if (!block.IsInFormation && !nestMemberIds.Contains(block.Id))
				DrawRootingVisual(block, rect);

			// Frozen/stunned overlay
			if (block.IsStunned)
				DrawFrozenOverlay(block, rect);

			// Combat threat indicators (red corners for adjacent soldiers)
			if (!block.IsStunned && block.Type != BlockType.Soldier)
				DrawThreatIndicators(block, rect);

			// Selection border (dashed, at visual position)
			if (_selectedIds.Contains(block.Id))
			{
				DrawDashedRect(rect.Grow(1f), _config.SelectionBorderColor, 2f, _config.SelectionDashLength, _config.SelectionGapLength);

				// Rooting progress bar above block
				if (block.State == BlockState.Rooting || block.State == BlockState.Uprooting)
				{
					float rootProgress = (float)block.RootProgress / Constants.RootTicks;
					var barRect = new Rect2(rect.Position.X, rect.Position.Y - 5f, rect.Size.X * rootProgress, 3f);
					DrawRect(barRect, Colors.Yellow);
				}
			}
		}

		// Draw Warden ZoC pulse
		DrawWardenZoC();

		// Draw active rays
		DrawRays();

		// Draw push waves
		DrawPushWaves();

		// Draw formation visuals
		DrawFormations();

		// Draw nest spawn progress
		DrawNestProgress();

		// Draw death effects (explosions, fragments)
		DrawDeathEffects();
	}

	public static Vector2 GridToWorld(GridPos pos) =>
		new(pos.X * CellSize + CellSize * 0.5f, pos.Y * CellSize + CellSize * 0.5f);

	public static GridPos WorldToGrid(Vector2 world) =>
		new((int)Mathf.Floor(world.X / CellSize), (int)Mathf.Floor(world.Y / CellSize));

	private Color GetPlayerColor(int playerId) => _config.GetPalette(playerId).Base;
}
