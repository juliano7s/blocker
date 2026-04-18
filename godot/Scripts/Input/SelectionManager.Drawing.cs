using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

public partial class SelectionManager
{
    public override void _Draw()
    {
        if (_gameState == null) return;

        // Hover highlight
        if (_hoveredCell.HasValue && !_isDragging)
        {
            var pos = _hoveredCell.Value;
            if (_blueprint.IsActive)
            {
                // Draw blueprint hover preview (unit sprites, translucent)
                var cells = _blueprint.GetCells();
                foreach (var cell in cells)
                {
                    var cellPos = new GridPos(pos.X + cell.Offset.X, pos.Y + cell.Offset.Y);
                    var rect = CellRect(cellPos);
                    var bt = RoleToBlockType(cell.Role);
                    BlockIconPainter.Draw(this, bt, ControllingPlayer, rect, _config, enabled: true, alpha: 0.5f);
                    var roleColor = GetBlueprintRoleColor(cell.Role);
                    DrawRect(rect, roleColor with { A = 0.8f }, false, 2f);
                }
            }
            else if (_attackMoveMode || _jumpMode)
            {
                var color = _jumpMode ? new Color(0.3f, 0.8f, 1f, 0.5f) : AttackMoveColor;
                var borderColor = _jumpMode ? new Color(0.3f, 0.8f, 1f, 0.8f) : new Color(1f, 0.3f, 0.3f, 0.8f);
                var rect = CellRect(pos);
                DrawRect(rect, color);
                DrawRect(rect, borderColor, false, 2f);
            }
            else
            {
                var rect = CellRect(pos);
                DrawRect(rect, HoverColor);
                DrawRect(rect, HoverBorderColor, false, 1f);
            }
        }

        // Blueprint placed ghosts
        float now = (float)Time.GetTicksMsec() / 1000f;
        _blueprint.PruneGhosts(now);
        foreach (var ghost in _blueprint.PlacedGhosts)
        {
            var ghostCells = BlueprintMode.GetCells(ghost.Type, ghost.Rotation);
            float age = now - ghost.PlacedAt;
            float fadeAlpha = Mathf.Clamp(1f - age / 15f, 0.15f, 0.45f);
            foreach (var cell in ghostCells)
            {
                var cellPos = new GridPos(ghost.Position.X + cell.Offset.X, ghost.Position.Y + cell.Offset.Y);
                var rect = CellRect(cellPos);
                var bt = RoleToBlockType(cell.Role);
                BlockIconPainter.Draw(this, bt, ControllingPlayer, rect, _config, enabled: true, alpha: fadeAlpha);
            }
        }

        // Right-drag paint cells
        if (_isRightDragging && _paintedCells.Count > 1)
        {
            foreach (var cell in _paintedCells)
            {
                var rect = CellRect(cell);
                DrawRect(rect, PaintCellColor);
                DrawCircle(GridRenderer.GridToWorld(cell), GridRenderer.CellSize * 0.1f,
                    new Color(0.3f, 0.9f, 0.3f, 0.6f));
            }
        }

        // Move target dots (correctly at grid destination, not visual pos)
        foreach (var block in _state.SelectedBlocks)
        {
            if (block.MoveTarget.HasValue)
            {
                var from = GridRenderer.GridToWorld(block.Pos);
                var to = GridRenderer.GridToWorld(block.MoveTarget.Value);
                DrawDashedLine(from, to, QueuePathColor, 1f, 3f, 4f);
                DrawCircle(to, GridRenderer.CellSize * 0.12f, QueuePathColor);
            }
        }

        // Command queue paths (dotted lines through waypoints)
        foreach (var block in _state.SelectedBlocks)
        {
            var from = GridRenderer.GridToWorld(block.MoveTarget ?? block.Pos);
            foreach (var cmd in block.CommandQueue)
            {
                if (cmd.TargetPos.HasValue)
                {
                    var to = GridRenderer.GridToWorld(cmd.TargetPos.Value);
                    DrawDashedLine(from, to, QueuePathColor, 1f, 3f, 4f);
                    DrawCircle(to, GridRenderer.CellSize * 0.08f, QueuePathColor);
                    from = to;
                }
            }
        }

        // Drag rectangle
        if (_isDragging)
        {
            var dragRect = new Rect2(
                Mathf.Min(_dragStart.X, _dragEnd.X),
                Mathf.Min(_dragStart.Y, _dragEnd.Y),
                Mathf.Abs(_dragEnd.X - _dragStart.X),
                Mathf.Abs(_dragEnd.Y - _dragStart.Y)
            );
            DrawRect(dragRect, DragRectColor);
            DrawRect(dragRect, DragRectBorderColor, false, 1f);
        }
    }

    private static BlockType RoleToBlockType(string role) => role switch
    {
        "builder" => BlockType.Builder,
        "soldier" => BlockType.Soldier,
        "stunner" => BlockType.Stunner,
        "wall" => BlockType.Wall,
        _ => BlockType.Builder,
    };

    private Color GetBlueprintRoleColor(string role)
    {
        var baseColor = _config?.GetPalette(ControllingPlayer).Base ?? new Color(0.3f, 0.6f, 1f);
        return role switch
        {
            "center" => Colors.White,
            "wall" => baseColor.Lerp(Colors.Gray, 0.4f),
            _ => baseColor
        };
    }

    private static Rect2 CellRect(GridPos pos) =>
        new(pos.X * GridRenderer.CellSize + GridRenderer.GridPadding, pos.Y * GridRenderer.CellSize + GridRenderer.GridPadding,
            GridRenderer.CellSize, GridRenderer.CellSize);

    private void DrawDashedRect(Rect2 rect, Color color, float width, float dashLen, float gapLen)
    {
        var tl = rect.Position;
        var tr = tl + new Vector2(rect.Size.X, 0);
        var br = tl + rect.Size;
        var bl = tl + new Vector2(0, rect.Size.Y);

        DrawDashedLine(tl, tr, color, width, dashLen, gapLen);
        DrawDashedLine(tr, br, color, width, dashLen, gapLen);
        DrawDashedLine(br, bl, color, width, dashLen, gapLen);
        DrawDashedLine(bl, tl, color, width, dashLen, gapLen);
    }

    private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float lineWidth, float dashLen, float gapLen)
    {
        var dir = to - from;
        var totalLen = dir.Length();
        if (totalLen < 0.01f) return;
        dir /= totalLen;

        float pos = 0;
        bool drawing = true;
        while (pos < totalLen)
        {
            var segLen = Mathf.Min(drawing ? dashLen : gapLen, totalLen - pos);
            if (drawing)
                DrawLine(from + dir * pos, from + dir * (pos + segLen), color, lineWidth);
            pos += segLen;
            drawing = !drawing;
        }
    }
}
