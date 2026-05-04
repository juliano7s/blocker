using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Draws static unit-type icons that match GridRenderer block visuals.
/// Used by SpawnToggles — same drawing code as the grid, no animation.
/// </summary>
public static class BlockIconPainter
{
    /// <summary>
    /// Draw a unit icon into rect. Applies a dim overlay when enabled=false.
    /// No-op if config is null (sprites not ready yet).
    /// </summary>
    public static void Draw(CanvasItem canvas, BlockType type, int playerId, Rect2 rect, GameConfig? config, bool enabled, float alpha = 1f, bool isRooted = false)
    {
        if (config == null) return;
        var palette = config.GetPalette(playerId);
        var tint = new Color(1f, 1f, 1f, alpha);

        if (isRooted && type != BlockType.Wall)
        {
            DrawDiagonalStripes(canvas, rect, 0.18f * alpha);
        }

        switch (type)
        {
            case BlockType.Builder:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Builder, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                break;
            }
            case BlockType.Soldier:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Soldier, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                DrawSoldierArms(canvas, rect, palette, alpha);
                break;
            }
            case BlockType.Stunner:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Stunner, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                DrawStunnerDiamond(canvas, rect, palette, alpha);
                break;
            }
            case BlockType.Warden:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Warden, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                DrawWardenShield(canvas, rect, palette, alpha);
                break;
            }
            case BlockType.Jumper:
            {
                var style = JumperStyleToggle.Current;
                if (style == JumperStyle.GlossyOrb || style == JumperStyle.BronzeBall)
                {
                    var sprite = SpriteFactory.GetSprite(BlockType.Jumper, playerId);
                    if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                }
                DrawJumperIcon(canvas, rect, palette, alpha);
                break;
            }
            case BlockType.Wall:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Wall, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                break;
            }
            case BlockType.Nugget:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Nugget, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
                DrawNuggetDiamond(canvas, rect, alpha);
                break;
            }
        }

        if (!enabled)
            canvas.DrawRect(rect, new Color(0f, 0f, 0f, 0.72f * alpha));
    }

    private static Color Fade(Color c, float alpha) => c with { A = c.A * alpha };

    private static void DrawSoldierArms(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha = 1f)
    {
        var center = rect.GetCenter();
        var gold = Fade(palette.SoldierArmsColor, alpha);
        float arm = rect.Size.X * 0.25f;
        var tl = center + new Vector2(-arm, -arm);
        var tr = center + new Vector2(arm, -arm);
        var bl = center + new Vector2(-arm, arm);
        var br = center + new Vector2(arm, arm);
        canvas.DrawLine(tl, center, gold, 1.4f, true);
        canvas.DrawLine(br, center, gold, 1.4f, true);
        canvas.DrawLine(tr, center, gold, 1.4f, true);
        canvas.DrawLine(bl, center, gold, 1.4f, true);
        canvas.DrawCircle(center, 1f, gold);
    }

    private static void DrawStunnerDiamond(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha = 1f)
    {
        var center = rect.GetCenter();
        float d = rect.Size.X * 0.28f;
        var pts = DiamondPoints(center, d);
        canvas.DrawColoredPolygon(pts, Fade(palette.StunnerDiamondOuter, alpha));
        var stroke = new Color(1f, 1f, 1f, 0.5f * alpha);
        for (int i = 0; i < 4; i++)
            canvas.DrawLine(pts[i], pts[(i + 1) % 4], stroke, 1.5f, true);
        var inner = DiamondPoints(center, d * 0.5f);
        canvas.DrawColoredPolygon(inner, new Color(1f, 1f, 1f, 0.2f * alpha));
    }

    private static void DrawWardenShield(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha = 1f)
    {
        var center = rect.GetCenter();
        float sw = rect.Size.X * 0.34f;
        float sh = rect.Size.Y * 0.42f;
        const float pulse = 0.8f;

        var top = center + new Vector2(0, -sh * 0.48f);
        var outer = new Vector2[]
        {
            top + new Vector2(-sw, 0),
            top + new Vector2(sw, 0),
            center + new Vector2(sw * 0.9f, sh * 0.05f),
            center + new Vector2(sw * 0.55f, sh * 0.3f),
            center + new Vector2(0, sh * 0.52f),
            center + new Vector2(-sw * 0.55f, sh * 0.3f),
            center + new Vector2(-sw * 0.9f, sh * 0.05f)
        };
        var white = new Color(1f, 1f, 1f);
        canvas.DrawColoredPolygon(outer, white with { A = 0.25f * pulse * alpha });

        const float s = 0.7f;
        var innerTop = center + new Vector2(0, -sh * 0.48f * s);
        var inner = new Vector2[]
        {
            innerTop + new Vector2(-sw * s, 0),
            innerTop + new Vector2(sw * s, 0),
            center + new Vector2(sw * 0.9f * s, sh * 0.05f * s),
            center + new Vector2(sw * 0.55f * s, sh * 0.3f * s),
            center + new Vector2(0, sh * 0.52f * s),
            center + new Vector2(-sw * 0.55f * s, sh * 0.3f * s),
            center + new Vector2(-sw * 0.9f * s, sh * 0.05f * s)
        };
        canvas.DrawColoredPolygon(inner, white with { A = 0.2f * pulse * alpha });

        canvas.DrawLine(outer[0], outer[1], white with { A = 0.7f * pulse * alpha }, 2f, true);
        canvas.DrawLine(outer[6], outer[0], white with { A = 0.7f * pulse * alpha }, 1.5f, true);
        canvas.DrawLine(outer[1], outer[2], white with { A = 0.7f * pulse * alpha }, 1.5f, true);
        canvas.DrawLine(outer[2], outer[3], white with { A = 0.35f * pulse * alpha }, 1.5f, true);
        canvas.DrawLine(outer[5], outer[6], white with { A = 0.35f * pulse * alpha }, 1.5f, true);
        canvas.DrawLine(outer[3], outer[4], white with { A = 0.35f * pulse * alpha }, 1.5f, true);
        canvas.DrawLine(outer[4], outer[5], white with { A = 0.35f * pulse * alpha }, 1.5f, true);

        var cross = white with { A = 0.55f * pulse * alpha };
        canvas.DrawLine(center + new Vector2(0, -sh * 0.28f), center + new Vector2(0, sh * 0.28f), cross, 1.5f, true);
        canvas.DrawLine(center + new Vector2(-sw * 0.45f, -sh * 0.04f), center + new Vector2(sw * 0.45f, -sh * 0.04f), cross, 1.5f, true);
    }

    private static void DrawJumperIcon(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha = 1f)
    {
        switch (JumperStyleToggle.Current)
        {
            case JumperStyle.GlossyOrb:
                DrawJumperGlossyOrbIcon(canvas, rect, palette, alpha);
                break;
            case JumperStyle.BronzeBall:
                DrawJumperBronzeBallIcon(canvas, rect, palette, alpha);
                break;
            case JumperStyle.BeveledSphere:
                DrawJumperBeveledSphereIcon(canvas, rect, palette, alpha);
                break;
            case JumperStyle.FacetedGem:
                DrawJumperFacetedGemIcon(canvas, rect, palette, alpha);
                break;
        }
    }

    private static void DrawJumperGlossyOrbIcon(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha)
    {
        var center = rect.GetCenter();
        float r = rect.Size.X * 0.34f;
        var lightOff = new Vector2(-r * 0.18f, -r * 0.18f);

        for (int i = 0; i < 10; i++)
        {
            float t = (float)i / 10;
            float ringR = r * (1f - t);
            Color c;
            if (t < 0.3f) c = palette.JumperDark.Lerp(palette.JumperCore, t / 0.3f);
            else if (t < 0.6f) c = palette.JumperCore.Lerp(palette.JumperBright, (t - 0.3f) / 0.3f);
            else c = palette.JumperBright.Lerp(new Color(1f, 1f, 0.95f), (t - 0.6f) / 0.4f);
            canvas.DrawCircle(center + lightOff * t * 0.6f, ringR, Fade(c, alpha));
        }
        canvas.DrawCircle(center + lightOff * 0.55f, r * 0.22f, Colors.White with { A = 0.55f * alpha });
        canvas.DrawCircle(center + lightOff * 0.44f, r * 0.1f, Colors.White with { A = 0.75f * alpha });
    }

    private static void DrawJumperBronzeBallIcon(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha)
    {
        var center = rect.GetCenter();
        float r = rect.Size.X * 0.33f;
        var lightOff = new Vector2(-r * 0.15f, -r * 0.18f);
        var rimColor = new Color(0.25f, 0.15f, 0.06f);
        var midColor = new Color(0.6f, 0.4f, 0.2f);
        var brightColor = new Color(0.95f, 0.85f, 0.65f);

        canvas.DrawCircle(center, r + 1f, Fade(new Color(0.15f, 0.08f, 0.02f, 0.7f), alpha));
        for (int i = 0; i < 10; i++)
        {
            float t = (float)i / 10;
            float ringR = r * (1f - t);
            var c = t < 0.5f ? rimColor.Lerp(midColor, t / 0.5f) : midColor.Lerp(brightColor, (t - 0.5f) / 0.5f);
            canvas.DrawCircle(center + lightOff * t * 0.5f, ringR, Fade(c, alpha));
        }
        canvas.DrawCircle(center + lightOff * 0.5f, r * 0.16f, Colors.White with { A = 0.35f * alpha });
    }

    private static void DrawJumperBeveledSphereIcon(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha)
    {
        var center = rect.GetCenter();
        float r = rect.Size.X * 0.46f;
        var lightOff = new Vector2(-r * 0.15f, -r * 0.2f);

        canvas.DrawCircle(center + new Vector2(0.5f, 1f), r, Fade(new Color(0.2f, 0.07f, 0f, 0.6f), alpha));
        canvas.DrawCircle(center, r, Fade(palette.JumperDark.Darkened(0.4f), alpha));

        for (int i = 0; i < 12; i++)
        {
            float t = (float)i / 12;
            float ringR = (r - 2f) * (1f - t);
            Color c;
            if (t < 0.35f) c = palette.JumperDark.Lerp(palette.JumperCore, t / 0.35f);
            else if (t < 0.65f) c = palette.JumperCore.Lerp(palette.JumperBright, (t - 0.35f) / 0.3f);
            else c = palette.JumperBright.Lerp(new Color(1f, 0.98f, 0.9f), (t - 0.65f) / 0.35f);
            canvas.DrawCircle(center + lightOff * t * 0.6f, ringR, Fade(c, alpha));
        }
        canvas.DrawCircle(center + lightOff * 0.4f, r * 0.4f, Colors.White with { A = 0.12f * alpha });
        canvas.DrawCircle(center + lightOff * 0.55f, r * 0.18f, Colors.White with { A = 0.45f * alpha });
        canvas.DrawCircle(center + lightOff * 0.44f, r * 0.08f, Colors.White with { A = 0.65f * alpha });
    }

    private static void DrawJumperFacetedGemIcon(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha)
    {
        var center = rect.GetCenter();
        float r = rect.Size.X * 0.46f;
        var lightOff = new Vector2(-r * 0.12f, -r * 0.15f);

        canvas.DrawCircle(center + new Vector2(0.5f, 0.8f), r, Fade(palette.JumperDark.Darkened(0.5f) with { A = 0.5f }, alpha));

        for (int i = 0; i < 12; i++)
        {
            float t = (float)i / 12;
            float ringR = r * (1f - t);
            Color c = t < 0.4f
                ? palette.JumperDark.Lerp(palette.JumperCore, t / 0.4f)
                : palette.JumperCore.Lerp(palette.JumperBright, (t - 0.4f) / 0.6f * 0.7f);
            canvas.DrawCircle(center + lightOff * t * 0.5f, ringR, Fade(c, alpha));
        }

        var top = center + new Vector2(0, -r * 0.92f);
        var left = center + new Vector2(-r * 0.88f, r * 0.15f);
        var right = center + new Vector2(r * 0.88f, r * 0.15f);
        var bot = center + new Vector2(0, r * 0.92f);
        var midL = center + new Vector2(-r * 0.55f, -r * 0.35f);
        var midR = center + new Vector2(r * 0.55f, -r * 0.35f);

        canvas.DrawColoredPolygon(new[] { top, midL, midR }, Colors.White with { A = 0.14f * alpha });
        canvas.DrawColoredPolygon(new[] { top, midL, left }, Colors.White with { A = 0.07f * alpha });

        var facetLight = Colors.White with { A = 0.25f * alpha };
        var facetDark = Colors.Black with { A = 0.18f * alpha };
        canvas.DrawLine(top, midL, facetLight, 1.2f, true);
        canvas.DrawLine(top, midR, facetLight, 1.2f, true);
        canvas.DrawLine(midL, midR, Colors.White with { A = 0.12f * alpha }, 1f, true);
        canvas.DrawLine(midL, left, facetDark, 1f, true);
        canvas.DrawLine(midR, right, facetDark, 1f, true);
        canvas.DrawLine(left, bot, facetDark, 0.8f, true);
        canvas.DrawLine(right, bot, facetDark, 0.8f, true);

        var specPos = center + new Vector2(-r * 0.12f, -r * 0.42f);
        canvas.DrawCircle(specPos, r * 0.14f, Colors.White with { A = 0.4f * alpha });
        canvas.DrawArc(center, r, 0f, Mathf.Tau, 32, Fade(palette.JumperDark.Darkened(0.3f) with { A = 0.45f }, alpha), 1.2f, true);
    }

    private static void DrawNuggetDiamond(CanvasItem canvas, Rect2 rect, float alpha)
    {
        var center = rect.GetCenter();
        float d = rect.Size.X * 0.30f;
        var pts = DiamondPoints(center, d);
        canvas.DrawColoredPolygon(pts, new Color(1f, 1f, 1f, 0.6f * alpha));
        var stroke = new Color(0.7f, 0.75f, 0.85f, 0.8f * alpha);
        for (int i = 0; i < 4; i++)
            canvas.DrawLine(pts[i], pts[(i + 1) % 4], stroke, 1.5f, true);
    }

    private static Vector2[] DiamondPoints(Vector2 center, float halfSize)
    {
        return
        [
            center + new Vector2(0, -halfSize),
            center + new Vector2(halfSize, 0),
            center + new Vector2(0, halfSize),
            center + new Vector2(-halfSize, 0)
        ];
    }

    private static void DrawDiagonalStripes(CanvasItem canvas, Rect2 rect, float alpha)
    {
        float stripeSpacing = 8f;
        float stripeWidth = 3f;

        var stripeColor = new Color(0.15f, 0.15f, 0.15f, alpha);
        float w = rect.Size.X;
        float h = rect.Size.Y;

        for (float d = 0; d <= w + h; d += stripeSpacing)
        {
            float x0 = Mathf.Max(0, d - h);
            float y0 = d - x0;
            float x1 = Mathf.Min(w, d);
            float y1 = d - x1;

            if (x0 >= w || x1 <= 0 || y0 < 0 || y1 >= h) continue;

            var p0 = rect.Position + new Vector2(x0, y0);
            var p1 = rect.Position + new Vector2(x1, y1);
            canvas.DrawLine(p0, p1, stripeColor, stripeWidth, true);
        }
    }
}
