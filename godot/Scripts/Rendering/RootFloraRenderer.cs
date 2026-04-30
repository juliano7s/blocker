using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Blocker.Game.Rendering;

/// <summary>
/// Manages persistent digital-flora vine decorations for rooted blocks.
///
/// For each connected cluster of rooted blocks, procedurally generates
/// Line2D vines that hug the cluster perimeter and thread between cells.
/// Vines grow in over the rooting duration (36 ticks), then stay alive
/// with gentle sway until the block uproots.
///
/// When a new block roots adjacent to an existing rooted block, the cluster
/// is regenerated so vines connect the two seamlessly.
///
/// All vines use the digital_vine.gdshader for GPU shimmer, taper, and color.
/// CPU updates sway per frame.
/// </summary>
public partial class RootFloraRenderer : Node2D
{
	private Shader _vineShader = null!;
	private Shader _leafShader = null!;
	private readonly Random _rng = new();

	private readonly Dictionary<int, RootCluster> _blockToCluster = new();
	private readonly List<RootCluster> _clusters = new();

	public override void _Ready()
	{
		_vineShader = GD.Load<Shader>("res://Assets/Shaders/digital_vine.gdshader");
		_leafShader = GD.Load<Shader>("res://Assets/Shaders/digital_leaf.gdshader");
	}

	public override void _Process(double delta)
	{
		float ageMs = (float)Time.GetTicksMsec();

		foreach (var cluster in _clusters)
		{
			foreach (var vine in cluster.Vines)
			{
				vine.UpdateSway(ageMs);
				vine.CoreMat.SetShaderParameter("age_ms", ageMs);
				vine.GlowMat.SetShaderParameter("age_ms", ageMs);
			}
			if (cluster.LeafMat != null)
				cluster.LeafMat.SetShaderParameter("age_ms", ageMs);
		}
	}

	/// <summary>
	/// Called every frame from GridRenderer for each block in rooting/rooted/uprooting state.
	/// Updates growth progress and manages cluster lifecycle.
	/// </summary>
	public void UpdateBlock(Block block, Color teamColor)
	{
		if (block.Type == BlockType.Wall) return;

		bool rooting = block.State == BlockState.Rooting;
		bool rooted = block.State == BlockState.Rooted;
		bool uprooting = block.State == BlockState.Uprooting;

		if (!rooting && !rooted && !uprooting)
		{
			RemoveBlock(block.Id);
			return;
		}

		if (uprooting && block.RootProgress <= 0)
		{
			RemoveBlock(block.Id);
			return;
		}

		if (_blockToCluster.ContainsKey(block.Id))
		{
			UpdateGrowth(block);
			return;
		}

		if (rooting || rooted)
			AddBlock(block, teamColor);
	}

	/// <summary>
	/// Immediately remove all flora for a block by ID (e.g. block destroyed).
	/// </summary>
	public void RemoveBlock(int blockId)
	{
		if (!_blockToCluster.TryGetValue(blockId, out var cluster)) return;

		cluster.Blocks.Remove(blockId);
		_blockToCluster.Remove(blockId);

		if (cluster.Blocks.Count == 0)
		{
			cluster.Destroy();
			_clusters.Remove(cluster);
		}
		else
		{
			RebuildCluster(cluster);
		}
	}

	/// <summary>
	/// Clean up everything (scene exit, game end).
	/// </summary>
	public void ClearAll()
	{
		foreach (var cluster in _clusters)
			cluster.Destroy();
		_clusters.Clear();
		_blockToCluster.Clear();
	}

	public void SetGameState(GameState gameState)
	{
	}

	private void AddBlock(Block block, Color teamColor)
	{
		RootCluster? adjacentCluster = FindAdjacentCluster(block);

		if (adjacentCluster != null)
		{
			adjacentCluster.Blocks[block.Id] = block;
			_blockToCluster[block.Id] = adjacentCluster;
			RebuildCluster(adjacentCluster);
		}
		else
		{
			var cluster = new RootCluster { TeamColor = teamColor };
			cluster.Blocks[block.Id] = block;
			_blockToCluster[block.Id] = cluster;
			_clusters.Add(cluster);
			BuildClusterVines(cluster);
		}
	}

	private void UpdateGrowth(Block block)
	{
		if (!_blockToCluster.TryGetValue(block.Id, out var cluster)) return;

		float progress = (float)block.RootProgress / Constants.RootTicks;
		bool rooting = block.State == BlockState.Rooting;
		bool rooted = block.State == BlockState.Rooted;
		bool uprooting = block.State == BlockState.Uprooting;

		if (uprooting)
		{
			if (cluster.GrowthProgress > progress)
				cluster.GrowthProgress = progress;
		}
		else if (rooting)
		{
			if (cluster.GrowthProgress < progress)
				cluster.GrowthProgress = progress;
		}
		else if (rooted)
		{
			cluster.GrowthProgress = 1.0f;
		}

		foreach (var vine in cluster.Vines)
		{
			vine.CoreMat.SetShaderParameter("progress", cluster.GrowthProgress);
			vine.GlowMat.SetShaderParameter("progress", cluster.GrowthProgress);
		}
		if (cluster.LeafMat != null)
			cluster.LeafMat.SetShaderParameter("progress", cluster.GrowthProgress);
	}

	private RootCluster? FindAdjacentCluster(Block block)
	{
		foreach (var offset in GridPos.OrthogonalOffsets)
		{
			var neighbor = block.Pos + offset;
			foreach (var cluster in _clusters)
			{
				foreach (var b in cluster.Blocks.Values)
				{
					if (b.Pos == neighbor)
						return cluster;
				}
			}
		}
		return null;
	}

	private void RebuildCluster(RootCluster cluster)
	{
		cluster.DestroyVines();
		BuildClusterVines(cluster);
	}

	// ─── Cluster Vine Generation ──────────────────────────────────────

	private void BuildClusterVines(RootCluster cluster)
	{
		var cells = new HashSet<(int X, int Y)>();
		foreach (var b in cluster.Blocks.Values)
			cells.Add((b.Pos.X, b.Pos.Y));

		if (cells.Count == 0) return;

		var perimeter = FindPerimeterPoints(cells);
		var color = cluster.TeamColor;
		var vineColor = new Color(
			color.R * 0.6f + 0.05f,
			color.G * 0.6f + 0.25f,
			color.B * 0.6f + 0.1f
		);
		var glowColor = vineColor * 0.8f;

		// Perimeter-hugging vines — density scales with cluster size
		int vineCount = Math.Min(cells.Count * 3 + 2, 20);
		for (int i = 0; i < vineCount; i++)
		{
			var path = GenerateVinePath(perimeter, 3, 6 + cells.Count);
			if (path.Length < 2) continue;

			SpawnVine(cluster, path, vineColor, 1.0f, 0.18f + _rng.NextSingle() * 0.08f, 1.3f);
		}

		// Outward tendrils
		int tendrilCount = Math.Max(2, cells.Count);
		for (int i = 0; i < tendrilCount; i++)
		{
			var path = GenerateOutwardTendril(cells, 2);
			if (path.Length < 2) continue;

			SpawnVine(cluster, path, glowColor, 1.2f, 0.15f, 1.0f);
		}

		// Interior bridge vines between adjacent cells
		SpawnInteriorVines(cluster, cells, vineColor * 0.7f);

		// Leaves at perimeter
		var leafPositions = new List<Vector2>();
		foreach (var (px, py) in perimeter)
		{
			if (_rng.NextSingle() < 0.3f)
				leafPositions.Add(new Vector2(
					px + (_rng.NextSingle() - 0.5f) * 0.4f,
					py + (_rng.NextSingle() - 0.5f) * 0.4f));
		}
		if (leafPositions.Count > 0)
			SpawnLeaves(cluster, leafPositions, vineColor);

		// Apply current growth
		float progress = cluster.GrowthProgress;
		foreach (var vine in cluster.Vines)
		{
			vine.CoreMat.SetShaderParameter("progress", progress);
			vine.GlowMat.SetShaderParameter("progress", progress);
		}
		if (cluster.LeafMat != null)
			cluster.LeafMat.SetShaderParameter("progress", progress);
	}

	private void SpawnVine(RootCluster cluster, Vector2[] gridPoints, Color color,
		float swayAmt, float swaySpeed, float width)
	{
		if (gridPoints.Length < 2) return;

		var pixelPoints = new Vector2[gridPoints.Length];
		for (int i = 0; i < gridPoints.Length; i++)
			pixelPoints[i] = GridToPixel(gridPoints[i]);

		var coreMat = MakeVineMat(color, 0.75f, 0.25f, swaySpeed);
		var glowMat = MakeVineMat(color, 0.08f, 0.18f, swaySpeed);

		var coreLine = MakeLine(pixelPoints, coreMat, width);
		var glowLine = MakeLine(pixelPoints, glowMat, width * 3f);

		cluster.Vines.Add(new FloraVine
		{
			GridPoints = (Vector2[])gridPoints.Clone(),
			CoreLine = coreLine,
			GlowLine = glowLine,
			CoreMat = coreMat,
			GlowMat = glowMat,
			SwayAmount = swayAmt,
			SwaySpeed = swaySpeed,
			Phase = _rng.NextSingle() * MathF.Tau,
		});
	}

	private void SpawnInteriorVines(RootCluster cluster, HashSet<(int X, int Y)> cells, Color color)
	{
		// Find pairs of adjacent cells and draw short vines between their centers
		foreach (var (cx, cy) in cells)
		{
			if (cells.Contains((cx + 1, cy)))
			{
				SpawnVine(cluster, new Vector2[] {
					new(cx + 0.5f, cy + 0.5f),
					new(cx + 1.5f, cy + 0.5f),
				}, color, 0.6f, 0.15f, 0.8f);
			}
			if (cells.Contains((cx, cy + 1)))
			{
				SpawnVine(cluster, new Vector2[] {
					new(cx + 0.5f, cy + 0.5f),
					new(cx + 0.5f, cy + 1.5f),
				}, color, 0.6f, 0.15f, 0.8f);
			}
		}
	}

	private void SpawnLeaves(RootCluster cluster, List<Vector2> positions, Color color)
	{
		var mm = new MultiMesh();
		mm.TransformFormat = MultiMesh.TransformFormatEnum.Transform2D;
		mm.UseColors = true;
		mm.Mesh = new QuadMesh { Size = new Vector2(4.5f, 4.5f) };
		mm.InstanceCount = positions.Count;

		for (int i = 0; i < positions.Count; i++)
		{
			var px = GridToPixel(positions[i]);
			float rotation = _rng.NextSingle() * MathF.PI * 2f;
			mm.SetInstanceTransform2D(i, Transform2D.Identity.Translated(px).RotatedLocal(rotation));

			float leafType = _rng.NextSingle() < 0.5f ? 0f : 0.5f;
			float phase = _rng.NextSingle();
			float sizeFactor = 0.5f + _rng.NextSingle() * 0.5f;
			mm.SetInstanceColor(i, new Color(leafType, phase, sizeFactor, 1f));
		}

		var mat = new ShaderMaterial { Shader = _leafShader };
		mat.SetShaderParameter("leaf_color", color);
		mat.SetShaderParameter("accent_color", color.Lightened(0.4f));
		mat.SetShaderParameter("fade_mult", 0.7f);
		mat.SetShaderParameter("base_alpha", 0.2f);
		mat.SetShaderParameter("progress", cluster.GrowthProgress);
		mat.SetShaderParameter("age_ms", 0.0f);
		mat.SetShaderParameter("sway_speed", 0.8f);
		mat.SetShaderParameter("sway_amount", 0.8f);
		mat.SetShaderParameter("sparkle", 0.15f);

		var node = new MultiMeshInstance2D { Multimesh = mm, Material = mat };
		AddChild(node);

		cluster.LeafMat = mat;
		cluster.LeafNode = node;
	}

	// ─── Procedural Path Generation ───────────────────────────────────

	private static HashSet<(int X, int Y)> FindPerimeterPoints(HashSet<(int X, int Y)> cells)
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

		var start = perimeter.ElementAt(_rng.Next(perimeter.Count));
		var path = new List<Vector2> { new(start.X, start.Y) };

		int cx = start.X, cy = start.Y;
		int len = minLen + _rng.Next(maxLen - minLen + 1);
		int prevX = -1, prevY = -1;

		for (int step = 0; step < len; step++)
		{
			var neighbors = new List<(int X, int Y)>();
			int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };
			foreach (var d in dirs)
			{
				int nx = cx + d[0], ny = cy + d[1];
				if (perimeter.Contains((nx, ny)) && (nx != prevX || ny != prevY))
					neighbors.Add((nx, ny));
			}

			if (neighbors.Count == 0) break;

			var chosen = neighbors[_rng.Next(neighbors.Count)];
			prevX = cx; prevY = cy;
			cx = chosen.X; cy = chosen.Y;
			path.Add(new Vector2(cx, cy));
		}

		return path.Count >= 2 ? path.ToArray() : Array.Empty<Vector2>();
	}

	private Vector2[] GenerateOutwardTendril(HashSet<(int X, int Y)> cells, int maxReach)
	{
		var perimeter = FindPerimeterPoints(cells);
		if (perimeter.Count == 0) return Array.Empty<Vector2>();

		var start = perimeter.ElementAt(_rng.Next(perimeter.Count));
		var path = new List<Vector2> { new(start.X, start.Y) };

		int cx = start.X, cy = start.Y;
		int dx = 0, dy = 0;

		for (int i = 0; i < 1 + _rng.Next(maxReach); i++)
		{
			if (i == 0)
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
			else if (_rng.NextSingle() < 0.3f)
			{
				if (dx != 0) { dy = _rng.NextSingle() < 0.5f ? 1 : -1; dx = 0; }
				else { dx = _rng.NextSingle() < 0.5f ? 1 : -1; dy = 0; }
			}

			cx += dx; cy += dy;
			path.Add(new Vector2(cx, cy));
		}

		return path.Count >= 2 ? path.ToArray() : Array.Empty<Vector2>();
	}

	// ─── Material & Node Factories ────────────────────────────────────

	private ShaderMaterial MakeVineMat(Color color, float fadeMult, float baseAlpha, float shimmerSpeed)
	{
		var mat = new ShaderMaterial { Shader = _vineShader };
		mat.SetShaderParameter("vine_color", color);
		mat.SetShaderParameter("fade_mult", fadeMult);
		mat.SetShaderParameter("base_alpha", baseAlpha);
		mat.SetShaderParameter("shimmer_speed", shimmerSpeed);
		mat.SetShaderParameter("progress", 0.0f);
		mat.SetShaderParameter("age_ms", 0.0f);
		mat.SetShaderParameter("taper", 0.35f);
		mat.SetShaderParameter("color_shift", 0.04f);
		mat.SetShaderParameter("grow_mode", true);
		mat.SetShaderParameter("trail", 0.15f);
		return mat;
	}

	private Line2D MakeLine(Vector2[] pixelPoints, ShaderMaterial mat, float width)
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

	private static Vector2 GridToPixel(Vector2 gridPos) =>
		new(gridPos.X * GridRenderer.CellSize + GridRenderer.GridPadding,
			gridPos.Y * GridRenderer.CellSize + GridRenderer.GridPadding);

	// ─── Data Classes ─────────────────────────────────────────────────

	private class RootCluster
	{
		public readonly Dictionary<int, Block> Blocks = new();
		public readonly List<FloraVine> Vines = new();
		public ShaderMaterial? LeafMat;
		public MultiMeshInstance2D? LeafNode;
		public Color TeamColor;
		public float GrowthProgress;

		public void DestroyVines()
		{
			foreach (var vine in Vines)
			{
				vine.CoreLine.QueueFree();
				vine.GlowLine.QueueFree();
			}
			Vines.Clear();
			if (LeafNode != null)
			{
				LeafNode.QueueFree();
				LeafNode = null;
				LeafMat = null;
			}
		}

		public void Destroy()
		{
			DestroyVines();
		}
	}

	private class FloraVine
	{
		public Vector2[] GridPoints = Array.Empty<Vector2>();
		public Line2D CoreLine = null!;
		public Line2D GlowLine = null!;
		public ShaderMaterial CoreMat = null!;
		public ShaderMaterial GlowMat = null!;
		public float SwayAmount;
		public float SwaySpeed;
		public float Phase;

		public void UpdateSway(float ageMs)
		{
			if (GridPoints.Length < 2) return;

			var pts = new Vector2[GridPoints.Length];
			float maxDist = GridPoints.Length - 1;
			float cellSize = GridRenderer.CellSize;

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

				pts[i] = new Vector2(
					GridPoints[i].X * cellSize + GridRenderer.GridPadding + perp.X * swayOffset,
					GridPoints[i].Y * cellSize + GridRenderer.GridPadding + perp.Y * swayOffset);
			}

			CoreLine.Points = pts;
			GlowLine.Points = pts;
		}
	}
}
