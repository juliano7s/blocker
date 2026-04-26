using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Rendering;

public partial class GridRenderer : Node2D
{
    private ColorRect? _fogRect;
    private ShaderMaterial? _fogMaterial;
    private ImageTexture? _fogTexture;
    private Image? _fogImage;

    // Ghost blocks: last-seen state of static units (walls, rooted, formation members).
    private readonly Dictionary<GridPos, (BlockType Type, int PlayerId)> _fogGhosts = new();

    private int _fogLastTick = -1;

    private void InitFogOverlay()
    {
        _fogRect?.QueueFree();
        _fogRect = null;

        if (_gameState == null || !Constants.FogOfWarEnabled) return;

        var shader = GD.Load<Shader>("res://Assets/Shaders/fog_overlay.gdshader");
        _fogMaterial = new ShaderMaterial { Shader = shader };
        _fogRect = new ColorRect
        {
            Material = _fogMaterial,
            Color = Colors.Transparent, // Prevent white screen if shader fails
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 10, // Above blocks (~Z 0), below UI
        };
        AddChild(_fogRect);

        var grid = _gameState.Grid;
        _fogImage = Image.CreateEmpty(grid.Width, grid.Height, false, Image.Format.Rgba8);
        _fogTexture = ImageTexture.CreateFromImage(_fogImage);

        _fogMaterial.SetShaderParameter("fog_data", _fogTexture);
        _fogMaterial.SetShaderParameter("grid_size", new Vector2I(grid.Width, grid.Height));
        _fogMaterial.SetShaderParameter("cell_size", CellSize);
        _fogMaterial.SetShaderParameter("grid_padding", GridPadding);

        float totalW = grid.Width * CellSize + GridPadding * 2f;
        float totalH = grid.Height * CellSize + GridPadding * 2f;
        _fogRect.Size = new Vector2(totalW, totalH);
        _fogRect.Position = new Vector2(-GridPadding, -GridPadding); // offset by padding
    }

    private void UpdateFog()
    {
        if (_gameState == null) return;
        
        if (_localVisibility == null || !Constants.FogOfWarEnabled) 
        {
            if (_fogRect != null)
            {
                _fogRect.QueueFree();
                _fogRect = null;
                _fogGhosts.Clear();
                _fogLastTick = -1;
            }
            return;
        }

        // Initialize if not ready
        if (_fogRect == null)
            InitFogOverlay();

        if (_gameState.TickNumber == _fogLastTick) return;
        _fogLastTick = _gameState.TickNumber;

        var grid = _gameState.Grid;

        // Update ghost state: record last-seen static blocks in visible cells
        foreach (var block in _gameState.Blocks)
        {
            if (!_localVisibility.IsVisible(block.Pos)) continue;

            bool isStatic = block.Type == BlockType.Wall
                || block.IsFullyRooted
                || block.IsInFormation;

            if (isStatic)
                _fogGhosts[block.Pos] = (block.Type, block.PlayerId);
            else
                _fogGhosts.Remove(block.Pos);
        }

        // Clear ghosts in visible cells that are now empty
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                var pos = new GridPos(x, y);
                if (_localVisibility.IsVisible(pos) && !grid[pos].BlockId.HasValue)
                    _fogGhosts.Remove(pos);
            }
        }

        // Write fog texture
        if (_fogImage == null || _fogTexture == null) return;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                float val = _localVisibility.IsVisible(x, y) ? 1.0f
                    : _localVisibility.IsExplored(x, y) ? 128f / 255f
                    : 0.0f;
                _fogImage.SetPixel(x, y, new Color(val, 0f, 0f, 1f));
            }
        }

        _fogTexture.Update(_fogImage);
    }

    private void DrawFogGhosts()
    {
        if (_localVisibility == null || !Constants.FogOfWarEnabled) return;

        foreach (var (pos, ghost) in _fogGhosts)
        {
            if (!_localVisibility.IsExplored(pos) || _localVisibility.IsVisible(pos)) continue;

            var worldPos = GridToWorld(pos);
            var rect = new Rect2(
                worldPos.X - CellSize / 2f + BlockInset,
                worldPos.Y - CellSize / 2f + BlockInset,
                CellSize - BlockInset * 2,
                CellSize - BlockInset * 2
            );

            var baseColor = _config.GetPalette(ghost.PlayerId).Base;
            var ghostColor = new Color(
                baseColor.R * 0.50f,
                baseColor.G * 0.50f,
                baseColor.B * 0.55f,
                0.45f
            );

            DrawRect(rect, ghostColor);

            if (ghost.Type == BlockType.Wall)
            {
                DrawRect(rect, new Color(ghostColor.R, ghostColor.G, ghostColor.B, ghostColor.A * 0.6f), false, 1.5f);
            }
        }
    }
}