using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.Rendering.Effects;

/// <summary>
/// Static path generators for visual effects.
/// All coordinates are in grid-space (1 unit = 1 cell).
/// Grid intersection (x, y) corresponds to pixel (x * CellSize + GridPadding, y * CellSize + GridPadding).
/// </summary>
public static class EffectShapes
{
    // ─── Lightning Burst ────────────────────────────────────────────
    // Random-walk from all cell edges, branching along grid lines.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> LightningBurst(
        int cx, int cy, Random rng, int maxSegs = 56,
        float contProb = 0.90f, float branchProb = 0.55f)
    {
        var seeds = AllEdgeSeeds(cx, cy);
        return BuildLightningPaths(seeds, rng, maxSegs, contProb, branchProb);
    }

    // ─── Lightning Directional ──────────────────────────────────────
    // Lightning from one edge direction only (e.g., opposite of movement).

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> LightningDirectional(
        int cx, int cy, int dx, int dy, Random rng, int maxSegs = 30,
        float contProb = 0.90f, float branchProb = 0.55f)
    {
        var seeds = EdgeSeeds(cx, cy, dx, dy);
        return BuildLightningPaths(seeds, rng, maxSegs, contProb, branchProb);
    }

    // ─── Spiral Trace ───────────────────────────────────────────────
    // Clockwise spiral outward from center cell.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> SpiralTrace(
        int cx, int cy, int maxSegs = 48)
    {
        var points = new List<Vector2>();
        int x = cx + 1, y = cy + 1;
        int dx = 1, dy = 0;
        int stepsInLeg = 1, stepsTaken = 0, turnsAtLen = 0;

        for (int i = 0; i < maxSegs; i++)
        {
            points.Add(new Vector2(x, y));
            x += dx; y += dy;
            stepsTaken++;
            if (stepsTaken >= stepsInLeg)
            {
                int tmp = dx; dx = -dy; dy = tmp;
                stepsTaken = 0;
                turnsAtLen++;
                if (turnsAtLen >= 2) { turnsAtLen = 0; stepsInLeg++; }
            }
        }
        points.Add(new Vector2(x, y));

        return new List<(Vector2[], float, float)> { (points.ToArray(), 0f, 1f) };
    }

    // ─── Square Rings ───────────────────────────────────────────────
    // Concentric square perimeters expanding outward.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> SquareRings(
        int cx, int cy, int maxRadius)
    {
        var paths = new List<(Vector2[], float, float)>(maxRadius);

        for (int ring = 1; ring <= maxRadius; ring++)
        {
            int tlx = cx - ring, tly = cy - ring;
            int brx = cx + 1 + ring, bry = cy + 1 + ring;

            var pts = new Vector2[]
            {
                new(tlx, tly), new(brx, tly),
                new(brx, tly), new(brx, bry),
                new(brx, bry), new(tlx, bry),
                new(tlx, bry), new(tlx, tly),
            };

            float normStart = (ring - 1) / (float)maxRadius;
            float normEnd = ring / (float)maxRadius;
            paths.Add((pts, normStart, normEnd));
        }

        return paths;
    }

    // ─── Cross ──────────────────────────────────────────────────────
    // Short straight arms from all 4 edges of a cell.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> Cross(
        int cx, int cy, int armLen = 3)
    {
        var paths = new List<(Vector2[], float, float)>();

        foreach (var seed in AllEdgeSeeds(cx, cy))
        {
            var pts = new List<Vector2>();
            float x = seed.Ix, y = seed.Iy;
            pts.Add(new Vector2(x, y));
            for (int i = 0; i < armLen; i++)
            {
                x += seed.Dx; y += seed.Dy;
                pts.Add(new Vector2(x, y));
            }
            paths.Add((pts.ToArray(), 0f, 1f));
        }

        return paths;
    }

    // ─── Cell Perimeter ─────────────────────────────────────────────
    // Trace a single cell's outline clockwise (2 passes).

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> CellPerimeter(
        int cx, int cy)
    {
        var corners = new (float x, float y)[]
        {
            (cx, cy), (cx + 1, cy), (cx + 1, cy + 1), (cx, cy + 1)
        };

        var pts = new List<Vector2>();
        for (int pass = 0; pass < 2; pass++)
            for (int i = 0; i < 4; i++)
                pts.Add(new Vector2(corners[i].x, corners[i].y));
        pts.Add(new Vector2(corners[0].x, corners[0].y));

        return new List<(Vector2[], float, float)> { (pts.ToArray(), 0f, 1f) };
    }

    // ─── Straight Tracer ────────────────────────────────────────────
    // Parallel lines along cell edges in one direction.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> StraightTracer(
        int cx, int cy, int dx, int dy, int reach = 12)
    {
        var paths = new List<(Vector2[], float, float)>();

        for (int lane = 0; lane < 2; lane++)
        {
            float offX = dy != 0 ? (lane == 0 ? cx : cx + 1) : cx + 0.5f;
            float offY = dx != 0 ? (lane == 0 ? cy : cy + 1) : cy + 0.5f;

            var pts = new Vector2[reach + 1];
            for (int i = 0; i <= reach; i++)
                pts[i] = new Vector2(offX + dx * i, offY + dy * i);

            paths.Add((pts, 0f, (float)(reach - 1) / reach));
        }

        return paths;
    }

    // ─── Straight Tracer All Directions ─────────────────────────────

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> StraightTracerAllDirs(
        int cx, int cy, int reach = 12)
    {
        var paths = new List<(Vector2[], float, float)>();
        int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        foreach (var d in dirs)
        {
            int dx = d[0], dy = d[1];
            for (int lane = 0; lane < 2; lane++)
            {
                float offX = dy != 0 ? (lane == 0 ? cx : cx + 1) : cx + 0.5f;
                float offY = dx != 0 ? (lane == 0 ? cy : cy + 1) : cy + 0.5f;

                var pts = new Vector2[reach + 1];
                for (int i = 0; i <= reach; i++)
                    pts[i] = new Vector2(offX + dx * i, offY + dy * i);

                paths.Add((pts, 0f, (float)(reach - 1) / reach));
            }
        }

        return paths;
    }

    // ─── Dashed Tendrils ────────────────────────────────────────────
    // Random walk with right-angle turns from cell edges (used with dashed shader flag).

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> DashedTendrils(
        int cx, int cy, Random rng, int tendrilCount = 8, int minLen = 5, int maxLen = 10)
    {
        var seeds = AllEdgeSeeds(cx, cy);
        var paths = new List<(Vector2[], float, float)>();

        for (int a = 0; a < tendrilCount; a++)
        {
            var seed = seeds[a % seeds.Count];
            float x = seed.Ix, y = seed.Iy;
            int sdx = seed.Dx, sdy = seed.Dy;
            int len = minLen + rng.Next(maxLen - minLen + 1);

            var pts = new List<Vector2> { new(x, y) };

            for (int i = 0; i < len; i++)
            {
                if (i > 1 && rng.NextSingle() < 0.3f)
                {
                    if (sdx == 0) { sdx = rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
                    else { sdy = rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
                }
                x += sdx; y += sdy;
                pts.Add(new Vector2(x, y));
            }

            paths.Add((pts.ToArray(), 0f, 1f));
        }

        return paths;
    }

    // ─── Staggered Arms ─────────────────────────────────────────────
    // Single straight lines from each edge, staggered by delay in distance.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> StaggeredArms(
        int cx, int cy, int armLen = 3, int stagger = 2)
    {
        var armDefs = new (float ix, float iy, int dx, int dy)[]
        {
            (cx + 1, cy + 0.5f, 1, 0),
            (cx, cy + 0.5f, -1, 0),
            (cx + 0.5f, cy + 1, 0, 1),
            (cx + 0.5f, cy, 0, -1),
        };

        float maxDist = armLen - 1 + (armDefs.Length - 1) * stagger;
        var paths = new List<(Vector2[], float, float)>();

        for (int a = 0; a < armDefs.Length; a++)
        {
            var (ix, iy, dx, dy) = armDefs[a];
            var pts = new Vector2[armLen + 1];
            for (int i = 0; i <= armLen; i++)
                pts[i] = new Vector2(ix + dx * i, iy + dy * i);

            float start = (a * stagger) / maxDist;
            float end = Math.Min(1f, (a * stagger + armLen - 1) / maxDist);
            paths.Add((pts, start, end));
        }

        return paths;
    }

    // ─── Jitter Arms ────────────────────────────────────────────────
    // Jittery random-walk arms from one edge direction. Used for Soldier spawn.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> JitterArms(
        int cx, int cy, int dx, int dy, Random rng, int armCount = 6, int armLen = 4)
    {
        var seeds = EdgeSeeds(cx, cy, dx, dy);
        var paths = new List<(Vector2[], float, float)>();

        for (int a = 0; a < armCount; a++)
        {
            var seed = seeds[a % seeds.Count];
            float x = seed.Ix, y = seed.Iy;
            int sdx = seed.Dx, sdy = seed.Dy;
            var pts = new List<Vector2> { new(x, y) };

            for (int i = 0; i < armLen; i++)
            {
                if (i > 0 && rng.NextSingle() < 0.6f)
                {
                    if (sdx == 0) { sdx = rng.NextSingle() < 0.5f ? 1 : -1; sdy = 0; }
                    else { sdy = rng.NextSingle() < 0.5f ? 1 : -1; sdx = 0; }
                }
                x += sdx; y += sdy;
                pts.Add(new Vector2(x, y));
            }

            paths.Add((pts.ToArray(), 0f, 1f));
        }

        return paths;
    }

    // ─── Line Trail ─────────────────────────────────────────────────
    // Straight line from one grid position to another. Used for Jump trail.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> LineTrail(
        int fromX, int fromY, int toX, int toY)
    {
        var pts = new[] { new Vector2(fromX, fromY), new Vector2(toX, toY) };
        return new List<(Vector2[], float, float)> { (pts, 0f, 1f) };
    }

    // ─── Arc Chain ──────────────────────────────────────────────────
    // Bezier arc chain: 8 arcs from center, each to a random nearby target.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> ArcChain(
        int cx, int cy, Random rng, int arcCount = 8, int subSegs = 4)
    {
        var allPts = new List<Vector2>();
        float x = cx + 0.5f, y = cy + 0.5f;

        for (int a = 0; a < arcCount; a++)
        {
            float tx = cx + rng.Next(-5, 6) + 0.5f;
            float ty = cy + rng.Next(-5, 6) + 0.5f;

            float midOffX = (rng.NextSingle() - 0.5f) * 2f;
            float midOffY = (rng.NextSingle() - 0.5f) * 2f;
            float midX = (x + tx) / 2f + midOffX;
            float midY = (y + ty) / 2f + midOffY;

            for (int i = 0; i <= subSegs; i++)
            {
                float t = (float)i / subSegs;
                float px = (1 - t) * (1 - t) * x + 2 * (1 - t) * t * midX + t * t * tx;
                float py = (1 - t) * (1 - t) * y + 2 * (1 - t) * t * midY + t * t * ty;
                allPts.Add(new Vector2(px, py));
            }

            x = tx; y = ty;
        }

        return new List<(Vector2[], float, float)> { (allPts.ToArray(), 0f, 1f) };
    }

    // ─── Circuit Trace ──────────────────────────────────────────────
    // BFS right-angle random walk from cell edges; each segment is its own path.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> CircuitTrace(
        int cx, int cy, Random rng, int maxSegs = 50)
    {
        var segs = new List<(float X1, float Y1, float X2, float Y2, float Dist)>();
        var frontier = new List<(float X, float Y, int Dx, int Dy, float Dist)>
        {
            (cx + 1, cy + 0.5f, 1, 0, 0),
            (cx,     cy + 0.5f, -1, 0, 0),
            (cx + 0.5f, cy + 1, 0, 1, 0),
            (cx + 0.5f, cy,     0, -1, 0),
        };
        float maxDist = 0;

        while (frontier.Count > 0 && segs.Count < maxSegs)
        {
            int idx = rng.Next(frontier.Count);
            var item = frontier[idx];
            frontier.RemoveAt(idx);

            float nx = item.X + item.Dx, ny = item.Y + item.Dy;
            segs.Add((item.X, item.Y, nx, ny, item.Dist));
            if (item.Dist > maxDist) maxDist = item.Dist;

            if (rng.NextSingle() < 0.7f)
                frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1));
            if (rng.NextSingle() < 0.35f)
            {
                int pdx = item.Dy == 0 ? 0 : (rng.NextSingle() < 0.5f ? 1 : -1);
                int pdy = item.Dy == 0 ? (rng.NextSingle() < 0.5f ? 1 : -1) : 0;
                frontier.Add((nx, ny, pdx, pdy, item.Dist + 1));
            }
        }

        var paths = new List<(Vector2[], float, float)>();
        foreach (var s in segs)
        {
            float normDist = maxDist > 0 ? s.Dist / maxDist : 0f;
            paths.Add((new Vector2[] { new(s.X1, s.Y1), new(s.X2, s.Y2) }, normDist, normDist));
        }

        return paths;
    }

    // ─── Wave Pulse ─────────────────────────────────────────────────
    // 4-directional lines with sine perpendicular displacement + random branches.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> WavePulse(
        int cx, int cy, Random rng, int reach = 12)
    {
        var paths = new List<(Vector2[], float, float)>();
        int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        foreach (var d in dirs)
        {
            int dx = d[0], dy = d[1];
            var pts = new List<Vector2>();

            for (int dist = 0; dist < reach; dist++)
            {
                float bx = cx + 0.5f + dx * dist;
                float by = cy + 0.5f + dy * dist;
                float perpAmt = 0.3f * MathF.Sin(dist * 1.2f + rng.NextSingle() * 0.5f);
                float px = dy != 0 ? perpAmt : 0;
                float py = dx != 0 ? perpAmt : 0;
                pts.Add(new Vector2(bx + px, by + py));
            }

            paths.Add((pts.ToArray(), 0f, 1f));

            // Random perpendicular branches
            for (int i = 0; i < pts.Count; i++)
            {
                if (rng.NextSingle() < 0.4f)
                {
                    float bpx = dy == 0 ? 0 : (rng.NextSingle() < 0.5f ? 1 : -1);
                    float bpy = dx == 0 ? 0 : (rng.NextSingle() < 0.5f ? 1 : -1);
                    var from = pts[i];
                    var to = from + new Vector2(bpx, bpy);
                    float normDist = (float)i / pts.Count;
                    paths.Add((new[] { from, to }, normDist, normDist + 0.05f));
                }
            }
        }

        return paths;
    }

    // ─── Sine Ripple ────────────────────────────────────────────────
    // 3 parallel lanes per direction (4 directions), straight lines.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> SineRipple(
        int cx, int cy, int laneCount = 3, int reach = 10)
    {
        var paths = new List<(Vector2[], float, float)>();
        int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        foreach (var d in dirs)
        {
            int dx = d[0], dy = d[1];
            for (int lane = 0; lane < laneCount; lane++)
            {
                float offX = dx != 0 ? cx + 0.5f : cx + (lane - 1) * 0.5f + 0.5f;
                float offY = dy != 0 ? cy + 0.5f : cy + (lane - 1) * 0.5f + 0.5f;

                var pts = new Vector2[reach + 1];
                for (int i = 0; i <= reach; i++)
                    pts[i] = new Vector2(offX + dx * i, offY + dy * i);

                paths.Add((pts, 0f, 1f));
            }
        }

        return paths;
    }

    // ─── ZoC Dashed Pulse ───────────────────────────────────────────
    // Cardinal radial lines + diagonal staircase paths. Used with dashed + loopMode.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> ZocDashedPulse(
        int cx, int cy, int zocR = 6)
    {
        var paths = new List<(Vector2[], float, float)>();
        int[][] dirs = { new[] { 1, 0 }, new[] { -1, 0 }, new[] { 0, 1 }, new[] { 0, -1 } };

        // Cardinal radial lines (2 lanes each direction)
        foreach (var d in dirs)
        {
            int dx = d[0], dy = d[1];
            for (int lane = 0; lane < 2; lane++)
            {
                float offX = dy != 0 ? (lane == 0 ? cx : cx + 1) : cx + 0.5f;
                float offY = dx != 0 ? (lane == 0 ? cy : cy + 1) : cy + 0.5f;

                var pts = new Vector2[zocR + 1];
                for (int i = 0; i <= zocR; i++)
                    pts[i] = new Vector2(offX + dx * i, offY + dy * i);

                paths.Add((pts, 0f, (float)(zocR - 1) / zocR));
            }
        }

        // Diagonal staircase lines (4 diagonal directions)
        int[] ddx = { 1, 1, -1, -1 };
        int[] ddy = { 1, -1, 1, -1 };
        for (int dir = 0; dir < 4; dir++)
        {
            var pts = new List<Vector2>();
            int sx = cx + (ddx[dir] > 0 ? 1 : 0);
            int sy = cy + (ddy[dir] > 0 ? 1 : 0);

            for (int i = 0; i < zocR; i++)
            {
                int gx = sx + ddx[dir] * i;
                int gy = sy + ddy[dir] * i;
                pts.Add(new Vector2(gx, gy));
                pts.Add(new Vector2(gx + ddx[dir], gy));
                pts.Add(new Vector2(gx + ddx[dir], gy + ddy[dir]));
            }

            paths.Add((pts.ToArray(), 0f, 1f));
        }

        return paths;
    }

    // ─── Select Squares ────────────────────────────────────────────
    // 3 concentric squares around cell center, expanding inward→outward.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> SelectSquares(
        int cx, int cy)
    {
        var paths = new List<(Vector2[], float, float)>();
        float ctrX = cx + 0.5f, ctrY = cy + 0.5f;

        for (int ring = 0; ring < 3; ring++)
        {
            float half = 0.45f + ring * 0.15f;
            var tl = new Vector2(ctrX - half, ctrY - half);
            var tr = new Vector2(ctrX + half, ctrY - half);
            var br = new Vector2(ctrX + half, ctrY + half);
            var bl = new Vector2(ctrX - half, ctrY + half);

            var pts = new[] { tl, tr, br, bl, tl };
            float normDist = ring / 2f;
            paths.Add((pts, normDist, normDist));
        }

        return paths;
    }

    // ─── Mining Spark ────────────────────────────────────────────────
    // Short paths from the shared edge between two adjacent cells, propagating
    // perpendicular in both directions. Used for the "hot tool hit" sparkle.

    public static List<(Vector2[] Points, float DistStart, float DistEnd)> MiningSpark(
        int cx, int cy, int dx, int dy, Random rng, int armCount = 6, int armLen = 3)
    {
        var paths = new List<(Vector2[], float, float)>();

        // Shared edge endpoints (the grid line between nugget and builder)
        float ex1, ey1, ex2, ey2;
        if (dx != 0) // Horizontal adjacency — shared edge is vertical
        {
            float edgeX = dx > 0 ? cx + 1 : cx;
            ex1 = edgeX; ey1 = cy;
            ex2 = edgeX; ey2 = cy + 1;
        }
        else // Vertical adjacency — shared edge is horizontal
        {
            float edgeY = dy > 0 ? cy + 1 : cy;
            ex1 = cx; ey1 = edgeY;
            ex2 = cx + 1; ey2 = edgeY;
        }

        // Perpendicular direction along the edge
        int px = Math.Abs(dy), py = Math.Abs(dx);

        for (int a = 0; a < armCount; a++)
        {
            // Start from a random point along the edge
            float t = (float)rng.NextDouble();
            float startX = ex1 + (ex2 - ex1) * t;
            float startY = ey1 + (ey2 - ey1) * t;

            // Alternate directions along the edge and into both cells
            int dir = (a % 2 == 0) ? 1 : -1;
            int len = 1 + rng.Next(armLen);
            var pts = new List<Vector2> { new(startX, startY) };

            float x = startX, y = startY;
            int sdx = px * dir, sdy = py * dir;
            for (int i = 0; i < len; i++)
            {
                // Occasionally jitter into the perpendicular (toward/away from cells)
                if (rng.NextSingle() < 0.4f)
                {
                    sdx = dx != 0 ? (rng.NextSingle() < 0.5f ? 1 : -1) : sdx;
                    sdy = dy != 0 ? (rng.NextSingle() < 0.5f ? 1 : -1) : sdy;
                }
                x += sdx; y += sdy;
                pts.Add(new(x, y));
            }

            paths.Add((pts.ToArray(), 0f, 1f));
        }

        return paths;
    }

    // ─── Edge Seed Helpers ──────────────────────────────────────────

    public record struct EdgeSeed(float Ix, float Iy, int Dx, int Dy);

    /// <summary>Seeds at all 4 edges of cell (cx, cy) — 2 per edge.</summary>
    public static List<EdgeSeed> AllEdgeSeeds(int cx, int cy) => new()
    {
        new(cx + 1, cy, 1, 0), new(cx + 1, cy + 1, 1, 0),
        new(cx, cy, -1, 0), new(cx, cy + 1, -1, 0),
        new(cx, cy + 1, 0, 1), new(cx + 1, cy + 1, 0, 1),
        new(cx, cy, 0, -1), new(cx + 1, cy, 0, -1),
    };

    /// <summary>Seeds at one edge of cell (cx, cy) facing direction (dx, dy).</summary>
    public static List<EdgeSeed> EdgeSeeds(int cx, int cy, int dx, int dy) => (dx, dy) switch
    {
        (1, 0) => new() { new(cx + 1, cy, 1, 0), new(cx + 1, cy + 1, 1, 0) },
        (-1, 0) => new() { new(cx, cy, -1, 0), new(cx, cy + 1, -1, 0) },
        (0, 1) => new() { new(cx, cy + 1, 0, 1), new(cx + 1, cy + 1, 0, 1) },
        _ => new() { new(cx, cy, 0, -1), new(cx + 1, cy, 0, -1) },
    };

    // ─── Lightning Path Builder ─────────────────────────────────────
    // Random-walk along grid lines with branching, grouped into connected paths.

    private static List<(Vector2[] Points, float DistStart, float DistEnd)> BuildLightningPaths(
        List<EdgeSeed> seeds, Random rng, int maxSegs, float contProb, float branchProb)
    {
        var allSegs = new List<(float X1, float Y1, float X2, float Y2, float Dist)>();
        var visited = new HashSet<long>();
        var frontier = new List<(float Ix, float Iy, int Dx, int Dy, int Dist, float Cp)>();

        foreach (var s in seeds)
            frontier.Add((s.Ix, s.Iy, s.Dx, s.Dy, 0, contProb));
        float maxDist = 0;

        while (frontier.Count > 0 && allSegs.Count < maxSegs)
        {
            int idx = rng.Next(frontier.Count);
            var item = frontier[idx];
            frontier.RemoveAt(idx);

            float nx = item.Ix + item.Dx, ny = item.Iy + item.Dy;

            int minIX = (int)Math.Min(item.Ix, nx), minIY = (int)Math.Min(item.Iy, ny);
            int maxIX = (int)Math.Max(item.Ix, nx), maxIY = (int)Math.Max(item.Iy, ny);
            long key = ((long)minIX << 48) | ((long)minIY << 32) | ((long)maxIX << 16) | (uint)(maxIY & 0xFFFF);
            if (!visited.Add(key)) continue;

            allSegs.Add((item.Ix, item.Iy, nx, ny, item.Dist));
            if (item.Dist > maxDist) maxDist = item.Dist;

            if (rng.NextSingle() < item.Cp)
                frontier.Add((nx, ny, item.Dx, item.Dy, item.Dist + 1, item.Cp * 0.82f));
            if (rng.NextSingle() < branchProb)
            {
                var (pdx, pdy) = item.Dy == 0
                    ? (0, rng.NextSingle() < 0.5f ? 1 : -1)
                    : (rng.NextSingle() < 0.5f ? 1 : -1, 0);
                frontier.Add((nx, ny, pdx, pdy, item.Dist + 1, item.Cp * 0.55f));
            }
        }

        // Group segments into connected paths by chaining endpoints
        var paths = new List<(Vector2[], float, float)>();
        var used = new bool[allSegs.Count];

        for (int si = 0; si < allSegs.Count; si++)
        {
            if (used[si]) continue;
            var chain = new List<Vector2>();
            float firstDist = allSegs[si].Dist;
            float lastDist = allSegs[si].Dist;
            int curEndX = (int)allSegs[si].X2;
            int curEndY = (int)allSegs[si].Y2;

            chain.Add(new Vector2(allSegs[si].X1, allSegs[si].Y1));
            chain.Add(new Vector2(allSegs[si].X2, allSegs[si].Y2));
            used[si] = true;

            bool extended;
            do
            {
                extended = false;
                for (int j = 0; j < allSegs.Count; j++)
                {
                    if (used[j]) continue;
                    var seg = allSegs[j];
                    if ((int)seg.X1 == curEndX && (int)seg.Y1 == curEndY)
                    {
                        chain.Add(new Vector2(seg.X2, seg.Y2));
                        curEndX = (int)seg.X2;
                        curEndY = (int)seg.Y2;
                        lastDist = seg.Dist;
                        used[j] = true;
                        extended = true;
                        break;
                    }
                }
            } while (extended);

            float normStart = maxDist > 0 ? firstDist / maxDist : 0f;
            float normEnd = maxDist > 0 ? lastDist / maxDist : 1f;
            paths.Add((chain.ToArray(), normStart, normEnd));
        }

        return paths;
    }
}
