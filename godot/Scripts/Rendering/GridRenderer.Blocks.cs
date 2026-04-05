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

		// 1) Spinning outline tracer — clockwise when rooting, counter-clockwise when uprooting
		if (!rooted)
		{
			float time = (float)Time.GetTicksMsec() / 1000f;
			float spinSpeed = rooting ? 1.5f : -1.5f;
			float tracerLen = 0.25f; // fraction of perimeter covered by the tracer
			DrawOutlineTracer(rect, time * spinSpeed, tracerLen, new Color(0.78f, 0.78f, 0.78f, 0.4f + 0.3f * progress), 1.5f);
		}

		// 2) Digital root tendrils — grow from corners along grid lines
		DrawRootTendrils(rect, progress, palette.Base);

		// 3) Diagonal stripes — scroll TL→BR when rooting, BR→TL when uprooting, static when rooted
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
		DrawLine(from.Lerp(to, t0), from.Lerp(to, t1), color, width, true);
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
		float maxGridLen = CellSize * 0.6f; // max reach along grid line after the diagonal
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
		float maxWidth = 1.0f;
		float minWidth = 0.2f;

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

			DrawLine(p0, p1, color with { A = alpha * 0.3f }, width + 2f, true);
			DrawLine(p0, p1, color with { A = alpha }, width, true);
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

		DrawRoundLine(tl, tl + new Vector2(-len, 0), color, width);
		DrawRoundLine(tl, tl + new Vector2(0, -len), color, width);
		DrawRoundLine(tr, tr + new Vector2(len, 0), color, width);
		DrawRoundLine(tr, tr + new Vector2(0, -len), color, width);
		DrawRoundLine(bl, bl + new Vector2(-len, 0), color, width);
		DrawRoundLine(bl, bl + new Vector2(0, len), color, width);
		DrawRoundLine(br, br + new Vector2(len, 0), color, width);
		DrawRoundLine(br, br + new Vector2(0, len), color, width);
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

		if (frame != 0) DrawLine(midTop, midTop + new Vector2(0, crackLen), crackColor, 1f, true);
		if (frame != 1) DrawLine(midRight, midRight + new Vector2(-crackLen, 0), crackColor, 1f, true);
		if (frame != 2) DrawLine(midBottom, midBottom + new Vector2(0, -crackLen), crackColor, 1f, true);
		if (frame != 3) DrawLine(midLeft, midLeft + new Vector2(crackLen, 0), crackColor, 1f, true);
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
			DrawRoundLine(tl, tl + new Vector2(cornerLen, 0), red, 2.5f);
			DrawRoundLine(tl, tl + new Vector2(0, cornerLen), red, 2.5f);
			DrawRoundLine(tl, tl + new Vector2(cornerLen, 0), glowRed, 5f);
			DrawRoundLine(tl, tl + new Vector2(0, cornerLen), glowRed, 5f);
		}
		if (soldiers >= 2)
		{
			DrawRoundLine(tr, tr + new Vector2(-cornerLen, 0), red, 2.5f);
			DrawRoundLine(tr, tr + new Vector2(0, cornerLen), red, 2.5f);
			DrawRoundLine(tr, tr + new Vector2(-cornerLen, 0), glowRed, 5f);
			DrawRoundLine(tr, tr + new Vector2(0, cornerLen), glowRed, 5f);
		}
		if (soldiers >= 3)
		{
			DrawRoundLine(br, br + new Vector2(-cornerLen, 0), red, 2.5f);
			DrawRoundLine(br, br + new Vector2(0, -cornerLen), red, 2.5f);
			DrawRoundLine(br, br + new Vector2(-cornerLen, 0), glowRed, 5f);
			DrawRoundLine(br, br + new Vector2(0, -cornerLen), glowRed, 5f);
		}
	}

	private void DrawBlockTypeIndicator(Block block, Rect2 rect, Color color)
	{
		var center = rect.GetCenter();
		float time = (float)Time.GetTicksMsec() / 1000f;
		float idleAngle = _idleAngles.GetValueOrDefault(block.Id, 0f);
		var palette = _config.GetPalette(block.PlayerId);

		switch (block.Type)
		{
			case BlockType.Wall:
			{
				var sprite = SpriteFactory.GetSprite(BlockType.Wall, block.PlayerId);
				if (sprite != null)
					DrawTextureRect(sprite, rect, false);
				else
					DrawWallBlock(rect, palette);
				break;
			}

			case BlockType.Builder:
				DrawBuilderBody(rect, center, palette, idleAngle, block.PlayerId);
				break;

			case BlockType.Soldier:
			{
				var sprite = SpriteFactory.GetSprite(BlockType.Soldier, block.PlayerId);
				if (sprite != null)
					DrawTextureRect(sprite, rect, false);
				else
					DrawSmoothGradientBody(rect, palette.SoldierFill, palette.SoldierFill.Lightened(0.2f), palette.SoldierFill.Darkened(0.2f));
				DrawSoldierAnimated(block, rect, center, palette, idleAngle);
				break;
			}

			case BlockType.Stunner:
			{
				var sprite = SpriteFactory.GetSprite(BlockType.Stunner, block.PlayerId);
				if (sprite != null)
					DrawTextureRect(sprite, rect, false);
				else
					DrawSmoothGradientBody(rect, palette.StunnerFill, palette.StunnerBevelLight, palette.StunnerBevelShadow);
				DrawStunnerAnimated(rect, center, palette, idleAngle);
				break;
			}

			case BlockType.Warden:
			{
				var sprite = SpriteFactory.GetSprite(BlockType.Warden, block.PlayerId);
				if (sprite != null)
					DrawTextureRect(sprite, rect, false);
				else
					DrawSmoothGradientBody(rect, palette.BuilderFill, palette.BuilderGradientLight, palette.BuilderGradientDark);
				DrawWardenAnimated(rect, center, palette, time);
				break;
			}

			case BlockType.Jumper:
				DrawJumperAnimated(block, rect, center, palette, time);
				break;
		}
	}

	/// <summary>
	/// Multi-layer soft glow effect. Draws several increasingly wide, faint layers.
	/// </summary>
	private void DrawGlow(Vector2 from, Vector2 to, Color color, float baseWidth, int layers = 4)
	{
		QueueGlowLine(from, to, color with { A = 0.06f }, baseWidth * 3.6f, true);
		QueueGlowLine(from, to, color with { A = 0.15f }, baseWidth * 2.0f, true);
		QueueGlowLine(from, to, color with { A = 0.80f }, baseWidth * 0.72f, true);
		QueueGlowLine(from, to, Colors.White with { A = 0.60f }, baseWidth * 0.4f, true);
	}

	/// <summary>
	/// Circular multi-layer glow.
	/// </summary>
	private void DrawCircleGlow(Vector2 center, float radius, Color color, int layers = 4)
	{
		QueueGlowRadial(center, radius * 2.5f, color);
	}

	/// <summary>
	/// Smooth gradient body. Light from top-left, dark at bottom-right.
	/// Uses Godot's per-vertex color interpolation for smooth shading.
	/// </summary>
	private void DrawSmoothGradientBody(Rect2 rect, Color fill, Color light, Color dark)
	{
		var tl = rect.Position;
		var tr = new Vector2(rect.End.X, rect.Position.Y);
		var br = rect.End;
		var bl = new Vector2(rect.Position.X, rect.End.Y);
		DrawPolygon(
			new Vector2[] { tl, tr, br, bl },
			new Color[] { light, fill, dark, fill });
	}

	/// <summary>Wall: solid dark square with inner bevel for depth.</summary>
	private void DrawWallBlock(Rect2 rect, PlayerPalette palette)
	{
		// Solid dark fill
		DrawRect(rect, palette.WallFill);
		// Inner bevel: lighter top-left edge, darker bottom-right
		DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), palette.WallHighlight, 1.5f, true);
		DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), palette.WallHighlight, 1.5f, true);
		DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), palette.WallShadow, 1.5f, true);
		DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), palette.WallShadow, 1.5f, true);
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
	/// Builder: the whole block body rotates. At rest (angle=0 mod 90°) looks like normal rect.
	/// Uses ease-in/out for the rotation.
	/// </summary>
	private void DrawBuilderBody(Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle, int playerId)
	{
		var sprite = SpriteFactory.GetSprite(BlockType.Builder, playerId);
		if (sprite != null)
		{
			DrawSetTransform(center, 0);
			var halfSize = rect.Size * 0.5f;
			DrawTextureRect(sprite, new Rect2(-halfSize, rect.Size), false);
			DrawSetTransform(Vector2.Zero, 0);
		}
	}

	/// <summary>
	/// Soldier: crossed swords (X shape) from corners to center.
	/// Periodic 500ms ease-in-out spin every 4s, staggered per block.
	/// Arms lost in order: BR(4), BL(3), TL(2), TR(1 = last).
	/// Matches TS CanvasRenderer.ts soldier rendering.
	/// </summary>
	private void DrawSoldierAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle)
	{
		var gold = palette.SoldierArmsColor;
		float arm = rect.Size.X * 0.25f; // quarter of block = TS bs*0.25

		// Periodic spin: 500ms ease-in-out every 4s, staggered per block
		float now = (float)Time.GetTicksMsec();
		float cycle = 4000f;
		float spinDur = 500f;
		float phase = ((now + block.Id * 1337f) % cycle) / spinDur;
		float angle = 0f;
		if (phase <= 1f)
		{
			float t = phase;
			float ease = t < 0.5f ? 4 * t * t * t : 1 - Mathf.Pow(-2 * t + 2, 3) / 2;
			angle = ease * Mathf.Tau;
		}

		// Arms visible per HP (removal order: BR first, TR last)
		int hp = block.Hp;
		bool armTL = hp >= 2;
		bool armTR = hp >= 1; // last to go
		bool armBL = hp >= 3;
		bool armBR = hp >= 4; // first to go

		if (hp <= 0) return;

		float cos = Mathf.Cos(angle);
		float sin = Mathf.Sin(angle);

		// Arm endpoints (rotated corners)
		Vector2 RotateArm(float mx, float my)
		{
			return center + new Vector2(cos * mx - sin * my, sin * mx + cos * my);
		}

		var tl = RotateArm(-arm, -arm);
		var tr = RotateArm(arm, -arm);
		var bl = RotateArm(-arm, arm);
		var br = RotateArm(arm, arm);

		// Glow layer (wide, faint)
		float glowPulse = 0.25f + 0.2f * Mathf.Sin(now * 0.004f + block.Id * 2.1f);
		var glowColor = gold with { A = glowPulse * 1f };

		if (armTL) QueueGlowLine(tl, center, glowColor, 2.0f, false);
		if (armBR) QueueGlowLine(center, br, glowColor, 2.0f, false);
		if (armTR) QueueGlowLine(tr, center, glowColor, 2.0f, false);
		if (armBL) QueueGlowLine(center, bl, glowColor, 2.0f, false);

		// Core swords (thin, solid)
		if (armTL) DrawRoundLine(tl, center, gold, 1.5f);
		if (armBR) DrawRoundLine(center, br, gold, 1.5f);
		if (armTR) DrawRoundLine(tr, center, gold, 1.5f);
		if (armBL) DrawRoundLine(center, bl, gold, 1.5f);

		// Center dot
		DrawCircle(center, 1f, gold);
	}

	/// <summary>
	/// Stunner: Equilateral diamond (square rotated 45°) with periodic spin.
	/// 500ms ease-in-out spin every 4s, staggered per block. Radial glow behind.
	/// Outer diamond in player color + white stroke, inner diamond white highlight.
	/// Matches TS CanvasRenderer.ts stunner rendering.
	/// </summary>
	private void DrawStunnerAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle)
	{
		float d = rect.Size.X * 0.28f; // equilateral diamond size (TS: bs * 0.28)

		// Radial glow behind — purple-tinted, matches TS rgba(200,160,255)
		var glowColor = palette.StunnerGlow;
		float glowRadius = rect.Size.X * 0.5f;
		QueueGlowRadial(center, glowRadius, glowColor with { A = 0.3f });

		// Periodic spin: 500ms ease-in-out every 4s, staggered per block
		// (uses idleAngle which is driven by the same system — but stunner was set to constant spin)
		// Override: compute spin from time directly to match TS periodic behavior
		float now = (float)Time.GetTicksMsec();
		float cycle = 4000f;
		float spinDur = 500f;
		// We don't have block.Id here, but idleAngle is already staggered — use it for offset
		float phase = now % cycle / spinDur;
		float angle = idleAngle; // fallback to existing idle system which already handles spin

		// Outer diamond: equilateral (same width and height), filled with player color
		var pts = GetDiamondPoints(center, d, d, angle);
		DrawColoredPolygon(pts, palette.StunnerDiamondOuter);

		// White stroke on outer diamond (TS: rgba(255,255,255,0.5), lineWidth 1.5)
		var strokeColor = new Color(1f, 1f, 1f, 0.5f);
		DrawLine(pts[0], pts[1], strokeColor, 1.5f, true);
		DrawLine(pts[1], pts[2], strokeColor, 1.5f, true);
		DrawLine(pts[2], pts[3], strokeColor, 1.5f, true);
		DrawLine(pts[3], pts[0], strokeColor, 1.5f, true);

		// Inner diamond: 50% size, white 0.2α fill (TS: di = d * 0.5, rgba(255,255,255,0.2))
		float di = d * 0.5f;
		var innerPts = GetDiamondPoints(center, di, di, angle);
		DrawColoredPolygon(innerPts, new Color(1f, 1f, 1f, 0.2f));
	}

	/// <summary>
	/// Returns 4 diamond points: top, right, bottom, left — with separate width/height and rotation.
	/// </summary>
	private static Vector2[] GetDiamondPoints(Vector2 center, float halfWidth, float halfHeight, float angle)
	{
		float cos = Mathf.Cos(angle);
		float sin = Mathf.Sin(angle);
		// Unrotated: top=(0,-hh), right=(hw,0), bottom=(0,hh), left=(-hw,0)
		Vector2[] raw = { new(0, -halfHeight), new(halfWidth, 0), new(0, halfHeight), new(-halfWidth, 0) };
		var pts = new Vector2[4];
		for (int i = 0; i < 4; i++)
		{
			var r = raw[i];
			pts[i] = center + new Vector2(r.X * cos - r.Y * sin, r.X * sin + r.Y * cos);
		}
		return pts;
	}

	/// <summary>Equilateral rotated diamond (used by formations).</summary>
	private void DrawRotatedDiamond(Vector2 center, float size, float angle, Color color)
	{
		DrawColoredPolygon(GetDiamondPoints(center, size, size, angle), color);
	}

	/// <summary>
	/// Warden: Builder-shaded body with a layered white glowing shield icon.
	/// </summary>
	private void DrawWardenAnimated(Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		float shieldW = rect.Size.X * 0.34f;
		float shieldH = rect.Size.Y * 0.42f;
		float pulse = 0.6f + 0.35f * Mathf.Sin(time * 2.5f);

		// Shield shape: flat top, tapers to point at bottom, with curved sides
		var top = center + new Vector2(0, -shieldH * 0.48f);
		var stl = top + new Vector2(-shieldW, 0);
		var str = top + new Vector2(shieldW, 0);
		var midL = center + new Vector2(-shieldW * 0.9f, shieldH * 0.05f);
		var midR = center + new Vector2(shieldW * 0.9f, shieldH * 0.05f);
		var lowL = center + new Vector2(-shieldW * 0.55f, shieldH * 0.3f);
		var lowR = center + new Vector2(shieldW * 0.55f, shieldH * 0.3f);
		var bot = center + new Vector2(0, shieldH * 0.52f);

		var outer = new Vector2[] { stl, str, midR, lowR, bot, lowL, midL };
		var shieldWhite = new Color(1f, 1f, 1f);

		// Layer 1: soft glow aura behind
		DrawCircleGlow(center, shieldH * 0.4f, shieldWhite with { A = 0.15f * pulse });

		// Layer 2: filled shield body (semi-transparent white)
		DrawColoredPolygon(outer, shieldWhite with { A = 0.25f * pulse });

		// Layer 3: inner shield (smaller, brighter)
		float shrink = 0.7f;
		var innerTop = center + new Vector2(0, -shieldH * 0.48f * shrink);
		var inner = new Vector2[]
		{
			innerTop + new Vector2(-shieldW * shrink, 0),
			innerTop + new Vector2(shieldW * shrink, 0),
			center + new Vector2(shieldW * 0.9f * shrink, shieldH * 0.05f * shrink),
			center + new Vector2(shieldW * 0.55f * shrink, shieldH * 0.3f * shrink),
			center + new Vector2(0, shieldH * 0.52f * shrink),
			center + new Vector2(-shieldW * 0.55f * shrink, shieldH * 0.3f * shrink),
			center + new Vector2(-shieldW * 0.9f * shrink, shieldH * 0.05f * shrink)
		};
		DrawColoredPolygon(inner, shieldWhite with { A = 0.2f * pulse });

		// Layer 4: beveled outline
		var lightEdge = shieldWhite with { A = 0.7f * pulse };
		var darkEdge = shieldWhite with { A = 0.35f * pulse };
		// Top (light)
		DrawLine(outer[0], outer[1], lightEdge, 2f, true);
		// Upper sides (light)
		DrawLine(outer[6], outer[0], lightEdge, 1.5f, true);
		DrawLine(outer[1], outer[2], lightEdge, 1.5f, true);
		// Lower sides (darker)
		DrawLine(outer[2], outer[3], darkEdge, 1.5f, true);
		DrawLine(outer[5], outer[6], darkEdge, 1.5f, true);
		// Bottom converging (darkest)
		DrawLine(outer[3], outer[4], darkEdge, 1.5f, true);
		DrawLine(outer[4], outer[5], darkEdge, 1.5f, true);

		// Layer 5: cross emblem
		var crossColor = shieldWhite with { A = 0.55f * pulse };
		DrawRoundLine(center + new Vector2(0, -shieldH * 0.28f), center + new Vector2(0, shieldH * 0.28f), crossColor, 1.5f);
		DrawRoundLine(center + new Vector2(-shieldW * 0.45f, -shieldH * 0.04f), center + new Vector2(shieldW * 0.45f, -shieldH * 0.04f), crossColor, 1.5f);
	}

	/// <summary>
	/// Jumper: Lava sphere — smooth gradient from bright center to dark rim.
	/// No background square. Shrinks with HP loss.
	/// </summary>
	private void DrawJumperAnimated(Block block, Rect2 rect, Vector2 center, PlayerPalette palette, float time)
	{
		float maxRadius = rect.Size.X * 0.46f;

		float hpScale = block.Hp switch
		{
			>= 3 => 1f,
			2 => 0.75f,
			1 => 0.45f,
			_ => 0.2f
		};
		float radius = maxRadius * hpScale;

		float pulse1 = Mathf.Sin(time * 3.5f);
		float pulse2 = Mathf.Sin(time * 5.1f + 1.3f);

		// Sphere-like gradient: many concentric circles from dark rim to bright center
		// Light source offset (top-left) for 3D feel
		var lightOff = new Vector2(-radius * 0.15f, -radius * 0.15f);
		int rings = 8;
		for (int i = 0; i < rings; i++)
		{
			float t = (float)i / rings; // 0 = outermost, 1 = innermost
			float r = radius * (1f - t);
			// Interpolate from dark rim → core → bright highlight
			Color ringColor;
			if (t < 0.4f)
				ringColor = palette.JumperDark.Lerp(palette.JumperCore, t / 0.4f);
			else
				ringColor = palette.JumperCore.Lerp(palette.JumperBright, (t - 0.4f) / 0.6f);

			// Shift inner rings toward light source for sphere illusion
			var ringCenter = center + lightOff * t * 0.5f;
			DrawCircle(ringCenter, r, ringColor);
		}

		// Specular highlight — small bright spot offset toward light
		var specCenter = center + lightOff * 0.6f + new Vector2(pulse1 * 0.3f, pulse2 * 0.2f);
		DrawCircle(specCenter, radius * 0.18f, Colors.White with { A = 0.45f + 0.2f * pulse1 });

		// Lava churn: subtle shifting bright patches
		float churnOff = radius * 0.1f * pulse2;
		DrawCircle(center + new Vector2(churnOff, -churnOff * 0.5f), radius * 0.25f,
			palette.JumperBright with { A = 0.15f + 0.1f * pulse1 });

		// Outer heat glow
		QueueGlowRadial(center, radius * 1.8f, palette.JumperPulseGlow with { A = 0.08f + 0.04f * pulse1 });
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
			QueueGlowRadial(ghost.Pos, radius * 1.5f, ghost.Color with { A = alpha * 0.4f });
			DrawCircle(ghost.Pos, radius * 0.5f, ghost.Color with { A = alpha });

			if (ghost.IsJump)
			{
				// Motion blur: stretched ellipse effect via horizontal/vertical lines
				float blurLen = CellSize * 0.2f * (1f - t);
				DrawLine(ghost.Pos - new Vector2(blurLen, 0), ghost.Pos + new Vector2(blurLen, 0),
					ghost.Color with { A = alpha * 0.5f }, 3f, true);
				DrawLine(ghost.Pos - new Vector2(0, blurLen), ghost.Pos + new Vector2(0, blurLen),
					ghost.Color with { A = alpha * 0.5f }, 3f, true);
			}
		}
	}

	/// <summary>
	/// Draws terrain wall cells as inset blocks with appropriate styling.
	/// Called during cell background rendering for Terrain/BreakableWall/FragileWall cells.
	/// </summary>
	private void DrawTerrainWallBlock(Rect2 cellRect, TerrainType terrain)
	{
		var rect = new Rect2(
			cellRect.Position.X + BlockInset,
			cellRect.Position.Y + BlockInset,
			cellRect.Size.X - BlockInset * 2,
			cellRect.Size.Y - BlockInset * 2
		);

		// Color grades: SolidWall darkest, CrackedWall medium, WeakWall lightest
		var (fill, highlight, shadow, inner) = terrain switch
		{
			TerrainType.Terrain => (
				new Color(0.25f, 0.25f, 0.28f),    // dark gray
				new Color(0.35f, 0.35f, 0.38f),
				new Color(0.12f, 0.12f, 0.14f),
				new Color(0.20f, 0.20f, 0.22f)
			),
			TerrainType.BreakableWall => (
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
		DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), highlight, 1.5f, true);
		DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), highlight, 1.5f, true);
		DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), shadow, 1.5f, true);
		DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), shadow, 1.5f, true);
		// Inner rect for depth
		var innerRect = rect.Grow(-3f);
		DrawRect(innerRect, inner);

		// Obsidian stripes for SolidWall (Terrain)
		if (terrain == TerrainType.Terrain)
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
			DrawRoundLine(arm1, tip, color, 2f);
			DrawRoundLine(arm2, tip, color, 2f);
		}
	}

}
