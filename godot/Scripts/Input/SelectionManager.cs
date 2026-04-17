using Blocker.Game.Config;
using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Input;

/// <summary>
/// Orchestrates block selection, move commands, and ability keys.
/// Delegating state to SelectionState and specific logic to partial classes.
/// </summary>
public partial class SelectionManager : Node2D
{
	public interface ICommandSink { void Submit(Command cmd); }

	[Export] public int ControllingPlayer = 0;

	// Hot-seat switch is for single-player dev only.
	public bool AllowHotSeatSwitch = true;

	private ICommandSink? _commandSink;
	public void SetCommandSink(ICommandSink? sink) => _commandSink = sink;

	private GameState? _gameState;
	private GameConfig? _config;
	private GridPos? _hoveredCell;

	// Specialized components
	private readonly SelectionState _state = new();
	private readonly BlueprintMode _blueprint = new();

	// Temporary command list for single-player (consumed by TickRunner)
	private readonly List<Command> _pendingCommands = [];

	// Input tracking state
	private bool _isDragging;
	private Vector2 _dragStart;
	private Vector2 _dragEnd;

	private ulong _lastClickTime;
	private GridPos? _lastClickCell;
	private const ulong DoubleClickMs = 400;

	private ulong _lastGroupTapTime;
	private int _lastGroupTapKey = -1;
	private const ulong DoubleTapMs = 350;

	private bool _attackMoveMode;
	private bool _jumpMode;

	private bool _isRightDragging;
	private Vector2 _rightDragStart;
	private readonly List<GridPos> _paintedCells = [];
	private const float PaintDragThreshold = 0.4f * GridRenderer.CellSize;

	// Visual constants
	private static readonly Color HoverColor = new(1f, 1f, 1f, 0.08f);
	private static readonly Color HoverBorderColor = new(1f, 1f, 1f, 0.25f);
	private static readonly Color MoveTargetColor = new(0.3f, 0.9f, 0.3f, 0.6f);
	private static readonly Color DragRectColor = new(1f, 1f, 1f, 0.15f);
	private static readonly Color DragRectBorderColor = new(1f, 1f, 1f, 0.4f);
	private static readonly Color BlueprintTargetColor = new(0.9f, 0.6f, 0.1f, 0.5f);
	private static readonly Color AttackMoveColor = new(1f, 0.3f, 0.3f, 0.5f);
	private static readonly Color QueuePathColor = new(1f, 1f, 1f, 0.3f);
	private static readonly Color PaintCellColor = new(0.3f, 0.9f, 0.3f, 0.3f);

	// Public accessors
	public IReadOnlyList<Block> SelectedBlocks => _state.SelectedBlocks;
	public IReadOnlyDictionary<int, IReadOnlyList<int>> ControlGroups => _state.ControlGroups;

	public void SelectBlockById(int blockId)
	{
		if (_gameState == null) return;
		var block = _gameState.Blocks.FirstOrDefault(b => b.Id == blockId);
		if (block != null) _state.SelectOnly(block);
	}

	public void DeselectBlockById(int blockId) => _state.Deselect(blockId);

	public void ToggleBlueprintMode(BlueprintMode.BlueprintType blueprintType) =>
		_blueprint.Toggle(blueprintType);
	public List<Command> FlushCommands()
	{
		var cmds = new List<Command>(_pendingCommands);
		_pendingCommands.Clear();
		return cmds;
	}

	public void SubmitSurrender() =>
		EmitCommand(new Command(ControllingPlayer, CommandType.Surrender, new List<int>()));

	public void SubmitToggleSpawn(BlockType unitType) =>
		EmitCommand(new Command(ControllingPlayer, CommandType.ToggleSpawn, new List<int>(), UnitType: unitType));

	public void SetGameState(GameState state) => _gameState = state;
	public void SetConfig(GameConfig config) => _config = config;

	public override void _Process(double delta)
	{
		if (_gameState == null) return;

		var mouseWorld = GetGlobalMousePosition();
		var gridPos = GridRenderer.WorldToGrid(mouseWorld);
		var newHover = _gameState.Grid.InBounds(gridPos) ? gridPos : (GridPos?)null;

		if (newHover != _hoveredCell) _hoveredCell = newHover;
		if (_isDragging) _dragEnd = mouseWorld;

		// Right-drag paint: add cells as mouse moves
		if (_isRightDragging && _gameState.Grid.InBounds(gridPos))
		{
			if (!_paintedCells.Contains(gridPos)) _paintedCells.Add(gridPos);
		}

		// Auto-rotate blueprint based on hover position
		if (_blueprint.IsActive && _hoveredCell.HasValue)
		{
			_blueprint.AutoRotate(_hoveredCell.Value, _gameState.Grid.Width, _gameState.Grid.Height);
		}

		// Clean up dead/removed blocks from selection
		_state.RemoveDeadBlocks(_gameState);

		QueueRedraw();
	}

	private bool IsBlockVisible(Block block)
	{
		var camera = GetViewport().GetCamera2D();
		if (camera == null) return true;

		var viewportSize = GetViewportRect().Size;
		var halfView = viewportSize / (2f * camera.Zoom);
		var camPos = camera.GlobalPosition;

		float minX = ((camPos.X - halfView.X) - GridRenderer.GridPadding);
		float maxX = ((camPos.X + halfView.X) - GridRenderer.GridPadding);
		float minY = ((camPos.Y - halfView.Y) - GridRenderer.GridPadding);
		float maxY = ((camPos.Y + halfView.Y) - GridRenderer.GridPadding);

		var worldPos = block.Pos.X * GridRenderer.CellSize;
		var worldPosY = block.Pos.Y * GridRenderer.CellSize;

		return worldPos >= minX - GridRenderer.CellSize && worldPos <= maxX + GridRenderer.CellSize
			&& worldPosY >= minY - GridRenderer.CellSize && worldPosY <= maxY + GridRenderer.CellSize;
	}
}
