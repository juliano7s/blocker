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

	private GameState? _gameState;
	private float _interpolation = 1f;

	public void SetInterpolation(float t) => _interpolation = t;

	// Death effect tracking
	private record struct DeathEffect(Vector2 Pos, Color Color, float StartTime, float Duration);
	private record struct Fragment(Vector2 Pos, Vector2 Velocity, float Rotation, float RotSpeed, Color Color, float StartTime);
	private readonly List<DeathEffect> _deathEffects = [];
	private readonly List<Fragment> _fragments = [];

	// Ground type colors
	private static readonly Color NormalColor = new(0.12f, 0.12f, 0.14f);
	private static readonly Color BootColor = new(0.08f, 0.22f, 0.08f);
	private static readonly Color OverloadColor = new(0.20f, 0.08f, 0.25f);
	private static readonly Color ProtoColor = new(0.14f, 0.14f, 0.18f);
	private static readonly Color TerrainColor = new(0.25f, 0.25f, 0.25f);
	private static readonly Color BreakableWallColor = new(0.30f, 0.22f, 0.15f);
	private static readonly Color FragileWallColor = new(0.25f, 0.18f, 0.12f);
	private static readonly Color GridLineColor = new(0.20f, 0.20f, 0.22f, 0.5f);

	// Player colors
	private static readonly Color[] PlayerColors =
	[
		new(0.2f, 0.4f, 0.9f),   // P0: Blue
		new(0.9f, 0.25f, 0.2f),  // P1: Red
		new(0.9f, 0.8f, 0.2f),   // P2: Yellow
		new(0.2f, 0.8f, 0.3f),   // P3: Green
		new(0.8f, 0.4f, 0.1f),   // P4: Orange
		new(0.6f, 0.2f, 0.8f),   // P5: Purple
	];

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
					for (int i = 0; i < 28; i++)
					{
						float angle = i * Mathf.Tau / 28f + (i * 0.37f); // pseudo-random spread
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
			_fragments.RemoveAll(f => now - f.StartTime > 0.8f);
		}

		QueueRedraw();
	}

	public override void _Draw()
	{
		if (_gameState == null) return;

		var grid = _gameState.Grid;

		// Draw cell backgrounds
		for (int y = 0; y < grid.Height; y++)
		{
			for (int x = 0; x < grid.Width; x++)
			{
				var cell = grid[x, y];
				var rect = new Rect2(x * CellSize, y * CellSize, CellSize, CellSize);
				DrawRect(rect, GetGroundColor(cell.Ground));
			}
		}

		// Draw grid lines
		for (int x = 0; x <= grid.Width; x++)
		{
			var from = new Vector2(x * CellSize, 0);
			var to = new Vector2(x * CellSize, grid.Height * CellSize);
			DrawLine(from, to, GridLineColor, GridLineWidth);
		}
		for (int y = 0; y <= grid.Height; y++)
		{
			var from = new Vector2(0, y * CellSize);
			var to = new Vector2(grid.Width * CellSize, y * CellSize);
			DrawLine(from, to, GridLineColor, GridLineWidth);
		}

		// Draw blocks as colored squares (with smooth interpolation)
		foreach (var block in _gameState.Blocks)
		{
			var color = GetPlayerColor(block.PlayerId);
			var inset = 2f;

			// Interpolate between PrevPos and Pos for smooth movement
			float lerpX = block.PrevPos.X + (block.Pos.X - block.PrevPos.X) * _interpolation;
			float lerpY = block.PrevPos.Y + (block.Pos.Y - block.PrevPos.Y) * _interpolation;
			var rect = new Rect2(
				lerpX * CellSize + inset,
				lerpY * CellSize + inset,
				CellSize - inset * 2,
				CellSize - inset * 2
			);
			DrawRect(rect, color);

			// Draw type indicator
			DrawBlockTypeIndicator(block, rect, color);

			// Rooting visual: corner anchors
			DrawRootingVisual(block, rect);

			// Frozen/stunned overlay
			if (block.IsStunned)
				DrawFrozenOverlay(block, rect);

			// Combat threat indicators (red corners for adjacent soldiers)
			if (!block.IsStunned && block.Type != BlockType.Soldier)
				DrawThreatIndicators(block, rect);
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

	private void DrawRootingVisual(Block block, Rect2 rect)
	{
		if (block.State == BlockState.Rooting || block.State == BlockState.Rooted)
		{
			float progress = (float)block.RootProgress / Constants.RootTicks;
			var anchorLen = rect.Size.X * 0.35f * progress;
			var anchorColor = block.IsFullyRooted
				? new Color(0.6f, 0.6f, 0.6f, 0.8f)
				: new Color(0.5f, 0.5f, 0.5f, 0.1f + 0.3f * progress);
			var w = 1.5f + progress;
			DrawCornerBrackets(rect, anchorLen, anchorColor, w);
		}
		else if (block.State == BlockState.Uprooting)
		{
			float progress = (float)block.RootProgress / Constants.RootTicks;
			var anchorLen = rect.Size.X * 0.35f * progress;
			var anchorColor = new Color(0.5f, 0.5f, 0.5f, 0.3f * progress);
			DrawCornerBrackets(rect, anchorLen, anchorColor, 1.5f);
		}
	}

	private void DrawCornerBrackets(Rect2 rect, float len, Color color, float width)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		var br = rect.End;

		DrawLine(tl, tl + new Vector2(len, 0), color, width);
		DrawLine(tl, tl + new Vector2(0, len), color, width);
		DrawLine(tr, tr + new Vector2(-len, 0), color, width);
		DrawLine(tr, tr + new Vector2(0, len), color, width);
		DrawLine(bl, bl + new Vector2(len, 0), color, width);
		DrawLine(bl, bl + new Vector2(0, -len), color, width);
		DrawLine(br, br + new Vector2(-len, 0), color, width);
		DrawLine(br, br + new Vector2(0, -len), color, width);
	}

	private void DrawFrozenOverlay(Block block, Rect2 rect)
	{
		// Ice blue overlay pulsing 15-25% alpha
		float pulse = 0.15f + 0.10f * Mathf.Sin((float)Time.GetTicksMsec() * 0.004f);
		var iceColor = new Color(0.55f, 0.78f, 1f, pulse);
		DrawRect(rect, iceColor);

		// Crystalline border pulsing 30-50% alpha
		float borderPulse = 0.30f + 0.20f * Mathf.Sin((float)Time.GetTicksMsec() * 0.003f);
		var borderColor = new Color(0.55f, 0.82f, 1f, borderPulse);
		DrawRect(rect, borderColor, false, 2f);

		// Frost crack lines from edges inward
		var center = rect.GetCenter();
		float crackLen = rect.Size.X * 0.35f;
		var crackColor = new Color(0.7f, 0.9f, 1f, 0.3f);

		// Cycle: show 3 of 4 crack sets
		int frame = (int)((float)Time.GetTicksMsec() * 0.002f) % 4;
		var midTop = new Vector2(center.X, rect.Position.Y);
		var midRight = new Vector2(rect.End.X, center.Y);
		var midBottom = new Vector2(center.X, rect.End.Y);
		var midLeft = new Vector2(rect.Position.X, center.Y);

		if (frame != 0) DrawLine(midTop, midTop + new Vector2(0, crackLen), crackColor, 1f);
		if (frame != 1) DrawLine(midRight, midRight + new Vector2(-crackLen, 0), crackColor, 1f);
		if (frame != 2) DrawLine(midBottom, midBottom + new Vector2(0, -crackLen), crackColor, 1f);
		if (frame != 3) DrawLine(midLeft, midLeft + new Vector2(crackLen, 0), crackColor, 1f);
	}

	private void DrawThreatIndicators(Block block, Rect2 rect)
	{
		// Count adjacent enemy soldiers
		int soldiers = 0;
		bool useAll8 = block.State is BlockState.Rooted or BlockState.Rooting or BlockState.Uprooting
					   || block.IsInFormation;
		var offsets = useAll8 ? GridPos.AllOffsets : GridPos.OrthogonalOffsets;

		foreach (var offset in offsets)
		{
			var neighbor = block.Pos + offset;
			var other = _gameState!.GetBlockAt(neighbor);
			if (other != null && other.PlayerId != block.PlayerId && other.Type == BlockType.Soldier)
				soldiers++;
		}

		if (soldiers == 0) return;

		// Pulsing red corners (0.55→1.0 brightness oscillation)
		float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.006f));
		var red = new Color(1f, 0.15f, 0.1f, pulse * 0.8f);
		var glowRed = new Color(1f, 0.15f, 0.1f, pulse * 0.3f);
		float cornerLen = 5f;

		// Corners light up clockwise: TL(1), TR(2), BR(3)
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var br = rect.End;

		if (soldiers >= 1)
		{
			DrawLine(tl, tl + new Vector2(cornerLen, 0), red, 2.5f);
			DrawLine(tl, tl + new Vector2(0, cornerLen), red, 2.5f);
			DrawLine(tl, tl + new Vector2(cornerLen, 0), glowRed, 5f);
			DrawLine(tl, tl + new Vector2(0, cornerLen), glowRed, 5f);
		}
		if (soldiers >= 2)
		{
			DrawLine(tr, tr + new Vector2(-cornerLen, 0), red, 2.5f);
			DrawLine(tr, tr + new Vector2(0, cornerLen), red, 2.5f);
			DrawLine(tr, tr + new Vector2(-cornerLen, 0), glowRed, 5f);
			DrawLine(tr, tr + new Vector2(0, cornerLen), glowRed, 5f);
		}
		if (soldiers >= 3)
		{
			DrawLine(br, br + new Vector2(-cornerLen, 0), red, 2.5f);
			DrawLine(br, br + new Vector2(0, -cornerLen), red, 2.5f);
			DrawLine(br, br + new Vector2(-cornerLen, 0), glowRed, 5f);
			DrawLine(br, br + new Vector2(0, -cornerLen), glowRed, 5f);
		}
	}

	private void DrawWardenZoC()
	{
		foreach (var block in _gameState!.Blocks)
		{
			if (block.Type != BlockType.Warden) continue;
			if (!block.IsFullyRooted || block.IsStunned) continue;

			var center = GridToWorld(block.Pos);
			var playerColor = GetPlayerColor(block.PlayerId);

			// Expanding pulse wave (2500ms cycle)
			float cycle = ((float)Time.GetTicksMsec() % 2500f) / 2500f;
			float radius = Constants.WardenZocRadius * CellSize * cycle * 1.1f; // slight overshoot

			// Gaussian-shaped ring
			float ringWidth = CellSize * 0.8f;
			float alpha = 0.14f * Mathf.Exp(-2f * Mathf.Pow(cycle - 0.5f, 2) / 0.1f);
			alpha = Mathf.Max(alpha, 0.04f);

			var zocColor = playerColor with { A = alpha };
			DrawArc(center, radius, 0, Mathf.Tau, 32, zocColor, ringWidth);

			// Base aura at low alpha
			DrawArc(center, Constants.WardenZocRadius * CellSize, 0, Mathf.Tau, 32,
				playerColor with { A = 0.05f }, 1.5f);
		}
	}

	private void DrawRays()
	{
		foreach (var ray in _gameState!.Rays)
		{
			// Blue tint for stun, orange for blast
			Color baseColor = ray.Type == RayType.Stun
				? new Color(0.3f, 0.5f, 1f)
				: new Color(1f, 0.5f, 0.2f);

			float alpha;
			if (ray.IsExpired)
			{
				// Fading
				alpha = 0.5f * ((float)ray.FadeTicks / Constants.StunRayFade);
			}
			else
			{
				alpha = 0.6f;
			}

			if (alpha <= 0.01f) continue;

			var offset = ray.Direction.ToOffset();
			var origin = GridToWorld(ray.Origin);

			// Draw ray body: line from origin to head
			for (int i = 0; i <= ray.Distance; i++)
			{
				var cellPos = new GridPos(ray.Origin.X + offset.X * i, ray.Origin.Y + offset.Y * i);
				var worldPos = GridToWorld(cellPos);

				// Traveling pulse wave (800ms cycle, 1D Gaussian ring)
				float pulse = ((float)Time.GetTicksMsec() % 800f) / 800f;
				float distNorm = ray.Distance > 0 ? (float)i / ray.Distance : 0;
				float ringStr = Mathf.Exp(-8f * Mathf.Pow(distNorm - pulse, 2));
				float cellAlpha = alpha * (0.15f + 0.55f * ringStr);

				var cellCenter = worldPos;
				var rayColor = baseColor with { A = cellAlpha };

				// Draw glow at cell
				DrawCircle(cellCenter, CellSize * 0.3f, rayColor);
			}

			// Head glow (brighter)
			var headWorld = GridToWorld(ray.HeadPos);
			DrawCircle(headWorld, CellSize * 0.4f, baseColor with { A = alpha * 0.7f });
		}
	}

	private void DrawPushWaves()
	{
		foreach (var wave in _gameState!.PushWaves)
		{
			float alpha;
			if (wave.IsExpired)
				alpha = 0.4f * ((float)wave.FadeTicks / Constants.PushWaveFade);
			else
				alpha = 0.4f;

			if (alpha <= 0.01f) continue;

			var playerColor = GetPlayerColor(wave.PlayerId);
			// Teal/cyan tint
			var waveColor = new Color(
				playerColor.R * 0.3f + 0.1f,
				playerColor.G * 0.3f + 0.6f,
				playerColor.B * 0.3f + 0.6f,
				alpha);

			var offset = wave.Direction.ToOffset();

			// Draw chevrons along wave path
			for (int i = 0; i <= wave.Distance; i++)
			{
				var cellPos = new GridPos(wave.Origin.X + offset.X * i, wave.Origin.Y + offset.Y * i);
				var center = GridToWorld(cellPos);
				DrawChevron(center, wave.Direction, waveColor, CellSize * 0.22f);
			}

			// Outer glow at head
			if (!wave.IsExpired)
			{
				var headCenter = GridToWorld(wave.HeadPos);
				DrawCircle(headCenter, CellSize * 0.7f, waveColor with { A = alpha * 0.15f });
			}
		}
	}

	private void DrawChevron(Vector2 center, Direction dir, Color color, float armLen)
	{
		// Double chevron pointing in push direction
		Vector2 fwd, perp;
		switch (dir)
		{
			case Direction.Right: fwd = Vector2.Right; perp = Vector2.Up; break;
			case Direction.Left:  fwd = Vector2.Left;  perp = Vector2.Up; break;
			case Direction.Down:  fwd = Vector2.Down;  perp = Vector2.Right; break;
			default:              fwd = Vector2.Up;    perp = Vector2.Right; break;
		}

		for (int c = 0; c < 2; c++)
		{
			var tip = center + fwd * (armLen * 0.4f + c * armLen * 0.6f);
			var arm1 = tip - fwd * armLen + perp * armLen * 0.5f;
			var arm2 = tip - fwd * armLen - perp * armLen * 0.5f;
			DrawLine(arm1, tip, color, 2f);
			DrawLine(arm2, tip, color, 2f);
		}
	}

	private void DrawFormations()
	{
		foreach (var formation in _gameState!.Formations)
		{
			var formColor = new Color(0.2f, 0.8f, 0.2f); // Supply = green
			float alpha = formation.TeardownTimer > 0 ? 0.4f : 0.8f;

			// Draw member connections and borders
			var members = formation.MemberIds
				.Select(id => _gameState.GetBlock(id))
				.Where(b => b != null)
				.ToList();

			foreach (var block in members)
			{
				var rect = new Rect2(
					block!.Pos.X * CellSize, block.Pos.Y * CellSize,
					CellSize, CellSize);

				// Double-stroke border
				DrawRect(rect, formColor with { A = alpha * 0.35f }, false, 4f);
				DrawRect(rect, formColor with { A = alpha }, false, 1.5f);

				// Corner brackets
				DrawCornerBrackets(rect, 4f, formColor with { A = alpha }, 1.5f);
			}

			// Member connectors (dashed lines)
			for (int i = 0; i < members.Count; i++)
			{
				for (int j = i + 1; j < members.Count; j++)
				{
					var from = GridToWorld(members[i]!.Pos);
					var to = GridToWorld(members[j]!.Pos);
					DrawDashedLine(from, to, formColor with { A = alpha * 0.4f }, 1f, 3f, 4f);
				}
			}

			// Center diamond (rotating)
			if (members.Count > 0)
			{
				var avgPos = members.Aggregate(Vector2.Zero,
					(sum, b) => sum + GridToWorld(b!.Pos)) / members.Count;
				float angle = (float)Time.GetTicksMsec() * 0.002f;
				float dSize = CellSize * 0.18f;
				var pts = new Vector2[4];
				for (int i = 0; i < 4; i++)
				{
					float a = angle + i * Mathf.Pi / 2;
					pts[i] = avgPos + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * dSize;
				}
				DrawColoredPolygon(pts, formColor with { A = alpha * 0.6f });
			}

			// Tearing down indicator
			if (formation.TeardownTimer > 0)
			{
				float tearProgress = (float)formation.TeardownTimer / Constants.TeardownTicks;
				foreach (var block in members)
				{
					var rect = new Rect2(
						block!.Pos.X * CellSize + CellSize * (1 - tearProgress) * 0.5f,
						block.Pos.Y * CellSize + CellSize * (1 - tearProgress) * 0.5f,
						CellSize * tearProgress,
						CellSize * tearProgress);
					DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
				}
			}
		}

		// Same for nests
		foreach (var nest in _gameState.Nests)
		{
			Color nestColor = nest.Type switch
			{
				NestType.Builder => new Color(0.4f, 0.7f, 1f),
				NestType.Soldier => new Color(1f, 0.6f, 0.2f),
				NestType.Stunner => new Color(0.8f, 0.3f, 1f),
				_ => Colors.White
			};

			float alpha = nest.TeardownTimer > 0 ? 0.4f : 0.7f;

			var members = nest.MemberIds
				.Select(id => _gameState.GetBlock(id))
				.Where(b => b != null)
				.ToList();

			foreach (var block in members)
			{
				var rect = new Rect2(
					block!.Pos.X * CellSize, block.Pos.Y * CellSize,
					CellSize, CellSize);
				DrawRect(rect, nestColor with { A = alpha * 0.35f }, false, 4f);
				DrawRect(rect, nestColor with { A = alpha }, false, 1.5f);
			}

			// Member connectors
			for (int i = 0; i < members.Count; i++)
			{
				for (int j = i + 1; j < members.Count; j++)
				{
					var from = GridToWorld(members[i]!.Pos);
					var to = GridToWorld(members[j]!.Pos);
					DrawDashedLine(from, to, nestColor with { A = alpha * 0.4f }, 1f, 3f, 4f);
				}
			}
		}
	}

	private void DrawNestProgress()
	{
		foreach (var nest in _gameState!.Nests)
		{
			if (nest.IsPaused) continue;
			if (nest.SpawnProgress <= 0) continue;

			var center = GridToWorld(nest.Center);
			int spawnTicks = nest.GetSpawnTicks(
				_gameState.Grid.InBounds(nest.Center) ? _gameState.Grid[nest.Center].Ground : GroundType.Normal);
			if (spawnTicks <= 0) continue;

			float progress = (float)nest.SpawnProgress / spawnTicks;

			// Draw progress bar below nest center
			var barWidth = CellSize * 1.2f;
			var barHeight = 3f;
			var barPos = new Vector2(center.X - barWidth / 2, center.Y + CellSize * 0.7f);

			// Background
			DrawRect(new Rect2(barPos, new Vector2(barWidth, barHeight)),
				new Color(0.1f, 0.1f, 0.1f, 0.6f));

			// Fill
			Color nestColor = nest.Type switch
			{
				NestType.Builder => new Color(0.4f, 0.7f, 1f),
				NestType.Soldier => new Color(1f, 0.6f, 0.2f),
				NestType.Stunner => new Color(0.8f, 0.3f, 1f),
				_ => Colors.White
			};
			DrawRect(new Rect2(barPos, new Vector2(barWidth * progress, barHeight)), nestColor);
		}
	}

	private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float lineWidth, float dashLen, float gapLen)
	{
		var dir = to - from;
		var totalLen = dir.Length();
		if (totalLen < 0.01f) return;
		dir /= totalLen;

		float pos = 0;
		bool drawing = true;
		while (pos < totalLen)
		{
			var segLen = Mathf.Min(drawing ? dashLen : gapLen, totalLen - pos);
			if (drawing)
				DrawLine(from + dir * pos, from + dir * (pos + segLen), color, lineWidth);
			pos += segLen;
			drawing = !drawing;
		}
	}

	private void DrawDeathEffects()
	{
		float now = (float)Time.GetTicksMsec() / 1000f;

		// Phase 1+2: radial glow burst
		foreach (var effect in _deathEffects)
		{
			float progress = (now - effect.StartTime) / effect.Duration;
			if (progress > 1f) continue;

			if (progress < 0.3f)
			{
				// Phase 1: Inflation + white flash
				float p1 = progress / 0.3f;
				float scale = 1f + 0.15f * p1;
				float flashAlpha = 0.5f * p1;
				var size = CellSize * scale;
				var rect = new Rect2(effect.Pos - Vector2.One * size / 2, Vector2.One * size);
				DrawRect(rect, effect.Color with { A = 0.6f * (1f - progress) });
				DrawRect(rect, Colors.White with { A = flashAlpha });
			}
			else
			{
				// Phase 2: Expanding radial glow
				float p2 = (progress - 0.3f) / 0.7f;
				float radius = CellSize * (0.3f + 1.0f * p2);
				float alpha = 0.5f * (1f - p2);
				DrawCircle(effect.Pos, radius, effect.Color with { A = alpha });
			}
		}

		// Fragments
		foreach (var frag in _fragments)
		{
			float age = now - frag.StartTime;
			if (age > 0.8f) continue;

			float progress = age / 0.8f;
			var pos = frag.Pos + frag.Velocity * age + new Vector2(0, 40f * age * age); // gravity
			float alpha = 1f - progress;
			float size = 2f + (frag.RotSpeed % 3f); // 2-5px

			DrawRect(new Rect2(pos - Vector2.One * size / 2, Vector2.One * size),
				frag.Color with { A = alpha });
		}
	}

	// Sprite cache: loaded once, null = no sprite found
	private readonly Dictionary<BlockType, Texture2D?> _blockSprites = [];
	private bool _spritesLoaded;

	/// <summary>
	/// Try to load a sprite for a block type. Falls back to procedural rendering.
	/// Sprites are expected at res://Assets/Sprites/Blocks/{Type}.png
	/// </summary>
	private Texture2D? GetBlockSprite(BlockType type)
	{
		if (!_spritesLoaded)
		{
			_spritesLoaded = true;
			foreach (BlockType bt in Enum.GetValues<BlockType>())
			{
				var path = $"res://Assets/Sprites/Blocks/{bt}.png";
				_blockSprites[bt] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
			}
		}
		return _blockSprites.GetValueOrDefault(type);
	}

	private void DrawBlockTypeIndicator(Block block, Rect2 rect, Color color)
	{
		var center = rect.GetCenter();
		float time = (float)Time.GetTicksMsec() / 1000f;

		// Check for sprite override
		var sprite = GetBlockSprite(block.Type);
		if (sprite != null)
		{
			DrawTextureRect(sprite, rect, false, color);
			return;
		}

		// Procedural rendering by type
		switch (block.Type)
		{
			case BlockType.Wall:
				DrawRect(rect, color.Darkened(0.3f));
				break;

			case BlockType.Soldier:
				DrawSoldierAnimated(block, rect, center, color, time);
				break;

			case BlockType.Stunner:
				DrawStunnerAnimated(rect, center, color, time);
				break;

			case BlockType.Warden:
				DrawWardenAnimated(rect, center, color, time);
				break;

			case BlockType.Jumper:
				DrawJumperAnimated(block, rect, center, color, time);
				break;
		}
	}

	/// <summary>
	/// Soldier: 4 diagonal gold sword arms rotating around center.
	/// HP-based arm visibility (4 arms at full HP, disappearing BR→BL→TL→TR).
	/// Periodic 500ms spin every 4 seconds, staggered per block.
	/// </summary>
	private void DrawSoldierAnimated(Block block, Rect2 rect, Vector2 center, Color color, float time)
	{
		var gold = new Color(0.85f, 0.75f, 0.3f);
		float armLen = rect.Size.X * 0.35f;

		// Stagger by block ID so they don't all spin in sync
		float stagger = (block.Id % 12) * 0.33f;
		float cycle = (time + stagger) % 4f;
		float spinAngle = 0;
		if (cycle < 0.5f)
		{
			// 500ms ease-in-out spin
			float t = cycle / 0.5f;
			float ease = t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
			spinAngle = ease * Mathf.Pi * 0.5f;
		}

		// Base angle for diagonal arms
		float[] armAngles = [Mathf.Pi * 0.25f, Mathf.Pi * 0.75f, Mathf.Pi * 1.25f, Mathf.Pi * 1.75f]; // TL, TR, BR, BL
		// Arms disappear order: BR(2) → BL(3) → TL(0) → TR(1)
		int[] disappearOrder = [2, 3, 0, 1];
		int visibleArms = Mathf.Clamp(block.Hp, 0, 4);

		for (int i = 0; i < 4; i++)
		{
			// Check if this arm is visible based on HP
			bool visible = true;
			int armIndex = Array.IndexOf(disappearOrder, i);
			if (armIndex >= 4 - visibleArms) { } // visible
			else visible = false;
			// Correction: arms disappear from index 0 of disappearOrder
			visible = armIndex >= (4 - visibleArms);

			if (!visible) continue;

			float angle = armAngles[i] + spinAngle;
			var tip = center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * armLen;
			DrawLine(center, tip, gold, 2.5f);
			// Small triangle at tip for "sword"
			var perpAngle = angle + Mathf.Pi / 2;
			var t1 = tip + new Vector2(Mathf.Cos(perpAngle), Mathf.Sin(perpAngle)) * 2f;
			var t2 = tip - new Vector2(Mathf.Cos(perpAngle), Mathf.Sin(perpAngle)) * 2f;
			var t3 = tip + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * 3f;
			DrawColoredPolygon([t1, t2, t3], gold);
		}
	}

	/// <summary>
	/// Stunner: Outer diamond (player color) + inner diamond (white highlight).
	/// 4-second spin cycle matching Soldier timing.
	/// </summary>
	private void DrawStunnerAnimated(Rect2 rect, Vector2 center, Color color, float time)
	{
		float halfSize = rect.Size.X * 0.35f;
		float innerSize = halfSize * 0.55f;

		float cycle = time % 4f;
		float spinAngle = 0;
		if (cycle < 0.5f)
		{
			float t = cycle / 0.5f;
			float ease = t < 0.5f ? 2 * t * t : 1 - Mathf.Pow(-2 * t + 2, 2) / 2;
			spinAngle = ease * Mathf.Pi * 0.5f;
		}

		// Outer diamond (player color)
		DrawRotatedDiamond(center, halfSize, spinAngle, color.Lightened(0.1f));
		// Inner diamond (white highlight)
		DrawRotatedDiamond(center, innerSize, spinAngle, Colors.White with { A = 0.5f });
	}

	private void DrawRotatedDiamond(Vector2 center, float size, float angle, Color color)
	{
		var pts = new Vector2[4];
		for (int i = 0; i < 4; i++)
		{
			float a = angle + i * Mathf.Pi / 2;
			pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * size;
		}
		DrawColoredPolygon(pts, color);
	}

	/// <summary>
	/// Warden: Periodic 500ms flash every 4 seconds. Breathing bob ±2%.
	/// Scale pulses with flash intensity. Radial glow at flash moment.
	/// </summary>
	private void DrawWardenAnimated(Rect2 rect, Vector2 center, Color color, float time)
	{
		float halfSize = rect.Size.X * 0.3f;

		// 4-second cycle with 500ms flash
		float cycle = time % 4f;
		float flashIntensity = 0;
		if (cycle < 0.5f)
		{
			float t = cycle / 0.5f;
			// Cubic ease in-out
			flashIntensity = t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
		}

		// Breathing bob: ±2% vertical sway
		float bob = Mathf.Sin(time * 1.5f) * halfSize * 0.04f;
		var bobCenter = center + new Vector2(0, bob);

		// Scale pulse with flash
		float scale = 1f + flashIntensity * 0.08f;
		float r = halfSize * scale;

		// Shield arc
		var lightened = color.Lightened(0.3f + flashIntensity * 0.3f);
		DrawArc(bobCenter, r, 0, Mathf.Tau, 20, lightened, 2.5f);

		// Inner fill
		DrawCircle(bobCenter, r * 0.6f, color with { A = 0.4f + flashIntensity * 0.2f });

		// Radial glow at flash
		if (flashIntensity > 0.1f)
			DrawCircle(bobCenter, r * 1.4f, color with { A = flashIntensity * 0.15f });
	}

	/// <summary>
	/// Jumper: Radial gradient circle. HP scaling: 3=100%, 2=60%, 1=20%.
	/// Slow 2-second pulse glow.
	/// </summary>
	private void DrawJumperAnimated(Block block, Rect2 rect, Vector2 center, Color color, float time)
	{
		float maxSize = rect.Size.X * 0.38f;

		// HP scaling
		float hpScale = block.Hp switch
		{
			>= 3 => 1f,
			2 => 0.6f,
			1 => 0.2f,
			_ => 0.1f
		};
		float radius = maxSize * hpScale;

		// 2-second pulse glow
		float pulse = 0.5f + 0.5f * Mathf.Sin(time * Mathf.Pi);

		// Radial gradient: bright highlight → core → dark rim
		var bright = color.Lightened(0.5f);
		var core = color;
		var dark = color.Darkened(0.3f);

		// Outer ring (dark rim)
		DrawCircle(center, radius, dark);
		// Core
		DrawCircle(center, radius * 0.7f, core);
		// Bright highlight
		DrawCircle(center, radius * 0.35f, bright with { A = 0.6f + pulse * 0.3f });

		// Pulse glow overlay
		DrawCircle(center, radius * 1.2f, color with { A = 0.05f + pulse * 0.08f });
	}

	public static Vector2 GridToWorld(GridPos pos) =>
		new(pos.X * CellSize + CellSize * 0.5f, pos.Y * CellSize + CellSize * 0.5f);

	public static GridPos WorldToGrid(Vector2 world) =>
		new((int)Mathf.Floor(world.X / CellSize), (int)Mathf.Floor(world.Y / CellSize));

	private static Color GetGroundColor(GroundType ground) => ground switch
	{
		GroundType.Boot => BootColor,
		GroundType.Overload => OverloadColor,
		GroundType.Proto => ProtoColor,
		GroundType.Terrain => TerrainColor,
		GroundType.BreakableWall => BreakableWallColor,
		GroundType.FragileWall => FragileWallColor,
		_ => NormalColor
	};

	private static Color GetPlayerColor(int playerId) =>
		playerId >= 0 && playerId < PlayerColors.Length ? PlayerColors[playerId] : Colors.White;
}
