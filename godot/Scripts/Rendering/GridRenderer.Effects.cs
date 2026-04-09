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

                    var cellRect = new Rect2(cx * CellSize, cy * CellSize, CellSize, CellSize);
                    QueueGlowCircle(cellRect.GetCenter(), CellSize * 0.35f, playerColor with { A = alpha });
                }
            }
        }
    }

    private void DrawRays()
    {
        foreach (var ray in _gameState!.Rays)
        {
            // Blue tint for stun, orange for blast
            Color baseColor = ray.Type == RayType.Stun
                ? _config.StunRayColor
                : _config.BlastRayColor;

            float alpha;
            if (ray.IsExpired)
            {
                alpha = 0.5f * ((float)ray.FadeTicks / Constants.StunRayFade);
            }
            else
            {
                alpha = 0.6f;
            }

            if (alpha <= 0.01f) continue;

            var offset = ray.Direction.ToOffset();

            // Traveling pulse wave (800ms cycle)
            float pulse = ((float)Time.GetTicksMsec() % 800f) / 800f;

            // Draw whole-cell fills along ray path with wave pattern
            for (int i = 0; i <= ray.Distance; i++)
            {
                var cellPos = new GridPos(ray.Origin.X + offset.X * i, ray.Origin.Y + offset.Y * i);

                // Wave pattern: gaussian ring traveling outward
                float distNorm = ray.Distance > 0 ? (float)i / ray.Distance : 0;
                float ringStr = Mathf.Exp(-6f * Mathf.Pow(distNorm - pulse, 2));
                // Trace behind the wave: cells already passed stay lit at base level
                float traceStr = distNorm < pulse ? 0.2f : 0.05f;
                float cellAlpha = alpha * (traceStr + 0.6f * ringStr);

                // Fill whole cell
                var cellRect = new Rect2(cellPos.X * CellSize, cellPos.Y * CellSize, CellSize, CellSize);
                DrawRect(cellRect, baseColor with { A = cellAlpha });
            }

            // Brighter head cell
            if (!ray.IsExpired)
            {
                var headRect = new Rect2(ray.HeadPos.X * CellSize, ray.HeadPos.Y * CellSize, CellSize, CellSize);
                DrawRect(headRect, baseColor with { A = alpha * 0.5f });
            }
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

                var cellRect = new Rect2(cellPos.X * CellSize, cellPos.Y * CellSize, CellSize, CellSize);
                DrawRect(cellRect, waveColor with { A = cellAlpha });

                // Chevron overlay on brighter cells
                if (ringStr > 0.3f)
                    DrawChevron(center, wave.Direction, waveColor with { A = alpha * 0.8f }, CellSize * 0.22f);
            }
        }
    }

}
