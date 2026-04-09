namespace Blocker.Game.Rendering.Effects;

/// <summary>
/// Base class for GPU-accelerated visual effects.
/// CPU cost per frame: O(1) — typically 2 shader uniform updates.
/// </summary>
public abstract class GpuEffect
{
    /// <summary>Total duration in milliseconds.</summary>
    public float Duration;

    /// <summary>Elapsed time in milliseconds.</summary>
    public float Age;

    /// <summary>If true, wraps Age back to 0 instead of expiring.</summary>
    public bool Looping;

    /// <summary>Normalized progress 0..1.</summary>
    public float Progress => Math.Clamp(Age / Duration, 0f, 1f);

    /// <summary>Update shader uniforms for the current frame.</summary>
    public abstract void Update();

    /// <summary>Remove all scene-tree nodes owned by this effect.</summary>
    public abstract void Destroy();
}
