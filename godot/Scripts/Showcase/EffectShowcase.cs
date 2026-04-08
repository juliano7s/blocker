using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.Showcase;

public partial class EffectShowcase : Node2D
{
	private const int GridWidth = 30;
	private const int GridHeight = 30;
	private const float CellSize = 28f;
	private const int CenterX = 15;
	private const int CenterY = 15;

	private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.1f);
	private static readonly Color GridLineColor = new(0.15f, 0.15f, 0.22f);
	private static readonly Color CenterCellColor = new(0.2f, 0.2f, 0.3f);

	// Glow layer for additive blending
	private GlowNode? _glowNode;

	// Active effects
	private readonly List<GridEffect> _effects = new();

	// Button panel
	private VBoxContainer? _buttonPanel;

	public override void _Ready()
	{
		// Dark background
		RenderingServer.SetDefaultClearColor(BackgroundColor);

		// Center the grid in the viewport
		var viewport = GetViewportRect().Size;
		var gridPixelW = GridWidth * CellSize;
		var gridPixelH = GridHeight * CellSize;
		Position = new Vector2(
			(viewport.X - gridPixelW) / 2f,
			(viewport.Y - gridPixelH) / 2f
		);

		// Glow layer (additive blend child)
		_glowNode = new GlowNode { Name = "GlowNode" };
		AddChild(_glowNode);

		// Button panel (in CanvasLayer so it doesn't move with the grid)
		var canvasLayer = new CanvasLayer { Name = "UI" };
		AddChild(canvasLayer);

		_buttonPanel = new VBoxContainer();
		_buttonPanel.SetAnchorsPreset(Control.LayoutPreset.TopLeft);
		_buttonPanel.Position = new Vector2(16, 16);
		_buttonPanel.AddThemeConstantOverride("separation", 6);
		canvasLayer.AddChild(_buttonPanel);

		AddEffectButton("1. Electric Lightning", () => SpawnElectricLightning());
		AddEffectButton("2. Wave Pulse", () => SpawnWavePulse());
		AddEffectButton("3. Ghost Flicker", () => SpawnGhostFlicker());
		AddEffectButton("4. Digital Cascade", () => SpawnDigitalCascade());
		AddEffectButton("5. Spiral Trace", () => SpawnSpiralTrace());
		AddEffectButton("6. Circuit Trace", () => SpawnCircuitTrace());
		AddEffectButton("7. Shockwave Ring", () => SpawnShockwaveRing());
		AddEffectButton("8. Jitter Burst", () => SpawnJitterBurst());
		AddEffectButton("9. Converging Drain", () => SpawnConvergingDrain());
		AddEffectButton("10. Arc Chain", () => SpawnArcChain());

		// Separator + line style variations
		_buttonPanel.AddChild(new HSeparator());
		AddEffectButton("11. Straight Tracer", () => SpawnStraightTracer());
		AddEffectButton("12. Sine Ripple", () => SpawnSineRipple());
		AddEffectButton("13. Dashed Tendrils", () => SpawnDashedTendrils());
		AddEffectButton("14. Pulse Beam", () => SpawnPulseBeam());
		AddEffectButton("15. Dotted Trail", () => SpawnDottedTrail());

		// Separator + All button
		_buttonPanel.AddChild(new HSeparator());
		AddEffectButton("ALL", () =>
		{
			SpawnElectricLightning();
			SpawnWavePulse();
			SpawnGhostFlicker();
			SpawnDigitalCascade();
			SpawnSpiralTrace();
			SpawnCircuitTrace();
			SpawnShockwaveRing();
			SpawnJitterBurst();
			SpawnConvergingDrain();
			SpawnArcChain();
			SpawnStraightTracer();
			SpawnSineRipple();
			SpawnDashedTendrils();
			SpawnPulseBeam();
			SpawnDottedTrail();
		});
	}

	private void AddEffectButton(string label, Action onPressed)
	{
		var btn = new Button { Text = label };
		btn.AddThemeFontSizeOverride("font_size", 13);
		btn.Pressed += onPressed;
		_buttonPanel!.AddChild(btn);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta * 1000f; // ms

		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var e = _effects[i];
			e.T += dt / e.Duration;
			e.Age += dt;

			// Update sparks
			for (int j = e.Sparks.Count - 1; j >= 0; j--)
			{
				var s = e.Sparks[j];
				s.X += s.Vx * (float)delta;
				s.Y += s.Vy * (float)delta;
				s.Life -= dt / 400f;
				if (s.Life <= 0) e.Sparks.RemoveAt(j);
				else e.Sparks[j] = s;
			}

			if (e.T >= 1f) _effects.RemoveAt(i);
		}

		QueueRedraw();
		_glowNode?.QueueRedraw();
	}

	public override void _Draw()
	{
		DrawGrid();
		DrawAllEffects();
	}

	private void DrawGrid()
	{
		// Cell backgrounds
		for (int y = 0; y < GridHeight; y++)
		{
			for (int x = 0; x < GridWidth; x++)
			{
				var rect = new Rect2(x * CellSize, y * CellSize, CellSize, CellSize);
				if (x == CenterX && y == CenterY)
					DrawRect(rect, CenterCellColor);
			}
		}

		// Grid lines
		for (int x = 0; x <= GridWidth; x++)
			DrawLine(new Vector2(x * CellSize, 0), new Vector2(x * CellSize, GridHeight * CellSize), GridLineColor, 1f);
		for (int y = 0; y <= GridHeight; y++)
			DrawLine(new Vector2(0, y * CellSize), new Vector2(GridWidth * CellSize, y * CellSize), GridLineColor, 1f);
	}

	// --- Effect data structures ---

	private struct LightSegment
	{
		public float X1, Y1, X2, Y2;
		public float Dist;
	}

	private struct Spark
	{
		public float X, Y, Vx, Vy, Life, Size;
	}

	private enum LineStyle
	{
		Normal,     // solid glow lines (default)
		Dashed,     // tapered dashed segments with gaps
		SineWave,   // animated sine displacement on segments
		PulseBeam,  // thick pulsing width
		Dotted,     // dots instead of lines
		FadingTrace // persistent trail that lingers
	}

	private class GridEffect
	{
		public List<LightSegment> Segments = new();
		public float MaxDist;
		public float T;          // 0..1 progress
		public float Duration;   // ms
		public float TrailDist;
		public Color Color;
		public List<Spark> Sparks = new();
		public float Age;
		public bool Reverse;     // for converging effects
		public bool FlickerMode; // for ghost flicker
		public LineStyle Style;  // how lines are rendered
	}

	private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

	// --- Placeholder spawn methods (implemented in later tasks) ---

	private static (List<LightSegment> Segments, float MaxDist) BuildLightning(
		List<(int Ix, int Iy, int Dx, int Dy)> seeds, int maxSegs, float contProb, float branchProb)
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		var visited = new HashSet<long>();
		var frontier = new List<(int Ix, int Iy, int Dx, int Dy, int Dist, float Cp)>();
		foreach (var s in seeds)
			frontier.Add((s.Ix, s.Iy, s.Dx, s.Dy, 0, contProb));
		float maxDist = 0;

		while (frontier.Count > 0 && segments.Count < maxSegs)
		{
			int idx = rng.Next(frontier.Count);
			var item = frontier[idx];
			frontier.RemoveAt(idx);

			int nx = item.Ix + item.Dx;
			int ny = item.Iy + item.Dy;
			if (nx < -2 || nx > GridWidth + 2 || ny < -2 || ny > GridHeight + 2) continue;

			int minX = Math.Min(item.Ix, nx), minY = Math.Min(item.Iy, ny);
			int maxX = Math.Max(item.Ix, nx), maxY = Math.Max(item.Iy, ny);
			long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (long)(maxY & 0xFFFF);
			if (!visited.Add(key)) continue;

			segments.Add(new LightSegment { X1 = item.Ix, Y1 = item.Iy, X2 = nx, Y2 = ny, Dist = item.Dist });
			if (item.Dist > maxDist) maxDist = item.Dist;

			if (rng.NextSingle() < item.Cp)
				frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1, item.Cp * 0.82f));

			if (rng.NextSingle() < branchProb)
			{
				var (pdx, pdy) = item.Dy == 0
					? (rng.NextSingle() < 0.5f ? (0, 1) : (0, -1))
					: (rng.NextSingle() < 0.5f ? (1, 0) : (-1, 0));
				frontier.Add((nx, ny, pdx, pdy, item.Dist + 1, item.Cp * 0.55f));
			}
		}

		return (segments, maxDist);
	}

	private static List<(int Ix, int Iy, int Dx, int Dy)> AllEdgeSeeds(int cx, int cy)
	{
		return new List<(int Ix, int Iy, int Dx, int Dy)>
		{
			(cx + 1, cy, 1, 0), (cx + 1, cy + 1, 1, 0),
			(cx, cy, -1, 0), (cx, cy + 1, -1, 0),
			(cx, cy + 1, 0, 1), (cx + 1, cy + 1, 0, 1),
			(cx, cy, 0, -1), (cx + 1, cy, 0, -1),
		};
	}

	private void SpawnElectricLightning()
	{
		var seeds = AllEdgeSeeds(CenterX, CenterY);
		var (segments, maxDist) = BuildLightning(seeds, 60, 0.90f, 0.55f);
		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1200, TrailDist = 3f,
			Color = new Color(0.3f, 0.8f, 1f), // cyan
		});
	}

	private void SpawnWavePulse()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;

		// Radiate grid lines outward from center, with sine-wave perpendicular displacement
		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			for (int dist = 0; dist < 12; dist++)
			{
				// Main line along grid
				float bx = CenterX + 0.5f + dx * dist;
				float by = CenterY + 0.5f + dy * dist;

				// Perpendicular displacement: sine wave
				float perpAmt = 0.3f * MathF.Sin(dist * 1.2f + rng.NextSingle() * 0.5f);
				float px = dy != 0 ? perpAmt : 0;
				float py = dx != 0 ? perpAmt : 0;

				float x1 = bx + px;
				float y1 = by + py;
				float x2 = bx + dx + px * 1.1f;
				float y2 = by + dy + py * 1.1f;

				segments.Add(new LightSegment { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Dist = dist });
				if (dist > maxDist) maxDist = dist;

				// Side branches with wobble
				if (rng.NextSingle() < 0.4f)
				{
					float bpx = dy == 0 ? 0 : (rng.NextSingle() < 0.5f ? 1 : -1);
					float bpy = dx == 0 ? 0 : (rng.NextSingle() < 0.5f ? 1 : -1);
					float wobble = 0.2f * (rng.NextSingle() - 0.5f);
					segments.Add(new LightSegment
					{
						X1 = x2, Y1 = y2,
						X2 = x2 + bpx + wobble, Y2 = y2 + bpy + wobble,
						Dist = dist + 0.5f,
					});
				}
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1500, TrailDist = 4f,
			Color = new Color(0.2f, 0.85f, 0.75f), // teal
		});
	}
	private void SpawnGhostFlicker()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;
		int radius = 6;

		// Scatter grid-line segments randomly in a radius around center
		for (int dy = -radius; dy <= radius; dy++)
		{
			for (int dx = -radius; dx <= radius; dx++)
			{
				float dist = MathF.Max(MathF.Abs(dx), MathF.Abs(dy));
				if (dist > radius) continue;

				int gx = CenterX + dx;
				int gy = CenterY + dy;

				// Randomly include horizontal and vertical segments
				if (rng.NextSingle() < 0.35f)
				{
					float wobble = (rng.NextSingle() - 0.5f) * 0.15f;
					segments.Add(new LightSegment
					{
						X1 = gx + wobble, Y1 = gy + wobble,
						X2 = gx + 1 + wobble, Y2 = gy + wobble,
						Dist = dist + rng.NextSingle() * 3f, // randomized dist for phase variation
					});
				}
				if (rng.NextSingle() < 0.35f)
				{
					float wobble = (rng.NextSingle() - 0.5f) * 0.15f;
					segments.Add(new LightSegment
					{
						X1 = gx + wobble, Y1 = gy + wobble,
						X2 = gx + wobble, Y2 = gy + 1 + wobble,
						Dist = dist + rng.NextSingle() * 3f,
					});
				}
			}
		}

		maxDist = radius + 3f;

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 2000, TrailDist = 2f,
			Color = new Color(0.75f, 0.6f, 1f), // pale violet
			FlickerMode = true,
		});
	}
	private void SpawnDigitalCascade()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;

		// Multiple vertical "rain" columns near center
		int columns = 8;
		for (int c = 0; c < columns; c++)
		{
			int colX = CenterX - 4 + rng.Next(9);
			int startY = CenterY - 2 + rng.Next(3);
			int length = 6 + rng.Next(8);
			float yOffset = (rng.NextSingle() - 0.5f) * 0.2f; // slight drift

			for (int i = 0; i < length; i++)
			{
				int gy = startY + i;
				if (gy < 0 || gy >= GridHeight) continue;

				float dist = i + c * 2f; // stagger columns
				segments.Add(new LightSegment
				{
					X1 = colX + yOffset, Y1 = gy,
					X2 = colX + yOffset, Y2 = gy + 1,
					Dist = dist,
				});
				if (dist > maxDist) maxDist = dist;

				// Random horizontal branches at intersections
				if (rng.NextSingle() < 0.3f)
				{
					int branchDir = rng.NextSingle() < 0.5f ? 1 : -1;
					int branchLen = 1 + rng.Next(3);
					for (int b = 0; b < branchLen; b++)
					{
						segments.Add(new LightSegment
						{
							X1 = colX + branchDir * b, Y1 = gy,
							X2 = colX + branchDir * (b + 1), Y2 = gy,
							Dist = dist + b * 0.5f,
						});
					}
				}
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1800, TrailDist = 5f,
			Color = new Color(0.2f, 1f, 0.4f), // green/lime
		});
	}

	private void SpawnSpiralTrace()
	{
		var segments = new List<LightSegment>();
		int x = CenterX + 1, y = CenterY + 1;
		int dx = 1, dy = 0;
		int stepsInLeg = 1, stepsTaken = 0, turnsAtLen = 0;
		int maxSegs = 48;

		for (int i = 0; i < maxSegs; i++)
		{
			int nx = x + dx, ny = y + dy;
			segments.Add(new LightSegment { X1 = x, Y1 = y, X2 = nx, Y2 = ny, Dist = i });
			x = nx; y = ny;
			stepsTaken++;
			if (stepsTaken >= stepsInLeg)
			{
				int tmp = dx; dx = -dy; dy = tmp; // turn clockwise
				stepsTaken = 0;
				turnsAtLen++;
				if (turnsAtLen >= 2) { turnsAtLen = 0; stepsInLeg++; }
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxSegs - 1,
			T = 0, Duration = 1800, TrailDist = 4f,
			Color = new Color(1f, 0.8f, 0.2f), // gold
		});
	}

	private void SpawnCircuitTrace()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;

		// BFS-like right-angle paths from center
		var frontier = new List<(float X, float Y, int Dx, int Dy, float Dist)>();
		frontier.Add((CenterX + 1, CenterY + 0.5f, 1, 0, 0));
		frontier.Add((CenterX, CenterY + 0.5f, -1, 0, 0));
		frontier.Add((CenterX + 0.5f, CenterY + 1, 0, 1, 0));
		frontier.Add((CenterX + 0.5f, CenterY, 0, -1, 0));

		int maxSegs = 50;
		while (frontier.Count > 0 && segments.Count < maxSegs)
		{
			int idx = rng.Next(frontier.Count);
			var item = frontier[idx];
			frontier.RemoveAt(idx);

			float nx = item.X + item.Dx;
			float ny = item.Y + item.Dy;

			segments.Add(new LightSegment
			{
				X1 = item.X, Y1 = item.Y, X2 = nx, Y2 = ny,
				Dist = item.Dist,
			});
			if (item.Dist > maxDist) maxDist = item.Dist;

			// Continue forward
			if (rng.NextSingle() < 0.7f)
				frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1));

			// Right-angle branch
			if (rng.NextSingle() < 0.35f)
			{
				var (pdx, pdy) = item.Dy == 0
					? (0, rng.NextSingle() < 0.5f ? 1 : -1)
					: (rng.NextSingle() < 0.5f ? 1 : -1, 0);
				frontier.Add((nx, ny, pdx, pdy, item.Dist + 1));
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1400, TrailDist = 2.5f,
			Color = new Color(1f, 0.6f, 0.2f), // warm orange
		});
	}

	private void SpawnShockwaveRing()
	{
		var segments = new List<LightSegment>();
		int maxRings = 10;

		for (int ring = 1; ring <= maxRings; ring++)
		{
			// Square perimeter at Chebyshev distance 'ring' from center
			int tlx = CenterX - ring, tly = CenterY - ring;
			int brx = CenterX + 1 + ring, bry = CenterY + 1 + ring;

			// Top edge
			for (int x = tlx; x < brx; x++)
				segments.Add(new LightSegment { X1 = x, Y1 = tly, X2 = x + 1, Y2 = tly, Dist = ring });
			// Right edge
			for (int y = tly; y < bry; y++)
				segments.Add(new LightSegment { X1 = brx, Y1 = y, X2 = brx, Y2 = y + 1, Dist = ring });
			// Bottom edge
			for (int x = brx; x > tlx; x--)
				segments.Add(new LightSegment { X1 = x, Y1 = bry, X2 = x - 1, Y2 = bry, Dist = ring });
			// Left edge
			for (int y = bry; y > tly; y--)
				segments.Add(new LightSegment { X1 = tlx, Y1 = y, X2 = tlx, Y2 = y - 1, Dist = ring });
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxRings,
			T = 0, Duration = 1000, TrailDist = 2f,
			Color = new Color(0.9f, 0.9f, 1f), // white/silver
		});
	}

	private void SpawnJitterBurst()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;
		var seeds = AllEdgeSeeds(CenterX, CenterY);
		int armCount = 10;
		int armLen = 4;

		for (int a = 0; a < armCount; a++)
		{
			var seed = seeds[a % seeds.Count];
			float x = seed.Ix, y = seed.Iy;
			int sdx = seed.Dx, sdy = seed.Dy;

			for (int i = 0; i < armLen; i++)
			{
				if (i > 0 && rng.NextSingle() < 0.6f)
				{
					if (sdx == 0) { sdx = rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
					else { sdy = rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
				}
				// Add jitter overshoot
				float jx = (rng.NextSingle() - 0.5f) * 0.3f;
				float jy = (rng.NextSingle() - 0.5f) * 0.3f;
				float nx = x + sdx + jx;
				float ny = y + sdy + jy;

				segments.Add(new LightSegment { X1 = x, Y1 = y, X2 = nx, Y2 = ny, Dist = i });
				if (i > maxDist) maxDist = i;
				x = nx; y = ny;
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 400, TrailDist = 1f,
			Color = new Color(1f, 0.25f, 0.2f), // red/crimson
		});
	}

	private void SpawnConvergingDrain()
	{
		var seeds = AllEdgeSeeds(CenterX, CenterY);
		var (segments, maxDist) = BuildLightning(seeds, 50, 0.85f, 0.50f);

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1000, TrailDist = 2f,
			Color = new Color(0.8f, 0.3f, 1f), // purple/magenta
			Reverse = true,
		});
	}

	private void SpawnArcChain()
	{
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;
		int arcCount = 8;
		float dist = 0;

		// Start at center
		float cx = CenterX + 0.5f, cy = CenterY + 0.5f;

		for (int a = 0; a < arcCount; a++)
		{
			// Pick random target intersection nearby
			float tx = CenterX + rng.Next(-5, 6) + 0.5f;
			float ty = CenterY + rng.Next(-5, 6) + 0.5f;

			// Approximate arc with 3-4 segments (slight curve via midpoint offset)
			int subSegs = 3 + rng.Next(2);
			float midOffX = (rng.NextSingle() - 0.5f) * 2f;
			float midOffY = (rng.NextSingle() - 0.5f) * 2f;

			for (int i = 0; i < subSegs; i++)
			{
				float t0 = (float)i / subSegs;
				float t1 = (float)(i + 1) / subSegs;

				// Quadratic bezier: P0=current, P1=midpoint+offset, P2=target
				float midX = (cx + tx) / 2f + midOffX;
				float midY = (cy + ty) / 2f + midOffY;

				float x0 = (1 - t0) * (1 - t0) * cx + 2 * (1 - t0) * t0 * midX + t0 * t0 * tx;
				float y0 = (1 - t0) * (1 - t0) * cy + 2 * (1 - t0) * t0 * midY + t0 * t0 * ty;
				float x1 = (1 - t1) * (1 - t1) * cx + 2 * (1 - t1) * t1 * midX + t1 * t1 * tx;
				float y1 = (1 - t1) * (1 - t1) * cy + 2 * (1 - t1) * t1 * midY + t1 * t1 * ty;

				segments.Add(new LightSegment { X1 = x0, Y1 = y0, X2 = x1, Y2 = y1, Dist = dist });
				dist += 1;
			}

			cx = tx; cy = ty;
			if (dist > maxDist) maxDist = dist;
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1200, TrailDist = 1.5f,
			Color = new Color(1f, 0.95f, 0.4f), // electric yellow
		});
	}

	// --- Line style variation effects ---

	private void SpawnStraightTracer()
	{
		// Fast straight lines shooting across 2+ grid lines in all 4 directions, leaving a fading trace
		var segments = new List<LightSegment>();
		int reach = 12; // how far the tracer goes

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
			// Two parallel lines per direction (both edges of center cell)
			for (int lane = 0; lane < 2; lane++)
			{
				float offX = dy != 0 ? (lane == 0 ? CenterX : CenterX + 1) : CenterX + 0.5f;
				float offY = dx != 0 ? (lane == 0 ? CenterY : CenterY + 1) : CenterY + 0.5f;

				for (int i = 0; i < reach; i++)
				{
					segments.Add(new LightSegment
					{
						X1 = offX + dx * i, Y1 = offY + dy * i,
						X2 = offX + dx * (i + 1), Y2 = offY + dy * (i + 1),
						Dist = i,
					});
				}
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = reach - 1,
			T = 0, Duration = 800, TrailDist = 8f, // long trail = fading trace
			Color = new Color(0.4f, 0.9f, 1f), // light blue
			Style = LineStyle.FadingTrace,
		});
	}

	private void SpawnSineRipple()
	{
		// Outward-radiating lines with animated sine wave displacement
		var segments = new List<LightSegment>();
		float maxDist = 0;

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			for (int lane = 0; lane < 3; lane++)
			{
				float offX, offY;
				if (dx != 0)
				{
					offX = CenterX + 0.5f;
					offY = CenterY + (lane - 1) * 0.5f + 0.5f;
				}
				else
				{
					offX = CenterX + (lane - 1) * 0.5f + 0.5f;
					offY = CenterY + 0.5f;
				}

				for (int i = 0; i < 10; i++)
				{
					segments.Add(new LightSegment
					{
						X1 = offX + dx * i, Y1 = offY + dy * i,
						X2 = offX + dx * (i + 1), Y2 = offY + dy * (i + 1),
						Dist = i,
					});
					if (i > maxDist) maxDist = i;
				}
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 2000, TrailDist = 5f,
			Color = new Color(0.3f, 0.6f, 1f), // blue
			Style = LineStyle.SineWave,
		});
	}

	private void SpawnDashedTendrils()
	{
		// Tapered dashed lines growing from center along grid lines, like rooting tendrils
		var segments = new List<LightSegment>();
		float maxDist = 0;
		var rng = new Random();

		// 8 tendrils from cell edges, each following grid lines with right-angle turns
		var seeds = AllEdgeSeeds(CenterX, CenterY);
		for (int a = 0; a < 8; a++)
		{
			var seed = seeds[a % seeds.Count];
			float x = seed.Ix, y = seed.Iy;
			int sdx = seed.Dx, sdy = seed.Dy;
			int len = 5 + rng.Next(6);

			for (int i = 0; i < len; i++)
			{
				// Occasionally turn at grid intersections
				if (i > 1 && rng.NextSingle() < 0.3f)
				{
					if (sdx == 0) { sdx = rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
					else { sdy = rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
				}

				float nx = x + sdx;
				float ny = y + sdy;
				segments.Add(new LightSegment { X1 = x, Y1 = y, X2 = nx, Y2 = ny, Dist = i });
				if (i > maxDist) maxDist = i;
				x = nx; y = ny;
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 1600, TrailDist = 3f,
			Color = new Color(0.5f, 0.9f, 0.3f), // organic green
			Style = LineStyle.Dashed,
		});
	}

	private void SpawnPulseBeam()
	{
		// Thick pulsing beam in all 4 cardinal directions from center
		var segments = new List<LightSegment>();
		int reach = 8;

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;
			float offX = dx != 0 ? CenterX + 0.5f : (float)CenterX + 0.5f;
			float offY = dy != 0 ? CenterY + 0.5f : (float)CenterY + 0.5f;

			for (int i = 0; i < reach; i++)
			{
				segments.Add(new LightSegment
				{
					X1 = offX + dx * i, Y1 = offY + dy * i,
					X2 = offX + dx * (i + 1), Y2 = offY + dy * (i + 1),
					Dist = i,
				});
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = reach - 1,
			T = 0, Duration = 1500, TrailDist = 3f,
			Color = new Color(1f, 0.4f, 0.6f), // hot pink
			Style = LineStyle.PulseBeam,
		});
	}

	private void SpawnDottedTrail()
	{
		// Dots scattered along a spiral path — each segment midpoint becomes a glowing dot
		var rng = new Random();
		var segments = new List<LightSegment>();
		float maxDist = 0;

		// Use the same spiral geometry but with dotted rendering
		int x = CenterX + 1, y = CenterY + 1;
		int dx = 1, dy = 0;
		int stepsInLeg = 1, stepsTaken = 0, turnsAtLen = 0;
		int maxSegs = 36;

		for (int i = 0; i < maxSegs; i++)
		{
			int nx = x + dx, ny = y + dy;
			// Add slight random offset to make dots feel organic
			float jx = (rng.NextSingle() - 0.5f) * 0.15f;
			float jy = (rng.NextSingle() - 0.5f) * 0.15f;
			segments.Add(new LightSegment
			{
				X1 = x + jx, Y1 = y + jy,
				X2 = nx + jx, Y2 = ny + jy,
				Dist = i
			});
			if (i > maxDist) maxDist = i;
			x = nx; y = ny;
			stepsTaken++;
			if (stepsTaken >= stepsInLeg)
			{
				int tmp = dx; dx = -dy; dy = tmp;
				stepsTaken = 0;
				turnsAtLen++;
				if (turnsAtLen >= 2) { turnsAtLen = 0; stepsInLeg++; }
			}
		}

		AddEffect(new GridEffect
		{
			Segments = segments, MaxDist = maxDist,
			T = 0, Duration = 2000, TrailDist = 3f,
			Color = new Color(0.9f, 0.7f, 1f), // soft lavender
			Style = LineStyle.Dotted,
		});
	}

	// --- Rendering ---
	private void DrawAllEffects()
	{
		// Non-glow passes: core lines + white tips + sparks
		foreach (var e in _effects)
			DrawEffectCore(e);
	}

	internal void DrawGlowPass()
	{
		// Glow passes: outer + inner bloom (additive blend)
		foreach (var e in _effects)
			DrawEffectGlow(e);
	}

	private void DrawEffectGlow(GridEffect e)
	{
		var visible = ComputeVisibleSegments(e);
		if (visible.Count == 0) return;

		float avgBa = 0f;
		foreach (var v in visible) avgBa += v.Ba;
		avgBa /= visible.Count;

		var color = e.Color;

		if (e.Style == LineStyle.Dotted)
		{
			// Dotted: glow circles instead of lines
			foreach (var v in visible)
			{
				var mid = new Vector2((v.Px1 + v.Px2) / 2f, (v.Py1 + v.Py2) / 2f);
				DrawCircle(mid, 6f, color with { A = avgBa * 0.08f });
				DrawCircle(mid, 3f, color with { A = avgBa * 0.18f });
			}
			return;
		}

		float outerWidth = e.Style == LineStyle.PulseBeam ? 14f : 9f;
		float innerWidth = e.Style == LineStyle.PulseBeam ? 8f : 5f;

		if (e.Style == LineStyle.PulseBeam)
		{
			// Pulsing width modulation
			float pulse = 0.6f + 0.4f * MathF.Sin(e.Age * 0.008f);
			outerWidth *= pulse;
			innerWidth *= pulse;
		}

		// Pass 1a: outer bloom — wide, faint
		foreach (var v in visible)
		{
			var (from, to) = GetStyledEndpoints(v, e);
			if (e.Style == LineStyle.Dashed)
			{
				DrawDashedLine(from, to, color with { A = avgBa * 0.06f }, outerWidth, e.Age);
			}
			else
			{
				DrawLine(from, to, color with { A = avgBa * 0.06f }, outerWidth);
			}
		}

		// Pass 1b: inner bloom
		foreach (var v in visible)
		{
			var (from, to) = GetStyledEndpoints(v, e);
			if (e.Style == LineStyle.Dashed)
			{
				DrawDashedLine(from, to, color with { A = avgBa * 0.15f }, innerWidth, e.Age);
			}
			else
			{
				DrawLine(from, to, color with { A = avgBa * 0.15f }, innerWidth);
			}
		}
	}

	private void DrawEffectCore(GridEffect e)
	{
		var visible = ComputeVisibleSegments(e);
		if (visible.Count == 0) return;

		var color = e.Color;
		float shimmer = 0.8f + 0.2f * MathF.Sin(e.Age * 0.006f);

		float coreWidth = 1.8f;
		if (e.Style == LineStyle.PulseBeam)
		{
			float pulse = 0.6f + 0.4f * MathF.Sin(e.Age * 0.008f);
			coreWidth = 3f * pulse;
		}
		else if (e.Style == LineStyle.FadingTrace)
		{
			coreWidth = 2.5f;
		}

		if (e.Style == LineStyle.Dotted)
		{
			// Dotted: draw circles at segment midpoints
			foreach (var v in visible)
			{
				var mid = new Vector2((v.Px1 + v.Px2) / 2f, (v.Py1 + v.Py2) / 2f);
				float dotSize = 1.5f + v.Brightness * 1.5f;
				DrawCircle(mid, dotSize, color with { A = v.Ba * 0.9f * shimmer });
				if (v.Brightness > 0.5f)
					DrawCircle(mid, dotSize * 0.5f, new Color(1f, 1f, 1f, v.Ba * 0.5f * shimmer));
			}
		}
		else
		{
			// Pass 2: colored core
			foreach (var v in visible)
			{
				var (from, to) = GetStyledEndpoints(v, e);
				float segAlpha = v.Ba * 0.8f * shimmer;

				// FadingTrace: segments behind the wave front stay visible longer
				if (e.Style == LineStyle.FadingTrace)
					segAlpha = MathF.Max(segAlpha, v.Brightness * 0.3f * (1f - e.T));

				if (e.Style == LineStyle.Dashed)
				{
					DrawDashedLine(from, to, color with { A = segAlpha }, coreWidth, e.Age);
				}
				else
				{
					DrawLine(from, to, color with { A = segAlpha }, coreWidth);
				}
			}

			// Pass 3: white-hot tips
			foreach (var v in visible)
			{
				if (v.Brightness > 0.55f)
				{
					var tipAlpha = ((v.Brightness - 0.55f) / 0.45f) * v.Ba * 0.6f;
					var (from, to) = GetStyledEndpoints(v, e);

					if (e.Style == LineStyle.PulseBeam)
						tipAlpha *= 1.5f; // brighter tips for pulse beam

					if (e.Style == LineStyle.Dashed)
					{
						DrawDashedLine(from, to, new Color(1f, 1f, 1f, tipAlpha * shimmer), 1f, e.Age);
					}
					else
					{
						DrawLine(from, to, new Color(1f, 1f, 1f, tipAlpha * shimmer), 1f);
					}
				}
			}
		}

		// Spawn + draw sparks
		var rng = new Random();
		if (e.Sparks.Count < 30)
		{
			foreach (var v in visible)
			{
				if (v.Brightness > 0.5f && e.Sparks.Count < 30 && rng.NextSingle() < 0.08f * v.Brightness)
				{
					float mx = (v.Px1 + v.Px2) / 2f;
					float my = (v.Py1 + v.Py2) / 2f;
					float sd = CellSize * 0.4f;
					e.Sparks.Add(new Spark
					{
						X = mx + (rng.NextSingle() - 0.5f) * sd,
						Y = my + (rng.NextSingle() - 0.5f) * sd,
						Vx = (rng.NextSingle() - 0.5f) * sd * 1.5f,
						Vy = (rng.NextSingle() - 0.5f) * sd * 1.5f,
						Life = 1f,
						Size = 1f + rng.NextSingle() * 1.5f,
					});
				}
			}
		}

		foreach (var s in e.Sparks)
		{
			if (s.Life < 0.05f) continue;
			float sa = s.Life * s.Life * shimmer;
			DrawRect(new Rect2(s.X - s.Size / 2f, s.Y - s.Size / 2f, s.Size, s.Size),
				color with { A = sa * 0.7f });
		}
	}

	/// <summary>
	/// Apply per-style endpoint transformations (sine wave displacement, etc.)
	/// </summary>
	private static (Vector2 From, Vector2 To) GetStyledEndpoints(VisibleSegment v, GridEffect e)
	{
		var from = new Vector2(v.Px1, v.Py1);
		var to = new Vector2(v.Px2, v.Py2);

		if (e.Style == LineStyle.SineWave)
		{
			// Animate sine displacement perpendicular to segment direction
			var dir = (to - from).Normalized();
			var perp = new Vector2(-dir.Y, dir.X);
			float wave1 = MathF.Sin(e.Age * 0.005f + v.Px1 * 0.1f + v.Py1 * 0.07f) * CellSize * 0.25f;
			float wave2 = MathF.Sin(e.Age * 0.005f + v.Px2 * 0.1f + v.Py2 * 0.07f) * CellSize * 0.25f;
			// Fade the displacement as effect progresses
			float fadeMult = 1f - e.T * 0.6f;
			from += perp * wave1 * fadeMult;
			to += perp * wave2 * fadeMult;
		}

		return (from, to);
	}

	/// <summary>
	/// Draw a dashed line with tapered segments — gap fraction animates with age for a crawling feel.
	/// </summary>
	private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float width, float age)
	{
		var dir = to - from;
		float len = dir.Length();
		if (len < 0.5f) return;

		int dashes = (int)MathF.Max(2, len / (CellSize * 0.3f));
		float dashLen = len / dashes;
		float gapFrac = 0.25f; // 25% gap
		// Animate the offset so dashes crawl along the line
		float crawl = (age * 0.002f) % 1f;
		var norm = dir / len;

		for (int i = 0; i < dashes; i++)
		{
			float t0 = ((float)i / dashes + crawl * (1f / dashes)) % 1f;
			float t1 = t0 + (1f - gapFrac) / dashes;
			if (t1 > 1f) t1 = 1f;

			var p0 = from + norm * (t0 * len);
			var p1 = from + norm * (t1 * len);

			// Taper: thinner toward the end of each dash
			float taperMid = (t0 + t1) / 2f;
			float taper = 1f - 0.3f * taperMid;
			DrawLine(p0, p1, color, width * taper);
		}
	}

	private struct VisibleSegment
	{
		public float Px1, Py1, Px2, Py2;
		public float Ba;
		public float Brightness;
	}

	private List<VisibleSegment> ComputeVisibleSegments(GridEffect e)
	{
		var result = new List<VisibleSegment>();
		float p = EaseOutCubic(MathF.Min(e.T, 1f));
		float md = MathF.Max(e.MaxDist, 1f);
		float shimmer = 0.8f + 0.2f * MathF.Sin(e.Age * 0.006f);

		foreach (var seg in e.Segments)
		{
			float brightness;
			if (e.FlickerMode)
			{
				// Ghost flicker: random per-segment visibility based on age + dist
				float phase = MathF.Sin(e.Age * 0.01f + seg.Dist * 2.7f + seg.X1 * 1.3f);
				brightness = phase > 0.2f ? MathF.Abs(phase) * (1f - e.T * 0.5f) : 0f;
			}
			else if (e.Reverse)
			{
				// Converging: wave travels inward (high dist first)
				float wavePos = p * (md + e.TrailDist);
				float invertedDist = md - seg.Dist;
				float diff = wavePos - invertedDist;
				brightness = diff < 0 ? 0 : MathF.Max(0, 1f - diff / e.TrailDist);
			}
			else
			{
				float wavePos = p * (md + e.TrailDist);
				float diff = wavePos - seg.Dist;
				brightness = diff < 0 ? 0 : MathF.Max(0, 1f - diff / e.TrailDist);
			}

			if (brightness <= 0.02f) continue;

			float alpha = brightness * (1f - e.T * 0.7f);
			float ba = alpha * shimmer;

			result.Add(new VisibleSegment
			{
				Px1 = seg.X1 * CellSize, Py1 = seg.Y1 * CellSize,
				Px2 = seg.X2 * CellSize, Py2 = seg.Y2 * CellSize,
				Ba = ba, Brightness = brightness,
			});
		}

		return result;
	}

	private void AddEffect(GridEffect effect)
	{
		if (_effects.Count >= 50) _effects.RemoveAt(0);
		_effects.Add(effect);
	}

	// --- Glow child node ---
	private partial class GlowNode : Node2D
	{
		public override void _Ready()
		{
			Material = new CanvasItemMaterial
			{
				BlendMode = CanvasItemMaterial.BlendModeEnum.Add
			};
		}

		public override void _Draw()
		{
			var parent = GetParent<EffectShowcase>();
			parent.DrawGlowPass();
		}
	}

}
