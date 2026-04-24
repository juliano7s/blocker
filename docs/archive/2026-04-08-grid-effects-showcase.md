# Grid Effects Showcase Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a standalone Godot test scene with 10 different grid-line-based visual effects triggered by buttons, for visual exploration and comparison.

**Architecture:** Single scene (`EffectShowcase.tscn`) with one script (`EffectShowcase.cs`). Draws a 30x30 grid and renders effects via `_Draw()` using multi-pass glow rendering (outer bloom, inner bloom, core, white tips, sparks). A `GlowLayer` child node handles additive blending. Button panel on the left triggers effects at center cell (15,15).

**Tech Stack:** Godot 4.6 C#, `_Draw()` API, `CanvasItemMaterial` additive blend

---

### Task 1: Scene file and grid rendering

**Files:**
- Create: `godot/Scenes/EffectShowcase.tscn`
- Create: `godot/Scripts/Showcase/EffectShowcase.cs`

- [ ] **Step 1: Create the script with grid drawing**

Create `godot/Scripts/Showcase/EffectShowcase.cs`:

```csharp
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
    }

    private static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    // --- Placeholder spawn methods (implemented in later tasks) ---
    private void SpawnElectricLightning() { }
    private void SpawnWavePulse() { }
    private void SpawnGhostFlicker() { }
    private void SpawnDigitalCascade() { }
    private void SpawnSpiralTrace() { }
    private void SpawnCircuitTrace() { }
    private void SpawnShockwaveRing() { }
    private void SpawnJitterBurst() { }
    private void SpawnConvergingDrain() { }
    private void SpawnArcChain() { }

    // --- Rendering (filled in Task 2) ---
    private void DrawAllEffects() { }

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

    internal void DrawGlowPass() { }
}
```

- [ ] **Step 2: Create the scene file**

Create `godot/Scenes/EffectShowcase.tscn`:

```
[gd_scene format=3]

[ext_resource type="Script" path="res://Scripts/Showcase/EffectShowcase.cs" id="1_showcase"]

[node name="EffectShowcase" type="Node2D"]
script = ExtResource("1_showcase")
```

- [ ] **Step 3: Build and verify the scene loads**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds.

Then open Godot, run EffectShowcase.tscn — should show a 30x30 dark grid with a highlighted center cell and buttons on the left (buttons do nothing yet).

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs godot/Scenes/EffectShowcase.tscn
git commit -m "feat: add EffectShowcase scene with grid and button panel"
```

---

### Task 2: Multi-pass effect rendering

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

Implement the core rendering that all 10 effects share: the multi-pass glow drawing and spark rendering.

- [ ] **Step 1: Implement DrawAllEffects and DrawGlowPass**

Replace the empty `DrawAllEffects()` and `DrawGlowPass()` methods in `EffectShowcase.cs`:

```csharp
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

        // Pass 1a: outer bloom — wide, faint
        foreach (var v in visible)
        {
            var from = new Vector2(v.Px1, v.Py1);
            var to = new Vector2(v.Px2, v.Py2);
            DrawLine(from, to, color with { A = avgBa * 0.06f }, 9f);
        }

        // Pass 1b: inner bloom
        foreach (var v in visible)
        {
            var from = new Vector2(v.Px1, v.Py1);
            var to = new Vector2(v.Px2, v.Py2);
            DrawLine(from, to, color with { A = avgBa * 0.15f }, 5f);
        }
    }

    private void DrawEffectCore(GridEffect e)
    {
        var visible = ComputeVisibleSegments(e);
        if (visible.Count == 0) return;

        var color = e.Color;
        float shimmer = 0.8f + 0.2f * MathF.Sin(e.Age * 0.006f);

        // Pass 2: colored core
        foreach (var v in visible)
        {
            var from = new Vector2(v.Px1, v.Py1);
            var to = new Vector2(v.Px2, v.Py2);
            DrawLine(from, to, color with { A = v.Ba * 0.8f * shimmer }, 1.8f);
        }

        // Pass 3: white-hot tips
        foreach (var v in visible)
        {
            if (v.Brightness > 0.55f)
            {
                var tipAlpha = ((v.Brightness - 0.55f) / 0.45f) * v.Ba * 0.6f;
                var from = new Vector2(v.Px1, v.Py1);
                var to = new Vector2(v.Px2, v.Py2);
                DrawLine(from, to, new Color(1f, 1f, 1f, tipAlpha * shimmer), 1f);
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
```

- [ ] **Step 2: Add helper method for adding effects**

Add to `EffectShowcase.cs`:

```csharp
    private void AddEffect(GridEffect effect)
    {
        if (_effects.Count >= 50) _effects.RemoveAt(0);
        _effects.Add(effect);
    }
```

- [ ] **Step 3: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds. (No visual change yet — no effects are spawned.)

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add multi-pass effect rendering (bloom, core, tips, sparks)"
```

---

### Task 3: Electric Lightning effect

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

Port the branching random-walk lightning from the TS prototype.

- [ ] **Step 1: Implement BuildLightning and SpawnElectricLightning**

Replace the empty `SpawnElectricLightning()` and add the `BuildLightning` helper:

```csharp
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
```

- [ ] **Step 2: Build, run scene, click "1. Electric Lightning"**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds. Run scene in Godot — clicking button shows branching lightning emanating from center cell with glow.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Electric Lightning effect (branching random walk)"
```

---

### Task 4: Wave Pulse effect

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

- [ ] **Step 1: Implement SpawnWavePulse**

Replace the empty `SpawnWavePulse()`:

```csharp
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
```

- [ ] **Step 2: Build, test button "2. Wave Pulse"**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Wavy lines radiate from center with perpendicular displacement.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Wave Pulse effect (sine-displaced grid lines)"
```

---

### Task 5: Ghost Flicker effect

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

- [ ] **Step 1: Implement SpawnGhostFlicker**

Replace the empty `SpawnGhostFlicker()`:

```csharp
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
```

- [ ] **Step 2: Build, test button "3. Ghost Flicker"**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Grid segments around center flicker in and out randomly with a ghostly violet glow.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Ghost Flicker effect (strobing grid segments)"
```

---

### Task 6: Digital Cascade effect

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

- [ ] **Step 1: Implement SpawnDigitalCascade**

Replace the empty `SpawnDigitalCascade()`:

```csharp
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
```

- [ ] **Step 2: Build, test button "4. Digital Cascade"**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Vertical lines cascade downward with horizontal branches. Matrix-esque.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Digital Cascade effect (matrix rain on grid lines)"
```

---

### Task 7: Spiral Trace + Circuit Trace + Shockwave Ring effects

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

Three precise grid-snapped effects grouped together since they're simpler geometry builders.

- [ ] **Step 1: Implement SpawnSpiralTrace**

Replace empty `SpawnSpiralTrace()`:

```csharp
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
```

- [ ] **Step 2: Implement SpawnCircuitTrace**

Replace empty `SpawnCircuitTrace()`:

```csharp
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
```

- [ ] **Step 3: Implement SpawnShockwaveRing**

Replace empty `SpawnShockwaveRing()`:

```csharp
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
```

- [ ] **Step 4: Build, test buttons 5, 6, 7**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Spiral Trace shows clockwise expanding spiral. Circuit Trace shows branching right-angle paths. Shockwave Ring shows expanding square rings.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Spiral Trace, Circuit Trace, Shockwave Ring effects"
```

---

### Task 8: Jitter Burst + Converging Drain + Arc Chain effects

**Files:**
- Modify: `godot/Scripts/Showcase/EffectShowcase.cs`

Three wild/off-grid effects grouped together.

- [ ] **Step 1: Implement SpawnJitterBurst**

Replace empty `SpawnJitterBurst()`:

```csharp
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
```

- [ ] **Step 2: Implement SpawnConvergingDrain**

Replace empty `SpawnConvergingDrain()`:

```csharp
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
```

- [ ] **Step 3: Implement SpawnArcChain**

Replace empty `SpawnArcChain()`:

```csharp
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
```

- [ ] **Step 4: Build, test buttons 8, 9, 10**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Jitter Burst shows erratic red zigzags. Converging Drain shows purple lightning converging inward. Arc Chain shows yellow arcing chains between random points.

- [ ] **Step 5: Test "ALL" button**

Click ALL button — all 10 effects fire simultaneously from center. Visual feast.

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Showcase/EffectShowcase.cs
git commit -m "feat: add Jitter Burst, Converging Drain, Arc Chain effects"
```
