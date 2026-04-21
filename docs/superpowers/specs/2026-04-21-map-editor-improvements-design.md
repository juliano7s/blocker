# Map Editor Improvements — Design Spec

**Date:** 2026-04-21  
**Status:** Approved

## Overview

Bring the Godot map editor up to feature parity with the TypeScript prototype (`d:/claude/min-rts/src/editor`), while keeping everything that's already better in Godot (minimap, WYSIWYG rendering, camera pan, Test Map, JSON save/load). Replace the broken live-symmetry painting with proven one-shot mirror operations ported from the prototype.

---

## 1. Tool Buttons in Top Bar

Six editing-mode buttons are added to the top bar, between the map-management buttons (Back/New/Resize/Save/Load/Test) and the name/slot controls. Each shows a keyboard shortcut label.

| Button | Shortcut | Behaviour |
|--------|----------|-----------|
| Paint  | P | Current behaviour — left-click paints, right-click erases |
| Fill   | F | Flood-fill from clicked cell with current tile type |
| Pick   | K | Click a cell to select its tile type and switch to Paint |
| Select | S | Drag to draw a rectangle; drag inside to move; Delete clears |
| Line   | L | Click start, drag to preview Bresenham line, release to commit |
| Erase  | E | Left-click erases (equivalent to right-click in Paint) |

Additional shortcuts active everywhere in the editor:
- `Ctrl+Z` / `Ctrl+Shift+Z` — undo / redo
- `Esc` — cancel active selection or line preview
- `Delete` — clear selected region (Select tool)

The active tool is highlighted. Switching tile type in the sidebar automatically switches to Paint if another mode was active.

---

## 2. Fill Tool

Flood-fill from the clicked cell, replacing all contiguous cells of the same type with the current selection:

- **GroundPaint active:** fills contiguous cells with the same `GroundType` with the current ground.
- **TerrainPaint active:** fills contiguous cells with the same `TerrainType` (including `None`) with the current terrain.
- **UnitPlace / Eraser active:** no-op (fill has no meaningful unit semantics).

One undo entry per fill operation. Implementation: iterative stack flood-fill (same algorithm as the prototype's `floodFill`).

---

## 3. Pick Tool

Click any cell to read its tile:
- Reads the `GroundType`, `TerrainType`, and block (if any) under the cursor.
- If a block is present: sets the active tool to UnitPlace with that BlockType and slot.
- Else if terrain is present: sets TerrainPaint with that TerrainType.
- Else: sets GroundPaint with that GroundType.

After picking, automatically switches back to Paint mode.

---

## 4. Select + Move Tool

Ported from the prototype's select tool logic.

**Draw phase:** mouse-down outside any selection starts a rubber-band rectangle.  
**Ready phase:** release mouse — selection rect is shown with dashed border + corner handles.  
**Move phase:** mouse-down inside the selection rect starts a drag; the lifted region is shown at its destination while the source shows empty.  
**Commit:** mouse-up outside draw phase commits the move (clears source, writes destination). One undo entry.  
**Cancel:** `Esc` drops the selection without moving. `Delete` clears the selected cells.

The selection overlay is drawn by a dedicated `SelectionOverlay` Node2D on top of the grid, using `_Draw()`.

---

## 5. Line Tool

Ported from the prototype's line tool.

- Mouse-down records the start cell.
- Mouse-move shows a live Bresenham preview (semi-transparent overlay).
- Mouse-up commits the line to the map. One undo entry.
- `Esc` cancels a line in progress.

Line preview is drawn by a dedicated `LinePreviewOverlay` Node2D.

---

## 6. One-Shot Mirror (replaces live symmetry)

The live-symmetry checkboxes (`Left/Right`, `Top/Bottom`, `Diagonal TLBR`, `Diagonal TRBR`) are **removed** from the sidebar.

In their place, a **Mirror** section appears in the sidebar with four buttons:

| Button | Effect |
|--------|--------|
| L → R | Copy left half onto right half |
| R → L | Copy right half onto left half |
| T → B | Point-rotate top half onto bottom half |
| B → T | Point-rotate bottom half onto top half |
| TL → BR | Reflect across the TL–BR diagonal (transpose, scaled to map aspect ratio) |
| TR → BL | Reflect across the TR–BL diagonal |

Each operation is **one-shot** (applies to the whole map) and creates one undo entry.

**Team tile swapping:** a toggle `Teams: ON / OFF` (default ON) controls whether mirror operations swap team ownership. When ON, slot 0 ↔ slot 1, slot 2 ↔ slot 3, slot 4 ↔ slot 5. When OFF, units are cleared from the mirrored side (ground/terrain still copies).

**Implementation:** port directly from the prototype's `mirrorLeftToRight`, `mirrorRightToLeft`, `mirrorTopToBottom`, `mirrorBottomToTop` methods in `MapEditor.ts`, adapted from char-based to Godot's `GroundType` / `TerrainType` / Block model.

---

## 7. Status Bar

A thin bar at the bottom of the editor viewport (above nothing, below the grid body) shows:

```
x: 12  y: 8    tile: Boot Ground    tool: Paint    zoom: 1.0×    size: 41 × 25
```

- **x / y**: cell under the mouse cursor. Blank when mouse is outside the grid.
- **tile**: ground type, terrain type, and/or block type at the cursor. Shows the most specific non-empty value (block > terrain > ground).
- **tool**: current active tool name.
- **zoom**: current camera zoom level.
- **size**: current map dimensions.

Implemented as a `PanelContainer` in the `UILayer`, anchored to the bottom of the screen. Updated each `_Process` frame via mouse position → `GetGridPos`.

---

## 8. Tile Icons for Ground and Terrain Buttons

Ground and terrain buttons in the sidebar are upgraded from plain text buttons to **icon + label cards** (2-column grid), matching the existing unit button style.

Each button is a custom `CanvasItem`-derived control that calls `_Draw()` to paint a miniature tile preview:

- **Ground tiles**: draw the ground background using the same procedural rendering as `GridRenderer` (Boot = green tint, Overload = purple, Proto = dark green, Normal = dark).
- **Terrain tiles**: draw a solid-filled block with the terrain's border colour (Solid = grey, Breakable = blue-grey, Fragile = brown).

The label sits below the icon, same font/size as unit button labels. Active selection is highlighted with a blue border.

---

## 9. Unit Icon Bug Fix

In `MapEditorScene._Ready()`, `_toolbar.SetConfig(_config)` is currently called **before** `_uiLayer.AddChild(_toolbar)`. Since `_toolbar._Ready()` (which creates the `_unitButtons` list) fires during `AddChild`, the config is set before any buttons exist and is never applied to them.

**Fix:** move `_toolbar.SetConfig(_config)` to after `_uiLayer.AddChild(_toolbar)`.

---

## What Is Not Changing

- Minimap with camera jump
- WASD / arrow key / edge scrolling / middle-mouse pan
- Camera zoom (scroll wheel, discrete levels)
- Slot selector
- Guide lines (center lines toggle)
- Save / Load / Test Map / New / Resize
- Undo/Redo stack (EditorActionStack) — extended to cover new tools

---

## Files Affected

| File | Change |
|------|--------|
| `godot/Scripts/Editor/MapEditorScene.cs` | Add tool state machine, fill/pick/select/line logic, status bar update, mirror operations, fix SetConfig call order |
| `godot/Scripts/Editor/EditorToolbar.cs` | Add tool buttons to top bar, replace symmetry checkboxes with mirror buttons + teams toggle, upgrade ground/terrain buttons to icon cards |
| `godot/Scripts/Editor/SymmetryMirror.cs` | Delete — also remove `_symmetry` field and all `GetMirroredPositions()` calls from `MapEditorScene.ApplyToolAt` |
| `godot/Scripts/Editor/EditorAction.cs` | No change |
| `godot/Scripts/Editor/EditorActionStack.cs` | No change |
| `godot/Scripts/Rendering/BlockIconPainter.cs` | No change (already correct) |
| New: `godot/Scripts/Editor/SelectionOverlay.cs` | Node2D that draws the selection rect overlay |
| New: `godot/Scripts/Editor/LinePreviewOverlay.cs` | Node2D that draws the line preview overlay |
