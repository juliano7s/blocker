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
		if (block.Type == BlockType.Wall)
			return;

		bool rooting = block.State == BlockState.Rooting;
		bool rooted = block.State == BlockState.Rooted;
		bool uprooting = block.State == BlockState.Uprooting;

		if (!rooting && !rooted && !uprooting)
			return;

		float progress = (float)block.RootProgress / Constants.RootTicks;
		var palette = _config.GetPalette(block.PlayerId);

		// 1) Corner brackets — grow while rooting, shrink while uprooting
		var bracketBase = palette.RootingBracketColor;
		float anchorLen = rect.Size.X * 0.35f * progress;
		if (rooted)
		{
			DrawCornerBrackets(rect, anchorLen, bracketBase with { A = 0.8f }, 2.5f);
		}
		else
		{
			float bracketAlpha = rooting ? 0.1f + 0.3f * progress : 0.2f + 0.6f * progress;
			float bracketWidth = rooting ? 1.5f + progress : 1.5f + progress;
			DrawCornerBrackets(rect, anchorLen, bracketBase with { A = bracketAlpha }, bracketWidth);
		}

		// 2) Spinning outline tracer — clockwise when rooting, counter-clockwise when uprooting
		if (!rooted)
		{
			float time = (float)Time.GetTicksMsec() / 1000f;
			float spinSpeed = rooting ? 1.5f : -1.5f;
			float tracerLen = 0.25f; // fraction of perimeter covered by the tracer
			DrawOutlineTracer(rect, time * spinSpeed, tracerLen, new Color(0.78f, 0.78f, 0.78f, 0.4f + 0.3f * progress), 1.5f);
		}

		// 3) Digital root tendrils — grow from corners along grid lines
		DrawRootTendrils(rect, progress, palette.Base);

		// 4) Diagonal stripes — scroll TL→BR when rooting, BR→TL when uprooting, static when rooted
		{
			float stripeAlpha = rooted ? 0.18f : 0.18f * progress;
			if (stripeAlpha > 0.005f)
			{
				float time = rooted ? 0f : (float)Time.GetTicksMsec() / 1000f;
				float stripeDirection = rooting ? -1f : 1f;
				DrawDiagonalStripes(rect, stripeAlpha, time, stripeDirection);
			}
		}
	}

	/// <summary>
	/// Draws a short bright segment that travels along the rect's perimeter.
	/// offset = current position along perimeter (0..1 wraps), length = fraction of perimeter to cover.
	/// </summary>
	private void DrawOutlineTracer(Rect2 rect, float timeOffset, float length, Color color, float width)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var br = rect.End;
		var bl = new Vector2(rect.Position.X, rect.End.Y);

		// Perimeter as 4 segments: top(0..w), right(w..w+h), bottom(w+h..2w+h), left(2w+h..perimeter)
		float w = rect.Size.X;
		float h = rect.Size.Y;
		float perimeter = 2 * (w + h);

		// Normalize timeOffset to 0..1 range
		float startT = timeOffset - Mathf.Floor(timeOffset);
		float endT = startT + length;

		// Walk the perimeter and draw the lit portion
		float[] segLengths = { w, h, w, h };
		Vector2[] segStarts = { tl, tr, br, bl };
		Vector2[] segEnds = { tr, br, bl, tl };

		float cumulative = 0;
		for (int i = 0; i < 4; i++)
		{
			float segLen = segLengths[i] / perimeter; // normalized segment length
			float segStart = cumulative;
			float segEnd = cumulative + segLen;
			cumulative = segEnd;

			// Check overlap with tracer range [startT, endT] (endT may exceed 1.0 for wrapping)
			DrawTracerOnSegment(segStarts[i], segEnds[i], segStart, segEnd, startT, endT, color, width);
			// Handle wrap-around (tracer crossing the 1.0 boundary)
			if (endT > 1f)
				DrawTracerOnSegment(segStarts[i], segEnds[i], segStart, segEnd, startT - 1f, endT - 1f, color, width);
		}
	}

	private void DrawTracerOnSegment(Vector2 from, Vector2 to, float segStart, float segEnd,
		float tracerStart, float tracerEnd, Color color, float width)
	{
		float overlapStart = Mathf.Max(segStart, tracerStart);
		float overlapEnd = Mathf.Min(segEnd, tracerEnd);
		if (overlapStart >= overlapEnd) return;

		float segLen = segEnd - segStart;
		if (segLen < 0.0001f) return;

		float t0 = (overlapStart - segStart) / segLen;
		float t1 = (overlapEnd - segStart) / segLen;
		DrawLine(from.Lerp(to, t0), from.Lerp(to, t1), color, width);
	}

	/// <summary>
	/// Digital root tendrils: segmented lines that grow from each block corner,
	/// first diagonally to the nearest grid intersection, then along the grid line.
	/// Thicker near the corner, tapering to thin. Team-colored, blending into the grid.
	/// </summary>
	private void DrawRootTendrils(Rect2 rect, float progress, Color teamColor)
	{
		if (progress < 0.02f) return;

		float inset = BlockInset;
		float maxGridLen = CellSize * 0.9f; // max reach along grid line after the diagonal
		float totalReach = inset * Mathf.Sqrt2 + maxGridLen; // diagonal + grid portion
		float tendrilLen = totalReach * progress;

		if (tendrilLen < 1f) return;

		// Grid intersections (cell corners) for each block corner
		var tl = rect.Position; // block corner (inset from grid)
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		var br = rect.End;

		// Grid intersections: block corner offset by inset diagonally outward
		var tlGrid = tl + new Vector2(-inset, -inset);
		var trGrid = tr + new Vector2(inset, -inset);
		var blGrid = bl + new Vector2(-inset, inset);
		var brGrid = br + new Vector2(inset, inset);

		// Each corner sends two tendrils: diagonal to grid corner, then along each grid line
		DrawTendril(tl, tlGrid, Vector2.Left, tendrilLen, totalReach, teamColor);
		DrawTendril(tl, tlGrid, Vector2.Up, tendrilLen, totalReach, teamColor);
		DrawTendril(tr, trGrid, Vector2.Right, tendrilLen, totalReach, teamColor);
		DrawTendril(tr, trGrid, Vector2.Up, tendrilLen, totalReach, teamColor);
		DrawTendril(bl, blGrid, Vector2.Left, tendrilLen, totalReach, teamColor);
		DrawTendril(bl, blGrid, Vector2.Down, tendrilLen, totalReach, teamColor);
		DrawTendril(br, brGrid, Vector2.Right, tendrilLen, totalReach, teamColor);
		DrawTendril(br, brGrid, Vector2.Down, tendrilLen, totalReach, teamColor);
	}

	/// <summary>
	/// Draws one tendril: diagonal from blockCorner to gridCorner, then along gridDir.
	/// The tendril is drawn as tapered segments with gaps for a digital feel.
	/// </summary>
	private void DrawTendril(Vector2 blockCorner, Vector2 gridCorner, Vector2 gridDir,
		float currentLen, float maxLen, Color color)
	{
		float diagLen = (gridCorner - blockCorner).Length();
		int segments = 6;
		float maxWidth = 2.5f;
		float minWidth = 0.5f;

		for (int i = 0; i < segments; i++)
		{
			float t0 = (float)i / segments;
			float t1 = (float)(i + 1) / segments;

			// Distance along the full tendril path
			float d0 = t0 * currentLen;
			float d1 = t1 * currentLen;

			// Small gap between segments
			float gap = (d1 - d0) * 0.15f;
			d1 -= gap;

			if (d1 <= d0) continue;

			// Map distance to world position: first diagonal, then along grid
			var p0 = TendrilPointAt(blockCorner, gridCorner, gridDir, diagLen, d0);
			var p1 = TendrilPointAt(blockCorner, gridCorner, gridDir, diagLen, d1);

			// Taper width and alpha from root to tip
			float width = Mathf.Lerp(maxWidth, minWidth, t0);
			float alpha = Mathf.Lerp(0.6f, 0.08f, t0);

			DrawLine(p0, p1, color with { A = alpha * 0.3f }, width + 2f);
			DrawLine(p0, p1, color with { A = alpha }, width);
		}
	}

	private static Vector2 TendrilPointAt(Vector2 blockCorner, Vector2 gridCorner,
		Vector2 gridDir, float diagLen, float dist)
	{
		if (dist <= diagLen)
		{
			// Still on the diagonal portion
			float t = diagLen > 0.001f ? dist / diagLen : 1f;
			return blockCorner.Lerp(gridCorner, t);
		}
		// Past the diagonal — continue along the grid line
		return gridCorner + gridDir * (dist - diagLen);
	}

	/// <summary>
	/// Draws dark diagonal stripes at 45° across the rect, scrolling in the given direction.
	/// </summary>
	private void DrawDiagonalStripes(Rect2 rect, float alpha, float time, float direction)
	{
		float stripeSpacing = 8f;
		float stripeWidth = 3f;
		float scrollSpeed = 8f * direction;
		float offset = time * scrollSpeed;

		var stripeColor = new Color(0.15f, 0.15f, 0.15f, alpha);

		// Diagonal stripes: lines from top-left to bottom-right (slope = -1 in local coords)
		// We sweep diagonal coordinate d = x + y, drawing lines where d hits the stripe grid
		float minD = offset;
		float maxD = rect.Size.X + rect.Size.Y + offset;

		float firstStripe = Mathf.Floor(minD / stripeSpacing) * stripeSpacing;

		for (float d = firstStripe; d <= maxD + stripeSpacing; d += stripeSpacing)
		{
			float localD = d - offset;
			// Line equation: x + y = localD
			// Entry: x=max(0, localD-h), y=localD-x  Exit: x=min(w, localD), y=localD-x
			float x0 = Mathf.Max(0, localD - rect.Size.Y);
			float y0 = localD - x0;
			float x1 = Mathf.Min(rect.Size.X, localD);
			float y1 = localD - x1;

			if (x0 >= rect.Size.X || x1 <= 0 || y0 < 0 || y1 >= rect.Size.Y)
				continue;

			var p0 = rect.Position + new Vector2(x0, y0);
			var p1 = rect.Position + new Vector2(x1, y1);
			DrawLine(p0, p1, stripeColor, stripeWidth);
		}
	}

	private void DrawCornerBrackets(Rect2 rect, float len, Color color, float width)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		var br = rect.End;

		// Extend each arm slightly past the corner to fill the gap where perpendicular lines meet
		float ext = width * 0.5f;

		DrawLine(tl + new Vector2(-ext, 0), tl + new Vector2(len, 0), color, width);
		DrawLine(tl + new Vector2(0, -ext), tl + new Vector2(0, len), color, width);
		DrawLine(tr + new Vector2(ext, 0), tr + new Vector2(-len, 0), color, width);
		DrawLine(tr + new Vector2(0, -ext), tr + new Vector2(0, len), color, width);
		DrawLine(bl + new Vector2(-ext, 0), bl + new Vector2(len, 0), color, width);
		DrawLine(bl + new Vector2(0, ext), bl + new Vector2(0, -len), color, width);
		DrawLine(br + new Vector2(ext, 0), br + new Vector2(-len, 0), color, width);
		DrawLine(br + new Vector2(0, ext), br + new Vector2(0, -len), color, width);
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
		float idleAngle = _idleAngles.GetValueOrDefault(block.Id, 0f);

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
				DrawBuilderBody(rect, center, palette, idleAngle);
				break;

			case BlockType.Soldier:
				DrawGradientBody(rect, palette.SoldierFill, palette.SoldierFill.Lightened(0.15f), palette.SoldierFill.Darkened(0.15f));
				DrawSoldierAnimated(block, rect, center, palette, idleAngle);
				break;

			case BlockType.Stunner:
				DrawStunnerBody(rect, palette);
				DrawStunnerAnimated(rect, center, palette, idleAngle);
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
	/// Builder: the whole block rotates 90° revolutions when idle, with ease in/out.
	/// </summary>
	private void DrawBuilderBody(Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle)
	{
		// Angle snaps to nearest 90° multiple — rotation is purely cosmetic
		float rev = idleAngle / (Mathf.Pi * 0.5f);
		float frac = rev - Mathf.Floor(rev);
		float ease = frac < 0.5f ? 4 * frac * frac * frac : 1 - Mathf.Pow(-2 * frac + 2, 3) / 2;
		float angle = (Mathf.Floor(rev) + ease) * Mathf.Pi * 0.5f;

		float half = rect.Size.X * 0.5f;
		var pts = new Vector2[4];
		for (int i = 0; i < 4; i++)
		{
			float a = angle + Mathf.Pi * 0.25f + i * Mathf.Pi * 0.5f;
			pts[i] = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * half;
		}

		// Gradient: light at corner 0 (top-left-ish), dark at corner 2
		DrawPolygon(pts, new Color[]
		{
			palette.BuilderGradientLight,
			palette.BuilderFill.Lightened(0.05f),
			palette.BuilderGradientDark,
			palette.BuilderFill.Lightened(0.05f)
		});
	}

	/// <summary>
	/// Soldier: 4 individual sword arms radiating from center, each drawn separately.
	/// 1 arm lost per HP lost (4 at full, 0 at 0). Pulsing glow. Idle spin only.
	/// </summary>
	private void DrawSoldierAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle)
	{
		var gold = palette.SoldierArmsColor;
		float armLen = rect.Size.X * 0.42f;

		// Idle spin: fast with ease in/out per quarter-turn
		float spinAngle = 0f;
		if (idleAngle > 0.001f)
		{
			float rev = idleAngle / (Mathf.Pi * 0.5f);
			float frac = rev - Mathf.Floor(rev);
			float ease = frac < 0.5f ? 4 * frac * frac * frac : 1 - Mathf.Pow(-2 * frac + 2, 3) / 2;
			spinAngle = (Mathf.Floor(rev) + ease) * Mathf.Pi * 0.5f;
		}

		int visibleArms = Mathf.Clamp(block.Hp, 0, 4);
		if (visibleArms == 0) return;

		// Pulsing glow
		float glowPulse = 0.2f + 0.15f * Mathf.Sin((float)Time.GetTicksMsec() * 0.005f);
		var glowColor = palette.SoldierArmsGlow with { A = glowPulse };

		// 4 individual arms at 90° intervals (NE, NW, SW, SE)
		// Remove arms one at a time: arm 0 (NE) lost first, then 2 (SW), then 1 (NW), then 3 (SE)
		int[] armOrder = { 0, 2, 1, 3 }; // order of removal (least important first)
		for (int i = 0; i < 4; i++)
		{
			// Check if this arm is still alive
			int removalIndex = Array.IndexOf(armOrder, i);
			if (removalIndex >= 4 - visibleArms) continue; // this arm has been removed

			float baseAngle = Mathf.Pi * 0.25f + i * Mathf.Pi * 0.5f;
			float a = baseAngle + spinAngle;
			var tip = center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * armLen;

			// Glow layer
			DrawLine(center, tip, glowColor, 8f);
			// Solid arm
			DrawLine(center, tip, gold, 3f);
		}

		// Bright center dot
		DrawCircle(center, 2.5f, palette.SoldierCenterDot);
	}

	/// <summary>
	/// Stunner: Outer diamond (player color) + inner diamond (white highlight).
	/// Spins constantly when idle. Subtle pulse glow.
	/// </summary>
	private void DrawStunnerAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle)
	{
		float size = rect.Size.X * 0.22f;
		float pulse = 0.85f + 0.15f * Mathf.Sin((float)Time.GetTicksMsec() * 0.0025f);

		// Constant spin from idle angle (no easing — smooth continuous rotation)
		float spinAngle = idleAngle;

		// Soft glow circle behind diamond
		DrawCircle(center, size * 1.6f, palette.StunnerGlow with { A = 0.12f * pulse });
		// Outer diamond (team color, lightened)
		DrawRotatedDiamond(center, size, spinAngle, palette.StunnerDiamondOuter with { A = pulse });
		// Inner diamond (white highlight, counter-rotates)
		DrawRotatedDiamond(center, size * 0.5f, -spinAngle * 0.5f, palette.StunnerDiamondInner with { A = 0.5f * pulse });
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
	/// Warden: Builder-shaded body with a white glowing shield icon.
	/// </summary>
	private void DrawWardenAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		// Body: same gradient as builder
		DrawGradientBody(rect, palette.BuilderFill, palette.BuilderGradientLight, palette.BuilderGradientDark);

		// Shield icon: simple shield shape (rounded top, pointed bottom)
		float shieldW = rect.Size.X * 0.32f;
		float shieldH = rect.Size.Y * 0.38f;

		// Glow pulse
		float pulse = 0.6f + 0.3f * Mathf.Sin(time * 2.5f);

		// Shield outline points: top-left, top-right, mid-right, bottom point, mid-left
		var shieldTop = center + new Vector2(0, -shieldH * 0.45f);
		var stl = shieldTop + new Vector2(-shieldW, 0);
		var str = shieldTop + new Vector2(shieldW, 0);
		var smr = center + new Vector2(shieldW * 0.85f, shieldH * 0.1f);
		var sml = center + new Vector2(-shieldW * 0.85f, shieldH * 0.1f);
		var sbot = center + new Vector2(0, shieldH * 0.55f);

		var shieldPts = new Vector2[] { stl, str, smr, sbot, sml };
		var shieldColor = new Color(1f, 1f, 1f, 0.7f * pulse);

		// Glow behind shield
		DrawColoredPolygon(shieldPts, new Color(1f, 1f, 1f, 0.15f * pulse));
		// Shield border
		for (int i = 0; i < shieldPts.Length; i++)
		{
			var a = shieldPts[i];
			var b = shieldPts[(i + 1) % shieldPts.Length];
			DrawLine(a, b, shieldColor, 1.5f);
		}
		// Bright center line (cross on shield)
		DrawLine(center + new Vector2(0, -shieldH * 0.25f), center + new Vector2(0, shieldH * 0.25f),
			shieldColor, 1.2f);
		DrawLine(center + new Vector2(-shieldW * 0.4f, -shieldH * 0.05f), center + new Vector2(shieldW * 0.4f, -shieldH * 0.05f),
			shieldColor, 1.2f);
	}

	/// <summary>
	/// Jumper: Lava ball that shrinks with HP loss. Not a square — drawn as circle.
	/// </summary>
	private void DrawJumperAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		float maxRadius = rect.Size.X * 0.44f;

		// HP scaling — smooth shrink per HP lost
		float hpScale = block.Hp switch
		{
			>= 3 => 1f,
			2 => 0.75f,
			1 => 0.45f,
			_ => 0.2f
		};
		float radius = maxRadius * hpScale;

		// Lava pulse — churning, faster than other blocks
		float pulse1 = Mathf.Sin(time * 3.5f);
		float pulse2 = Mathf.Sin(time * 5.1f + 1.3f);

		// Outer glow (heat haze)
		DrawCircle(center, radius * 1.25f, palette.JumperPulseGlow with { A = 0.06f + 0.04f * pulse1 });

		// Dark rim (cooled rock shell)
		DrawCircle(center, radius, palette.JumperDark);

		// Lava core — wobbles slightly
		float coreOffset = 0.8f * pulse2;
		var coreCenter = center + new Vector2(coreOffset, -coreOffset * 0.5f);
		DrawCircle(coreCenter, radius * 0.72f, palette.JumperCore);

		// Hot bright spot — shifts position for lava churn effect
		float brightOffset = radius * 0.12f * pulse1;
		var brightCenter = center + new Vector2(-brightOffset, brightOffset * 0.7f);
		DrawCircle(brightCenter, radius * 0.4f, palette.JumperBright with { A = 0.65f + 0.25f * pulse1 });

		// Tiny white-hot center
		DrawCircle(center, radius * 0.15f, Colors.White with { A = 0.5f + 0.3f * pulse2 });
	}

	/// <summary>
	/// Ghost trails: fading afterimages for jumper movement and jumps.
	/// </summary>
	private void DrawGhostTrails()
	{
		float now = (float)Time.GetTicksMsec() / 1000f;
		foreach (var ghost in _ghostTrails)
		{
			float maxLife = ghost.IsJump ? 0.3f : 0.5f;
			float age = now - ghost.StartTime;
			float t = Mathf.Clamp(age / maxLife, 0, 1);
			float alpha = (1f - t) * (ghost.IsJump ? 0.5f : 0.3f);
			float radius = CellSize * 0.3f * (ghost.IsJump ? (1f - t * 0.5f) : (1f - t));

			if (radius < 0.5f) continue;

			// Ghost circle
			DrawCircle(ghost.Pos, radius, ghost.Color with { A = alpha * 0.4f });
			DrawCircle(ghost.Pos, radius * 0.5f, ghost.Color with { A = alpha });

			if (ghost.IsJump)
			{
				// Motion blur: stretched ellipse effect via horizontal/vertical lines
				float blurLen = CellSize * 0.2f * (1f - t);
				DrawLine(ghost.Pos - new Vector2(blurLen, 0), ghost.Pos + new Vector2(blurLen, 0),
					ghost.Color with { A = alpha * 0.5f }, 3f);
				DrawLine(ghost.Pos - new Vector2(0, blurLen), ghost.Pos + new Vector2(0, blurLen),
					ghost.Color with { A = alpha * 0.5f }, 3f);
			}
		}
	}

	/// <summary>
	/// Draws terrain wall cells as inset blocks with appropriate styling.
	/// Called during cell background rendering for Terrain/BreakableWall/FragileWall cells.
	/// </summary>
	private void DrawTerrainWallBlock(Rect2 cellRect, GroundType ground)
	{
		var rect = new Rect2(
			cellRect.Position.X + BlockInset,
			cellRect.Position.Y + BlockInset,
			cellRect.Size.X - BlockInset * 2,
			cellRect.Size.Y - BlockInset * 2
		);

		// Color grades: SolidWall darkest, CrackedWall medium, WeakWall lightest
		var (fill, highlight, shadow, inner) = ground switch
		{
			GroundType.Terrain => (
				new Color(0.25f, 0.25f, 0.28f),    // dark gray
				new Color(0.35f, 0.35f, 0.38f),
				new Color(0.12f, 0.12f, 0.14f),
				new Color(0.20f, 0.20f, 0.22f)
			),
			GroundType.BreakableWall => (
				new Color(0.32f, 0.32f, 0.35f),    // medium gray
				new Color(0.42f, 0.42f, 0.45f),
				new Color(0.18f, 0.18f, 0.20f),
				new Color(0.27f, 0.27f, 0.29f)
			),
			_ => (                                   // FragileWall — lightest
				new Color(0.38f, 0.38f, 0.40f),
				new Color(0.48f, 0.48f, 0.50f),
				new Color(0.24f, 0.24f, 0.26f),
				new Color(0.33f, 0.33f, 0.35f)
			)
		};

		// Solid fill
		DrawRect(rect, fill);
		// Inner bevel
		DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), highlight, 1.5f);
		DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), highlight, 1.5f);
		DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), shadow, 1.5f);
		DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), shadow, 1.5f);
		// Inner rect for depth
		var innerRect = rect.Grow(-3f);
		DrawRect(innerRect, inner);

		// Obsidian stripes for SolidWall (Terrain)
		if (ground == GroundType.Terrain)
		{
			var stripeColor = new Color(0.15f, 0.12f, 0.18f, 0.35f);
			float spacing = 7f;
			// Diagonal stripes (static, 45°)
			for (float d = 0; d <= rect.Size.X + rect.Size.Y; d += spacing)
			{
				float x0 = Mathf.Max(0, d - rect.Size.Y);
				float y0 = d - x0;
				float x1 = Mathf.Min(rect.Size.X, d);
				float y1 = d - x1;
				if (x0 < rect.Size.X && x1 > 0)
				{
					DrawLine(rect.Position + new Vector2(x0, y0),
						rect.Position + new Vector2(x1, y1), stripeColor, 2f);
				}
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
