using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.Showcase;

/// <summary>
/// GPU-optimized EffectShowcase demonstrating three rendering techniques:
///
/// 1. Line2D + Shader — path effects (spiral, lightning, arcs).
///    Points set once at spawn. Shader animates wave. 1 draw call per path.
///
/// 2. ColorRect + Shader — grid-area effects (shockwave, beams, flicker).
///    Shader computes per-pixel. Zero per-segment CPU work.
///
/// 3. MultiMeshInstance2D + Shader — particle/dot effects.
///    Single draw call for hundreds of instances.
///
/// CPU cost per frame: O(num_effects) — just 2 uniform float updates per effect.
/// Compare to original: O(num_segments × num_effects) with per-segment DrawLine calls.
/// </summary>
public partial class EffectShowcase2 : Node2D
{
	private const int GridW = 30;
	private const int GridH = 30;
	private const float CellSize = 28f;
	private const int CX = 15;
	private const int CY = 15;

	private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.1f);
	private static readonly Color GridLineColor = new(0.15f, 0.15f, 0.22f);
	private static readonly Color CenterCellColor = new(0.2f, 0.2f, 0.3f);

	// Shaders (loaded once)
	private Shader _lineWaveShader = null!;
	private Shader _gridRingsShader = null!;
	private Shader _gridScatterShader = null!;
	private Shader _pointWaveShader = null!;

	// Shared gradient template (0→1 in R channel) for Line2D distance encoding
	private Gradient _distGradient = null!;

	// Active effects
	private readonly List<GpuEffect> _effects = new();
	private readonly Random _rng = new();

	// UI
	private VBoxContainer _buttonPanel = null!;

	// ─── Lifecycle ───

	public override void _Ready()
	{
		RenderingServer.SetDefaultClearColor(BackgroundColor);

		// Center grid in viewport
		var vp = GetViewportRect().Size;
		Position = new Vector2(
			(vp.X - GridW * CellSize) / 2f,
			(vp.Y - GridH * CellSize) / 2f
		);

		// Load shaders
		_lineWaveShader = GD.Load<Shader>("res://Assets/Shaders/line_wave.gdshader");
		_gridRingsShader = GD.Load<Shader>("res://Assets/Shaders/grid_rings.gdshader");
		_gridScatterShader = GD.Load<Shader>("res://Assets/Shaders/grid_scatter.gdshader");
		_pointWaveShader = GD.Load<Shader>("res://Assets/Shaders/point_wave.gdshader");

		// Shared distance gradient: R channel 0→1 along line length
		_distGradient = new Gradient();
		_distGradient.SetColor(0, new Color(0f, 0f, 0f, 1f));
		_distGradient.SetOffset(0, 0f);
		_distGradient.SetColor(1, new Color(1f, 1f, 1f, 1f));
		_distGradient.SetOffset(1, 1f);

		BuildUI();
		QueueRedraw(); // draw grid once
	}

	public override void _Process(double delta)
	{
		float dtMs = (float)delta * 1000f;
		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var e = _effects[i];
			e.Age += dtMs;
			e.Update();

			if (e.Progress >= 1f)
			{
				if (e.Looping)
					e.Age -= e.Duration;
				else
				{
					e.Destroy();
					_effects.RemoveAt(i);
				}
			}
		}
	}

	public override void _Draw()
	{
		DrawGrid();
	}

	// ─── Grid Background ───

	private void DrawGrid()
	{
		for (int y = 0; y < GridH; y++)
		for (int x = 0; x < GridW; x++)
		{
			if (x == CX && y == CY)
				DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), CenterCellColor);
		}

		for (int x = 0; x <= GridW; x++)
			DrawLine(new Vector2(x * CellSize, 0), new Vector2(x * CellSize, GridH * CellSize), GridLineColor, 1f);
		for (int y = 0; y <= GridH; y++)
			DrawLine(new Vector2(0, y * CellSize), new Vector2(GridW * CellSize, y * CellSize), GridLineColor, 1f);
	}

	// ─── UI ───

	private void BuildUI()
	{
		var canvas = new CanvasLayer { Name = "UI" };
		AddChild(canvas);

		var scroll = new ScrollContainer();
		scroll.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
		scroll.OffsetLeft = 12;
		scroll.OffsetTop = 12;
		scroll.OffsetRight = 220;
		scroll.OffsetBottom = -12;
		scroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		canvas.AddChild(scroll);

		_buttonPanel = new VBoxContainer();
		_buttonPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_buttonPanel.AddThemeConstantOverride("separation", 4);
		scroll.AddChild(_buttonPanel);

		// Line2D effects
		AddBtn("LINE2D + Shader:", null);
		AddBtn("1. Spiral Trace", SpawnSpiralTrace);
		AddBtn("2. Electric Lightning", SpawnElectricLightning);
		AddBtn("3. Straight Tracer", SpawnStraightTracer);
		AddBtn("4. Dashed Tendrils", SpawnDashedTendrils);
		AddBtn("5. Arc Chain", SpawnArcChain);
		AddBtn("6. Circuit Trace", SpawnCircuitTrace);
		AddBtn("7. Converging Drain", SpawnConvergingDrain);
		AddBtn("8. Wave Pulse", SpawnWavePulse);
		AddBtn("9. Sine Ripple", SpawnSineRipple);

		_buttonPanel.AddChild(new HSeparator());

		// Grid shader effects
		AddBtn("COLORRECT + Shader:", null);
		AddBtn("10. Shockwave Ring", SpawnShockwaveRing);
		AddBtn("11. Pulse Beam", SpawnPulseBeam);
		AddBtn("12. Ghost Flicker", SpawnGhostFlicker);
		AddBtn("13. Digital Cascade", SpawnDigitalCascade);

		_buttonPanel.AddChild(new HSeparator());

		// MultiMesh effects
		AddBtn("MULTIMESH + Shader:", null);
		AddBtn("14. Dotted Trail", SpawnDottedTrail);
		AddBtn("15. Jitter Burst", SpawnJitterBurst);

		_buttonPanel.AddChild(new HSeparator());

		// Looping ZoC
		AddBtn("LOOPING ZOC:", null);
		AddBtn("16. ZoC Grid Wave", SpawnZocGridWave);
		AddBtn("17. ZoC Sine Rings", SpawnZocSineRings);
		AddBtn("18. ZoC Dashed Pulse", SpawnZocDashedPulse);
		AddBtn("19. ZoC Sequential Rings", SpawnZocSequentialRings);
		AddBtn("Stop Looping", () => _effects.RemoveAll(e => { if (e.Looping) e.Destroy(); return e.Looping; }));

		_buttonPanel.AddChild(new HSeparator());
		AddBtn("ALL (one-shot)", SpawnAll);

		_buttonPanel.AddChild(new HSeparator());

		// Game effects from TS GridEffects.ts
		AddBtn("GAME EFFECTS (TS GridEffects):", null);
		AddBtn("20. Move Trail", SpawnMoveEffect);
		AddBtn("21. Root", SpawnRootEffect);
		AddBtn("22. Uproot", SpawnUprootEffect);
		AddBtn("23. Explode", SpawnExplodeEffect);
		AddBtn("24. Wall Convert", SpawnConvertEffect);
		AddBtn("25. Uproot Done", SpawnUprootDoneEffect);
		AddBtn("26. Select", SpawnSelectEffect);
		AddBtn("27. Builder Spawn", SpawnBuilderSpawnEffect);
		AddBtn("28. Soldier Spawn", SpawnSoldierSpawnEffect);
		AddBtn("29. Stunner Spawn", SpawnStunnerSpawnEffect);
		AddBtn("30. Spawn Cell", SpawnCellEffect);

		_buttonPanel.AddChild(new HSeparator());
		AddBtn("ALL+GAME", SpawnAll);
	}

	private void AddBtn(string label, Action? onPressed)
	{
		var btn = new Button { Text = label };
		btn.AddThemeFontSizeOverride("font_size", 12);
		if (onPressed != null) btn.Pressed += onPressed;
		else btn.Disabled = true;
		_buttonPanel.AddChild(btn);
	}

	// ─── Effect Infrastructure ───

	private abstract class GpuEffect
	{
		public float Duration;  // ms
		public float Age;       // ms
		public bool Looping;
		public float Progress => Math.Clamp(Age / Duration, 0f, 1f);
		public abstract void Update();
		public abstract void Destroy();
	}

	/// <summary>
	/// Line2D-based effect. One or more Line2D paths sharing a single ShaderMaterial.
	/// CPU sets points once at spawn. Shader animates the wave via gradient R channel.
	/// </summary>
	private class LineEffect : GpuEffect
	{
		public ShaderMaterial CoreMat = null!;
		public ShaderMaterial GlowMat = null!;
		public List<Line2D> CoreLines = new();
		public List<Line2D> GlowLines = new();

		public override void Update()
		{
			float p = Progress;
			float age = Age;
			CoreMat.SetShaderParameter("progress", p);
			CoreMat.SetShaderParameter("age_ms", age);
			GlowMat.SetShaderParameter("progress", p);
			GlowMat.SetShaderParameter("age_ms", age);
		}

		public override void Destroy()
		{
			foreach (var l in CoreLines) l.QueueFree();
			foreach (var l in GlowLines) l.QueueFree();
		}
	}

	/// <summary>
	/// ColorRect-based effect. Shader computes per-pixel lighting.
	/// CPU only updates progress + age uniforms.
	/// </summary>
	private class GridEffect : GpuEffect
	{
		public ShaderMaterial Mat = null!;
		public ColorRect Rect = null!;

		public override void Update()
		{
			Mat.SetShaderParameter("progress", Progress);
			Mat.SetShaderParameter("age_ms", Age);
		}

		public override void Destroy() => Rect.QueueFree();
	}

	/// <summary>
	/// MultiMesh-based effect. Instance colors encode distance + phase.
	/// CPU sets instance data once at spawn. Shader animates the wave.
	/// </summary>
	private class DotEffect : GpuEffect
	{
		public ShaderMaterial Mat = null!;
		public MultiMeshInstance2D MmNode = null!;

		public override void Update()
		{
			Mat.SetShaderParameter("progress", Progress);
			Mat.SetShaderParameter("age_ms", Age);
		}

		public override void Destroy() => MmNode.QueueFree();
	}

	// ─── Shader Material Factories ───

	private ShaderMaterial MakeLineMat(Color color, float trail, float fadeMult,
		bool reverse = false, bool dashed = false, bool flicker = false,
		bool loopMode = false, float baseAlpha = 0f,
		bool contract = false, float fadeSpeed = 0.7f)
	{
		var mat = new ShaderMaterial { Shader = _lineWaveShader };
		mat.SetShaderParameter("line_color", color);
		mat.SetShaderParameter("trail", trail);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("reverse", reverse);
		mat.SetShaderParameter("dashed", dashed);
		mat.SetShaderParameter("flicker", flicker);
		mat.SetShaderParameter("loop_mode", loopMode);
		mat.SetShaderParameter("base_alpha", baseAlpha);
		mat.SetShaderParameter("contract", contract);
		mat.SetShaderParameter("fade_speed", fadeSpeed);
		return mat;
	}

	private ShaderMaterial MakeRingsMat(int mode, Color color, float maxRadius, float trail,
		float fadeMult = 1f, bool loopMode = false, float baseAlpha = 0f)
	{
		var mat = new ShaderMaterial { Shader = _gridRingsShader };
		mat.SetShaderParameter("center", new Vector2(CX + 0.5f, CY + 0.5f));
		mat.SetShaderParameter("grid_size", new Vector2(GridW, GridH));
		mat.SetShaderParameter("cell_size", CellSize);
		mat.SetShaderParameter("max_radius", maxRadius);
		mat.SetShaderParameter("trail", trail);
		mat.SetShaderParameter("ring_color", color);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("mode", mode);
		mat.SetShaderParameter("loop_mode", loopMode);
		mat.SetShaderParameter("base_alpha", baseAlpha);
		return mat;
	}

	private ShaderMaterial MakeScatterMat(int mode, Color color, float radius, float fadeMult = 1f)
	{
		var mat = new ShaderMaterial { Shader = _gridScatterShader };
		mat.SetShaderParameter("center", new Vector2(CX + 0.5f, CY + 0.5f));
		mat.SetShaderParameter("grid_size", new Vector2(GridW, GridH));
		mat.SetShaderParameter("radius", radius);
		mat.SetShaderParameter("effect_color", color);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("mode", mode);
		return mat;
	}

	private ShaderMaterial MakePointMat(Color color, float trail, float fadeMult,
		bool loopMode = false, float baseAlpha = 0f)
	{
		var mat = new ShaderMaterial { Shader = _pointWaveShader };
		mat.SetShaderParameter("dot_color", color);
		mat.SetShaderParameter("trail", trail);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("loop_mode", loopMode);
		mat.SetShaderParameter("base_alpha", baseAlpha);
		return mat;
	}

	// ─── Line2D Node Factories ───

	/// <summary>
	/// Create a Line2D for a path effect, with gradient encoding distance.
	/// distStart/distEnd are normalized (0..1) distances for the gradient range.
	/// </summary>
	private Line2D MakePathLine(Vector2[] points, ShaderMaterial mat, float width,
		float distStart = 0f, float distEnd = 1f)
	{
		var line = new Line2D();
		line.Points = points;
		line.Width = width;
		line.JointMode = Line2D.LineJointMode.Round;
		line.BeginCapMode = Line2D.LineCapMode.Round;
		line.EndCapMode = Line2D.LineCapMode.Round;
		line.Material = mat;

		// Gradient encodes normalized distance along path
		var grad = new Gradient();
		grad.SetColor(0, new Color(distStart, distStart, distStart, 1f));
		grad.SetOffset(0, 0f);
		grad.SetColor(1, new Color(distEnd, distEnd, distEnd, 1f));
		grad.SetOffset(1, 1f);
		line.Gradient = grad;

		AddChild(line);
		return line;
	}

	private void AddLineEffect(List<(Vector2[] Points, float DistStart, float DistEnd)> paths,
		Color color, float duration, float trail,
		bool reverse = false, bool dashed = false, bool flicker = false,
		bool loopMode = false, float baseAlpha = 0f,
		bool contract = false, float fadeSpeed = 0.7f)
	{
		var coreMat = MakeLineMat(color, trail, 0.85f, reverse, dashed, flicker, loopMode, baseAlpha, contract, fadeSpeed);
		var glowMat = MakeLineMat(color, trail, 0.12f, reverse, dashed, flicker, loopMode, baseAlpha, contract, fadeSpeed);

		var effect = new LineEffect
		{
			Duration = duration,
			CoreMat = coreMat,
			GlowMat = glowMat,
		};

		foreach (var (pts, ds, de) in paths)
		{
			effect.CoreLines.Add(MakePathLine(pts, coreMat, 2.5f, ds, de));
			effect.GlowLines.Add(MakePathLine(pts, glowMat, 10f, ds, de));
		}

		_effects.Add(effect);
	}

	private void AddGridEffect(ShaderMaterial mat, float duration, bool looping = false)
	{
		var rect = new ColorRect();
		rect.Size = new Vector2(GridW * CellSize, GridH * CellSize);
		rect.Material = mat;
		AddChild(rect);

		_effects.Add(new GridEffect
		{
			Duration = duration,
			Mat = mat,
			Rect = rect,
			Looping = looping,
		});
	}

	private void AddDotEffect(MultiMesh mm, ShaderMaterial mat, float duration,
		bool looping = false)
	{
		var node = new MultiMeshInstance2D { Multimesh = mm };
		node.Material = mat;
		AddChild(node);

		_effects.Add(new DotEffect
		{
			Duration = duration,
			Mat = mat,
			MmNode = node,
			Looping = looping,
		});
	}

	// ─── Helper: Lightning builder ───

	private List<(Vector2[] Points, float DistStart, float DistEnd)> BuildLightningPaths(
		List<(int Ix, int Iy, int Dx, int Dy)> seeds, int maxSegs, float contProb, float branchProb)
	{
		// Build all segments first
		var allSegs = new List<(float X1, float Y1, float X2, float Y2, float Dist)>();
		var visited = new HashSet<long>();
		var frontier = new List<(int Ix, int Iy, int Dx, int Dy, int Dist, float Cp)>();
		foreach (var s in seeds)
			frontier.Add((s.Ix, s.Iy, s.Dx, s.Dy, 0, contProb));
		float maxDist = 0;

		while (frontier.Count > 0 && allSegs.Count < maxSegs)
		{
			int idx = _rng.Next(frontier.Count);
			var item = frontier[idx];
			frontier.RemoveAt(idx);

			int nx = item.Ix + item.Dx, ny = item.Iy + item.Dy;
			if (nx < -2 || nx > GridW + 2 || ny < -2 || ny > GridH + 2) continue;

			int minX = Math.Min(item.Ix, nx), minY = Math.Min(item.Iy, ny);
			int maxX = Math.Max(item.Ix, nx), maxY = Math.Max(item.Iy, ny);
			long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (uint)(maxY & 0xFFFF);
			if (!visited.Add(key)) continue;

			allSegs.Add((item.Ix, item.Iy, nx, ny, item.Dist));
			if (item.Dist > maxDist) maxDist = item.Dist;

			if (_rng.NextSingle() < item.Cp)
				frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1, item.Cp * 0.82f));
			if (_rng.NextSingle() < branchProb)
			{
				var (pdx, pdy) = item.Dy == 0
					? (_rng.NextSingle() < 0.5f ? (0, 1) : (0, -1))
					: (_rng.NextSingle() < 0.5f ? (1, 0) : (-1, 0));
				frontier.Add((nx, ny, pdx, pdy, item.Dist + 1, item.Cp * 0.55f));
			}
		}

		// Group connected segments into paths by tracing from seeds
		// For simplicity: group by similar distance, creating contiguous chains
		var paths = new List<(Vector2[] Points, float DistStart, float DistEnd)>();
		var used = new bool[allSegs.Count];

		// Simple greedy path builder: start from lowest-dist unused segment, chain forward
		for (int si = 0; si < allSegs.Count; si++)
		{
			if (used[si]) continue;
			var chain = new List<Vector2>();
			float firstDist = allSegs[si].Dist;
			float lastDist = allSegs[si].Dist;
			int curEndX = allSegs[si].X2 >= 0 ? (int)allSegs[si].X2 : -1;
			int curEndY = allSegs[si].Y2 >= 0 ? (int)allSegs[si].Y2 : -1;

			chain.Add(new Vector2(allSegs[si].X1 * CellSize, allSegs[si].Y1 * CellSize));
			chain.Add(new Vector2(allSegs[si].X2 * CellSize, allSegs[si].Y2 * CellSize));
			used[si] = true;

			// Try to extend the chain
			bool extended;
			do
			{
				extended = false;
				for (int j = 0; j < allSegs.Count; j++)
				{
					if (used[j]) continue;
					var seg = allSegs[j];
					// Connect to end of chain
					if ((int)seg.X1 == curEndX && (int)seg.Y1 == curEndY)
					{
						chain.Add(new Vector2(seg.X2 * CellSize, seg.Y2 * CellSize));
						curEndX = (int)seg.X2;
						curEndY = (int)seg.Y2;
						lastDist = seg.Dist;
						used[j] = true;
						extended = true;
						break;
					}
				}
			} while (extended);

			float normStart = maxDist > 0 ? firstDist / maxDist : 0f;
			float normEnd = maxDist > 0 ? lastDist / maxDist : 1f;
			paths.Add((chain.ToArray(), normStart, normEnd));
		}

		return paths;
	}

	private static List<(int Ix, int Iy, int Dx, int Dy)> EdgeSeeds(int dx, int dy)
	{
		return (dx, dy) switch
		{
			(1, 0) => new List<(int, int, int, int)> { (CX + 1, CY, 1, 0), (CX + 1, CY + 1, 1, 0) },
			(-1, 0) => new List<(int, int, int, int)> { (CX, CY, -1, 0), (CX, CY + 1, -1, 0) },
			(0, 1) => new List<(int, int, int, int)> { (CX, CY + 1, 0, 1), (CX + 1, CY + 1, 0, 1) },
			_ => new List<(int, int, int, int)> { (CX, CY, 0, -1), (CX + 1, CY, 0, -1) },
		};
	}

	private static List<(int Ix, int Iy, int Dx, int Dy)> AllEdgeSeeds()
	{
		var result = new List<(int, int, int, int)>(8);
		result.AddRange(EdgeSeeds(1, 0));
		result.AddRange(EdgeSeeds(-1, 0));
		result.AddRange(EdgeSeeds(0, 1));
		result.AddRange(EdgeSeeds(0, -1));
		return result;
	}

	// ════════════════════════════════════════════════════════════════
	// EFFECT SPAWN METHODS
	// ════════════════════════════════════════════════════════════════

	// ─── Line2D Effects ───

	private void SpawnSpiralTrace()
	{
		var points = new List<Vector2>();
		int x = CX + 1, y = CY + 1;
		int dx = 1, dy = 0;
		int stepsInLeg = 1, stepsTaken = 0, turnsAtLen = 0;
		int maxSegs = 48;

		for (int i = 0; i < maxSegs; i++)
		{
			points.Add(new Vector2(x * CellSize, y * CellSize));
			x += dx; y += dy;
			stepsTaken++;
			if (stepsTaken >= stepsInLeg)
			{
				int tmp = dx; dx = -dy; dy = tmp;
				stepsTaken = 0;
				turnsAtLen++;
				if (turnsAtLen >= 2) { turnsAtLen = 0; stepsInLeg++; }
			}
		}
		points.Add(new Vector2(x * CellSize, y * CellSize));

		AddLineEffect(
			new List<(Vector2[], float, float)> { (points.ToArray(), 0f, 1f) },
			new Color(1f, 0.8f, 0.2f), 1800, 0.15f
		);
	}

	private void SpawnElectricLightning()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 60, 0.90f, 0.55f);
		AddLineEffect(paths, new Color(0.3f, 0.8f, 1f), 1200, 0.15f);
	}

	private void SpawnStraightTracer()
	{
		var pathsList = new List<(Vector2[], float, float)>();
		int reach = 12;

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			for (int lane = 0; lane < 2; lane++)
			{
				float offX = dy != 0 ? (lane == 0 ? CX : CX + 1) : CX + 0.5f;
				float offY = dx != 0 ? (lane == 0 ? CY : CY + 1) : CY + 0.5f;

				var pts = new Vector2[reach + 1];
				for (int i = 0; i <= reach; i++)
					pts[i] = new Vector2((offX + dx * i) * CellSize, (offY + dy * i) * CellSize);

				pathsList.Add((pts, 0f, (float)(reach - 1) / reach));
			}
		}

		AddLineEffect(pathsList, new Color(0.4f, 0.9f, 1f), 800, 0.4f);
	}

	private void SpawnDashedTendrils()
	{
		var pathsList = new List<(Vector2[], float, float)>();
		var seeds = AllEdgeSeeds();

		for (int a = 0; a < 8; a++)
		{
			var seed = seeds[a % seeds.Count];
			float x = seed.Ix, y = seed.Iy;
			int sdx = seed.Dx, sdy = seed.Dy;
			int len = 5 + _rng.Next(6);

			var pts = new List<Vector2>();
			pts.Add(new Vector2(x * CellSize, y * CellSize));

			for (int i = 0; i < len; i++)
			{
				if (i > 1 && _rng.NextSingle() < 0.3f)
				{
					if (sdx == 0) { sdx = _rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
					else { sdy = _rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
				}
				x += sdx; y += sdy;
				pts.Add(new Vector2(x * CellSize, y * CellSize));
			}

			pathsList.Add((pts.ToArray(), 0f, 1f));
		}

		AddLineEffect(pathsList, new Color(0.5f, 0.9f, 0.3f), 1600, 0.15f, dashed: true);
	}

	private void SpawnArcChain()
	{
		var allPts = new List<Vector2>();
		float cx = CX + 0.5f, cy = CY + 0.5f;

		for (int a = 0; a < 8; a++)
		{
			float tx = CX + _rng.Next(-5, 6) + 0.5f;
			float ty = CY + _rng.Next(-5, 6) + 0.5f;

			float midOffX = (_rng.NextSingle() - 0.5f) * 2f;
			float midOffY = (_rng.NextSingle() - 0.5f) * 2f;
			float midX = (cx + tx) / 2f + midOffX;
			float midY = (cy + ty) / 2f + midOffY;

			int subSegs = 3 + _rng.Next(2);
			for (int i = 0; i <= subSegs; i++)
			{
				float t = (float)i / subSegs;
				float px = (1 - t) * (1 - t) * cx + 2 * (1 - t) * t * midX + t * t * tx;
				float py = (1 - t) * (1 - t) * cy + 2 * (1 - t) * t * midY + t * t * ty;
				allPts.Add(new Vector2(px * CellSize, py * CellSize));
			}

			cx = tx; cy = ty;
		}

		AddLineEffect(
			new List<(Vector2[], float, float)> { (allPts.ToArray(), 0f, 1f) },
			new Color(1f, 0.95f, 0.4f), 1200, 0.1f
		);
	}

	private void SpawnCircuitTrace()
	{
		// BFS-like right-angle paths — collect all segments then chain into paths
		var segs = new List<(float X1, float Y1, float X2, float Y2, float Dist)>();
		var frontier = new List<(float X, float Y, int Dx, int Dy, float Dist)>
		{
			(CX + 1, CY + 0.5f, 1, 0, 0),
			(CX, CY + 0.5f, -1, 0, 0),
			(CX + 0.5f, CY + 1, 0, 1, 0),
			(CX + 0.5f, CY, 0, -1, 0),
		};
		float maxDist = 0;

		while (frontier.Count > 0 && segs.Count < 50)
		{
			int idx = _rng.Next(frontier.Count);
			var item = frontier[idx];
			frontier.RemoveAt(idx);

			float nx = item.X + item.Dx, ny = item.Y + item.Dy;
			segs.Add((item.X, item.Y, nx, ny, item.Dist));
			if (item.Dist > maxDist) maxDist = item.Dist;

			if (_rng.NextSingle() < 0.7f)
				frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1));
			if (_rng.NextSingle() < 0.35f)
			{
				var (pdx, pdy) = item.Dy == 0
					? (0, _rng.NextSingle() < 0.5f ? 1 : -1)
					: (_rng.NextSingle() < 0.5f ? 1 : -1, 0);
				frontier.Add((nx, ny, pdx, pdy, item.Dist + 1));
			}
		}

		// Each segment becomes its own short path (simplistic but works for showcase)
		var paths = new List<(Vector2[], float, float)>();
		foreach (var s in segs)
		{
			float normDist = maxDist > 0 ? s.Dist / maxDist : 0f;
			paths.Add((new Vector2[]
			{
				new(s.X1 * CellSize, s.Y1 * CellSize),
				new(s.X2 * CellSize, s.Y2 * CellSize)
			}, normDist, normDist));
		}

		AddLineEffect(paths, new Color(1f, 0.6f, 0.2f), 1400, 0.15f);
	}

	private void SpawnConvergingDrain()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 50, 0.85f, 0.50f);
		AddLineEffect(paths, new Color(0.8f, 0.3f, 1f), 1000, 0.15f, reverse: true);
	}

	private void SpawnWavePulse()
	{
		var pathsList = new List<(Vector2[], float, float)>();

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			var pts = new List<Vector2>();
			for (int dist = 0; dist < 12; dist++)
			{
				float bx = CX + 0.5f + dx * dist;
				float by = CY + 0.5f + dy * dist;

				// Perpendicular sine displacement
				float perpAmt = 0.3f * MathF.Sin(dist * 1.2f + _rng.NextSingle() * 0.5f);
				float px = dy != 0 ? perpAmt : 0;
				float py = dx != 0 ? perpAmt : 0;

				pts.Add(new Vector2((bx + px) * CellSize, (by + py) * CellSize));
			}

			pathsList.Add((pts.ToArray(), 0f, 1f));

			// Side branches as separate short paths
			for (int i = 0; i < pts.Count; i++)
			{
				if (_rng.NextSingle() < 0.4f)
				{
					float bpx = dy == 0 ? 0 : (_rng.NextSingle() < 0.5f ? 1 : -1);
					float bpy = dx == 0 ? 0 : (_rng.NextSingle() < 0.5f ? 1 : -1);
					var from = pts[i];
					var to = from + new Vector2(bpx * CellSize, bpy * CellSize);
					float normDist = (float)i / pts.Count;
					pathsList.Add((new[] { from, to }, normDist, normDist + 0.05f));
				}
			}
		}

		AddLineEffect(pathsList, new Color(0.2f, 0.85f, 0.75f), 1500, 0.2f);
	}

	private void SpawnSineRipple()
	{
		var pathsList = new List<(Vector2[], float, float)>();

		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			for (int lane = 0; lane < 3; lane++)
			{
				float offX, offY;
				if (dx != 0)
				{
					offX = CX + 0.5f;
					offY = CY + (lane - 1) * 0.5f + 0.5f;
				}
				else
				{
					offX = CX + (lane - 1) * 0.5f + 0.5f;
					offY = CY + 0.5f;
				}

				var pts = new Vector2[11];
				for (int i = 0; i <= 10; i++)
				{
					float baseX = offX + dx * i;
					float baseY = offY + dy * i;
					// Sine displacement will be baked into the points
					// (shader's sine animation won't apply since these are Line2D, not grid shader)
					pts[i] = new Vector2(baseX * CellSize, baseY * CellSize);
				}

				pathsList.Add((pts, 0f, 1f));
			}
		}

		AddLineEffect(pathsList, new Color(0.3f, 0.6f, 1f), 2000, 0.2f);
	}

	// ─── Grid Shader Effects ───

	private void SpawnShockwaveRing()
	{
		var mat = MakeRingsMat(0, new Color(0.9f, 0.9f, 1f), 10, 2f);
		AddGridEffect(mat, 1000);
	}

	private void SpawnPulseBeam()
	{
		var mat = MakeRingsMat(1, new Color(1f, 0.4f, 0.6f), 8, 3f);
		AddGridEffect(mat, 1500);
	}

	private void SpawnGhostFlicker()
	{
		var mat = MakeScatterMat(0, new Color(0.75f, 0.6f, 1f), 6f);
		AddGridEffect(mat, 2000);
	}

	private void SpawnDigitalCascade()
	{
		var mat = MakeScatterMat(1, new Color(0.2f, 1f, 0.4f), 5f);
		AddGridEffect(mat, 1800);
	}

	// ─── MultiMesh Effects ───

	private void SpawnDottedTrail()
	{
		// Build spiral points for dots
		var positions = new List<Vector2>();
		var distances = new List<float>();
		int x = CX + 1, y = CY + 1;
		int dx = 1, dy = 0;
		int stepsInLeg = 1, stepsTaken = 0, turnsAtLen = 0;
		int maxSegs = 36;

		for (int i = 0; i < maxSegs; i++)
		{
			float jx = (_rng.NextSingle() - 0.5f) * 0.15f;
			float jy = (_rng.NextSingle() - 0.5f) * 0.15f;
			float mx = (x + 0.5f + jx) * CellSize;
			float my = (y + 0.5f + jy) * CellSize;
			positions.Add(new Vector2(mx, my));
			distances.Add(i / (float)(maxSegs - 1));

			x += dx; y += dy;
			stepsTaken++;
			if (stepsTaken >= stepsInLeg)
			{
				int tmp = dx; dx = -dy; dy = tmp;
				stepsTaken = 0;
				turnsAtLen++;
				if (turnsAtLen >= 2) { turnsAtLen = 0; stepsInLeg++; }
			}
		}

		var mm = new MultiMesh();
		mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		mm.UseColors = true;
		mm.Mesh = new QuadMesh { Size = new Vector2(6, 6) };
		mm.InstanceCount = positions.Count;

		for (int i = 0; i < positions.Count; i++)
		{
			mm.SetInstanceTransform2D(i, Transform2D.Identity.Translated(positions[i]));
			mm.SetInstanceColor(i, new Color(distances[i], _rng.NextSingle(), 0, 1));
		}

		var mat = MakePointMat(new Color(0.9f, 0.7f, 1f), 0.15f, 1f);
		AddDotEffect(mm, mat, 2000);
	}

	private void SpawnJitterBurst()
	{
		var seeds = AllEdgeSeeds();
		int armCount = 10;
		int armLen = 4;
		var positions = new List<Vector2>();
		var distances = new List<float>();

		for (int a = 0; a < armCount; a++)
		{
			var seed = seeds[a % seeds.Count];
			float px = seed.Ix + 0.5f, py = seed.Iy + 0.5f;
			int sdx = seed.Dx, sdy = seed.Dy;

			for (int i = 0; i < armLen; i++)
			{
				if (i > 0 && _rng.NextSingle() < 0.6f)
				{
					if (sdx == 0) { sdx = _rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
					else { sdy = _rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
				}
				float jx = (_rng.NextSingle() - 0.5f) * 0.3f * CellSize;
				float jy = (_rng.NextSingle() - 0.5f) * 0.3f * CellSize;
				positions.Add(new Vector2(px * CellSize + jx, py * CellSize + jy));
				distances.Add(i / (float)(armLen - 1));

				px += sdx; py += sdy;
			}
		}

		var mm = new MultiMesh();
		mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		mm.UseColors = true;
		mm.Mesh = new QuadMesh { Size = new Vector2(5, 5) };
		mm.InstanceCount = positions.Count;

		for (int i = 0; i < positions.Count; i++)
		{
			mm.SetInstanceTransform2D(i, Transform2D.Identity.Translated(positions[i]));
			mm.SetInstanceColor(i, new Color(distances[i], _rng.NextSingle(), 0, 1));
		}

		var mat = MakePointMat(new Color(1f, 0.25f, 0.2f), 0.1f, 1f);
		AddDotEffect(mm, mat, 400);
	}

	// ─── Looping ZoC Effects ───

	private void SpawnZocGridWave()
	{
		_effects.RemoveAll(e => { if (e.Looping) e.Destroy(); return e.Looping; });
		var mat = MakeRingsMat(0, new Color(0.25f, 0.55f, 1f), 6, 1.5f, 1f, true, 0f);
		AddGridEffect(mat, 1800, looping: true);
	}

	private void SpawnZocSineRings()
	{
		_effects.RemoveAll(e => { if (e.Looping) e.Destroy(); return e.Looping; });
		var mat = MakeRingsMat(2, new Color(0.25f, 0.55f, 1f), 6, 2f, 1f, true, 0f);
		AddGridEffect(mat, 2500, looping: true);
	}

	private void SpawnZocSequentialRings()
	{
		_effects.RemoveAll(e => { if (e.Looping) e.Destroy(); return e.Looping; });
		int zocR = 6;
		var paths = BuildSquareRingPaths(zocR);
		AddLineEffect(paths, new Color(0.25f, 0.55f, 1f), 1800, 0.15f,
			loopMode: true, baseAlpha: 0.10f);
	}

	/// <summary>
	/// Build concentric square rings as sequential Line2D paths.
	/// Ring 1 (closest) → ring N (farthest), each a closed square perimeter.
	/// The single chain ordering means the line_wave shader sweeps ring by ring.
	/// </summary>
	private List<(Vector2[] Points, float DistStart, float DistEnd)> BuildSquareRingPaths(int zocR)
	{
		var paths = new List<(Vector2[], float, float)>(zocR);

		for (int ring = 1; ring <= zocR; ring++)
		{
			float tlx = (CX - ring) * CellSize;
			float tly = (CY - ring) * CellSize;
			float brx = (CX + 1 + ring) * CellSize;
			float bry = (CY + 1 + ring) * CellSize;

			// Single closed perimeter: TL → TR → BR → BL → TL
			var pts = new Vector2[]
			{
				new(tlx, tly), new(brx, tly),
				new(brx, tly), new(brx, bry),
				new(brx, bry), new(tlx, bry),
				new(tlx, bry), new(tlx, tly),
			};

			float normStart = (ring - 1) / (float)zocR;
			float normEnd = ring / (float)zocR;
			paths.Add((pts, normStart, normEnd));
		}

		return paths;
	}



	private void SpawnZocDashedPulse()
	{
		_effects.RemoveAll(e => { if (e.Looping) e.Destroy(); return e.Looping; });

		var pathsList = new List<(Vector2[], float, float)>();
		int zocR = 6;

		// Cardinal radial lines
		for (int dir = 0; dir < 4; dir++)
		{
			int dx = dir == 0 ? 1 : dir == 1 ? -1 : 0;
			int dy = dir == 2 ? 1 : dir == 3 ? -1 : 0;

			for (int lane = 0; lane < 2; lane++)
			{
				float offX = dy != 0 ? (lane == 0 ? CX : CX + 1) : CX + 0.5f;
				float offY = dx != 0 ? (lane == 0 ? CY : CY + 1) : CY + 0.5f;

				var pts = new Vector2[zocR + 1];
				for (int i = 0; i <= zocR; i++)
					pts[i] = new Vector2((offX + dx * i) * CellSize, (offY + dy * i) * CellSize);

				pathsList.Add((pts, 0f, (float)(zocR - 1) / zocR));
			}
		}

		// Diagonal staircase lines
		int[] ddx = { 1, 1, -1, -1 };
		int[] ddy = { 1, -1, 1, -1 };
		for (int dir = 0; dir < 4; dir++)
		{
			var pts = new List<Vector2>();
			int sx = CX + (ddx[dir] > 0 ? 1 : 0);
			int sy = CY + (ddy[dir] > 0 ? 1 : 0);

			for (int i = 0; i < zocR; i++)
			{
				int cx2 = sx + ddx[dir] * i;
				int cy2 = sy + ddy[dir] * i;
				pts.Add(new Vector2(cx2 * CellSize, cy2 * CellSize));
				pts.Add(new Vector2((cx2 + ddx[dir]) * CellSize, cy2 * CellSize));
				pts.Add(new Vector2((cx2 + ddx[dir]) * CellSize, (cy2 + ddy[dir]) * CellSize));
			}

			pathsList.Add((pts.ToArray(), 0f, 1f));
		}

		AddLineEffect(pathsList, new Color(0.25f, 0.55f, 1f), 2200, 0.12f,
			dashed: true, loopMode: true, baseAlpha: 0.10f);
	}

	// ─── Game Effects (from TS GridEffects.ts) ───

	/// <summary>Move command: lightning trails from back edge (opposite direction).</summary>
	private void SpawnMoveEffect()
	{
		// Simulate movement to the right, so trail goes left
		var seeds = EdgeSeeds(-1, 0);
		var paths = BuildLightningPaths(seeds, 30, 0.90f, 0.55f);
		AddLineEffect(paths, new Color(0.4f, 0.75f, 1f), 1200, 0.15f);
	}

	/// <summary>Root (F key): lightning radiates outward from all edges, short.</summary>
	private void SpawnRootEffect()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 28, 0.78f, 0.44f);
		AddLineEffect(paths, new Color(0.45f, 0.8f, 0.95f), 900, 0.12f);
	}

	/// <summary>Uproot: same shape but contracts inward — outer segments fade first.</summary>
	private void SpawnUprootEffect()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 28, 0.78f, 0.44f);
		AddLineEffect(paths, new Color(0.45f, 0.8f, 0.95f), 900, 0.12f,
			contract: true, fadeSpeed: 0f);
	}

	/// <summary>Explosion (D key): long slow burst on all sides.</summary>
	private void SpawnExplodeEffect()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 56, 0.90f, 0.56f);
		AddLineEffect(paths, new Color(1f, 0.55f, 0.15f), 1800, 0.28f,
			fadeSpeed: 1f);
	}

	/// <summary>Wall conversion: fast short burst on all sides.</summary>
	private void SpawnConvertEffect()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 14, 0.66f, 0.34f);
		AddLineEffect(paths, new Color(0.85f, 0.75f, 0.5f), 450, 0.25f);
	}

	/// <summary>Uproot complete: small cross contracting inward (4 arms × 3 segments).</summary>
	private void SpawnUprootDoneEffect()
	{
		var pathsList = new List<(Vector2[], float, float)>();
		foreach (var seed in AllEdgeSeeds())
		{
			var pts = new List<Vector2>();
			float x = seed.Ix, y = seed.Iy;
			pts.Add(new Vector2(x * CellSize, y * CellSize));
			for (int i = 0; i < 3; i++)
			{
				x += seed.Dx; y += seed.Dy;
					pts.Add(new Vector2(x * CellSize, y * CellSize));
			}
			pathsList.Add((pts.ToArray(), 0f, 1f));
		}

		AddLineEffect(pathsList, new Color(0.5f, 0.7f, 0.9f), 600, 0.15f,
			contract: true, fadeSpeed: 0f);
	}

	/// <summary>Selection: 3 concentric squares blinking outward.</summary>
	private void SpawnSelectEffect()
	{
		var pathsList = new List<(Vector2[], float, float)>();
		float ctrX = CX + 0.5f, ctrY = CY + 0.5f;

		for (int ring = 0; ring < 3; ring++)
		{
			float half = 0.45f + ring * 0.15f;
			var tl = new Vector2((ctrX - half) * CellSize, (ctrY - half) * CellSize);
			var tr = new Vector2((ctrX + half) * CellSize, (ctrY - half) * CellSize);
			var br = new Vector2((ctrX + half) * CellSize, (ctrY + half) * CellSize);
			var bl = new Vector2((ctrX - half) * CellSize, (ctrY + half) * CellSize);

			// Single closed square as one path
			var pts = new[] { tl, tr, br, bl, tl };
			float normDist = ring / 2f;
			pathsList.Add((pts, normDist, normDist));
		}

		AddLineEffect(pathsList, new Color(1f, 1f, 1f), 350, 0.15f);
	}

	/// <summary>Builder spawn: 4 staggered single-line arms.</summary>
	private void SpawnBuilderSpawnEffect()
	{
		var pathsList = new List<(Vector2[], float, float)>();

		// 4 arms from edge midpoints, staggered by 2 dist units each
		var armDefs = new (float ix, float iy, int dx, int dy)[]
		{
			(CX + 1, CY + 0.5f, 1, 0),
			(CX, CY + 0.5f, -1, 0),
			(CX + 0.5f, CY + 1, 0, 1),
			(CX + 0.5f, CY, 0, -1),
		};

		int armLen = 3;
		float maxDist = armLen - 1 + (armDefs.Length - 1) * 2;

		for (int a = 0; a < armDefs.Length; a++)
		{
			var (ix, iy, dx, dy) = armDefs[a];
			var pts = new Vector2[armLen + 1];
			for (int i = 0; i <= armLen; i++)
				pts[i] = new Vector2((ix + dx * i) * CellSize, (iy + dy * i) * CellSize);

			float start = (a * 2) / maxDist;
			float end = Math.Min(1f, (a * 2 + armLen - 1) / maxDist);
			pathsList.Add((pts, start, end));
		}

		AddLineEffect(pathsList, new Color(0.45f, 0.8f, 1f), 800, 0.25f);
	}

	/// <summary>Soldier spawn: 6 jittery arms from one edge only.</summary>
	private void SpawnSoldierSpawnEffect()
	{
		// Soldier fires toward the right, so spawn burst goes left (back edge)
		var seeds = EdgeSeeds(-1, 0);
		var pathsList = new List<(Vector2[], float, float)>();
		int armLen = 4;

		for (int a = 0; a < 6; a++)
		{
			var seed = seeds[a % seeds.Count];
			float x = seed.Ix, y = seed.Iy;
			int sdx = seed.Dx, sdy = seed.Dy;
			var pts = new List<Vector2> { new(x * CellSize, y * CellSize) };

			for (int i = 0; i < armLen; i++)
			{
				if (i > 0 && _rng.NextSingle() < 0.6f)
				{
					if (sdx == 0) { sdx = _rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
					else { sdy = _rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
				}
				x += sdx; y += sdy;
				pts.Add(new Vector2(x * CellSize, y * CellSize));
			}

			pathsList.Add((pts.ToArray(), 0f, 1f));
		}

		AddLineEffect(pathsList, new Color(0.9f, 0.5f, 0.2f), 1000, 0.15f);
	}

	/// <summary>Stunner spawn: fast branching lightning in all directions.</summary>
	private void SpawnStunnerSpawnEffect()
	{
		var paths = BuildLightningPaths(AllEdgeSeeds(), 36, 0.88f, 0.60f);
		AddLineEffect(paths, new Color(0.3f, 0.7f, 1f), 600, 0.18f);
	}

	/// <summary>Spawn cell: perimeter trace around the cell (2 passes clockwise).</summary>
	private void SpawnCellEffect()
	{
		// Trace cell perimeter clockwise, 2 passes
		var corners = new (float x, float y)[]
		{
			(CX, CY), (CX + 1, CY), (CX + 1, CY + 1), (CX, CY + 1)
		};

		var pts = new List<Vector2>();
		for (int pass = 0; pass < 2; pass++)
		{
			for (int i = 0; i < 4; i++)
			{
				var from = corners[i];
				pts.Add(new Vector2(from.x * CellSize, from.y * CellSize));
			}
		}
		// Close back to start
		pts.Add(new Vector2(corners[0].x * CellSize, corners[0].y * CellSize));

		AddLineEffect(
			new List<(Vector2[], float, float)> { (pts.ToArray(), 0f, 1f) },
			new Color(0.4f, 0.85f, 0.6f), 600, 0.25f
		);
	}

	// ─── All Effects ───

	private void SpawnAll()
	{
		SpawnSpiralTrace();
		SpawnElectricLightning();
		SpawnStraightTracer();
		SpawnDashedTendrils();
		SpawnArcChain();
		SpawnCircuitTrace();
		SpawnConvergingDrain();
		SpawnWavePulse();
		SpawnSineRipple();
		SpawnShockwaveRing();
		SpawnPulseBeam();
		SpawnGhostFlicker();
		SpawnDigitalCascade();
		SpawnDottedTrail();
		SpawnJitterBurst();

		SpawnMoveEffect();
		SpawnRootEffect();
		SpawnUprootEffect();
		SpawnExplodeEffect();
		SpawnConvertEffect();
		SpawnUprootDoneEffect();
		SpawnSelectEffect();
		SpawnBuilderSpawnEffect();
		SpawnSoldierSpawnEffect();
		SpawnStunnerSpawnEffect();
		SpawnCellEffect();
	}
}
