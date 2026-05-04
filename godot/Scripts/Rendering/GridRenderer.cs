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
	private VisibilityMap? _localVisibility;
	private int _controllingTeamId = -1;
	private float _tickInterval = 1f / 12f; // Updated by GameManager
	private GlowLayer? _glowLayer;
	private RootFloraRenderer? _floraRenderer;

	private struct FlyingArm
	{
		public Vector2 Start;
		public Vector2 Direction;
		public float StartTime;
		public Color Color;
		public float ArmLength;
	}
	private readonly List<FlyingArm> _flyingArms = new();

	public void SetVisibility(VisibilityMap? visibility) => _localVisibility = visibility;

	public void SetControllingPlayer(int playerId)
	{
		if (_gameState == null) return;
		_controllingTeamId = _gameState.GetTeamFor(playerId);
	}

	// Shader-based grid background
	private ColorRect _bgRect = null!;
	private ShaderMaterial _bgMaterial = null!;
	private ImageTexture? _mapDataTexture;

	// Nugget overlay: shader-based sparkles for unmined nuggets + fortified wall diamonds
	private ColorRect? _nuggetOverlayRect;
	private ShaderMaterial? _nuggetOverlayMaterial;
	private ImageTexture? _nuggetDataTexture;
	private Image? _nuggetDataImage;

	public override void _Ready()
	{
		_glowLayer = new GlowLayer { Name = "GlowLayer" };
		AddChild(_glowLayer);

		_floraRenderer = new RootFloraRenderer { Name = "RootFlora" };
		AddChild(_floraRenderer);

		// Initialize background shader
		var shader = GD.Load<Shader>("res://Assets/Shaders/grid_background.gdshader");
		_bgMaterial = new ShaderMaterial { Shader = shader };
		_bgRect = new ColorRect
		{
			Material = _bgMaterial,
			Color = Colors.Black, // Fallback if shader fails
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = -10, // Ensure it's behind everything
		};
		AddChild(_bgRect);

		// Initialize nugget overlay shader
		var nuggetShader = GD.Load<Shader>("res://Assets/Shaders/nugget_overlay.gdshader");
		_nuggetOverlayMaterial = new ShaderMaterial { Shader = nuggetShader };
		_nuggetOverlayRect = new ColorRect
		{
			Material = _nuggetOverlayMaterial,
			Color = Colors.White,
			MouseFilter = Control.MouseFilterEnum.Ignore,
			ZIndex = 5,
		};
		AddChild(_nuggetOverlayRect);
	}

	public void SetTickInterval(float interval) => _tickInterval = interval;
	public void SetConfig(GameConfig config)
	{
		_config = config;
		SpriteFactory.Build(config);
		UpdateGridShader();
	}

	// Per-block visual positions in world coords — driven at constant speed toward actual pos
	private readonly Dictionary<int, Vector2> _visualPositions = new();

	// Per-block idle spin state — angle + cooldown until next spin
	private readonly Dictionary<int, float> _idleAngles = new();
	private readonly Dictionary<int, float> _comboAngles = new();
	private readonly Dictionary<int, float> _idleCooldowns = new(); // seconds until next spin burst

	// Reusable set for dead-block cleanup — avoids per-frame allocation
	private readonly HashSet<int> _liveIdSet = new();
	private readonly List<int> _deadIds = new();
	private readonly HashSet<int> _nuggetBlockIds = new();
	private readonly HashSet<int> _pulledBlockIds = new();

	// Dying blocks: heat-up animation before explosion into tendrils
	private enum DeathCause { Killed, Consumed }
	private record struct DyingBlockData(Vector2 WorldPos, GridPos GridPos, Color Color, float DeathTime, bool IsNugget = false, DeathCause Cause = DeathCause.Killed);
	private readonly Dictionary<int, DyingBlockData> _dyingBlocks = new();
	private const float DeathAnimationDuration = 0.25f; // 250ms heat-up before explosion

	// Mining hit times — for syncing diamond vibration bursts with sparkle effects
	private readonly Dictionary<int, float> _lastMiningHitTime = new();

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

	// Track last processed tick to avoid processing events multiple times per tick
	private int _lastProcessedTick = -1;

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
		_lastProcessedTick = -1; // Reset when state changes
		ClearAllGhosts();
		_fogLastTick = -1;

		// Update background shader
		UpdateGridShader();

		// Clean up stale warden ZoC rects from previous game state
		foreach (var (_, rect) in _wardenZocRects)
			rect.QueueFree();
		_wardenZocRects.Clear();

		// Clean up nest refine zone rects from previous game state
		foreach (var (_, rect) in _nestRefineRects)
			rect.QueueFree();
		_nestRefineRects.Clear();

		QueueRedraw();
	}

	private void UpdateGridShader()
	{
		if (_gameState == null || _bgMaterial == null) return;

		var grid = _gameState.Grid;
		int width = grid.Width;
		int height = grid.Height;

		// Create data texture
		var image = Image.CreateEmpty(width, height, false, Image.Format.R8);
		for (int y = 0; y < height; y++)
		{
			for (int x = 0; x < width; x++)
			{
				var cell = grid[x, y];

				// Ground: 0: Normal, 1: Boot, 2: Overload, 3: Proto
				int groundVal = (int)cell.Ground;

				// Terrain: 0: None, 1: Terrain, 2: Breakable, 3: Fragile
				int terrainVal = (int)cell.Terrain;

				// Combine into R8: lower 4 bits ground, upper 4 bits terrain
				int rawVal = (groundVal & 0x0F) | ((terrainVal & 0x0F) << 4);
				image.SetPixel(x, y, new Color(rawVal / 255f, 0, 0, 1.0f));
			}
		}

		_mapDataTexture = ImageTexture.CreateFromImage(image);
		_bgMaterial.SetShaderParameter("map_data", _mapDataTexture);
		_bgMaterial.SetShaderParameter("grid_size", new Vector2I(width, height));
		_bgMaterial.SetShaderParameter("cell_size", CellSize);
		_bgMaterial.SetShaderParameter("grid_padding", GridPadding);
		_bgMaterial.SetShaderParameter("grid_line_width", GridLineWidth);

		_bgMaterial.SetShaderParameter("normal_color", _config.NormalGroundColor);
		_bgMaterial.SetShaderParameter("boot_color", _config.BootGroundColor);
		_bgMaterial.SetShaderParameter("overload_color", _config.OverloadGroundColor);
		_bgMaterial.SetShaderParameter("proto_color", _config.ProtoGroundColor);
		_bgMaterial.SetShaderParameter("grid_line_color", _config.GridLineColor);

		_bgMaterial.SetShaderParameter("terrain_color", _config.TerrainGroundColor);
		_bgMaterial.SetShaderParameter("breakable_color", _config.BreakableWallGroundColor);
		_bgMaterial.SetShaderParameter("fragile_color", _config.FragileWallGroundColor);

		// Update nugget overlay shader params
		if (_nuggetOverlayMaterial != null)
		{
			var grid2 = _gameState.Grid;
			_nuggetOverlayMaterial.SetShaderParameter("grid_size", new Vector2I(grid2.Width, grid2.Height));
			_nuggetOverlayMaterial.SetShaderParameter("cell_size", CellSize);
			_nuggetOverlayMaterial.SetShaderParameter("grid_padding", GridPadding);

			// Create data image (reused each tick)
			_nuggetDataImage = Image.CreateEmpty(grid2.Width, grid2.Height, false, Image.Format.Rgba8);
			_nuggetDataTexture = ImageTexture.CreateFromImage(_nuggetDataImage);
			_nuggetOverlayMaterial.SetShaderParameter("nugget_data", _nuggetDataTexture);

			float totalW = grid2.Width * CellSize + GridPadding * 2f;
			float totalH = grid2.Height * CellSize + GridPadding * 2f;
			_nuggetOverlayRect!.Size = new Vector2(totalW, totalH);
			_nuggetOverlayRect.Position = Vector2.Zero;
		}

		// Size the ColorRect to cover grid + padding
		float totalWidth = width * CellSize + GridPadding * 2.0f;
		float totalHeight = height * CellSize + GridPadding * 2.0f;
		_bgRect.Size = new Vector2(totalWidth, totalHeight);
		_bgRect.Position = Vector2.Zero; // GridRenderer origin is at (0,0), which is top-left of padding
	}

	public override void _Process(double delta)
	{
		if (_gameState != null)
		{
			UpdateFog();
			float now = (float)Time.GetTicksMsec() / 1000f;

			// Consume visual events once per tick
			if (_gameState.TickNumber != _lastProcessedTick)
			{
				_lastProcessedTick = _gameState.TickNumber;
				foreach (var evt in _gameState.VisualEvents)
				{
					// Capture dying blocks for heat-up animation
					if (evt.Type == VisualEventType.BlockDied && evt.BlockId.HasValue && !_dyingBlocks.ContainsKey(evt.BlockId.Value))
					{
						bool isNugget = _nuggetBlockIds.Contains(evt.BlockId.Value);
						var dyingColor = isNugget
							? Colors.White
							: (evt.PlayerId.HasValue ? _config.GetPalette(evt.PlayerId.Value).Base : Colors.White);
						var dyingWorldPos = _visualPositions.TryGetValue(evt.BlockId.Value, out var dvp)
							? dvp : GridToWorld(evt.Position);
						_dyingBlocks[evt.BlockId.Value] = new DyingBlockData(dyingWorldPos, evt.Position, dyingColor, now, isNugget);
					}

					// Nugget consumption: flash like death in the cell it occupied
					if ((evt.Type == VisualEventType.NuggetRefineConsumed
						|| evt.Type == VisualEventType.NuggetHealConsumed
						|| evt.Type == VisualEventType.NuggetFortifyConsumed)
						&& evt.BlockId.HasValue && !_dyingBlocks.ContainsKey(evt.BlockId.Value))
					{
						var consumeColor = evt.Type switch
						{
							VisualEventType.NuggetHealConsumed => new Color(0.4f, 1f, 0.5f),
							VisualEventType.NuggetFortifyConsumed => new Color(0.7f, 0.85f, 1f),
							_ => Colors.White,
						};
						var consumeWorldPos = _visualPositions.TryGetValue(evt.BlockId.Value, out var cwp)
							? cwp : GridToWorld(evt.Position);
						_dyingBlocks[evt.BlockId.Value] = new DyingBlockData(
							consumeWorldPos, evt.Position, consumeColor, now, IsNugget: true, Cause: DeathCause.Consumed);
					}

					// Mining hit: record time for diamond vibration burst
					if (evt.Type == VisualEventType.NuggetMiningStarted && evt.BlockId.HasValue)
						_lastMiningHitTime[evt.BlockId.Value] = now;

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

					if (evt.Type == VisualEventType.SoldierArmLost && evt.BlockId.HasValue)
					{
						var worldPos = _visualPositions.TryGetValue(evt.BlockId.Value, out var wp)
							? wp : GridToWorld(evt.Position);
						var armColor = _config.GetPalette(evt.PlayerId ?? 0).SoldierArmsColor;
						float armLen = (CellSize - BlockInset * 2) * 0.25f;
						var soldier = _gameState.GetBlock(evt.BlockId.Value);
						int hp = soldier?.Hp ?? 0;
						// Pick the arm that was just lost based on remaining HP
						// Loss order: BR(4→3), BL(3→2), TL(2→1), TR(1→0)
						Vector2 flyDir = hp switch
						{
							3 => new Vector2(1, 1).Normalized(),
							2 => new Vector2(-1, 1).Normalized(),
							1 => new Vector2(-1, -1).Normalized(),
							_ => new Vector2(1, -1).Normalized(),
						};
						_flyingArms.Add(new FlyingArm
						{
							Start = worldPos,
							Direction = flyDir,
							StartTime = now,
							Color = armColor,
							ArmLength = armLen,
						});
					}
				}
			}

			// Single pass: smooth visual positions + idle spin angles + build live ID set
			// Note: _nuggetBlockIds still holds PREVIOUS frame's data here, which is
			// load-bearing — death event processing above relies on it to detect nuggets
			// (the block is already removed from GameState when BlockDied fires).
			float dt = (float)delta;
			_liveIdSet.Clear();
			_nuggetBlockIds.Clear();
			foreach (var block in _gameState.Blocks)
			{
				_liveIdSet.Add(block.Id);
				if (block.Type == BlockType.Nugget)
					_nuggetBlockIds.Add(block.Id);

				// --- Visual position interpolation ---
				var target = GridToWorld(block.Pos);
				if (!_visualPositions.TryGetValue(block.Id, out var vp))
				{
					_visualPositions[block.Id] = target;
				}
				else
				{
					if (block.WasPulledThisTick)
						_pulledBlockIds.Add(block.Id);
					else if (vp.DistanceSquaredTo(target) < 1f)
						_pulledBlockIds.Remove(block.Id);

					float speed;
					if (_pulledBlockIds.Contains(block.Id))
						speed = CellSize / (1f * _tickInterval);
					else
						speed = CellSize / (block.EffectiveMoveInterval * _tickInterval);
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

			// Update combo spin angles — accumulate per frame for smooth rotation
			foreach (var block in _gameState.Blocks)
			{
				if (block.Type != BlockType.Soldier) continue;
				if (block.SwordComboTimer > 0)
				{
					float comboMax = (float)Constants.SoldierComboTicks;
					float comboT = block.SwordComboTimer / comboMax;
					float speed = comboT * 18f + 2f;
					_comboAngles.TryGetValue(block.Id, out float ca);
					_comboAngles[block.Id] = ca + speed * dt;
				}
				else
				{
					_comboAngles.Remove(block.Id);
				}
			}

			// Clean up ghost trails
			_ghostTrails.RemoveAll(g => now - g.StartTime > (g.IsJump ? 0.3f : 0.5f));

			// Clean up finished flying arms
			_flyingArms.RemoveAll(a => now - a.StartTime > 0.6f);

			// Complete dying block animations — spawn tendrils on explosion
			_deadIds.Clear();
			foreach (var (id, dying) in _dyingBlocks)
			{
				if (now - dying.DeathTime >= DeathAnimationDuration)
				{
					if (dying.Cause == DeathCause.Consumed)
						_effectManager?.SpawnNuggetConsumptionFlash(dying.GridPos, dying.Color);
					else if (dying.IsNugget)
						_effectManager?.SpawnNuggetDeathExplosion(dying.GridPos);
					else
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
				_comboAngles.Remove(id);
				_lastMiningHitTime.Remove(id);
				_floraRenderer?.RemoveBlock(id);
			}
		}

		// Update nugget overlay data texture
		UpdateNuggetOverlayData();

		// Update warden ZoC shader-based sine rings
		UpdateWardenZoC();

		// Update nest refine zone visualization
		UpdateNestRefineZones();

		QueueRedraw();
	}

	private void UpdateNuggetOverlayData()
	{
		if (_gameState == null || _nuggetDataImage == null || _nuggetDataTexture == null) return;

		// Clear to black (all zeros = empty cells)
		_nuggetDataImage.Fill(Colors.Black);

		foreach (var block in _gameState.Blocks)
		{
			if (_localVisibility != null && !_localVisibility.IsVisible(block.Pos)) continue;

			if (block.Type == BlockType.Nugget && block.NuggetState is { IsMined: false })
			{
				// Unmined nugget: R=1, G=mining progress, B=phase
				int miningProgress = 0;
				if (block.PlayerId != -1)
					miningProgress = (int)(255f * block.NuggetState.MiningProgress / Simulation.Core.Constants.NuggetMiningTicks);
				int phaseByte = block.Id & 0xFF;
				_nuggetDataImage.SetPixel(block.Pos.X, block.Pos.Y,
					new Color(1f / 255f, miningProgress / 255f, phaseByte / 255f, 0f));
			}
			else if (block.Type == BlockType.Wall && block.FortifiedHp > 0)
			{
				// Fortified wall: R=2, G=HP, B=phase
				int phaseByte = block.Id & 0xFF;
				_nuggetDataImage.SetPixel(block.Pos.X, block.Pos.Y,
					new Color(2f / 255f, block.FortifiedHp / 255f, phaseByte / 255f, 0f));
			}
		}

		_nuggetDataTexture.Update(_nuggetDataImage);
	}

	public override void _Draw()
	{
		if (_gameState == null) return;
		_glowLayer?.BeginFrame();

		var grid = _gameState.Grid;
		// Viewport cull using current canvas transform
		var xform = GetCanvasTransform();
		var invXform = xform.AffineInverse();
		var vpSize = GetViewportRect().Size;
		var topLeft = invXform * Vector2.Zero;
		var bottomRight = invXform * vpSize;

		// Convert to cell indices with 1-cell margin for partial visibility
		int minX = Mathf.Max(0, (int)Mathf.Floor((topLeft.X - GridPadding) / CellSize) - 1);
		int minY = Mathf.Max(0, (int)Mathf.Floor((topLeft.Y - GridPadding) / CellSize) - 1);
		int maxX = Mathf.Min(grid.Width - 1, (int)Mathf.Ceil((bottomRight.X - GridPadding) / CellSize) + 1);
		int maxY = Mathf.Min(grid.Height - 1, (int)Mathf.Ceil((bottomRight.Y - GridPadding) / CellSize) + 1);

		// Draw terrain walls — viewport culled
		for (int y = minY; y <= maxY; y++)
		{
			for (int x = minX; x <= maxX; x++)
			{
				var cell = grid[x, y];
				if (cell.Terrain != TerrainType.None)
				{
					if (_localVisibility != null && !_localVisibility.IsExplored(x, y)) continue;
					var rect = new Rect2(x * CellSize + GridPadding, y * CellSize + GridPadding, CellSize, CellSize);
					DrawTerrainWallBlock(rect, cell.Terrain);
				}
			}
		}

		DrawFogGhosts();

		// Draw blocks — viewport cull using visible cell range with extra margin
		float blockViewMinX = (minX - 1) * CellSize + GridPadding;
		float blockViewMaxX = (maxX + 2) * CellSize + GridPadding;
		float blockViewMinY = (minY - 1) * CellSize + GridPadding;
		float blockViewMaxY = (maxY + 2) * CellSize + GridPadding;

		foreach (var block in _gameState.Blocks)
		{
			if (Constants.FogOfWarEnabled && _localVisibility != null && block.PlayerId != -1)
			{
				int blockTeam = _gameState.GetTeamFor(block.PlayerId);
				if (blockTeam != _controllingTeamId && !_localVisibility.IsVisible(block.Pos))
					continue;
			}

			var worldPos = _visualPositions.TryGetValue(block.Id, out var vp)
				? vp
				: GridToWorld(block.Pos);

			// Skip blocks outside visible area
			if (worldPos.X < blockViewMinX || worldPos.X > blockViewMaxX ||
				worldPos.Y < blockViewMinY || worldPos.Y > blockViewMaxY)
				continue;

			var color = _config.GetPalette(block.PlayerId).Base;
			_floraRenderer?.UpdateBlock(block, color);
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

			// Surround trap blink
			if (block.TrapTicks > 0)
				DrawSurroundTrapBlink(block, rect);

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

		// Draw flying arms from soldier combo expiry
		DrawFlyingArms(drawNow);

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
