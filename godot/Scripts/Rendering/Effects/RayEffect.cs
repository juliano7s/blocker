using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Rendering.Effects;

public class RayEffect : GpuEffect
{
    public readonly List<(ShaderMaterial Mat, ColorRect Rect)> Layers = new();

    public override void Update()
    {
        float p = Progress;
        float a = Age;
        foreach (var (mat, _) in Layers)
        {
            mat.SetShaderParameter("progress", p);
            mat.SetShaderParameter("age_ms", a);
        }
    }

    public override void Destroy()
    {
        foreach (var (_, rect) in Layers)
            rect.QueueFree();
    }
}
