# HUD Redesign Spec

## Overview

Redesign the in-game HUD with a unified solid frame visual style, reorganized top bar, functional bottom bar panels (selection info, command card), floating spawn toggles, and control groups.

## Visual Style

**Unified solid frame** — both top and bottom bars share the same treatment:
- Background: linear gradient `#141922` → `#0c1015`
- Border: 2px solid `#2d3748`
- Text: `#e5e5e5` (primary), `#888` (secondary)
- Distinct from game area (darker, more saturated than grid background)

## Top Bar

Height: ~42px

**Contents (left to right):**
1. Player color square (16x16, player's color with 1px lighter border)
2. Player name
3. Divider
4. Game time in `hh:mm:ss` (converted from ticks: `tick / tickRate`)
5. Divider
6. Population: `Pop: X / Y`
7. Menu button (right-aligned): `☰ Menu` → dropdown with Surrender, Exit to Menu

**Removed:** Block ratio bar (future spectator feature), tick counter (replaced by time), FPS (moved to debug)

## Spawn Toggles

**Position:** Floating panel, top-right of game area (below top bar, ~20px from right edge)

**Contents:** 3 toggle buttons stacked vertically:
- Builder (blue `#3b82f6`)
- Soldier (green `#22c55e`)
- Stunner (purple `#a855f7`)

**Behavior:**
- Hotkeys: `1`, `2`, `3`
- Active: full opacity
- Paused: 35% opacity
- Click or hotkey toggles global spawn for that unit type across all player's nests

**Visual:** Same panel treatment as HUD (solid background, border), with subtle shadow

## Bottom Bar

Height: ~110px. Three panels with 6px gap.

**Responsive layout:**
- Minimap: fixed width (120px min)
- Command card: fixed width (120px min)
- Selection panel: **flexible**, stretches to fill remaining space

### Minimap (Left Panel)

Width: 120px (fixed)

- Label: "Minimap" (small, uppercase, dimmed)
- Clear border distinguishing map area from padding
- Shows: terrain, ground types, blocks as colored dots
- Viewport rectangle (white outline)
- Click to jump camera

### Selection Panel (Center, flex: 1)

**Control Groups Row (top):**
- Horizontal row of control group buttons (1-9 or 0-9)
- Each shows: group number + block count (e.g., "1: 5")
- Click to select group, Ctrl+click to assign selection to group
- Empty groups hidden or dimmed

**Selection Info (below):**

*Single unit selected:*
- Unit type name
- Stats: defense, speed, HP (Soldier/Jumper), root progress %

*Multiple units selected:*
- Label: "Selected: N [Type]" or "Selected: N units" (mixed)
- Grid of clickable mini-blocks (unit icons)
- Click: select just that unit
- Shift+click: remove from selection
- Scrollable or wrapped if many units

### Command Card (Right Panel)

Width: 120px (fixed)

- Label: "Commands" (small, uppercase, dimmed)
- Grid of command buttons (3 columns)
- Each button: icon + hotkey in bottom-right corner
- Tooltip on hover: command name + description
- Hidden: commands that don't apply to selection
- Grayed out: conditional commands (e.g., Explode when not rooted)
- Icons: placeholder shapes for now, PNG-replaceable later

**Commands by unit type:**
- Builder: Root (F), Wall (W), Push (G), Uproot (U)
- Soldier: Root (F), Explode (D), Uproot (U)
- Stunner: Root (F), Stun (S), Uproot (U)
- Wall: (none)
- Mixed: show intersection or union with state indicators

## Debug Display

- FPS counter: toggled via debug setting
- Position: below top bar, right-aligned, small text
- Color-coded: green (55+), yellow (30-54), red (<30)

## Out of Scope

- Nest timers (spawn progress bars)
- Chat messages
- Controls overlay (keybind hints)
- Spectator features (block ratio bar)

## Files to Modify

- `godot/Scripts/Rendering/HudOverlay.cs` — top bar
- `godot/Scripts/Rendering/HudBar.cs` — bottom bar structure
- `godot/Scripts/Rendering/MinimapPanel.cs` — minimap improvements
- New: `SelectionPanel.cs` — selection info + control groups
- New: `CommandCard.cs` — command buttons
- New: `SpawnToggles.cs` — floating toggle panel

## Dependencies

- Selection state from `SelectionManager`
- Available commands depend on unit types + states
- Control groups: new simulation or Godot-side feature (TBD)
- Spawn toggle state: new simulation feature (global spawn pause per unit type)
