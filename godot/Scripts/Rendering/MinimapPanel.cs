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
