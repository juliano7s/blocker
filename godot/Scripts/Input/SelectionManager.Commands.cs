using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

public partial class SelectionManager
{
    private void EmitCommand(Command cmd)
    {
        if (_commandSink != null) _commandSink.Submit(cmd);
        else _pendingCommands.Add(cmd);
    }

    /// <summary>Issue a command by its UI action (from CommandCard). Mirrors hotkey logic.</summary>
    public void IssueCommand(CommandAction action, bool queue = false)
    {
        switch (action)
        {
            case CommandAction.Root:
                IssueCommandToSelected(CommandType.Root, queue);
                break;
            case CommandAction.Uproot:
                IssueCommandToSelected(CommandType.Root, queue);
                break;
            case CommandAction.Wall:
                IssueCommandToSelected(CommandType.ConvertToWall, queue);
                break;
            case CommandAction.Push:
                IssueDirectionalCommand(CommandType.TogglePush, queue);
                break;
            case CommandAction.Explode:
                IssueCommandToSelected(CommandType.SelfDestruct, queue);
                break;
            case CommandAction.Stun:
                IssueDirectionalCommand(CommandType.FireStunRay, queue);
                break;
            case CommandAction.Jump:
                if (_state.SelectedBlocks.Any(b => b.Type == BlockType.Jumper))
                {
                    var nonJumpers = _state.SelectedBlocks.Where(b => b.Type != BlockType.Jumper).ToList();
                    if (nonJumpers.Count > 0)
                    {
                        var ids = nonJumpers.Select(b => b.Id).ToList();
                        EmitCommand(new Command(ControllingPlayer, CommandType.Root, ids, Queue: queue));
                    }
                    _jumpMode = true;
                    _blueprint.Deactivate();
                    _attackMoveMode = false;
                    GD.Print("Jump: click target direction");
                }
                break;
            case CommandAction.Magnet:
                IssueCommandToSelected(CommandType.MagnetPull, queue);
                break;
            case CommandAction.Tower:
                IssueCommandToSelected(CommandType.CreateTower, queue);
                break;
            case CommandAction.RefineNuggets:
                IssueCommandToSelected(CommandType.ToggleRefine, queue);
                break;
            }
    }

    private void HandleRightClick(Vector2 worldPos, bool queue = false)
    {
        if (_state.SelectedBlocks.Count == 0) return;

        var gridPos = GridRenderer.WorldToGrid(worldPos);
        if (!_gameState!.Grid.InBounds(gridPos)) return;

        var targetBlock = _gameState.GetBlockAt(gridPos);

        // Context-aware: builders selected + right-click unmined nugget → MineNugget
        if (targetBlock is { Type: BlockType.Nugget, NuggetState.IsMined: false })
        {
            var builderIds = _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Builder)
                .Select(b => b.Id)
                .ToList();
            if (builderIds.Count > 0)
            {
                EmitCommand(new Command(ControllingPlayer, CommandType.MineNugget, builderIds, gridPos, Queue: queue));
                GD.Print($"{(queue ? "Queued" : "Issued")} mine nugget at {gridPos} with {builderIds.Count} builders");
            }

            // Non-builder selected blocks get a normal move to that area
            var otherIds = _state.SelectedBlocks
                .Where(b => b.Type != BlockType.Builder)
                .Select(b => b.Id)
                .ToList();
            if (otherIds.Count > 0)
                EmitCommand(new Command(ControllingPlayer, CommandType.Move, otherIds, gridPos, Queue: queue));
            return;
        }

        // Context-aware: mined nugget(s) selected + right-click damaged friendly soldier/jumper → HealWithNugget
        if (targetBlock != null
            && targetBlock.Type is BlockType.Soldier or BlockType.Jumper
            && targetBlock.PlayerId == ControllingPlayer
            && targetBlock.Hp < (targetBlock.Type == BlockType.Soldier ? Simulation.Core.Constants.SoldierMaxHp : Simulation.Core.Constants.JumperMaxHp))
        {
            var nuggetIds = _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Nugget && b.NuggetState is { IsMined: true })
                .Select(b => b.Id)
                .ToList();
            if (nuggetIds.Count > 0)
            {
                EmitCommand(new Command(ControllingPlayer, CommandType.HealWithNugget, nuggetIds, gridPos, Queue: queue));
                GD.Print($"{(queue ? "Queued" : "Issued")} heal with {nuggetIds.Count} nuggets on {targetBlock.Type} at {gridPos}");

                // Non-nugget selected blocks get a normal move
                var otherIds = _state.SelectedBlocks
                    .Where(b => b.Type != BlockType.Nugget)
                    .Select(b => b.Id)
                    .ToList();
                if (otherIds.Count > 0)
                    EmitCommand(new Command(ControllingPlayer, CommandType.Move, otherIds, gridPos, Queue: queue));
                return;
            }
        }

        // Context-aware: mined nugget(s) selected + right-click friendly wall → FortifyWithNugget
        if (targetBlock is { Type: BlockType.Wall } && targetBlock.PlayerId == ControllingPlayer)
        {
            var nuggetIds = _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Nugget && b.NuggetState is { IsMined: true })
                .Select(b => b.Id)
                .ToList();
            if (nuggetIds.Count > 0)
            {
                EmitCommand(new Command(ControllingPlayer, CommandType.FortifyWithNugget, nuggetIds, gridPos, Queue: queue));
                GD.Print($"{(queue ? "Queued" : "Issued")} fortify with {nuggetIds.Count} nuggets on wall at {gridPos}");

                var otherIds = _state.SelectedBlocks
                    .Where(b => b.Type != BlockType.Nugget)
                    .Select(b => b.Id)
                    .ToList();
                if (otherIds.Count > 0)
                    EmitCommand(new Command(ControllingPlayer, CommandType.Move, otherIds, gridPos, Queue: queue));
                return;
            }
        }

        // Default: normal move
        var blockIds = _state.SelectedBlocks
            .Select(b => b.Id)
            .ToList();

        if (blockIds.Count > 0)
        {
            EmitCommand(new Command(ControllingPlayer, CommandType.Move, blockIds, gridPos, Queue: queue));
            GD.Print($"{(queue ? "Queued" : "Issued")} move for {blockIds.Count} blocks to {gridPos}");
        }
    }

    private void HandleBlueprintClick(Vector2 worldPos, bool shiftHeld = false)
    {
        var gridPos = GridRenderer.WorldToGrid(worldPos);
        if (!_gameState!.Grid.InBounds(gridPos)) return;

        var cells = _blueprint.GetCells();
        if (cells.Count == 0) return;

        // Build target list: (world position, role)
        var targets = new List<(GridPos pos, string role)>();
        foreach (var cell in cells)
        {
            var targetPos = new GridPos(gridPos.X + cell.Offset.X, gridPos.Y + cell.Offset.Y);
            if (!_gameState.Grid.InBounds(targetPos)) return; // Blueprint doesn't fit
            // Skip cells already occupied by friendly blocks of the right type
            var existing = _gameState.GetBlockAt(targetPos);
            if (existing != null && existing.PlayerId == ControllingPlayer)
                continue; // Gap-filling: skip occupied cells
            targets.Add((targetPos, cell.Role));
        }

        if (targets.Count == 0)
        {
            GD.Print("Blueprint: all cells already occupied");
            return;
        }

        // Match selected blocks to blueprint roles, closest-first
        var available = _state.SelectedBlocks.Where(b => b.IsMobile).ToList();
        var assigned = new List<(Block block, GridPos target, string role)>();
        var usedBlocks = new HashSet<int>();
        var usedTargets = new HashSet<GridPos>();

        // Build all possible assignments
        var pairs = new List<(Block block, GridPos target, string role, int dist)>();
        foreach (var block in available)
        {
            foreach (var (targetPos, role) in targets)
            {
                // Check role compatibility
                bool compatible = role switch
                {
                    "builder" => block.Type == BlockType.Builder,
                    "soldier" => block.Type == BlockType.Soldier,
                    "stunner" => block.Type == BlockType.Stunner,
                    "wall" => block.Type == BlockType.Builder, // Builders become walls
                    _ => false
                };
                if (!compatible) continue;
                pairs.Add((block, targetPos, role, block.Pos.ManhattanDistance(targetPos)));
            }
        }

        pairs.Sort((a, b) => a.dist.CompareTo(b.dist));

        foreach (var (block, target, role, _) in pairs)
        {
            if (usedBlocks.Contains(block.Id)) continue;
            if (usedTargets.Contains(target)) continue;
            assigned.Add((block, target, role));
            usedBlocks.Add(block.Id);
            usedTargets.Add(target);
        }

        if (assigned.Count == 0)
        {
            GD.Print("Blueprint: no compatible units for this formation");
            return;
        }

        // Issue commands for each assignment
        foreach (var (block, target, role) in assigned)
        {
            EmitCommand(new Command(ControllingPlayer, CommandType.Move, [block.Id], target));

            if (role == "wall")
            {
                // move → root → convert to wall
                EmitCommand(new Command(ControllingPlayer, CommandType.Root, [block.Id], Queue: true));
                EmitCommand(new Command(ControllingPlayer, CommandType.ConvertToWall, [block.Id], Queue: true));
            }
            else
            {
                // move → root (for builders becoming part of nests, soldiers/stunners for nests/towers)
                EmitCommand(new Command(ControllingPlayer, CommandType.Root, [block.Id], Queue: true));

                if ((_blueprint.ActiveType == BlueprintMode.BlueprintType.SoldierTower && role == "soldier") ||
                    (_blueprint.ActiveType == BlueprintMode.BlueprintType.StunTower && role == "stunner"))
                {
                    EmitCommand(new Command(ControllingPlayer, CommandType.CreateTower, [block.Id], Queue: true));
                }
            }
        }

        // Remove dispatched units from selection
        foreach (var (block, _, _) in assigned)
            _state.Deselect(block.Id);

        // Place ghost
        float now = (float)Time.GetTicksMsec() / 1000f;
        _blueprint.PlacedGhosts.Add(new BlueprintMode.PlacedGhost(
            _blueprint.ActiveType, gridPos, _blueprint.Rotation, now));

        GD.Print($"Blueprint: dispatched {assigned.Count} units for {_blueprint.ActiveType}");

        // Shift+click keeps blueprint active; otherwise always deactivate
        if (!shiftHeld)
            _blueprint.Deactivate();
    }

    private void HandlePaintRelease(bool queue)
    {
        if (_state.SelectedBlocks.Count == 0 || _paintedCells.Count == 0) return;

        var mobileBlocks = _state.SelectedBlocks.Where(b => b.IsMobile).ToList();
        if (mobileBlocks.Count == 0) return;

        // Build assignment pairs: (block, target) sorted by Manhattan distance
        var pairs = new List<(Block block, GridPos target, int dist)>();
        foreach (var block in mobileBlocks)
        {
            foreach (var cell in _paintedCells)
            {
                int dist = block.Pos.ManhattanDistance(cell);
                pairs.Add((block, cell, dist));
            }
        }

        pairs.Sort((a, b) => a.dist.CompareTo(b.dist));

        // Greedy assignment: closest pair first, each block and cell used once
        var assignedBlocks = new HashSet<int>();
        var assignedCells = new HashSet<GridPos>();

        foreach (var (block, target, _) in pairs)
        {
            if (assignedBlocks.Contains(block.Id)) continue;
            if (assignedCells.Contains(target)) continue;

            EmitCommand(new Command(
                ControllingPlayer, CommandType.Move, [block.Id], target, Queue: queue));
            assignedBlocks.Add(block.Id);
            assignedCells.Add(target);

            if (assignedBlocks.Count >= mobileBlocks.Count) break;
            if (assignedCells.Count >= _paintedCells.Count) break;
        }

        // If more blocks than cells, remaining blocks go to last painted cell
        if (assignedBlocks.Count < mobileBlocks.Count && _paintedCells.Count > 0)
        {
            var lastCell = _paintedCells[^1];
            foreach (var block in mobileBlocks)
            {
                if (assignedBlocks.Contains(block.Id)) continue;
                EmitCommand(new Command(
                    ControllingPlayer, CommandType.Move, [block.Id], lastCell, Queue: queue));
            }
        }

        GD.Print($"Paint-moved {assignedBlocks.Count} blocks to {assignedCells.Count} cells");
    }

    private void HandleJumpClick(Vector2 worldPos, bool queue)
    {
        if (_state.SelectedBlocks.Count == 0) return;
        IssueDirectionalCommand(CommandType.Jump, queue, worldPos);
    }

    private void HandleAttackMoveClick(Vector2 worldPos, bool queue)
    {
        if (_state.SelectedBlocks.Count == 0) return;

        var gridPos = GridRenderer.WorldToGrid(worldPos);
        if (!_gameState!.Grid.InBounds(gridPos)) return;

        var blockIds = _state.SelectedBlocks
            .Where(b => b.Type == BlockType.Soldier || b.Type == BlockType.Jumper)
            .Select(b => b.Id)
            .ToList();

        if (blockIds.Count > 0)
        {
            EmitCommand(new Command(ControllingPlayer, CommandType.AttackMove, blockIds, gridPos, Queue: queue));
            GD.Print($"Attack-move {blockIds.Count} blocks to {gridPos}");
        }
    }

    private List<Block> GetRelevantBlocks(CommandType type)
    {
        return type switch
        {
            CommandType.Root => _state.SelectedBlocks
                .Where(b => b.Type is BlockType.Builder or BlockType.Soldier or BlockType.Stunner or BlockType.Warden)
                .ToList(),
            CommandType.ConvertToWall => _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Builder).ToList(),
            CommandType.FireStunRay => _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Stunner).ToList(),
            CommandType.SelfDestruct => _state.SelectedBlocks
                .Where(b => b.Type is BlockType.Soldier or BlockType.Stunner).ToList(),
            CommandType.MagnetPull => _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Warden).ToList(),
            CommandType.CreateTower => _state.SelectedBlocks
                .Where(b => b.Type is BlockType.Soldier or BlockType.Stunner).ToList(),
            CommandType.TogglePush => _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Builder).ToList(),
            CommandType.ToggleRefine => _state.SelectedBlocks
                .Where(b => b.IsInFormation).ToList(),
            CommandType.Jump => _state.SelectedBlocks
                .Where(b => b.Type == BlockType.Jumper).ToList(),
            _ => _state.SelectedBlocks.ToList()
        };
    }

    private void IssueCommandToSelected(CommandType type, bool queue = false)
    {
        if (_state.SelectedBlocks.Count == 0) return;

        var relevant = GetRelevantBlocks(type);
        if (relevant.Count == 0) return;

        var blockIds = relevant.Select(b => b.Id).ToList();
        EmitCommand(new Command(ControllingPlayer, type, blockIds, Queue: queue));
        GD.Print($"{(queue ? "Queued" : "Issued")} {type} for {blockIds.Count} blocks");
    }

    private void IssueDirectionalCommand(CommandType type, bool queue = false, Vector2? targetWorld = null)
    {
        if (_state.SelectedBlocks.Count == 0) return;

        var mouseWorld = targetWorld ?? GetGlobalMousePosition();
        var relevant = GetRelevantBlocks(type);
        if (relevant.Count == 0) return;

        // Group blocks by their snapped direction for fewer commands
        var byDirection = new Dictionary<Blocker.Simulation.Core.Direction, List<int>>();
        foreach (var block in relevant)
        {
            var blockWorld = GridRenderer.GridToWorld(block.Pos);
            var delta = mouseWorld - blockWorld;
            Blocker.Simulation.Core.Direction dir;
            if (Mathf.Abs(delta.X) >= Mathf.Abs(delta.Y))
                dir = delta.X >= 0 ? Blocker.Simulation.Core.Direction.Right : Blocker.Simulation.Core.Direction.Left;
            else
                dir = delta.Y >= 0 ? Blocker.Simulation.Core.Direction.Down : Blocker.Simulation.Core.Direction.Up;

            if (!byDirection.ContainsKey(dir))
                byDirection[dir] = [];
            byDirection[dir].Add(block.Id);
        }

        foreach (var (dir, blockIds) in byDirection)
            EmitCommand(new Command(ControllingPlayer, type, blockIds,
                TargetPos: GridRenderer.WorldToGrid(mouseWorld), Direction: dir, Queue: queue));

        GD.Print($"{(queue ? "Queued" : "Issued")} {type} for {relevant.Count} blocks ({byDirection.Count} directions)");
    }
}
