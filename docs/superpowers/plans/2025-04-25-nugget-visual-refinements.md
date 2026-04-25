# Nugget Visual Refinements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace CPU-drawn nugget sparkles and fortified wall diamonds with a GPU shader overlay, add nest refine zone visualization, and reduce refine radius from 3 to 2.

**Architecture:** A new `nugget_overlay.gdshader` handles all unmined nugget sparkles and fortified wall diamond sparkles in a single draw call via a data texture. Nest refine zones reuse `grid_rings.gdshader` in square-ring mode. Mined nuggets keep CPU drawing (few, transient, moving).

**Tech Stack:** Godot 4 shaders (GLSL), C# (GridRenderer partials), xUnit tests

**Spec:** `docs/superpowers/specs/2025-04-25-nugget-visual-refinements-design.md`

---

### Task 1: Change RefineRadius from 3 to 2

**Files:**
- Modify: `src/Blocker.Simulation/Core/SimulationConfig.cs:73`
- Modify: `docs/game-bible.md` (nugget consumption description)

- [ ] **Step 1: Update SimulationConfig**

In `src/Blocker.Simulation/Core/SimulationConfig.cs`, change line 73:

```csharp
// Before:
public int RefineRadius { get; init; } = 3;

// After:
public int RefineRadius { get; init; } = 2;
```

- [ ] **Step 2: Update game-bible.md**

Find the line (around line 162):
```
  - *Nest Refine*: Within 3 Chebyshev distance of friendly nest → auto-consumed
```
Change `3` to `2`.

Also find the duplicate section (around line 188-192) and update any references to refine distance there.

Update the fortified wall description (around line 168-169) to mention the diamond formation:
```
- `FortifiedHp` absorbs stun ray hits (decrements instead of destroying). Visual: inverted triangle of 3 diamonds at full HP, vertical pair at 2 HP, single diamond at 1 HP. When 0, wall is normal again.
```

- [ ] **Step 3: Run tests to verify nothing breaks**

Run: `dotnet test`
Expected: All tests pass — existing test at distance 2 is still within the new radius.

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Simulation/Core/SimulationConfig.cs docs/game-bible.md
git commit -m "feat: reduce nugget refine radius from 3 to 2, update docs"
```

---

### Task 2: Create the nugget overlay shader

**Files:**
- Create: `godot/Assets/Shaders/nugget_overlay.gdshader`

- [ ] **Step 1: Write the shader**

Create `godot/Assets/Shaders/nugget_overlay.gdshader`:

```glsl
shader_type canvas_item;
render_mode blend_add;

// Data texture: R = cell type (0=empty, 1=unmined nugget, 2=fortified wall)
//               G = mining progress (nuggets) or FortifiedHp (walls, 1-3)
//               B = block ID low byte (phase offset)
uniform sampler2D nugget_data : filter_nearest, repeat_disable;
uniform ivec2 grid_size;
uniform float cell_size = 28.0;
uniform float grid_padding = 140.0;

// Diamond distance field: returns 0 at center, 1 at diamond edge
float diamond_df(vec2 p, vec2 center, float size) {
    vec2 d = abs(p - center);
    return (d.x + d.y) / size;
}

// 4-pointed star sparkle — cross pattern
float star4(vec2 p, vec2 center, float arm_len, float arm_w) {
    vec2 d = abs(p - center);
    float h = step(d.y, arm_w) * step(d.x, arm_len);
    float v = step(d.x, arm_w) * step(d.y, arm_len);
    return max(h, v);
}

void fragment() {
    vec2 total_size = vec2(grid_size) * cell_size + grid_padding * 2.0;
    vec2 local_pos = UV * total_size;

    // Outside grid area — discard
    if (local_pos.x < grid_padding || local_pos.y < grid_padding ||
        local_pos.x > total_size.x - grid_padding || local_pos.y > total_size.y - grid_padding) {
        discard;
    }

    vec2 grid_local = local_pos - grid_padding;
    ivec2 cell_coords = ivec2(floor(grid_local / cell_size));
    cell_coords = clamp(cell_coords, ivec2(0), grid_size - 1);

    vec2 tex_uv = (vec2(cell_coords) + 0.5) / vec2(grid_size);
    vec4 data = texture(nugget_data, tex_uv);
    int cell_type = int(round(data.r * 255.0));
    int cell_g = int(round(data.g * 255.0));
    float phase = data.b * 255.0;

    if (cell_type == 0) discard;

    vec2 cell_origin = vec2(cell_coords) * cell_size;
    vec2 p = grid_local - cell_origin; // position within cell [0, cell_size]
    vec2 center = vec2(cell_size * 0.5);
    float cs = cell_size;

    vec3 col = vec3(0.0);
    float alpha = 0.0;

    if (cell_type == 1) {
        // === UNMINED NUGGET ===
        float diamond_size = cs * 0.30;
        float df = diamond_df(p, center, diamond_size);

        // Diamond fill — prismatic hue shift
        float hue_shift = sin(TIME * 1.2 + phase * 0.0145) * 0.08;
        float hue = 0.55 + hue_shift;
        // HSV to RGB (simplified for narrow hue range around cyan)
        vec3 diamond_rgb = vec3(
            0.5 + 0.5 * cos(6.2832 * (hue - 0.0)),
            0.5 + 0.5 * cos(6.2832 * (hue - 0.333)),
            0.5 + 0.5 * cos(6.2832 * (hue - 0.666))
        );
        diamond_rgb = mix(vec3(1.0), diamond_rgb, 0.08); // very low saturation

        if (df < 1.0) {
            float fill_alpha = 0.6 * (1.0 - df * 0.3);
            col = diamond_rgb;
            alpha = fill_alpha;
        }

        // Diamond stroke
        if (df > 0.85 && df < 1.15) {
            float stroke_a = 0.8 * (1.0 - abs(df - 1.0) / 0.15);
            col = mix(col, vec3(0.7, 0.75, 0.85), stroke_a);
            alpha = max(alpha, stroke_a * 0.8);
        }

        // Ambient radial glow
        float glow_dist = length(p - center) / (cs * 0.45);
        if (glow_dist < 1.0) {
            float glow_a = 0.20 * (1.0 - glow_dist);
            col = mix(col, vec3(0.85, 0.9, 1.0), glow_a / max(alpha + glow_a, 0.001));
            alpha = max(alpha, glow_a);
        }

        // 4 prismatic star sparkles
        vec3 star_colors[4] = vec3[4](
            vec3(0.67, 0.87, 1.0),  // ice blue
            vec3(1.0, 0.83, 1.0),   // pink
            vec3(0.83, 1.0, 0.87),  // green
            vec3(1.0, 1.0, 1.0)     // white
        );

        for (int i = 0; i < 4; i++) {
            float fi = float(i);
            float sp = TIME * 1.5 + fi * 2.1 + phase * 0.051;
            float sparkle_alpha = max(0.0, sin(sp)) * 0.9;

            if (sparkle_alpha > 0.05) {
                float sx = center.x + sin(sp * 0.7 + fi * 4.2) * cs * 0.3;
                float sy = center.y + cos(sp * 0.5 + fi * 3.1) * cs * 0.3;
                float arm_len = 2.5 + sparkle_alpha * 1.5;
                float arm_w = 0.6;
                float s = star4(p, vec2(sx, sy), arm_len, arm_w);
                if (s > 0.0) {
                    col = mix(col, star_colors[i], s * sparkle_alpha / max(alpha + s * sparkle_alpha, 0.001));
                    alpha = max(alpha, s * sparkle_alpha);
                }
            }
        }

        // Mining progress cracks (cell_g > 0 means being mined)
        if (cell_g > 0) {
            float progress = float(cell_g) / 255.0;
            float crack_len = cs * 0.35 * progress;
            float crack_a = 0.4 + 0.5 * progress;

            // 4 radial crack lines from center
            vec2 crack_dirs[4] = vec2[4](
                vec2(1.0, -0.6), vec2(-0.8, 0.4), vec2(0.5, 0.7), vec2(-0.6, -0.9)
            );
            for (int i = 0; i < 4; i++) {
                if (i == 3 && progress < 0.4) continue;
                vec2 dir = normalize(crack_dirs[i]);
                vec2 to_p = p - center;
                float proj = dot(to_p, dir);
                float perp = length(to_p - dir * proj);
                if (proj > 0.0 && proj < crack_len && perp < 0.8) {
                    col = mix(col, vec3(1.0), crack_a);
                    alpha = max(alpha, crack_a);
                }
            }

            // Intensified shimmer during mining
            float shimmer = 0.15 + 0.2 * progress;
            float glow_r = length(p - center) / (cs * 0.5);
            if (glow_r < 1.0) {
                alpha = max(alpha, shimmer * (1.0 - glow_r));
                col = mix(col, vec3(1.0), shimmer * 0.5);
            }
        }
    }
    else if (cell_type == 2) {
        // === FORTIFIED WALL ===
        int hp = cell_g;
        float d_size = cs * 0.15;
        float pulse = 0.4 + 0.3 * sin(TIME * 2.5 + phase * 0.067);

        // Diamond positions based on HP
        vec2 positions[3];
        int count = 0;

        if (hp == 3) {
            // Inverted triangle
            positions[0] = center + vec2(-cs * 0.18, -cs * 0.12);
            positions[1] = center + vec2( cs * 0.18, -cs * 0.12);
            positions[2] = center + vec2(0.0,         cs * 0.15);
            count = 3;
        } else if (hp == 2) {
            // Vertical pair
            positions[0] = center + vec2(0.0, -cs * 0.12);
            positions[1] = center + vec2(0.0,  cs * 0.12);
            count = 2;
        } else if (hp >= 1) {
            // Single center
            positions[0] = center;
            count = 1;
        }

        for (int i = 0; i < count; i++) {
            float df = diamond_df(p, positions[i], d_size);

            // Diamond fill
            if (df < 1.0) {
                float fill_a = pulse * (1.0 - df * 0.3);
                col = mix(col, vec3(0.7, 0.85, 1.0), fill_a / max(alpha + fill_a, 0.001));
                alpha = max(alpha, fill_a);
            }

            // Diamond stroke
            if (df > 0.8 && df < 1.2) {
                float stroke_a = pulse * 0.6 * (1.0 - abs(df - 1.0) / 0.2);
                col = mix(col, vec3(1.0), stroke_a / max(alpha + stroke_a, 0.001));
                alpha = max(alpha, stroke_a);
            }

            // Per-diamond sparkle star
            float sp = TIME * 1.8 + float(i) * 2.3 + phase * 0.043;
            float sparkle_a = max(0.0, sin(sp)) * 0.7;
            if (sparkle_a > 0.05) {
                float sx = positions[i].x + sin(sp * 0.6 + float(i) * 3.7) * d_size * 0.8;
                float sy = positions[i].y + cos(sp * 0.4 + float(i) * 2.9) * d_size * 0.8;
                float s = star4(p, vec2(sx, sy), 2.0, 0.5);
                if (s > 0.0) {
                    col = mix(col, vec3(0.8, 0.9, 1.0), s * sparkle_a / max(alpha + s * sparkle_a, 0.001));
                    alpha = max(alpha, s * sparkle_a);
                }
            }
        }
    }

    if (alpha < 0.005) discard;
    COLOR = vec4(col, alpha);
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Assets/Shaders/nugget_overlay.gdshader
git commit -m "feat: add nugget overlay shader for sparkles and fortified wall diamonds"
```

---

### Task 3: Wire up the nugget overlay shader in GridRenderer

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.cs`

This task adds the data texture creation, the `ColorRect` for the overlay shader, and the per-tick data texture update.

- [ ] **Step 1: Add fields for the nugget overlay**

In `GridRenderer.cs`, after the existing `_mapDataTexture` field (around line 27), add:

```csharp
// Nugget overlay: shader-based sparkles for unmined nuggets + fortified wall diamonds
private ColorRect? _nuggetOverlayRect;
private ShaderMaterial? _nuggetOverlayMaterial;
private ImageTexture? _nuggetDataTexture;
private Image? _nuggetDataImage;
```

- [ ] **Step 2: Initialize the overlay in _Ready()**

In `GridRenderer._Ready()`, after the `AddChild(_bgRect)` call (after line 44), add:

```csharp
// Initialize nugget overlay shader
var nuggetShader = GD.Load<Shader>("res://Assets/Shaders/nugget_overlay.gdshader");
_nuggetOverlayMaterial = new ShaderMaterial { Shader = nuggetShader };
_nuggetOverlayRect = new ColorRect
{
    Material = _nuggetOverlayMaterial,
    Color = Colors.White,
    MouseFilter = Control.MouseFilterEnum.Ignore,
    ZIndex = 5,
};
AddChild(_nuggetOverlayRect);
```

- [ ] **Step 3: Set shader params when game state is set**

In `UpdateGridShader()`, after the existing `_bgMaterial` param calls (after line 162), add:

```csharp
// Update nugget overlay shader params
if (_nuggetOverlayMaterial != null)
{
    var grid2 = _gameState.Grid;
    _nuggetOverlayMaterial.SetShaderParameter("grid_size", new Vector2I(grid2.Width, grid2.Height));
    _nuggetOverlayMaterial.SetShaderParameter("cell_size", CellSize);
    _nuggetOverlayMaterial.SetShaderParameter("grid_padding", GridPadding);

    // Create data image (reused each tick)
    _nuggetDataImage = Image.CreateEmpty(grid2.Width, grid2.Height, false, Image.Format.Rgba8);
    _nuggetDataTexture = ImageTexture.CreateFromImage(_nuggetDataImage);
    _nuggetOverlayMaterial.SetShaderParameter("nugget_data", _nuggetDataTexture);

    float totalW = grid2.Width * CellSize + GridPadding * 2f;
    float totalH = grid2.Height * CellSize + GridPadding * 2f;
    _nuggetOverlayRect!.Size = new Vector2(totalW, totalH);
    _nuggetOverlayRect.Position = Vector2.Zero;
}
```

- [ ] **Step 4: Add data texture update method**

Add a new method to `GridRenderer.cs`, before the `_Draw()` method:

```csharp
private void UpdateNuggetOverlayData()
{
    if (_gameState == null || _nuggetDataImage == null || _nuggetDataTexture == null) return;

    // Clear to black (all zeros = empty cells)
    _nuggetDataImage.Fill(Colors.Black);

    foreach (var block in _gameState.Blocks)
    {
        if (block.Type == BlockType.Nugget && block.NuggetState is { IsMined: false })
        {
            // Unmined nugget: R=1, G=mining progress, B=phase
            int miningProgress = 0;
            if (block.PlayerId != -1)
                miningProgress = (int)(255f * block.NuggetState.MiningProgress / Simulation.Core.Constants.NuggetMiningTicks);
            int phaseByte = block.Id & 0xFF;
            _nuggetDataImage.SetPixel(block.Pos.X, block.Pos.Y,
                new Color(1f / 255f, miningProgress / 255f, phaseByte / 255f, 0f));
        }
        else if (block.Type == BlockType.Wall && block.FortifiedHp > 0)
        {
            // Fortified wall: R=2, G=HP, B=phase
            int phaseByte = block.Id & 0xFF;
            _nuggetDataImage.SetPixel(block.Pos.X, block.Pos.Y,
                new Color(2f / 255f, block.FortifiedHp / 255f, phaseByte / 255f, 0f));
        }
    }

    _nuggetDataTexture.Update(_nuggetDataImage);
}
```

- [ ] **Step 5: Call the update in _Process()**

In `_Process()`, just before the `UpdateWardenZoC()` call (line 379), add:

```csharp
// Update nugget overlay data texture
UpdateNuggetOverlayData();
```

- [ ] **Step 6: Build and verify**

Run: `dotnet build`
Expected: Build succeeds. The overlay rect is created but won't show visible results until CPU drawing is removed (Task 4), since both will draw on top of each other for now.

- [ ] **Step 7: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.cs
git commit -m "feat: wire up nugget overlay shader with data texture in GridRenderer"
```

---

### Task 4: Remove CPU-drawn sparkles and fortified overlay, update mined nuggets

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Blocks.cs`

- [ ] **Step 1: Remove `DrawFortifiedWallOverlay` call from wall drawing**

In `DrawBlockTypeIndicator()`, find line 413:
```csharp
                DrawFortifiedWallOverlay(block, rect);
```
Remove this line entirely.

- [ ] **Step 2: Delete `DrawFortifiedWallOverlay` method**

Delete the entire method at lines 896-917:
```csharp
    private void DrawFortifiedWallOverlay(Block block, Rect2 rect)
    {
        // ... entire method
    }
```

- [ ] **Step 3: Update `DrawNuggetDiamond` for unmined nuggets**

Replace the unmined nugget section in `DrawNuggetDiamond()`. The shader now handles the diamond shape, sparkles, glow, and stroke for unmined nuggets. Keep only the mining vibration shake overlay.

Replace lines 830-893 (everything from `var pts = new Vector2[]` through the mining shimmer) with:

```csharp
        if (block.NuggetState is { IsMined: true })
        {
            // Mined: team-colored diamond with star bursts
            var palette = _config.GetPalette(block.PlayerId);
            var pts = new Vector2[]
            {
                drawCenter + new Vector2(0, -d),
                drawCenter + new Vector2(d, 0),
                drawCenter + new Vector2(0, d),
                drawCenter + new Vector2(-d, 0)
            };
            float pulse = 0.6f + 0.15f * Mathf.Sin(time * 2f + block.Id);
            DrawColoredPolygon(pts, palette.Base with { A = pulse });

            // 2 small prismatic star bursts near diamond vertices
            for (int i = 0; i < 2; i++)
            {
                float sp = time * 1.8f + i * 2.5f + block.Id * 1.7f;
                float sparkleAlpha = Mathf.Max(0, Mathf.Sin(sp)) * 0.8f;
                if (sparkleAlpha > 0.05f)
                {
                    float sx = Mathf.Sin(sp * 0.6f + i * 3.8f) * d * 0.8f;
                    float sy = Mathf.Cos(sp * 0.4f + i * 2.6f) * d * 0.8f;
                    var starCenter = drawCenter + new Vector2(sx, sy);
                    float armLen = 2.5f;
                    var starColor = i == 0 ? palette.Base.Lightened(0.5f) : Colors.White;
                    starColor = starColor with { A = sparkleAlpha };
                    DrawLine(starCenter - new Vector2(armLen, 0), starCenter + new Vector2(armLen, 0), starColor, 0.8f, true);
                    DrawLine(starCenter - new Vector2(0, armLen), starCenter + new Vector2(0, armLen), starColor, 0.8f, true);
                }
            }
        }
        else
        {
            // Unmined: shader handles diamond, sparkles, glow, stroke, and cracks.
            // CPU only draws the mining shake overlay when actively being mined.
            if (isMining && miningProgress > 0f)
            {
                var pts = new Vector2[]
                {
                    drawCenter + new Vector2(0, -d),
                    drawCenter + new Vector2(d, 0),
                    drawCenter + new Vector2(0, d),
                    drawCenter + new Vector2(-d, 0)
                };
                // Semi-transparent shaking diamond over the shader's static one
                float hueShift = Mathf.Sin(time * 1.2f + block.Id * 3.7f) * 0.08f;
                var shimmerColor = Color.FromHsv(0.55f + hueShift, 0.08f, 1f, 0.3f * miningProgress);
                DrawColoredPolygon(pts, shimmerColor);
            }
        }
```

Also remove the diamond stroke block (lines 868-874) since the shader handles it:
```csharp
        // Diamond stroke (unmined only — mined nuggets show borderless team diamond per spec §9.3)
        if (block.NuggetState is not { IsMined: true })
        {
            var stroke = new Color(0.7f, 0.75f, 0.85f, 0.8f);
            for (int i = 0; i < 4; i++)
                DrawLine(pts[i], pts[(i + 1) % 4], stroke, 1.5f, true);
        }
```

- [ ] **Step 4: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Blocks.cs
git commit -m "feat: replace CPU nugget sparkles with shader, add mined nugget star bursts"
```

---

### Task 5: Add nest refine zone visualization

**Files:**
- Modify: `godot/Scripts/Rendering/GridRenderer.Effects.cs`
- Modify: `godot/Scripts/Rendering/GridRenderer.cs` (add field + call)

- [ ] **Step 1: Add the nest refine rects dictionary**

In `GridRenderer.Effects.cs`, after the `_wardenZocRects` dictionary (line 14), add:

```csharp
private readonly Dictionary<int, ColorRect> _nestRefineRects = new();
```

- [ ] **Step 2: Add `UpdateNestRefineZones` method**

In `GridRenderer.Effects.cs`, after the `DrawWardenZoC()` method (after line 89), add:

```csharp
private void UpdateNestRefineZones()
{
    if (_gameState == null) return;

    var grid = _gameState.Grid;
    int refineR = Simulation.Core.Constants.NuggetRefineRadius;
    var gridPixelSize = new Vector2(grid.Width * CellSize, grid.Height * CellSize);

    var activeNests = new HashSet<int>();
    foreach (var nest in _gameState.Nests)
    {
        activeNests.Add(nest.Id);

        if (!_nestRefineRects.TryGetValue(nest.Id, out var rect))
        {
            var mat = new ShaderMaterial { Shader = _gridRingsShader };

            mat.SetShaderParameter("grid_size", new Vector2(grid.Width, grid.Height));
            mat.SetShaderParameter("cell_size", CellSize);
            mat.SetShaderParameter("max_radius", (float)refineR + 1f);
            mat.SetShaderParameter("trail", 1.5f);
            mat.SetShaderParameter("ring_color", new Color(0.8f, 0.85f, 0.95f, 0.6f));
            mat.SetShaderParameter("fade_mult", 0.6f);
            mat.SetShaderParameter("mode", 0); // square rings
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
            _nestRefineRects[nest.Id] = rect;
        }

        var mat2 = (ShaderMaterial)rect.Material;
        mat2.SetShaderParameter("center", new Vector2(nest.Center.X + 0.5f, nest.Center.Y + 0.5f));
        mat2.SetShaderParameter("age_ms", (float)Time.GetTicksMsec());
        float waveCycleMs = 3000f;
        mat2.SetShaderParameter("progress", ((float)Time.GetTicksMsec() % waveCycleMs) / waveCycleMs);
    }

    // Remove rects for nests no longer active
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
```

- [ ] **Step 3: Add cleanup in SetGameState()**

In `GridRenderer.cs` `SetGameState()`, after the warden ZoC cleanup (after line 114), add:

```csharp
// Clean up nest refine zone rects from previous game state
foreach (var (_, rect) in _nestRefineRects)
    rect.QueueFree();
_nestRefineRects.Clear();
```

- [ ] **Step 4: Call UpdateNestRefineZones in _Process()**

In `GridRenderer.cs` `_Process()`, after the `UpdateWardenZoC()` call (line 379), add:

```csharp
// Update nest refine zone visualization
UpdateNestRefineZones();
```

- [ ] **Step 5: Build and verify**

Run: `dotnet build`
Expected: Build succeeds.

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Rendering/GridRenderer.Effects.cs godot/Scripts/Rendering/GridRenderer.cs
git commit -m "feat: add nest refine zone visualization using square-ring shader"
```

---

### Task 6: Run all tests and manual verification

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`
Expected: All 208+ tests pass.

- [ ] **Step 2: Manual verification checklist**

Open the Godot project and start a game with nuggets on the map:

1. **Unmined nuggets**: Verify prismatic 4-pointed star sparkles cycling through blue/pink/green/white, diamond shape with hue shimmer, ambient glow
2. **Mining**: Verify crack lines grow as mining progresses, shake overlay appears during active mining
3. **Mined nuggets**: Verify team-colored diamond with 2 small star bursts, pulse animation
4. **Fortified walls**: Verify 3-diamond inverted triangle at full HP, 2 vertical at 2 HP, 1 centered at 1 HP, with sparkle on each diamond
5. **Nest refine zone**: Verify square-ring glow around nests, 5×5 grid (radius 2), distinct from warden ZoC
6. **Performance**: Check FPS counter with 50+ nuggets on screen — should stay at 60fps

- [ ] **Step 3: Final commit if any adjustments needed**

```bash
git add -A
git commit -m "fix: visual refinement adjustments from manual testing"
```
