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

	private void DrawRootingVisual(Block block, Rect2 rect)
	{
		var bracketBase = _config.GetPalette(block.PlayerId).RootingBracketColor;
		if (block.State == BlockState.Rooting || block.State == BlockState.Rooted)
		{
			float progress = (float)block.RootProgress / Constants.RootTicks;
			var anchorLen = rect.Size.X * 0.35f * progress;
			var anchorColor = block.IsFullyRooted
				? bracketBase with { A = 0.8f }
				: bracketBase with { A = 0.1f + 0.3f * progress };
			var w = 1.5f + progress;
			DrawCornerBrackets(rect, anchorLen, anchorColor, w);
		}
		else if (block.State == BlockState.Uprooting)
		{
			float progress = (float)block.RootProgress / Constants.RootTicks;
			var anchorLen = rect.Size.X * 0.35f * progress;
			var anchorColor = bracketBase with { A = 0.3f * progress };
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

	/// <summary>
	/// Corner ticks that point outward from each corner of the rect.
	/// Used by formation visuals to "stick out" beyond the block outline.
	/// </summary>
	private void DrawOuterCornerTicks(Rect2 rect, float len, Color color, float width)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		var br = rect.End;

		DrawLine(tl, tl + new Vector2(-len, 0), color, width);
		DrawLine(tl, tl + new Vector2(0, -len), color, width);
		DrawLine(tr, tr + new Vector2(len, 0), color, width);
		DrawLine(tr, tr + new Vector2(0, -len), color, width);
		DrawLine(bl, bl + new Vector2(-len, 0), color, width);
		DrawLine(bl, bl + new Vector2(0, len), color, width);
		DrawLine(br, br + new Vector2(len, 0), color, width);
		DrawLine(br, br + new Vector2(0, len), color, width);
	}

	private void DrawFrozenOverlay(Block block, Rect2 rect)
	{
		// Ice blue overlay pulsing 15-25% alpha
		float pulse = 0.15f + 0.10f * Mathf.Sin((float)Time.GetTicksMsec() * 0.004f);
		var iceColor = _config.FrozenOverlayColor with { A = pulse };
		DrawRect(rect, iceColor);

		// Crystalline border pulsing 30-50% alpha
		float borderPulse = 0.30f + 0.20f * Mathf.Sin((float)Time.GetTicksMsec() * 0.003f);
		var borderColor = _config.FrozenBorderColor with { A = borderPulse };
		DrawRect(rect, borderColor, false, 2f);

		// Frost crack lines from edges inward
		var center = rect.GetCenter();
		float crackLen = rect.Size.X * 0.35f;
		var crackColor = _config.FrostCrackColor;

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

		// Pulsing threat corners (0.55→1.0 brightness oscillation)
		float pulse = 0.55f + 0.45f * (0.5f + 0.5f * Mathf.Sin((float)Time.GetTicksMsec() * 0.006f));
		var threatColor = _config.ThreatIndicatorColor;
		var red = threatColor with { A = pulse * 0.8f };
		var glowRed = threatColor with { A = pulse * 0.3f };
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
		var palette = _config.GetPalette(block.PlayerId);
		switch (block.Type)
		{
			case BlockType.Wall:
				DrawWallBlock(rect, palette);
				break;

			case BlockType.Builder:
				DrawGradientBody(rect, palette.BuilderFill, palette.BuilderGradientLight, palette.BuilderGradientDark);
				break;

			case BlockType.Soldier:
				DrawGradientBody(rect, palette.SoldierFill, palette.SoldierFill.Lightened(0.15f), palette.SoldierFill.Darkened(0.15f));
				DrawSoldierAnimated(block, rect, center, palette, time);
				break;

			case BlockType.Stunner:
				DrawStunnerBody(rect, palette);
				DrawStunnerAnimated(rect, center, palette, time);
				break;

			case BlockType.Warden:
				DrawWardenAnimated(rect, center, palette, time);
				break;

			case BlockType.Jumper:
				DrawJumperAnimated(block, rect, center, palette, time);
				break;
		}
	}

	/// <summary>Wall: solid dark square with inner bevel for depth.</summary>
	private void DrawWallBlock(Rect2 rect, PlayerPalette palette)
	{
		// Solid dark fill
		DrawRect(rect, palette.WallFill);
		// Inner bevel: lighter top-left edge, darker bottom-right
		DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), palette.WallHighlight, 1.5f);
		DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), palette.WallHighlight, 1.5f);
		DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), palette.WallShadow, 1.5f);
		DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), palette.WallShadow, 1.5f);
		// Small inner rect for depth
		var inner = rect.Grow(-3f);
		DrawRect(inner, palette.WallInner);
	}

	/// <summary>Top-left lit gradient body. Used for Builder and Soldier.</summary>
	private void DrawGradientBody(Rect2 rect, Color fill, Color light, Color dark)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var br = rect.End;
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		DrawPolygon(
			new Vector2[] { tl, tr, br, bl },
			new Color[] { light, fill.Lightened(0.05f), dark, fill.Lightened(0.05f) });
	}

	/// <summary>Beveled square body for Stunner: lighter top/left edge, darker bottom/right.</summary>
	private void DrawStunnerBody(Rect2 rect, PlayerPalette palette)
	{
		DrawGradientBody(rect, palette.StunnerFill, palette.StunnerBevelLight, palette.StunnerBevelShadow);
		// Lighter bevel on top and left edges
		DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), palette.StunnerBevelLight with { A = 0.7f }, 1.5f);
		DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), palette.StunnerBevelLight with { A = 0.7f }, 1.5f);
		// Darker bevel on bottom and right edges
		DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), palette.StunnerBevelShadow with { A = 0.7f }, 1.5f);
		DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), palette.StunnerBevelShadow with { A = 0.7f }, 1.5f);
	}

	/// <summary>
	/// Soldier: two thick diagonal lines forming an X (crossed swords).
	/// HP controls how many arms (4→2→1→0). Staggered fast spin.
	/// </summary>
	private void DrawSoldierAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		var gold = palette.SoldierArmsColor;
		float armLen = rect.Size.X * 0.42f; // reach toward corners

		// Fast spin with hard acceleration: period 2.5s, spin lasts 0.35s
		float stagger = (block.Id % 8) * 0.31f;
		float cycle = (time + stagger) % 2.5f;
		float spinAngle = 0f;
		if (cycle < 0.35f)
		{
			float t = cycle / 0.35f;
			// Cubic ease in-out
			float ease = t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
			spinAngle = ease * Mathf.Pi * 0.5f;
		}

		int visibleArms = Mathf.Clamp(block.Hp, 0, 4);
		if (visibleArms == 0) return;

		// X = two lines at 45° and 135°, each going both ways from center
		// Arm 0: NE-SW axis   Arm 1: NW-SE axis   (HP drops remove arms pairwise)
		bool showAxis0 = visibleArms >= 2; // NE + SW
		bool showAxis1 = visibleArms >= 1; // NW + SE always last

		float a0 = Mathf.Pi * 0.25f + spinAngle;  // NE direction
		float a1 = Mathf.Pi * 0.75f + spinAngle;  // NW direction

		// Draw glow layer first, then solid line on top
		if (showAxis0)
		{
			var ne = center + new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * armLen;
			var sw = center - new Vector2(Mathf.Cos(a0), Mathf.Sin(a0)) * armLen;
			DrawLine(ne, sw, palette.SoldierArmsGlow, 8f);
			DrawLine(ne, sw, gold, 3f);
		}
		if (showAxis1)
		{
			var nw = center + new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * armLen;
			var se = center - new Vector2(Mathf.Cos(a1), Mathf.Sin(a1)) * armLen;
			DrawLine(nw, se, palette.SoldierArmsGlow, 8f);
			DrawLine(nw, se, gold, 3f);
		}

		// Bright center dot
		DrawCircle(center, 2.5f, palette.SoldierCenterDot);
	}

	/// <summary>
	/// Stunner: Outer diamond (player color) + inner diamond (white highlight).
	/// Periodic 4-second spin cycle matching Soldier timing. Subtle pulse glow.
	/// </summary>
	private void DrawStunnerAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		float size = rect.Size.X * 0.22f;
		float pulse = 0.85f + 0.15f * Mathf.Sin(time * 2.5f);

		// Periodic spin: same timing as soldier (4s cycle, brief spin)
		float cycle = time % 4f;
		float spinAngle = 0f;
		if (cycle < 0.5f)
		{
			float t = cycle / 0.5f;
			float ease = t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
			spinAngle = ease * Mathf.Pi * 0.5f;
		}

		// Soft glow circle behind diamond
		DrawCircle(center, size * 1.6f, palette.StunnerGlow with { A = 0.12f * pulse });
		// Outer diamond (team color, lightened)
		DrawRotatedDiamond(center, size, spinAngle, palette.StunnerDiamondOuter with { A = pulse });
		// Inner diamond (white highlight, static orientation)
		DrawRotatedDiamond(center, size * 0.5f, 0, palette.StunnerDiamondInner with { A = 0.5f * pulse });
		// Tiny bright center dot
		DrawCircle(center, 1.5f, Colors.White with { A = 0.9f * pulse });
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
	/// Warden: Compact filled circle with ring border. Periodic flash.
	/// Looks like a shield/orb — matching reference visual.
	/// </summary>
	private void DrawWardenAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		float r = rect.Size.X * 0.32f;

		// 4-second cycle with 500ms flash
		float cycle = time % 4f;
		float flashIntensity = 0;
		if (cycle < 0.5f)
		{
			float t = cycle / 0.5f;
			flashIntensity = t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
		}

		// Breathing bob: ±2% vertical sway
		float bob = Mathf.Sin(time * 1.5f) * r * 0.04f;
		var bobCenter = center + new Vector2(0, bob);

		// Scale pulse with flash
		float scale = 1f + flashIntensity * 0.06f;
		float sr = r * scale;

		// Soft outer glow
		DrawCircle(bobCenter, sr * 1.3f, palette.WardenGlow with { A = 0.08f + flashIntensity * 0.1f });
		// Filled circle body
		DrawCircle(bobCenter, sr, palette.WardenFill with { A = 0.5f + flashIntensity * 0.2f });
		// Bright ring border
		var ringColor = palette.WardenRing.Lightened(flashIntensity * 0.3f);
		DrawArc(bobCenter, sr, 0, Mathf.Tau, 24, ringColor, 2f);
		// Inner bright highlight
		DrawCircle(bobCenter, sr * 0.4f, palette.WardenInnerHighlight with { A = 0.6f + flashIntensity * 0.3f });
	}

	/// <summary>
	/// Jumper: Radial gradient circle. HP scaling: 3=100%, 2=60%, 1=20%.
	/// Slow 2-second pulse glow.
	/// </summary>
	private void DrawJumperAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float time)
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
		// Outer ring (dark rim)
		DrawCircle(center, radius, palette.JumperDark);
		// Core
		DrawCircle(center, radius * 0.7f, palette.JumperCore);
		// Bright highlight
		DrawCircle(center, radius * 0.35f, palette.JumperBright with { A = 0.6f + pulse * 0.3f });

		// Pulse glow overlay
		DrawCircle(center, radius * 1.2f, palette.JumperPulseGlow with { A = 0.05f + pulse * 0.08f });
	}

	private void DrawChevron(Vector2 center, Direction dir, Color color, float armLen)
	{
		// Double chevron pointing in push direction
		Vector2 fwd, perp;
		switch (dir)
		{
			case Direction.Right: fwd = Vector2.Right; perp = Vector2.Up; break;
			case Direction.Left: fwd = Vector2.Left; perp = Vector2.Up; break;
			case Direction.Down: fwd = Vector2.Down; perp = Vector2.Right; break;
			default: fwd = Vector2.Up; perp = Vector2.Right; break;
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

}
