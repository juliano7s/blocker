using Blocker.Game.Config;
using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

/// <summary>
/// Handles block selection (left-click, box select), move commands (right-click),
/// and ability keys (F=root, W=wall). Queues Commands for the TickRunner.
/// </summary>
public partial class SelectionManager : Node2D
{
	public interface ICommandSink { void Submit(Command cmd); }

	[Export] public int ControllingPlayer = 0;

	// Hot-seat Tab toggle is a single-player development convenience. In a
	// networked game it's catastrophic: cycling ControllingPlayer locally makes
	// us emit Commands tagged with the wrong PlayerId, which the relay still
	// broadcasts under our real connection ID — the peers immediately desync.
	// GameManager flips this off when launching a multiplayer session.
	public bool AllowHotSeatSwitch = true;

	private ICommandSink? _commandSink;
	public void SetCommandSink(ICommandSink? sink) => _commandSink = sink;

	private GameState? _gameState;
	private GameConfig? _config;
	private GridPos? _hoveredCell;
	private readonly List<Block> _selectedBlocks = [];
	private readonly List<Command> _pendingCommands = [];

	// Routes a command either to the configured sink (multiplayer: pushes
	// straight to LockstepCoordinator.QueueLocalCommand) or to the local
	// pending list that the single-player TickRunner pulls each tick.
	private void EmitCommand(Command cmd)
	{
		if (_commandSink != null) _commandSink.Submit(cmd);
		else _pendingCommands.Add(cmd);
	}

	// Drag select state
	private bool _isDragging;
	private Vector2 _dragStart;
	private Vector2 _dragEnd;

	// Double-click detection
	private ulong _lastClickTime;
	private GridPos? _lastClickCell;
	private const ulong DoubleClickMs = 400;

	// Control groups (keys 0-9)
	private readonly Dictionary<int, List<int>> _controlGroups = new();
	private ulong _lastGroupTapTime;
	private int _lastGroupTapKey = -1;
	private const ulong DoubleTapMs = 350;

	// Blueprint system
	private readonly BlueprintMode _blueprint = new();
	private bool _blueprintMode => _blueprint.IsActive;

	// Attack-move mode (A key)
	private bool _attackMoveMode;

	// Jump targeting mode (F key with Jumpers selected)
	private bool _jumpMode;

	// Right-drag paint mode
	private bool _isRightDragging;
	private Vector2 _rightDragStart;
	private readonly List<GridPos> _paintedCells = [];
	private const float PaintDragThreshold = 0.4f * GridRenderer.CellSize;

	private static readonly Color HoverColor = new(1f, 1f, 1f, 0.08f);
	private static readonly Color HoverBorderColor = new(1f, 1f, 1f, 0.25f);
private static readonly Color MoveTargetColor = new(0.3f, 0.9f, 0.3f, 0.6f);
	private static readonly Color DragRectColor = new(1f, 1f, 1f, 0.15f);
	private static readonly Color DragRectBorderColor = new(1f, 1f, 1f, 0.4f);
	private static readonly Color BlueprintTargetColor = new(0.9f, 0.6f, 0.1f, 0.5f);
	private static readonly Color AttackMoveColor = new(1f, 0.3f, 0.3f, 0.5f);
	private static readonly Color QueuePathColor = new(1f, 1f, 1f, 0.3f);
	private static readonly Color PaintCellColor = new(0.3f, 0.9f, 0.3f, 0.3f);

	public IReadOnlyList<Block> SelectedBlocks => _selectedBlocks;

	/// <summary>Control groups (1-9) mapped to block IDs.</summary>
	public IReadOnlyDictionary<int, IReadOnlyList<int>> ControlGroups =>
		_controlGroups.ToDictionary(
			kvp => kvp.Key,
			kvp => (IReadOnlyList<int>)kvp.Value.AsReadOnly());

	/// <summary>Issue a command by its UI key (from CommandCard). Mirrors hotkey logic.</summary>
	public void IssueCommandByKey(string commandKey, bool queue = false)
	{
		switch (commandKey)
		{
			case "root":
				IssueCommandToSelected(CommandType.Root, queue);
				break;
			case "uproot":
				IssueCommandToSelected(CommandType.Root, queue);
				break;
			case "wall":
				IssueCommandToSelected(CommandType.ConvertToWall, queue);
				break;
			case "push":
				IssueDirectionalCommand(CommandType.TogglePush, queue);
				break;
			case "explode":
				IssueCommandToSelected(CommandType.SelfDestruct, queue);
				break;
			case "stun":
				IssueDirectionalCommand(CommandType.FireStunRay, queue);
				break;
			case "jump":
				if (_selectedBlocks.Any(b => b.Type == BlockType.Jumper))
				{
					var nonJumpers = _selectedBlocks.Where(b => b.Type != BlockType.Jumper).ToList();
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
			case "magnet":
				IssueCommandToSelected(CommandType.MagnetPull, queue);
				break;
			}
	}

	/// <summary>Select only the specified block (from HUD click).</summary>
	public void SelectBlockById(int blockId)
	{
		if (_gameState == null) return;
		var block = _gameState.Blocks.FirstOrDefault(b => b.Id == blockId);
		if (block != null)
		{
			_selectedBlocks.Clear();
			_selectedBlocks.Add(block);
		}
	}

	/// <summary>Remove the specified block from selection (from HUD shift-click).</summary>
	public void DeselectBlockById(int blockId)
	{
		_selectedBlocks.RemoveAll(b => b.Id == blockId);
	}

	/// <summary>Toggle blueprint mode by type (from HUD click).</summary>
	public void ToggleBlueprintMode(int blueprintType)
	{
		_blueprint.Toggle((BlueprintMode.BlueprintType)blueprintType);
	}

	/// <summary>Flush pending commands for the tick runner to consume.</summary>
	public List<Command> FlushCommands()
	{
		var cmds = new List<Command>(_pendingCommands);
		_pendingCommands.Clear();
		return cmds;
	}

	/// <summary>
	/// Submit a Surrender command for the controlling player. Routes through the
	/// same EmitCommand path as everything else, so it lockstep-syncs in MP and
	/// goes into the local pending list in SP.
	/// </summary>
	public void SubmitSurrender()
	{
		EmitCommand(new Command(ControllingPlayer, CommandType.Surrender, new List<int>()));
	}

	public void SetGameState(GameState state)
	{
		_gameState = state;
	}

	public void SetConfig(GameConfig config) => _config = config;

	public override void _Process(double delta)
	{
		if (_gameState == null) return;

		var mouseWorld = GetGlobalMousePosition();
		var gridPos = GridRenderer.WorldToGrid(mouseWorld);
		var newHover = _gameState.Grid.InBounds(gridPos) ? gridPos : (GridPos?)null;

		if (newHover != _hoveredCell)
		{
			_hoveredCell = newHover;
		}

		if (_isDragging)
		{
			_dragEnd = mouseWorld;
		}

		// Right-drag paint: add cells as mouse moves
		if (_isRightDragging && _gameState.Grid.InBounds(gridPos))
		{
			if (!_paintedCells.Contains(gridPos))
				_paintedCells.Add(gridPos);
		}

		// Auto-rotate blueprint based on hover position (only if user hasn't manually rotated)
		if (_blueprintMode && _hoveredCell.HasValue)
		{
			_blueprint.AutoRotate(_hoveredCell.Value, _gameState.Grid.Width, _gameState.Grid.Height);
		}

		// Clean up dead/removed blocks from selection
		_selectedBlocks.RemoveAll(b => !_gameState.Blocks.Contains(b));

		QueueRedraw();
	}

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
					if (_blueprintMode)
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
					if (_blueprintMode)
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
					if (_selectedBlocks.Any(b => b.Type == BlockType.Jumper))
					{
						// Root non-jumpers immediately, then enter jump targeting for jumpers
						var nonJumpers = _selectedBlocks.Where(b => b.Type != BlockType.Jumper).ToList();
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
					if (_selectedBlocks.Any(b => b.Type == BlockType.Warden))
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
					if (_selectedBlocks.Count > 0)
					{
						_attackMoveMode = true;
						_blueprint.Deactivate();
						GD.Print("Attack-move: click target");
					}
					break;
				case Key.B:
					if (_blueprintMode)
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
					if (_blueprintMode || _attackMoveMode || _jumpMode)
					{
						_blueprint.Deactivate();
						_attackMoveMode = false;
						_jumpMode = false;
						GD.Print("Mode cancelled");
					}
					else
						_selectedBlocks.Clear();
					break;
				case Key.Quoteleft: // Backtick (`)
					// Quick-select all uprooted Soldiers + Stunners
					_selectedBlocks.Clear();
					foreach (var b in _gameState.Blocks)
					{
						if (b.PlayerId != ControllingPlayer) continue;
						if (b.Type is not (BlockType.Soldier or BlockType.Stunner)) continue;
						if (!b.IsMobile) continue;
						if (b.IsInFormation) continue;
						_selectedBlocks.Add(b);
					}
					GD.Print($"Quick-selected {_selectedBlocks.Count} mobile combat units");
					break;
				case Key.Tab:
					// Hot-seat: switch player. Disabled in multiplayer (would desync).
					if (!AllowHotSeatSwitch) break;
					ControllingPlayer = (ControllingPlayer + 1) % _gameState.Players.Count;
					_selectedBlocks.Clear();
					GD.Print($"Switched to Player {ControllingPlayer}");
					break;
				case Key.Q:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.BuilderNest);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
							? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
							: "Blueprint deactivated");
					}
					break;
				case Key.W:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.SoldierNest);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
							? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
							: "Blueprint deactivated");
					}
					break;
				case Key.E:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.StunnerNest);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
							? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
							: "Blueprint deactivated");
					}
					break;
				case Key.R:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.Supply);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
							? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
							: "Blueprint deactivated");
					}
					break;
				case Key.T:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.SoldierTower);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
							? $"Blueprint: {_blueprint.ActiveType} (B=rotate, click=place, X=clear ghosts)"
							: "Blueprint deactivated");
					}
					break;
				case Key.Y:
					if (_selectedBlocks.Count > 0)
					{
						_blueprint.Toggle(BlueprintMode.BlueprintType.StunTower);
						_attackMoveMode = false;
						GD.Print(_blueprintMode
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
			if (!additive) _selectedBlocks.Clear();
			return;
		}

		var block = _gameState.GetBlockAt(gridPos);

		if (block != null && block.PlayerId == ControllingPlayer && block.Type != BlockType.Wall)
		{
			if (!additive) _selectedBlocks.Clear();

			if (_selectedBlocks.Contains(block))
				_selectedBlocks.Remove(block);
			else
				_selectedBlocks.Add(block);
		}
		else
		{
			if (!additive) _selectedBlocks.Clear();
		}
	}

	private void HandleBoxSelect(Vector2 start, Vector2 end, bool additive)
	{
		if (!additive) _selectedBlocks.Clear();

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
		foreach (var block in toSelect)
		{
			if (!_selectedBlocks.Contains(block))
				_selectedBlocks.Add(block);
		}

		if (_selectedBlocks.Count > 0)
			GD.Print($"Box selected {_selectedBlocks.Count} blocks{(mobileInBox.Count > 0 && allInBox.Count > mobileInBox.Count ? " (mobile priority)" : "")}");
	}

	private void HandleRightClick(Vector2 worldPos, bool queue = false)
	{
		if (_selectedBlocks.Count == 0) return;

		var gridPos = GridRenderer.WorldToGrid(worldPos);
		if (!_gameState!.Grid.InBounds(gridPos)) return;

		var blockIds = _selectedBlocks
			.Select(b => b.Id)
			.ToList();

		if (blockIds.Count > 0)
		{
			EmitCommand(new Command(ControllingPlayer, CommandType.Move, blockIds, gridPos, Queue: queue));
			GD.Print($"{(queue ? "Queued" : "Issued")} move for {blockIds.Count} blocks to {gridPos}");
		}
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
		_selectedBlocks.Clear();

		// Camera-limited: same type AND same rooting state, only visible blocks
		foreach (var b in _gameState.Blocks)
		{
			if (b.PlayerId != ControllingPlayer) continue;
			if (b.Type != targetType) continue;
			if (b.IsMobile != targetMobile) continue;
			if (!IsBlockVisible(b)) continue;
			_selectedBlocks.Add(b);
		}

		GD.Print($"Double-click selected {_selectedBlocks.Count} visible {(targetMobile ? "mobile" : "rooted")} {targetType}s");
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
			// Assign current selection to group
			if (_selectedBlocks.Count > 0)
			{
				_controlGroups[groupIndex] = _selectedBlocks.Select(b => b.Id).ToList();
				GD.Print($"Assigned {_selectedBlocks.Count} blocks to group {groupIndex}");
			}
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

		// Single tap → select group
		if (!_controlGroups.TryGetValue(groupIndex, out var ids)) return;

		_selectedBlocks.Clear();
		foreach (var id in ids)
		{
			var block = _gameState!.Blocks.FirstOrDefault(b => b.Id == id);
			if (block != null && block.PlayerId == ControllingPlayer)
				_selectedBlocks.Add(block);
		}

		// Clean up dead block IDs
		_controlGroups[groupIndex] = _selectedBlocks.Select(b => b.Id).ToList();
		GD.Print($"Selected group {groupIndex}: {_selectedBlocks.Count} blocks");
	}

	private void CenterCameraOnGroup(int groupIndex)
	{
		if (!_controlGroups.TryGetValue(groupIndex, out var ids) || ids.Count == 0) return;

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

	/// <summary>
	/// Blueprint placement: dispatch units to build the active blueprint at click position.
	/// Closest-first greedy assignment. Shift+click keeps blueprint active.
	/// </summary>
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
		var available = _selectedBlocks.Where(b => b.IsMobile).ToList();
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
			}
		}

		// Remove dispatched units from selection
		foreach (var (block, _, _) in assigned)
			_selectedBlocks.Remove(block);

		// Place ghost
		float now = (float)Time.GetTicksMsec() / 1000f;
		_blueprint.PlacedGhosts.Add(new BlueprintMode.PlacedGhost(
			_blueprint.ActiveType, gridPos, _blueprint.Rotation, now));

		GD.Print($"Blueprint: dispatched {assigned.Count} units for {_blueprint.ActiveType}");

		// Shift+click keeps blueprint active; otherwise always deactivate
		if (!shiftHeld)
			_blueprint.Deactivate();
	}

	/// <summary>
	/// Paint mode release: distribute selected units to painted cells using
	/// closest-first greedy assignment (minimize total Manhattan distance).
	/// </summary>
	private void HandlePaintRelease(bool queue)
	{
		if (_selectedBlocks.Count == 0 || _paintedCells.Count == 0) return;

		var mobileBlocks = _selectedBlocks.Where(b => b.IsMobile).ToList();
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
		if (_selectedBlocks.Count == 0) return;
		IssueDirectionalCommand(CommandType.Jump, queue, worldPos);
	}

	private void HandleAttackMoveClick(Vector2 worldPos, bool queue)
	{
		if (_selectedBlocks.Count == 0) return;

		var gridPos = GridRenderer.WorldToGrid(worldPos);
		if (!_gameState!.Grid.InBounds(gridPos)) return;

		var blockIds = _selectedBlocks
			.Where(b => b.Type == BlockType.Soldier || b.Type == BlockType.Jumper)
			.Select(b => b.Id)
			.ToList();

		if (blockIds.Count > 0)
		{
			EmitCommand(new Command(ControllingPlayer, CommandType.AttackMove, blockIds, gridPos, Queue: queue));
			GD.Print($"Attack-move {blockIds.Count} blocks to {gridPos}");
		}
	}

	/// <summary>Filter selected blocks to only those relevant for a command type.</summary>
	private List<Block> GetRelevantBlocks(CommandType type)
	{
		return type switch
		{
			CommandType.Root => _selectedBlocks
				.Where(b => b.Type is BlockType.Builder or BlockType.Soldier or BlockType.Stunner or BlockType.Warden)
				.ToList(),
			CommandType.ConvertToWall => _selectedBlocks
				.Where(b => b.Type == BlockType.Builder).ToList(),
			CommandType.FireStunRay => _selectedBlocks
				.Where(b => b.Type == BlockType.Stunner).ToList(),
			CommandType.SelfDestruct => _selectedBlocks
				.Where(b => b.Type is BlockType.Soldier or BlockType.Stunner).ToList(),
			CommandType.MagnetPull => _selectedBlocks
				.Where(b => b.Type == BlockType.Warden).ToList(),
			CommandType.CreateTower => _selectedBlocks
				.Where(b => b.Type is BlockType.Soldier or BlockType.Stunner).ToList(),
			CommandType.TogglePush => _selectedBlocks
				.Where(b => b.Type == BlockType.Builder).ToList(),
			CommandType.Jump => _selectedBlocks
				.Where(b => b.Type == BlockType.Jumper).ToList(),
			_ => _selectedBlocks.ToList()
		};
	}

	private void IssueCommandToSelected(CommandType type, bool queue = false)
	{
		if (_selectedBlocks.Count == 0) return;

		var relevant = GetRelevantBlocks(type);
		if (relevant.Count == 0) return;

		var blockIds = relevant.Select(b => b.Id).ToList();
		EmitCommand(new Command(ControllingPlayer, type, blockIds, Queue: queue));
		GD.Print($"{(queue ? "Queued" : "Issued")} {type} for {blockIds.Count} blocks");
	}

	/// <summary>Per-unit direction snapping: each block gets its own direction from its position to mouse.</summary>
	private void IssueDirectionalCommand(CommandType type, bool queue = false, Vector2? targetWorld = null)
	{
		if (_selectedBlocks.Count == 0) return;

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

	public override void _Draw()
	{
		if (_gameState == null) return;

		// Hover highlight
		if (_hoveredCell.HasValue && !_isDragging)
		{
			var pos = _hoveredCell.Value;
			if (_blueprintMode)
			{
				// Draw blueprint hover preview (all cells)
				var cells = _blueprint.GetCells();
				foreach (var cell in cells)
				{
					var cellPos = new GridPos(pos.X + cell.Offset.X, pos.Y + cell.Offset.Y);
					var rect = CellRect(cellPos);
					var roleColor = GetBlueprintRoleColor(cell.Role);
					DrawRect(rect, roleColor with { A = 0.4f });
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
			float fadeAlpha = Mathf.Clamp(1f - age / 15f, 0.1f, 0.3f);
			foreach (var cell in ghostCells)
			{
				var cellPos = new GridPos(ghost.Position.X + cell.Offset.X, ghost.Position.Y + cell.Offset.Y);
				var rect = CellRect(cellPos);
				var roleColor = GetBlueprintRoleColor(cell.Role);
				DrawRect(rect, roleColor with { A = fadeAlpha });
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
		foreach (var block in _selectedBlocks)
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
		foreach (var block in _selectedBlocks)
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

	/// <summary>Get the grid-coordinate bounds currently visible in the camera viewport.</summary>
	private (int minX, int minY, int maxX, int maxY) GetVisibleGridBounds()
	{
		var camera = GetViewport().GetCamera2D();
		if (camera == null)
			return (0, 0, _gameState!.Grid.Width - 1, _gameState!.Grid.Height - 1);

		var viewportSize = GetViewportRect().Size;
		var halfView = viewportSize / (2f * camera.Zoom);
		var camPos = camera.GlobalPosition;

		int minX = (int)Mathf.Floor(((camPos.X - halfView.X) - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int maxX = (int)Mathf.Floor(((camPos.X + halfView.X) - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int minY = (int)Mathf.Floor(((camPos.Y - halfView.Y) - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int maxY = (int)Mathf.Floor(((camPos.Y + halfView.Y) - GridRenderer.GridPadding) / GridRenderer.CellSize);

		return (minX, minY, maxX, maxY);
	}

	private bool IsBlockVisible(Block block)
	{
		var (minX, minY, maxX, maxY) = GetVisibleGridBounds();
		return block.Pos.X >= minX && block.Pos.X <= maxX
			&& block.Pos.Y >= minY && block.Pos.Y <= maxY;
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
