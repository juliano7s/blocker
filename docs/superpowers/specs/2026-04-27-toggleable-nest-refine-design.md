# Toggleable Nest Nugget Refining

## Summary

Make nugget consumption toggleable per nest. Mined nuggets auto-rally only to nests with refining enabled. The refine zone visual (marching ants + sparkles) appears only when enabled. A new "Refine Nuggets" command in the command card controls the toggle.

## Simulation Changes

### Nest State

Add `bool RefineEnabled` to `Nest` (default: `true`).

Preserves current behavior — all nests accept nuggets by default. Disabling is a deliberate tactical choice.

### New Command: `ToggleRefine`

- **CommandType**: `ToggleRefine` (new enum value)
- **BlockIds**: Any blocks that are nest members
- **Behavior**: For each block in `BlockIds`, find its parent nest and flip `RefineEnabled`
- **No TargetPos or Direction needed**

### Nugget Auto-Rally (`SetAutoRallyTarget`)

Current logic: find nearest friendly nest by Chebyshev distance, tie-break by nest ID.

New logic: same, but **filter to nests where `RefineEnabled == true`**. If no qualifying nest exists, do not set `MoveTarget` — nugget stays where it was mined.

### Nugget Consumption (`TryConsumeNestRefine`)

Current logic: check if nugget is within `NuggetRefineRadius` of any friendly nest.

New logic: additionally require `nest.RefineEnabled == true`. A nugget sitting near a disabled nest is not consumed.

### Edge Case: Toggle While Nuggets In Flight

**Disabling**: If a nest's refine is disabled while nuggets are already moving toward it:
- Nuggets that have been **manually moved** (`ManuallyMoved == true`) keep their target — manual commands always override.
- Nuggets on **auto-rally** should re-evaluate their target. On the tick the toggle command is processed, call `SetAutoRallyTarget()` for all mined friendly nuggets currently targeting the disabled nest's center. They will re-route to the next nearest enabled nest, or stop if none exists.

**Enabling**: When a nest's refine is enabled, on that same tick call `SetAutoRallyTarget()` for all idle mined friendly nuggets (no `MoveTarget`, not manually moved). They will discover the newly available nest and begin auto-rallying.

## Godot / UI Changes

### Command Card

New button: **"Refine Nuggets"**

- **Visibility**: Shown when any selected block is a nest member (rooted in a nest formation)
- **Toggle state display**: Highlighted/active when enabled, dimmed when disabled
- **Mixed selection**: State follows the **last-selected block's** nest. Clicking toggles all selected nests to match the resulting state of that nest.
- **Follows existing pattern**: Same toggle mechanics as `ToggleSpawn` and `TogglePush`

### Command Card Button Definition

Add to the `AllCommands` array in `CommandCard.cs`:
- Label: `"Refine Nuggets"`
- Action: Emit `ToggleRefine` command with selected block IDs
- Availability: `block.IsNestMember()` (or equivalent nest membership check)
- Toggle indicator: Read `RefineEnabled` from the block's parent nest

## Shader: Nest Refine Zone

### Replace Current Effect

Current: `grid_rings.gdshader` mode 0 (square rings) with `base_alpha: 0.15` — a static pulsing glow.

New: Dedicated `nest_refine.gdshader` — marching ants dashed perimeter + interior sparkles.

### Marching Ants Perimeter

- **Shape**: Continuous dashed rectangle tracing the Chebyshev boundary (radius 2 from nest center = 5×5 cell zone)
- **Dash pattern**: Uniform dash + gap segments marching at constant speed around the perimeter
- **Color**: Neutral blue-white (`rgba(140, 170, 255, 0.8)`) — not player-colored
- **Line width**: ~2.5px equivalent
- **Speed**: Full loop every ~3-4 seconds (tunable uniform)
- **No corner accents** — clean continuous loop

### Interior Sparkles

- **Style**: Diamond-shaped (4-point star) sparkles, matching the nugget overlay aesthetic
- **Color**: Mix of warm gold (`rgba(255, 240, 180, a)`) and cool blue (`rgba(180, 220, 255, a)`)
- **Distribution**: Random positions across interior cells (excluding nest center cell)
- **Animation**: Each sparkle has a unique phase and speed. Fades in and out sinusoidally. Only visible above brightness threshold (0.3) to create discrete twinkling, not constant noise.
- **Count**: ~1 sparkle per interior cell, with staggered phases so only 3-5 are visible at any moment.

### Shader Uniforms

```
center: vec2        — nest center in grid coords (+ 0.5 offset)
grid_size: vec2     — grid dimensions
cell_size: float    — pixels per cell
max_radius: float   — Chebyshev radius (2.0 + 1.0 for boundary)
time_ms: float      — current time for animation
march_speed: float  — dash animation speed (tunable)
zone_color: vec4    — marching ants color
```

### Visibility Toggle

The `ColorRect` for the refine zone is **only created/maintained when `RefineEnabled == true`**. When refine is disabled, the rect is `QueueFree()`'d — no shader runs, no visual. This is handled in `UpdateNestRefineZones()` by adding `nest.RefineEnabled` to the filter condition alongside the existing visibility check.

## Constants

No new constants needed. `NuggetRefineRadius` (2) is already defined and drives both the simulation check and the shader `max_radius`.

## Testing

### Unit Tests (xUnit)

1. **Default state**: New nest has `RefineEnabled == true`
2. **Toggle command**: `ToggleRefine` flips `RefineEnabled` on the target nest
3. **Auto-rally filters**: Mined nugget ignores nests with `RefineEnabled == false`
4. **No enabled nest**: Mined nugget stays put when all friendly nests disabled
5. **Consumption filters**: Nugget within radius of disabled nest is NOT consumed
6. **Re-route on disable**: Nuggets auto-rallying to a disabled nest re-evaluate and pick next enabled nest
7. **Re-route to stay**: Nuggets re-evaluate but find no enabled nest → stop
8. **Manual override**: Manually-moved nuggets keep their target regardless of toggle state
9. **Toggle re-enable**: Enabling refine on a nest causes nearby idle mined nuggets to rally to it (via next tick's auto-rally re-evaluation — or not, if they've already been manually moved)

### Manual Testing

- Toggle refine on/off and verify visual zone appears/disappears
- Mine nuggets and watch them route to the correct enabled nest
- Disable all nests and verify nuggets stay put
- Verify command card button state reflects nest state correctly
- Mixed selection: select blocks from two nests, verify toggle behavior
