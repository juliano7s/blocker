# HUD Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Redesign the in-game HUD with unified solid frame styling, reorganized top bar, functional selection panel with control groups, command card, and floating spawn toggles.

**Architecture:** Extract shared HUD styling into constants. Rebuild top bar with time display and menu button. Create three new components (SelectionPanel, CommandCard, SpawnToggles) as separate Control nodes. Wire them into HudBar and connect to SelectionManager for state.

**Tech Stack:** Godot 4 + C#, CanvasLayer for HUD, existing SelectionManager for selection state and control groups.

---

## File Structure

**Modify:**
- `godot/Scripts/Rendering/HudOverlay.cs` — top bar (time, menu button, debug FPS)
- `godot/Scripts/Rendering/HudBar.cs` — bottom bar structure, wire new panels
- `godot/Scripts/Rendering/MinimapPanel.cs` — clearer borders, label
- `godot/Scripts/Game/GameManager.cs` — wire SelectionManager to HUD panels

**Create:**
- `godot/Scripts/Rendering/HudStyles.cs` — shared colors, dimensions, fonts
- `godot/Scripts/Rendering/SelectionPanel.cs` — control groups + unit info
- `godot/Scripts/Rendering/CommandCard.cs` — command buttons grid
- `godot/Scripts/Rendering/SpawnToggles.cs` — floating spawn toggle panel

---

### Task 1: Create HudStyles with Shared Constants

**Files:**
- Create: `godot/Scripts/Rendering/HudStyles.cs`

- [ ] **Step 1: Create the HudStyles class**

```csharp
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Shared visual constants for HUD components.
/// </summary>
public static class HudStyles
{
    // Panel backgrounds
    public static readonly Color PanelBgTop = new(0.078f, 0.098f, 0.133f);    // #141922
    public static readonly Color PanelBgBottom = new(0.047f, 0.063f, 0.082f); // #0c1015
    public static readonly Color PanelBorder = new(0.176f, 0.216f, 0.282f);   // #2d3748
    public static readonly Color InnerPanelBg = new(0.039f, 0.055f, 0.078f);  // #0a0e14

    // Text colors
    public static readonly Color TextPrimary = new(0.898f, 0.898f, 0.898f);   // #e5e5e5
    public static readonly Color TextSecondary = new(0.533f, 0.533f, 0.533f); // #888888
    public static readonly Color TextDim = new(0.333f, 0.333f, 0.333f);       // #555555

    // Dimensions
    public const float TopBarHeight = 42f;
    public const float BottomBarHeight = 110f;
    public const float PanelBorderWidth = 2f;
    public const float PanelGap = 6f;
    public const float FixedPanelWidth = 120f;

    // Font sizes
    public const int FontSizeNormal = 13;
    public const int FontSizeSmall = 10;
    public const int FontSizeHotkey = 9;

    /// <summary>Draw a panel background with gradient and border.</summary>
    public static void DrawPanelBackground(Control control, Rect2 rect)
    {
        // Gradient from top to bottom
        var topRect = new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.5f));
        var bottomRect = new Rect2(
            new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f),
            new Vector2(rect.Size.X, rect.Size.Y * 0.5f));

        control.DrawRect(topRect, PanelBgTop);
        control.DrawRect(bottomRect, PanelBgBottom);
        control.DrawRect(rect, PanelBorder, false, PanelBorderWidth);
    }

    /// <summary>Draw an inner panel (darker, for sub-sections).</summary>
    public static void DrawInnerPanel(Control control, Rect2 rect)
    {
        control.DrawRect(rect, InnerPanelBg);
        control.DrawRect(rect, PanelBorder, false, PanelBorderWidth);
    }
}
```

- [ ] **Step 2: Verify file compiles**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/HudStyles.cs
git commit -m "feat(hud): add HudStyles with shared visual constants"
```

---

### Task 2: Redesign Top Bar (HudOverlay)

**Files:**
- Modify: `godot/Scripts/Rendering/HudOverlay.cs`

- [ ] **Step 1: Update HudOverlay colors and remove ratio bar**

Replace the color constants and update `_Ready` to use new styling:

```csharp
// At top of class, replace existing colors with:
private static readonly Color DividerColor = new(0.176f, 0.216f, 0.282f); // #2d3748

// In _Ready(), remove _exitBtn and _surrenderBtn creation, replace with menu button:
// Remove these lines:
// _exitBtn = new Button { ... }
// _surrenderBtn = new Button { ... }

// Add menu button instead (we'll create MenuButton in a later step)
```

- [ ] **Step 2: Rewrite HudDrawControl._Draw for new top bar layout**

Replace the entire `_Draw` method in `HudDrawControl`:

```csharp
public override void _Draw()
{
    var state = _hud.GetGameState();
    if (state == null) return;

    var viewport = GetViewportRect().Size;
    var font = ThemeDB.FallbackFont;

    // Draw top bar background with gradient
    HudStyles.DrawPanelBackground(this, new Rect2(0, 0, viewport.X, HudStyles.TopBarHeight));

    // Player info (left side)
    int pid = _hud.GetControllingPlayer();
    var playerColor = _hud._config.GetPalette(pid).Base;
    var lighterBorder = playerColor.Lightened(0.3f);

    float x = 14f;
    float centerY = HudStyles.TopBarHeight / 2f;

    // Player color square
    var colorRect = new Rect2(x, centerY - 8, 16, 16);
    DrawRect(colorRect, playerColor);
    DrawRect(colorRect, lighterBorder, false, 1f);
    x += 24;

    // Player name
    string playerName = $"Player {pid}";
    DrawString(font, new Vector2(x, centerY + 5), playerName,
        HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);
    x += font.GetStringSize(playerName, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X + 14;

    // Divider
    DrawLine(new Vector2(x, centerY - 11), new Vector2(x, centerY + 11), DividerColor, 1f);
    x += 14;

    // Game time (convert ticks to hh:mm:ss)
    int totalSeconds = state.TickNumber / 12; // Assuming 12 tps
    int hours = totalSeconds / 3600;
    int minutes = (totalSeconds % 3600) / 60;
    int seconds = totalSeconds % 60;
    string timeText = hours > 0 ? $"{hours}:{minutes:D2}:{seconds:D2}" : $"{minutes:D2}:{seconds:D2}";
    DrawString(font, new Vector2(x, centerY + 5), timeText,
        HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextSecondary);
    x += font.GetStringSize(timeText, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X + 14;

    // Divider
    DrawLine(new Vector2(x, centerY - 11), new Vector2(x, centerY + 11), DividerColor, 1f);
    x += 14;

    // Population
    if (pid < state.Players.Count)
    {
        var player = state.Players.Find(p => p.Id == pid);
        if (player != null)
        {
            int currentPop = state.GetPopulation(pid);
            string popText = $"Pop: {currentPop} / {player.MaxPopulation}";
            DrawString(font, new Vector2(x, centerY + 5), popText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);
        }
    }

    // Debug FPS (right side, only if enabled)
    if (_hud._showDebugFps)
    {
        string fpsText = $"{_fps:F0} FPS";
        var fpsColor = _fps >= 55 ? new Color(0.4f, 0.9f, 0.4f) :
                       _fps >= 30 ? new Color(0.9f, 0.9f, 0.3f) :
                       new Color(0.9f, 0.3f, 0.3f);
        float fpsWidth = font.GetStringSize(fpsText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall).X;
        // Position below top bar
        DrawString(font, new Vector2(viewport.X - fpsWidth - 14, HudStyles.TopBarHeight + 16),
            fpsText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, fpsColor);
    }
}
```

- [ ] **Step 3: Add debug FPS toggle field and menu button**

Add to HudOverlay class:

```csharp
private bool _showDebugFps = false;
private Button? _menuBtn;
private PopupMenu? _menuPopup;

public void SetShowDebugFps(bool show) => _showDebugFps = show;
```

Update `_Ready()` to create menu button:

```csharp
// Menu button (right side)
_menuBtn = new Button
{
    Text = "☰ Menu",
    MouseFilter = Control.MouseFilterEnum.Stop,
};
_menuBtn.SetAnchorsPreset(Control.LayoutPreset.TopRight);
_menuBtn.OffsetLeft = -90;
_menuBtn.OffsetTop = 8;
_menuBtn.OffsetRight = -14;
_menuBtn.OffsetBottom = 34;
_menuBtn.Pressed += OnMenuPressed;
AddChild(_menuBtn);

// Popup menu
_menuPopup = new PopupMenu();
_menuPopup.AddItem("Surrender", 0);
_menuPopup.AddItem("Exit to Menu", 1);
_menuPopup.IdPressed += OnMenuItemSelected;
AddChild(_menuPopup);
```

Add handlers:

```csharp
private void OnMenuPressed()
{
    if (_menuPopup == null || _menuBtn == null) return;
    var btnRect = _menuBtn.GetGlobalRect();
    _menuPopup.Position = new Vector2I((int)btnRect.Position.X, (int)(btnRect.Position.Y + btnRect.Size.Y));
    _menuPopup.Popup();
}

private void OnMenuItemSelected(long id)
{
    switch (id)
    {
        case 0: // Surrender
            if (!_surrendered)
            {
                _surrendered = true;
                _surrenderHandler?.Invoke();
            }
            break;
        case 1: // Exit
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
            break;
    }
}
```

- [ ] **Step 4: Remove old button variables and ratio bar method**

Remove from class:
- `_exitBtn` field
- Old `_exitBtn` and `_surrenderBtn` creation in `_Ready()`
- `DrawBlockRatioBar` method call and method

- [ ] **Step 5: Build and test**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 6: Commit**

```bash
git add godot/Scripts/Rendering/HudOverlay.cs
git commit -m "feat(hud): redesign top bar with time display and menu button"
```

---

### Task 3: Create SpawnToggles Panel

**Files:**
- Create: `godot/Scripts/Rendering/SpawnToggles.cs`

- [ ] **Step 1: Create SpawnToggles class**

```csharp
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Floating panel with toggle buttons for global spawn control per unit type.
/// Positioned at top-right of game area, below top bar.
/// </summary>
public partial class SpawnToggles : Control
{
    [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType, bool enabled);

    private readonly bool[] _spawnEnabled = [true, true, true]; // Builder, Soldier, Stunner
    private static readonly BlockType[] ToggleTypes = [BlockType.Builder, BlockType.Soldier, BlockType.Stunner];
    private static readonly Color[] ToggleColors =
    [
        new(0.231f, 0.510f, 0.965f), // #3b82f6 Builder blue
        new(0.133f, 0.773f, 0.369f), // #22c55e Soldier green
        new(0.659f, 0.333f, 0.969f), // #a855f7 Stunner purple
    ];
    private static readonly string[] Hotkeys = ["1", "2", "3"];

    private const float ButtonSize = 28f;
    private const float ButtonGap = 6f;
    private const float Padding = 8f;
    private const float PanelWidth = ButtonSize + Padding * 2;
    private const float PanelHeight = ButtonSize * 3 + ButtonGap * 2 + Padding * 2;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);

        // Panel background
        DrawRect(rect, HudStyles.PanelBgBottom);
        DrawRect(rect, HudStyles.PanelBorder, false, HudStyles.PanelBorderWidth);

        // Shadow effect
        var shadowRect = new Rect2(rect.Position + new Vector2(2, 2), rect.Size);
        DrawRect(shadowRect, new Color(0, 0, 0, 0.3f));
        DrawRect(rect, HudStyles.PanelBgBottom);
        DrawRect(rect, HudStyles.PanelBorder, false, HudStyles.PanelBorderWidth);

        var font = ThemeDB.FallbackFont;

        // Draw toggle buttons
        for (int i = 0; i < 3; i++)
        {
            float y = Padding + i * (ButtonSize + ButtonGap);
            var btnRect = new Rect2(Padding, y, ButtonSize, ButtonSize);

            var color = ToggleColors[i];
            if (!_spawnEnabled[i])
                color = color with { A = 0.35f };

            DrawRect(btnRect, color);

            // Hotkey in corner
            string hotkey = Hotkeys[i];
            var hotkeyPos = new Vector2(btnRect.End.X - 8, btnRect.End.Y - 3);
            DrawString(font, hotkeyPos, hotkey, HorizontalAlignment.Right, -1,
                HudStyles.FontSizeHotkey, new Color(1, 1, 1, 0.6f));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int index = GetButtonIndexAt(mb.Position);
            if (index >= 0)
            {
                ToggleSpawn(index);
                AcceptEvent();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            int index = key.Keycode switch
            {
                Key.Key1 => 0,
                Key.Key2 => 1,
                Key.Key3 => 2,
                _ => -1
            };
            if (index >= 0)
            {
                ToggleSpawn(index);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private int GetButtonIndexAt(Vector2 pos)
    {
        for (int i = 0; i < 3; i++)
        {
            float y = Padding + i * (ButtonSize + ButtonGap);
            var btnRect = new Rect2(Padding, y, ButtonSize, ButtonSize);
            if (btnRect.HasPoint(pos))
                return i;
        }
        return -1;
    }

    private void ToggleSpawn(int index)
    {
        _spawnEnabled[index] = !_spawnEnabled[index];
        EmitSignal(SignalName.SpawnToggleChanged, (int)ToggleTypes[index], _spawnEnabled[index]);
        QueueRedraw();
    }

    public bool IsSpawnEnabled(BlockType type)
    {
        int index = type switch
        {
            BlockType.Builder => 0,
            BlockType.Soldier => 1,
            BlockType.Stunner => 2,
            _ => -1
        };
        return index >= 0 && _spawnEnabled[index];
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/SpawnToggles.cs
git commit -m "feat(hud): add SpawnToggles floating panel"
```

---

### Task 4: Improve MinimapPanel

**Files:**
- Modify: `godot/Scripts/Rendering/MinimapPanel.cs`

- [ ] **Step 1: Update colors and add label**

Add label drawing at the top of `_Draw()`:

```csharp
public override void _Draw()
{
    if (_gameState == null) return;

    var grid = _gameState.Grid;
    var panelSize = Size;
    var font = ThemeDB.FallbackFont;

    // Panel background with border
    HudStyles.DrawInnerPanel(this, new Rect2(Vector2.Zero, panelSize));

    // Label
    DrawString(font, new Vector2(10, 14), "MINIMAP",
        HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

    var (scale, gridOffX, gridOffY) = ComputeLayout();

    // Offset for label
    float labelOffset = 20f;

    // ... rest of drawing code, adjust gridOffY calculations to account for label
```

- [ ] **Step 2: Update ComputeLayout for label space**

Modify `ComputeLayout()` to reserve space for label:

```csharp
private (float scale, float gridOffX, float gridOffY) ComputeLayout()
{
    var grid = _gameState!.Grid;
    var panelSize = Size;

    const float labelHeight = 20f;
    float availH = panelSize.Y - labelHeight - 8f; // 8px bottom padding

    float marginCellsX = grid.Width * MarginFraction;
    float marginCellsY = grid.Height * MarginFraction;
    float totalW = grid.Width + marginCellsX * 2;
    float totalH = grid.Height + marginCellsY * 2;

    const float panelMargin = 4f;
    float availW = panelSize.X - panelMargin * 2;
    float scale = Mathf.Min(availW / totalW, availH / totalH);

    float drawnW = totalW * scale;
    float drawnH = totalH * scale;
    float areaOffX = panelMargin + (availW - drawnW) * 0.5f;
    float areaOffY = labelHeight + (availH - drawnH) * 0.5f;

    float gridOffX = areaOffX + marginCellsX * scale;
    float gridOffY = areaOffY + marginCellsY * scale;

    return (scale, gridOffX, gridOffY);
}
```

- [ ] **Step 3: Replace background colors with HudStyles**

Replace old color constants:

```csharp
// Remove these old constants:
// private static readonly Color BorderColor = ...
// private static readonly Color BgColor = ...
// etc.

// In _Draw, replace:
// DrawRect(new Rect2(Vector2.Zero, panelSize), BgColor);
// with:
// HudStyles.DrawInnerPanel(this, new Rect2(Vector2.Zero, panelSize));
```

- [ ] **Step 4: Build and test**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Rendering/MinimapPanel.cs
git commit -m "feat(hud): improve minimap with label and consistent styling"
```

---

### Task 5: Create SelectionPanel

**Files:**
- Create: `godot/Scripts/Rendering/SelectionPanel.cs`

- [ ] **Step 1: Create SelectionPanel class with control groups row**

```csharp
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Center panel showing control groups and selection info.
/// </summary>
public partial class SelectionPanel : Control
{
    [Signal] public delegate void ControlGroupClickedEventHandler(int groupIndex, bool ctrlHeld);
    [Signal] public delegate void UnitClickedEventHandler(int blockId, bool shiftHeld);

    private IReadOnlyList<Block>? _selectedBlocks;
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _controlGroups;

    private const float ControlGroupHeight = 24f;
    private const float ControlGroupWidth = 36f;
    private const float UnitIconSize = 26f;
    private const float UnitIconGap = 6f;

    public void SetSelection(IReadOnlyList<Block>? blocks) => _selectedBlocks = blocks;
    public void SetControlGroups(IReadOnlyDictionary<int, IReadOnlyList<int>>? groups) => _controlGroups = groups;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        HudStyles.DrawInnerPanel(this, rect);

        var font = ThemeDB.FallbackFont;
        float y = 8f;

        // Control groups row
        DrawControlGroups(font, ref y);

        y += 8f;

        // Selection info
        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
        {
            DrawString(font, new Vector2(10, y + 14), "No selection",
                HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextSecondary);
        }
        else if (_selectedBlocks.Count == 1)
        {
            DrawSingleUnitInfo(font, y);
        }
        else
        {
            DrawMultiUnitInfo(font, y);
        }
    }

    private void DrawControlGroups(Font font, ref float y)
    {
        DrawString(font, new Vector2(10, y + 12), "GROUPS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        float x = 60f;
        for (int i = 1; i <= 9; i++)
        {
            int count = 0;
            if (_controlGroups != null && _controlGroups.TryGetValue(i, out var ids))
                count = ids.Count;

            var groupRect = new Rect2(x, y, ControlGroupWidth, ControlGroupHeight);

            if (count > 0)
            {
                DrawRect(groupRect, HudStyles.PanelBgTop);
                DrawRect(groupRect, HudStyles.PanelBorder, false, 1f);

                string text = $"{i}:{count}";
                var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
                DrawString(font, new Vector2(x + (ControlGroupWidth - textSize.X) / 2, y + 16), text,
                    HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextPrimary);
            }
            else
            {
                DrawRect(groupRect, HudStyles.InnerPanelBg);
                DrawRect(groupRect, HudStyles.PanelBorder with { A = 0.3f }, false, 1f);

                string text = $"{i}";
                var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
                DrawString(font, new Vector2(x + (ControlGroupWidth - textSize.X) / 2, y + 16), text,
                    HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);
            }

            x += ControlGroupWidth + 4f;
        }

        y += ControlGroupHeight;
    }

    private void DrawSingleUnitInfo(Font font, float y)
    {
        var block = _selectedBlocks![0];

        DrawString(font, new Vector2(10, y + 14), block.Type.ToString().ToUpper(),
            HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);

        y += 20f;

        // Stats
        string speedText = block.Type switch
        {
            BlockType.Builder => "Speed: Normal",
            BlockType.Soldier => "Speed: Slow",
            BlockType.Stunner => "Speed: Fast",
            BlockType.Jumper => "Speed: Normal",
            BlockType.Warden => "Speed: Fast",
            BlockType.Wall => "Immobile",
            _ => ""
        };
        DrawString(font, new Vector2(10, y + 14), speedText,
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);

        y += 16f;

        // HP for Soldier/Jumper
        if (block.Type == BlockType.Soldier || block.Type == BlockType.Jumper)
        {
            string hpText = $"HP: {block.HitPoints}";
            DrawString(font, new Vector2(10, y + 14), hpText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);
            y += 16f;
        }

        // Root progress
        if (block.IsRooting || block.IsRooted)
        {
            string rootText = block.IsRooted ? "Rooted" : $"Rooting: {block.RootProgress * 100 / 36:F0}%";
            DrawString(font, new Vector2(10, y + 14), rootText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);
        }
    }

    private void DrawMultiUnitInfo(Font font, float y)
    {
        // Count by type
        var counts = new Dictionary<BlockType, int>();
        foreach (var block in _selectedBlocks!)
        {
            counts.TryGetValue(block.Type, out int c);
            counts[block.Type] = c + 1;
        }

        string label = counts.Count == 1
            ? $"SELECTED: {_selectedBlocks.Count} {counts.Keys.First()}S"
            : $"SELECTED: {_selectedBlocks.Count} UNITS";

        DrawString(font, new Vector2(10, y + 14), label,
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        y += 24f;

        // Unit icons grid
        float x = 10f;
        float maxX = Size.X - 10f;
        foreach (var block in _selectedBlocks)
        {
            if (x + UnitIconSize > maxX)
            {
                x = 10f;
                y += UnitIconSize + UnitIconGap;
            }

            var iconRect = new Rect2(x, y, UnitIconSize, UnitIconSize);
            var color = GetBlockColor(block.Type);
            DrawRect(iconRect, color);
            DrawRect(iconRect, color.Lightened(0.3f), false, 2f);

            x += UnitIconSize + UnitIconGap;
        }
    }

    private static Color GetBlockColor(BlockType type) => type switch
    {
        BlockType.Builder => new Color(0.231f, 0.510f, 0.965f),
        BlockType.Soldier => new Color(0.133f, 0.773f, 0.369f),
        BlockType.Stunner => new Color(0.659f, 0.333f, 0.969f),
        BlockType.Jumper => new Color(0.965f, 0.600f, 0.200f),
        BlockType.Warden => new Color(0.200f, 0.800f, 0.800f),
        BlockType.Wall => new Color(0.5f, 0.5f, 0.5f),
        _ => new Color(0.5f, 0.5f, 0.5f)
    };

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Check control group clicks
            int groupIndex = GetControlGroupAt(mb.Position);
            if (groupIndex >= 0)
            {
                EmitSignal(SignalName.ControlGroupClicked, groupIndex, mb.CtrlPressed);
                AcceptEvent();
                return;
            }

            // Check unit icon clicks
            int blockId = GetUnitIconAt(mb.Position);
            if (blockId >= 0)
            {
                EmitSignal(SignalName.UnitClicked, blockId, mb.ShiftPressed);
                AcceptEvent();
            }
        }
    }

    private int GetControlGroupAt(Vector2 pos)
    {
        float y = 8f;
        float x = 60f;
        for (int i = 1; i <= 9; i++)
        {
            var rect = new Rect2(x, y, ControlGroupWidth, ControlGroupHeight);
            if (rect.HasPoint(pos))
                return i;
            x += ControlGroupWidth + 4f;
        }
        return -1;
    }

    private int GetUnitIconAt(Vector2 pos)
    {
        if (_selectedBlocks == null || _selectedBlocks.Count <= 1)
            return -1;

        float y = 8f + ControlGroupHeight + 8f + 24f;
        float x = 10f;
        float maxX = Size.X - 10f;

        foreach (var block in _selectedBlocks)
        {
            if (x + UnitIconSize > maxX)
            {
                x = 10f;
                y += UnitIconSize + UnitIconGap;
            }

            var iconRect = new Rect2(x, y, UnitIconSize, UnitIconSize);
            if (iconRect.HasPoint(pos))
                return block.Id;

            x += UnitIconSize + UnitIconGap;
        }
        return -1;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/SelectionPanel.cs
git commit -m "feat(hud): add SelectionPanel with control groups and unit info"
```

---

### Task 6: Create CommandCard

**Files:**
- Create: `godot/Scripts/Rendering/CommandCard.cs`

- [ ] **Step 1: Create CommandCard class**

```csharp
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Right panel showing available commands for current selection.
/// </summary>
public partial class CommandCard : Control
{
    [Signal] public delegate void CommandClickedEventHandler(string commandKey);

    private IReadOnlyList<Block>? _selectedBlocks;

    private record CommandDef(string Key, string Name, string Icon, string Hotkey, Func<Block, bool> Available, Func<Block, bool>? Conditional = null);

    private static readonly CommandDef[] AllCommands =
    [
        new("root", "Root", "⌾", "F", b => b.Type != BlockType.Wall && !b.IsRooted && !b.IsRooting),
        new("uproot", "Uproot", "⊙", "U", b => b.IsRooted || b.IsRooting),
        new("wall", "Wall", "▣", "W", b => b.Type == BlockType.Builder && b.IsRooted),
        new("push", "Push", "↠", "G", b => b.Type == BlockType.Builder && b.IsRooted && !b.IsInFormation),
        new("explode", "Explode", "✸", "D", b => b.Type == BlockType.Soldier, b => b.IsRooted),
        new("stun", "Stun", "⚡", "S", b => b.Type == BlockType.Stunner && b.IsRooted),
        new("jump", "Jump", "⤴", "J", b => b.Type == BlockType.Jumper),
        new("magnet", "Magnet", "🧲", "M", b => b.Type == BlockType.Warden && b.IsRooted),
    ];

    private const float ButtonSize = 32f;
    private const float ButtonGap = 5f;
    private const int Columns = 3;

    public void SetSelection(IReadOnlyList<Block>? blocks) => _selectedBlocks = blocks;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        HudStyles.DrawInnerPanel(this, rect);

        var font = ThemeDB.FallbackFont;

        // Label
        DrawString(font, new Vector2(10, 14), "COMMANDS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
            return;

        // Get available commands
        var available = GetAvailableCommands();

        float startY = 24f;
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var (cmd, isConditional) in available)
        {
            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);

            // Button background
            if (isConditional)
            {
                DrawRect(btnRect, HudStyles.PanelBgTop with { A = 0.4f });
                DrawRect(btnRect, HudStyles.PanelBorder with { A = 0.4f }, false, 1f);
            }
            else
            {
                DrawRect(btnRect, HudStyles.PanelBgTop);
                DrawRect(btnRect, HudStyles.PanelBorder, false, 1f);
            }

            // Icon
            var iconColor = isConditional ? HudStyles.TextDim : HudStyles.TextSecondary;
            var iconSize = font.GetStringSize(cmd.Icon, HorizontalAlignment.Left, -1, 14);
            DrawString(font, new Vector2(x + (ButtonSize - iconSize.X) / 2, y + 20), cmd.Icon,
                HorizontalAlignment.Left, -1, 14, iconColor);

            // Hotkey
            var hotkeyColor = isConditional ? HudStyles.TextDim with { A = 0.5f } : HudStyles.TextDim;
            DrawString(font, new Vector2(btnRect.End.X - 10, btnRect.End.Y - 4), cmd.Hotkey,
                HorizontalAlignment.Right, -1, HudStyles.FontSizeHotkey, hotkeyColor);

            col++;
            if (col >= Columns)
            {
                col = 0;
                x = 10f;
                y += ButtonSize + ButtonGap;
            }
            else
            {
                x += ButtonSize + ButtonGap;
            }
        }
    }

    private List<(CommandDef cmd, bool isConditional)> GetAvailableCommands()
    {
        var result = new List<(CommandDef, bool)>();
        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
            return result;

        foreach (var cmd in AllCommands)
        {
            bool anyAvailable = false;
            bool allConditional = true;

            foreach (var block in _selectedBlocks)
            {
                if (cmd.Available(block))
                {
                    anyAvailable = true;
                    if (cmd.Conditional == null || cmd.Conditional(block))
                        allConditional = false;
                }
            }

            if (anyAvailable)
            {
                bool isConditional = cmd.Conditional != null && allConditional;
                result.Add((cmd, isConditional));
            }
        }

        return result;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            string? cmdKey = GetCommandAt(mb.Position);
            if (cmdKey != null)
            {
                EmitSignal(SignalName.CommandClicked, cmdKey);
                AcceptEvent();
            }
        }
    }

    private string? GetCommandAt(Vector2 pos)
    {
        var available = GetAvailableCommands();
        float startY = 24f;
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var (cmd, isConditional) in available)
        {
            if (isConditional) // Can't click conditional commands
            {
                col++;
                if (col >= Columns) { col = 0; x = 10f; y += ButtonSize + ButtonGap; }
                else { x += ButtonSize + ButtonGap; }
                continue;
            }

            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);
            if (btnRect.HasPoint(pos))
                return cmd.Key;

            col++;
            if (col >= Columns)
            {
                col = 0;
                x = 10f;
                y += ButtonSize + ButtonGap;
            }
            else
            {
                x += ButtonSize + ButtonGap;
            }
        }
        return null;
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/CommandCard.cs
git commit -m "feat(hud): add CommandCard with contextual command buttons"
```

---

### Task 7: Update HudBar to Use New Panels

**Files:**
- Modify: `godot/Scripts/Rendering/HudBar.cs`

- [ ] **Step 1: Replace placeholder panels with new components**

Update HudBar to use SelectionPanel and CommandCard:

```csharp
using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Bottom-of-screen HUD bar with three panels:
/// Left = minimap, Center = selection info, Right = command card.
/// </summary>
public partial class HudBar : CanvasLayer
{
    private MinimapPanel _minimap = null!;
    private SelectionPanel _selectionPanel = null!;
    private CommandCard _commandCard = null!;

    [Signal] public delegate void MinimapCameraJumpEventHandler(Vector2 worldPos);
    [Signal] public delegate void ControlGroupClickedEventHandler(int groupIndex, bool ctrlHeld);
    [Signal] public delegate void UnitClickedEventHandler(int blockId, bool shiftHeld);
    [Signal] public delegate void CommandClickedEventHandler(string commandKey);

    public override void _Ready()
    {
        Layer = 10;

        // Anchor bar to bottom of screen
        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.OffsetTop = -HudStyles.BottomBarHeight;
        anchor.OffsetBottom = 0;
        anchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(anchor);

        // Background panel
        var bg = new HudBarBackground();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        anchor.AddChild(bg);

        // HBox for three panels
        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", (int)HudStyles.PanelGap);
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        anchor.AddChild(margin);
        margin.AddChild(hbox);

        // Left: Minimap (fixed width)
        _minimap = new MinimapPanel
        {
            CustomMinimumSize = new Vector2(HudStyles.FixedPanelWidth, 0),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _minimap.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _minimap.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _minimap.CameraJumpRequested += pos => EmitSignal(SignalName.MinimapCameraJump, pos);
        hbox.AddChild(_minimap);

        // Center: Selection panel (flexible)
        _selectionPanel = new SelectionPanel
        {
            CustomMinimumSize = new Vector2(0, 0),
        };
        _selectionPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _selectionPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _selectionPanel.ControlGroupClicked += (idx, ctrl) => EmitSignal(SignalName.ControlGroupClicked, idx, ctrl);
        _selectionPanel.UnitClicked += (id, shift) => EmitSignal(SignalName.UnitClicked, id, shift);
        hbox.AddChild(_selectionPanel);

        // Right: Command card (fixed width)
        _commandCard = new CommandCard
        {
            CustomMinimumSize = new Vector2(HudStyles.FixedPanelWidth, 0),
        };
        _commandCard.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _commandCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _commandCard.CommandClicked += key => EmitSignal(SignalName.CommandClicked, key);
        hbox.AddChild(_commandCard);
    }

    public void SetGameState(GameState state) => _minimap.SetGameState(state);
    public void SetConfig(GameConfig config) => _minimap.SetConfig(config);

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _minimap.SetCameraView(worldPos, viewSize);
    }

    public void SetSelection(IReadOnlyList<Block>? blocks)
    {
        _selectionPanel.SetSelection(blocks);
        _commandCard.SetSelection(blocks);
    }

    public void SetControlGroups(IReadOnlyDictionary<int, IReadOnlyList<int>>? groups)
    {
        _selectionPanel.SetControlGroups(groups);
    }

    /// <summary>Inner control for drawing the bar background.</summary>
    private partial class HudBarBackground : Control
    {
        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            // Draw gradient background
            var topRect = new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.5f));
            var bottomRect = new Rect2(
                new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f),
                new Vector2(rect.Size.X, rect.Size.Y * 0.5f));

            DrawRect(topRect, HudStyles.PanelBgTop);
            DrawRect(bottomRect, HudStyles.PanelBgBottom);

            // Top border
            DrawLine(new Vector2(0, 0), new Vector2(Size.X, 0), HudStyles.PanelBorder, HudStyles.PanelBorderWidth);
        }
    }
}
```

- [ ] **Step 2: Build and verify**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Rendering/HudBar.cs
git commit -m "feat(hud): integrate SelectionPanel and CommandCard into HudBar"
```

---

### Task 8: Wire Everything in GameManager

**Files:**
- Modify: `godot/Scripts/Game/GameManager.cs`

- [ ] **Step 1: Add SpawnToggles and wire selection state to HudBar**

Add field and instantiation:

```csharp
private SpawnToggles _spawnToggles = null!;
```

In the HUD setup section of `_Ready()`, after creating `_hudBar`:

```csharp
// Set up spawn toggles (floating, top-right)
_spawnToggles = new SpawnToggles();
_spawnToggles.SetAnchorsPreset(Control.LayoutPreset.TopRight);
_spawnToggles.OffsetLeft = -70;
_spawnToggles.OffsetTop = HudStyles.TopBarHeight + 20;
_spawnToggles.OffsetRight = -20;
_spawnToggles.OffsetBottom = HudStyles.TopBarHeight + 20 + 110;
AddChild(_spawnToggles);
```

- [ ] **Step 2: Update _Process to feed selection state to HudBar**

Add to `_Process()`:

```csharp
// Update HUD with selection state
_hudBar.SetSelection(_selectionManager.SelectedBlocks);

// Note: Control groups are internal to SelectionManager currently.
// To expose them, we'd need to add a public getter. For now, skip.
```

- [ ] **Step 3: Add using statement for HudStyles**

At top of file, ensure:

```csharp
using Blocker.Game.Rendering;
```

- [ ] **Step 4: Build and test**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/Game/GameManager.cs
git commit -m "feat(hud): wire SpawnToggles and selection state in GameManager"
```

---

### Task 9: Expose Control Groups from SelectionManager

**Files:**
- Modify: `godot/Scripts/Input/SelectionManager.cs`

- [ ] **Step 1: Add public getter for control groups**

Add property to SelectionManager:

```csharp
public IReadOnlyDictionary<int, IReadOnlyList<int>> ControlGroups =>
    _controlGroups.ToDictionary(
        kvp => kvp.Key,
        kvp => (IReadOnlyList<int>)kvp.Value.AsReadOnly());
```

- [ ] **Step 2: Update GameManager to pass control groups to HudBar**

In GameManager `_Process()`:

```csharp
_hudBar.SetControlGroups(_selectionManager.ControlGroups);
```

- [ ] **Step 3: Build and test**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: Build succeeded

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Input/SelectionManager.cs godot/Scripts/Game/GameManager.cs
git commit -m "feat(hud): expose control groups to HUD"
```

---

### Task 10: Manual Integration Test

**Files:** None (testing only)

- [ ] **Step 1: Launch game and verify top bar**

Open Godot, run the game. Check:
- Player color/name displays
- Time shows in mm:ss format
- Population shows correctly
- Menu button opens dropdown with Surrender and Exit

- [ ] **Step 2: Verify spawn toggles**

- Floating panel visible top-right
- Clicking toggles dim/brighten buttons
- Hotkeys 1/2/3 work

- [ ] **Step 3: Verify bottom bar**

- Minimap has label, clear borders
- Selection panel shows "No selection" when empty
- Select single unit: shows type and stats
- Select multiple units: shows icon grid
- Command card shows context-appropriate commands

- [ ] **Step 4: Verify control groups**

- Assign units to group (Ctrl+1)
- Group button shows count
- Click group button to recall selection

- [ ] **Step 5: Final commit if any fixes needed**

```bash
git add -A
git commit -m "fix(hud): integration test fixes"
```

---

## Summary

| Task | Description | Files |
|------|-------------|-------|
| 1 | HudStyles constants | Create HudStyles.cs |
| 2 | Top bar redesign | Modify HudOverlay.cs |
| 3 | SpawnToggles panel | Create SpawnToggles.cs |
| 4 | Minimap improvements | Modify MinimapPanel.cs |
| 5 | SelectionPanel | Create SelectionPanel.cs |
| 6 | CommandCard | Create CommandCard.cs |
| 7 | HudBar integration | Modify HudBar.cs |
| 8 | GameManager wiring | Modify GameManager.cs |
| 9 | Expose control groups | Modify SelectionManager.cs, GameManager.cs |
| 10 | Manual integration test | Testing only |
