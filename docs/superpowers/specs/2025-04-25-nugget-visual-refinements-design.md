# Nugget Visual Refinements — Design Spec

## Overview

Five visual refinements to the nugget block system: shinier sparkles on unmined nuggets, HP-tracking diamond formation on fortified walls, visible nest refine zones, and a shift from CPU-drawn animations to GPU shaders for performance.

## 1. Nugget Overlay Shader (`nugget_overlay.gdshader`)

A single full-grid `canvas_item` shader that handles all stationary nugget-related sparkle effects. One `ColorRect` covering the grid, one draw call.

### Data Texture

An `RGBA8` texture (`nugget_data`) the same dimensions as the grid, updated once per simulation tick:

| Channel | Meaning |
|---------|---------|
| R | Cell type: `0` = empty, `1` = unmined nugget, `2` = fortified wall |
| G | For nuggets: mining progress (0–255). For walls: `FortifiedHp` (1–3) |
| B | Block ID low byte (phase offset for animation variation) |
| A | Reserved (0) |

### Unmined Nugget Sparkle (R == 1)

- **Diamond shape**: 4-point diamond at cell center, 30% of cell size. Rendered as distance-field in shader (Manhattan distance `abs(dx) + abs(dy) < size`).
- **Fill**: Prismatic hue shift — base hue 0.55 (cyan), oscillates ±0.08 using `sin(TIME * 1.2 + phase)`.
- **Sparkle stars**: 4 four-pointed star bursts per nugget cell, each tinted differently:
  - Star 0: `#aaddff` (ice blue)
  - Star 1: `#ffd4ff` (pink)
  - Star 2: `#d4ffdd` (green)
  - Star 3: `#ffffff` (white)
- Each star is a cross pattern (`abs(dx) < w && abs(dy) < len || abs(dy) < w && abs(dx) < len`) with pseudo-random position driven by `sin/cos(TIME * freq + phase * offset)`.
- **Star alpha**: `max(0, sin(TIME * 1.5 + i * 2.1 + phase)) * 0.9` — brighter than current 0.7.
- **Ambient glow**: Radial falloff from cell center, cool white at ~20% alpha (up from 15%).
- **Diamond stroke**: Faint outline, 0.8 alpha, rendered as distance-field edge detection.
- **Mining progress** (G > 0 while R == 1): Crack lines grow outward from center proportional to progress. Glow intensifies.

### Fortified Wall Diamonds (R == 2)

Diamond formation changes based on HP (G channel):

- **3 HP**: Inverted triangle (▽) — 2 diamonds top, 1 bottom. Each diamond is 15% of cell size.
  - Top-left offset: `(-0.18, -0.12)` relative to cell center (in cell-size units)
  - Top-right offset: `(+0.18, -0.12)`
  - Bottom-center offset: `(0, +0.15)`
- **2 HP**: Vertical pair, center-aligned.
  - Top offset: `(0, -0.12)`
  - Bottom offset: `(0, +0.12)`
- **1 HP**: Single diamond, centered.

Each diamond has:
- Light blue fill `(0.7, 0.85, 1.0)` with pulsing opacity `0.4 + 0.3 * sin(TIME * 2.5 + phase)`.
- White stroke at 60% of fill alpha.
- Prismatic sparkle: one 4-pointed star per diamond, same style as nugget sparkles but smaller.

### Shader Uniforms

```glsl
uniform sampler2D nugget_data : filter_nearest, repeat_disable;
uniform ivec2 grid_size;
uniform float cell_size;
uniform float grid_padding;
```

### Integration

- `GridRenderer` creates one `ColorRect` with this shader, positioned at grid padding, sized to grid pixel dimensions.
- Data texture updated in `GridRenderer._Process()` by scanning blocks for nuggets and fortified walls.
- Shader uses `blend_add` render mode for additive sparkle over existing block rendering.

## 2. Mined Nugget Sparkle (CPU-drawn)

Mined nuggets move between cells and are consumed quickly. Keep CPU drawing in `DrawNuggetDiamond()`:

- Team-colored diamond (existing).
- Add 2 small 4-pointed star bursts (DrawLine crosses) near diamond vertices, team-colored with white tips.
- Subtle pulse on diamond fill alpha: `0.6 + 0.15 * sin(TIME * 2 + blockId)`.

## 3. Nest Refine Zone

### Config Change

`SimulationConfig.NuggetConfig.RefineRadius`: `3` → `2`. This makes the zone a 5×5 Chebyshev square around the nest center.

### Visualization

Reuse `grid_rings.gdshader` in square-ring mode (mode 0) with `base_alpha` for persistent grid-line glow:

- One `ColorRect` per nest, same lifecycle pattern as `UpdateWardenZoC()`.
- New method `UpdateNestRefineZones()` in `GridRenderer.Effects.cs`.
- Dictionary `_nestRefineRects` tracking `ColorRect` per nest ID.

Shader parameters:
- `mode`: `0` (square rings)
- `max_radius`: `3.0` (radius 2 + 1 buffer)
- `trail`: `1.5`
- `ring_color`: neutral cool white `(0.8, 0.85, 0.95, 0.6)` — not team-colored, since the zone is a property of the nest position, not ownership
- `base_alpha`: `0.04`
- `loop_mode`: `true`
- `fade_mult`: `0.6`
- Wave cycle: `3000ms` (slower than warden's 2500ms for visual distinction)

Nest center updates each frame to track position (nests are static, but for consistency with warden pattern).

## 4. Removed CPU Drawing

Once the shader is in place, remove from `GridRenderer.Blocks.cs`:
- Sparkle dots loop (lines 854–865) in `DrawNuggetDiamond()` for unmined nuggets.
- `QueueGlowRadial` call for unmined nuggets (line 852).
- `DrawFortifiedWallOverlay()` method entirely — replaced by shader.
- The diamond shape and stroke for unmined nuggets (lines 830–874) — replaced by shader distance field.

Keep for unmined nuggets: mining vibration offset, crack lines during mining, and mining glow intensification (these interact with block-level state that's easier to keep CPU-side for now).

Actually, since the shader receives mining progress in the G channel, the crack lines and mining glow can also move to the shader. The mining vibration (shake offset) is the only thing that needs CPU positioning — but since the shader draws at the cell center, not an offset position, we can skip the vibration in the shader and keep it as a CPU-drawn overlay only during active mining. This simplifies to:

- **Shader draws**: diamond, sparkles, glow, cracks, fortified diamonds — for all nuggets and fortified walls.
- **CPU draws**: mining vibration offset (re-draws a shaking diamond over the shader one during mining only). This is a minor overlay on the few blocks being actively mined.

## 5. Performance Characteristics

- **Nugget overlay shader**: One draw call, one texture upload per tick. GPU computes all sparkles for all nuggets + fortified walls. No per-block CPU cost per frame.
- **Nest refine zones**: One `ColorRect` + shader per nest (typically 2–6 nests per game). Same proven pattern as warden ZoC.
- **Mined nugget CPU drawing**: Few blocks (typically 0–5 at any time), simple DrawLine calls. Negligible.
- **Data texture update**: One pass over blocks per tick to write RGBA values. O(n) where n = number of blocks, but only writes nuggets and fortified walls.

## Files Changed

| File | Change |
|------|--------|
| `godot/Assets/Shaders/nugget_overlay.gdshader` | New shader |
| `godot/Scripts/Rendering/GridRenderer.cs` | Data texture management, ColorRect setup |
| `godot/Scripts/Rendering/GridRenderer.Blocks.cs` | Remove CPU sparkle/glow, add mined nugget stars, remove `DrawFortifiedWallOverlay` |
| `godot/Scripts/Rendering/GridRenderer.Effects.cs` | Add `UpdateNestRefineZones()` |
| `src/Blocker.Simulation/Core/SimulationConfig.cs` | `RefineRadius` 3 → 2 |
| `docs/game-bible.md` | Update refine radius, fortified wall diamond description |
