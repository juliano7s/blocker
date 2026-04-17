using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

public partial class SelectionManager
{
    public override void _UnhandledInput(InputEvent @event)
    {
        if (_gameState == null) return;

        // Mouse input
        if (@event is InputEventMouseButton mouseButton)
        {
            var mouseWorld = GetGlobalMousePosition();

            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    if (_jumpMode)
                    {
                        HandleJumpClick(mouseWorld, mouseButton.ShiftPressed);
                        _jumpMode = false;
                        return;
                    }
                    if (_blueprint.IsActive)
                    {
                        HandleBlueprintClick(mouseWorld, mouseButton.ShiftPressed);
                        return;
                    }
                    if (_attackMoveMode)
                    {
                        HandleAttackMoveClick(mouseWorld, mouseButton.ShiftPressed);
                        _attackMoveMode = false;
                        return;
                    }
                    _isDragging = true;
                    _dragStart = mouseWorld;
                    _dragEnd = mouseWorld;
                }
                else if (_isDragging)
                {
                    _isDragging = false;
                    var dragDist = (_dragEnd - _dragStart).Length();
                    bool additive = mouseButton.ShiftPressed;

                    if (dragDist < 5f)
                    {
                        var gridPos = GridRenderer.WorldToGrid(mouseWorld);
                        var now = Time.GetTicksMsec();
                        bool isDoubleClick = _lastClickCell.HasValue
                            && _lastClickCell.Value == gridPos
                            && (now - _lastClickTime) < DoubleClickMs;

                        if (isDoubleClick)
                            HandleDoubleClickSelect(mouseWorld);
                        else if (mouseButton.CtrlPressed)
                            HandleCtrlClickSelect(mouseWorld);
                        else
                            HandleClickSelect(mouseWorld, additive);

                        _lastClickTime = now;
                        _lastClickCell = gridPos;
                    }
                    else
                    {
                        HandleBoxSelect(_dragStart, _dragEnd, additive);
                    }
                }
            }
            else if (mouseButton.ButtonIndex == MouseButton.Right)
            {
                if (mouseButton.Pressed)
                {
                    if (_blueprint.IsActive)
                    {
                        _blueprint.Deactivate();
                        GD.Print("Blueprint mode cancelled");
                        return;
                    }
                    // Start tracking right-drag
                    _isRightDragging = true;
                    _rightDragStart = mouseWorld;
                    _paintedCells.Clear();
                    var gp = GridRenderer.WorldToGrid(mouseWorld);
                    if (_gameState.Grid.InBounds(gp))
                        _paintedCells.Add(gp);
                }
                else if (_isRightDragging)
                {
                    _isRightDragging = false;
                    var dragDist = (mouseWorld - _rightDragStart).Length();
                    bool queue = mouseButton.ShiftPressed;

                    if (dragDist < PaintDragThreshold || _paintedCells.Count <= 1)
                    {
                        // Short drag = normal right-click
                        HandleRightClick(mouseWorld, queue);
                    }
                    else
                    {
                        // Paint mode: distribute units to painted cells
                        HandlePaintRelease(queue);
                    }
                    _paintedCells.Clear();
                }
            }
        }

        // Keyboard input for abilities
        if (@event is InputEventKey { Pressed: true, Echo: false } key)
        {
            bool shiftHeld = key.ShiftPressed;
            switch (key.Keycode)
            {
                case Key.F:
                    if (_state.SelectedBlocks.Any(b => b.Type == BlockType.Jumper))
                    {
                        // Root non-jumpers immediately, then enter jump targeting for jumpers
                        var nonJumpers = _state.SelectedBlocks.Where(b => b.Type != BlockType.Jumper).ToList();
                        if (nonJumpers.Count > 0)
                        {
                            var ids = nonJumpers.Select(b => b.Id).ToList();
                            EmitCommand(new Command(ControllingPlayer, CommandType.Root, ids, Queue: shiftHeld));
                        }
                        _jumpMode = true;
                        _blueprint.Deactivate();
                        _attackMoveMode = false;
                        GD.Print("Jump: click target direction");
                    }
                    else
                        IssueCommandToSelected(CommandType.Root, shiftHeld);
                    break;
                case Key.V:
                    IssueCommandToSelected(CommandType.ConvertToWall, shiftHeld);
                    break;
                case Key.S when !key.IsCommandOrControlPressed():
                    IssueDirectionalCommand(CommandType.FireStunRay, shiftHeld);
                    break;
                case Key.D when !key.IsCommandOrControlPressed():
                    if (_state.SelectedBlocks.Any(b => b.Type == BlockType.Warden))
                        IssueCommandToSelected(CommandType.MagnetPull, shiftHeld);
                    else
                        IssueCommandToSelected(CommandType.SelfDestruct, shiftHeld);
                    break;
                case Key.Z:
                    IssueCommandToSelected(CommandType.CreateTower, shiftHeld);
                    break;
                case Key.G:
                    IssueDirectionalCommand(CommandType.TogglePush, shiftHeld);
                    break;
                case Key.A when !key.IsCommandOrControlPressed():
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _attackMoveMode = true;
                        _blueprint.Deactivate();
                        GD.Print("Attack-move: click target");
                    }
                    break;
                case Key.B:
                    if (_blueprint.IsActive)
                    {
                        _blueprint.Rotate();
                        GD.Print($"Blueprint rotated to {_blueprint.Rotation * 90}°");
                    }
                    break;
                case Key.X:
                    if (_blueprint.PlacedGhosts.Count > 0)
                    {
                        _blueprint.ClearGhosts();
                        GD.Print("Blueprint ghosts cleared");
                    }
                    break;
                case Key.Escape:
                    if (_blueprint.IsActive || _attackMoveMode || _jumpMode)
                    {
                        _blueprint.Deactivate();
                        _attackMoveMode = false;
                        _jumpMode = false;
                        GD.Print("Mode cancelled");
                    }
                    else
                        _state.Clear();
                    break;
                case Key.Quoteleft: // Backtick (`)
                    // Quick-select all uprooted Soldiers + Stunners
                    var combatUnits = _gameState.Blocks.Where(b => 
                        b.PlayerId == ControllingPlayer && 
                        b.Type is (BlockType.Soldier or BlockType.Stunner) &&
                        b.IsMobile && !b.IsInFormation);
                    _state.SelectAll(combatUnits);
                    GD.Print($"Quick-selected {_state.SelectedBlocks.Count} mobile combat units");
                    break;
                case Key.Tab:
                    // Hot-seat: switch player. Disabled in multiplayer (would desync).
                    if (!AllowHotSeatSwitch) break;
                    ControllingPlayer = (ControllingPlayer + 1) % _gameState.Players.Count;
                    _state.Clear();
                    GD.Print($"Switched to Player {ControllingPlayer}");
                    break;
                case Key.Q:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.BuilderNest);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case Key.W:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.SoldierNest);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case Key.E:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.StunnerNest);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case Key.R:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.Supply);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case Key.T:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.SoldierTower);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case Key.Y:
                    if (_state.SelectedBlocks.Count > 0)
                    {
                        _blueprint.Toggle(BlueprintMode.BlueprintType.StunTower);
                        _attackMoveMode = false;
                        GD.Print(_blueprint.IsActive
                            ? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
                            : "Blueprint deactivated");
                    }
                    break;
                case >= Key.Key0 and <= Key.Key9:
                    HandleControlGroupKey((int)(key.Keycode - Key.Key0), key.CtrlPressed);
                    break;
            }
        }
    }

    private void HandleClickSelect(Vector2 worldPos, bool additive)
    {
        var gridPos = GridRenderer.WorldToGrid(worldPos);

        if (!_gameState!.Grid.InBounds(gridPos))
        {
            if (!additive) _state.Clear();
            return;
        }

        var block = _gameState.GetBlockAt(gridPos);

        if (block != null && block.PlayerId == ControllingPlayer && block.Type != BlockType.Wall)
        {
            _state.Select(block, additive);
        }
        else
        {
            if (!additive) _state.Clear();
        }
    }

    private void HandleBoxSelect(Vector2 start, Vector2 end, bool additive)
    {
        var minX = (int)Mathf.Floor((Mathf.Min(start.X, end.X) - GridRenderer.GridPadding) / GridRenderer.CellSize);
        var maxX = (int)Mathf.Floor((Mathf.Max(start.X, end.X) - GridRenderer.GridPadding) / GridRenderer.CellSize);
        var minY = (int)Mathf.Floor((Mathf.Min(start.Y, end.Y) - GridRenderer.GridPadding) / GridRenderer.CellSize);
        var maxY = (int)Mathf.Floor((Mathf.Max(start.Y, end.Y) - GridRenderer.GridPadding) / GridRenderer.CellSize);

        var allInBox = new List<Block>();
        var mobileInBox = new List<Block>();

        foreach (var block in _gameState!.Blocks)
        {
            if (block.PlayerId != ControllingPlayer) continue;
            if (block.Type == BlockType.Wall) continue;
            if (block.Pos.X < minX || block.Pos.X > maxX ||
                block.Pos.Y < minY || block.Pos.Y > maxY) continue;

            allInBox.Add(block);
            // Mobile priority: not rooting, not in formation, actually mobile
            if (block.IsMobile && !block.IsInFormation)
                mobileInBox.Add(block);
        }

        // If movable blocks exist in box, prefer those; otherwise take all
        var toSelect = mobileInBox.Count > 0 ? mobileInBox : allInBox;
        _state.SelectAll(toSelect, additive);

        if (toSelect.Count > 0)
            GD.Print($"Box selected {toSelect.Count} blocks{(mobileInBox.Count > 0 && allInBox.Count > mobileInBox.Count ? " (mobile priority)" : "")}");
    }

    /// <summary>Double-click: select all blocks of same type AND rooting state, visible on screen.</summary>
    private void HandleDoubleClickSelect(Vector2 worldPos)
    {
        var gridPos = GridRenderer.WorldToGrid(worldPos);
        if (!_gameState!.Grid.InBounds(gridPos)) return;

        var block = _gameState.GetBlockAt(gridPos);
        if (block == null || block.PlayerId != ControllingPlayer || block.Type == BlockType.Wall)
            return;

        var targetType = block.Type;
        bool targetMobile = block.IsMobile;

        // Camera-limited: same type AND same rooting state, only visible blocks
        var visibleSameType = _gameState.Blocks.Where(b => 
            b.PlayerId == ControllingPlayer && 
            b.Type == targetType && 
            b.IsMobile == targetMobile && 
            IsBlockVisible(b));
        
        _state.SelectAll(visibleSameType, false);

        GD.Print($"Double-click selected {_state.SelectedBlocks.Count} visible {(targetMobile ? "mobile" : "rooted")} {targetType}s");
    }

    /// <summary>Ctrl+click: select all same type on screen (replaces current selection, same as double-click).</summary>
    private void HandleCtrlClickSelect(Vector2 worldPos)
    {
        HandleDoubleClickSelect(worldPos);
    }

    /// <summary>Control groups: Ctrl+N assign, N select, double-tap N center camera.</summary>
    private void HandleControlGroupKey(int groupIndex, bool ctrlHeld)
    {
        if (ctrlHeld)
        {
            _state.AssignGroup(groupIndex);
            return;
        }

        // Check for double-tap → center camera
        var now = Time.GetTicksMsec();
        if (_lastGroupTapKey == groupIndex && (now - _lastGroupTapTime) < DoubleTapMs)
        {
            CenterCameraOnGroup(groupIndex);
            _lastGroupTapKey = -1;
            return;
        }
        _lastGroupTapKey = groupIndex;
        _lastGroupTapTime = now;

        _state.SelectGroup(groupIndex, _gameState!, ControllingPlayer);
    }

    private void CenterCameraOnGroup(int groupIndex)
    {
        if (!_state.ControlGroups.TryGetValue(groupIndex, out var ids) || ids.Count == 0) return;

        var positions = ids
            .Select(id => _gameState!.Blocks.FirstOrDefault(b => b.Id == id))
            .Where(b => b != null)
            .Select(b => GridRenderer.GridToWorld(b!.Pos))
            .ToList();

        if (positions.Count == 0) return;

        var center = positions.Aggregate(Vector2.Zero, (sum, p) => sum + p) / positions.Count;
        var camera = GetViewport().GetCamera2D();
        if (camera != null)
            camera.GlobalPosition = center;
    }
}
