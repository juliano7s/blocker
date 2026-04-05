# Rendering Quality Improvements — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Close the visual quality gap between the Godot port and the original TS/Canvas prototype by adding additive glow blending, pre-rendered sprite textures, round line caps, radial glow textures, anti-aliased lines, and tuned multi-pass glow rendering.

**Architecture:** Six changes to the rendering layer. Two new files (SpriteFactory, GlowLayer) plus modifications to the existing GridRenderer partials. No simulation changes. All textures cached at startup for zero per-frame allocation.

**Tech Stack:** Godot 4 C#, `Image`/`ImageTexture` for sprite generation, `CanvasItemMaterial` for additive blending, `Node2D._Draw()` for all rendering.

**Spec:** `docs/superpowers/specs/2026-04-04-rendering-quality-design.md`

**Reference:** Original TS renderer at `d:/claude/min-rts/src/renderer/Sprites.ts` and `CanvasRenderer.ts`

---

### Task 1: SpriteFactory — pre-rendered block textures

**Files:**
- Create: `godot/Scripts/Rendering/SpriteFactory.cs`

This is a static utility class. No tests needed — visual output verified by running the game.

- [ ] **Step 1: Create SpriteFactory.cs**

```csharp
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
    private const float BlockInset = GridRenderer.BlockInset;
    private const float CellSize = GridRenderer.CellSize;

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
        // Custom sprites take priority
        LoadCustomSprites();
        if (_customSprites.TryGetValue(type, out var custom) && custom != null)
            return custom;
        // Procedural sprite (Jumper has no static body sprite)
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

    private static Image CreateBlockImage(Color baseColor, BlockType type, PlayerPalette palette)
    {
        int s = SpriteSize;
        var image = Image.CreateEmpty(s, s, false, Image.Format.Rgba8);

        // 1. Base fill
        for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                image.SetPixel(x, y, baseColor);

        // 2. Top gradient: white 0.30a -> 0a over top 40%
        int topH = (int)(s * 0.4f);
        for (int y = 0; y < topH; y++)
        {
            float t = (float)y / topH;
            float alpha = 0.30f * (1f - t);
            var overlay = new Color(1f, 1f, 1f, alpha);
            for (int x = 0; x < s; x++)
                image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
        }

        // 3. Left gradient: white 0.15a -> 0a over left 30%
        int leftW = (int)(s * 0.3f);
        for (int x = 0; x < leftW; x++)
        {
            float t = (float)x / leftW;
            float alpha = 0.15f * (1f - t);
            var overlay = new Color(1f, 1f, 1f, alpha);
            for (int y = 0; y < s; y++)
                image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
        }

        // 4. Bottom gradient: black 0a -> 0.25a over bottom 40%
        int botStart = s - (int)(s * 0.4f);
        for (int y = botStart; y < s; y++)
        {
            float t = (float)(y - botStart) / (s - botStart);
            float alpha = 0.25f * t;
            var overlay = new Color(0f, 0f, 0f, alpha);
            for (int x = 0; x < s; x++)
                image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), overlay));
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
            PaintWallStuds(image, s, palette);

        return image;
    }

    private static void PaintWallStuds(Image image, int s, PlayerPalette palette)
    {
        // 2x2 grid of beveled studs (matching TS Sprites.ts)
        float studSize = s * 0.25f;
        float studGap = s * 0.08f;
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

            // Top gradient on stud: white 0.22a -> 0 over top 40%
            int studTopH = (int)(h * 0.4f);
            for (int y = y0; y < y0 + studTopH && y < s; y++)
            {
                float t = (float)(y - y0) / studTopH;
                float alpha = 0.22f * (1f - t);
                for (int x = x0; x < x0 + w && x < s; x++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(1f, 1f, 1f, alpha)));
            }

            // Left gradient on stud: white 0.15a -> 0 over left 35%
            int studLeftW = (int)(w * 0.35f);
            for (int x = x0; x < x0 + studLeftW && x < s; x++)
            {
                float t = (float)(x - x0) / studLeftW;
                float alpha = 0.15f * (1f - t);
                for (int y = y0; y < y0 + h && y < s; y++)
                    image.SetPixel(x, y, BlendOver(image.GetPixel(x, y), new Color(1f, 1f, 1f, alpha)));
            }

            // Bottom gradient on stud: black 0 -> 0.25a over bottom 40%
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

    /// <summary>
    /// Alpha-composite overlay onto base (premultiplied-style "source over").
    /// </summary>
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
```

- [ ] **Step 2: Build the project**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/SpriteFactory.cs
git commit -m "feat: add SpriteFactory for pre-rendered block sprites and radial glow texture"
```

---

### Task 2: GlowLayer — additive blend node

**Files:**
- Create: `godot/Scripts/Rendering/GlowLayer.cs`

- [ ] **Step 1: Create GlowLayer.cs**

This node receives synced state from GridRenderer and draws all glow/emission effects with additive blending. It needs access to the same data structures GridRenderer uses.

```csharp
using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Additive-blend child of GridRenderer. Draws all glow/emission effects
/// (soldier arm glow, stunner aura, death bursts, rays, ZoC, ghost trails)
/// with CanvasItemMaterial.BlendMode = Add for luminous overlap.
/// </summary>
public partial class GlowLayer : Node2D
{
    private GameConfig _config = GameConfig.CreateDefault();
    private GameState? _gameState;
    private Dictionary<int, Vector2> _visualPositions = new();
    private Dictionary<int, float> _idleAngles = new();
    private List<GlowCommand> _commands = new();

    // Shared glow texture (created by SpriteFactory)
    private ImageTexture? _glowTex;

    /// <summary>
    /// Simple draw command — avoids passing complex state across nodes.
    /// GridRenderer enqueues glow draws during its _Draw(); GlowLayer replays them with additive blend.
    /// </summary>
    public record struct GlowCommand
    {
        public enum Kind { Line, Circle, Texture }
        public Kind Type;
        public Vector2 From, To;
        public Color Color;
        public float Width;
        public float Radius;
        public Rect2 Rect;
        public bool RoundCaps;

        public static GlowCommand MakeLine(Vector2 from, Vector2 to, Color color, float width, bool roundCaps = false)
            => new() { Type = Kind.Line, From = from, To = to, Color = color, Width = width, RoundCaps = roundCaps };

        public static GlowCommand MakeCircle(Vector2 center, float radius, Color color)
            => new() { Type = Kind.Circle, From = center, Radius = radius, Color = color };

        public static GlowCommand MakeTexture(Rect2 rect, Color color)
            => new() { Type = Kind.Texture, Rect = rect, Color = color };
    }

    public override void _Ready()
    {
        Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };
        _glowTex = SpriteFactory.GetRadialGlow();
    }

    /// <summary>
    /// Clear command buffer. Called by GridRenderer at the start of each _Draw().
    /// </summary>
    public void BeginFrame()
    {
        _commands.Clear();
    }

    /// <summary>
    /// Enqueue a glow draw command.
    /// </summary>
    public void Add(GlowCommand cmd)
    {
        _commands.Add(cmd);
    }

    /// <summary>
    /// Signal that GridRenderer is done enqueuing. Triggers redraw.
    /// </summary>
    public void EndFrame()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var cmd in _commands)
        {
            switch (cmd.Type)
            {
                case GlowCommand.Kind.Line:
                    DrawLine(cmd.From, cmd.To, cmd.Color, cmd.Width, true);
                    if (cmd.RoundCaps)
                    {
                        DrawCircle(cmd.From, cmd.Width * 0.5f, cmd.Color);
                        DrawCircle(cmd.To, cmd.Width * 0.5f, cmd.Color);
                    }
                    break;

                case GlowCommand.Kind.Circle:
                    DrawCircle(cmd.From, cmd.Radius, cmd.Color);
                    break;

                case GlowCommand.Kind.Texture:
                    if (_glowTex != null)
                        DrawTextureRect(_glowTex, cmd.Rect, false, cmd.Color);
                    break;
            }
        }
    }
}
```

- [ ] **Step 2: Build the project**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/GlowLayer.cs
git commit -m "feat: add GlowLayer with additive blend for luminous glow effects"
```

---

### Task 3: Wire GlowLayer and SpriteFactory into GridRenderer

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.cs`

- [ ] **Step 1: Add GlowLayer child creation and SpriteFactory initialization**

In `GridRenderer.cs`, add the GlowLayer field, `_Ready()` to create it, and wire up SpriteFactory in `SetConfig`:

Add a field and `_Ready()` method after the existing fields (around line 30):

```csharp
private GlowLayer? _glowLayer;

public override void _Ready()
{
    _glowLayer = new GlowLayer { Name = "GlowLayer" };
    AddChild(_glowLayer);
}
```

Update `SetConfig` to build sprites:

```csharp
public void SetConfig(GameConfig config)
{
    _config = config;
    SpriteFactory.Build(config);
}
```

- [ ] **Step 2: Add DrawRoundLine and DrawGlowRadial helpers**

Add after `GridToWorld`/`WorldToGrid` (around line 330):

```csharp
/// <summary>
/// Draw a line with round caps (circle at each endpoint). Used for effect/glow lines.
/// </summary>
private void DrawRoundLine(Vector2 from, Vector2 to, Color color, float width)
{
    DrawLine(from, to, color, width, true);
    DrawCircle(from, width * 0.5f, color);
    DrawCircle(to, width * 0.5f, color);
}

/// <summary>
/// Draw a smooth radial glow using the cached gradient texture.
/// Replaces layered DrawCircle calls with a single textured quad.
/// </summary>
private void DrawGlowRadial(Vector2 center, float radius, Color color)
{
    var tex = SpriteFactory.GetRadialGlow();
    var rect = new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2f);
    DrawTextureRect(tex, rect, false, color);
}

/// <summary>
/// Enqueue a glow line on the additive GlowLayer.
/// </summary>
private void QueueGlowLine(Vector2 from, Vector2 to, Color color, float width, bool roundCaps = false)
{
    _glowLayer?.Add(GlowLayer.GlowCommand.MakeLine(from, to, color, width, roundCaps));
}

/// <summary>
/// Enqueue a glow circle on the additive GlowLayer.
/// </summary>
private void QueueGlowCircle(Vector2 center, float radius, Color color)
{
    _glowLayer?.Add(GlowLayer.GlowCommand.MakeCircle(center, radius, color));
}

/// <summary>
/// Enqueue a radial glow texture on the additive GlowLayer.
/// </summary>
private void QueueGlowRadial(Vector2 center, float radius, Color color)
{
    _glowLayer?.Add(GlowLayer.GlowCommand.MakeTexture(
        new Rect2(center - Vector2.One * radius, Vector2.One * radius * 2f), color));
}
```

- [ ] **Step 3: Add GlowLayer frame sync to _Draw()**

At the top of `_Draw()`, after the null check, add:

```csharp
_glowLayer?.BeginFrame();
```

At the bottom of `_Draw()`, before closing brace, add:

```csharp
_glowLayer?.EndFrame();
```

- [ ] **Step 4: Remove old GetBlockSprite method and sprite cache**

In `GridRenderer.Blocks.cs`, remove the `_blockSprites` dictionary, `_spritesLoaded` field, and `GetBlockSprite` method (lines 344-364). These are replaced by SpriteFactory.

- [ ] **Step 5: Build the project**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds with no errors.

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.cs godot/Scripts/Rendering/GridRenderer.Blocks.cs
git commit -m "feat: wire GlowLayer and SpriteFactory into GridRenderer, add draw helpers"
```

---

### Task 4: Update block body rendering to use pre-rendered sprites

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs`

The key change: `DrawBlockTypeIndicator` now uses SpriteFactory sprites for the block body, then draws animated overlays on top. Custom sprites also get animated overlays instead of the current "skip everything" approach.

- [ ] **Step 1: Rewrite DrawBlockTypeIndicator**

Replace the existing `DrawBlockTypeIndicator` method (around line 366) with:

```csharp
private void DrawBlockTypeIndicator(Block block, Rect2 rect, Color color)
{
    var center = rect.GetCenter();
    float time = (float)Time.GetTicksMsec() / 1000f;
    float idleAngle = _idleAngles.GetValueOrDefault(block.Id, 0f);
    var palette = _config.GetPalette(block.PlayerId);

    switch (block.Type)
    {
        case BlockType.Wall:
        {
            // Wall: use sprite, no animation
            var sprite = SpriteFactory.GetSprite(BlockType.Wall, block.PlayerId);
            if (sprite != null)
                DrawTextureRect(sprite, rect, false);
            else
                DrawWallBlock(rect, palette);
            break;
        }

        case BlockType.Builder:
        {
            // Builder: the whole body rotates, so we draw the sprite rotated
            DrawBuilderBody(rect, center, palette, idleAngle);
            break;
        }

        case BlockType.Soldier:
        {
            // Soldier: sprite base + animated sword arms
            var sprite = SpriteFactory.GetSprite(BlockType.Soldier, block.PlayerId);
            if (sprite != null)
                DrawTextureRect(sprite, rect, false);
            else
                DrawSmoothGradientBody(rect, palette.SoldierFill, palette.SoldierFill.Lightened(0.2f), palette.SoldierFill.Darkened(0.2f));
            DrawSoldierAnimated(block, rect, center, palette, idleAngle);
            break;
        }

        case BlockType.Stunner:
        {
            // Stunner: sprite base + animated diamond overlay
            var sprite = SpriteFactory.GetSprite(BlockType.Stunner, block.PlayerId);
            if (sprite != null)
                DrawTextureRect(sprite, rect, false);
            else
                DrawSmoothGradientBody(rect, palette.StunnerFill, palette.StunnerBevelLight, palette.StunnerBevelShadow);
            DrawStunnerAnimated(rect, center, palette, idleAngle);
            break;
        }

        case BlockType.Warden:
        {
            // Warden: sprite base + animated shield
            var sprite = SpriteFactory.GetSprite(BlockType.Warden, block.PlayerId);
            if (sprite != null)
                DrawTextureRect(sprite, rect, false);
            else
                DrawSmoothGradientBody(rect, palette.BuilderFill, palette.BuilderGradientLight, palette.BuilderGradientDark);
            DrawWardenAnimated(rect, center, palette, time);
            break;
        }

        case BlockType.Jumper:
            // Jumper: fully procedural (lava sphere), no sprite
            DrawJumperAnimated(block, rect, center, palette, time);
            break;
    }
}
```

- [ ] **Step 2: Update DrawBuilderBody to use sprite when not rotating**

The builder rotates its whole body, so we need the sprite drawn with rotation. Add `int playerId` parameter and use `DrawSetTransform` for rotation. Replace `DrawBuilderBody`:

```csharp
private void DrawBuilderBody(Rect2 rect, Vector2 center, PlayerPalette palette, float idleAngle, int playerId)
{
    float quarterTurn = Mathf.Pi * 0.5f;
    float rev = idleAngle / quarterTurn;
    float frac = rev - Mathf.Floor(rev);
    float ease = frac < 0.5f ? 4 * frac * frac * frac : 1 - Mathf.Pow(-2 * frac + 2, 3) / 2;
    float angle = (Mathf.Floor(rev) + ease) * quarterTurn;

    var sprite = SpriteFactory.GetSprite(BlockType.Builder, playerId);

    if (sprite != null)
    {
        // Draw rotated sprite using canvas transform
        DrawSetTransform(center, angle);
        var halfSize = rect.Size * 0.5f;
        DrawTextureRect(sprite, new Rect2(-halfSize, rect.Size), false);
        DrawSetTransform(Vector2.Zero, 0); // reset
    }
    else
    {
        // Fallback: vertex-color polygon (old approach)
        float hx = rect.Size.X * 0.5f;
        float hy = rect.Size.Y * 0.5f;
        float cos = Mathf.Cos(angle);
        float sin = Mathf.Sin(angle);
        Vector2[] offsets = { new(-hx, -hy), new(hx, -hy), new(hx, hy), new(-hx, hy) };
        var pts = new Vector2[4];
        for (int i = 0; i < 4; i++)
        {
            var o = offsets[i];
            pts[i] = center + new Vector2(o.X * cos - o.Y * sin, o.X * sin + o.Y * cos);
        }
        DrawPolygon(pts, new Color[]
        {
            palette.BuilderGradientLight, palette.BuilderFill,
            palette.BuilderGradientDark, palette.BuilderFill
        });
    }
}
```

Also update the call site in `DrawBlockTypeIndicator` for Builder:
```csharp
case BlockType.Builder:
    DrawBuilderBody(rect, center, palette, idleAngle, block.PlayerId);
    break;
```

- [ ] **Step 3: Update DrawWardenAnimated to only draw the shield overlay (not the body)**

The body is now drawn by the sprite in `DrawBlockTypeIndicator`. Remove the first line of `DrawWardenAnimated` that draws the gradient body:

Remove this line from `DrawWardenAnimated`:
```csharp
DrawSmoothGradientBody(rect, palette.BuilderFill, palette.BuilderGradientLight, palette.BuilderGradientDark);
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Blocks.cs
git commit -m "feat: use pre-rendered sprites for block bodies, animated overlays draw on top"
```

---

### Task 5: Move glow effects to GlowLayer + update DrawGlow/DrawCircleGlow

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs`

- [ ] **Step 1: Replace DrawGlow with tuned multi-pass using GlowLayer**

Replace the existing `DrawGlow` method with the TS-tuned 4-pass version that enqueues to GlowLayer:

```csharp
/// <summary>
/// Multi-pass glow effect, tuned to match TS prototype.
/// Enqueued on additive GlowLayer for proper luminous overlap.
/// 4 passes: outer bloom -> inner halo -> colored core -> white-hot tip.
/// </summary>
private void DrawGlow(Vector2 from, Vector2 to, Color color, float baseWidth, int layers = 4)
{
    // Pass 1: outermost bloom
    QueueGlowLine(from, to, color with { A = 0.06f }, baseWidth * 3.6f, true);
    // Pass 2: inner halo
    QueueGlowLine(from, to, color with { A = 0.15f }, baseWidth * 2.0f, true);
    // Pass 3: colored core
    QueueGlowLine(from, to, color with { A = 0.80f }, baseWidth * 0.72f, true);
    // Pass 4: white-hot tip
    QueueGlowLine(from, to, Colors.White with { A = 0.60f }, baseWidth * 0.4f, true);
}
```

- [ ] **Step 2: Replace DrawCircleGlow with radial texture on GlowLayer**

Replace the existing `DrawCircleGlow` method:

```csharp
/// <summary>
/// Smooth radial glow using gradient texture on additive GlowLayer.
/// </summary>
private void DrawCircleGlow(Vector2 center, float radius, Color color, int layers = 4)
{
    // Radial gradient on glow layer — smooth falloff, single draw call
    QueueGlowRadial(center, radius * 2.5f, color);
}
```

- [ ] **Step 3: Move ghost trail glow to GlowLayer**

In `DrawGhostTrails`, the outer ghost circles should go to GlowLayer. Replace the two `DrawCircle` calls with:

```csharp
// Ghost circle — outer glow on additive layer, inner on main
QueueGlowRadial(ghost.Pos, radius * 1.5f, ghost.Color with { A = alpha * 0.4f });
DrawCircle(ghost.Pos, radius * 0.5f, ghost.Color with { A = alpha });
```

- [ ] **Step 4: Move jumper heat glow and specular to GlowLayer**

In `DrawJumperAnimated`, the outer heat glow line at the bottom should use `QueueGlowRadial` instead of `DrawCircleGlow`:

Replace the last two lines of `DrawJumperAnimated`:
```csharp
// Outer heat glow — additive layer
QueueGlowRadial(center, radius * 1.8f, palette.JumperPulseGlow with { A = 0.08f + 0.04f * pulse1 });
```

- [ ] **Step 5: Move ray and push wave cell fills to GlowLayer**

In `GridRenderer.Effects.cs`, in `DrawRays`, replace `DrawRect` calls for cell fills with `QueueGlowCircle` or keep as `DrawRect` but route through glow layer. Actually, rays and push waves use cell-fill rectangles which work fine on the main layer — the additive layer is most impactful on point/line glows. Leave these on the main layer for now.

- [ ] **Step 6: Move Warden ZoC pulse cells to GlowLayer**

In `DrawWardenZoC`, replace the `DrawRect` cell fill with glow layer:

```csharp
// Replace: DrawRect(cellRect, playerColor with { A = alpha });
// With:
QueueGlowCircle(cellRect.GetCenter(), CellSize * 0.35f, playerColor with { A = alpha });
```

This gives the ZoC a softer, more radiant look instead of sharp cell rectangles.

- [ ] **Step 7: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds.

- [ ] **Step 8: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Blocks.cs godot/Scripts/Rendering/GridRenderer.Effects.cs
git commit -m "feat: route glow effects through additive GlowLayer, tune multi-pass parameters"
```

---

### Task 6: Apply round line caps and anti-aliasing

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Effects.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Formations.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.Selection.cs`

This task applies `DrawRoundLine` to effect/indicator lines and adds `antialiased: true` to visible DrawLine calls wider than 1px. Does NOT touch grid lines or 1px structural edges.

- [ ] **Step 1: GridRenderer.Blocks.cs — apply round caps and AA**

Lines to update (use `DrawRoundLine` instead of `DrawLine`):

**DrawSoldierAnimated** — soldier sword arms (solid core line):
```csharp
// Replace: DrawLine(center, tip, gold, 2.5f);
DrawRoundLine(center, tip, gold, 2.5f);
```

**DrawOutlineTracer** — rooting animation tracer:
```csharp
// Replace: DrawLine(from.Lerp(to, t0), from.Lerp(to, t1), color, width);
DrawLine(from.Lerp(to, t0), from.Lerp(to, t1), color, width, true);
```
(No round caps for tracers — just AA. The tracer is a continuous segment.)

**DrawTendril** — root tendril segments:
```csharp
// Both DrawLine calls — add AA:
DrawLine(p0, p1, color with { A = alpha * 0.3f }, width + 2f, true);
DrawLine(p0, p1, color with { A = alpha }, width, true);
```

**DrawFrozenOverlay** — frost crack lines:
```csharp
// Add AA to the crack DrawLine calls:
if (frame != 0) DrawLine(midTop, midTop + new Vector2(0, crackLen), crackColor, 1f, true);
if (frame != 1) DrawLine(midRight, midRight + new Vector2(-crackLen, 0), crackColor, 1f, true);
if (frame != 2) DrawLine(midBottom, midBottom + new Vector2(0, -crackLen), crackColor, 1f, true);
if (frame != 3) DrawLine(midLeft, midLeft + new Vector2(crackLen, 0), crackColor, 1f, true);
```

**DrawThreatIndicators** — red corner threat marks:
```csharp
// All 12 DrawLine calls for threat corners — use DrawRoundLine:
// Example for TL corner (repeat pattern for TR, BR):
DrawRoundLine(tl, tl + new Vector2(cornerLen, 0), red, 2.5f);
DrawRoundLine(tl, tl + new Vector2(0, cornerLen), red, 2.5f);
DrawRoundLine(tl, tl + new Vector2(cornerLen, 0), glowRed, 5f);
DrawRoundLine(tl, tl + new Vector2(0, cornerLen), glowRed, 5f);
```
(Apply same pattern to all soldiers >= 1/2/3 blocks.)

**DrawWallBlock** — inner bevel lines:
```csharp
// Add AA to bevel lines (1.5f width):
DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), palette.WallHighlight, 1.5f, true);
DrawLine(rect.Position, new Vector2(rect.Position.X, rect.End.Y), palette.WallHighlight, 1.5f, true);
DrawLine(rect.End, new Vector2(rect.End.X, rect.Position.Y), palette.WallShadow, 1.5f, true);
DrawLine(rect.End, new Vector2(rect.Position.X, rect.End.Y), palette.WallShadow, 1.5f, true);
```

**DrawStunnerAnimated** — diamond bevel edges:
```csharp
// All 4 DrawLine calls for the bevel — use DrawRoundLine:
DrawRoundLine(pts[0], pts[1], palette.StunnerBevelLight with { A = 0.85f * pulse }, 2.5f);
DrawRoundLine(pts[3], pts[0], palette.StunnerBevelLight with { A = 0.7f * pulse }, 2.5f);
DrawRoundLine(pts[1], pts[2], palette.StunnerBevelShadow with { A = 0.85f * pulse }, 2.5f);
DrawRoundLine(pts[2], pts[3], palette.StunnerBevelShadow with { A = 0.7f * pulse }, 2.5f);
```

**DrawWardenAnimated** — shield outline + cross:
```csharp
// Shield outline — AA only (round caps would look wrong on connected edges):
DrawLine(outer[0], outer[1], lightEdge, 2f, true);
DrawLine(outer[6], outer[0], lightEdge, 1.5f, true);
DrawLine(outer[1], outer[2], lightEdge, 1.5f, true);
DrawLine(outer[2], outer[3], darkEdge, 1.5f, true);
DrawLine(outer[5], outer[6], darkEdge, 1.5f, true);
DrawLine(outer[3], outer[4], darkEdge, 1.5f, true);
DrawLine(outer[4], outer[5], darkEdge, 1.5f, true);
// Cross — use round caps:
DrawRoundLine(center + new Vector2(0, -shieldH * 0.28f), center + new Vector2(0, shieldH * 0.28f), crossColor, 1.5f);
DrawRoundLine(center + new Vector2(-shieldW * 0.45f, -shieldH * 0.04f), center + new Vector2(shieldW * 0.45f, -shieldH * 0.04f), crossColor, 1.5f);
```

**DrawChevron** — chevron arms:
```csharp
// Replace both DrawLine calls:
DrawRoundLine(arm1, tip, color, 2f);
DrawRoundLine(arm2, tip, color, 2f);
```

**DrawOuterCornerTicks** — formation corner ticks:
```csharp
// All 8 DrawLine calls — use DrawRoundLine for effect lines:
DrawRoundLine(tl, tl + new Vector2(-len, 0), color, width);
DrawRoundLine(tl, tl + new Vector2(0, -len), color, width);
// ... (same for tr, bl, br — all 8 lines)
```

- [ ] **Step 2: GridRenderer.Effects.cs — apply AA to effect lines**

**DrawGhostTrails** — motion blur lines:
```csharp
// Add AA to the blur DrawLine calls:
DrawLine(ghost.Pos - new Vector2(blurLen, 0), ghost.Pos + new Vector2(blurLen, 0),
    ghost.Color with { A = alpha * 0.5f }, 3f, true);
DrawLine(ghost.Pos - new Vector2(0, blurLen), ghost.Pos + new Vector2(0, blurLen),
    ghost.Color with { A = alpha * 0.5f }, 3f, true);
```

**DrawTerrainWallBlock** — wall bevel lines:
```csharp
// Add AA to bevel lines (same pattern as DrawWallBlock):
DrawLine(rect.Position, new Vector2(rect.End.X, rect.Position.Y), highlight, 1.5f, true);
// ... (all 4 bevel lines)
```

- [ ] **Step 3: GridRenderer.Formations.cs — apply AA to formation outlines**

**DrawFormationBlock** — formation outline rect:
```csharp
// The DrawRect calls with false (outline mode) already use Godot's built-in AA.
// Just add AA to corner tick DrawLine calls via DrawOuterCornerTicks (already updated in Step 1).
```

- [ ] **Step 4: GridRenderer.Selection.cs — apply AA to selection dashes**

**DrawDashedLine** — selection border dashes:
```csharp
// Add AA to the DrawLine call:
DrawLine(from + dir * pos, from + dir * (pos + segLen), color, lineWidth, true);
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Blocks.cs godot/Scripts/Rendering/GridRenderer.Effects.cs godot/Scripts/Rendering/GridRenderer.Formations.cs godot/Scripts/Rendering/GridRenderer.Selection.cs
git commit -m "feat: apply round line caps and anti-aliasing to effect/indicator lines"
```

---

### Task 7: Final integration — verify everything works together

**Files:**
- All rendering files from previous tasks

- [ ] **Step 1: Full build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeds with no errors or warnings.

- [ ] **Step 2: Run simulation tests (ensure no regressions)**

Run: `dotnet test`
Expected: All tests pass. (Rendering changes shouldn't affect simulation.)

- [ ] **Step 3: Visual verification checklist**

Open the game in Godot and verify:
- Builder blocks show smooth directional gradients (not flat vertex interpolation)
- Soldier sword arm glows are luminous and combine when overlapping
- Stunner diamond has soft radial aura
- Jumper lava sphere has smooth heat glow
- Wall blocks have beveled studs with gradient shading
- Death effects produce bright additive bursts
- Ghost trails have smooth radial fade
- Line endpoints (arms, cracks, chevrons) have soft round caps, not hard squares
- Warden ZoC pulse uses soft radial circles instead of sharp cell rectangles
- Selection borders have anti-aliased dashes
- No FPS drop with 50+ blocks on screen
- Custom sprites (if any in Assets/Sprites/Blocks/) still load and get animated overlays

- [ ] **Step 4: Commit any final adjustments**

If visual tuning is needed (alpha values, glow sizes, etc.), make adjustments and commit:

```bash
git add -A
git commit -m "fix: tune rendering quality parameters after visual review"
```
