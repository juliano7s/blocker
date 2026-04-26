using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System.Collections.Generic;
using Blocker.Game.Config;

namespace Blocker.Game.Rendering;

public partial class GridRenderer : Node2D
{
    private ColorRect? _fogRect;
    private ShaderMaterial? _fogMaterial;
    private ImageTexture? _fogTexture;
    private Image? _fogImage;

    private enum GhostKind { Block, Formation, Nest, Tower }
    private record struct GhostData(GhostKind Kind, int SubType, int PlayerId, GridPos Pos, bool InFormation = false);

    // Block ghosts keyed by grid position (one block per cell)
    private readonly Dictionary<GridPos, GhostData> _blockGhosts = new();
    // Structure ghosts keyed by (kind, entityId)
    private readonly Dictionary<(GhostKind Kind, int Id), GhostData> _structureGhosts = new();

    // Pre-allocated collections for per-tick cleanup — avoids GC pressure
    private readonly List<GridPos> _staleBlockPositions = new();
    private readonly List<(GhostKind, int)> _staleStructureKeys = new();
    private readonly HashSet<int> _liveFormationIds = new();
    private readonly HashSet<int> _liveNestIds = new();
    private readonly HashSet<int> _liveTowerIds = new();
    private readonly List<GhostData> _formationGhostBuffer = new();
    private readonly Dictionary<GridPos, GhostData> _formationGhostPositions = new();

    private int _fogLastTick = -1;

    private void ClearAllGhosts()
    {
        _blockGhosts.Clear();
        _structureGhosts.Clear();
    }

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
            Color = Colors.Transparent,
            MouseFilter = Control.MouseFilterEnum.Ignore,
            ZIndex = 10,
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
        _fogRect.Position = Vector2.Zero;
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
                ClearAllGhosts();
                _fogLastTick = -1;
            }
            return;
        }

        if (_fogRect == null)
            InitFogOverlay();

        if (_gameState.TickNumber == _fogLastTick) return;
        _fogLastTick = _gameState.TickNumber;

        var grid = _gameState.Grid;

        // 1. Update block ghosts
        foreach (var block in _gameState.Blocks)
        {
            if (!_localVisibility.IsVisible(block.Pos)) continue;

            int teamId = _gameState.GetTeamFor(block.PlayerId);
            if (teamId == _controllingTeamId)
            {
                _blockGhosts.Remove(block.Pos);
                continue;
            }

            bool isStatic = block.Type == BlockType.Wall
                || block.IsFullyRooted
                || block.IsInFormation
                || block.State == BlockState.Rooting;

            if (isStatic)
                _blockGhosts[block.Pos] = new GhostData(GhostKind.Block, (int)block.Type, block.PlayerId, block.Pos, block.IsInFormation);
            else
                _blockGhosts.Remove(block.Pos);
        }

        // Clear block ghosts in visible cells that are now empty — O(ghosts) not O(grid)
        _staleBlockPositions.Clear();
        foreach (var (pos, _) in _blockGhosts)
        {
            if (_localVisibility.IsVisible(pos) && !grid[pos].BlockId.HasValue)
                _staleBlockPositions.Add(pos);
        }
        foreach (var pos in _staleBlockPositions)
            _blockGhosts.Remove(pos);

        // 2. Update formation ghosts
        foreach (var f in _gameState.Formations)
        {
            if (f.MemberIds.Count == 0) continue;
            var centerBlock = _gameState.GetBlock(f.MemberIds[0]);
            if (centerBlock == null) continue;
            if (!_localVisibility.IsVisible(centerBlock.Pos)) continue;

            var key = (GhostKind.Formation, f.Id);
            if (_gameState.GetTeamFor(f.PlayerId) == _controllingTeamId)
            {
                _structureGhosts.Remove(key);
                continue;
            }
            _structureGhosts[key] = new GhostData(GhostKind.Formation, (int)f.Type, f.PlayerId, centerBlock.Pos);
            _blockGhosts.Remove(centerBlock.Pos);
        }

        // 3. Update nest ghosts
        foreach (var n in _gameState.Nests)
        {
            if (!_localVisibility.IsVisible(n.Center)) continue;

            var key = (GhostKind.Nest, n.Id);
            if (_gameState.GetTeamFor(n.PlayerId) == _controllingTeamId)
            {
                _structureGhosts.Remove(key);
                continue;
            }
            _structureGhosts[key] = new GhostData(GhostKind.Nest, (int)n.Type, n.PlayerId, n.Center);
            _blockGhosts.Remove(n.Center);
        }

        // 4. Update tower ghosts
        foreach (var t in _gameState.Towers)
        {
            var centerBlock = _gameState.GetBlock(t.CenterId);
            if (centerBlock == null) continue;
            if (!_localVisibility.IsVisible(centerBlock.Pos)) continue;

            var key = (GhostKind.Tower, t.Id);
            if (_gameState.GetTeamFor(t.PlayerId) == _controllingTeamId)
            {
                _structureGhosts.Remove(key);
                continue;
            }
            _structureGhosts[key] = new GhostData(GhostKind.Tower, (int)t.Type, t.PlayerId, centerBlock.Pos);
            _blockGhosts.Remove(centerBlock.Pos);
        }

        // 5. Clean up stale structure ghosts (visible area but structure destroyed)
        _liveFormationIds.Clear();
        foreach (var f in _gameState.Formations) _liveFormationIds.Add(f.Id);
        _liveNestIds.Clear();
        foreach (var n in _gameState.Nests) _liveNestIds.Add(n.Id);
        _liveTowerIds.Clear();
        foreach (var t in _gameState.Towers) _liveTowerIds.Add(t.Id);

        _staleStructureKeys.Clear();
        foreach (var (key, ghost) in _structureGhosts)
        {
            if (!_localVisibility.IsVisible(ghost.Pos)) continue;
            bool alive = key.Kind switch
            {
                GhostKind.Formation => _liveFormationIds.Contains(key.Id),
                GhostKind.Nest => _liveNestIds.Contains(key.Id),
                GhostKind.Tower => _liveTowerIds.Contains(key.Id),
                _ => false
            };
            if (!alive) _staleStructureKeys.Add(key);
        }
        foreach (var k in _staleStructureKeys) _structureGhosts.Remove(k);

        // 6. Write fog texture
        if (_fogImage == null || _fogTexture == null) return;

        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                bool visible = _localVisibility.IsVisible(x, y);
                float val = visible ? 1.0f
                    : _localVisibility.IsExplored(x, y) ? 0.5f
                    : 0.0f;
                _fogImage.SetPixel(x, y, new Color(val, 0f, 0f, 1f));
            }
        }

        _fogTexture.Update(_fogImage);
    }

    private void DrawFogGhosts()
    {
        if (_localVisibility == null || !Constants.FogOfWarEnabled) return;

        DrawGhostFormationJoiners();

        foreach (var ghost in _blockGhosts.Values)
        {
            if (!_localVisibility.IsExplored(ghost.Pos) || _localVisibility.IsVisible(ghost.Pos)) continue;
            DrawBlockGhost(ghost, _config.GetPalette(ghost.PlayerId));
        }

        foreach (var ghost in _structureGhosts.Values)
        {
            if (!_localVisibility.IsExplored(ghost.Pos) || _localVisibility.IsVisible(ghost.Pos)) continue;
            DrawStructureGhost(ghost);
        }
    }

    private void DrawGhostFormationJoiners()
    {
        _formationGhostBuffer.Clear();
        foreach (var g in _blockGhosts.Values)
            if (g.InFormation) _formationGhostBuffer.Add(g);

        if (_formationGhostBuffer.Count < 2) return;

        _formationGhostPositions.Clear();
        foreach (var g in _formationGhostBuffer) _formationGhostPositions[g.Pos] = g;

        foreach (var g in _formationGhostBuffer)
        {
            var palette = _config.GetPalette(g.PlayerId);
            var joinerColor = palette.SupplyOutline.Darkened(0.2f).Lerp(Colors.Gray, 0.4f) with { A = 0.25f };

            var right = g.Pos + new GridPos(1, 0);
            if (_formationGhostPositions.TryGetValue(right, out var g2) && g2.PlayerId == g.PlayerId)
            {
                var rectA = GetFormationBlockRect(g.Pos);
                var rectB = GetFormationBlockRect(right);
                float gapW = rectB.Position.X - rectA.End.X;
                float halfH = rectA.Size.Y * 0.75f;
                DrawRect(new Rect2(rectA.End.X, rectA.Position.Y + halfH * 0.16f, gapW, halfH), joinerColor);
            }

            var down = g.Pos + new GridPos(0, 1);
            if (_formationGhostPositions.TryGetValue(down, out var g3) && g3.PlayerId == g.PlayerId)
            {
                var rectA = GetFormationBlockRect(g.Pos);
                var rectB = GetFormationBlockRect(down);
                float gapH = rectB.Position.Y - rectA.End.Y;
                float halfW = rectA.Size.X * 0.75f;
                DrawRect(new Rect2(rectA.Position.X + halfW * 0.16f, rectA.End.Y, halfW, gapH), joinerColor);
            }
        }
    }

    private void DrawBlockGhost(GhostData ghost, PlayerPalette palette)
    {
        var worldPos = GridToWorld(ghost.Pos);
        var rect = new Rect2(
            worldPos.X - CellSize / 2f + BlockInset,
            worldPos.Y - CellSize / 2f + BlockInset,
            CellSize - BlockInset * 2,
            CellSize - BlockInset * 2
        );

        var sprite = SpriteFactory.GetSprite((BlockType)ghost.SubType, ghost.PlayerId);
        var ghostColor = palette.Base.Lerp(Colors.Gray, 0.4f) with { A = 0.5f };

        if (sprite != null)
            DrawTextureRect(sprite, rect, false, ghostColor);
        else
            DrawRect(rect, ghostColor);

        if (ghost.InFormation)
        {
            const float alpha = 0.4f;
            var blockRect = GetFormationBlockRect(ghost.Pos);
            var outline = palette.SupplyOutline.Lerp(Colors.Gray, 0.4f);
            var diamond = palette.SupplyDiamond.Lerp(Colors.Gray, 0.4f);
            DrawFormationBlock(blockRect, outline with { A = alpha }, Colors.Transparent, diamond with { A = alpha });
        }
        else if ((BlockType)ghost.SubType == BlockType.Wall && sprite == null)
        {
            DrawRect(rect, ghostColor with { A = 0.2f }, false, 1.5f);
        }
    }

    private void DrawStructureGhost(GhostData ghost)
    {
        var palette = _config.GetPalette(ghost.PlayerId);

        var (outline, glow, diamond) = ghost.Kind switch
        {
            GhostKind.Formation => GetFormationStyle((FormationType)ghost.SubType, palette),
            GhostKind.Nest => GetNestStyle((NestType)ghost.SubType, palette),
            GhostKind.Tower => GetTowerStyle((TowerType)ghost.SubType, palette),
            _ => (Colors.Gray, Colors.Black, Colors.Gray)
        };

        const float alpha = 0.4f;
        var blockRect = GetFormationBlockRect(ghost.Pos);
        DrawFormationBlock(blockRect, outline.Lerp(Colors.Gray, 0.4f) with { A = alpha }, Colors.Transparent, diamond.Lerp(Colors.Gray, 0.4f) with { A = alpha });
    }
}
