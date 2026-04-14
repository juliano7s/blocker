using Blocker.Simulation.Core;
using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.Rendering.Effects;

/// <summary>
/// Creates named visual effect presets. Each method returns a fully configured GpuEffect
/// with Line2D nodes already added to the parent. Shapes come from <see cref="EffectShapes"/>,
/// rendering via <see cref="LineEffect"/> + line_wave.gdshader.
/// </summary>
public static class EffectFactory
{
    private static Shader? _lineWaveShader;
    private static readonly Random Rng = new();

    // Coordinate conversion constants (shared with GridRenderer)
    private const float CellSize = GridRenderer.CellSize;
    private const float GridPadding = GridRenderer.GridPadding;

    /// <summary>Load shaders. Must be called once after scene is ready.</summary>
    public static void Initialize()
    {
        _lineWaveShader = GD.Load<Shader>("res://Assets/Shaders/line_wave.gdshader");
    }

    // ─── Named Effect Presets ───────────────────────────────────────

    /// <summary>
    /// Random-walk lightning expanding outward from all edges.
    /// maxSegs and duration match bible §16.5 segment/duration columns.
    /// trail controls the fade trail behind the wave front (normalized 0..0.5).
    /// </summary>
    public static LineEffect LightningBurst(Node2D parent, GridPos origin, Color color,
        int maxSegs = 56, float duration = 1200f, float trail = 0.15f,
        float contProb = 0.90f, float branchProb = 0.55f)
    {
        var paths = EffectShapes.LightningBurst(origin.X, origin.Y, Rng, maxSegs, contProb, branchProb);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>
    /// Lightning contracting inward — outer segments fade first.
    /// Same parameters as LightningBurst but rendered with contract=true.
    /// </summary>
    public static LineEffect LightningConverge(Node2D parent, GridPos origin, Color color,
        int maxSegs = 28, float duration = 900f, float trail = 0.12f,
        float contProb = 0.90f, float branchProb = 0.55f)
    {
        var paths = EffectShapes.LightningBurst(origin.X, origin.Y, Rng, maxSegs, contProb, branchProb);
        return CreateLineEffect(parent, paths, color, duration, trail: trail,
            contract: true, fadeSpeed: 0f);
    }

    /// <summary>
    /// Lightning rendered in reverse — wave sweeps inward toward origin.
    /// Distinct from LightningConverge (which fades outer segments; this reverses the wave direction).
    /// </summary>
    public static LineEffect ConvergingDrain(Node2D parent, GridPos origin, Color color,
        int maxSegs = 50, float duration = 1000f, float trail = 0.15f,
        float contProb = 0.85f, float branchProb = 0.50f)
    {
        var paths = EffectShapes.LightningBurst(origin.X, origin.Y, Rng, maxSegs, contProb, branchProb);
        return CreateLineEffect(parent, paths, color, duration, trail: trail, reverse: true);
    }

    /// <summary>Lightning from one edge direction (e.g., trail behind movement).</summary>
    public static LineEffect LightningTrail(Node2D parent, GridPos origin, int dx, int dy,
        Color color, float duration = 1200f, int maxSegs = 30, float trail = 0.15f,
        float contProb = 0.90f, float branchProb = 0.55f)
    {
        var paths = EffectShapes.LightningDirectional(origin.X, origin.Y, dx, dy, Rng, maxSegs, contProb, branchProb);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Clockwise spiral outward from center.</summary>
    public static LineEffect SpiralTrace(Node2D parent, GridPos origin, Color color,
        float duration = 1800f, int maxSegs = 40, float trail = 0.15f)
    {
        var paths = EffectShapes.SpiralTrace(origin.X, origin.Y, maxSegs);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Concentric square perimeters expanding outward.</summary>
    public static LineEffect SquareShockwave(Node2D parent, GridPos origin, Color color,
        int maxRadius = 10, float duration = 1000f, float trail = 0.12f)
    {
        var paths = EffectShapes.SquareRings(origin.X, origin.Y, maxRadius);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Short arms contracting inward from all edges.</summary>
    public static LineEffect CrossContract(Node2D parent, GridPos origin, Color color,
        float duration = 600f, int armLen = 3, float trail = 0.15f)
    {
        var paths = EffectShapes.Cross(origin.X, origin.Y, armLen);
        return CreateLineEffect(parent, paths, color, duration, trail: trail,
            contract: true, fadeSpeed: 0f);
    }

    /// <summary>Single cell outline traced clockwise (2 passes).</summary>
    public static LineEffect CellPerimeter(Node2D parent, GridPos origin, Color color,
        float duration = 600f, float trail = 0.25f)
    {
        var paths = EffectShapes.CellPerimeter(origin.X, origin.Y);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>6 jittery random-walk arms from one edge direction.</summary>
    public static LineEffect JitterArms(Node2D parent, GridPos origin, int dx, int dy,
        Color color, float duration = 1000f, int armCount = 6, int armLen = 4,
        float trail = 0.15f)
    {
        var paths = EffectShapes.JitterArms(origin.X, origin.Y, dx, dy, Rng, armCount, armLen);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Straight line from one position to another (e.g., jump trail).</summary>
    public static LineEffect LineTrail(Node2D parent, GridPos from, GridPos to,
        Color color, float duration = 500f, float trail = 0.15f)
    {
        var paths = EffectShapes.LineTrail(from.X, from.Y, to.X, to.Y);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Parallel straight lines in one direction with fading trail.</summary>
    public static LineEffect StraightTracer(Node2D parent, GridPos origin, int dx, int dy,
        Color color, float duration = 800f, int reach = 12, float trail = 0.4f)
    {
        var paths = EffectShapes.StraightTracer(origin.X, origin.Y, dx, dy, reach);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Parallel straight lines in all 4 directions.</summary>
    public static LineEffect StraightTracerAllDirs(Node2D parent, GridPos origin, Color color,
        float duration = 800f, int reach = 12, float trail = 0.4f)
    {
        var paths = EffectShapes.StraightTracerAllDirs(origin.X, origin.Y, reach);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Dashed tendrils crawling outward from edges with right-angle turns.</summary>
    public static LineEffect DashedTendrils(Node2D parent, GridPos origin, Color color,
        float duration = 1600f, int tendrilCount = 8, int minLen = 5, int maxLen = 10,
        float trail = 0.15f)
    {
        var paths = EffectShapes.DashedTendrils(origin.X, origin.Y, Rng, tendrilCount, minLen, maxLen);
        return CreateLineEffect(parent, paths, color, duration, trail: trail, dashed: true);
    }

    /// <summary>4 staggered single-line arms from each edge, delayed sequentially.</summary>
    public static LineEffect StaggeredArms(Node2D parent, GridPos origin, Color color,
        float duration = 800f, int armLen = 3, int stagger = 2, float trail = 0.25f)
    {
        var paths = EffectShapes.StaggeredArms(origin.X, origin.Y, armLen, stagger);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>3 concentric squares around cell center — blink outward on selection.</summary>
    public static LineEffect SelectSquares(Node2D parent, GridPos origin, Color color,
        float duration = 350f, float trail = 0.15f)
    {
        var paths = EffectShapes.SelectSquares(origin.X, origin.Y);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Bezier arc chain — 8 arcs to random nearby targets.</summary>
    public static LineEffect ArcChain(Node2D parent, GridPos origin, Color color,
        float duration = 1200f, int arcCount = 8, int subSegs = 4, float trail = 0.1f)
    {
        var paths = EffectShapes.ArcChain(origin.X, origin.Y, Rng, arcCount, subSegs);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>BFS right-angle random walk — each segment is its own short path.</summary>
    public static LineEffect CircuitTrace(Node2D parent, GridPos origin, Color color,
        float duration = 1400f, int maxSegs = 50, float trail = 0.15f)
    {
        var paths = EffectShapes.CircuitTrace(origin.X, origin.Y, Rng, maxSegs);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>4-directional lines with sine perpendicular displacement and random branches.</summary>
    public static LineEffect WavePulse(Node2D parent, GridPos origin, Color color,
        float duration = 1500f, int reach = 12, float trail = 0.2f)
    {
        var paths = EffectShapes.WavePulse(origin.X, origin.Y, Rng, reach);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>3 parallel lanes in all 4 directions — straight sine-ripple shape.</summary>
    public static LineEffect SineRipple(Node2D parent, GridPos origin, Color color,
        float duration = 2000f, int laneCount = 3, int reach = 10, float trail = 0.2f)
    {
        var paths = EffectShapes.SineRipple(origin.X, origin.Y, laneCount, reach);
        return CreateLineEffect(parent, paths, color, duration, trail: trail);
    }

    /// <summary>Looping ZoC dashed pulse — cardinal radials + diagonal staircases, dashed.</summary>
    public static LineEffect ZocDashedPulse(Node2D parent, GridPos origin, Color color,
        float duration = 2200f, int zocR = 6, float trail = 0.12f)
    {
        var paths = EffectShapes.ZocDashedPulse(origin.X, origin.Y, zocR);
        return CreateLineEffect(parent, paths, color, duration, trail: trail,
            dashed: true, loopMode: true, baseAlpha: 0.10f);
    }

    // ─── Factory Internals ──────────────────────────────────────────

    private static LineEffect CreateLineEffect(
        Node2D parent,
        List<(Vector2[] Points, float DistStart, float DistEnd)> paths,
        Color color, float duration, float trail,
        bool reverse = false, bool dashed = false, bool flicker = false,
        bool contract = false, float fadeSpeed = 0.7f,
        bool loopMode = false, float baseAlpha = 0f)
    {
        var coreMat = MakeLineMat(color, trail, 0.85f, reverse, dashed, flicker, contract, fadeSpeed, loopMode, baseAlpha);
        var glowMat = MakeLineMat(color, trail, 0.12f, reverse, dashed, flicker, contract, fadeSpeed, loopMode, baseAlpha);

        var effect = new LineEffect
        {
            Duration = duration,
            CoreMat = coreMat,
            GlowMat = glowMat,
            Looping = loopMode,
        };

        foreach (var (gridPoints, ds, de) in paths)
        {
            var pixelPoints = PathToPixel(gridPoints);
            effect.CoreLines.Add(MakePathLine(parent, pixelPoints, coreMat, 2.5f, ds, de));
            effect.GlowLines.Add(MakePathLine(parent, pixelPoints, glowMat, 10f, ds, de));
        }

        return effect;
    }

    private static ShaderMaterial MakeLineMat(Color color, float trail, float fadeMult,
        bool reverse = false, bool dashed = false, bool flicker = false,
        bool contract = false, float fadeSpeed = 0.7f,
        bool loopMode = false, float baseAlpha = 0f)
    {
        var mat = new ShaderMaterial { Shader = _lineWaveShader };
        mat.SetShaderParameter("line_color", color);
        mat.SetShaderParameter("trail", trail);
        mat.SetShaderParameter("fade_mult", fadeMult);
        mat.SetShaderParameter("reverse", reverse);
        mat.SetShaderParameter("dashed", dashed);
        mat.SetShaderParameter("flicker", flicker);
        mat.SetShaderParameter("contract", contract);
        mat.SetShaderParameter("fade_speed", fadeSpeed);
        mat.SetShaderParameter("loop_mode", loopMode);
        mat.SetShaderParameter("base_alpha", baseAlpha);
        return mat;
    }

    private static Line2D MakePathLine(Node2D parent, Vector2[] pixelPoints,
        ShaderMaterial mat, float width, float distStart, float distEnd)
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

        // Gradient encodes normalized distance in R channel for the wave shader
        var grad = new Gradient();
        grad.SetColor(0, new Color(distStart, distStart, distStart, 1f));
        grad.SetOffset(0, 0f);
        grad.SetColor(1, new Color(distEnd, distEnd, distEnd, 1f));
        grad.SetOffset(1, 1f);
        line.Gradient = grad;

        parent.AddChild(line);
        return line;
    }

    /// <summary>Convert grid-space coordinates to pixel-space.</summary>
    private static Vector2 GridToPixel(float gx, float gy) =>
        new(gx * CellSize + GridPadding, gy * CellSize + GridPadding);

    private static Vector2[] PathToPixel(Vector2[] gridPoints)
    {
        var result = new Vector2[gridPoints.Length];
        for (int i = 0; i < gridPoints.Length; i++)
            result[i] = GridToPixel(gridPoints[i].X, gridPoints[i].Y);
        return result;
    }
}
