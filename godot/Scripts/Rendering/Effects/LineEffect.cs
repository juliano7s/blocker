using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Rendering.Effects;

/// <summary>
/// Line2D-based path effect with wave animation shader.
/// CPU sets points + gradient once at spawn. Shader animates the traveling wave.
/// Per-frame CPU cost: 2 float uniform updates.
///
/// Each effect creates two rendering layers sharing one ShaderMaterial:
///   - Core (narrow, bright) — the visible line
///   - Glow (wide, dim) — the soft bloom halo
/// </summary>
public class LineEffect : GpuEffect
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
