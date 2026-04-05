using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Pre-renders block body sprites at startup with layered directional gradients,
/// matching the TS prototype's visual quality. Sprites are cached as ImageTexture.
/// Custom sprites from res://Assets/Sprites/Blocks/{Type}.png take priority.
/// </summary>
public static class SpriteFactory
{
    // Sprite size: 2x resolution for quality. Block visual size = CellSize - 2*BlockInset = 23px.
    private const int SpriteSize = 46;

    private static readonly Dictionary<(BlockType, int), ImageTexture> _cache = new();
    private static readonly Dictionary<BlockType, Texture2D?> _customSprites = new();
    private static ImageTexture? _radialGlow;
    private static bool _customSpritesLoaded;

    /// <summary>
    /// Build all block sprites for the given config. Call once when config is set.
    /// </summary>
    public static void Build(GameConfig config)
    {
        _cache.Clear();
        var types = new[] { BlockType.Builder, BlockType.Soldier, BlockType.Stunner, BlockType.Wall, BlockType.Warden };

        for (int playerId = 0; playerId < config.PlayerPalettes.Length; playerId++)
        {
            var palette = config.GetPalette(playerId);
            foreach (var type in types)
            {
                var baseColor = GetBaseColor(type, palette);
                var image = CreateBlockImage(baseColor, type, palette);
                var tex = ImageTexture.CreateFromImage(image);
                _cache[(type, playerId)] = tex;
            }
        }
    }

    /// <summary>
    /// Get sprite for a block type + player. Returns custom sprite if available,
    /// otherwise the pre-rendered procedural sprite.
    /// </summary>
    public static Texture2D? GetSprite(BlockType type, int playerId)
    {
        LoadCustomSprites();
        if (_customSprites.TryGetValue(type, out var custom) && custom != null)
            return custom;
        _cache.TryGetValue((type, playerId), out var tex);
        return tex;
    }

    /// <summary>
    /// Get a cached 64x64 radial glow texture (white center -> transparent edge, Gaussian falloff).
    /// </summary>
    public static ImageTexture GetRadialGlow()
    {
        if (_radialGlow != null) return _radialGlow;

        const int size = 64;
        var image = Image.CreateEmpty(size, size, false, Image.Format.Rgba8);
        float center = (size - 1) * 0.5f;

        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dx = (x - center) / center;
                float dy = (y - center) / center;
                float distSq = dx * dx + dy * dy;
                float alpha = distSq < 1f ? Mathf.Exp(-3f * distSq) : 0f;
                image.SetPixel(x, y, new Color(1f, 1f, 1f, alpha));
            }
        }

        _radialGlow = ImageTexture.CreateFromImage(image);
        return _radialGlow;
    }

    private static void LoadCustomSprites()
    {
        if (_customSpritesLoaded) return;
        _customSpritesLoaded = true;
        foreach (BlockType bt in System.Enum.GetValues<BlockType>())
        {
            var path = $"res://Assets/Sprites/Blocks/{bt}.png";
            _customSprites[bt] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
        }
    }

    private static Color GetBaseColor(BlockType type, PlayerPalette palette) => type switch
    {
        BlockType.Builder => palette.BuilderFill,
        BlockType.Soldier => palette.SoldierFill,
        BlockType.Stunner => palette.StunnerFill,
        BlockType.Wall => palette.WallFill,
        BlockType.Warden => palette.BuilderFill,
        _ => palette.Base
    };

    /// <summary>
    /// Per-type gradient parameters matching the TS prototype's Sprites.ts exactly.
    /// </summary>
    private record struct GradientRecipe(
        float TopAlpha, float TopHeight,
        float LeftAlpha, float LeftWidth,
        float BotAlpha, float BotHeight);

    private static GradientRecipe GetRecipe(BlockType type) => type switch
    {
        // Soldier: subtle top highlight, no left, stronger bottom shadow
        BlockType.Soldier => new(0.15f, 0.4f, 0f, 0f, 0.30f, 0.4f),
        // Stunner: slightly different proportions
        BlockType.Stunner => new(0.25f, 0.35f, 0.12f, 0.25f, 0.30f, 0.35f),
        // Warden: builder-like gradients but shorter bottom shadow (border eats space)
        BlockType.Warden => new(0.25f, 0.4f, 0.12f, 0.3f, 0.25f, 0.2f),
        // Builder, Wall, and default: the original recipe
        _ => new(0.30f, 0.4f, 0.15f, 0.3f, 0.25f, 0.4f),
    };

    private static Image CreateBlockImage(Color baseColor, BlockType type, PlayerPalette palette)
    {
        int s = SpriteSize;
        var image = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);
        var recipe = GetRecipe(type);

        // 1. Base fill
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                image.SetPixel(x, y, baseColor);

        // 2. Top gradient
        if (recipe.TopAlpha > 0f)
        {
            int topH = (int)(s * recipe.TopHeight);
            for (int y = 0; y < topH; y++)
            {
                float t = (float)y / topH;
                float alpha = recipe.TopAlpha * (1f - t);
                var overlay = new Color(1f, 1f, 1f, alpha);
                for (int x = 0; x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
            }
        }

        // 3. Left gradient
        if (recipe.LeftAlpha > 0f)
        {
            int leftW = (int)(s * recipe.LeftWidth);
            for (int x = 0; x < leftW; x++)
            {
                float t = (float)x / leftW;
                float alpha = recipe.LeftAlpha * (1f - t);
                var overlay = new Color(1f, 1f, 1f, alpha);
                for (int y = 0; y < s; y++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
            }
        }

        // 4. Bottom gradient
        if (recipe.BotAlpha > 0f)
        {
            int botH = (int)(s * recipe.BotHeight);
            int botStart = s - botH;
            for (int y = botStart; y < s; y++)
            {
                float t = (float)(y - botStart) / botH;
                float alpha = recipe.BotAlpha * t;
                var overlay = new Color(0f, 0f, 0f, alpha);
                for (int x = 0; x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
            }
        }

        // 5. Depth edges: black 0.4a on right 1px and bottom 1px (2px at 2x res)
        var edgeColor = new Color(0f, 0f, 0f, 0.4f);
        for (int y = 1; y < s; y++)
        {
            image.SetPixel(s - 2, y, BlendOver(image.GetPixel(s - 2, y), edgeColor));
            image.SetPixel(s - 1, y, BlendOver(image.GetPixel(s - 1, y), edgeColor));
        }
        for (int x = 1; x < s; x++)
        {
            image.SetPixel(x, s - 2, BlendOver(image.GetPixel(x, s - 2), edgeColor));
            image.SetPixel(x, s - 1, BlendOver(image.GetPixel(x, s - 1), edgeColor));
        }

        // 6. Type-specific details
        if (type == BlockType.Wall)
            PaintWallStuds(image, s);
        else if (type == BlockType.Warden)
            PaintWardenShield(image, s);

        return image;
    }

    /// <summary>
    /// Warden: dark inset border + baked shield emblem + inner highlight.
    /// Matches TS Sprites.ts warden sprite exactly.
    /// </summary>
    private static void PaintWardenShield(Image image, int s)
    {
        // Dark border to distinguish from Builder (TS: strokeRect with rgba(0,0,0,0.5))
        int borderW = Mathf.Max(2, (int)(s * 0.07f));
        var borderColor = new Color(0f, 0f, 0f, 0.5f);
        for (int i = 0; i < borderW; i++)
        {
            for (int x = i; x < s - i; x++)
            {
                image.SetPixel(x, i, BlendOver(image.GetPixel(x, i), borderColor));
                image.SetPixel(x, s - 1 - i, BlendOver(image.GetPixel(x, s - 1 - i), borderColor));
            }
            for (int y = i; y < s - i; y++)
            {
                image.SetPixel(i, y, BlendOver(image.GetPixel(i, y), borderColor));
                image.SetPixel(s - 1 - i, y, BlendOver(image.GetPixel(s - 1 - i, y), borderColor));
            }
        }

        // Shield shape: pointed bottom, flat top (TS coordinates)
        float cx = s / 2f, cy = s * 0.42f;
        float sw = s * 0.34f, sh = s * 0.48f;

        // Outer shield fill: rgba(86,86,86,0.52)
        var shieldFill = new Color(86f / 255f, 86f / 255f, 86f / 255f, 0.52f);
        FillShieldShape(image, s, cx, cy, sw, sh, 1f, shieldFill);

        // Outer shield stroke: rgba(255,255,255,0.45), ~1-2px
        var strokeColor = new Color(1f, 1f, 1f, 0.45f);
        StrokeShieldShape(image, s, cx, cy, sw, sh, 1f, strokeColor);

        // Inner shield highlight: rgba(255,255,255,0.12), 55% scale
        var innerColor = new Color(1f, 1f, 1f, 0.12f);
        FillShieldShape(image, s, cx, cy, sw, sh, 0.55f, innerColor);
    }

    /// <summary>
    /// Fill a pointed-bottom shield polygon using scanline rasterization.
    /// Shield shape: flat top, straight sides tapering to a point at bottom.
    /// scale = 1.0 for outer, 0.55 for inner highlight.
    /// </summary>
    private static void FillShieldShape(Image image, int s, float cx, float cy,
        float sw, float sh, float scale, Color color)
    {
        // 5 vertices of the shield (matching TS)
        float topY = cy - sh * 0.35f * scale;
        float midY = cy + sh * 0.15f * scale;
        float botY = cy + sh * 0.65f * scale;
        float leftX = cx - sw * scale;
        float rightX = cx + sw * scale;

        // Scanline fill: for each row, find left and right edges
        int yMin = Mathf.Max(0, (int)topY);
        int yMax = Mathf.Min(s - 1, (int)botY);

        for (int y = yMin; y <= yMax; y++)
        {
            float fy = y + 0.5f;
            float xLeft, xRight;

            if (fy <= midY)
            {
                // Top section: straight vertical sides
                xLeft = leftX;
                xRight = rightX;
            }
            else
            {
                // Bottom section: taper to center point
                float t = (fy - midY) / (botY - midY);
                t = Mathf.Clamp(t, 0f, 1f);
                xLeft = Mathf.Lerp(leftX, cx, t);
                xRight = Mathf.Lerp(rightX, cx, t);
            }

            int x0 = Mathf.Max(0, (int)xLeft);
            int x1 = Mathf.Min(s - 1, (int)xRight);
            for (int x = x0; x <= x1; x++)
                image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), color));
        }
    }

    /// <summary>
    /// Stroke the shield shape outline (~1-2px).
    /// </summary>
    private static void StrokeShieldShape(Image image, int s, float cx, float cy,
        float sw, float sh, float scale, Color color)
    {
        float topY = cy - sh * 0.35f * scale;
        float midY = cy + sh * 0.15f * scale;
        float botY = cy + sh * 0.65f * scale;
        float leftX = cx - sw * scale;
        float rightX = cx + sw * scale;

        // Draw edges using Bresenham-like line segments
        // Top edge
        DrawLineOnImage(image, s, (int)leftX, (int)topY, (int)rightX, (int)topY, color);
        // Left side (top to mid)
        DrawLineOnImage(image, s, (int)leftX, (int)topY, (int)leftX, (int)midY, color);
        // Right side (top to mid)
        DrawLineOnImage(image, s, (int)rightX, (int)topY, (int)rightX, (int)midY, color);
        // Left taper (mid to bottom)
        DrawLineOnImage(image, s, (int)leftX, (int)midY, (int)cx, (int)botY, color);
        // Right taper (mid to bottom)
        DrawLineOnImage(image, s, (int)rightX, (int)midY, (int)cx, (int)botY, color);
    }

    /// <summary>
    /// Bresenham line on an Image (1px thick).
    /// </summary>
    private static void DrawLineOnImage(Image image, int s, int x0, int y0, int x1, int y1, Color color)
    {
        int dx = Mathf.Abs(x1 - x0), dy = -Mathf.Abs(y1 - y0);
        int sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1;
        int err = dx + dy;

        while (true)
        {
            if (x0 >= 0 && x0 < s && y0 >= 0 && y0 < s)
                image.SetPixel(x0, y0, BlendOver(image.GetPixel(x0, y0), color));
            if (x0 == x1 && y0 == y1) break;
            int e2 = 2 * err;
            if (e2 >= dy) { err += dy; x0 += sx; }
            if (e2 <= dx) { err += dx; y0 += sy; }
        }
    }

    private static void PaintWallStuds(Image image, int s)
    {
        float studSize = s * 0.25f;
        float studGap = s * 0.09f;
        float cx = s / 2f;
        float cy = s / 2f;

        (float sx, float sy)[] offsets =
        {
            (cx - studSize - studGap / 2f, cy - studSize - studGap / 2f),
            (cx + studGap / 2f,            cy - studSize - studGap / 2f),
            (cx - studSize - studGap / 2f, cy + studGap / 2f),
            (cx + studGap / 2f,            cy + studGap / 2f),
        };

        foreach (var (sx, sy) in offsets)
        {
            int x0 = (int)sx, y0 = (int)sy;
            int w = (int)studSize, h = (int)studSize;

            // Stud base: white 0.1a
            for (int y = y0; y < y0 + h && y < s; y++)
                for (int x = x0; x < x0 + w && x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(1f, 1f, 1f, 0.1f)));

            // Top gradient on stud
            int studTopH = (int)(h * 0.4f);
            for (int y = y0; y < y0 + studTopH && y < s; y++)
            {
                float t = (float)(y - y0) / studTopH;
                float alpha = 0.22f * (1f - t);
                for (int x = x0; x < x0 + w && x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(1f, 1f, 1f, alpha)));
            }

            // Left gradient on stud
            int studLeftW = (int)(w * 0.35f);
            for (int x = x0; x < x0 + studLeftW && x < s; x++)
            {
                float t = (float)(x - x0) / studLeftW;
                float alpha = 0.15f * (1f - t);
                for (int y = y0; y < y0 + h && y < s; y++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(1f, 1f, 1f, alpha)));
            }

            // Bottom gradient on stud
            int studBotStart = y0 + (int)(h * 0.6f);
            for (int y = studBotStart; y < y0 + h && y < s; y++)
            {
                float t = (float)(y - studBotStart) / (y0 + h - studBotStart);
                float alpha = 0.25f * t;
                for (int x = x0; x < x0 + w && x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(0f, 0f, 0f, alpha)));
            }
        }
    }

    private static Color BlendOver(Color baseC, Color overlay)
    {
        float oa = overlay.A;
        float r = baseC.R * (1f - oa) + overlay.R * oa;
        float g = baseC.G * (1f - oa) + overlay.G * oa;
        float b = baseC.B * (1f - oa) + overlay.B * oa;
        float a = baseC.A + oa * (1f - baseC.A);
        return new Color(r, g, b, a);
    }
}
