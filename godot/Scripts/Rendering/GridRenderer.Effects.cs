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
    // Warden ZoC: shader-based sine rings per rooted warden
    private readonly Dictionary<int, ColorRect> _wardenZocRects = new();
    private readonly Dictionary<int, ColorRect> _nestRefineRects = new();
    private static readonly Shader _gridRingsShader = GD.Load<Shader>("res://Assets/Shaders/grid_rings.gdshader");
    private static readonly Shader _nestRefineShader = GD.Load<Shader>("res://Assets/Shaders/nest_refine.gdshader");

    private void UpdateWardenZoC()
    {
        if (_gameState == null) return;

        var grid = _gameState.Grid;
        int zocR = Constants.WardenZocRadius;
        var gridPixelSize = new Vector2(grid.Width * CellSize, grid.Height * CellSize);

        // Collect active rooted wardens
        var activeWardens = new HashSet<int>();
        foreach (var block in _gameState.Blocks)
        {
            if (block.Type != BlockType.Warden) continue;
            if (!block.IsFullyRooted || block.IsStunned) continue;

            activeWardens.Add(block.Id);

            // Create ColorRect + ShaderMaterial if new
            if (!_wardenZocRects.TryGetValue(block.Id, out var rect))
            {
                var mat = new ShaderMaterial { Shader = _gridRingsShader };
                var playerColor = _config.GetPalette(block.PlayerId).WardenZocColor;

                mat.SetShaderParameter("grid_size", new Vector2(grid.Width, grid.Height));
                mat.SetShaderParameter("cell_size", CellSize);
                mat.SetShaderParameter("max_radius", (float)zocR + 1f);
                mat.SetShaderParameter("trail", 2f);
                mat.SetShaderParameter("ring_color", playerColor);
                mat.SetShaderParameter("fade_mult", 1f);
                mat.SetShaderParameter("mode", 2); // sine rings
                mat.SetShaderParameter("loop_mode", true);
                mat.SetShaderParameter("base_alpha", 0.04f);
                mat.SetShaderParameter("progress", 0f);
                mat.SetShaderParameter("age_ms", 0f);

                rect = new ColorRect
                {
                    Position = new Vector2(GridPadding, GridPadding),
                    Size = gridPixelSize,
                    Material = mat,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                AddChild(rect);
                _wardenZocRects[block.Id] = rect;
            }

            // Update center to follow warden position (in grid coords)
            var mat2 = (ShaderMaterial)rect.Material;
            mat2.SetShaderParameter("center", new Vector2(block.Pos.X + 0.5f, block.Pos.Y + 0.5f));
            mat2.SetShaderParameter("age_ms", (float)Time.GetTicksMsec());
            // Progress drives the expanding wave — loop so it resets
            float waveCycleMs = 2500f;
            mat2.SetShaderParameter("progress", ((float)Time.GetTicksMsec() % waveCycleMs) / waveCycleMs);
        }

        // Remove ColorRects for wardens that are no longer active
        var stale = new List<int>();
        foreach (var (id, rect) in _wardenZocRects)
        {
            if (!activeWardens.Contains(id))
            {
                rect.QueueFree();
                stale.Add(id);
            }
        }
        foreach (var id in stale)
            _wardenZocRects.Remove(id);
    }

    private void DrawWardenZoC()
    {
        // Legacy entry point kept for compatibility — all work done in UpdateWardenZoC
    }

    private void UpdateNestRefineZones()
    {
        if (_gameState == null) return;

        var grid = _gameState.Grid;
        int refineR = Simulation.Core.Constants.NuggetRefineRadius;
        var gridPixelSize = new Vector2(grid.Width * CellSize, grid.Height * CellSize);

        var activeNests = new HashSet<int>();
        foreach (var nest in _gameState.Nests)
        {
            if (!nest.RefineEnabled) continue;
            if (_localVisibility != null && !_localVisibility.IsVisible(nest.Center)) continue;

            activeNests.Add(nest.Id);

            if (!_nestRefineRects.TryGetValue(nest.Id, out var rect))
            {
                var mat = new ShaderMaterial { Shader = _nestRefineShader };

                mat.SetShaderParameter("grid_size", new Vector2(grid.Width, grid.Height));
                mat.SetShaderParameter("cell_size", CellSize);
                mat.SetShaderParameter("max_radius", (float)refineR + 1f);
                mat.SetShaderParameter("zone_color", new Color(0.55f, 0.67f, 1.0f, 0.8f));
                mat.SetShaderParameter("march_speed", 40f);
                mat.SetShaderParameter("time_ms", 0f);

                rect = new ColorRect
                {
                    Position = new Vector2(GridPadding, GridPadding),
                    Size = gridPixelSize,
                    Material = mat,
                    MouseFilter = Control.MouseFilterEnum.Ignore,
                };
                AddChild(rect);
                _nestRefineRects[nest.Id] = rect;
            }

            var mat2 = (ShaderMaterial)rect.Material;
            mat2.SetShaderParameter("center", new Vector2(nest.Center.X + 0.5f, nest.Center.Y + 0.5f));
            mat2.SetShaderParameter("time_ms", (float)Time.GetTicksMsec());
        }

        var stale = new List<int>();
        foreach (var (id, rect) in _nestRefineRects)
        {
            if (!activeNests.Contains(id))
            {
                rect.QueueFree();
                stale.Add(id);
            }
        }
        foreach (var id in stale)
            _nestRefineRects.Remove(id);
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

        // Stun and Blast rays: one-shot expansion like explosion (no repeating pulse)
        // Wavefront at ray.Distance, cells behind it fade to nothing
        const float fadeLength = 2.5f;

        // No repeating cycle — wavefront is exactly where the ray head is
        // Skip origin only if a block sits there (center ray over the stunner/soldier)
        // Side rays originate on empty cells beside the tower, so include i=0
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
