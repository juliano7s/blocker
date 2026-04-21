# Map Editor Improvements Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Bring the Godot map editor to feature parity with the TypeScript prototype — adding Fill, Pick, Select+Move, and Line tools; replacing broken live-symmetry with one-shot mirror operations; adding a status bar; and upgrading all palette buttons to icon+label cards.

**Architecture:** A new `EditorMode` enum (Paint/Fill/Pick/Select/Line/Erase) separates *how to paint* from *what to paint* (`EditorTool` stays for tile-type selection). Tool-mode buttons live in the top bar. Two new `Node2D` overlay nodes handle selection and line-preview drawing. Mirror operations are standalone methods on `MapEditorScene`.

**Tech Stack:** Godot 4, C#, existing `EditorActionStack` / `CellSnapshot` / `GameConfig` APIs.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `godot/Scripts/Editor/MapEditorScene.cs` | Modify | Tool state machine, Fill/Pick/Line/Select logic, mirror ops, status bar, SetConfig fix |
| `godot/Scripts/Editor/EditorToolbar.cs` | Modify | Tool-mode buttons in top bar, mirror section, icon tile buttons, remove symmetry |
| `godot/Scripts/Editor/EditorMode.cs` | **Create** | `EditorMode` enum (Paint, Fill, Pick, Select, Line, Erase) |
| `godot/Scripts/Editor/SelectionOverlay.cs` | **Create** | Node2D: draws selection rect, moving preview |
| `godot/Scripts/Editor/LinePreviewOverlay.cs` | **Create** | Node2D: draws Bresenham line preview |
| `godot/Scripts/Editor/SymmetryMirror.cs` | **Delete** | No longer used |

---

## Task 1: Fix unit icon config bug

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs` (around line 96–99)

- [ ] **Step 1: Swap SetConfig call order in `_Ready()`**

In `MapEditorScene.cs`, find the toolbar setup block (~line 96) and move `SetConfig` **after** `AddChild`:

```csharp
// Before (broken):
_toolbar = new EditorToolbar { Name = "EditorToolbar" };
_toolbar.SetConfig(_config);
_uiLayer.AddChild(_toolbar);

// After (fixed):
_toolbar = new EditorToolbar { Name = "EditorToolbar" };
_uiLayer.AddChild(_toolbar);      // _Ready() fires here, creating _unitButtons
_toolbar.SetConfig(_config);      // now the list is populated
```

- [ ] **Step 2: Build and verify**

```bash
dotnet build blocker.sln
```
Expected: no errors. Open Godot → Map Editor → unit buttons should show block icons for the active slot.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs
git commit -m "fix(editor): set toolbar config after AddChild so unit icons render"
```

---

## Task 2: Create EditorMode enum

**Files:**
- Create: `godot/Scripts/Editor/EditorMode.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace Blocker.Game.Editor;

public enum EditorMode
{
    Paint,   // left-click paints selected tile; right-click erases
    Fill,    // flood-fill from clicked cell with selected tile
    Pick,    // read tile under cursor → set tool, switch back to Paint
    Select,  // drag to select rectangle; drag inside to move; Delete to clear
    Line,    // click start + drag → Bresenham preview → release to commit
    Erase    // always erases on left-click
}
```

- [ ] **Step 2: Build**

```bash
dotnet build blocker.sln
```
Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Editor/EditorMode.cs
git commit -m "feat(editor): add EditorMode enum"
```

---

## Task 3: Add tool-mode state to MapEditorScene

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`
- Modify: `godot/Scripts/Editor/EditorToolbar.cs`

- [ ] **Step 1: Add `ToolModeSelected` event to EditorToolbar**

In `EditorToolbar.cs`, add to the events block at the top of the class:

```csharp
public event Action<EditorMode>? ToolModeSelected;
```

- [ ] **Step 2: Add tool-mode buttons to the top bar in `BuildTopBar()`**

In `EditorToolbar.BuildTopBar()`, after the `AddSeparator(hbox)` call that follows the Load/Test buttons, insert:

```csharp
AddSeparator(hbox);

var toolModes = new (string Label, string Key, EditorMode Mode)[]
{
    ("Paint", "P", EditorMode.Paint),
    ("Fill",  "F", EditorMode.Fill),
    ("Pick",  "K", EditorMode.Pick),
    ("Select","S", EditorMode.Select),
    ("Line",  "L", EditorMode.Line),
    ("Erase", "E", EditorMode.Erase),
};

_toolModeButtons = new List<Button>();
foreach (var (label, key, mode) in toolModes)
{
    var btn = new Button
    {
        Text = $"{label} [{key}]",
        CustomMinimumSize = new Vector2(0, 30),
        ToggleMode = true
    };
    btn.Pressed += () =>
    {
        SetActiveToolMode(mode, btn);
        ToolModeSelected?.Invoke(mode);
    };
    hbox.AddChild(btn);
    _toolModeButtons.Add(btn);
}
// Highlight Paint as default
if (_toolModeButtons.Count > 0)
    SetActiveToolMode(EditorMode.Paint, _toolModeButtons[0]);
```

Add the field near the top of `EditorToolbar`:

```csharp
private List<Button> _toolModeButtons = new();
```

Add the helper after `SelectSlot`:

```csharp
private void SetActiveToolMode(EditorMode mode, Button btn)
{
    foreach (var b in _toolModeButtons)
        b.ButtonPressed = b == btn;
}

public void HighlightToolMode(EditorMode mode)
{
    int idx = (int)mode;
    for (int i = 0; i < _toolModeButtons.Count; i++)
        _toolModeButtons[i].ButtonPressed = i == idx;
}
```

- [ ] **Step 3: Wire `ToolModeSelected` in MapEditorScene**

Add the field at the top of `MapEditorScene`:

```csharp
private EditorMode _currentMode = EditorMode.Paint;
```

In `_Ready()`, after wiring the existing toolbar events, add:

```csharp
_toolbar.ToolModeSelected += OnToolModeSelected;
```

Add the handler:

```csharp
private void OnToolModeSelected(EditorMode mode)
{
    _currentMode = mode;
    // Cancel any in-progress operations when switching modes
    if (mode != EditorMode.Line) CancelLine();
    if (mode != EditorMode.Select) CancelSelection();
}
```

Add stub methods (to be filled in later tasks):

```csharp
private void CancelLine() { }
private void CancelSelection() { }
```

- [ ] **Step 4: Wire keyboard shortcuts in `_UnhandledInput()`**

In `MapEditorScene._UnhandledInput()`, inside the `InputEventKey` block, after the undo/redo handling:

```csharp
if (key.Pressed && !key.Echo && !key.CtrlPressed)
{
    EditorMode? newMode = key.Keycode switch
    {
        Key.P => EditorMode.Paint,
        Key.F => EditorMode.Fill,
        Key.K => EditorMode.Pick,
        Key.S => EditorMode.Select,
        Key.L => EditorMode.Line,
        Key.E => EditorMode.Erase,
        _ => null
    };
    if (newMode.HasValue)
    {
        OnToolModeSelected(newMode.Value);
        _toolbar.HighlightToolMode(newMode.Value);
        GetViewport().SetInputAsHandled();
        return;
    }
}
```

- [ ] **Step 5: Make sidebar tile buttons reset mode to Paint**

In `EditorToolbar.BuildSidebar()`, replace the lambdas for `GroundSelected`, `TerrainSelected`, and `BlockSelected` so they also reset tool mode. Change `AddToolButton` calls and `AddUnitToolButton` calls to also invoke `ToolModeSelected?.Invoke(EditorMode.Paint)` and call `HighlightToolMode(EditorMode.Paint)` at the end.

Easiest: update `AddToolButton` to accept an optional "also reset mode" bool, or just inline it for the tile buttons. The simplest approach — update each `AddToolButton` call that maps to a tile:

```csharp
// Ground buttons example — same pattern for terrain and unit buttons:
AddToolButton(vbox, "Normal", () =>
{
    GroundSelected?.Invoke(GroundType.Normal);
    ToolModeSelected?.Invoke(EditorMode.Paint);
    HighlightToolMode(EditorMode.Paint);
});
```

Apply this pattern to all ground, terrain, and unit (including `AddUnitToolButton`) buttons.

For `AddUnitToolButton`, update the `Pressed` lambda:

```csharp
btn.Pressed += () =>
{
    SetActiveToolButton(btn);
    BlockSelected?.Invoke(type, isRooted);
    ToolModeSelected?.Invoke(EditorMode.Paint);
    HighlightToolMode(EditorMode.Paint);
};
```

- [ ] **Step 6: Build and verify**

```bash
dotnet build blocker.sln
```
Expected: no errors. Open Godot → Map Editor. Top bar should show 6 tool buttons. Clicking them or pressing P/F/K/S/L/E should highlight the matching button. Clicking a tile in the sidebar should revert to Paint.

- [ ] **Step 7: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs godot/Scripts/Editor/EditorToolbar.cs
git commit -m "feat(editor): add EditorMode tool buttons to top bar with keyboard shortcuts"
```

---

## Task 4: Fill tool

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`

- [ ] **Step 1: Add flood-fill method**

Add to `MapEditorScene`:

```csharp
private void FloodFill(GridPos start)
{
    var grid = _editorState.Grid;
    if (!grid.InBounds(start)) return;

    var action = new EditorAction();
    var startCell = grid[start.X, start.Y];
    var targetGround = startCell.Ground;
    var targetTerrain = startCell.Terrain;

    var visited = new HashSet<(int, int)>();
    var stack = new Stack<GridPos>();
    stack.Push(start);

    while (stack.Count > 0)
    {
        var pos = stack.Pop();
        if (!grid.InBounds(pos)) continue;
        if (visited.Contains((pos.X, pos.Y))) continue;
        visited.Add((pos.X, pos.Y));

        var cell = grid[pos.X, pos.Y];
        var existingBlock = _editorState.GetBlockAt(pos);

        bool matches = _currentTool switch
        {
            EditorTool.GroundPaint  => cell.Ground == targetGround,
            EditorTool.TerrainPaint => cell.Terrain == targetTerrain,
            _ => false
        };
        if (!matches) continue;

        // Record before
        action.Before.Add(new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
            existingBlock?.Type, existingBlock != null ? (int?)existingBlock.PlayerId : null));

        // Apply
        if (_currentTool == EditorTool.GroundPaint)
            cell.Ground = _currentGround;
        else if (_currentTool == EditorTool.TerrainPaint)
        {
            if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
            cell.Terrain = _currentTerrain;
        }

        // Record after
        var newBlock = _editorState.GetBlockAt(pos);
        action.After.Add(new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
            newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null));

        stack.Push(new GridPos(pos.X + 1, pos.Y));
        stack.Push(new GridPos(pos.X - 1, pos.Y));
        stack.Push(new GridPos(pos.X, pos.Y + 1));
        stack.Push(new GridPos(pos.X, pos.Y - 1));
    }

    if (action.Before.Count > 0)
        _actionStack.Push(action);

    RefreshRenderer();
}
```

- [ ] **Step 2: Dispatch fill in `_UnhandledInput()`**

In `_UnhandledInput()`, inside the left-click `mouseButton.Pressed` block, before the existing `StartDrag` call, add a mode check:

```csharp
if (mouseButton.ButtonIndex == MouseButton.Left)
{
    if (mouseButton.Pressed)
    {
        var gridPos = GetGridPos(mouseButton.GlobalPosition);
        if (_currentMode == EditorMode.Fill)
        {
            FloodFill(gridPos);
            GetViewport().SetInputAsHandled();
            return;
        }
        // existing Paint/Erase drag path continues below
        StartDrag(_currentMode == EditorMode.Erase);
        ApplyToolAt(gridPos, _currentMode == EditorMode.Erase);
    }
    // ...
}
```

Also update the right-click handler to respect Erase mode:

```csharp
if (mouseButton.ButtonIndex == MouseButton.Right)
{
    if (mouseButton.Pressed)
    {
        StartDrag(true);
        var gridPos = GetGridPos(mouseButton.GlobalPosition);
        ApplyToolAt(gridPos, true);
    }
    // ...
}
```

Update `ApplyToolAt` to also treat `EditorMode.Erase` as erase regardless of `_currentTool`:

```csharp
private void ApplyToolAt(GridPos pos, bool isErase)
{
    if (_currentMode == EditorMode.Erase) isErase = true;
    // ... rest unchanged
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build blocker.sln
```
Expected: no errors. Open Godot → Map Editor. Press F, click on a ground tile — adjacent same-type tiles should fill. Ctrl+Z undoes in one step.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat(editor): add Fill tool (flood fill)"
```

---

## Task 5: Pick tool

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`

- [ ] **Step 1: Add pick logic in `_UnhandledInput()`**

In the left-click pressed block (after the Fill check added in Task 4):

```csharp
if (_currentMode == EditorMode.Pick)
{
    var gridPos = GetGridPos(mouseButton.GlobalPosition);
    if (_editorState.Grid.InBounds(gridPos))
        PickTileAt(gridPos);
    GetViewport().SetInputAsHandled();
    return;
}
```

- [ ] **Step 2: Add `PickTileAt` method**

```csharp
private void PickTileAt(GridPos pos)
{
    var cell = _editorState.Grid[pos.X, pos.Y];
    var block = _editorState.GetBlockAt(pos);

    if (block != null)
    {
        _currentBlock = block.Type;
        _currentBlockRooted = block.IsFullyRooted && block.Type != BlockType.Wall;
        _currentSlot = block.PlayerId;
        _currentTool = EditorTool.UnitPlace;
    }
    else if (cell.Terrain != TerrainType.None)
    {
        _currentTerrain = cell.Terrain;
        _currentTool = EditorTool.TerrainPaint;
    }
    else
    {
        _currentGround = cell.Ground;
        _currentTool = EditorTool.GroundPaint;
    }

    // Switch back to Paint mode
    _currentMode = EditorMode.Paint;
    _toolbar.HighlightToolMode(EditorMode.Paint);
}
```

- [ ] **Step 3: Build and verify**

```bash
dotnet build blocker.sln
```
Expected: no errors. Open Godot → Map Editor. Press K, click a terrain tile — mode should revert to Paint and the picked tile type should now paint on the next click.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat(editor): add Pick (eyedropper) tool"
```

---

## Task 6: Line tool with preview overlay

**Files:**
- Create: `godot/Scripts/Editor/LinePreviewOverlay.cs`
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`

- [ ] **Step 1: Create `LinePreviewOverlay`**

```csharp
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Editor;

public partial class LinePreviewOverlay : Node2D
{
    public List<Vector2I> Points { get; set; } = [];
    public float CellSize { get; set; } = 24f;
    public float GridPadding { get; set; }

    private static readonly Color PreviewFill   = new(1f, 1f, 1f, 0.22f);
    private static readonly Color PreviewBorder = new(1f, 1f, 1f, 0.55f);

    public override void _Draw()
    {
        if (Points.Count == 0) return;

        foreach (var p in Points)
        {
            float px = p.X * CellSize + GridPadding;
            float py = p.Y * CellSize + GridPadding;
            DrawRect(new Rect2(px + 1, py + 1, CellSize - 2, CellSize - 2), PreviewFill);
        }

        if (Points.Count > 1)
        {
            var first = Points[0];
            var last  = Points[^1];
            var from = new Vector2(first.X * CellSize + GridPadding + CellSize * 0.5f,
                                   first.Y * CellSize + GridPadding + CellSize * 0.5f);
            var to   = new Vector2(last.X  * CellSize + GridPadding + CellSize * 0.5f,
                                   last.Y  * CellSize + GridPadding + CellSize * 0.5f);
            DrawDashedLine(from, to, PreviewBorder, 1.5f, 4f);
        }
    }
}
```

- [ ] **Step 2: Add line state fields to `MapEditorScene`**

```csharp
// Line tool state
private bool _lineActive;
private GridPos _lineStart;
private readonly List<Vector2I> _linePreviewPoints = [];
private LinePreviewOverlay _lineOverlay = null!;
```

- [ ] **Step 3: Create and wire the overlay in `_Ready()`**

After creating `_guideOverlay`, add:

```csharp
_lineOverlay = new LinePreviewOverlay
{
    Name = "LinePreviewOverlay",
    CellSize = GridRenderer.CellSize,
    GridPadding = GridRenderer.GridPadding
};
AddChild(_lineOverlay);
```

- [ ] **Step 4: Update `CancelLine()`**

Replace the empty stub:

```csharp
private void CancelLine()
{
    _lineActive = false;
    _linePreviewPoints.Clear();
    _lineOverlay.Points = [];
    _lineOverlay.QueueRedraw();
}
```

- [ ] **Step 5: Add Bresenham helper**

```csharp
private static List<Vector2I> Bresenham(int x0, int y0, int x1, int y1)
{
    var pts = new List<Vector2I>();
    int dx = Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
    int dy = -Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
    int err = dx + dy;
    while (true)
    {
        pts.Add(new Vector2I(x0, y0));
        if (x0 == x1 && y0 == y1) break;
        int e2 = 2 * err;
        if (e2 >= dy) { err += dy; x0 += sx; }
        if (e2 <= dx) { err += dx; y0 += sy; }
    }
    return pts;
}
```

- [ ] **Step 6: Handle line tool input in `_UnhandledInput()`**

Add a dedicated section *before* the existing left-click handler, to intercept when `_currentMode == EditorMode.Line`:

```csharp
// Line tool intercept
if (_currentMode == EditorMode.Line)
{
    if (@event is InputEventMouseButton lb && lb.ButtonIndex == MouseButton.Left)
    {
        if (lb.Pressed)
        {
            _lineActive = true;
            _lineStart = GetGridPos(lb.GlobalPosition);
            _linePreviewPoints.Clear();
            _linePreviewPoints.Add(new Vector2I(_lineStart.X, _lineStart.Y));
            _lineOverlay.Points = new List<Vector2I>(_linePreviewPoints);
            _lineOverlay.QueueRedraw();
        }
        else if (_lineActive)
        {
            CommitLine(GetGridPos(lb.GlobalPosition));
        }
        GetViewport().SetInputAsHandled();
        return;
    }
    if (@event is InputEventMouseMotion lm && _lineActive)
    {
        var end = GetGridPos(lm.GlobalPosition);
        _linePreviewPoints.Clear();
        _linePreviewPoints.AddRange(Bresenham(_lineStart.X, _lineStart.Y, end.X, end.Y));
        _lineOverlay.Points = new List<Vector2I>(_linePreviewPoints);
        _lineOverlay.QueueRedraw();
        GetViewport().SetInputAsHandled();
        return;
    }
}
```

Also handle Esc for line cancel — add to the key-handling block:

```csharp
if (key.Keycode == Key.Escape)
{
    CancelLine();
    CancelSelection();
    GetViewport().SetInputAsHandled();
    return;
}
```

- [ ] **Step 7: Add `CommitLine`**

```csharp
private void CommitLine(GridPos end)
{
    var pts = Bresenham(_lineStart.X, _lineStart.Y, end.X, end.Y);
    var action = new EditorAction();
    var grid = _editorState.Grid;

    foreach (var p in pts)
    {
        var pos = new GridPos(p.X, p.Y);
        if (!grid.InBounds(pos)) continue;

        var cell = grid[p.X, p.Y];
        var existing = _editorState.GetBlockAt(pos);
        action.Before.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
            existing?.Type, existing != null ? (int?)existing.PlayerId : null));

        ApplyCurrentTileToCell(pos);

        var after = _editorState.GetBlockAt(pos);
        action.After.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
            after?.Type, after != null ? (int?)after.PlayerId : null));
    }

    if (action.Before.Count > 0) _actionStack.Push(action);
    _lineActive = false;
    _linePreviewPoints.Clear();
    _lineOverlay.Points = [];
    _lineOverlay.QueueRedraw();
    RefreshRenderer();
}
```

- [ ] **Step 8: Add `ApplyCurrentTileToCell` helper (shared by line and future uses)**

```csharp
private void ApplyCurrentTileToCell(GridPos pos)
{
    var grid = _editorState.Grid;
    if (!grid.InBounds(pos)) return;
    var cell = grid[pos.X, pos.Y];
    var existing = _editorState.GetBlockAt(pos);

    switch (_currentTool)
    {
        case EditorTool.GroundPaint:
            cell.Ground = _currentGround;
            break;
        case EditorTool.TerrainPaint:
            if (existing != null) _editorState.RemoveBlock(existing);
            cell.Terrain = _currentTerrain;
            break;
        case EditorTool.UnitPlace:
            if (existing != null) _editorState.RemoveBlock(existing);
            if (cell.Terrain != TerrainType.None) break;
            EnsurePlayerExists(_currentSlot);
            var placed = _editorState.AddBlock(_currentBlock, _currentSlot, pos);
            if (_currentBlockRooted && placed.Type != BlockType.Wall)
            {
                placed.State = BlockState.Rooted;
                placed.RootProgress = Constants.RootTicks;
            }
            break;
        case EditorTool.Eraser:
            cell.Ground = GroundType.Normal;
            cell.Terrain = TerrainType.None;
            if (existing != null) _editorState.RemoveBlock(existing);
            break;
    }
}
```

Note: `CommitLine`'s before/after snapshot logic needs a slight fix — capture `after` state *after* applying, not before. Update the loop:

```csharp
foreach (var p in pts)
{
    var pos = new GridPos(p.X, p.Y);
    if (!grid.InBounds(pos)) continue;
    var cell = grid[p.X, p.Y];
    var existing = _editorState.GetBlockAt(pos);
    action.Before.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
        existing?.Type, existing != null ? (int?)existing.PlayerId : null));

    ApplyCurrentTileToCell(pos);

    var newBlock = _editorState.GetBlockAt(pos);
    action.After.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
        newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null));
}
```

- [ ] **Step 9: Build and verify**

```bash
dotnet build blocker.sln
```
Open Godot → Map Editor. Press L, click-drag → should see a dotted line preview following the mouse. Release → line is committed. Ctrl+Z undoes.

- [ ] **Step 10: Commit**

```bash
git add godot/Scripts/Editor/LinePreviewOverlay.cs godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat(editor): add Line tool with Bresenham preview overlay"
```

---

## Task 7: Select+Move tool with overlay

**Files:**
- Create: `godot/Scripts/Editor/SelectionOverlay.cs`
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`

- [ ] **Step 1: Create `SelectionOverlay`**

```csharp
using Godot;
using Blocker.Simulation.Core;
using Blocker.Simulation.Blocks;

namespace Blocker.Game.Editor;

public enum SelectPhase { Idle, Drawing, Ready, Moving }

public partial class SelectionOverlay : Node2D
{
    public SelectPhase Phase { get; set; } = SelectPhase.Idle;
    public Rect2I Rect     { get; set; }
    public Vector2I Offset { get; set; }
    public float CellSize  { get; set; } = 24f;
    public float GridPadding { get; set; }

    private static readonly Color FillDrawing = new(0.29f, 0.62f, 1f, 0.18f);
    private static readonly Color FillReady   = new(0.29f, 0.62f, 1f, 0.08f);
    private static readonly Color Border      = new(0.29f, 0.62f, 1f, 1f);
    private static readonly Color SourceDim   = new(0f, 0f, 0f, 0.30f);

    public override void _Draw()
    {
        if (Phase == SelectPhase.Idle) return;

        float cs = CellSize, gp = GridPadding;

        if (Phase == SelectPhase.Drawing || Phase == SelectPhase.Ready)
        {
            var px = Rect.Position.X * cs + gp;
            var py = Rect.Position.Y * cs + gp;
            var pw = Rect.Size.X * cs;
            var ph = Rect.Size.Y * cs;
            var r = new Rect2(px, py, pw, ph);
            DrawRect(r, Phase == SelectPhase.Drawing ? FillDrawing : FillReady);
            DrawRect(r, Border, false, 1.5f);
            if (Phase == SelectPhase.Ready)
            {
                float hs = 5f;
                foreach (var corner in new[] { r.Position, r.Position + new Vector2(pw - hs, 0),
                    r.Position + new Vector2(0, ph - hs), r.Position + new Vector2(pw - hs, ph - hs) })
                    DrawRect(new Rect2(corner, new Vector2(hs, hs)), Border);
            }
        }
        else if (Phase == SelectPhase.Moving)
        {
            // Dim source
            var spx = Rect.Position.X * cs + gp;
            var spy = Rect.Position.Y * cs + gp;
            DrawRect(new Rect2(spx, spy, Rect.Size.X * cs, Rect.Size.Y * cs), SourceDim);

            // Destination outline
            var dx = (Rect.Position.X + Offset.X) * cs + gp;
            var dy = (Rect.Position.Y + Offset.Y) * cs + gp;
            var dw = Rect.Size.X * cs;
            var dh = Rect.Size.Y * cs;
            DrawRect(new Rect2(dx, dy, dw, dh), Border, false, 2f);
        }
    }
}
```

- [ ] **Step 2: Add selection state fields to `MapEditorScene`**

```csharp
// Selection tool state
private SelectPhase _selPhase = SelectPhase.Idle;
private Rect2I _selRect;
private GridPos _selDrawStart;
private GridPos _selMoveStart;
private Vector2I _selOffset;
private GroundType[,]? _selGround;
private TerrainType[,]? _selTerrain;
private (BlockType type, int slot, bool rooted)[,]? _selBlocks;
private SelectionOverlay _selOverlay = null!;
```

- [ ] **Step 3: Create and wire selection overlay in `_Ready()`**

After `_lineOverlay` creation:

```csharp
_selOverlay = new SelectionOverlay
{
    Name = "SelectionOverlay",
    CellSize = GridRenderer.CellSize,
    GridPadding = GridRenderer.GridPadding
};
AddChild(_selOverlay);
```

- [ ] **Step 4: Update `CancelSelection()`**

```csharp
private void CancelSelection()
{
    _selPhase = SelectPhase.Idle;
    _selGround = null;
    _selTerrain = null;
    _selBlocks = null;
    _selOverlay.Phase = SelectPhase.Idle;
    _selOverlay.QueueRedraw();
}
```

- [ ] **Step 5: Add selection input handling in `_UnhandledInput()`**

Add a dedicated block *before* the existing left-click handler, to intercept when `_currentMode == EditorMode.Select`:

```csharp
if (_currentMode == EditorMode.Select)
{
    if (@event is InputEventMouseButton smb && smb.ButtonIndex == MouseButton.Left)
    {
        if (smb.Pressed)
        {
            var gp = GetGridPos(smb.GlobalPosition);
            SelectMouseDown(gp);
        }
        else
        {
            var gp = GetGridPos(smb.GlobalPosition);
            SelectMouseUp(gp);
        }
        GetViewport().SetInputAsHandled();
        return;
    }
    if (@event is InputEventMouseMotion smm)
    {
        if (_selPhase == SelectPhase.Drawing || _selPhase == SelectPhase.Moving)
        {
            SelectMouseMove(GetGridPos(smm.GlobalPosition));
            GetViewport().SetInputAsHandled();
            return;
        }
    }
}
```

Also handle Delete key for selection (add to the key-handling block):

```csharp
if (key.Keycode == Key.Delete || key.Keycode == Key.Backspace)
{
    DeleteSelection();
    GetViewport().SetInputAsHandled();
    return;
}
```

- [ ] **Step 6: Add selection helper methods**

```csharp
private Rect2I NormalizeSelRect(GridPos a, GridPos b)
{
    int x = Math.Max(0, Math.Min(a.X, b.X));
    int y = Math.Max(0, Math.Min(a.Y, b.Y));
    int x2 = Math.Min(_mapWidth - 1, Math.Max(a.X, b.X));
    int y2 = Math.Min(_mapHeight - 1, Math.Max(a.Y, b.Y));
    return new Rect2I(x, y, x2 - x + 1, y2 - y + 1);
}

private bool InsideSelRect(GridPos gp) =>
    _selPhase != SelectPhase.Idle
    && gp.X >= _selRect.Position.X && gp.X < _selRect.Position.X + _selRect.Size.X
    && gp.Y >= _selRect.Position.Y && gp.Y < _selRect.Position.Y + _selRect.Size.Y;

private void SelectMouseDown(GridPos gp)
{
    if (_selPhase == SelectPhase.Ready && InsideSelRect(gp))
    {
        _selPhase = SelectPhase.Moving;
        _selMoveStart = gp;
        _selOffset = Vector2I.Zero;
    }
    else
    {
        if (_selPhase == SelectPhase.Ready) CancelSelection();
        _selPhase = SelectPhase.Drawing;
        _selDrawStart = gp;
        _selRect = NormalizeSelRect(gp, gp);
        _selGround = null; _selTerrain = null; _selBlocks = null;
        _selOffset = Vector2I.Zero;
        UpdateSelOverlay();
    }
}

private void SelectMouseMove(GridPos gp)
{
    if (_selPhase == SelectPhase.Drawing)
    {
        _selRect = NormalizeSelRect(_selDrawStart, gp);
        UpdateSelOverlay();
    }
    else if (_selPhase == SelectPhase.Moving)
    {
        _selOffset = new Vector2I(gp.X - _selMoveStart.X, gp.Y - _selMoveStart.Y);
        UpdateSelOverlay();
    }
}

private void SelectMouseUp(GridPos gp)
{
    if (_selPhase == SelectPhase.Drawing)
    {
        _selRect = NormalizeSelRect(_selDrawStart, gp);
        CaptureSelection();
        _selPhase = SelectPhase.Ready;
        UpdateSelOverlay();
    }
    else if (_selPhase == SelectPhase.Moving)
    {
        if (_selOffset != Vector2I.Zero) CommitSelection();
        else { _selPhase = SelectPhase.Ready; UpdateSelOverlay(); }
    }
}

private void CaptureSelection()
{
    int w = _selRect.Size.X, h = _selRect.Size.Y;
    int ox = _selRect.Position.X, oy = _selRect.Position.Y;
    _selGround = new GroundType[w, h];
    _selTerrain = new TerrainType[w, h];
    _selBlocks = new (BlockType, int, bool)[w, h];
    var grid = _editorState.Grid;
    for (int dy = 0; dy < h; dy++)
        for (int dx = 0; dx < w; dx++)
        {
            var cell = grid[ox + dx, oy + dy];
            var blk = _editorState.GetBlockAt(new GridPos(ox + dx, oy + dy));
            _selGround[dx, dy] = cell.Ground;
            _selTerrain[dx, dy] = cell.Terrain;
            _selBlocks[dx, dy] = blk != null
                ? (blk.Type, blk.PlayerId, blk.IsFullyRooted && blk.Type != BlockType.Wall)
                : (BlockType.Builder, -1, false);
        }
}

private void CommitSelection()
{
    if (_selGround == null) { CancelSelection(); return; }

    var action = new EditorAction();
    int w = _selRect.Size.X, h = _selRect.Size.Y;
    int ox = _selRect.Position.X, oy = _selRect.Position.Y;
    int dx = _selOffset.X, dy = _selOffset.Y;
    var grid = _editorState.Grid;

    // Record & clear source
    for (int row = 0; row < h; row++)
        for (int col = 0; col < w; col++)
        {
            var sp = new GridPos(ox + col, oy + row);
            var sc = grid[sp.X, sp.Y];
            var sb = _editorState.GetBlockAt(sp);
            action.Before.Add(new CellSnapshot(sp.X, sp.Y, sc.Ground, sc.Terrain,
                sb?.Type, sb != null ? (int?)sb.PlayerId : null));
            if (sb != null) _editorState.RemoveBlock(sb);
            sc.Ground = GroundType.Normal;
            sc.Terrain = TerrainType.None;
            action.After.Add(new CellSnapshot(sp.X, sp.Y, GroundType.Normal, TerrainType.None, null, null));
        }

    // Write destination
    for (int row = 0; row < h; row++)
        for (int col = 0; col < w; col++)
        {
            var dp = new GridPos(ox + col + dx, oy + row + dy);
            if (!grid.InBounds(dp)) continue;
            var dc = grid[dp.X, dp.Y];
            var db = _editorState.GetBlockAt(dp);
            action.Before.Add(new CellSnapshot(dp.X, dp.Y, dc.Ground, dc.Terrain,
                db?.Type, db != null ? (int?)db.PlayerId : null));
            if (db != null) _editorState.RemoveBlock(db);
            dc.Ground = _selGround![col, row];
            dc.Terrain = _selTerrain![col, row];
            var (btype, bslot, brooted) = _selBlocks![col, row];
            if (bslot >= 0 && dc.Terrain == TerrainType.None)
            {
                EnsurePlayerExists(bslot);
                var placed = _editorState.AddBlock(btype, bslot, dp);
                if (brooted && btype != BlockType.Wall)
                { placed.State = BlockState.Rooted; placed.RootProgress = Constants.RootTicks; }
            }
            var newb = _editorState.GetBlockAt(dp);
            action.After.Add(new CellSnapshot(dp.X, dp.Y, dc.Ground, dc.Terrain,
                newb?.Type, newb != null ? (int?)newb.PlayerId : null));
        }

    _actionStack.Push(action);
    CancelSelection();
    RefreshRenderer();
}

private void DeleteSelection()
{
    if (_selPhase != SelectPhase.Ready || _selGround == null) return;
    var action = new EditorAction();
    var grid = _editorState.Grid;
    for (int row = 0; row < _selRect.Size.Y; row++)
        for (int col = 0; col < _selRect.Size.X; col++)
        {
            var p = new GridPos(_selRect.Position.X + col, _selRect.Position.Y + row);
            var c = grid[p.X, p.Y];
            var b = _editorState.GetBlockAt(p);
            action.Before.Add(new CellSnapshot(p.X, p.Y, c.Ground, c.Terrain,
                b?.Type, b != null ? (int?)b.PlayerId : null));
            if (b != null) _editorState.RemoveBlock(b);
            c.Ground = GroundType.Normal;
            c.Terrain = TerrainType.None;
            action.After.Add(new CellSnapshot(p.X, p.Y, GroundType.Normal, TerrainType.None, null, null));
        }
    _actionStack.Push(action);
    CancelSelection();
    RefreshRenderer();
}

private void UpdateSelOverlay()
{
    _selOverlay.Phase = _selPhase;
    _selOverlay.Rect = _selRect;
    _selOverlay.Offset = _selOffset;
    _selOverlay.QueueRedraw();
}
```

- [ ] **Step 7: Build and verify**

```bash
dotnet build blocker.sln
```
Open Godot → Map Editor. Press S. Drag to draw a selection. Drag inside it to move. Release → tiles move. Ctrl+Z undoes. Delete clears selection. Esc cancels.

- [ ] **Step 8: Commit**

```bash
git add godot/Scripts/Editor/SelectionOverlay.cs godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat(editor): add Select+Move tool with overlay"
```

---

## Task 8: One-shot mirror (replace live symmetry)

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`
- Modify: `godot/Scripts/Editor/EditorToolbar.cs`
- Delete: `godot/Scripts/Editor/SymmetryMirror.cs`

- [ ] **Step 1: Add mirror fields to `MapEditorScene`**

```csharp
private bool _mirrorTeams = true;
```

- [ ] **Step 2: Add `MirrorSlot` helper**

```csharp
private int MirrorSlot(int slot)
{
    // Pairs: 0↔1, 2↔3, 4↔5
    if (slot % 2 == 0 && slot + 1 < _slotCount) return slot + 1;
    if (slot % 2 == 1 && slot - 1 >= 0) return slot - 1;
    return slot;
}
```

- [ ] **Step 3: Add `CopyCellMirrored` helper**

```csharp
// Copies the state at (srcX, srcY) into (dstX, dstY), recording undo in action.
// If mirrorSlot == true, swaps player slot pairings.
private void CopyCellMirrored(int srcX, int srcY, int dstX, int dstY, EditorAction action)
{
    var grid = _editorState.Grid;
    var src = grid[srcX, srcY];
    var dst = grid[dstX, dstY];
    var srcBlk = _editorState.GetBlockAt(new GridPos(srcX, srcY));
    var dstBlk = _editorState.GetBlockAt(new GridPos(dstX, dstY));

    action.Before.Add(new CellSnapshot(dstX, dstY, dst.Ground, dst.Terrain,
        dstBlk?.Type, dstBlk != null ? (int?)dstBlk.PlayerId : null));

    if (dstBlk != null) _editorState.RemoveBlock(dstBlk);
    dst.Ground = src.Ground;
    dst.Terrain = src.Terrain;

    if (srcBlk != null)
    {
        int mirroredSlot = _mirrorTeams ? MirrorSlot(srcBlk.PlayerId) : srcBlk.PlayerId;
        EnsurePlayerExists(mirroredSlot);
        var placed = _editorState.AddBlock(srcBlk.Type, mirroredSlot, new GridPos(dstX, dstY));
        if (srcBlk.IsFullyRooted && srcBlk.Type != BlockType.Wall)
        { placed.State = BlockState.Rooted; placed.RootProgress = Constants.RootTicks; }
    }
    else if (!_mirrorTeams && dstBlk != null)
    {
        // Teams OFF: clear any unit on the destination side (already removed above)
    }

    var newBlk = _editorState.GetBlockAt(new GridPos(dstX, dstY));
    action.After.Add(new CellSnapshot(dstX, dstY, dst.Ground, dst.Terrain,
        newBlk?.Type, newBlk != null ? (int?)newBlk.PlayerId : null));
}
```

- [ ] **Step 4: Add the six mirror methods**

```csharp
private void MirrorLeftToRight()
{
    var action = new EditorAction();
    for (int y = 0; y < _mapHeight; y++)
        for (int x = 0; x < _mapWidth / 2; x++)
            CopyCellMirrored(x, y, _mapWidth - 1 - x, y, action);
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}

private void MirrorRightToLeft()
{
    var action = new EditorAction();
    for (int y = 0; y < _mapHeight; y++)
        for (int x = _mapWidth - 1; x >= (_mapWidth + 1) / 2; x--)
            CopyCellMirrored(x, y, _mapWidth - 1 - x, y, action);
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}

private void MirrorTopToBottom()
{
    var action = new EditorAction();
    int halfH = _mapHeight / 2;
    for (int y = 0; y < halfH; y++)
        for (int x = 0; x < _mapWidth; x++)
            CopyCellMirrored(x, y, _mapWidth - 1 - x, _mapHeight - 1 - y, action);
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}

private void MirrorBottomToTop()
{
    var action = new EditorAction();
    int halfH = _mapHeight / 2;
    for (int y = _mapHeight - 1; y >= (_mapHeight + 1) / 2; y--)
        for (int x = 0; x < _mapWidth; x++)
            CopyCellMirrored(x, y, _mapWidth - 1 - x, _mapHeight - 1 - y, action);
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}

private void MirrorDiagonalTLBR()
{
    var action = new EditorAction();
    float wf = _mapWidth - 1f, hf = _mapHeight - 1f;
    if (wf <= 0 || hf <= 0) return;
    for (int y = 0; y < _mapHeight; y++)
        for (int x = 0; x < _mapWidth; x++)
        {
            // Only process the TL half (above the diagonal: y * wf < x * hf)
            if ((float)y * wf >= (float)x * hf) continue;
            int mx = (int)Math.Round(y * wf / hf);
            int my = (int)Math.Round(x * hf / wf);
            if (mx < 0 || mx >= _mapWidth || my < 0 || my >= _mapHeight) continue;
            CopyCellMirrored(x, y, mx, my, action);
        }
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}

private void MirrorDiagonalTRBL()
{
    var action = new EditorAction();
    float wf = _mapWidth - 1f, hf = _mapHeight - 1f;
    if (wf <= 0 || hf <= 0) return;
    for (int y = 0; y < _mapHeight; y++)
        for (int x = 0; x < _mapWidth; x++)
        {
            // TR half: above the anti-diagonal (y * wf < (wf - x) * hf / wf ... simplified)
            float normX = x / wf, normY = y / hf;
            if (normX + normY >= 1f) continue; // bottom-left half, skip
            int mx = (int)Math.Round((1f - normY) * wf);
            int my = (int)Math.Round((1f - normX) * hf);
            if (mx < 0 || mx >= _mapWidth || my < 0 || my >= _mapHeight) continue;
            CopyCellMirrored(x, y, mx, my, action);
        }
    if (action.Before.Count > 0) _actionStack.Push(action);
    RefreshRenderer();
}
```

- [ ] **Step 5: Add toolbar events for mirror in `EditorToolbar.cs`**

```csharp
public event Action<MirrorDirection>? MirrorRequested;
public event Action<bool>? MirrorTeamsToggled;
```

Add the enum near the top of `EditorToolbar.cs` (or in its own file):

```csharp
public enum MirrorDirection { LR, RL, TB, BT, DiagTLBR, DiagTRBL }
```

- [ ] **Step 6: Replace symmetry section with mirror section in `BuildSidebar()`**

Remove:
- The `_symLR`, `_symTB`, `_symDiagTLBR`, `_symDiagTRBL` field declarations
- The `AddCheckbox(vbox, "Left/Right")` ... `EmitSymmetry()` block
- The `EmitSymmetry()` method
- The `SymmetryChanged` event declaration

Remove from `EditorToolbar` class fields:
```csharp
// DELETE these four lines:
private CheckBox _symLR = null!;
private CheckBox _symTB = null!;
private CheckBox _symDiagTLBR = null!;
private CheckBox _symDiagTRBL = null!;
```

Add in `BuildSidebar()` after the slot section (where symmetry was):

```csharp
AddSectionLabel(vbox, "Mirror");

var mirrorGrid = new GridContainer { Columns = 2 };
mirrorGrid.AddThemeConstantOverride("h_separation", 2);
mirrorGrid.AddThemeConstantOverride("v_separation", 2);
vbox.AddChild(mirrorGrid);

var mirrorBtns = new (string Label, MirrorDirection Dir)[]
{
    ("L → R", MirrorDirection.LR),
    ("R → L", MirrorDirection.RL),
    ("T → B", MirrorDirection.TB),
    ("B → T", MirrorDirection.BT),
    ("TL→BR", MirrorDirection.DiagTLBR),
    ("TR→BL", MirrorDirection.DiagTRBL),
};
foreach (var (label, dir) in mirrorBtns)
{
    var btn = new Button
    {
        Text = label,
        CustomMinimumSize = new Vector2(0, 28),
        SizeFlagsHorizontal = SizeFlags.ExpandFill
    };
    btn.Pressed += () => MirrorRequested?.Invoke(dir);
    mirrorGrid.AddChild(btn);
}

// Teams toggle
var teamsHBox = new HBoxContainer();
var teamsLabel = new Label { Text = "Teams:" };
teamsLabel.AddThemeFontSizeOverride("font_size", 10);
teamsHBox.AddChild(teamsLabel);

_teamsToggle = new CheckButton { Text = "ON", ButtonPressed = true };
_teamsToggle.Toggled += pressed =>
{
    _teamsToggle.Text = pressed ? "ON" : "OFF";
    MirrorTeamsToggled?.Invoke(pressed);
};
teamsHBox.AddChild(_teamsToggle);
vbox.AddChild(teamsHBox);
```

Add field:
```csharp
private CheckButton _teamsToggle = null!;
```

- [ ] **Step 7: Wire mirror events in `MapEditorScene._Ready()`**

Replace:
```csharp
_toolbar.SymmetryChanged += OnSymmetryChanged;
```
With:
```csharp
_toolbar.MirrorRequested += OnMirrorRequested;
_toolbar.MirrorTeamsToggled += on => _mirrorTeams = on;
```

Add handler:
```csharp
private void OnMirrorRequested(MirrorDirection dir)
{
    switch (dir)
    {
        case MirrorDirection.LR:       MirrorLeftToRight();  break;
        case MirrorDirection.RL:       MirrorRightToLeft();  break;
        case MirrorDirection.TB:       MirrorTopToBottom();  break;
        case MirrorDirection.BT:       MirrorBottomToTop();  break;
        case MirrorDirection.DiagTLBR: MirrorDiagonalTLBR(); break;
        case MirrorDirection.DiagTRBL: MirrorDiagonalTRBL(); break;
    }
}
```

Remove the old `OnSymmetryChanged` handler and `_symmetry` field/usages:
- Remove `private readonly SymmetryMirror _symmetry = new();`
- Remove `_symmetry.GetMirroredPositions(...)` call in `ApplyToolAt` (replace with just the primary position)
- Remove `UpdateSymmetryMap(...)` calls
- Remove `OnSymmetryChanged` method

Update `ApplyToolAt` — replace the mirrored-positions loop with a single-position apply:

```csharp
private void ApplyToolAt(GridPos pos, bool isErase)
{
    if (_currentMode == EditorMode.Erase) isErase = true;
    if (!_editorState.Grid.InBounds(pos)) return;
    if (_dragVisited.Contains((pos.X, pos.Y))) return;
    _dragVisited.Add((pos.X, pos.Y));

    var cell = _editorState.Grid[pos.X, pos.Y];
    var existingBlock = _editorState.GetBlockAt(pos);

    var before = new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
        existingBlock?.Type, existingBlock != null ? (int?)existingBlock.PlayerId : null);

    if (isErase)
    {
        cell.Ground = GroundType.Normal;
        cell.Terrain = TerrainType.None;
        if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
    }
    else
    {
        switch (_currentTool)
        {
            case EditorTool.GroundPaint:
                cell.Ground = _currentGround;
                break;
            case EditorTool.TerrainPaint:
                if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
                cell.Terrain = _currentTerrain;
                break;
            case EditorTool.UnitPlace:
                if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
                if (cell.Terrain != TerrainType.None) break;
                EnsurePlayerExists(_currentSlot);
                var placed = _editorState.AddBlock(_currentBlock, _currentSlot, pos);
                if (_currentBlockRooted && placed.Type != BlockType.Wall)
                {
                    placed.State = BlockState.Rooted;
                    placed.RootProgress = Constants.RootTicks;
                }
                break;
            case EditorTool.Eraser:
                cell.Ground = GroundType.Normal;
                cell.Terrain = TerrainType.None;
                if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
                break;
        }
    }

    var newBlock = _editorState.GetBlockAt(pos);
    var after = new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
        newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null);

    if (before != after)
    {
        _dragAction?.Before.Add(before);
        _dragAction?.After.Add(after);
    }

    RefreshRenderer();
}
```

- [ ] **Step 8: Delete `SymmetryMirror.cs`**

```bash
rm godot/Scripts/Editor/SymmetryMirror.cs
rm godot/Scripts/Editor/SymmetryMirror.cs.uid
```

- [ ] **Step 9: Build and verify**

```bash
dotnet build blocker.sln
```
Open Godot → Map Editor. Paint the left half, click L→R — right half should mirror. Ctrl+Z undoes.

- [ ] **Step 10: Commit**

```bash
git add -A godot/Scripts/Editor/
git commit -m "feat(editor): replace live symmetry with one-shot mirror operations"
```

---

## Task 9: Status bar

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`
- Modify: `godot/Scripts/Editor/EditorToolbar.cs`

- [ ] **Step 1: Add status bar field**

In `MapEditorScene`:

```csharp
private Label _statusLabel = null!;
```

- [ ] **Step 2: Build the status bar in `_Ready()`**

Add after the minimap setup, before `RefreshRenderer()`:

```csharp
// Status bar
var statusBar = new PanelContainer();
statusBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
statusBar.OffsetTop = -24;
statusBar.OffsetBottom = 0;
statusBar.MouseFilter = Control.MouseFilterEnum.Ignore;
var statusStyle = new StyleBoxFlat { BgColor = new Color(0.055f, 0.055f, 0.07f, 0.92f) };
statusBar.AddThemeStyleboxOverride("panel", statusStyle);

_statusLabel = new Label
{
    Text = "",
    VerticalAlignment = VerticalAlignment.Center,
    LabelSettings = new LabelSettings { FontSize = 11 }
};
_statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.55f, 0.65f));
statusBar.AddChild(_statusLabel);
_uiLayer.AddChild(statusBar);
```

- [ ] **Step 3: Update status bar in `_Process()`**

At the end of `_Process()`:

```csharp
UpdateStatusBar();
```

Add the method:

```csharp
private void UpdateStatusBar()
{
    var mousePos = GetViewport().GetMousePosition();
    var local = _gridRenderer.ToLocal(GetGlobalMousePosition());
    int gx = (int)Mathf.Floor((local.X - GridRenderer.GridPadding) / GridRenderer.CellSize);
    int gy = (int)Mathf.Floor((local.Y - GridRenderer.GridPadding) / GridRenderer.CellSize);

    string posStr = "—";
    string tileStr = "—";

    if (_editorState.Grid.InBounds(gx, gy))
    {
        posStr = $"x: {gx}  y: {gy}";
        var cell = _editorState.Grid[gx, gy];
        var blk = _editorState.GetBlockAt(new GridPos(gx, gy));
        if (blk != null)
            tileStr = $"{blk.Type} (slot {blk.PlayerId})";
        else if (cell.Terrain != TerrainType.None)
            tileStr = cell.Terrain.ToString();
        else
            tileStr = cell.Ground == GroundType.Normal ? "Normal" : cell.Ground.ToString();
    }

    string toolStr = _currentMode.ToString();
    string zoomStr = $"{ZoomLevels[_zoomIndex]:0.##}×";
    string sizeStr = $"{_mapWidth} × {_mapHeight}";

    _statusLabel.Text = $"  {posStr}    tile: {tileStr}    tool: {toolStr}    zoom: {zoomStr}    size: {sizeStr}";
}
```

Note: `Grid.InBounds` needs an `int, int` overload. Check if one exists; if not, use `_editorState.Grid.InBounds(new GridPos(gx, gy))`.

- [ ] **Step 4: Build and verify**

```bash
dotnet build blocker.sln
```
Open Godot → Map Editor. Move the mouse over the grid — status bar at the bottom should show cursor position, tile type, active tool, zoom, and map size.

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat(editor): add status bar showing cursor position, tile, tool, zoom"
```

---

## Task 10: Ground and terrain icon buttons

**Files:**
- Modify: `godot/Scripts/Editor/EditorToolbar.cs`

- [ ] **Step 1: Add `GroundTileButton` class at the bottom of `EditorToolbar.cs`**

```csharp
public partial class GroundTileButton : Button
{
    public GroundType GroundType { get; set; }
    public GameConfig? Config { get; set; }
    public string TileLabel { get; set; } = "";

    public override void _Draw()
    {
        if (Config == null) return;
        var size = Size;
        float iconSize = Mathf.Min(size.X - 4f, size.Y * 0.6f);
        float iconX = (size.X - iconSize) / 2f;
        float iconY = 3f;
        var iconRect = new Rect2(iconX, iconY, iconSize, iconSize);

        // Ground fill
        var color = Config.GetGroundColor(GroundType);
        DrawRect(iconRect, color);

        // Subtle border
        DrawRect(iconRect, color.Lightened(0.15f), false, 1f);
    }
}
```

- [ ] **Step 2: Add `TerrainTileButton` class at the bottom of `EditorToolbar.cs`**

```csharp
public partial class TerrainTileButton : Button
{
    public TerrainType TerrainType { get; set; }
    public GameConfig? Config { get; set; }

    public override void _Draw()
    {
        if (Config == null) return;
        var size = Size;
        float iconSize = Mathf.Min(size.X - 4f, size.Y * 0.6f);
        float iconX = (size.X - iconSize) / 2f;
        float iconY = 3f;
        var iconRect = new Rect2(iconX, iconY, iconSize, iconSize);

        var color = TerrainType switch
        {
            TerrainType.BreakableWall => Config.BreakableWallGroundColor,
            TerrainType.FragileWall   => Config.FragileWallGroundColor,
            _                         => Config.TerrainGroundColor
        };
        DrawRect(iconRect, color);
        DrawRect(iconRect, color.Lightened(0.2f), false, 1.5f);
    }
}
```

- [ ] **Step 3: Add tile button list fields for config updates**

Add alongside `_unitButtons`:

```csharp
private readonly List<GroundTileButton> _groundButtons = new();
private readonly List<TerrainTileButton> _terrainButtons = new();
```

Update `SetConfig` to also redraw them:

```csharp
public void SetConfig(GameConfig config)
{
    _config = config;
    foreach (var btn in _unitButtons)   { btn.Config = config; btn.QueueRedraw(); }
    foreach (var btn in _groundButtons)  { btn.Config = config; btn.QueueRedraw(); }
    foreach (var btn in _terrainButtons) { btn.Config = config; btn.QueueRedraw(); }
}
```

- [ ] **Step 4: Add `AddGroundTileButton` and `AddTerrainTileButton` helpers**

```csharp
private GroundTileButton AddGroundTileButton(GridContainer parent, GroundType type, string label, Action onPressed)
{
    var btn = new GroundTileButton
    {
        GroundType = type,
        Config = _config,
        TooltipText = label,
        CustomMinimumSize = new Vector2(36, 44),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        Text = label  // shown below icon via default Button text rendering at bottom
    };
    // Godot Button renders Text centrally; we want it at the bottom.
    // Use a VBoxContainer approach instead: icon button + label.
    // Simplest: use a Button with multiline text disabled and icon via _Draw,
    // then overlay the label as a child Label node.
    btn.Text = "";  // We draw everything manually
    btn.Pressed += () =>
    {
        SetActiveToolButton(btn);
        onPressed();
    };
    parent.AddChild(btn);
    _groundButtons.Add(btn);
    return btn;
}

private TerrainTileButton AddTerrainTileButton(GridContainer parent, TerrainType type, string label, Action onPressed)
{
    var btn = new TerrainTileButton
    {
        TerrainType = type,
        Config = _config,
        TooltipText = label,
        CustomMinimumSize = new Vector2(36, 44),
        SizeFlagsHorizontal = SizeFlags.ExpandFill,
        Text = ""
    };
    btn.Pressed += () =>
    {
        SetActiveToolButton(btn);
        onPressed();
    };
    parent.AddChild(btn);
    _terrainButtons.Add(btn);
    return btn;
}
```

Since Godot `Button` renders the `Text` property centrally and we're drawing our own icon, we need the label shown too. The cleanest approach: draw the label text in `_Draw()` of `GroundTileButton` and `TerrainTileButton`. Update `_Draw()` in each:

For `GroundTileButton._Draw()`, after the icon drawing, add:

```csharp
// Draw label at bottom
if (GetThemeFont("font") is Font font)
{
    float fontSize = 9f;
    var labelColor = new Color(0.55f, 0.6f, 0.7f);
    var labelPos = new Vector2(2f, size.Y - fontSize - 2f);
    DrawString(font, labelPos, TileLabel, HorizontalAlignment.Center, size.X - 4f, (int)fontSize, labelColor);
}
```

And set `TileLabel` when creating in Step 5.

For `TerrainTileButton._Draw()`, add similarly using `TerrainType.ToString()` as the label, or pass in a label string. Add `public string TileLabel { get; set; } = "";` and draw the same way.

- [ ] **Step 5: Replace text-only ground/terrain buttons in `BuildSidebar()`**

Replace the four `AddToolButton(vbox, "Normal", ...)` ground calls with a grid:

```csharp
AddSectionLabel(vbox, "Ground");
var groundGrid = new GridContainer { Columns = 2 };
groundGrid.AddThemeConstantOverride("h_separation", 2);
groundGrid.AddThemeConstantOverride("v_separation", 2);
vbox.AddChild(groundGrid);

var groundTypes = new (GroundType Type, string Label)[]
{
    (GroundType.Normal,   "Normal"),
    (GroundType.Boot,     "Boot"),
    (GroundType.Overload, "Overload"),
    (GroundType.Proto,    "Proto"),
};
foreach (var (type, label) in groundTypes)
{
    var t = type; // capture
    var btn = AddGroundTileButton(groundGrid, type, label, () =>
    {
        GroundSelected?.Invoke(t);
        ToolModeSelected?.Invoke(EditorMode.Paint);
        HighlightToolMode(EditorMode.Paint);
    });
    btn.TileLabel = label;
}
```

Replace the three `AddToolButton(vbox, "Solid Wall", ...)` terrain calls with:

```csharp
AddSectionLabel(vbox, "Terrain");
var terrainGrid = new GridContainer { Columns = 2 };
terrainGrid.AddThemeConstantOverride("h_separation", 2);
terrainGrid.AddThemeConstantOverride("v_separation", 2);
vbox.AddChild(terrainGrid);

var terrainTypes = new (TerrainType Type, string Label)[]
{
    (TerrainType.Terrain,       "Solid"),
    (TerrainType.BreakableWall, "Breakable"),
    (TerrainType.FragileWall,   "Fragile"),
};
foreach (var (type, label) in terrainTypes)
{
    var t = type;
    var btn = AddTerrainTileButton(terrainGrid, type, label, () =>
    {
        TerrainSelected?.Invoke(t);
        ToolModeSelected?.Invoke(EditorMode.Paint);
        HighlightToolMode(EditorMode.Paint);
    });
    btn.TileLabel = label;
}
```

- [ ] **Step 6: Build and verify**

```bash
dotnet build blocker.sln
```
Open Godot → Map Editor. The Ground and Terrain sections should show 2-column icon+label card buttons showing the actual tile colours.

- [ ] **Step 7: Commit**

```bash
git add godot/Scripts/Editor/EditorToolbar.cs
git commit -m "feat(editor): upgrade ground and terrain buttons to icon+label cards"
```

---

## Self-Review Checklist

Spec section → task coverage:

| Spec requirement | Task |
|---|---|
| Fill tool | Task 4 |
| Pick tool | Task 5 |
| Line tool + preview | Task 6 |
| Select+Move tool | Task 7 |
| Tool buttons in top bar with hotkeys | Task 3 |
| Ctrl+Z/Y, Esc, Delete shortcuts | Tasks 3, 6, 7 |
| One-shot mirror (6 directions) | Task 8 |
| Teams toggle | Task 8 |
| Remove live symmetry + SymmetryMirror.cs | Task 8 |
| Status bar (pos, tile, tool, zoom, size) | Task 9 |
| Ground/terrain icon buttons | Task 10 |
| Unit icon bug fix | Task 1 |
