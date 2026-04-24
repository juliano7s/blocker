# Map System & Editor Design

**Date:** 2026-04-05
**Status:** Approved

## Overview

Redesign the map system to support up to 6 players with a sparse JSON format, an in-game map editor, and a main menu for navigation. Replaces the legacy 2-player plain-text format.

## 1. JSON Map Format

Maps are `.json` files stored in `godot/Assets/Maps/`. The format is sparse — only non-default data is stored.

### Structure

```json
{
  "meta": {
    "name": "Crossfire",
    "author": "jjack",
    "version": 1,
    "width": 120,
    "height": 80,
    "slots": 4
  },
  "ground": [
    { "x": 10, "y": 5, "type": "boot" },
    { "x": 11, "y": 5, "type": "boot" },
    { "x": 60, "y": 40, "type": "overload" }
  ],
  "terrain": [
    { "x": 30, "y": 20, "type": "terrain" },
    { "x": 60, "y": 40, "type": "breakableWall" }
  ],
  "units": [
    { "x": 5, "y": 3, "type": "builder", "slot": 1 },
    { "x": 5, "y": 5, "type": "builder", "slot": 1 },
    { "x": 115, "y": 75, "type": "builder", "slot": 2 }
  ]
}
```

### Three independent layers

Each cell has up to three layers that combine independently:

1. **`ground`** — floor type affecting gameplay mechanics (spawn rates, unit variants). Omitted = normal.
2. **`terrain`** — physical obstacles occupying the cell. Omitted = none. A terrain entry can coexist with a ground entry at the same position (e.g., a breakable wall on overload ground — when the wall breaks, the overload ground is revealed).
3. **`units`** — starting blocks with slot assignment. Omitted = empty.

### Rules

- **Sparse**: any cell not in `ground` defaults to Normal floor. Any cell not in `terrain` has no obstacle. Any cell not in `units` is empty.
- **`meta.slots`**: number of spawn slots (1-6). The lobby assigns players to slots at game start.
- **`meta.version`**: format version for future migration (decorations, scripts, resources, etc.).
- **Ground types**: `boot`, `overload`, `proto` (omitted cells are `normal`).
- **Terrain types**: `terrain`, `breakableWall`, `fragileWall`.
- **Unit types**: `builder`, `soldier`, `stunner`, `warden`, `jumper`, `wall`.
- **Slot IDs**: 1-based integers (1-6).
- Maps can be arbitrarily large (hundreds of cells per axis, StarCraft-scale relative to camera viewport). The sparse format keeps file sizes small regardless of map dimensions.

### Future extensibility

The JSON format naturally extends for:
- **Decorations/new terrain types**: additional `type` values in `ground` or `terrain` arrays.
- **Neutral buildings/resources**: new top-level arrays (e.g., `"structures"`, `"resources"`).
- **Scripted scenarios**: a `"scripts"` or `"triggers"` section alongside map data.
- **Procedural generation**: generators produce the same `MapData` structure in memory, optionally saving to JSON.

## 2. Simulation-Side Map Data

The simulation layer receives parsed map data as pure C# — no JSON dependency.

### Data structures (in `src/Blocker.Simulation/Maps/`)

```csharp
// MapData.cs
public record MapData(
    string Name,
    int Width,
    int Height,
    int SlotCount,
    List<GroundEntry> Ground,
    List<TerrainEntry> Terrain,
    List<UnitEntry> Units
);

public record GroundEntry(int X, int Y, GroundType Type);
public record TerrainEntry(int X, int Y, TerrainType Type);
public record UnitEntry(int X, int Y, BlockType Type, int SlotId);
```

### Simulation Cell changes

The current `Cell` class uses a single `GroundType` enum that conflates floor types and obstacles. This must be split:

```csharp
// GroundType — floor only (affects economy/spawning)
public enum GroundType { Normal, Boot, Overload, Proto }

// TerrainType — physical obstacles (separate layer)
public enum TerrainType { None, Terrain, BreakableWall, FragileWall }

public class Cell
{
    public GroundType Ground { get; set; }
    public TerrainType Terrain { get; set; }
    public int? BlockId { get; set; }

    public bool IsPassable => Terrain == TerrainType.None;
    public bool IsNestZone => Ground is GroundType.Boot or GroundType.Overload or GroundType.Proto;
}
```

When a breakable/fragile wall is destroyed, `Terrain` is set to `None` — revealing whatever `Ground` was underneath.

```csharp
// SlotAssignment.cs — maps slot IDs to player IDs
public record SlotAssignment(int SlotId, int PlayerId);
```

### MapLoader changes

- **New method**: `Load(MapData data, List<SlotAssignment> assignments)` — builds a `GameState` from `MapData`, translating slot IDs to player IDs via the assignment list.
- **Legacy method**: existing `Load(string mapText)` stays for test compatibility.
- **Camera spawn positions**: derived from the average position of each slot's starting units (no explicit field needed).

### Data flow

```
JSON file (Godot) → MapSerializer → MapData (pure C#)
                                        ↓
Procedural generator → MapData ──→ MapLoader.Load(MapData, assignments) → GameState
                                        ↑
Editor (Godot) → MapData            SlotAssignment from lobby
```

## 3. Map Editor

An in-game Godot scene reusing the existing `GridRenderer`.

### Tools

- **Ground paint**: select a ground type (Normal, Boot, Overload, Proto), click/drag to paint the floor layer.
- **Terrain paint**: select a terrain type (Terrain, BreakableWall, FragileWall), click/drag to paint obstacles. Terrain overlays ground — both layers are independent.
- **Unit place**: select a block type, click to place. Replaces any existing unit at that cell.
- **Eraser**: click/drag to clear layers. Erases terrain and units; resets ground to Normal.
- **Slot selector**: pick active slot (1-6) before placing units. Each slot displayed in its player color.

### UI layout

- **Left sidebar/toolbar**: tool selection (ground types, unit types, eraser), slot selector with color indicators.
- **Top bar**: New Map (prompts for width/height/slot count), Save, Load, editable map name field.
- **Main area**: the grid, rendered with the existing `GridRenderer`.

### Interaction

- Left-click: paint/place with active tool.
- Click-drag: paint streaks (for terrain painting).
- Right-click: erase (shortcut).
- Camera: same as gameplay — scroll to zoom, middle-drag/edge scroll to pan.
- Ctrl+Z: undo. Ctrl+Shift+Z: redo.
- Back to Menu button (with unsaved changes warning).

### Undo/Redo

Command pattern with an action stack:

- Each action (`EditorAction`) stores the before/after state of affected cells.
- `Do()` applies the change, `Undo()` reverts it.
- Drag-painting batches into a single action (one Ctrl+Z undoes the whole stroke).
- Stack has a configurable max depth.

### Symmetry tools

Toggle-able symmetry modes — when active, every paint/place/erase action is automatically mirrored:

- **Left ↔ Right**: mirrors across the vertical center axis.
- **Top ↔ Bottom**: mirrors across the horizontal center axis.
- **Diagonal (TL ↔ BR)**: mirrors across the top-left to bottom-right diagonal.
- **Diagonal (TR ↔ BL)**: mirrors across the top-right to bottom-left diagonal.

Multiple symmetry modes can be active simultaneously (e.g., Left↔Right + Top↔Bottom gives 4-way symmetry).

For units, the mirrored copies are assigned to a configurable "mirror slot" — e.g., you paint slot 1 units on the left and the mirror auto-places slot 2 units on the right. The slot mapping is shown in the toolbar next to each symmetry toggle.

The mirrored actions are bundled into the same undo step as the original action.

### Limitations (MVP)

- No grid resize after creation — set dimensions on New Map.
- No copy/paste, selection, or fill tools.

## 4. Main Menu & Navigation

### Main Menu scene (Godot main scene)

Three centered buttons:
- **Play Test**: loads current test gameplay scene directly (temporary, for dev).
- **Play vs AI**: navigates to map select flow.
- **Map Editor**: navigates to editor scene.

### Play vs AI flow

1. **Map Select screen**: lists available `.json` maps from `godot/Assets/Maps/`. Click one to proceed.
2. **Slot Config screen**: shows the map's slots. Player assigns themselves to a slot; others marked "AI (inactive)". Start button launches the game.
3. AI does nothing for now — this sets up the slot → player mapping plumbing for when AI is implemented.

### Navigation

- Scene transitions via `SceneTree.ChangeSceneToFile()`.
- Back button on every sub-screen.
- Editor warns on unsaved changes before navigating away.
- Minimal styling — functional, readable. Visual polish is separate work.

## 5. File Organization

### New simulation files

```
src/Blocker.Simulation/Maps/
├── MapLoader.cs          — updated: add Load(MapData, assignments) overload
├── MapData.cs            — new: MapData, GroundEntry, UnitEntry records
└── SlotAssignment.cs     — new: slot-to-player mapping
```

### New Godot files

```
godot/Scripts/
├── Maps/
│   ├── MapSerializer.cs      — JSON ↔ MapData (System.Text.Json)
│   └── MapFileManager.cs     — list/save/load from Assets/Maps/
├── Editor/
│   ├── MapEditorScene.cs     — main editor controller
│   ├── EditorToolbar.cs      — tool/slot selection UI
│   ├── EditorAction.cs       — undo/redo command interface
│   └── EditorActionStack.cs  — undo/redo stack management
└── UI/
    ├── MainMenu.cs           — main menu buttons + navigation
    ├── MapSelectScreen.cs    — list maps, pick one
    └── SlotConfigScreen.cs   — assign players to slots

godot/Scenes/
├── MainMenu.tscn             — new main scene (replaces current main)
├── MapEditor.tscn            — editor scene
├── MapSelect.tscn            — map selection screen
└── SlotConfig.tscn           — slot configuration screen

godot/Assets/Maps/
└── (saved .json map files)
```

### Reuse

- The editor scene reuses `GridRenderer` — no duplicate rendering code.
- `MapData` is the shared contract between editor, serializer, loader, and future generators.
