# Menu Revamp Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the plain MainMenu with a grid-integrated menu where the title, buttons, and ambient blocks all live on a drawn grid with animated effects.

**Architecture:** Four new Node2D scripts compose the menu: MenuGrid (background grid), MenuTitle (animated letter cells), MenuButton (block-cluster buttons with hover/click effects), and MenuAmbience (sparse drifting blocks). MainMenu.cs is rewritten to compose them. The existing EffectFactory/LineEffect/line_wave.gdshader pipeline is reused for click effects and ambient explosions. No simulation dependency — everything is purely visual.

**Tech Stack:** Godot 4 + C#, existing line_wave.gdshader effect pipeline, grid_background.gdshader for the background grid.

---

## File Structure

| File | Responsibility |
|------|---------------|
| `godot/Scripts/UI/MenuGrid.cs` | Draws full-screen grid background. Provides `GridToPixel(gx, gy)` coordinate conversion for all menu components. |
| `godot/Scripts/UI/MenuTitle.cs` | Letter cell definitions (ported from TitleRenderer.ts), 4 animation patterns (sweep, radial, cascade, lightning), glow rendering via `_Draw()`. |
| `godot/Scripts/UI/MenuButton.cs` | One button = row of block cells + label. Hover detection via mouse→grid mapping, color transitions, click → EffectFactory effect + delayed navigation. |
| `godot/Scripts/UI/MenuAmbience.cs` | 2-3 blocks drifting on the grid. Timer-based movement + random explosions via EffectFactory. |
| `godot/Scripts/UI/MainMenu.cs` | Rewritten: composes MenuGrid + MenuTitle + MenuButtons + MenuAmbience + EffectLayer. No more VBox/Button nodes. |
| `godot/Scenes/MainMenu.tscn` | Updated scene tree with the new node hierarchy. |

---

### Task 1: MenuGrid — Grid Background

**Files:**
- Create: `godot/Scripts/UI/MenuGrid.cs`

This is the foundation — a full-screen grid that all other menu components draw on top of.

- [x] **Step 1: Create MenuGrid.cs with grid drawing and coordinate helpers**

```csharp
using Godot;

namespace Blocker.Game.UI;

public partial class MenuGrid : Node2D
{
    public const float CellSize = 28f;
    public const float GridLineAlpha = 0.12f;
    private static readonly Color GridLineColor = new(0.267f, 0.667f, 1f, GridLineAlpha); // #4AF at 12%

    private int _cols;
    private int _rows;
    private float _offsetX;
    private float _offsetY;

    public int Cols => _cols;
    public int Rows => _rows;

    public override void _Ready()
    {
        RecalculateGrid();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationResized || what == (int)Control.LayoutPresetMode.MinSize)
            RecalculateGrid();
    }

    private void RecalculateGrid()
    {
        var viewportSize = GetViewportRect().Size;
        _cols = (int)(viewportSize.X / CellSize) + 1;
        _rows = (int)(viewportSize.Y / CellSize) + 1;
        _offsetX = (viewportSize.X - _cols * CellSize) / 2f;
        _offsetY = (viewportSize.Y - _rows * CellSize) / 2f;
        QueueRedraw();
    }

    public Vector2 GridToPixel(float gx, float gy) =>
        new(_offsetX + gx * CellSize, _offsetY + gy * CellSize);

    public (int gx, int gy) PixelToGrid(Vector2 pixel)
    {
        int gx = (int)((pixel.X - _offsetX) / CellSize);
        int gy = (int)((pixel.Y - _offsetY) / CellSize);
        return (gx, gy);
    }

    public override void _Draw()
    {
        // Dark background
        var viewportSize = GetViewportRect().Size;
        DrawRect(new Rect2(0, 0, viewportSize.X, viewportSize.Y), new Color(0.04f, 0.04f, 0.04f));

        // Vertical grid lines
        for (int c = 0; c <= _cols; c++)
        {
            float x = _offsetX + c * CellSize;
            DrawLine(new Vector2(x, 0), new Vector2(x, viewportSize.Y), GridLineColor, 1f);
        }

        // Horizontal grid lines
        for (int r = 0; r <= _rows; r++)
        {
            float y = _offsetY + r * CellSize;
            DrawLine(new Vector2(0, y), new Vector2(viewportSize.X, y), GridLineColor, 1f);
        }
    }
}
```

- [x] **Step 2: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 3: Commit**

```bash
git add godot/Scripts/UI/MenuGrid.cs
git commit -m "feat(menu): add MenuGrid background with coordinate helpers"
```

---

### Task 2: MenuTitle — Animated Title Letters

**Files:**
- Create: `godot/Scripts/UI/MenuTitle.cs`

Port the letter definitions from TitleRenderer.ts and implement 4 animation patterns. Each letter is a 5×7 grid of cells. Animations assign a `dist` value to each edge segment, then a wave front sweeps through them over time.

- [x] **Step 1: Create MenuTitle.cs with letter definitions, edge computation, and layout**

```csharp
using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MenuTitle : Node2D
{
    private const string Word = "blocker";
    private const int LetterH = 7;
    private const int LetterGap = 1;

    private static readonly Color PrimaryColor = new(0.267f, 0.667f, 1f); // #4AF
    private static readonly Color WhiteHot = new(1f, 1f, 1f);

    // 5×7 letter bitmaps (row-major). 1=filled, 0=empty.
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

    private struct Cell { public int Gx, Gy; }
    private struct Edge { public float X1, Y1, X2, Y2; public bool Border; }
    private struct LetterBound { public int Start, End; }

    private Cell[] _cells = Array.Empty<Cell>();
    private Edge[] _edges = Array.Empty<Edge>();
    private LetterBound[] _letterBounds = Array.Empty<LetterBound>();
    private int _totalW, _totalH;

    // Grid coordinate offset — set by MainMenu to position title on screen
    private int _gridOffsetX, _gridOffsetY;
    private Func<float, float, Vector2> _gridToPixel = null!;
    private float _cellSize;

    // Animation state
    private struct TitleEffect
    {
        public (int Idx, float Dist)[] SegDists;
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
            bounds.Add(new LetterBound { Start = ox, End = ox + lw });
            for (int row = 0; row < LetterH; row++)
                for (int col = 0; col < lw; col++)
                    if (letter[row, col] == 1)
                        cells.Add(new Cell { Gx = ox + col, Gy = row });
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
        foreach (var c in _cells) filled.Add(((long)c.Gx << 32) | (uint)c.Gy);

        var edgeSet = new HashSet<long>();
        var edges = new List<Edge>();

        foreach (var cell in _cells)
        {
            int gx = cell.Gx, gy = cell.Gy;
            var cellEdges = new (float x1, float y1, float x2, float y2, int dx, int dy)[]
            {
                (gx, gy, gx + 1, gy, 0, -1),
                (gx + 1, gy, gx + 1, gy + 1, 1, 0),
                (gx, gy + 1, gx + 1, gy + 1, 0, 1),
                (gx, gy, gx, gy + 1, -1, 0),
            };

            foreach (var (x1, y1, x2, y2, dx, dy) in cellEdges)
            {
                int minX = (int)Math.Min(x1, x2), minY = (int)Math.Min(y1, y2);
                int maxX = (int)Math.Max(x1, x2), maxY = (int)Math.Max(y1, y2);
                long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (uint)maxY;
                if (!edgeSet.Add(key)) continue;
                bool border = !filled.Contains(((long)(gx + dx) << 32) | (uint)(gy + dy));
                edges.Add(new Edge { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Border = border });
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

    private void SpawnRandomEffect()
    {
        int pattern = _rng.Next(4);
        TitleEffect effect;

        switch (pattern)
        {
            case 0: // Left→Right sweep
                effect = BuildSweepEffect(false);
                effect.Duration = 1500f + (float)_rng.NextDouble() * 500f;
                effect.TrailDist = 8f;
                break;
            case 1: // Radial burst
                effect = BuildRadialEffect();
                effect.Duration = 1800f + (float)_rng.NextDouble() * 600f;
                effect.TrailDist = 6f;
                break;
            case 2: // Letter cascade
                effect = BuildCascadeEffect();
                effect.Duration = 2000f + (float)_rng.NextDouble() * 800f;
                effect.TrailDist = 4f;
                break;
            default: // Branching lightning
                effect = BuildLightningEffect();
                effect.Duration = 1400f + (float)_rng.NextDouble() * 600f;
                effect.TrailDist = 3f;
                break;
        }

        if (_effects.Count >= MaxEffects) _effects.RemoveAt(0);
        _effects.Add(effect);

        // 40% chance to layer branching lightning on top of sweep/radial/cascade
        if (pattern < 3 && _rng.NextDouble() < 0.4)
        {
            var lightning = BuildLightningEffect();
            lightning.Duration = 1200f + (float)_rng.NextDouble() * 500f;
            lightning.TrailDist = 3f;
            if (_effects.Count >= MaxEffects + 1) _effects.RemoveAt(0);
            _effects.Add(lightning);
        }
    }

    private TitleEffect BuildSweepEffect(bool rightToLeft)
    {
        var segDists = new (int, float)[_edges.Length];
        float maxDist = 0;
        for (int i = 0; i < _edges.Length; i++)
        {
            float midX = (_edges[i].X1 + _edges[i].X2) / 2f;
            float dist = rightToLeft ? _totalW - midX : midX;
            segDists[i] = (i, dist);
            if (dist > maxDist) maxDist = dist;
        }
        return new TitleEffect { SegDists = segDists, MaxDist = maxDist, T = 0, Age = 0 };
    }

    private TitleEffect BuildRadialEffect()
    {
        float cx = _totalW / 2f, cy = _totalH / 2f;
        var segDists = new (int, float)[_edges.Length];
        float maxDist = 0;
        for (int i = 0; i < _edges.Length; i++)
        {
            float mx = (_edges[i].X1 + _edges[i].X2) / 2f;
            float my = (_edges[i].Y1 + _edges[i].Y2) / 2f;
            float dist = MathF.Sqrt((mx - cx) * (mx - cx) + (my - cy) * (my - cy));
            segDists[i] = (i, dist);
            if (dist > maxDist) maxDist = dist;
        }
        return new TitleEffect { SegDists = segDists, MaxDist = maxDist, T = 0, Age = 0 };
    }

    private TitleEffect BuildCascadeEffect()
    {
        var segDists = new (int, float)[_edges.Length];
        float maxDist = 0;
        for (int i = 0; i < _edges.Length; i++)
        {
            float mx = (_edges[i].X1 + _edges[i].X2) / 2f;
            int letterIdx = 0;
            for (int li = 0; li < _letterBounds.Length; li++)
                if (mx >= _letterBounds[li].Start && mx <= _letterBounds[li].End) { letterIdx = li; break; }
            var b = _letterBounds[letterIdx];
            float withinLetter = b.End > b.Start ? (mx - b.Start) / (b.End - b.Start) : 0;
            float dist = letterIdx * 2f + withinLetter;
            segDists[i] = (i, dist);
            if (dist > maxDist) maxDist = dist;
        }
        return new TitleEffect { SegDists = segDists, MaxDist = maxDist, T = 0, Age = 0 };
    }

    private TitleEffect BuildLightningEffect()
    {
        // Pick 3-6 random border edges as seeds, random-walk outward
        var borderIndices = new List<int>();
        for (int i = 0; i < _edges.Length; i++)
            if (_edges[i].Border) borderIndices.Add(i);
        if (borderIndices.Count == 0)
            return new TitleEffect { SegDists = Array.Empty<(int, float)>(), MaxDist = 1, T = 0, Age = 0 };

        var segments = new List<(float X1, float Y1, float X2, float Y2, float Dist)>();
        var visited = new HashSet<long>();
        float maxDist = 0;
        int seedCount = 3 + _rng.Next(4);

        for (int s = 0; s < seedCount; s++)
        {
            var edge = _edges[borderIndices[_rng.Next(borderIndices.Count)]];
            float mx = (edge.X1 + edge.X2) / 2f;
            float my = (edge.Y1 + edge.Y2) / 2f;
            float centerX = _totalW / 2f, centerY = _totalH / 2f;
            bool isHoriz = Math.Abs(edge.Y1 - edge.Y2) < 0.01f;
            int dx, dy;
            if (isHoriz) { dx = 0; dy = my < centerY ? -1 : 1; }
            else { dx = mx < centerX ? -1 : 1; dy = 0; }

            int ix = (int)Math.Round(mx), iy = (int)Math.Round(my);
            var walkers = new List<(int X, int Y, int Dx, int Dy, int Dist, float Prob)>
            {
                (ix, iy, dx, dy, 0, 0.92f)
            };

            while (walkers.Count > 0 && segments.Count < 80)
            {
                int wi = _rng.Next(walkers.Count);
                var w = walkers[wi];
                int nx = w.X + w.Dx, ny = w.Y + w.Dy;

                if (nx < -6 || nx > _totalW + 6 || ny < -6 || ny > _totalH + 6)
                {
                    walkers.RemoveAt(wi);
                    continue;
                }

                int minX = Math.Min(w.X, nx), minY = Math.Min(w.Y, ny);
                int maxX = Math.Max(w.X, nx), maxY = Math.Max(w.Y, ny);
                long key = ((long)minX << 48) | ((long)minY << 32) | ((long)maxX << 16) | (uint)maxY;
                if (!visited.Add(key)) { walkers.RemoveAt(wi); continue; }

                int dist = w.Dist + 1;
                segments.Add((w.X, w.Y, nx, ny, dist));
                if (dist > maxDist) maxDist = dist;

                if (_rng.NextSingle() < w.Prob)
                    walkers[wi] = (nx, ny, w.Dx, w.Dy, dist, w.Prob * 0.82f);
                else
                    walkers.RemoveAt(wi);

                if (_rng.NextSingle() < 0.4f)
                {
                    var (pdx, pdy) = w.Dy == 0
                        ? (0, _rng.NextSingle() < 0.5f ? 1 : -1)
                        : (_rng.NextSingle() < 0.5f ? 1 : -1, 0);
                    walkers.Add((nx, ny, pdx, pdy, dist, w.Prob * 0.5f));
                }
            }
        }

        // Convert lightning segments to edge-index-independent format
        // Lightning segments are separate from the letter edges — they render as additional line segments
        // We'll store them with negative indices to distinguish from letter edges
        var allDists = new List<(int, float)>();
        // Letter edges are not part of lightning — skip them
        // Store lightning segs as extra data
        // Actually, we'll extend _edges temporarily or store lightning separately
        // Simpler: store lightning segments directly in the effect
        // Let's refactor TitleEffect to also carry extra segments
        // For now, return empty — we'll handle lightning drawing separately in _Draw

        // Actually let's keep it simple: we don't need edge indices for lightning.
        // Let's store the raw segments in the effect.
        // We need to refactor TitleEffect slightly.
        // For now return the edge-based version — we'll handle this properly.

        // Map segments to a simple dist array (these are NOT letter edges, they're lightning lines)
        var segArr = new (int Idx, float Dist)[segments.Count];
        for (int i = 0; i < segments.Count; i++)
            segArr[i] = (-(i + 1), segments[i].Dist); // negative index = lightning segment

        return new TitleEffect { SegDists = segArr, MaxDist = maxDist > 0 ? maxDist : 1, T = 0, Age = 0,
            LightningSegs = segments.ToArray() };
    }
```

Wait — this is getting complex. Let me restructure TitleEffect to handle both edge-based animations and lightning segments cleanly.

- [x] **Step 1: Create MenuTitle.cs**

The full file is large, so here's the complete implementation. Key design decisions:
- `TitleEffect` has two modes: edge-based (sweep/radial/cascade assign dist to letter edges) and lightning (separate segments generated by random walk)
- `_Draw()` renders: (1) subtle cell fills, (2) dim base grid lines on letter edges, (3) animated effects as multi-pass glow lines
- Coordinate conversion uses the `_gridToPixel` delegate from MenuGrid

```csharp
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
        public float[] EdgeDists;        // per-edge dist (null for lightning-only)
        public LightningSeg[] LightningSegs; // extra lightning segments (null for edge-only)
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
            EdgeDists = dists, LightningSegs = null!, MaxDist = maxDist,
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
            EdgeDists = dists, LightningSegs = null!, MaxDist = maxDist,
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
            EdgeDists = dists, LightningSegs = null!, MaxDist = maxDist,
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
            EdgeDists = null!, LightningSegs = segs.ToArray(), MaxDist = maxDist > 0 ? maxDist : 1,
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
            float diff = wavePos - effect.EdgeDists[i];
            float brightness = diff < 0 ? 0 : Math.Max(0, 1f - diff / effect.TrailDist);
            if (brightness <= 0.02f) continue;

            float alpha = brightness * fadeOut * shimmer;
            var e = _edges[i];
            var p1 = _gridToPixel(_gridOffsetX + e.X1, _gridOffsetY + e.Y1);
            var p2 = _gridToPixel(_gridOffsetX + e.X2, _gridOffsetY + e.Y2);

            // Outer glow
            DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.25f), 5f);
            // Core
            DrawLine(p1, p2, new Color(PrimaryColor.R, PrimaryColor.G, PrimaryColor.B, alpha * 0.8f), 1.8f);
            // White-hot tip
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

        foreach (var seg in effect.LightningSegs)
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
```

- [x] **Step 2: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 3: Commit**

```bash
git add godot/Scripts/UI/MenuTitle.cs
git commit -m "feat(menu): add MenuTitle with letter grid and 4 animation patterns"
```

---

### Task 3: MenuButton — Grid-Integrated Buttons

**Files:**
- Create: `godot/Scripts/UI/MenuButton.cs`

Each button is a row of 2-3 block cells + a label. Hover shifts color to orange, click triggers a LightningBurst effect and navigates after a delay.

- [x] **Step 1: Create MenuButton.cs**

```csharp
using Godot;
using System;

namespace Blocker.Game.UI;

public partial class MenuButton : Node2D
{
    private static readonly Color IdleColor = new(0.267f, 0.667f, 1f);    // #4AF
    private static readonly Color HoverColor = new(1f, 0.416f, 0.2f);     // #FF6A33
    private static readonly Color IdleTextColor = new(0.267f, 0.667f, 1f, 0.8f);
    private static readonly Color HoverTextColor = new(1f, 0.416f, 0.2f, 1f);

    private const int BlockCount = 3;
    private static readonly float[] BlockAlphas = { 0.7f, 0.5f, 0.3f };

    private string _label = "";
    private Action? _onActivated;
    private int _gridX, _gridY;
    private float _cellSize;
    private Func<float, float, Vector2>? _gridToPixel;

    // Hover state
    private bool _hovered;
    private float _hoverT; // 0..1 interpolation for smooth transition

    // Click state
    private bool _clicked;
    private float _clickTimer;
    private const float ClickDelay = 400f; // ms to show effect before navigating

    // Hit area in pixel space (computed once)
    private Rect2 _hitRect;

    [Signal]
    public delegate void ClickedEventHandler();

    public void Initialize(string label, int gridX, int gridY, float cellSize,
        Func<float, float, Vector2> gridToPixel, Action onActivated)
    {
        _label = label;
        _gridX = gridX;
        _gridY = gridY;
        _cellSize = cellSize;
        _gridToPixel = gridToPixel;
        _onActivated = onActivated;

        // Compute hit rect: block cells + label area
        var topLeft = gridToPixel(gridX, gridY);
        float labelWidth = _label.Length * 10f + 40f; // rough estimate
        _hitRect = new Rect2(topLeft.X - 4, topLeft.Y - 4,
            BlockCount * cellSize + labelWidth + 8, cellSize + 8);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * 1000f;

        // Smooth hover interpolation
        float target = _hovered ? 1f : 0f;
        _hoverT = Mathf.MoveToward(_hoverT, target, dt / 150f); // 150ms transition

        if (_clicked)
        {
            _clickTimer -= dt;
            if (_clickTimer <= 0)
            {
                _clicked = false;
                _onActivated?.Invoke();
            }
        }

        QueueRedraw();
    }

    public override void _Input(InputEvent @event)
    {
        if (_gridToPixel == null || _clicked) return;

        if (@event is InputEventMouseMotion motion)
        {
            _hovered = _hitRect.HasPoint(motion.Position);
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            if (_hovered && !_clicked)
            {
                _clicked = true;
                _clickTimer = ClickDelay;
                EmitSignal(SignalName.Clicked);
            }
        }
    }

    public Vector2 GetGridCenter()
    {
        if (_gridToPixel == null) return Vector2.Zero;
        return _gridToPixel(_gridX + 1, _gridY);
    }

    public (int X, int Y) GridPosition => (_gridX + 1, _gridY);

    public override void _Draw()
    {
        if (_gridToPixel == null) return;

        Color blockColor = IdleColor.Lerp(HoverColor, _hoverT);
        Color textColor = IdleTextColor.Lerp(HoverTextColor, _hoverT);

        // Draw block cells
        for (int i = 0; i < BlockCount; i++)
        {
            var pos = _gridToPixel(_gridX + i, _gridY);
            float alpha = BlockAlphas[i] + _hoverT * 0.2f;
            var color = new Color(blockColor.R, blockColor.G, blockColor.B, alpha);
            DrawRect(new Rect2(pos.X + 1, pos.Y + 1, _cellSize - 2, _cellSize - 2), color);

            // Glow on hover
            if (_hoverT > 0.01f)
            {
                float glowAlpha = _hoverT * 0.15f * BlockAlphas[i];
                var glowColor = new Color(blockColor.R, blockColor.G, blockColor.B, glowAlpha);
                DrawRect(new Rect2(pos.X - 2, pos.Y - 2, _cellSize + 4, _cellSize + 4), glowColor);
            }
        }

        // Draw label text
        var labelPos = _gridToPixel(_gridX + BlockCount, _gridY);
        var font = ThemeDB.FallbackFont;
        int fontSize = 14;
        DrawString(font, new Vector2(labelPos.X + 8, labelPos.Y + _cellSize * 0.65f),
            _label, HorizontalAlignment.Left, -1, fontSize, textColor);
    }
}
```

- [x] **Step 2: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 3: Commit**

```bash
git add godot/Scripts/UI/MenuButton.cs
git commit -m "feat(menu): add MenuButton with grid cells, hover, and click effects"
```

---

### Task 4: MenuAmbience — Sparse Ambient Blocks

**Files:**
- Create: `godot/Scripts/UI/MenuAmbience.cs`

2-3 blocks that drift slowly, occasionally explode with a random EffectFactory effect, then respawn.

- [x] **Step 1: Create MenuAmbience.cs**

```csharp
using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Core;
using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MenuAmbience : Node2D
{
    private static readonly Color BlockColor = new(0.267f, 0.667f, 1f, 0.5f); // #4AF at 50%
    private static readonly Color EffectColor = new(1f, 0.667f, 0.2f);        // amber #FFAA33

    private const int MaxBlocks = 3;
    private const float MoveIntervalMs = 1500f; // move every 1.5s
    private const float ExplosionMinMs = 5000f;
    private const float ExplosionMaxMs = 10000f;
    private const float RespawnDelayMs = 2000f;

    private record struct AmbientBlock(int Gx, int Gy, float MoveTimer, float ExplodeTimer, bool Alive, float RespawnTimer);

    private readonly List<AmbientBlock> _blocks = new();
    private readonly Random _rng = new();
    private int _gridCols, _gridRows;
    private float _cellSize;
    private Func<float, float, Vector2>? _gridToPixel;
    private Node2D? _effectLayer;

    // Track GPU effects spawned by ambience
    private readonly List<GpuEffect> _effects = new();

    public void Initialize(int gridCols, int gridRows, float cellSize,
        Func<float, float, Vector2> gridToPixel, Node2D effectLayer)
    {
        _gridCols = gridCols;
        _gridRows = gridRows;
        _cellSize = cellSize;
        _gridToPixel = gridToPixel;
        _effectLayer = effectLayer;

        EffectFactory.Initialize();

        for (int i = 0; i < MaxBlocks; i++)
            _blocks.Add(SpawnBlock());
    }

    private AmbientBlock SpawnBlock()
    {
        // Spawn at random edge position
        int side = _rng.Next(4);
        int gx, gy;
        switch (side)
        {
            case 0: gx = 0; gy = _rng.Next(_gridRows); break;
            case 1: gx = _gridCols - 1; gy = _rng.Next(_gridRows); break;
            case 2: gx = _rng.Next(_gridCols); gy = 0; break;
            default: gx = _rng.Next(_gridCols); gy = _gridRows - 1; break;
        }
        float explodeTimer = ExplosionMinMs + (float)_rng.NextDouble() * (ExplosionMaxMs - ExplosionMinMs);
        return new AmbientBlock(gx, gy, MoveIntervalMs, explodeTimer, true, 0);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * 1000f;

        for (int i = 0; i < _blocks.Count; i++)
        {
            var b = _blocks[i];

            if (!b.Alive)
            {
                b.RespawnTimer -= dt;
                if (b.RespawnTimer <= 0)
                    b = SpawnBlock();
                _blocks[i] = b;
                continue;
            }

            // Movement
            b.MoveTimer -= dt;
            if (b.MoveTimer <= 0)
            {
                b.MoveTimer = MoveIntervalMs + (float)_rng.NextDouble() * 500f;
                int dir = _rng.Next(4);
                int dx = dir switch { 0 => 1, 1 => -1, _ => 0 };
                int dy = dir switch { 2 => 1, 3 => -1, _ => 0 };
                int nx = Math.Clamp(b.Gx + dx, 1, _gridCols - 2);
                int ny = Math.Clamp(b.Gy + dy, 1, _gridRows - 2);
                b = b with { Gx = nx, Gy = ny };
            }

            // Explosion timer
            b.ExplodeTimer -= dt;
            if (b.ExplodeTimer <= 0)
            {
                Explode(b.Gx, b.Gy);
                b = b with { Alive = false, RespawnTimer = RespawnDelayMs };
            }

            _blocks[i] = b;
        }

        // Update GPU effects
        for (int i = _effects.Count - 1; i >= 0; i--)
        {
            var effect = _effects[i];
            effect.Age += dt;
            effect.Update();
            if (effect.Progress >= 1f && !effect.Looping)
            {
                effect.Destroy();
                _effects.RemoveAt(i);
            }
        }

        QueueRedraw();
    }

    private void Explode(int gx, int gy)
    {
        if (_effectLayer == null) return;

        // Pick a random effect type
        var pos = new GridPos(gx, gy);
        int effectType = _rng.Next(5);
        LineEffect effect = effectType switch
        {
            0 => EffectFactory.LightningBurst(_effectLayer, pos, EffectColor, maxSegs: 30, duration: 1000f),
            1 => EffectFactory.SpiralTrace(_effectLayer, pos, EffectColor, duration: 1200f, maxSegs: 25),
            2 => EffectFactory.SquareShockwave(_effectLayer, pos, EffectColor, maxRadius: 5, duration: 800f),
            3 => EffectFactory.StaggeredArms(_effectLayer, pos, EffectColor, duration: 700f),
            _ => EffectFactory.DashedTendrils(_effectLayer, pos, EffectColor, duration: 1000f, tendrilCount: 5),
        };
        _effects.Add(effect);
    }

    public override void _Draw()
    {
        if (_gridToPixel == null) return;

        foreach (var b in _blocks)
        {
            if (!b.Alive) continue;
            var pos = _gridToPixel(b.Gx, b.Gy);
            DrawRect(new Rect2(pos.X + 2, pos.Y + 2, _cellSize - 4, _cellSize - 4), BlockColor);
            // Subtle glow
            DrawRect(new Rect2(pos.X - 1, pos.Y - 1, _cellSize + 2, _cellSize + 2),
                new Color(BlockColor.R, BlockColor.G, BlockColor.B, 0.1f));
        }
    }
}
```

**Important note about EffectFactory coordinate system:** EffectFactory uses `GridRenderer.CellSize` and `GridRenderer.GridPadding` for pixel conversion. The menu grid uses different offsets. We need to make EffectFactory work with the menu's coordinate system. The simplest fix: position the EffectLayer node so its origin aligns with the menu grid's origin, and set a scaling factor. However, since EffectFactory.PathToPixel uses `GridRenderer.GridPadding` (=140px), we need to offset the EffectLayer by `-(GridPadding - menuOffset)`.

Actually, looking more closely at EffectFactory, it hardcodes `GridToPixel` using `GridRenderer.CellSize` and `GridRenderer.GridPadding`. Since MenuGrid uses CellSize=28 (same) but a different offset, we need to position the EffectLayer node such that `Position = menuGridOffset - GridPadding` to compensate. This way EffectFactory's pixel coordinates align with the menu's grid.

Add to the step: the EffectLayer's position must be set in MainMenu.cs to `new Vector2(menuGrid._offsetX - GridRenderer.GridPadding, menuGrid._offsetY - GridRenderer.GridPadding)`.

- [x] **Step 2: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 3: Commit**

```bash
git add godot/Scripts/UI/MenuAmbience.cs
git commit -m "feat(menu): add MenuAmbience with drifting blocks and random explosions"
```

---

### Task 5: Rewrite MainMenu — Compose Everything

**Files:**
- Modify: `godot/Scripts/UI/MainMenu.cs`
- Modify: `godot/Scenes/MainMenu.tscn`

Rewrite MainMenu to compose MenuGrid, MenuTitle, MenuButtons, MenuAmbience, and EffectLayer. Center the title and buttons on the grid.

- [x] **Step 1: Rewrite MainMenu.cs**

```csharp
using Blocker.Game.Maps;
using Blocker.Game.Rendering;
using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MainMenu : Control
{
    private static readonly Color EffectColor = new(1f, 0.667f, 0.2f); // amber #FFAA33

    private MenuGrid _menuGrid = null!;
    private MenuTitle _menuTitle = null!;
    private MenuAmbience _menuAmbience = null!;
    private Node2D _effectLayer = null!;
    private readonly List<MenuButton> _buttons = new();
    private readonly List<GpuEffect> _clickEffects = new();

    public override void _Ready()
    {
        // Create scene tree
        _menuGrid = new MenuGrid { Name = "MenuGrid" };
        AddChild(_menuGrid);

        _effectLayer = new Node2D { Name = "EffectLayer" };
        AddChild(_effectLayer);

        _menuTitle = new MenuTitle { Name = "MenuTitle" };
        AddChild(_menuTitle);

        // Initialize grid first to get coordinate system
        // Wait one frame for grid to compute layout
        CallDeferred(MethodName.InitializeComponents);
    }

    private void InitializeComponents()
    {
        float cellSize = MenuGrid.CellSize;
        int cols = _menuGrid.Cols;
        int rows = _menuGrid.Rows;

        // Position EffectLayer to align EffectFactory's coordinate system with menu grid
        float menuOffsetX = (GetViewportRect().Size.X - cols * cellSize) / 2f;
        float menuOffsetY = (GetViewportRect().Size.Y - rows * cellSize) / 2f;
        _effectLayer.Position = new Vector2(
            menuOffsetX - GridRenderer.GridPadding,
            menuOffsetY - GridRenderer.GridPadding);

        EffectFactory.Initialize();

        // Title: center horizontally, place near top (~row 3)
        int titleRow = 3;
        _menuTitle.Initialize(0, 0, cellSize, _menuGrid.GridToPixel);
        int titleGridWidth = _menuTitle.TotalWidth;
        int titleOffsetX = (cols - titleGridWidth) / 2;
        _menuTitle.Initialize(titleOffsetX, titleRow, cellSize, _menuGrid.GridToPixel);

        // Buttons: center below title
        int buttonStartRow = titleRow + _menuTitle.TotalHeight + 3;
        var buttonDefs = new (string Label, System.Action Action)[]
        {
            ("PLAY TEST", OnPlayTestPressed),
            ("PLAY VS AI", OnPlayVsAiPressed),
            ("PLAY MULTIPLAYER", OnPlayMultiplayerPressed),
            ("MAP EDITOR", OnMapEditorPressed),
            ("EXIT GAME", OnExitPressed),
        };

        int buttonBlockWidth = 3; // each button has 3 block cells
        int buttonGridX = (cols - buttonBlockWidth) / 2 - 2; // offset left to center with label

        for (int i = 0; i < buttonDefs.Length; i++)
        {
            var (label, action) = buttonDefs[i];
            var btn = new MenuButton { Name = $"MenuButton_{label.Replace(" ", "")}" };
            AddChild(btn);
            btn.Initialize(label, buttonGridX, buttonStartRow + i * 2, cellSize,
                _menuGrid.GridToPixel, action);

            btn.Clicked += () => OnButtonClicked(btn);
            _buttons.Add(btn);
        }

        // Ambience
        _menuAmbience = new MenuAmbience { Name = "MenuAmbience" };
        AddChild(_menuAmbience);
        _menuAmbience.Initialize(cols, rows, cellSize, _menuGrid.GridToPixel, _effectLayer);
    }

    public override void _Process(double delta)
    {
        float dt = (float)delta * 1000f;
        for (int i = _clickEffects.Count - 1; i >= 0; i--)
        {
            var effect = _clickEffects[i];
            effect.Age += dt;
            effect.Update();
            if (effect.Progress >= 1f)
            {
                effect.Destroy();
                _clickEffects.RemoveAt(i);
            }
        }
    }

    private void OnButtonClicked(MenuButton btn)
    {
        var (gx, gy) = btn.GridPosition;
        var effect = EffectFactory.LightningBurst(_effectLayer, new GridPos(gx, gy),
            EffectColor, maxSegs: 25, duration: 500f);
        _clickEffects.Add(effect);
    }

    private void OnPlayTestPressed()
    {
        var data = MapFileManager.Load("overload-test.json");
        if (data == null)
        {
            GD.PrintErr("Failed to load overload-test.json for Play Test");
            return;
        }
        var assignments = new List<SlotAssignment>();
        for (int i = 0; i < data.SlotCount; i++)
            assignments.Add(new SlotAssignment(i, i));
        GameLaunchData.MapData = data;
        GameLaunchData.Assignments = assignments;
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    private void OnPlayVsAiPressed() =>
        GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");

    private void OnPlayMultiplayerPressed() =>
        GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");

    private void OnMapEditorPressed() =>
        GetTree().ChangeSceneToFile("res://Scenes/MapEditor.tscn");

    private void OnExitPressed() =>
        GetTree().Quit();
}
```

- [x] **Step 2: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 3: Commit**

```bash
git add godot/Scripts/UI/MainMenu.cs
git commit -m "feat(menu): rewrite MainMenu to compose grid-integrated components"
```

---

### Task 6: Make MenuGrid Offset Accessible + Fix EffectLayer Alignment

**Files:**
- Modify: `godot/Scripts/UI/MenuGrid.cs`

MainMenu needs access to the grid's pixel offset to position the EffectLayer. Expose `OffsetX` and `OffsetY` as public properties.

- [x] **Step 1: Add public offset properties to MenuGrid**

Add after the `_offsetY` field declaration:

```csharp
public float OffsetX => _offsetX;
public float OffsetY => _offsetY;
```

- [x] **Step 2: Update MainMenu to use MenuGrid.OffsetX/OffsetY**

In `MainMenu.InitializeComponents()`, replace the manual offset calculation:

```csharp
// Replace:
float menuOffsetX = (GetViewportRect().Size.X - cols * cellSize) / 2f;
float menuOffsetY = (GetViewportRect().Size.Y - rows * cellSize) / 2f;

// With:
float menuOffsetX = _menuGrid.OffsetX;
float menuOffsetY = _menuGrid.OffsetY;
```

- [x] **Step 3: Verify it builds**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [x] **Step 4: Commit**

```bash
git add godot/Scripts/UI/MenuGrid.cs godot/Scripts/UI/MainMenu.cs
git commit -m "fix(menu): expose grid offset and use it for EffectLayer alignment"
```

---

### Task 7: Visual Testing and Polish

**Files:**
- Possibly modify: any of the above files based on testing

- [x] **Step 1: Open Godot and run the MainMenu scene**

Open the project in Godot (`C:/Program Files/Godot`), run the MainMenu scene. Verify:
- Grid background draws correctly
- Title "blocker" appears as grid cells, centered
- Title animations cycle (wait 2-5s between each)
- Buttons render as block clusters, centered
- Hover changes button color to orange smoothly
- Click triggers lightning effect, then navigates after ~400ms
- Ambient blocks drift and occasionally explode
- No errors in output

- [x] **Step 2: Fix any visual issues found during testing**

Common issues to watch for:
- Title position off-center → adjust `titleOffsetX` calculation
- Buttons too close/far from title → adjust `buttonStartRow` spacing
- EffectFactory effects misaligned → check EffectLayer position offset
- Font too small/large → adjust fontSize in MenuButton
- Hover detection area wrong → adjust `_hitRect` calculation in MenuButton

- [x] **Step 3: Commit fixes**

```bash
git add -A
git commit -m "fix(menu): visual polish from testing"
```

---

### Task 8: Update Docs

**Files:**
- Modify: `docs/architecture.md` (add menu section)
- Modify: `docs/ADR.md` (log the decision)
- Move: `docs/superpowers/specs/2026-04-26-menu-revamp-design.md` → `docs/archive/`

- [x] **Step 1: Add menu section to architecture.md**

Add a section describing the menu's Node2D-based architecture, the 4 components, and how they compose.

- [x] **Step 2: Log ADR entry**

Append to `docs/ADR.md`:
```
### ADR-XXX: Grid-Integrated Main Menu (2026-04-26)
**Decision:** Replace plain Button-based menu with custom Node2D components drawing directly on a grid.
**Reason:** The menu should showcase the game's grid aesthetic. Standard UI controls don't support per-cell hover effects or grid-line animations.
**Trade-off:** More code to maintain vs. using Godot's built-in UI system, but the visual payoff is significant for first impressions.
```

- [x] **Step 3: Archive the spec**

```bash
mv docs/superpowers/specs/2026-04-26-menu-revamp-design.md docs/archive/
```

- [x] **Step 4: Commit**

```bash
git add docs/
git commit -m "docs: add menu architecture, ADR, archive spec"
```
