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

    // How much extra space (as fraction of grid size) to show around the grid
    private const float MarginFraction = 0.15f;

    private static readonly Color ViewportRectColor = new(1f, 1f, 1f, 0.7f);
    private static readonly Color TerrainColor = new(0.3f, 0.3f, 0.35f);

    private int _frameCounter;
    private const int RedrawEveryNFrames = 3;

    private VisibilityMap? _localVisibility;

    public void SetGameState(GameState state) => _gameState = state;
    public void SetConfig(GameConfig config) => _config = config;
    public void SetVisibility(VisibilityMap? vm) => _localVisibility = vm;

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _cameraWorldPos = worldPos;
        _cameraViewSize = viewSize;
    }

    public override void _Ready()
    {
        ClipContents = true;
    }

    public override void _Process(double delta)
    {
        _frameCounter++;
        if (_frameCounter % RedrawEveryNFrames == 0)
            QueueRedraw();
    }

    /// <summary>
    /// Computes the mapping from "world in grid-cell units" to panel pixels.
    /// The total area is the grid plus MarginFraction on each side.
    /// Returns (scale, gridOffsetX, gridOffsetY) where gridOffset is
    /// the panel-pixel position of grid cell (0,0).
    /// </summary>
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

        // Grid (0,0) starts after the margin cells
        float gridOffX = areaOffX + marginCellsX * scale;
        float gridOffY = areaOffY + marginCellsY * scale;

        return (scale, gridOffX, gridOffY);
    }

    public override void _Draw()
    {
        if (_gameState == null) return;

        var grid = _gameState.Grid;
        var panelSize = Size;
        var font = ThemeDB.FallbackFont;

        // Panel background with border
        HudStyles.DrawInnerPanel(this, new Rect2(Vector2.Zero, panelSize));

        // Label
        // DrawString(font, new Vector2(10, 14), "MINIMAP",
            // HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        var (scale, gridOffX, gridOffY) = ComputeLayout();

        // Grid area background
        float gridW = grid.Width * scale;
        float gridH = grid.Height * scale;
        DrawRect(new Rect2(gridOffX, gridOffY, gridW, gridH), HudStyles.PanelBgTop);

        // Draw ground and terrain
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var cell = grid[x, y];
                Color color;

                bool explored = _localVisibility == null || _localVisibility.IsExplored(x, y);
                bool visible = _localVisibility == null || _localVisibility.IsVisible(x, y);

                if (!explored)
                {
                    color = Colors.Black; // Shroud
                }
                else
                {
                    if (cell.Terrain != TerrainType.None)
                        color = TerrainColor;
                    else if (cell.Ground != GroundType.Normal)
                        color = _config.GetGroundColor(cell.Ground);
                    else
                        color = HudStyles.PanelBgTop; // Draw bg manually if explored to override shroud

                    // Dim if in fog (explored but not visible)
                    if (!visible)
                        color = color.Darkened(0.5f);
                }

                if (color == HudStyles.PanelBgTop && explored && visible)
                    continue; // Optimize: background already covers it

                var rect = new Rect2(
                    gridOffX + x * scale,
                    gridOffY + y * scale,
                    Mathf.Max(scale, 1f),
                    Mathf.Max(scale, 1f));
                DrawRect(rect, color);
            }
        }

        // Draw blocks as colored dots
        float dotSize = Mathf.Max(scale * 0.8f, 1.5f);
        foreach (var block in _gameState.Blocks)
        {
            if (_localVisibility != null && !_localVisibility.IsVisible(block.Pos))
                continue;

            var bcolor = _config.GetPalette(block.PlayerId).Base;
            float bx = gridOffX + (block.Pos.X + 0.5f) * scale;
            float by = gridOffY + (block.Pos.Y + 0.5f) * scale;
            DrawRect(new Rect2(bx - dotSize * 0.5f, by - dotSize * 0.5f, dotSize, dotSize), bcolor);
        }

        // Draw camera viewport rectangle (in grid-cell units, offset from grid origin)
        float cellSize = GridRenderer.CellSize;
        float camLeft = (_cameraWorldPos.X - _cameraViewSize.X * 0.5f - GridRenderer.GridPadding) / cellSize;
        float camTop = (_cameraWorldPos.Y - _cameraViewSize.Y * 0.5f - GridRenderer.GridPadding) / cellSize;
        float camW = _cameraViewSize.X / cellSize;
        float camH = _cameraViewSize.Y / cellSize;

        var vpRect = new Rect2(
            gridOffX + camLeft * scale,
            gridOffY + camTop * scale,
            camW * scale,
            camH * scale);

        DrawRect(vpRect, ViewportRectColor, false, 1.0f);
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

        var (scale, gridOffX, gridOffY) = ComputeLayout();

        // Convert click to grid coords (can be negative or > grid size in margin area)
        float gridX = (localPos.X - gridOffX) / scale;
        float gridY = (localPos.Y - gridOffY) / scale;

        float worldX = gridX * GridRenderer.CellSize + GridRenderer.GridPadding;
        float worldY = gridY * GridRenderer.CellSize + GridRenderer.GridPadding;

        EmitSignal(SignalName.CameraJumpRequested, new Vector2(worldX, worldY));
    }
}
