# Menu Revamp — Design Spec

## Overview

Replace the utilitarian MainMenu with a grid-integrated menu where everything lives on the grid. Title letters are grid cells with cycling animations, menu buttons are block clusters with hover/click effects, and sparse ambient blocks drift and explode in the background.

## Layout

- Full-screen grid background
- "BLOCKER" title top-center, drawn as grid cells
- 5 menu buttons center-aligned below the title, each a row of block cells + label
- 2-3 ambient blocks scattered across the grid

## Color Palette

**Primary (Warm Complement):**
- Primary: `#4AF` (cyan-blue) — grid, title, idle buttons
- Hover: `#FF6A33` (orange) — button highlight
- Effects: `#FFAA33` (amber) — explosions, accents

**Alternative (Monochrome + White Flash) — saved for later testing:**
- Primary: `#4AF` (cyan-blue)
- Hover: `#88DDFF` (light cyan)
- Flash: `#FFFFFF` (white-hot)

## Title Animation

Letters are drawn as 5×7 grid cells, matching the TS prototype's `TitleRenderer.ts` letter definitions.

### Animation Patterns (4, cycling every 2-5s)

1. **Left→Right sweep** — wave reveals cells left to right with a trailing glow (1.5-2s, trail=8)
2. **Radial burst** — cells light up outward from center (1.8-2.4s, trail=6)
3. **Letter cascade** — one letter at a time lights up sequentially (2-2.8s, trail=4)
4. **Branching lightning** — lightning paths crawl along letter edges (1.4-2s, 40% chance to layer on top of another animation)

### Rendering Style

- Outer bloom (larger radius, low alpha)
- Inner halo (medium radius, ~15% alpha)
- Core line (thin, ~80% alpha)
- White-hot tip on bright segments
- All in cyan-blue

## Menu Buttons

5 buttons, center-aligned:

1. Play Test
2. Play vs AI
3. Play Multiplayer
4. Map Editor
5. Exit Game

### Button Structure

Each button is a horizontal row of 2-3 block cells followed by the label text. Cells have decreasing opacity left to right (0.7, 0.5, 0.3).

### Interactions

- **Idle**: Cyan-blue cells, subtle glow
- **Hover**: Cells shift to orange (`#FF6A33`), grid cells underneath the button area glow, label brightens with orange text-shadow
- **Click**: Triggers a grid effect (LightningBurst from EffectFactory) at the button position, then navigates after a short delay to let the effect play

## Grid Background

- Full-screen grid covering the viewport
- Reuse existing `grid_background.gdshader` or draw a simple grid with Line2D/ColorRect
- CellSize = 28px (matches gameplay)
- Grid line opacity ~12%
- Dark background

## Ambient Blocks

- 2-3 blocks on screen at a time (sparse — ambiance, not distraction)
- Blocks drift slowly across the grid (move to a random adjacent cell every ~1-2s)
- Occasionally one block explodes with a random grid effect from EffectFactory (every 5-10s)
- After explosion, a new block spawns at a random edge position after a short delay
- Block colors use cyan-blue (matching the menu theme)

## Architecture

### New Files

- `godot/Scripts/UI/MenuGrid.cs` — Draws the grid background for the menu, manages the grid coordinate system
- `godot/Scripts/UI/MenuTitle.cs` — Title letter definitions (5×7 cells), animation pattern cycling, glow rendering
- `godot/Scripts/UI/MenuButton.cs` — Single grid-integrated button: block cells + label, hover/click behavior
- `godot/Scripts/UI/MenuAmbience.cs` — Manages ambient blocks: spawning, drifting, triggering explosions

### Modified Files

- `godot/Scripts/UI/MainMenu.cs` — Rewritten to compose the above components instead of creating plain Godot Button nodes
- `godot/Scenes/MainMenu.tscn` — Updated scene tree

### Reused Systems

- `EffectFactory` / `GpuEffect` / `LineEffect` — existing grid effect system for click effects and ambient explosions
- `line_wave.gdshader` — existing shader for line-based effects
- Title glow rendering can use the same `GpuEffect` pipeline or a dedicated shader

### Scene Tree

```
MainMenu (Control)
├── MenuGrid (Node2D) — grid background
├── EffectLayer (Node2D) — GpuEffect instances for click/ambient effects
├── MenuTitle (Node2D) — animated title
├── MenuButtons (VBoxContainer or Node2D, centered)
│   ├── MenuButton "Play Test"
│   ├── MenuButton "Play vs AI"
│   ├── MenuButton "Play Multiplayer"
│   ├── MenuButton "Map Editor"
│   └── MenuButton "Exit Game"
└── MenuAmbience (Node2D) — ambient drifting blocks
```

## Technical Notes

- The menu runs independently of the simulation — no GameState needed. Title animations and ambient blocks are purely visual.
- EffectFactory creates effects at grid positions. The menu defines its own grid coordinate system (viewport-sized, CellSize=28).
- Title letter cell definitions should be ported from `d:/claude/min-rts/src/renderer/TitleRenderer.ts` (the LETTER_PATTERNS constant).
- Hover detection maps mouse position to grid cells occupied by each button's block cluster.
- Click effect should play for ~300-500ms before scene transition, so the player sees it.

## Future Considerations

- Test the monochrome+white alternative color palette
- Add remaining title animations (right→left, top→bottom, bottom→top sweeps) as directional variants
- Potential: menu buttons could have idle micro-animations (subtle cell pulse)
