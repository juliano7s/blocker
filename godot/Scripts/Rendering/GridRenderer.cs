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
	public const float BlockInset = 2.5f;
	public const float GridPadding = CellSize * 5f;

	private GameConfig _config = GameConfig.CreateDefault();
	private GameState? _gameState;
	private float _tickInterval = 1f / 12f; // Updated by GameManager
	private GlowLayer? _glowLayer;

	public override void _Ready()
	{
		_glowLayer = new GlowLayer { Name = "GlowLayer" };
		AddChild(_glowLayer);
	}

	public void SetTickInterval(float interval) => _tickInterval = interval;
	public void SetConfig(GameConfig config)
	{
		_config = config;
		SpriteFactory.Build(config);
	}

	// Per-block visual positions in world coords — driven at constant speed toward actual pos
	private readonly Dictionary<int, Vector2> _visualPositions = new();

	// Per-block idle spin state — angle + cooldown until next spin
	private readonly Dictionary<int, float> _idleAngles = new();
	private readonly Dictionary<int, float> _idleCooldowns = new(); // seconds until next spin burst

	// Reusable set for dead-block cleanup — avoids per-frame allocation
	private readonly HashSet<int> _liveIdSet = new();
	private readonly List<int> _deadIds = new();

	// Dying blocks: heat-up animation before explosion into tendrils
	private record struct DyingBlockData(Vector2 WorldPos, GridPos GridPos, Color Color, float DeathTime);
	private readonly Dictionary<int, DyingBlockData> _dyingBlocks = new();
	private const float DeathAnimationDuration = 0.25f; // 250ms heat-up before explosion

	private EffectManager? _effectManager;
	public void SetEffectManager(EffectManager em) => _effectManager = em;

	// Reusable collections for formation joiner rendering — avoids per-frame allocation
	private readonly List<(int Id, GridPos Pos)> _formationMembers = new();
	private readonly HashSet<GridPos> _formationPosSet = new();

	// Jumper ghost trails — position + start time for fading afterimages
	private record struct GhostTrail(Vector2 Pos, Color Color, float StartTime, bool IsJump);
	private readonly List<GhostTrail> _ghostTrails = [];

	// Selected block IDs — set each frame by GameManager
	private HashSet<int> _selectedIds = [];

	// SelectionBorderColor now comes from _config.SelectionBorderColor

	public void SetSelectedIds(IReadOnlyList<Blocker.Simulation.Blocks.Block> selected)
	{
		_selectedIds.Clear();
		foreach (var b in selected)
			_selectedIds.Add(b.Id);
	}


	// Ground colors, grid line color, and player colors now come from _config

	public void SetGameState(GameState state)
	{
		_gameState = state;

		// Clean up stale warden ZoC rects from previous game state
		foreach (var (_, rect) in _wardenZocRects)
			rect.QueueFree();
		_wardenZocRects.Clear();

		QueueRedraw();
	}

	public override void _Process(double delta)
	{
		if (_gameState != null)
		{
			// Consume visual events for death effects and jumper ghost trails
			float now = (float)Time.GetTicksMsec() / 1000f;
			foreach (var evt in _gameState.VisualEvents)
			{
				// Capture dying blocks for heat-up animation
				if (evt.Type == VisualEventType.BlockDied && evt.BlockId.HasValue && !_dyingBlocks.ContainsKey(evt.BlockId.Value))
				{
					var dyingColor = evt.PlayerId.HasValue ? _config.GetPalette(evt.PlayerId.Value).Base : Colors.White;
					var dyingWorldPos = _visualPositions.TryGetValue(evt.BlockId.Value, out var dvp)
						? dvp : GridToWorld(evt.Position);
					_dyingBlocks[evt.BlockId.Value] = new DyingBlockData(dyingWorldPos, evt.Position, dyingColor, now);
				}

				// Jumper ghost trails from moves and jumps
			if (evt.Type == VisualEventType.BlockMoved && evt.BlockId.HasValue)
			{
				var movedBlock = _gameState.GetBlock(evt.BlockId.Value);
				if (movedBlock is { Type: BlockType.Jumper })
				{
					var ghostPos = _visualPositions.TryGetValue(movedBlock.Id, out var gvp)
						? gvp : GridToWorld(movedBlock.PrevPos);
					var ghostColor = GetPlayerColor(movedBlock.PlayerId);
					_ghostTrails.Add(new GhostTrail(ghostPos, ghostColor, now, false));
				}
			}
			if (evt.Type == VisualEventType.JumpExecuted && evt.BlockId.HasValue)
			{
				var jumper = _gameState.GetBlock(evt.BlockId.Value);
				if (jumper != null)
				{
					// Snap visual position to landing — jumps are instant, no interpolation
					var landingWorld = GridToWorld(jumper.Pos);
					_visualPositions[jumper.Id] = landingWorld;

					var jumpColor = GetPlayerColor(jumper.PlayerId);
					// Add blur ghosts along the jump path
					var dir = evt.Direction!.Value;
					var range = evt.Range ?? 1;
					var offset = dir.ToOffset();
					// Reconstruct start position from landing + direction (PrevPos is
					// already overwritten by GameState step 11 at this point)
					var pos = jumper.Pos - new GridPos(offset.X * range, offset.Y * range);
					for (int i = 0; i < range; i++)
					{
						pos = pos + offset;
						_ghostTrails.Add(new GhostTrail(GridToWorld(pos), jumpColor, now, true));
					}
				}
			}
			}

			// Single pass: smooth visual positions + idle spin angles + build live ID set
			float dt = (float)delta;
			_liveIdSet.Clear();
			foreach (var block in _gameState.Blocks)
			{
				_liveIdSet.Add(block.Id);

				// --- Visual position interpolation ---
				var target = GridToWorld(block.Pos);
				if (!_visualPositions.TryGetValue(block.Id, out var vp))
				{
					_visualPositions[block.Id] = target;
				}
				else
				{
					float speed = CellSize / (block.EffectiveMoveInterval * _tickInterval);
					_visualPositions[block.Id] = vp.MoveToward(target, speed * dt);
				}

				// --- Idle spin angles ---
				bool idle = block.IsMobile && block.MoveTarget == null && !block.IsStunned;
				if (!_idleAngles.ContainsKey(block.Id))
				{
					_idleAngles[block.Id] = 0f;
					_idleCooldowns[block.Id] = 0.5f + (block.Id % 7) * 0.4f;
				}

				if (idle)
				{
					float current = _idleAngles[block.Id];
					float cooldown = _idleCooldowns.GetValueOrDefault(block.Id, 0f);

					if (block.Type == BlockType.Stunner)
					{
						_idleAngles[block.Id] += 1.5f * dt;
						continue;
					}

					float snapUnit = block.Type == BlockType.Builder
						? Mathf.Pi * 0.5f
						: Mathf.Tau;

					float snapped = Mathf.Round(current / snapUnit) * snapUnit;
					bool atRest = Mathf.Abs(current - snapped) < 0.01f;

					if (atRest)
					{
						_idleAngles[block.Id] = snapped;
						cooldown -= dt;
						_idleCooldowns[block.Id] = cooldown;
						if (cooldown <= 0)
						{
							_idleAngles[block.Id] += 0.02f;
							_idleCooldowns[block.Id] = 3f + ((block.Id * 7 + (int)(now * 3)) % 13) * 0.3f;
						}
					}
					else
					{
						float speed = block.Type == BlockType.Builder ? 3.5f : 18f;
						_idleAngles[block.Id] += speed * dt;
					}
				}
				else
				{
					float snapUnit = block.Type switch
					{
						BlockType.Builder => Mathf.Pi * 0.5f,
						BlockType.Soldier => Mathf.Tau,
						_ => Mathf.Pi * 0.5f
					};
					float current = _idleAngles[block.Id];
					_idleAngles[block.Id] = Mathf.Round(current / snapUnit) * snapUnit;
				}
			}

			// Clean up ghost trails
			_ghostTrails.RemoveAll(g => now - g.StartTime > (g.IsJump ? 0.3f : 0.5f));

			// Complete dying block animations — spawn tendrils on explosion
			_deadIds.Clear();
			foreach (var (id, dying) in _dyingBlocks)
			{
				if (now - dying.DeathTime >= DeathAnimationDuration)
				{
					_effectManager?.SpawnDeathExplosion(dying.GridPos, dying.Color);
					_deadIds.Add(id);
				}
			}
			foreach (var id in _deadIds)
				_dyingBlocks.Remove(id);

			// Clean up visual state for blocks no longer live AND not in dying animation
			_deadIds.Clear();
			foreach (var id in _visualPositions.Keys)
				if (!_liveIdSet.Contains(id) && !_dyingBlocks.ContainsKey(id))
					_deadIds.Add(id);
			foreach (var id in _deadIds)
				_visualPositions.Remove(id);

			_deadIds.Clear();
			foreach (var id in _idleAngles.Keys)
				if (!_liveIdSet.Contains(id) && !_dyingBlocks.ContainsKey(id))
					_deadIds.Add(id);
			foreach (var id in _deadIds)
			{
				_idleAngles.Remove(id);
				_idleCooldowns.Remove(id);
			}
		}

		// Update warden ZoC shader-based sine rings
		UpdateWardenZoC();

		QueueRedraw();
	}

	/// <summary>
	/// Computes the visible cell range from the current canvas transform.
	/// Returns (minX, minY, maxX, maxY) clamped to grid bounds.
	/// </summary>
	private (int minX, int minY, int maxX, int maxY) GetVisibleCellRange()
	{
		var grid = _gameState!.Grid;
		var xform = GetCanvasTransform();
		var invXform = xform.AffineInverse();
		var vpSize = GetViewportRect().Size;

		// Transform viewport corners to world space
		var topLeft = invXform * Vector2.Zero;
		var bottomRight = invXform * vpSize;

		// Convert to cell indices with 1-cell margin for partial visibility
		int minX = Mathf.Max(0, (int)Mathf.Floor((topLeft.X - GridPadding) / CellSize) - 1);
		int minY = Mathf.Max(0, (int)Mathf.Floor((topLeft.Y - GridPadding) / CellSize) - 1);
		int maxX = Mathf.Min(grid.Width - 1, (int)Mathf.Ceil((bottomRight.X - GridPadding) / CellSize) + 1);
		int maxY = Mathf.Min(grid.Height - 1, (int)Mathf.Ceil((bottomRight.Y - GridPadding) / CellSize) + 1);

		return (minX, minY, maxX, maxY);
	}

	public override void _Draw()
	{
		if (_gameState == null) return;
		_glowLayer?.BeginFrame();

		var grid = _gameState.Grid;
		var (minX, minY, maxX, maxY) = GetVisibleCellRange();
		// Draw padding border around grid
		var gridBounds = new Rect2(GridPadding, GridPadding, grid.Width * CellSize, grid.Height * CellSize);
		var paddedBounds = gridBounds.Grow(GridPadding);
		DrawRect(paddedBounds, _config.NormalGroundColor with { A = 0.5f });
		DrawRect(gridBounds.GrowIndividual(-0.5f, -0.5f, -0.5f, -0.5f), _config.GridLineColor with { A = 0.3f }, false, 1f);

		// Draw cell backgrounds — only visible cells
		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				var cell = grid[x, y];
				var rect = new Rect2(x * CellSize + GridPadding, y * CellSize + GridPadding, CellSize, CellSize);
				DrawRect(rect, _config.GetGroundColor(cell.Ground));

				// Draw terrain walls as inset blocks (after background, before grid lines)
				if (cell.Terrain != TerrainType.None)
					DrawTerrainWallBlock(rect, cell.Terrain);
			}
		}

		// Draw grid lines — only visible range, scale width by 1/zoom so lines are always 1 screen pixel
		float screenPixelWidth = GridLineWidth / GetCanvasTransform().X.X;
		float gridTop = minY * CellSize + GridPadding;
		float gridBottom = (maxY + 1) * CellSize + GridPadding;
		for (int x = minX; x <= maxX + 1; x++)
		{
			var from = new Vector2(x * CellSize + GridPadding, gridTop);
			var to = new Vector2(x * CellSize + GridPadding, gridBottom);
			DrawLine(from, to, _config.GridLineColor, screenPixelWidth, false);
		}
		float gridLeft = minX * CellSize + GridPadding;
		float gridRight = (maxX + 1) * CellSize + GridPadding;
		for (int y = minY; y <= maxY + 1; y++)
		{
			var from = new Vector2(gridLeft, y * CellSize + GridPadding);
			var to = new Vector2(gridRight, y * CellSize + GridPadding);
			DrawLine(from, to, _config.GridLineColor, screenPixelWidth, false);
		}

		// Draw blocks — viewport cull using visible cell range with extra margin
		float blockViewMinX = (minX - 1) * CellSize + GridPadding;
		float blockViewMaxX = (maxX + 2) * CellSize + GridPadding;
		float blockViewMinY = (minY - 1) * CellSize + GridPadding;
		float blockViewMaxY = (maxY + 2) * CellSize + GridPadding;

		foreach (var block in _gameState.Blocks)
		{
			var worldPos = _visualPositions.TryGetValue(block.Id, out var vp)
				? vp
				: GridToWorld(block.Pos);

			// Skip blocks outside visible area
			if (worldPos.X < blockViewMinX || worldPos.X > blockViewMaxX ||
				worldPos.Y < blockViewMinY || worldPos.Y > blockViewMaxY)
				continue;

			var color = _config.GetPalette(block.PlayerId).Base;
			var rect = new Rect2(
				worldPos.X - CellSize / 2f + BlockInset,
				worldPos.Y - CellSize / 2f + BlockInset,
				CellSize - BlockInset * 2,
				CellSize - BlockInset * 2
			);

			// Draw type indicator (each type draws its own body)
			DrawBlockTypeIndicator(block, rect, color);

			// Rooting visual: corner anchors, stripes, tendrils
			DrawRootingVisual(block, rect);

			// Frozen/stunned overlay
			if (block.IsStunned)
				DrawFrozenOverlay(block, rect);

			// Cooldown bars (stun timer + ability cooldown), floating below cell
			if (block.IsStunned || block.IsOnCooldown)
				DrawCooldownBars(block, rect);

			// Combat threat indicators (red corners for adjacent soldiers)
			if (!block.IsStunned && block.Type != BlockType.Soldier)
				DrawThreatIndicators(block, rect);

			// Selection border (dashed, at visual position)
			if (_selectedIds.Contains(block.Id))
			{
				DrawDashedRect(rect.Grow(1f), _config.SelectionBorderColor, 0.7f, _config.SelectionDashLength, _config.SelectionGapLength);
			}
		}

		// Draw dying blocks — heat-up glow before explosion
		float drawNow = (float)Time.GetTicksMsec() / 1000f;
		foreach (var (_, dying) in _dyingBlocks)
		{
			float elapsed = drawNow - dying.DeathTime;
			float progress = Mathf.Clamp(elapsed / DeathAnimationDuration, 0f, 1f);

			// Ease-in cubic: slow start, fast end — feels like pressure building
			float heat = progress * progress * progress;

			// Color shifts from original toward white-hot
			var heatColor = dying.Color.Lerp(Colors.White, heat);

			var rect = new Rect2(
				dying.WorldPos.X - CellSize / 2f + BlockInset,
				dying.WorldPos.Y - CellSize / 2f + BlockInset,
				CellSize - BlockInset * 2,
				CellSize - BlockInset * 2
			);

			// Draw the heated block body
			DrawRect(rect, heatColor);

			// Growing glow as heat intensifies
			if (heat > 0.15f)
			{
				float glowRadius = CellSize * (0.4f + 0.8f * heat);
				QueueGlowCircle(dying.WorldPos, glowRadius, heatColor with { A = heat * 0.7f });
			}
		}

		// Draw Warden ZoC pulse
		DrawWardenZoC();

		// Draw active rays
		DrawRays();

		// Draw push waves
		DrawPushWaves();

		// Draw nest spawn progress
		DrawNestProgress();

		// Draw jumper ghost trails
		DrawGhostTrails();


		// Draw formation visuals (last so outlines/corners are on top of block sprites)
		DrawFormations();

		_glowLayer?.EndFrame();
	}

	public static Vector2 GridToWorld(GridPos pos) =>
		new(pos.X * CellSize + CellSize * 0.5f + GridPadding,
			pos.Y * CellSize + CellSize * 0.5f + GridPadding);

	public static GridPos WorldToGrid(Vector2 world) =>
		new((int)Mathf.Floor((world.X - GridPadding) / CellSize),
			(int)Mathf.Floor((world.Y - GridPadding) / CellSize));

	private Color GetPlayerColor(int playerId) => _config.GetPalette(playerId).Base;

	private void DrawRoundLine(Vector2 from, Vector2 to, Color color, float width)
	{
		DrawLine(from, to, color, width, true);
		DrawCircle(from, width * 0.5f, color);
		DrawCircle(to, width * 0.5f, color);
	}

	private void DrawGlowRadial(Vector2 center, float radius, Color color)
	{
		var tex = SpriteFactory.GetRadialGlow();
		var rect = new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2f);
		DrawTextureRect(tex, rect, false, color);
	}

	private void QueueGlowLine(Vector2 from, Vector2 to, Color color, float width, bool roundCaps = false)
	{
		_glowLayer?.Add(GlowLayer.GlowCommand.MakeLine(from, to, color, width, roundCaps));
	}

	private void QueueGlowCircle(Vector2 center, float radius, Color color)
	{
		_glowLayer?.Add(GlowLayer.GlowCommand.MakeCircle(center, radius, color));
	}

	private void QueueGlowRadial(Vector2 center, float radius, Color color)
	{
		_glowLayer?.Add(GlowLayer.GlowCommand.MakeTexture(
			new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2f), color));
	}
}
