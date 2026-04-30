using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Blocker.Game.Showcase;

/// <summary>
/// Showcase for digital flora — vines, leaves, and flowers growing tightly
/// between clusters of walls and nuggets, tangled in the grid lines.
///
/// Procedural vine generation traces block perimeters and branches into empty space.
/// Multiple thin vines overlap and cross for a tangled, overgrown look.
/// Leaves (diamond + teardrop) and flowers (5-petal) decorate vine endpoints.
///
/// Shaders handle shimmer, taper, color shift. CPU handles gentle sway per frame.
/// Everything is continuous and slow — no one-shot effects.
/// </summary>
public partial class DigitalFloraShowcase : Node2D
{
	private const int GridW = 40;
	private const int GridH = 30;
	private const float CellSize = 28f;

	private static readonly Color BackgroundColor = new(0.04f, 0.04f, 0.08f);
	private static readonly Color GridLineColor = new(0.10f, 0.10f, 0.16f);
	private static readonly Color WallColor = new(0.35f, 0.35f, 0.42f);
	private static readonly Color NuggetColor = new(0.7f, 0.55f, 0.2f);
	private static readonly Color WallHighlight = new(0.45f, 0.45f, 0.55f);
	private static readonly Color NuggetHighlight = new(0.9f, 0.75f, 0.3f);

	private Shader _vineShader = null!;
	private Shader _leafShader = null!;
	private readonly Random _rng = new(42);

	private readonly List<VineDecoration> _vines = new();
	private readonly List<LeafCluster> _leafClusters = new();
	private float _ageMs;

	private readonly HashSet<(int X, int Y)> _wallCells = new();
	private readonly HashSet<(int X, int Y)> _nuggetCells = new();
	private readonly HashSet<(int X, int Y)> _allBlockCells = new();

	public override void _Ready()
	{
		RenderingServer.SetDefaultClearColor(BackgroundColor);

		var vp = GetViewportRect().Size;
		Position = new Vector2(
			(vp.X - GridW * CellSize) / 2f,
			(vp.Y - GridH * CellSize) / 2f
		);

		_vineShader = GD.Load<Shader>("res://Assets/Shaders/digital_vine.gdshader");
		_leafShader = GD.Load<Shader>("res://Assets/Shaders/digital_leaf.gdshader");

		DefineClusters();
		BuildUI();
		QueueRedraw();

		BuildAllZones();
	}

	public override void _Process(double delta)
	{
		_ageMs += (float)delta * 1000f;

		foreach (var vine in _vines)
		{
			vine.UpdateSway(_ageMs);
			vine.CoreMat.SetShaderParameter("age_ms", _ageMs);
			vine.GlowMat.SetShaderParameter("age_ms", _ageMs);
		}

		foreach (var cluster in _leafClusters)
			cluster.Mat.SetShaderParameter("age_ms", _ageMs);
	}

	public override void _Draw()
	{
		for (int x = 0; x <= GridW; x++)
			DrawLine(new Vector2(x * CellSize, 0), new Vector2(x * CellSize, GridH * CellSize), GridLineColor, 1f);
		for (int y = 0; y <= GridH; y++)
			DrawLine(new Vector2(0, y * CellSize), new Vector2(GridW * CellSize, y * CellSize), GridLineColor, 1f);

		foreach (var (x, y) in _wallCells)
		{
			DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), WallColor);
			DrawRect(new Rect2(x * CellSize + 3, y * CellSize + 3, CellSize - 6, CellSize - 6), WallHighlight);
		}

		foreach (var (x, y) in _nuggetCells)
		{
			DrawRect(new Rect2(x * CellSize, y * CellSize, CellSize, CellSize), NuggetColor);
			DrawRect(new Rect2(x * CellSize + 4, y * CellSize + 4, CellSize - 8, CellSize - 8), NuggetHighlight);
		}
	}

	// ─── Cluster Definitions ──────────────────────────────────────────

	private void DefineClusters()
	{
		// Zone A: Wall cluster (top-left) — 3x3 block
		for (int dx = 0; dx < 3; dx++)
			for (int dy = 0; dy < 3; dy++)
				_wallCells.Add((3 + dx, 3 + dy));

		// Zone B: Nugget cluster (top-right) — 3x3 block
		for (int dx = 0; dx < 3; dx++)
			for (int dy = 0; dy < 3; dy++)
				_nuggetCells.Add((26 + dx, 3 + dy));

		// Zone C: Mixed cluster (center) — walls top, nuggets bottom
		for (int dx = 0; dx < 4; dx++)
		{
			_wallCells.Add((14 + dx, 12));
			_wallCells.Add((14 + dx, 13));
		}
		for (int dx = 0; dx < 3; dx++)
		{
			_nuggetCells.Add((14 + dx, 16));
			_nuggetCells.Add((14 + dx, 17));
		}

		// Zone D: Sparse scattered walls (bottom-left)
		_wallCells.Add((3, 22));
		_wallCells.Add((6, 24));
		_wallCells.Add((4, 26));
		_wallCells.Add((8, 22));
		_wallCells.Add((7, 26));

		// Zone E: Dense nugget cluster (bottom-right) — 4x3
		for (int dx = 0; dx < 4; dx++)
			for (int dy = 0; dy < 3; dy++)
				_nuggetCells.Add((28 + dx, 22 + dy));
		_wallCells.Add((30, 21));
		_wallCells.Add((31, 21));
		_wallCells.Add((30, 25));
		_wallCells.Add((31, 25));

		foreach (var c in _wallCells) _allBlockCells.Add(c);
		foreach (var c in _nuggetCells) _allBlockCells.Add(c);
	}

	// ─── Procedural Vine Generation ───────────────────────────────────

	private HashSet<(int X, int Y)> FindPerimeterPoints(HashSet<(int X, int Y)> cells)
	{
		var result = new HashSet<(int, int)>();
		foreach (var (cx, cy) in cells)
		{
			for (int dx = 0; dx <= 1; dx++)
			{
				for (int dy = 0; dy <= 1; dy++)
				{
					int px = cx + dx, py = cy + dy;
					bool hasBlock = cells.Contains((px - 1, py - 1)) || cells.Contains((px, py - 1)) ||
									cells.Contains((px - 1, py)) || cells.Contains((px, py));
					bool hasEmpty = !cells.Contains((px - 1, py - 1)) || !cells.Contains((px, py - 1)) ||
									!cells.Contains((px - 1, py)) || !cells.Contains((px, py));
					if (hasBlock && hasEmpty)
						result.Add((px, py));
				}
			}
		}
		return result;
	}

	private Vector2[] GenerateVinePath(HashSet<(int X, int Y)> perimeter, int minLen, int maxLen)
	{
		if (perimeter.Count == 0) return Array.Empty<Vector2>();

		var perimeterList = perimeter.ToList();
		var start = perimeterList[_rng.Next(perimeterList.Count)];
		var path = new List<Vector2> { new(start.X, start.Y) };

		int cx = start.X, cy = start.Y;
		int len = minLen + _rng.Next(maxLen - minLen + 1);

		for (int step = 0; step < len; step++)
		{
			var neighbors = new List<(int X, int Y)>();
			int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
			foreach (var d in dirs)
			{
				int nx = cx + d[0], ny = cy + d[1];
				if (perimeter.Contains((nx, ny)))
					neighbors.Add((nx, ny));
			}

			if (neighbors.Count == 0)
			{
				var fallback = new List<(int, int)>();
				foreach (var d in dirs)
				{
					int nx = cx + d[0], ny = cy + d[1];
					if (nx >= 0 && ny >= 0 && nx < GridW && ny < GridH)
						fallback.Add((nx, ny));
				}
				if (fallback.Count == 0) break;
				var next = fallback[_rng.Next(fallback.Count)];
				cx = next.Item1; cy = next.Item2;
				path.Add(new Vector2(cx, cy));
				continue;
			}

			var chosen = neighbors[_rng.Next(neighbors.Count)];
			cx = chosen.X; cy = chosen.Y;
			path.Add(new Vector2(cx, cy));
		}

		return path.Count >= 2 ? path.ToArray() : Array.Empty<Vector2>();
	}

	private Vector2[] GenerateOutwardTendril(HashSet<(int X, int Y)> cells, int maxReach)
	{
		var perimeter = FindPerimeterPoints(cells);
		if (perimeter.Count == 0) return Array.Empty<Vector2>();

		var start = perimeter.ToList()[_rng.Next(perimeter.Count)];
		var path = new List<Vector2> { new(start.X, start.Y) };

		int cx = start.X, cy = start.Y;
		int dx = 0, dy = 0;

		for (int i = 0; i < 2 + _rng.Next(maxReach); i++)
		{
			if (i == 0)
			{
				bool isOnBlock = cells.Contains((cx - 1, cy)) || cells.Contains((cx, cy)) ||
								 cells.Contains((cx, cy - 1)) || cells.Contains((cx - 1, cy - 1));
				if (isOnBlock)
				{
					var outDirs = new List<(int, int)>();
					if (!cells.Contains((cx - 1, cy - 1)) && !cells.Contains((cx, cy - 1))) outDirs.Add((0, -1));
					if (!cells.Contains((cx, cy - 1)) && !cells.Contains((cx, cy))) outDirs.Add((1, 0));
					if (!cells.Contains((cx, cy)) && !cells.Contains((cx - 1, cy))) outDirs.Add((0, 1));
					if (!cells.Contains((cx - 1, cy)) && !cells.Contains((cx - 1, cy - 1))) outDirs.Add((-1, 0));
					if (outDirs.Count > 0)
					{
						var pick = outDirs[_rng.Next(outDirs.Count)];
						dx = pick.Item1; dy = pick.Item2;
					}
				}
				if (dx == 0 && dy == 0)
				{
					var dirs = new[] { (1, 0), (-1, 0), (0, 1), (0, -1) };
					var pick = dirs[_rng.Next(4)];
					dx = pick.Item1; dy = pick.Item2;
				}
			}
			else if (_rng.NextSingle() < 0.3f)
			{
				if (dx != 0) { dy = _rng.NextSingle() < 0.5f ? 1 : -1; dx = 0; }
				else { dx = _rng.NextSingle() < 0.5f ? 1 : -1; dy = 0; }
			}

			cx += dx; cy += dy;
			if (cx < 0 || cy < 0 || cx > GridW || cy > GridH) break;
			path.Add(new Vector2(cx, cy));
		}

		return path.Count >= 2 ? path.ToArray() : Array.Empty<Vector2>();
	}

	// ─── Build Zones ──────────────────────────────────────────────────

	private void BuildAllZones()
	{
		BuildZoneA();
		BuildZoneB();
		BuildZoneC();
		BuildZoneD();
		BuildZoneE();
	}

	private void BuildZoneA()
	{
		var cells = new HashSet<(int, int)>();
		for (int dx = 0; dx < 3; dx++)
			for (int dy = 0; dy < 3; dy++)
				cells.Add((3 + dx, 3 + dy));

		var color = new Color(0.12f, 0.72f, 0.38f);
		var leafColor = new Color(0.25f, 0.88f, 0.5f);
		var flowerColor = new Color(0.5f, 1f, 0.7f);
		var flowerAccent = new Color(1f, 0.95f, 0.8f);

		var perimeter = FindPerimeterPoints(cells);

		// Dense perimeter vines (many short overlapping vines)
		for (int i = 0; i < 12; i++)
		{
			var path = GenerateVinePath(perimeter, 3, 8);
			if (path.Length >= 2)
				SpawnVine(path, color, 1.2f, 0.15f + _rng.NextSingle() * 0.1f, 1.5f);
		}

		// Outward tendrils
		for (int i = 0; i < 6; i++)
		{
			var path = GenerateOutwardTendril(cells, 3);
			if (path.Length >= 2)
				SpawnVine(path, color * 0.8f, 1.5f, 0.2f, 1.2f);
		}

		// Interior vines threading between cells
		SpawnVine(new Vector2[] { new(3.5f, 3.5f), new(4.5f, 3.5f), new(5.5f, 3.5f), new(5.5f, 4.5f) },
			color * 0.6f, 0.8f, 0.15f, 1.0f);
		SpawnVine(new Vector2[] { new(3.5f, 5.5f), new(4.5f, 5.5f), new(4.5f, 4.5f), new(5.5f, 4.5f) },
			color * 0.6f, 0.8f, 0.18f, 1.0f);

		// Leaves and flowers at perimeter
		var leafPositions = new List<Vector2>();
		var flowerPositions = new List<Vector2>();
		foreach (var (px, py) in perimeter)
		{
			if (_rng.NextSingle() < 0.35f)
				leafPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.4f,
					py + (_rng.NextSingle() - 0.5f) * 0.4f));
			if (_rng.NextSingle() < 0.12f)
				flowerPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.3f,
					py + (_rng.NextSingle() - 0.5f) * 0.3f));
		}

		if (leafPositions.Count > 0)
			SpawnDecorCluster(leafPositions.ToArray(), leafColor, flowerColor, flowerAccent, 5f);
		if (flowerPositions.Count > 0)
			SpawnFlowerCluster(flowerPositions.ToArray(), flowerColor, flowerAccent, 6f);
	}

	private void BuildZoneB()
	{
		var cells = new HashSet<(int, int)>();
		for (int dx = 0; dx < 3; dx++)
			for (int dy = 0; dy < 3; dy++)
				cells.Add((26 + dx, 3 + dy));

		var color = new Color(0.78f, 0.62f, 0.18f);
		var accentColor = new Color(1f, 0.85f, 0.3f);
		var leafColor = new Color(0.9f, 0.75f, 0.25f);
		var flowerColor = new Color(1f, 0.9f, 0.5f);
		var flowerAccent = new Color(1f, 1f, 0.9f);

		var perimeter = FindPerimeterPoints(cells);

		for (int i = 0; i < 10; i++)
		{
			var path = GenerateVinePath(perimeter, 2, 6);
			if (path.Length >= 2)
				SpawnVine(path, i % 2 == 0 ? color : accentColor, 1.0f, 0.15f + _rng.NextSingle() * 0.1f, 1.3f);
		}

		// Crystal spikes radiating outward
		for (int i = 0; i < 5; i++)
		{
			var path = GenerateOutwardTendril(cells, 2);
			if (path.Length >= 2)
				SpawnVine(path, accentColor, 1.3f, 0.18f, 1.0f);
		}

		// Interior lattice
		SpawnVine(new Vector2[] { new(26.5f, 3.5f), new(27.5f, 3.5f), new(28.5f, 3.5f), new(28.5f, 4.5f), new(27.5f, 4.5f), new(26.5f, 4.5f), new(26.5f, 3.5f) },
			color * 0.7f, 0.6f, 0.12f, 1.0f);
		SpawnVine(new Vector2[] { new(26.5f, 5.5f), new(27.5f, 5.5f), new(28.5f, 5.5f), new(28.5f, 5f), new(27.5f, 5f), new(26.5f, 5f), new(26.5f, 5.5f) },
			color * 0.7f, 0.6f, 0.14f, 1.0f);

		var leafPositions = new List<Vector2>();
		var flowerPositions = new List<Vector2>();
		foreach (var (px, py) in perimeter)
		{
			if (_rng.NextSingle() < 0.3f)
				leafPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.4f,
					py + (_rng.NextSingle() - 0.5f) * 0.4f));
			if (_rng.NextSingle() < 0.15f)
				flowerPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.3f,
					py + (_rng.NextSingle() - 0.5f) * 0.3f));
		}

		if (leafPositions.Count > 0)
			SpawnDecorCluster(leafPositions.ToArray(), leafColor, flowerColor, flowerAccent, 4.5f);
		if (flowerPositions.Count > 0)
			SpawnFlowerCluster(flowerPositions.ToArray(), flowerColor, flowerAccent, 5f);
	}

	private void BuildZoneC()
	{
		var walls = new HashSet<(int, int)>();
		var nuggets = new HashSet<(int, int)>();
		for (int dx = 0; dx < 4; dx++) { walls.Add((14 + dx, 12)); walls.Add((14 + dx, 13)); }
		for (int dx = 0; dx < 3; dx++) { nuggets.Add((14 + dx, 16)); nuggets.Add((14 + dx, 17)); }

		var allCells = new HashSet<(int, int)>(walls);
		foreach (var n in nuggets) allCells.Add(n);

		var vineColor = new Color(0.1f, 0.58f, 0.68f);
		var leafColor = new Color(0.2f, 0.82f, 0.72f);
		var flowerColor = new Color(0.4f, 0.95f, 0.85f);
		var flowerAccent = new Color(0.9f, 1f, 1f);

		// Vines around wall cluster
		var wallPerimeter = FindPerimeterPoints(walls);
		for (int i = 0; i < 8; i++)
		{
			var path = GenerateVinePath(wallPerimeter, 3, 7);
			if (path.Length >= 2)
				SpawnVine(path, vineColor, 1.0f, 0.15f + _rng.NextSingle() * 0.08f, 1.3f);
		}

		// Vines around nugget cluster
		var nuggetPerimeter = FindPerimeterPoints(nuggets);
		for (int i = 0; i < 6; i++)
		{
			var path = GenerateVinePath(nuggetPerimeter, 2, 5);
			if (path.Length >= 2)
				SpawnVine(path, vineColor * 0.9f, 1.0f, 0.15f + _rng.NextSingle() * 0.08f, 1.2f);
		}

		// Bridge vines connecting walls to nuggets (vertical)
		SpawnVine(new Vector2[] { new(14, 14), new(14, 15), new(14, 16) },
			vineColor, 1.5f, 0.18f, 1.2f);
		SpawnVine(new Vector2[] { new(15, 14), new(15, 15), new(15, 16) },
			vineColor * 1.1f, 1.5f, 0.2f, 1.2f);
		SpawnVine(new Vector2[] { new(16, 14), new(16, 15), new(16, 16) },
			vineColor, 1.5f, 0.17f, 1.2f);

		// Outward spread
		for (int i = 0; i < 5; i++)
		{
			var path = GenerateOutwardTendril(allCells, 3);
			if (path.Length >= 2)
				SpawnVine(path, vineColor * 0.7f, 1.3f, 0.18f, 1.0f);
		}

		// Leaves and flowers on bridges and perimeters
		var allPerimeter = FindPerimeterPoints(allCells);
		var leafPos = new List<Vector2> {
			new(14.5f, 14.5f), new(15.5f, 14.5f), new(16.5f, 14.5f),
			new(14.5f, 15.5f), new(15.5f, 15.5f), new(16.5f, 15.5f),
		};
		var flowerPos = new List<Vector2> { new(15, 15), new(15.5f, 14.5f), new(15, 16) };

		foreach (var (px, py) in allPerimeter)
		{
			if (_rng.NextSingle() < 0.25f)
				leafPos.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.4f,
					py + (_rng.NextSingle() - 0.5f) * 0.4f));
		}

		if (leafPos.Count > 0)
			SpawnDecorCluster(leafPos.ToArray(), leafColor, flowerColor, flowerAccent, 4.5f);
		if (flowerPos.Count > 0)
			SpawnFlowerCluster(flowerPos.ToArray(), flowerColor, flowerAccent, 5.5f);
	}

	private void BuildZoneD()
	{
		var cells = new HashSet<(int, int)> { (3, 22), (6, 24), (4, 26), (8, 22), (7, 26) };
		var color = new Color(0.38f, 0.48f, 0.78f);
		var leafColor = new Color(0.5f, 0.65f, 0.95f);
		var flowerColor = new Color(0.65f, 0.8f, 1f);
		var flowerAccent = new Color(0.9f, 0.95f, 1f);

		// Each isolated block gets its own tiny vine tangle
		foreach (var cell in cells)
		{
			var singleCell = new HashSet<(int, int)> { cell };
			var perimeter = FindPerimeterPoints(singleCell);

			// 2-4 short vines per block
			for (int i = 0; i < 2 + _rng.Next(3); i++)
			{
				var path = GenerateVinePath(perimeter, 2, 5);
				if (path.Length >= 2)
					SpawnVine(path, color * (0.8f + _rng.NextSingle() * 0.2f),
						1.0f, 0.12f + _rng.NextSingle() * 0.08f, 1.0f);
			}

			// Tendril reaching toward nearest neighbor
			var path2 = GenerateOutwardTendril(singleCell, 2);
			if (path2.Length >= 2)
				SpawnVine(path2, color * 0.7f, 1.2f, 0.15f, 0.8f);
		}

		// Bridge vines between nearby blocks
		SpawnVine(new Vector2[] { new(4, 22), new(5, 23), new(6, 24) },
			color * 0.6f, 1.0f, 0.15f, 0.8f);
		SpawnVine(new Vector2[] { new(6, 25), new(5, 25.5f), new(4, 26) },
			color * 0.6f, 1.0f, 0.15f, 0.8f);
		SpawnVine(new Vector2[] { new(7, 24), new(7, 25), new(7, 26) },
			color * 0.6f, 1.0f, 0.18f, 0.8f);

		// Scattered leaves and flowers
		var leafPositions = new List<Vector2>();
		var flowerPositions = new List<Vector2>();
		foreach (var (cx, cy) in cells)
		{
			for (int i = 0; i < 2; i++)
				leafPositions.Add(new Vector2(cx + 0.5f + (_rng.NextSingle() - 0.5f) * 1.5f,
					cy + 0.5f + (_rng.NextSingle() - 0.5f) * 1.5f));
			if (_rng.NextSingle() < 0.4f)
				flowerPositions.Add(new Vector2(cx + 0.5f + (_rng.NextSingle() - 0.5f),
					cy + 0.5f + (_rng.NextSingle() - 0.5f)));
		}

		if (leafPositions.Count > 0)
			SpawnDecorCluster(leafPositions.ToArray(), leafColor, flowerColor, flowerAccent, 4f);
		if (flowerPositions.Count > 0)
			SpawnFlowerCluster(flowerPositions.ToArray(), flowerColor, flowerAccent, 4.5f);
	}

	private void BuildZoneE()
	{
		var cells = new HashSet<(int, int)>();
		for (int dx = 0; dx < 4; dx++)
			for (int dy = 0; dy < 3; dy++)
				cells.Add((28 + dx, 22 + dy));
		cells.Add((30, 21)); cells.Add((31, 21));
		cells.Add((30, 25)); cells.Add((31, 25));

		var color1 = new Color(0.58f, 0.28f, 0.68f);
		var color2 = new Color(0.28f, 0.68f, 0.48f);
		var leafColor = new Color(0.45f, 0.35f, 0.72f);
		var leafColor2 = new Color(0.3f, 0.72f, 0.55f);
		var flowerColor = new Color(0.7f, 0.5f, 0.9f);
		var flowerAccent = new Color(1f, 0.9f, 1f);

		var perimeter = FindPerimeterPoints(cells);

		// Dense perimeter vines — two colors alternating
		for (int i = 0; i < 14; i++)
		{
			var path = GenerateVinePath(perimeter, 3, 10);
			if (path.Length >= 2)
				SpawnVine(path, i % 2 == 0 ? color1 : color2,
					1.0f, 0.15f + _rng.NextSingle() * 0.08f, 1.4f);
		}

		// Many outward tendrils — overgrown look
		for (int i = 0; i < 8; i++)
		{
			var path = GenerateOutwardTendril(cells, 3);
			if (path.Length >= 2)
				SpawnVine(path, (i % 2 == 0 ? color1 : color2) * 0.75f,
					1.3f, 0.18f + _rng.NextSingle() * 0.06f, 1.0f);
		}

		// Interior vines
		SpawnVine(new Vector2[] { new(28.5f, 22.5f), new(29.5f, 22.5f), new(30.5f, 22.5f), new(31.5f, 22.5f) },
			color1 * 0.6f, 0.7f, 0.12f, 1.0f);
		SpawnVine(new Vector2[] { new(28.5f, 24.5f), new(29.5f, 24.5f), new(30.5f, 24.5f), new(31.5f, 24.5f) },
			color2 * 0.6f, 0.7f, 0.14f, 1.0f);
		SpawnVine(new Vector2[] { new(28.5f, 22.5f), new(28.5f, 23.5f), new(28.5f, 24.5f) },
			color1 * 0.5f, 0.6f, 0.12f, 0.8f);
		SpawnVine(new Vector2[] { new(31.5f, 22.5f), new(31.5f, 23.5f), new(31.5f, 24.5f) },
			color2 * 0.5f, 0.6f, 0.12f, 0.8f);

		// Dense leaves and flowers
		var leafPositions = new List<Vector2>();
		var flowerPositions = new List<Vector2>();
		foreach (var (px, py) in perimeter)
		{
			if (_rng.NextSingle() < 0.4f)
				leafPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.4f,
					py + (_rng.NextSingle() - 0.5f) * 0.4f));
			if (_rng.NextSingle() < 0.15f)
				flowerPositions.Add(new Vector2(px + (_rng.NextSingle() - 0.5f) * 0.3f,
					py + (_rng.NextSingle() - 0.5f) * 0.3f));
		}

		if (leafPositions.Count > 0)
			SpawnDecorCluster(leafPositions.ToArray(), leafColor, flowerColor, flowerAccent, 4.5f);
		if (flowerPositions.Count > 0)
			SpawnFlowerCluster(flowerPositions.ToArray(), flowerColor, flowerAccent, 5.5f);
	}

	// ─── Spawn Helpers ────────────────────────────────────────────────

	private void SpawnVine(Vector2[] gridPoints, Color color, float swayAmt, float swaySpeed,
		float width = 1.5f)
	{
		if (gridPoints.Length < 2) return;

		var pixelPoints = new Vector2[gridPoints.Length];
		for (int i = 0; i < gridPoints.Length; i++)
			pixelPoints[i] = gridPoints[i] * CellSize;

		var coreMat = MakeVineMat(color, 0.8f, 0.28f, swaySpeed);
		var glowMat = MakeVineMat(color, 0.10f, 0.20f, swaySpeed);

		var coreLine = MakeVineLine(pixelPoints, coreMat, width);
		var glowLine = MakeVineLine(pixelPoints, glowMat, width * 3.5f);

		_vines.Add(new VineDecoration
		{
			GridPoints = (Vector2[])gridPoints.Clone(),
			CoreLine = coreLine,
			GlowLine = glowLine,
			CoreMat = coreMat,
			GlowMat = glowMat,
			SwayAmount = swayAmt,
			SwaySpeed = swaySpeed,
			CellSize = CellSize,
			Phase = _rng.NextSingle() * MathF.Tau,
		});
	}

	private ShaderMaterial MakeVineMat(Color color, float fadeMult, float baseAlpha, float shimmerSpeed)
	{
		var mat = new ShaderMaterial { Shader = _vineShader };
		mat.SetShaderParameter("vine_color", color);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("base_alpha", baseAlpha);
		mat.SetShaderParameter("shimmer_speed", shimmerSpeed);
		mat.SetShaderParameter("progress", 1.0f);
		mat.SetShaderParameter("age_ms", 0.0f);
		mat.SetShaderParameter("taper", 0.35f);
		mat.SetShaderParameter("color_shift", 0.04f);
		return mat;
	}

	private Line2D MakeVineLine(Vector2[] pixelPoints, ShaderMaterial mat, float width)
	{
		var line = new Line2D
		{
			Points = pixelPoints,
			Width = width,
			JointMode = Line2D.LineJointMode.Round,
			BeginCapMode = Line2D.LineCapMode.Round,
			EndCapMode = Line2D.LineCapMode.Round,
			Material = mat,
		};

		var grad = new Gradient();
		grad.SetColor(0, new Color(0f, 0f, 0f, 1f));
		grad.SetOffset(0, 0f);
		grad.SetColor(1, new Color(1f, 1f, 1f, 1f));
		grad.SetOffset(1, 1f);
		line.Gradient = grad;

		AddChild(line);
		return line;
	}

	private void SpawnDecorCluster(Vector2[] gridPositions, Color leafColor,
		Color flowerColor, Color flowerAccent, float size)
	{
		if (gridPositions.Length == 0) return;

		var mm = new MultiMesh();
		mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		mm.UseColors = true;
		mm.Mesh = new QuadMesh { Size = new Vector2(size, size) };
		mm.InstanceCount = gridPositions.Length;

		for (int i = 0; i < gridPositions.Length; i++)
		{
			var px = gridPositions[i] * CellSize;
			float rotation = _rng.NextSingle() * MathF.PI * 2f;
			var transform = Transform2D.Identity.Translated(px).RotatedLocal(rotation);
			mm.SetInstanceTransform2D(i, transform);

			float leafType = _rng.NextSingle() < 0.5f ? 0f : 0.5f;
			float phase = _rng.NextSingle();
			float sizeFactor = 0.5f + _rng.NextSingle() * 0.5f;
			mm.SetInstanceColor(i, new Color(leafType, phase, sizeFactor, 1f));
		}

		var mat = new ShaderMaterial { Shader = _leafShader };
		mat.SetShaderParameter("leaf_color", leafColor);
		mat.SetShaderParameter("accent_color", flowerAccent);
		mat.SetShaderParameter("fade_mult", 0.75f);
		mat.SetShaderParameter("base_alpha", 0.22f);
		mat.SetShaderParameter("progress", 1.0f);
		mat.SetShaderParameter("age_ms", 0.0f);
		mat.SetShaderParameter("sway_speed", 0.8f);
		mat.SetShaderParameter("sway_amount", 1.0f);
		mat.SetShaderParameter("sparkle", 0.2f);

		var node = new MultiMeshInstance2D { Multimesh = mm, Material = mat };
		AddChild(node);
		_leafClusters.Add(new LeafCluster { Mat = mat, MmNode = node });
	}

	private void SpawnFlowerCluster(Vector2[] gridPositions, Color flowerColor,
		Color flowerAccent, float size)
	{
		if (gridPositions.Length == 0) return;

		var mm = new MultiMesh();
		mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		mm.UseColors = true;
		mm.Mesh = new QuadMesh { Size = new Vector2(size, size) };
		mm.InstanceCount = gridPositions.Length;

		for (int i = 0; i < gridPositions.Length; i++)
		{
			var px = gridPositions[i] * CellSize;
			float rotation = _rng.NextSingle() * MathF.PI * 2f;
			var transform = Transform2D.Identity.Translated(px).RotatedLocal(rotation);
			mm.SetInstanceTransform2D(i, transform);

			float phase = _rng.NextSingle();
			float sizeFactor = 0.6f + _rng.NextSingle() * 0.4f;
			mm.SetInstanceColor(i, new Color(1.0f, phase, sizeFactor, 1f));
		}

		var mat = new ShaderMaterial { Shader = _leafShader };
		mat.SetShaderParameter("leaf_color", flowerColor);
		mat.SetShaderParameter("accent_color", flowerAccent);
		mat.SetShaderParameter("fade_mult", 0.85f);
		mat.SetShaderParameter("base_alpha", 0.25f);
		mat.SetShaderParameter("progress", 1.0f);
		mat.SetShaderParameter("age_ms", 0.0f);
		mat.SetShaderParameter("sway_speed", 0.6f);
		mat.SetShaderParameter("sway_amount", 0.6f);
		mat.SetShaderParameter("sparkle", 0.35f);

		var node = new MultiMeshInstance2D { Multimesh = mm, Material = mat };
		AddChild(node);
		_leafClusters.Add(new LeafCluster { Mat = mat, MmNode = node });
	}

	// ─── Growth Animation ─────────────────────────────────────────────

	private void TriggerGrowthAll()
	{
		ClearFlora();

		foreach (var vine in _vines)
		{
			vine.CoreMat.SetShaderParameter("grow_mode", true);
			vine.GlowMat.SetShaderParameter("grow_mode", true);
			vine.CoreMat.SetShaderParameter("progress", 0.0f);
			vine.GlowMat.SetShaderParameter("progress", 0.0f);
		}
		foreach (var cluster in _leafClusters)
		{
			cluster.Mat.SetShaderParameter("grow_mode", true);
			cluster.Mat.SetShaderParameter("progress", 0.0f);
		}

		var tween = CreateTween();
		tween.SetParallel(true);

		foreach (var vine in _vines)
		{
			tween.TweenMethod(Callable.From<float>(p =>
			{
				vine.CoreMat.SetShaderParameter("progress", p);
				vine.GlowMat.SetShaderParameter("progress", p);
			}), 0.0f, 1.0f, 6.0f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		}

		foreach (var cluster in _leafClusters)
		{
			tween.TweenMethod(Callable.From<float>(p =>
			{
				cluster.Mat.SetShaderParameter("progress", p);
			}), 0.0f, 1.0f, 7.0f).SetEase(Tween.EaseType.Out).SetTrans(Tween.TransitionType.Cubic);
		}

		tween.Chain().TweenCallback(Callable.From(() =>
		{
			foreach (var vine in _vines)
			{
				vine.CoreMat.SetShaderParameter("grow_mode", false);
				vine.GlowMat.SetShaderParameter("grow_mode", false);
			}
			foreach (var cluster in _leafClusters)
				cluster.Mat.SetShaderParameter("grow_mode", false);
		}));
	}

	private void ClearFlora()
	{
		foreach (var vine in _vines)
		{
			vine.CoreLine.QueueFree();
			vine.GlowLine.QueueFree();
		}
		foreach (var cluster in _leafClusters)
			cluster.MmNode.QueueFree();
		_vines.Clear();
		_leafClusters.Clear();
		BuildAllZones();
	}

	// ─── UI ───────────────────────────────────────────────────────────

	private void BuildUI()
	{
		var canvas = new CanvasLayer { Name = "UI", Layer = 10 };
		AddChild(canvas);

		var panel = new PanelContainer();
		panel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
		panel.OffsetLeft = 8; panel.OffsetTop = 8;
		panel.OffsetRight = 260; panel.OffsetBottom = -8;
		canvas.AddChild(panel);

		var vbox = new VBoxContainer();
		vbox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		vbox.AddThemeConstantOverride("separation", 4);
		panel.AddChild(vbox);

		var title = new Label { Text = "Digital Flora Showcase" };
		title.AddThemeFontSizeOverride("font_size", 16);
		vbox.AddChild(title);

		var desc = new Label
		{
			Text = "Tangled vines, leaves and flowers growing\nbetween blocks on grid lines.",
			AutowrapMode = TextServer.AutowrapMode.WordSmart,
		};
		desc.AddThemeFontSizeOverride("font_size", 11);
		vbox.AddChild(desc);
		vbox.AddChild(new HSeparator());

		AddBtn(vbox, "Trigger Growth (6s)", TriggerGrowthAll);
		AddBtn(vbox, "Reset", ClearFlora);
		vbox.AddChild(new HSeparator());

		AddSection(vbox, "Zones:", new[]
		{
			"A) Walls + Emerald (top-left)",
			"B) Nuggets + Gold Crystal (top-right)",
			"C) Mixed + Cyan Forest (center)",
			"D) Sparse + Blue Tendrils (bottom-left)",
			"E) Dense + Purple Overgrowth (bottom-right)",
		});
		vbox.AddChild(new HSeparator());

		AddSection(vbox, "Rendering:", new[]
		{
			"Vines: Line2D + digital_vine shader",
			"Leaves: MultiMesh diamond + teardrop",
			"Flowers: MultiMesh 5-petal + glow center",
			"CPU: gentle sway per frame",
			"GPU: shimmer, taper, color shift, sparkle",
		});
	}

	private static void AddSection(Container parent, string header, string[] items)
	{
		var h = new Label { Text = header };
		h.AddThemeFontSizeOverride("font_size", 12);
		h.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
		parent.AddChild(h);
		foreach (var item in items)
		{
			var lbl = new Label { Text = $"  {item}" };
			lbl.AddThemeFontSizeOverride("font_size", 10);
			lbl.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
			parent.AddChild(lbl);
		}
	}

	private static void AddBtn(Container parent, string text, Action onPressed)
	{
		var btn = new Button { Text = text };
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.Pressed += onPressed;
		parent.AddChild(btn);
	}

	// ─── Data Classes ─────────────────────────────────────────────────

	private class VineDecoration
	{
		public Vector2[] GridPoints = Array.Empty<Vector2>();
		public Line2D CoreLine = null!;
		public Line2D GlowLine = null!;
		public ShaderMaterial CoreMat = null!;
		public ShaderMaterial GlowMat = null!;
		public float SwayAmount;
		public float SwaySpeed;
		public float CellSize;
		public float Phase;

		public void UpdateSway(float ageMs)
		{
			if (GridPoints.Length < 2) return;

			var pts = new Vector2[GridPoints.Length];
			float maxDist = GridPoints.Length - 1;

			for (int i = 0; i < GridPoints.Length; i++)
			{
				float t = maxDist > 0 ? i / maxDist : 0f;

				Vector2 segDir;
				if (i == 0)
					segDir = (GridPoints[1] - GridPoints[0]).Normalized();
				else if (i == GridPoints.Length - 1)
					segDir = (GridPoints[i] - GridPoints[i - 1]).Normalized();
				else
					segDir = (GridPoints[i + 1] - GridPoints[i - 1]).Normalized();

				var perp = new Vector2(-segDir.Y, segDir.X);

				float swayOffset = MathF.Sin(ageMs * SwaySpeed * 0.001f + t * 4.0f + Phase)
					* SwayAmount * t * (1f - t * 0.4f);

				pts[i] = GridPoints[i] * CellSize + perp * swayOffset;
			}

			CoreLine.Points = pts;
			GlowLine.Points = pts;
		}
	}

	private class LeafCluster
	{
		public ShaderMaterial Mat = null!;
		public MultiMeshInstance2D MmNode = null!;
	}
}
