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
                DrawJumperSphere(canvas, rect, palette, alpha);
                break;
            case BlockType.Wall:
            {
                var sprite = SpriteFactory.GetSprite(BlockType.Wall, playerId);
                if (sprite != null) canvas.DrawTextureRect(sprite, rect, false, tint);
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

    private static void DrawJumperSphere(CanvasItem canvas, Rect2 rect, PlayerPalette palette, float alpha = 1f)
    {
        var center = rect.GetCenter();
        float radius = rect.Size.X * 0.46f;
        var lightOff = new Vector2(-radius * 0.15f, -radius * 0.15f);

        for (int i = 0; i < 8; i++)
        {
            float t = (float)i / 8;
            float r = radius * (1f - t);
            var ringColor = t < 0.4f
                ? palette.JumperDark.Lerp(palette.JumperCore, t / 0.4f)
                : palette.JumperCore.Lerp(palette.JumperBright, (t - 0.4f) / 0.6f);
            canvas.DrawCircle(center + lightOff * t * 0.5f, r, Fade(ringColor, alpha));
        }

        canvas.DrawCircle(center + lightOff * 0.6f, radius * 0.18f, Colors.White with { A = 0.45f * alpha });
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
