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
