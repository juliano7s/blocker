using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Draws the grid: cell backgrounds by ground type, grid lines, and block placeholders.
/// Reads simulation GameState — never mutates it.
/// </summary>
public partial class GridRenderer : Node2D
{
    private void DrawWardenZoC()
    {
        foreach (var block in _gameState!.Blocks)
        {
            if (block.Type != BlockType.Warden) continue;
            if (!block.IsFullyRooted || block.IsStunned) continue;

            var playerColor = _config.GetPalette(block.PlayerId).WardenZocColor;
            int zocR = Constants.WardenZocRadius;

            // Wave propagation: expanding ring through cells
            float waveCycleMs = _config.WardenZocWaveCycleMs;
            float cycle = ((float)Time.GetTicksMsec() % waveCycleMs) / waveCycleMs;
            float waveFront = cycle * (zocR + 1); // wave position in cell units

            // Highlight each cell within ZoC range (Chebyshev distance)
            for (int dy = -zocR; dy <= zocR; dy++)
            {
                for (int dx = -zocR; dx <= zocR; dx++)
                {
                    if (dx == 0 && dy == 0) continue;

                    int cx = block.Pos.X + dx;
                    int cy = block.Pos.Y + dy;
                    if (!_gameState!.Grid.InBounds(new GridPos(cx, cy))) continue;

                    float dist = Mathf.Max(Mathf.Abs(dx), Mathf.Abs(dy));

                    // Wave ring: gaussian around the wave front
                    float ringStr = Mathf.Exp(-3f * Mathf.Pow(dist - waveFront, 2));
                    // Trace: cells already passed by wave stay faintly lit
                    float traceStr = dist < waveFront ? 0.03f : 0f;
                    // Distance fade: farther cells are dimmer
                    float distFade = 1f - (dist - 1f) / (zocR + 1);

                    float alpha = (traceStr + 0.10f * ringStr) * distFade;
                    if (alpha < 0.005f) continue;

                    var cellRect = new Rect2(cx * CellSize + GridPadding, cy * CellSize + GridPadding, CellSize, CellSize);
                    QueueGlowCircle(cellRect.GetCenter(), CellSize * 0.35f, playerColor with { A = alpha });
                }
            }
        }
    }

    private void DrawRays()
    {
        // Separate explosion rays from regular rays
        var explosionGroups = new Dictionary<GridPos, List<Ray>>();
        var regularRays = new List<Ray>();

        foreach (var ray in _gameState!.Rays)
        {
            if (ray.IsExplosion)
            {
                if (!explosionGroups.TryGetValue(ray.Origin, out var list))
                {
                    list = new List<Ray>();
                    explosionGroups[ray.Origin] = list;
                }
                list.Add(ray);
            }
            else
            {
                regularRays.Add(ray);
            }
        }

        // Render regular directional rays (stun rays, etc.)
        foreach (var ray in regularRays)
        {
            DrawDirectionalRay(ray);
        }

        // Render explosion rays as radial expanding blasts
        foreach (var (origin, rays) in explosionGroups)
        {
            DrawExplosion(origin, rays);
        }
    }

    private void DrawDirectionalRay(Ray ray)
    {
        Color baseColor = ray.Type == RayType.Stun
            ? _config.StunRayColor
            : _config.BlastRayColor;

        float alpha;
        if (ray.IsExpired)
            alpha = 0.5f * ((float)ray.FadeTicks / Constants.StunRayFade);
        else
            alpha = 0.6f;

        if (alpha <= 0.01f) return;

        var offset = ray.Direction.ToOffset();

        // Stun rays: one-shot expansion like explosion (no repeating pulse)
        // Wavefront at ray.Distance, cells behind it fade to nothing
        // Blast rays: repeating pulse wave
        float pulse;
        const float fadeLength = 2.5f;

        if (ray.Type == RayType.Stun)
            {
                // No repeating cycle — wavefront is exactly where the ray head is
                // Skip origin only if a block sits there (center ray over the stunner)
                // Side rays originate on empty cells beside the stunner, so include i=0
                int start = _gameState!.GetBlockAt(ray.Origin) != null ? 1 : 0;
                for (int i = start; i <= ray.Distance; i++)
            {
                var cellPos = new GridPos(ray.Origin.X + offset.X * i, ray.Origin.Y + offset.Y * i);

                // Age = how far behind the wavefront this cell is
                float age = ray.Distance - i;
                float brightness = Math.Max(0f, 1f - age / fadeLength);

                // White-hot pilot cell at the wavefront
                bool isPilot = i == ray.Distance && !ray.IsExpired;
                float cellAlpha;
                if (isPilot)
                    cellAlpha = brightness * alpha * 1.2f;
                else
                    cellAlpha = brightness * alpha * 0.8f;

                if (cellAlpha < 0.01f) continue;

                Color cellColor = isPilot
                    ? Colors.White with { A = cellAlpha }
                    : baseColor with { A = cellAlpha };

                var cellRect = new Rect2(cellPos.X * CellSize + GridPadding, cellPos.Y * CellSize + GridPadding, CellSize, CellSize);
                DrawRect(cellRect, cellColor);
            }
        }
        else
        {
            // Blast rays: repeating traveling pulse wave (800ms cycle)
            pulse = ((float)Time.GetTicksMsec() % 800f) / 800f;

            for (int i = 0; i <= ray.Distance; i++)
            {
                var cellPos = new GridPos(ray.Origin.X + offset.X * i, ray.Origin.Y + offset.Y * i);

                float distNorm = ray.Distance > 0 ? (float)i / ray.Distance : 0;
                float ringStr = Mathf.Exp(-6f * Mathf.Pow(distNorm - pulse, 2));
                float traceStr = distNorm < pulse ? 0.2f : 0.05f;
                float cellAlpha = alpha * (traceStr + 0.6f * ringStr);

                var cellRect = new Rect2(cellPos.X * CellSize + GridPadding, cellPos.Y * CellSize + GridPadding, CellSize, CellSize);
                DrawRect(cellRect, baseColor with { A = cellAlpha });
            }

            // White-hot pilot cell for blast rays (the lethal head)
            if (!ray.IsExpired)
            {
                var headRect = new Rect2(ray.HeadPos.X * CellSize + GridPadding, ray.HeadPos.Y * CellSize + GridPadding, CellSize, CellSize);
                DrawRect(headRect, Colors.White with { A = alpha * 0.9f });
            }
        }
    }

    /// <summary>
    /// Renders a radial explosion from grouped explosion rays.
    /// Star/asterisk pattern: 4 axis lines + 4 diagonal lines radiating from origin.
    /// Each cell lights up when the wavefront reaches it, then fades to nothing.
    /// </summary>
    private void DrawExplosion(GridPos origin, List<Ray> rays)
    {
        var first = rays[0];
        Color baseColor = first.Type == RayType.Stun
            ? _config.StunRayColor
            : _config.BlastRayColor;

        // Use max distance across all rays as the current expansion radius
        int maxDist = 0;
        int maxRange = first.Range;
        bool allExpired = true;
        int minFade = int.MaxValue;

        foreach (var ray in rays)
        {
            if (ray.Distance > maxDist) maxDist = ray.Distance;
            if (!ray.IsExpired) allExpired = false;
            if (ray.FadeTicks < minFade) minFade = ray.FadeTicks;
        }

        // Fade factor for expired explosions
        float expiredFade = allExpired
            ? (float)minFade / Constants.StunRayFade
            : 1f;

        if (expiredFade <= 0.01f) return;

        // How many "age steps" behind the front before a cell is invisible
        const float fadeLength = 2.5f;

        // Iterate all cells in the bounding box and draw those on the star pattern
        for (int dy = -maxDist; dy <= maxDist; dy++)
        {
            for (int dx = -maxDist; dx <= maxDist; dx++)
            {
                if (dx == 0 && dy == 0) continue;

                // Star pattern: cell is on the explosion if on an axis or diagonal
                if (dx != 0 && dy != 0 && Math.Abs(dx) != Math.Abs(dy)) continue;

                int cx = origin.X + dx;
                int cy = origin.Y + dy;
                if (!_gameState!.Grid.InBounds(new GridPos(cx, cy))) continue;

                float dist = Math.Max(Math.Abs(dx), Math.Abs(dy));

                // Age = how far behind the wavefront this cell is
                float age = maxDist - dist;

                // Brightness: peak when lit (age=0), fades to 0 over fadeLength steps
                float brightness = Math.Max(0f, 1f - age / fadeLength);

                float cellAlpha = brightness * expiredFade * 0.8f;
                if (cellAlpha < 0.01f) continue;

                // White-hot pilot cell at the wavefront
                bool isPilot = dist >= maxDist;
                Color cellColor = isPilot
                    ? Colors.White with { A = cellAlpha * 1.2f }
                    : baseColor with { A = cellAlpha };

                var cellRect = new Rect2(cx * CellSize + GridPadding, cy * CellSize + GridPadding, CellSize, CellSize);
                DrawRect(cellRect, cellColor);
            }
        }

        // Bright origin cell (ground zero)
        float originBrightness = Math.Max(0f, 1f - maxDist / fadeLength);
        float originAlpha = Math.Max(0.1f, originBrightness) * expiredFade * 0.9f;
        if (originAlpha > 0.01f)
        {
            var originRect = new Rect2(origin.X * CellSize + GridPadding, origin.Y * CellSize + GridPadding, CellSize, CellSize);
            DrawRect(originRect, baseColor with { A = originAlpha });
        }
    }

    private void DrawPushWaves()
    {
        foreach (var wave in _gameState!.PushWaves)
        {
            float alpha;
            if (wave.IsExpired)
                alpha = 0.4f * ((float)wave.FadeTicks / Constants.PushWaveFade);
            else
                alpha = 0.4f;

            if (alpha <= 0.01f) continue;

            var waveColor = _config.GetPalette(wave.PlayerId).PushWaveColor with { A = alpha };

            var offset = wave.Direction.ToOffset();

            // Traveling wave pulse (600ms cycle)
            float pulse = ((float)Time.GetTicksMsec() % 600f) / 600f;

            // Draw whole-cell fills with wave pattern + chevron overlay
            for (int i = 0; i <= wave.Distance; i++)
            {
                var cellPos = new GridPos(wave.Origin.X + offset.X * i, wave.Origin.Y + offset.Y * i);
                var center = GridToWorld(cellPos);

                // Wave pattern through cells
                float distNorm = wave.Distance > 0 ? (float)i / wave.Distance : 0;
                float ringStr = Mathf.Exp(-6f * Mathf.Pow(distNorm - pulse, 2));
                float traceStr = distNorm < pulse ? 0.15f : 0.03f;
                float cellAlpha = alpha * (traceStr + 0.5f * ringStr);

                var cellRect = new Rect2(cellPos.X * CellSize + GridPadding, cellPos.Y * CellSize + GridPadding, CellSize, CellSize);
                DrawRect(cellRect, waveColor with { A = cellAlpha });

                // Chevron overlay on brighter cells
                if (ringStr > 0.3f)
                    DrawChevron(center, wave.Direction, waveColor with { A = alpha * 0.8f }, CellSize * 0.22f);
            }
        }
    }

}
