# Minimap & HUD Shell Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a reusable minimap control and a bottom-of-screen HUD layout that hosts it, with the minimap also usable standalone in the map editor.

**Architecture:** `MinimapPanel` is a standalone `Control` that takes game state + config + camera info and renders the full grid in miniature. `HudBar` is a bottom-screen `CanvasLayer` that hosts three panels (minimap left, unit info center, command card right) — only the minimap panel is implemented now. `GameManager` wires the HUD; `MapEditorScene` adds the minimap directly to its UI layer.

**Tech Stack:** Godot 4 C#, custom `_Draw()` rendering on `Control` nodes.

---

## File Structure

| File | Action | Responsibility |
|------|--------|----------------|
| `godot/Scripts/Rendering/MinimapPanel.cs` | Create | Standalone minimap Control — draws grid, blocks, camera viewport rect. Emits signal on click with world position. |
| `godot/Scripts/Rendering/HudBar.cs` | Create | Bottom-of-screen CanvasLayer with HBoxContainer holding minimap (left), unit info placeholder (center), command card placeholder (right). |
| `godot/Scripts/Rendering/HudOverlay.cs` | Modify | Remove bottom-left selection info (moves to HudBar center panel later). Keep top bar as-is for now. |
| `godot/Scripts/Game/GameManager.cs` | Modify | Create HudBar instead of bare HudOverlay for bottom HUD. Wire minimap data + camera jump signal. |
| `godot/Scripts/Editor/MapEditorScene.cs` | Modify | Add MinimapPanel to editor UI layer, wire it to editor state + camera. |
| `godot/Scripts/Input/CameraController.cs` | Modify | Add `JumpTo(Vector2 worldPos)` public method. |

---

### Task 1: Create MinimapPanel Control

**Files:**
- Create: `godot/Scripts/Rendering/MinimapPanel.cs`

- [ ] **Step 1: Create MinimapPanel with data setters and sizing**

```csharp
using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Standalone minimap control. Draws the full grid scaled down: ground colors,
/// terrain, blocks as player-colored dots, and a camera viewport rectangle.
/// Emits CameraJumpRequested with world position when clicked.
/// Reusable in both game HUD and map editor.
/// </summary>
public partial class MinimapPanel : Control
{
    [Signal] public delegate void CameraJumpRequestedEventHandler(Vector2 worldPos);

    private GameState? _gameState;
    private GameConfig _config = GameConfig.CreateDefault();

    // Camera state — set each frame by the host
    private Vector2 _cameraWorldPos;
    private Vector2 _cameraViewSize; // visible world area in pixels

    private static readonly Color BorderColor = new(0.4f, 0.4f, 0.5f, 0.8f);
    private static readonly Color BgColor = new(0.05f, 0.05f, 0.08f, 0.9f);
    private static readonly Color ViewportRectColor = new(1f, 1f, 1f, 0.7f);
    private static readonly Color TerrainColor = new(0.3f, 0.3f, 0.35f);

    public void SetGameState(GameState state) => _gameState = state;
    public void SetConfig(GameConfig config) => _config = config;

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _cameraWorldPos = worldPos;
        _cameraViewSize = viewSize;
    }

    public override void _Process(double delta) => QueueRedraw();

    public override void _Draw()
    {
        if (_gameState == null) return;

        var grid = _gameState.Grid;
        var panelSize = Size;

        // Background
        DrawRect(new Rect2(Vector2.Zero, panelSize), BgColor);

        // Calculate scale to fit grid into panel with 2px margin
        const float margin = 2f;
        float availW = panelSize.X - margin * 2;
        float availH = panelSize.Y - margin * 2;
        float scaleX = availW / grid.Width;
        float scaleY = availH / grid.Height;
        float scale = Mathf.Min(scaleX, scaleY);

        // Center the map within the panel
        float mapW = grid.Width * scale;
        float mapH = grid.Height * scale;
        float offsetX = margin + (availW - mapW) * 0.5f;
        float offsetY = margin + (availH - mapH) * 0.5f;

        // Draw ground and terrain
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid[x, y];
                Color color;

                if (cell.Terrain != TerrainType.None)
                    color = TerrainColor;
                else if (cell.Ground != GroundType.Normal)
                    color = _config.GetGroundColor(cell.Ground);
                else
                    continue; // Normal ground — skip, BG covers it

                var rect = new Rect2(
                    offsetX + x * scale,
                    offsetY + y * scale,
                    Mathf.Max(scale, 1f),
                    Mathf.Max(scale, 1f));
                DrawRect(rect, color);
            }
        }

        // Draw blocks as colored dots
        float dotSize = Mathf.Max(scale * 0.8f, 1.5f);
        foreach (var block in _gameState.Blocks)
        {
            var bcolor = _config.GetPalette(block.PlayerId).Base;
            float bx = offsetX + (block.Pos.X + 0.5f) * scale;
            float by = offsetY + (block.Pos.Y + 0.5f) * scale;
            DrawRect(new Rect2(bx - dotSize * 0.5f, by - dotSize * 0.5f, dotSize, dotSize), bcolor);
        }

        // Draw camera viewport rectangle
        float cellSize = GridRenderer.CellSize;
        float camLeft = (_cameraWorldPos.X - _cameraViewSize.X * 0.5f) / cellSize;
        float camTop = (_cameraWorldPos.Y - _cameraViewSize.Y * 0.5f) / cellSize;
        float camW = _cameraViewSize.X / cellSize;
        float camH = _cameraViewSize.Y / cellSize;

        var vpRect = new Rect2(
            offsetX + camLeft * scale,
            offsetY + camTop * scale,
            camW * scale,
            camH * scale);

        // Clamp viewport rect to map bounds
        float clampedLeft = Mathf.Max(vpRect.Position.X, offsetX);
        float clampedTop = Mathf.Max(vpRect.Position.Y, offsetY);
        float clampedRight = Mathf.Min(vpRect.End.X, offsetX + mapW);
        float clampedBottom = Mathf.Min(vpRect.End.Y, offsetY + mapH);

        if (clampedRight > clampedLeft && clampedBottom > clampedTop)
        {
            var clampedVp = new Rect2(clampedLeft, clampedTop,
                clampedRight - clampedLeft, clampedBottom - clampedTop);
            DrawRect(clampedVp, ViewportRectColor, false, 1.0f);
        }

        // Border
        DrawRect(new Rect2(Vector2.Zero, panelSize), BorderColor, false, 1.0f);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            HandleClick(mb.Position);
            AcceptEvent();
        }
        else if (@event is InputEventMouseMotion motion
                 && Godot.Input.IsMouseButtonPressed(MouseButton.Left))
        {
            HandleClick(motion.Position);
            AcceptEvent();
        }
    }

    private void HandleClick(Vector2 localPos)
    {
        if (_gameState == null) return;

        var grid = _gameState.Grid;
        var panelSize = Size;

        const float margin = 2f;
        float availW = panelSize.X - margin * 2;
        float availH = panelSize.Y - margin * 2;
        float scaleX = availW / grid.Width;
        float scaleY = availH / grid.Height;
        float scale = Mathf.Min(scaleX, scaleY);

        float mapW = grid.Width * scale;
        float mapH = grid.Height * scale;
        float offsetX = margin + (availW - mapW) * 0.5f;
        float offsetY = margin + (availH - mapH) * 0.5f;

        // Convert click to grid coords, then to world coords
        float gridX = (localPos.X - offsetX) / scale;
        float gridY = (localPos.Y - offsetY) / scale;

        float worldX = gridX * GridRenderer.CellSize;
        float worldY = gridY * GridRenderer.CellSize;

        EmitSignal(SignalName.CameraJumpRequested, new Vector2(worldX, worldY));
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/Rendering/MinimapPanel.cs
git commit -m "feat: add reusable MinimapPanel control"
```

---

### Task 2: Add JumpTo method to CameraController

**Files:**
- Modify: `godot/Scripts/Input/CameraController.cs`

- [ ] **Step 1: Add JumpTo public method**

Add after the `SetGridSize` method:

```csharp
public void JumpTo(Vector2 worldPos)
{
    Position = worldPos;
    ClampPosition();
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/Input/CameraController.cs
git commit -m "feat: add JumpTo method to CameraController"
```

---

### Task 3: Create HudBar bottom-of-screen layout

**Files:**
- Create: `godot/Scripts/Rendering/HudBar.cs`

- [ ] **Step 1: Create HudBar with three-panel layout**

```csharp
using Blocker.Game.Config;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Bottom-of-screen HUD bar with three panels:
/// Left = minimap, Center = unit info (placeholder), Right = command card (placeholder).
/// Sits on a CanvasLayer above the game world.
/// </summary>
public partial class HudBar : CanvasLayer
{
    private MinimapPanel _minimap = null!;
    private PanelContainer _unitInfoPanel = null!;
    private PanelContainer _commandPanel = null!;

    private const float BarHeight = 150f;
    private const float MinimapWidth = 200f;

    [Signal] public delegate void MinimapCameraJumpEventHandler(Vector2 worldPos);

    public override void _Ready()
    {
        Layer = 10;

        // Anchor bar to bottom of screen
        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.OffsetTop = -BarHeight;
        anchor.OffsetBottom = 0;
        anchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(anchor);

        // Background panel
        var bg = new ColorRect
        {
            Color = new Color(0.05f, 0.05f, 0.08f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        anchor.AddChild(bg);

        // HBox for three panels
        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", 4);
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        anchor.AddChild(hbox);

        // Left: Minimap
        _minimap = new MinimapPanel
        {
            CustomMinimumSize = new Vector2(MinimapWidth, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _minimap.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _minimap.CameraJumpRequested += pos => EmitSignal(SignalName.MinimapCameraJump, pos);
        hbox.AddChild(_minimap);

        // Center: Unit info placeholder
        _unitInfoPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _unitInfoPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_unitInfoPanel);

        // Right: Command card placeholder
        _commandPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(MinimapWidth, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _commandPanel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        hbox.AddChild(_commandPanel);
    }

    public void SetGameState(GameState state) => _minimap.SetGameState(state);
    public void SetConfig(GameConfig config) => _minimap.SetConfig(config);

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _minimap.SetCameraView(worldPos, viewSize);
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/Rendering/HudBar.cs
git commit -m "feat: add HudBar with three-panel bottom layout"
```

---

### Task 4: Wire HudBar into GameManager

**Files:**
- Modify: `godot/Scripts/Game/GameManager.cs`

- [ ] **Step 1: Add HudBar field and setup**

In `GameManager.cs`, add a `_hudBar` field next to `_hud`:

```csharp
private HudBar _hudBar = null!;
```

In `_Ready()`, after the existing HUD setup block (after `_hud.SetControllingPlayer(0);`), add:

```csharp
// Set up bottom HUD bar with minimap
_hudBar = new HudBar();
AddChild(_hudBar);
_hudBar.SetGameState(gameState);
_hudBar.SetConfig(Config);
_hudBar.MinimapCameraJump += pos => _camera.JumpTo(pos);
```

- [ ] **Step 2: Update _Process to feed camera info to HudBar**

In `_Process()`, add after the existing `_gridRenderer.SetSelectedIds` line:

```csharp
// Feed camera position and visible area to HudBar minimap
var viewSize = GetViewportRect().Size / _camera.Zoom;
_hudBar.SetCameraView(_camera.Position, viewSize);
```

- [ ] **Step 3: Remove bottom-left selection info from HudOverlay**

In `HudOverlay.cs`, in `HudDrawControl._Draw()`, remove the line:

```csharp
// Selection info and keybind hints (bottom-left)
DrawSelectionInfo(state, viewport, font, fontSize);
```

Also remove the entire `DrawSelectionInfo` method. This info will move to the HudBar center panel in a future task.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Game/GameManager.cs godot/Scripts/Rendering/HudOverlay.cs
git commit -m "feat: wire HudBar minimap into game scene"
```

---

### Task 5: Add MinimapPanel to Map Editor

**Files:**
- Modify: `godot/Scripts/Editor/MapEditorScene.cs`

- [ ] **Step 1: Add minimap field and setup**

Add field:

```csharp
private MinimapPanel _minimap = null!;
```

In `_Ready()`, after the guide overlay setup (after `AddChild(_guideOverlay);`), add:

```csharp
// Add minimap to UI layer (bottom-left corner)
_minimap = new MinimapPanel
{
    CustomMinimumSize = new Vector2(200, 130),
    MouseFilter = Control.MouseFilterEnum.Stop
};
_minimap.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
_minimap.OffsetTop = -130;
_minimap.OffsetBottom = 0;
_minimap.OffsetLeft = 0;
_minimap.OffsetRight = 200;
_minimap.SetConfig(_config);
_minimap.SetGameState(_editorState);
_minimap.CameraJumpRequested += OnMinimapJump;
_uiLayer.AddChild(_minimap);
```

- [ ] **Step 2: Add minimap camera update in _Process and jump handler**

In `_Process()`, at the end of the method (after the velocity/clamp block), add:

```csharp
// Update minimap camera view
var viewSize = GetViewportRect().Size / _camera.Zoom;
_minimap.SetCameraView(_camera.Position, viewSize);
```

Add new method:

```csharp
private void OnMinimapJump(Vector2 worldPos)
{
    _camera.Position = worldPos;
    ClampCamera();
}
```

- [ ] **Step 3: Update RefreshRenderer to also update minimap**

In `RefreshRenderer()`, add after the existing line:

```csharp
_minimap.SetGameState(_editorState);
```

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Editor/MapEditorScene.cs
git commit -m "feat: add minimap to map editor"
```

---

### Task 6: Build and verify

- [ ] **Step 1: Build the solution**

Run: `dotnet build`
Expected: Build succeeded with 0 errors.

- [ ] **Step 2: Manual verification checklist**

Open Godot, run Main scene:
- Bottom of screen shows HudBar with minimap on the left
- Minimap displays ground types in correct colors
- Blocks appear as colored dots matching player colors
- White rectangle shows current camera viewport
- Clicking minimap jumps camera to that position
- Dragging on minimap continuously updates camera
- Top bar (player info, tick, pop, ratio) still renders correctly
- Bottom-left selection info no longer shows (removed, will move to center panel later)

Open map editor:
- Minimap appears bottom-left
- Painting updates minimap in real-time
- Click to jump works
- Minimap doesn't interfere with toolbar or painting

- [ ] **Step 3: Final commit if any fixes needed**
