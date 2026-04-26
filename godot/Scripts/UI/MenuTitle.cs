using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MenuTitle : Node2D
{
	private const string Word = "blocker";
	private const int LetterH = 7;
	private const int LetterGap = 1;

	private static readonly Color PrimaryColor = new(0.267f, 0.667f, 1f);

	private static readonly Dictionary<char, int[,]> Letters = new()
	{
		['b'] = new[,] {
			{1,0,0,0,0}, {1,0,0,0,0}, {1,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {1,1,1,1,0}
		},
		['l'] = new[,] {
			{1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,1,0,0,0}
		},
		['o'] = new[,] {
			{0,0,0,0,0}, {0,0,0,0,0}, {0,1,1,1,0}, {1,0,0,0,1}, {1,0,0,0,1}, {1,0,0,0,1}, {0,1,1,1,0}
		},
		['c'] = new[,] {
			{0,0,0,0,0}, {0,0,0,0,0}, {0,1,1,1,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {0,1,1,1,0}
		},
		['k'] = new[,] {
			{1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,1,0}, {1,0,1,0,0}, {1,1,0,0,0}, {1,0,1,0,0}, {1,0,0,1,0}
		},
		['e'] = new[,] {
			{0,0,0,0,0}, {0,0,0,0,0}, {0,1,1,1,0}, {1,0,0,0,1}, {1,1,1,1,0}, {1,0,0,0,0}, {0,1,1,1,0}
		},
		['r'] = new[,] {
			{0,0,0,0,0}, {0,0,0,0,0}, {1,0,1,1,0}, {1,1,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}, {1,0,0,0,0}
		},
	};

	private record struct Cell(int Gx, int Gy);
	private record struct Edge(float X1, float Y1, float X2, float Y2, bool Border);
	private record struct LetterBound(int Start, int End);
	private record struct LightningSeg(float X1, float Y1, float X2, float Y2, float Dist);

	private Cell[] _cells = Array.Empty<Cell>();
	private Edge[] _edges = Array.Empty<Edge>();
	private LetterBound[] _letterBounds = Array.Empty<LetterBound>();
	private int _totalW, _totalH;

	private int _gridOffsetX, _gridOffsetY;
	private Func<float, float, Vector2> _gridToPixel = null!;
	private float _cellSize;

	private struct TitleEffect
	{
		public float[]? EdgeDists;
		public LightningSeg[]? LightningSegs;
		public float MaxDist;
		public float T;
		public float Duration;
		public float TrailDist;
		public float Age;
	}
	private readonly List<TitleEffect> _effects = new();
	private float _timeSinceLastEffect;
	private float _nextEffectDelay = 800f;
	private readonly Random _rng = new();
	private const int MaxEffects = 4;

	public void Initialize(int gridOffsetX, int gridOffsetY, float cellSize, Func<float, float, Vector2> gridToPixel)
	{
		_gridOffsetX = gridOffsetX;
		_gridOffsetY = gridOffsetY;
		_cellSize = cellSize;
		_gridToPixel = gridToPixel;
		ComputeLayout();
		ComputeEdges();
		_timeSinceLastEffect = _nextEffectDelay * 0.7f;
	}

	public int TotalWidth => _totalW;
	public int TotalHeight => _totalH;

	private static int LetterWidth(int[,] grid)
	{
		int maxCol = 0;
		for (int row = 0; row < grid.GetLength(0); row++)
			for (int col = grid.GetLength(1) - 1; col >= 0; col--)
				if (grid[row, col] == 1) { maxCol = Math.Max(maxCol, col + 1); break; }
		return maxCol;
	}

	private void ComputeLayout()
	{
		var cells = new List<Cell>();
		var bounds = new List<LetterBound>();
		int ox = 0;
		foreach (char ch in Word)
		{
			var letter = Letters[ch];
			int lw = LetterWidth(letter);
			bounds.Add(new LetterBound(ox, ox + lw));
			for (int row = 0; row < LetterH; row++)
				for (int col = 0; col < lw; col++)
					if (letter[row, col] == 1)
						cells.Add(new Cell(ox + col, row));
			ox += lw + LetterGap;
		}
		_totalW = ox - LetterGap;
		_totalH = LetterH;
		_cells = cells.ToArray();
		_letterBounds = bounds.ToArray();
	}

	private void ComputeEdges()
	{
		var filled = new HashSet<long>();
		foreach (var c in _cells)
			filled.Add(((long)c.Gx << 32) | (uint)c.Gy);

		var edgeSet = new HashSet<long>();
		var edges = new List<Edge>();

		foreach (var cell in _cells)
		{
			int gx = cell.Gx, gy = cell.Gy;
			var defs = new (float x1, float y1, float x2, float y2, int dx, int dy)[]
			{
				(gx, gy, gx + 1, gy, 0, -1),
				(gx + 1, gy, gx + 1, gy + 1, 1, 0),
				(gx, gy + 1, gx + 1, gy + 1, 0, 1),
				(gx, gy, gx, gy + 1, -1, 0),
			};
			foreach (var (x1, y1, x2, y2, dx, dy) in defs)
			{
				int minX = (int)Math.Min(x1, x2), minY = (int)Math.Min(y1, y2);
				int maxX = (int)Math.Max(x1, x2), maxY = (int)Math.Max(y1, y2);
				long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (uint)maxY;
				if (!edgeSet.Add(key)) continue;
				bool border = !filled.Contains(((long)(gx + dx) << 32) | (uint)(gy + dy));
				edges.Add(new Edge(x1, y1, x2, y2, border));
			}
		}
		_edges = edges.ToArray();
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta * 1000f;
		_timeSinceLastEffect += dt;
		if (_timeSinceLastEffect >= _nextEffectDelay)
		{
			SpawnRandomEffect();
			_timeSinceLastEffect = 0;
			_nextEffectDelay = 2000f + (float)_rng.NextDouble() * 3000f;
		}
		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var e = _effects[i];
			e.T += dt / e.Duration;
			e.Age += dt;
			_effects[i] = e;
			if (e.T >= 1f) _effects.RemoveAt(i);
		}
		QueueRedraw();
	}

	private float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

	private void SpawnRandomEffect()
	{
		int pattern = _rng.Next(4);
		TitleEffect effect = pattern switch
		{
			0 => BuildSweep(false),
			1 => BuildRadial(),
			2 => BuildCascade(),
			_ => BuildLightning(),
		};
		if (_effects.Count >= MaxEffects) _effects.RemoveAt(0);
		_effects.Add(effect);

		if (pattern < 3 && _rng.NextDouble() < 0.4)
		{
			var lightning = BuildLightning();
			if (_effects.Count >= MaxEffects + 1) _effects.RemoveAt(0);
			_effects.Add(lightning);
		}
	}

	private TitleEffect BuildSweep(bool rightToLeft)
	{
		var dists = new float[_edges.Length];
		float maxDist = 0;
		for (int i = 0; i < _edges.Length; i++)
		{
			float midX = (_edges[i].X1 + _edges[i].X2) / 2f;
			dists[i] = rightToLeft ? _totalW - midX : midX;
			if (dists[i] > maxDist) maxDist = dists[i];
		}
		return new TitleEffect
		{
			EdgeDists = dists, LightningSegs = null, MaxDist = maxDist,
			Duration = 1500f + (float)_rng.NextDouble() * 500f, TrailDist = 8f
		};
	}

	private TitleEffect BuildRadial()
	{
		float cx = _totalW / 2f, cy = _totalH / 2f;
		var dists = new float[_edges.Length];
		float maxDist = 0;
		for (int i = 0; i < _edges.Length; i++)
		{
			float mx = (_edges[i].X1 + _edges[i].X2) / 2f;
			float my = (_edges[i].Y1 + _edges[i].Y2) / 2f;
			dists[i] = MathF.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy));
			if (dists[i] > maxDist) maxDist = dists[i];
		}
		return new TitleEffect
		{
			EdgeDists = dists, LightningSegs = null, MaxDist = maxDist,
			Duration = 1800f + (float)_rng.NextDouble() * 600f, TrailDist = 6f
		};
	}

	private TitleEffect BuildCascade()
	{
		var dists = new float[_edges.Length];
		float maxDist = 0;
		for (int i = 0; i < _edges.Length; i++)
		{
			float mx = (_edges[i].X1 + _edges[i].X2) / 2f;
			int letterIdx = 0;
			for (int li = 0; li < _letterBounds.Length; li++)
				if (mx >= _letterBounds[li].Start && mx <= _letterBounds[li].End) { letterIdx = li; break; }
			var b = _letterBounds[letterIdx];
			float within = b.End > b.Start ? (mx - b.Start) / (b.End - b.Start) : 0;
			dists[i] = letterIdx * 2f + within;
			if (dists[i] > maxDist) maxDist = dists[i];
		}
		return new TitleEffect
		{
			EdgeDists = dists, LightningSegs = null, MaxDist = maxDist,
			Duration = 2000f + (float)_rng.NextDouble() * 800f, TrailDist = 4f
		};
	}

	private TitleEffect BuildLightning()
	{
		var borderIndices = new List<int>();
		for (int i = 0; i < _edges.Length; i++)
			if (_edges[i].Border) borderIndices.Add(i);

		var segs = new List<LightningSeg>();
		var visited = new HashSet<long>();
		float maxDist = 0;
		int seedCount = 3 + _rng.Next(4);

		for (int s = 0; s < seedCount && borderIndices.Count > 0; s++)
		{
			var edge = _edges[borderIndices[_rng.Next(borderIndices.Count)]];
			float mx = (edge.X1 + edge.X2) / 2f, my = (edge.Y1 + edge.Y2) / 2f;
			float centerX = _totalW / 2f, centerY = _totalH / 2f;
			bool isHoriz = Math.Abs(edge.Y1 - edge.Y2) < 0.01f;
			int dx = isHoriz ? 0 : (mx < centerX ? -1 : 1);
			int dy = isHoriz ? (my < centerY ? -1 : 1) : 0;
			int ix = (int)Math.Round(mx), iy = (int)Math.Round(my);

			var walkers = new List<(int X, int Y, int Dx, int Dy, int Dist, float Prob)>
				{ (ix, iy, dx, dy, 0, 0.92f) };

			while (walkers.Count > 0 && segs.Count < 80)
			{
				int wi = _rng.Next(walkers.Count);
				var w = walkers[wi];
				int nx = w.X + w.Dx, ny = w.Y + w.Dy;
				if (nx < -6 || nx > _totalW + 6 || ny < -6 || ny > _totalH + 6)
				{ walkers.RemoveAt(wi); continue; }
				int minX = Math.Min(w.X, nx), minY = Math.Min(w.Y, ny);
				int maxX = Math.Max(w.X, nx), maxY = Math.Max(w.Y, ny);
				long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (uint)maxY;
				if (!visited.Add(key)) { walkers.RemoveAt(wi); continue; }

				int dist = w.Dist + 1;
				segs.Add(new LightningSeg(w.X, w.Y, nx, ny, dist));
				if (dist > maxDist) maxDist = dist;

				if (_rng.NextSingle() < w.Prob)
					walkers[wi] = (nx, ny, w.Dx, w.Dy, dist, w.Prob * 0.82f);
				else walkers.RemoveAt(wi);

				if (_rng.NextSingle() < 0.4f)
				{
					var (pdx, pdy) = w.Dy == 0
						? (0, _rng.NextSingle() < 0.5f ? 1 : -1)
						: (_rng.NextSingle() < 0.5f ? 1 : -1, 0);
					walkers.Add((nx, ny, pdx, pdy, dist, w.Prob * 0.5f));
				}
			}
		}

		return new TitleEffect
		{
			EdgeDists = null, LightningSegs = segs.ToArray(), MaxDist = maxDist > 0 ? maxDist : 1,
			Duration = 1400f + (float)_rng.NextDouble() * 600f, TrailDist = 3f
		};
	}

	public override void _Draw()
	{
		if (_gridToPixel == null) return;

		DrawCellFills();
		DrawBaseEdges();

		foreach (var effect in _effects)
		{
			if (effect.EdgeDists != null)
				DrawEdgeEffect(effect);
			if (effect.LightningSegs != null)
				DrawLightningEffect(effect);
		}
	}

	private void DrawCellFills()
	{
		var fillColor = new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, 0.06f);
		foreach (var cell in _cells)
		{
			var tl = _gridToPixel(_gridOffsetX + cell.Gx, _gridOffsetY + cell.Gy);
			DrawRect(new Rect2(tl.X + 1, tl.Y + 1, _cellSize - 2, _cellSize - 2), fillColor);
		}
	}

	private void DrawBaseEdges()
	{
		foreach (var e in _edges)
		{
			float alpha = e.Border ? 0.15f : 0.06f;
			var color = new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha);
			var p1 = _gridToPixel(_gridOffsetX + e.X1, _gridOffsetY + e.Y1);
			var p2 = _gridToPixel(_gridOffsetX + e.X2, _gridOffsetY + e.Y2);
			DrawLine(p1, p2, color, 1f);
		}
	}

	private void DrawEdgeEffect(TitleEffect effect)
	{
		float p = EaseOutCubic(Math.Min(effect.T, 1f));
		float md = Math.Max(effect.MaxDist, 1f);
		float shimmer = 0.85f + 0.15f * MathF.Sin(effect.Age * 0.008f);
		float fadeOut = 1f - effect.T * 0.6f;

		for (int i = 0; i < _edges.Length; i++)
		{
			float wavePos = p * (md + effect.TrailDist);
			float diff = wavePos - effect.EdgeDists![i];
			float brightness = diff < 0 ? 0 : Math.Max(0, 1f - diff / effect.TrailDist);
			if (brightness <= 0.02f) continue;

			float alpha = brightness * fadeOut * shimmer;
			var e = _edges[i];
			var p1 = _gridToPixel(_gridOffsetX + e.X1, _gridOffsetY + e.Y1);
			var p2 = _gridToPixel(_gridOffsetX + e.X2, _gridOffsetY + e.Y2);

			DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.25f), 5f);
			DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.8f), 1.8f);
			if (brightness > 0.6f)
				DrawLine(p1, p2, new Color(1, 1, 1, alpha * 0.5f), 1f);
		}
	}

	private void DrawLightningEffect(TitleEffect effect)
	{
		float p = EaseOutCubic(Math.Min(effect.T, 1f));
		float md = Math.Max(effect.MaxDist, 1f);
		float shimmer = 0.85f + 0.15f * MathF.Sin(effect.Age * 0.008f);
		float fadeOut = 1f - effect.T * 0.6f;

		foreach (var seg in effect.LightningSegs!)
		{
			float wavePos = p * (md + effect.TrailDist);
			float diff = wavePos - seg.Dist;
			float brightness = diff < 0 ? 0 : Math.Max(0, 1f - diff / effect.TrailDist);
			if (brightness <= 0.02f) continue;

			float alpha = brightness * fadeOut * shimmer;
			var p1 = _gridToPixel(_gridOffsetX + seg.X1, _gridOffsetY + seg.Y1);
			var p2 = _gridToPixel(_gridOffsetX + seg.X2, _gridOffsetY + seg.Y2);

			DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.25f), 5f);
			DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.8f), 1.8f);
			if (brightness > 0.6f)
				DrawLine(p1, p2, new Color(1, 1, 1, alpha * 0.5f), 1f);
		}
	}
}
