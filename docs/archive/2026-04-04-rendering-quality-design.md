# Rendering Quality Improvements

## Context

The Godot port's visuals fall short of the original TypeScript + HTML Canvas prototype. Root causes identified by comparing `d:/claude/min-rts/src/renderer/` against `godot/Scripts/Rendering/`:

1. **No additive blending** — Canvas uses `globalCompositeOperation = "lighter"` for luminous glow overlap. Godot's `_Draw()` only does normal alpha blend, making glows look flat.
2. **No smooth radial gradients** — Canvas has `createRadialGradient()`. Godot's `DrawCircle` is flat color, so glows are faked with layered circles that show visible stepping.
3. **No round line caps** — Canvas uses `lineCap = "round"` for soft line endpoints. Godot's `DrawLine` has hard square caps only.
4. **Coarser block gradients** — The TS version pre-renders sprites with 4 stacked directional linear gradients (top highlight, left highlight, bottom shadow, depth edges). Godot uses `DrawPolygon` with 4 vertex colors, which only interpolates corner-to-corner.
5. **No anti-aliasing on lines** — Godot's `DrawLine` has an `antialiased` parameter that defaults to `false`. None of the 60+ DrawLine calls enable it.
6. **Generic glow parameters** — The TS GridEffects uses carefully tuned 4-pass rendering (9px/0.06a, 5px/0.15a, 1.8px/0.8a, 1px/0.6a). Godot's `DrawGlow` uses generic falloff.

## Design

### 1. GlowLayer (additive blend child node)

New file: `godot/Scripts/Rendering/GlowLayer.cs`

A `Node2D` child of `GridRenderer` with `CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }`. GridRenderer creates it in `_Ready()`.

**Draws moved to GlowLayer:**
- Soldier arm glow passes (the wide/faint layers from `DrawGlow`)
- Soldier center dot glow (`DrawCircleGlow`)
- Stunner diamond glow aura
- Jumper outer heat glow + specular highlight
- Warden shield glow aura
- Death effect radial glow (the expanding circle in phase 2)
- Ghost trail circles
- Warden ZoC pulse cells
- Ray cell fills (stun + blast)
- Push wave cell fills

**Stays on GridRenderer:**
- Cell backgrounds, grid lines
- Block body sprites (opaque)
- Animated overlays (soldier arms solid core, stunner diamond solid, etc.)
- Selection borders, rooting visuals, threat indicators, formation outlines
- Chevrons, frost cracks, diagonal stripes

**Data flow:** GridRenderer calls `GlowLayer.Sync(gameState, visualPositions, idleAngles, selectedIds, ghostTrails, deathEffects, fragments, config, tickInterval)` in `_Process()` before `QueueRedraw()`. GlowLayer stores references and calls its own `QueueRedraw()`.

### 2. SpriteFactory (pre-rendered block textures)

New file: `godot/Scripts/Rendering/SpriteFactory.cs`

Static class. Called once when `GameConfig` is set on GridRenderer.

For each `BlockType` that has a static body (Builder, Soldier, Stunner, Wall) x each player palette, generates an `ImageTexture` matching the TS sprite recipe:

1. Base fill (player color for that block type)
2. Top gradient: white 0.30a -> 0a over top 40% height
3. Left gradient: white 0.15a -> 0a over left 30% width
4. Bottom gradient: black 0a -> 0.25a over bottom 40% height
5. Depth edges: black 0.4a on right 1px and bottom 1px

Wall sprites additionally get the 2x2 beveled stud pattern.

Rendered at 2x resolution (46x46 pixels for a 23px block) for subpixel quality. Stored in `Dictionary<(BlockType, int playerId), ImageTexture>`. Drawn with `DrawTextureRect`.

**Custom sprite integration:** `SpriteFactory.GetSprite(type, playerId)` first checks for a user-provided sprite at `res://Assets/Sprites/Blocks/{Type}.png`. If found, returns that (tinted with player color via `DrawTextureRect` modulate). Otherwise returns the pre-rendered procedural texture. This replaces the current `GetBlockSprite` method and its separate code path — no more "if sprite exists, skip all drawing." Animated overlays (arms, diamonds, shield icons, lava sphere) always draw on top regardless of sprite source.

**Block types and their layers:**
- **Builder:** sprite only (rotation handles the animation, sprite is the body)
- **Soldier:** sprite base + animated sword arms on top
- **Stunner:** sprite base + animated diamond overlay on top
- **Warden:** sprite base + animated shield icon on top
- **Wall:** sprite only (static)
- **Jumper:** no sprite (lava sphere is entirely procedural/animated)

### 3. DrawRoundLine helper

Added to `GridRenderer` as a private method:

```csharp
private void DrawRoundLine(Vector2 from, Vector2 to, Color color, float width, bool antialiased = true)
{
    DrawLine(from, to, color, width, antialiased);
    DrawCircle(from, width * 0.5f, color);
    DrawCircle(to, width * 0.5f, color);
}
```

Used on effect/glow lines only: soldier arms, glow passes, tendrils, frost cracks, threat indicator corners, chevron arms, formation diamond edges. Not used on grid lines, cell borders, or structural 1px edges.

Also added to GlowLayer for its glow line draws.

### 4. Radial glow texture

Cached `ImageTexture` created once at startup (64x64, white center -> transparent edge with Gaussian falloff). Stored on SpriteFactory or GridRenderer.

Replaces `DrawCircleGlow` — instead of 4-5 layered flat `DrawCircle` calls, a single `DrawTextureRect` with appropriate size and color modulation.

The Gaussian falloff formula per pixel: `alpha = exp(-3.0 * dist_squared)` where dist is normalized 0..1 from center to edge.

### 5. Anti-aliased lines

Add `antialiased: true` to all `DrawLine` calls on lines wider than 1px or on effect/indicator lines. Skip on 1px grid lines and thin structural edges where AA overhead isn't justified.

### 6. Multi-pass glow tuning

Update `DrawGlow` to match the TS-proven 4-pass parameters:

| Pass | Width | Alpha multiplier | Color |
|------|-------|-------------------|-------|
| 1 (outermost) | base * 3.6 | 0.06 | effect color |
| 2 | base * 2.0 | 0.15 | effect color |
| 3 (core) | base * 0.72 | 0.80 | effect color |
| 4 (hot tip) | base * 0.4 | 0.60 | white |

All glow passes use `DrawRoundLine` and draw on the GlowLayer (additive blend).

## Performance Budget

Target: 60 FPS with 200+ blocks on screen, no frame spikes.

- **SpriteFactory:** One-time cost at startup (~10ms for all sprites). Zero per-frame allocation.
- **Radial glow texture:** One-time 64x64 image creation. Each glow is 1 `DrawTextureRect` vs previous 4-5 `DrawCircle` = net draw call reduction.
- **GlowLayer:** Adds one `_Draw()` call per frame but moves draws out of GridRenderer — total draw count stays similar. Additive blend is a GPU-side operation, essentially free.
- **DrawRoundLine:** 2 extra `DrawCircle` calls per effect line. At typical ~40-80 effect lines on screen, that's ~80-160 tiny circles — negligible.
- **AA lines:** Godot's built-in AA adds ~10-20% cost per line. Only applied to visible effect lines, not the hundreds of grid lines.

## Files Changed

- **New:** `godot/Scripts/Rendering/GlowLayer.cs`
- **New:** `godot/Scripts/Rendering/SpriteFactory.cs`
- **Modified:** `godot/Scripts/Rendering/GridRenderer.cs` — create GlowLayer child, use SpriteFactory, add DrawRoundLine helper
- **Modified:** `godot/Scripts/Rendering/GridRenderer.Blocks.cs` — use pre-rendered sprites for block bodies, split animated overlays from body rendering, update glow parameters, apply AA + round caps
- **Modified:** `godot/Scripts/Rendering/GridRenderer.Effects.cs` — move glow draws to GlowLayer, apply round caps, tune multi-pass parameters
- **Modified:** `godot/Scripts/Rendering/GridRenderer.Formations.cs` — apply AA + round caps where applicable
- **Modified:** `godot/Scripts/Rendering/GridRenderer.Selection.cs` — apply AA to selection border
