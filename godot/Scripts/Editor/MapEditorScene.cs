using Blocker.Game.Config;
using Blocker.Game.Maps;
using Blocker.Game.Rendering;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.Editor;

public enum EditorTool
{
	GroundPaint,
	TerrainPaint,
	UnitPlace,
	Eraser
}

/// <summary>
/// Main map editor controller. Creates a GameState for editing (no simulation ticking),
/// reuses GridRenderer for WYSIWYG rendering, handles tool application, undo/redo,
/// symmetry mirroring, camera, and map save/load.
/// </summary>
public partial class MapEditorScene : Node2D
{
	private GameConfig _config = GameConfig.CreateDefault();
	private GameState _editorState = null!;
	private GridRenderer _gridRenderer = null!;
	private Camera2D _camera = null!;
	private EditorToolbar _toolbar = null!;
	private CanvasLayer _uiLayer = null!;

	private readonly EditorActionStack _actionStack = new();
	private bool _mirrorTeams = true;

	// Current tool state
	private EditorTool _currentTool = EditorTool.GroundPaint;
	private EditorMode _currentMode = EditorMode.Paint;
	private GroundType _currentGround = GroundType.Normal;
	private TerrainType _currentTerrain = TerrainType.Terrain;
	private BlockType _currentBlock = BlockType.Builder;
	private bool _currentBlockRooted = false;
	private int _currentSlot = 0;

	// Map metadata
	private string _mapName = "Untitled";
	private int _slotCount = 2;
	private int _mapWidth = 41;
	private int _mapHeight = 25;

	// Drag state
	private bool _isDragging;
	private EditorAction? _dragAction;
	private readonly HashSet<(int, int)> _dragVisited = new();

	// Line tool state
	private bool _lineActive;
	private GridPos _lineStart;
	private readonly List<Vector2I> _linePreviewPoints = [];
	private LinePreviewOverlay _lineOverlay = null!;

	// Selection tool state
	private SelectPhase _selPhase = SelectPhase.Idle;
	private Rect2I _selRect;
	private GridPos _selDrawStart;
	private GridPos _selMoveStart;
	private Vector2I _selOffset;
	private GroundType[,]? _selGround;
	private TerrainType[,]? _selTerrain;
	private (BlockType type, int slot, bool rooted)[,]? _selBlocks;
	private SelectionOverlay _selOverlay = null!;

	// Middle-mouse pan state
	private bool _isPanning;
	private Vector2 _panStart;

	// Guide lines
	private bool _showGuides;
	private Node2D _guideOverlay = null!;

	// Minimap
	private MinimapPanel _minimap = null!;

	// Status bar
	private Label _statusLabel = null!;

	// Camera settings
	private const float PanSpeed = 500f;
	// Discrete zoom levels — CellSize * zoom is always an integer for pixel-aligned grid lines
	private static readonly float[] ZoomLevels =
		[0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f];
	private int _zoomIndex = 2; // start at 1.0

	public override void _Ready()
	{
		// Initialize simulation constants
		Constants.Initialize(_config.ToSimulationConfig());

		// Create editor state
		CreateNewMap(_mapWidth, _mapHeight, _slotCount);

		// Create grid renderer
		_gridRenderer = new GridRenderer { Name = "GridRenderer" };
		_gridRenderer.SetConfig(_config);
		AddChild(_gridRenderer);

		// Create camera
		_camera = new Camera2D { Name = "EditorCamera" };
		AddChild(_camera);
		CenterCamera();

		// Create UI layer (renders above the grid)
		_uiLayer = new CanvasLayer { Name = "UILayer" };
		AddChild(_uiLayer);

		// Create toolbar
		_toolbar = new EditorToolbar { Name = "EditorToolbar" };
		_uiLayer.AddChild(_toolbar);      // _Ready() fires here, creating _unitButtons
		_toolbar.SetConfig(_config);      // now the list is populated

		// Wire up toolbar events
		_toolbar.ToolSelected += OnToolSelected;
		_toolbar.GroundSelected += OnGroundSelected;
		_toolbar.TerrainSelected += OnTerrainSelected;
		_toolbar.BlockSelected += OnBlockSelected;
		_toolbar.SlotSelected += OnSlotSelected;
		_toolbar.MirrorRequested += OnMirrorRequested;
		_toolbar.MirrorTeamsToggled += on => _mirrorTeams = on;
		_toolbar.NewMapRequested += OnNewMapRequested;
		_toolbar.ResizeRequested += OnResizeRequested;
		_toolbar.TestMapRequested += OnTestMapRequested;
		_toolbar.SaveRequested += OnSaveRequested;
		_toolbar.LoadRequested += OnLoadRequested;
		_toolbar.BackRequested += OnBackRequested;
		_toolbar.MapNameChanged += name => _mapName = name;
		_toolbar.SlotCountChanged += count => _slotCount = count;
		_toolbar.GuidesToggled += OnGuidesToggled;
		_toolbar.ToolModeSelected += OnToolModeSelected;

		// Guide overlay (draws on top of grid)
		_guideOverlay = new GuideOverlay { Name = "GuideOverlay" };
		AddChild(_guideOverlay);

		_lineOverlay = new LinePreviewOverlay
		{
			Name = "LinePreviewOverlay",
			CellSize = GridRenderer.CellSize,
			GridPadding = GridRenderer.GridPadding
		};
		AddChild(_lineOverlay);

		_selOverlay = new SelectionOverlay
		{
			Name = "SelectionOverlay",
			CellSize = GridRenderer.CellSize,
			GridPadding = GridRenderer.GridPadding
		};
		AddChild(_selOverlay);

		// Add minimap to UI layer (bottom-left corner)
		_minimap = new MinimapPanel
		{
			CustomMinimumSize = new Vector2(200, 130),
			MouseFilter = Control.MouseFilterEnum.Stop
		};
		_minimap.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
		_minimap.OffsetTop = -130;
		_minimap.OffsetBottom = 0;
		_minimap.OffsetLeft = 0;
		_minimap.OffsetRight = 200;
		_minimap.SetConfig(_config);
		_minimap.SetGameState(_editorState);
		_minimap.CameraJumpRequested += OnMinimapJump;
		_uiLayer.AddChild(_minimap);

		// Status bar
		var statusBar = new PanelContainer();
		statusBar.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
		statusBar.OffsetTop = -24;
		statusBar.OffsetBottom = 0;
		statusBar.MouseFilter = Control.MouseFilterEnum.Ignore;
		var statusStyle = new StyleBoxFlat { BgColor = new Color(0.055f, 0.055f, 0.07f, 0.92f) };
		statusBar.AddThemeStyleboxOverride("panel", statusStyle);

		_statusLabel = new Label
		{
			Text = "",
			VerticalAlignment = VerticalAlignment.Center,
			LabelSettings = new LabelSettings { FontSize = 11 }
		};
		_statusLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.55f, 0.65f));
		statusBar.AddChild(_statusLabel);
		_uiLayer.AddChild(statusBar);

		RefreshRenderer();
	}

	public override void _Process(double delta)
	{
		// WASD / Arrow key panning
		var dt = (float)delta;
		var velocity = Vector2.Zero;

		if (Godot.Input.IsActionPressed("pan_up")) velocity.Y -= 1;
		if (Godot.Input.IsActionPressed("pan_down")) velocity.Y += 1;
		if (Godot.Input.IsActionPressed("pan_left")) velocity.X -= 1;
		if (Godot.Input.IsActionPressed("pan_right")) velocity.X += 1;

		// Edge scrolling
		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewportRect().Size;
		float edgeMargin = 20f;

		if (mousePos.X < edgeMargin) velocity.X -= 1;
		if (mousePos.X > viewportSize.X - edgeMargin) velocity.X += 1;
		if (mousePos.Y < edgeMargin) velocity.Y -= 1;
		if (mousePos.Y > viewportSize.Y - edgeMargin) velocity.Y += 1;

		if (velocity != Vector2.Zero)
		{
			_camera.Position += velocity.Normalized() * PanSpeed * dt / _camera.Zoom.X;
			ClampCamera();
		}

		// Robust middle-mouse drag panning (fallback if UI eats mouse motion)
		if (Godot.Input.IsMouseButtonPressed(MouseButton.Middle))
		{
			if (!_isPanning)
			{
				_isPanning = true;
				_panStart = mousePos;
			}
			else
			{
				var rel = mousePos - _panStart;
				if (rel != Vector2.Zero)
				{
					_camera.Position -= rel / _camera.Zoom.X;
					ClampCamera();
					_panStart = mousePos;
				}
			}
		}
		else
		{
			_isPanning = false;
		}

		// Update minimap camera view
		var viewSize = GetViewportRect().Size / _camera.Zoom;
		_minimap.SetCameraView(_camera.Position, viewSize);

		UpdateStatusBar();
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		// Undo/Redo
		if (@event is InputEventKey key && key.Pressed && !key.Echo)
		{
			if (key.Keycode == Key.Z && key.CtrlPressed && key.ShiftPressed)
			{
				Redo();
				GetViewport().SetInputAsHandled();
				return;
			}
			if (key.Keycode == Key.Z && key.CtrlPressed)
			{
				Undo();
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		// Esc: cancel in-progress line or selection
		if (@event is InputEventKey keyEsc && keyEsc.Pressed && !keyEsc.Echo && keyEsc.Keycode == Key.Escape)
		{
			CancelLine();
			CancelSelection();
			GetViewport().SetInputAsHandled();
			return;
		}

		// Delete key: clear selection
		if (@event is InputEventKey keyDel && keyDel.Pressed && !keyDel.Echo
			&& (keyDel.Keycode == Key.Delete || keyDel.Keycode == Key.Backspace))
		{
			DeleteSelection();
			GetViewport().SetInputAsHandled();
			return;
		}

		// Tool mode keyboard shortcuts
		if (@event is InputEventKey key2 && key2.Pressed && !key2.Echo && !key2.CtrlPressed)
		{
			EditorMode? newMode = key2.Keycode switch
			{
				Key.P => EditorMode.Paint,
				Key.F => EditorMode.Fill,
				Key.K => EditorMode.Pick,
				Key.S => EditorMode.Select,
				Key.L => EditorMode.Line,
				Key.E => EditorMode.Erase,
				_ => null
			};
			if (newMode.HasValue)
			{
				OnToolModeSelected(newMode.Value);
				_toolbar.HighlightToolMode(newMode.Value);
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		// Mouse wheel zoom
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.Pressed)
			{
				if (mouseButton.ButtonIndex == MouseButton.WheelUp && _zoomIndex < ZoomLevels.Length - 1)
				{
					_zoomIndex++;
					_camera.Zoom = Vector2.One * ZoomLevels[_zoomIndex];
					ClampCamera();
					GetViewport().SetInputAsHandled();
					return;
				}
				if (mouseButton.ButtonIndex == MouseButton.WheelDown && _zoomIndex > 0)
				{
					_zoomIndex--;
					_camera.Zoom = Vector2.One * ZoomLevels[_zoomIndex];
					ClampCamera();
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			// Select tool intercept
			if (_currentMode == EditorMode.Select)
			{
				if (@event is InputEventMouseButton smb && smb.ButtonIndex == MouseButton.Left)
				{
					if (smb.Pressed)
					{
						var gp = GetGridPos(smb.GlobalPosition);
						SelectMouseDown(gp);
					}
					else
					{
						var gp = GetGridPos(smb.GlobalPosition);
						SelectMouseUp(gp);
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			// Line tool intercept
			if (_currentMode == EditorMode.Line)
			{
				if (@event is InputEventMouseButton lb && lb.ButtonIndex == MouseButton.Left)
				{
					if (lb.Pressed)
					{
						_lineActive = true;
						_lineStart = GetGridPos(lb.GlobalPosition);
						_linePreviewPoints.Clear();
						_linePreviewPoints.Add(new Vector2I(_lineStart.X, _lineStart.Y));
						_lineOverlay.Points = new List<Vector2I>(_linePreviewPoints);
						_lineOverlay.QueueRedraw();
					}
					else if (_lineActive)
					{
						CommitLine(GetGridPos(lb.GlobalPosition));
					}
					GetViewport().SetInputAsHandled();
					return;
				}
			}

			// Left-click: paint
			if (mouseButton.ButtonIndex == MouseButton.Left)
			{
				if (mouseButton.Pressed)
				{
					var gridPos = GetGridPos(mouseButton.GlobalPosition);
					if (_currentMode == EditorMode.Fill)
					{
						FloodFill(gridPos);
						GetViewport().SetInputAsHandled();
						return;
					}
					if (_currentMode == EditorMode.Pick)
					{
						if (_editorState.Grid.InBounds(gridPos))
							PickTileAt(gridPos);
						GetViewport().SetInputAsHandled();
						return;
					}
					// existing path:
					StartDrag(_currentMode == EditorMode.Erase);
					ApplyToolAt(gridPos, _currentMode == EditorMode.Erase);
				}
				else
				{
					EndDrag();
				}
				GetViewport().SetInputAsHandled();
				return;
			}

			// Right-click: erase
			if (mouseButton.ButtonIndex == MouseButton.Right)
			{
				if (mouseButton.Pressed)
				{
					StartDrag(true);
					var gridPos = GetGridPos(mouseButton.GlobalPosition);
					ApplyToolAt(gridPos, true);
				}
				else
				{
					EndDrag();
				}
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		// Mouse motion for select tool
		if (@event is InputEventMouseMotion smm && _currentMode == EditorMode.Select)
		{
			if (_selPhase == SelectPhase.Drawing || _selPhase == SelectPhase.Moving)
			{
				SelectMouseMove(GetGridPos(smm.GlobalPosition));
				GetViewport().SetInputAsHandled();
				return;
			}
		}

		// Mouse motion for line preview
		if (@event is InputEventMouseMotion lineMotion && _currentMode == EditorMode.Line && _lineActive)
		{
			var end = GetGridPos(lineMotion.GlobalPosition);
			_linePreviewPoints.Clear();
			_linePreviewPoints.AddRange(Bresenham(_lineStart.X, _lineStart.Y, end.X, end.Y));
			_lineOverlay.Points = new List<Vector2I>(_linePreviewPoints);
			_lineOverlay.QueueRedraw();
			GetViewport().SetInputAsHandled();
			return;
		}

		// Mouse motion for drag painting
		if (@event is InputEventMouseMotion motion)
		{
			if (_isDragging)
			{
				bool isErasing = Godot.Input.IsMouseButtonPressed(MouseButton.Right);
				var gridPos = GetGridPos(motion.GlobalPosition);
				ApplyToolAt(gridPos, isErasing);
				GetViewport().SetInputAsHandled();
			}
		}
	}

	private GridPos GetGridPos(Vector2 _)
	{
		var local = _gridRenderer.ToLocal(GetGlobalMousePosition());
		int x = (int)Mathf.Floor((local.X - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int y = (int)Mathf.Floor((local.Y - GridRenderer.GridPadding) / GridRenderer.CellSize);
		return new GridPos(x, y);
	}

	private static List<Vector2I> Bresenham(int x0, int y0, int x1, int y1)
	{
		var pts = new List<Vector2I>();
		int dx = System.Math.Abs(x1 - x0), sx = x0 < x1 ? 1 : -1;
		int dy = -System.Math.Abs(y1 - y0), sy = y0 < y1 ? 1 : -1;
		int err = dx + dy;
		while (true)
		{
			pts.Add(new Vector2I(x0, y0));
			if (x0 == x1 && y0 == y1) break;
			int e2 = 2 * err;
			if (e2 >= dy) { err += dy; x0 += sx; }
			if (e2 <= dx) { err += dx; y0 += sy; }
		}
		return pts;
	}

	private void StartDrag(bool isErase)
	{
		_isDragging = true;
		_dragAction = new EditorAction();
		_dragVisited.Clear();
	}

	private void EndDrag()
	{
		if (_isDragging && _dragAction != null && _dragAction.Before.Count > 0)
		{
			_actionStack.Push(_dragAction);
		}
		_isDragging = false;
		_dragAction = null;
		_dragVisited.Clear();
	}

	private void FloodFill(GridPos start)
	{
		var grid = _editorState.Grid;
		if (!grid.InBounds(start)) return;

		var action = new EditorAction();
		var startCell = grid[start.X, start.Y];
		var targetGround = startCell.Ground;
		var targetTerrain = startCell.Terrain;

		var visited = new HashSet<(int, int)>();
		var stack = new Stack<GridPos>();
		stack.Push(start);

		while (stack.Count > 0)
		{
			var pos = stack.Pop();
			if (!grid.InBounds(pos)) continue;
			if (visited.Contains((pos.X, pos.Y))) continue;
			visited.Add((pos.X, pos.Y));

			var cell = grid[pos.X, pos.Y];
			var existingBlock = _editorState.GetBlockAt(pos);

			bool matches = _currentTool switch
			{
				EditorTool.GroundPaint  => cell.Ground == targetGround,
				EditorTool.TerrainPaint => cell.Terrain == targetTerrain,
				_ => false
			};
			if (!matches) continue;

			action.Before.Add(new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
				existingBlock?.Type, existingBlock != null ? (int?)existingBlock.PlayerId : null));

			if (_currentTool == EditorTool.GroundPaint)
				cell.Ground = _currentGround;
			else if (_currentTool == EditorTool.TerrainPaint)
			{
				if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
				cell.Terrain = _currentTerrain;
			}

			var newBlock = _editorState.GetBlockAt(pos);
			action.After.Add(new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
				newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null));

			stack.Push(new GridPos(pos.X + 1, pos.Y));
			stack.Push(new GridPos(pos.X - 1, pos.Y));
			stack.Push(new GridPos(pos.X, pos.Y + 1));
			stack.Push(new GridPos(pos.X, pos.Y - 1));
		}

		if (action.Before.Count > 0)
			_actionStack.Push(action);

		RefreshRenderer();
	}

	private void PickTileAt(GridPos pos)
	{
		var cell = _editorState.Grid[pos.X, pos.Y];
		var block = _editorState.GetBlockAt(pos);

		if (block != null)
		{
			_currentBlock = block.Type;
			_currentBlockRooted = block.IsFullyRooted && block.Type != BlockType.Wall;
			_currentSlot = block.PlayerId;
			_currentTool = EditorTool.UnitPlace;
		}
		else if (cell.Terrain != TerrainType.None)
		{
			_currentTerrain = cell.Terrain;
			_currentTool = EditorTool.TerrainPaint;
		}
		else
		{
			_currentGround = cell.Ground;
			_currentTool = EditorTool.GroundPaint;
		}

		_currentMode = EditorMode.Paint;
		_toolbar.HighlightToolMode(EditorMode.Paint);
	}

	private void ApplyToolAt(GridPos pos, bool isErase)
	{
		if (_currentMode == EditorMode.Erase) isErase = true;
		if (!_editorState.Grid.InBounds(pos)) return;
		if (_dragVisited.Contains((pos.X, pos.Y))) return;
		_dragVisited.Add((pos.X, pos.Y));

		var cell = _editorState.Grid[pos.X, pos.Y];
		var existingBlock = _editorState.GetBlockAt(pos);

		var before = new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
			existingBlock?.Type, existingBlock != null ? (int?)existingBlock.PlayerId : null);

		if (isErase)
		{
			cell.Ground = GroundType.Normal;
			cell.Terrain = TerrainType.None;
			if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
		}
		else
		{
			switch (_currentTool)
			{
				case EditorTool.GroundPaint:
					cell.Ground = _currentGround;
					break;
				case EditorTool.TerrainPaint:
					if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
					cell.Terrain = _currentTerrain;
					break;
				case EditorTool.UnitPlace:
					if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
					if (cell.Terrain != TerrainType.None) break;
					int placeSlot = _currentBlock == BlockType.Nugget ? -1 : _currentSlot;
					if (placeSlot >= 0) EnsurePlayerExists(placeSlot);
					var placed = _editorState.AddBlock(_currentBlock, placeSlot, pos);
					if (_currentBlockRooted && placed.Type != BlockType.Wall && placed.Type != BlockType.Nugget)
					{
						placed.State = BlockState.Rooted;
						placed.RootProgress = Constants.RootTicks;
					}
					break;
				case EditorTool.Eraser:
					cell.Ground = GroundType.Normal;
					cell.Terrain = TerrainType.None;
					if (existingBlock != null) _editorState.RemoveBlock(existingBlock);
					break;
			}
		}

		var newBlock = _editorState.GetBlockAt(pos);
		var after = new CellSnapshot(pos.X, pos.Y, cell.Ground, cell.Terrain,
			newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null);

		if (before != after)
		{
			_dragAction?.Before.Add(before);
			_dragAction?.After.Add(after);
		}

		RefreshRenderer();
	}

	private void EnsurePlayerExists(int slotId)
	{
		if (_editorState.Players.Any(p => p.Id == slotId)) return;
		_editorState.Players.Add(new Player { Id = slotId, TeamId = slotId });
	}

	private void ApplyCurrentTileToCell(GridPos pos)
	{
		var grid = _editorState.Grid;
		if (!grid.InBounds(pos)) return;
		var cell = grid[pos.X, pos.Y];
		var existing = _editorState.GetBlockAt(pos);

		switch (_currentTool)
		{
			case EditorTool.GroundPaint:
				cell.Ground = _currentGround;
				break;
			case EditorTool.TerrainPaint:
				if (existing != null) _editorState.RemoveBlock(existing);
				cell.Terrain = _currentTerrain;
				break;
			case EditorTool.UnitPlace:
				if (existing != null) _editorState.RemoveBlock(existing);
				if (cell.Terrain != TerrainType.None) break;
				EnsurePlayerExists(_currentSlot);
				var placed = _editorState.AddBlock(_currentBlock, _currentSlot, pos);
				if (_currentBlockRooted && placed.Type != BlockType.Wall)
				{
					placed.State = BlockState.Rooted;
					placed.RootProgress = Constants.RootTicks;
				}
				break;
			case EditorTool.Eraser:
				cell.Ground = GroundType.Normal;
				cell.Terrain = TerrainType.None;
				if (existing != null) _editorState.RemoveBlock(existing);
				break;
		}
	}

	private void CommitLine(GridPos end)
	{
		var pts = Bresenham(_lineStart.X, _lineStart.Y, end.X, end.Y);
		var action = new EditorAction();
		var grid = _editorState.Grid;

		foreach (var p in pts)
		{
			var pos = new GridPos(p.X, p.Y);
			if (!grid.InBounds(pos)) continue;
			var cell = grid[p.X, p.Y];
			var existing = _editorState.GetBlockAt(pos);
			action.Before.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
				existing?.Type, existing != null ? (int?)existing.PlayerId : null));

			ApplyCurrentTileToCell(pos);

			var newBlock = _editorState.GetBlockAt(pos);
			action.After.Add(new CellSnapshot(p.X, p.Y, cell.Ground, cell.Terrain,
				newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null));
		}

		if (action.Before.Count > 0) _actionStack.Push(action);
		_lineActive = false;
		_linePreviewPoints.Clear();
		_lineOverlay.Points = [];
		_lineOverlay.QueueRedraw();
		RefreshRenderer();
	}

	private void Undo()
	{
		var action = _actionStack.Undo();
		if (action == null) return;
		ApplySnapshots(action.Before);
		RefreshRenderer();
	}

	private void Redo()
	{
		var action = _actionStack.Redo();
		if (action == null) return;
		ApplySnapshots(action.After);
		RefreshRenderer();
	}

	private void ApplySnapshots(List<CellSnapshot> snapshots)
	{
		foreach (var snap in snapshots)
		{
			var cell = _editorState.Grid[snap.X, snap.Y];
			var existing = _editorState.GetBlockAt(new GridPos(snap.X, snap.Y));
			if (existing != null)
				_editorState.RemoveBlock(existing);

			cell.Ground = snap.Ground;
			cell.Terrain = snap.Terrain;

			if (snap.UnitType.HasValue && snap.UnitSlot.HasValue)
			{
				EnsurePlayerExists(snap.UnitSlot.Value);
				_editorState.AddBlock(snap.UnitType.Value, snap.UnitSlot.Value, new GridPos(snap.X, snap.Y));
			}
		}
	}

	private void RefreshRenderer()
	{
		_gridRenderer.SetGameState(_editorState);
		_minimap.SetGameState(_editorState);
	}

	private void OnMinimapJump(Vector2 worldPos)
	{
		_camera.Position = worldPos;
		ClampCamera();
	}

	private void CenterCamera()
	{
		var gridPixelW = _mapWidth * GridRenderer.CellSize;
		var gridPixelH = _mapHeight * GridRenderer.CellSize;
		_camera.Position = new Vector2(gridPixelW * 0.5f + GridRenderer.GridPadding, gridPixelH * 0.5f + GridRenderer.GridPadding);
	}

	private void ClampCamera()
	{
		var gridPixelW = _mapWidth * GridRenderer.CellSize;
		var gridPixelH = _mapHeight * GridRenderer.CellSize;
		var effectiveViewW = GetViewportRect().Size.X / _camera.Zoom.X;
		var effectiveViewH = GetViewportRect().Size.Y / _camera.Zoom.Y;

		float marginX = effectiveViewW * 0.25f;
		float marginY = effectiveViewH * 0.25f;

		float minX = effectiveViewW * 0.5f - marginX + GridRenderer.GridPadding;
		float maxX = gridPixelW - effectiveViewW * 0.5f + marginX + GridRenderer.GridPadding;
		float minY = effectiveViewH * 0.5f - marginY + GridRenderer.GridPadding;
		float maxY = gridPixelH - effectiveViewH * 0.5f + marginY + GridRenderer.GridPadding;

		// If min > max (can happen when extremely zoomed out), Mathf.Clamp does something weird, so use Min/Max safely
		float clampX = Mathf.Clamp(_camera.Position.X, Mathf.Min(minX, maxX), Mathf.Max(minX, maxX));
		float clampY = Mathf.Clamp(_camera.Position.Y, Mathf.Min(minY, maxY), Mathf.Max(minY, maxY));

		_camera.Position = new Vector2(clampX, clampY);
	}

	// --- Selection tool helpers ---

	private Rect2I NormalizeSelRect(GridPos a, GridPos b)
	{
		int x = System.Math.Max(0, System.Math.Min(a.X, b.X));
		int y = System.Math.Max(0, System.Math.Min(a.Y, b.Y));
		int x2 = System.Math.Min(_mapWidth - 1, System.Math.Max(a.X, b.X));
		int y2 = System.Math.Min(_mapHeight - 1, System.Math.Max(a.Y, b.Y));
		return new Rect2I(x, y, x2 - x + 1, y2 - y + 1);
	}

	private bool InsideSelRect(GridPos gp) =>
		_selPhase != SelectPhase.Idle
		&& gp.X >= _selRect.Position.X && gp.X < _selRect.Position.X + _selRect.Size.X
		&& gp.Y >= _selRect.Position.Y && gp.Y < _selRect.Position.Y + _selRect.Size.Y;

	private void SelectMouseDown(GridPos gp)
	{
		if (_selPhase == SelectPhase.Ready && InsideSelRect(gp))
		{
			_selPhase = SelectPhase.Moving;
			_selMoveStart = gp;
			_selOffset = Vector2I.Zero;
		}
		else
		{
			if (_selPhase == SelectPhase.Ready) CancelSelection();
			_selPhase = SelectPhase.Drawing;
			_selDrawStart = gp;
			_selRect = NormalizeSelRect(gp, gp);
			_selGround = null; _selTerrain = null; _selBlocks = null;
			_selOffset = Vector2I.Zero;
			UpdateSelOverlay();
		}
	}

	private void SelectMouseMove(GridPos gp)
	{
		if (_selPhase == SelectPhase.Drawing)
		{
			_selRect = NormalizeSelRect(_selDrawStart, gp);
			UpdateSelOverlay();
		}
		else if (_selPhase == SelectPhase.Moving)
		{
			_selOffset = new Vector2I(gp.X - _selMoveStart.X, gp.Y - _selMoveStart.Y);
			UpdateSelOverlay();
		}
	}

	private void SelectMouseUp(GridPos gp)
	{
		if (_selPhase == SelectPhase.Drawing)
		{
			_selRect = NormalizeSelRect(_selDrawStart, gp);
			CaptureSelection();
			_selPhase = SelectPhase.Ready;
			UpdateSelOverlay();
		}
		else if (_selPhase == SelectPhase.Moving)
		{
			if (_selOffset != Vector2I.Zero) CommitSelection();
			else { _selPhase = SelectPhase.Ready; UpdateSelOverlay(); }
		}
	}

	private void CaptureSelection()
	{
		int w = _selRect.Size.X, h = _selRect.Size.Y;
		int ox = _selRect.Position.X, oy = _selRect.Position.Y;
		_selGround = new GroundType[w, h];
		_selTerrain = new TerrainType[w, h];
		_selBlocks = new (BlockType, int, bool)[w, h];
		var grid = _editorState.Grid;
		for (int dy = 0; dy < h; dy++)
			for (int dx = 0; dx < w; dx++)
			{
				var cell = grid[ox + dx, oy + dy];
				var blk = _editorState.GetBlockAt(new GridPos(ox + dx, oy + dy));
				_selGround[dx, dy] = cell.Ground;
				_selTerrain[dx, dy] = cell.Terrain;
				_selBlocks[dx, dy] = blk != null
					? (blk.Type, blk.PlayerId, blk.IsFullyRooted && blk.Type != BlockType.Wall)
					: (BlockType.Builder, -1, false);
			}
	}

	private void CommitSelection()
	{
		if (_selGround == null) { CancelSelection(); return; }

		var action = new EditorAction();
		int w = _selRect.Size.X, h = _selRect.Size.Y;
		int ox = _selRect.Position.X, oy = _selRect.Position.Y;
		int dox = _selOffset.X, doy = _selOffset.Y;
		var grid = _editorState.Grid;
		var recordedBefore = new HashSet<(int, int)>();

		// Record & clear source
		for (int row = 0; row < h; row++)
			for (int col = 0; col < w; col++)
			{
				var sp = new GridPos(ox + col, oy + row);
				var sc = grid[sp.X, sp.Y];
				var sb = _editorState.GetBlockAt(sp);
				action.Before.Add(new CellSnapshot(sp.X, sp.Y, sc.Ground, sc.Terrain,
					sb?.Type, sb != null ? (int?)sb.PlayerId : null));
				recordedBefore.Add((sp.X, sp.Y));
				if (sb != null) _editorState.RemoveBlock(sb);
				sc.Ground = GroundType.Normal;
				sc.Terrain = TerrainType.None;
				action.After.Add(new CellSnapshot(sp.X, sp.Y, GroundType.Normal, TerrainType.None, null, null));
			}

		// Write destination
		for (int row = 0; row < h; row++)
			for (int col = 0; col < w; col++)
			{
				var dp = new GridPos(ox + col + dox, oy + row + doy);
				if (!grid.InBounds(dp)) continue;
				var dc = grid[dp.X, dp.Y];
				var db = _editorState.GetBlockAt(dp);
				if (!recordedBefore.Contains((dp.X, dp.Y)))
					action.Before.Add(new CellSnapshot(dp.X, dp.Y, dc.Ground, dc.Terrain,
						db?.Type, db != null ? (int?)db.PlayerId : null));
				if (db != null) _editorState.RemoveBlock(db);
				dc.Ground = _selGround![col, row];
				dc.Terrain = _selTerrain![col, row];
				var (btype, bslot, brooted) = _selBlocks![col, row];
				if (bslot >= 0 && dc.Terrain == TerrainType.None)
				{
					EnsurePlayerExists(bslot);
					var placed = _editorState.AddBlock(btype, bslot, dp);
					if (brooted && btype != BlockType.Wall)
					{ placed.State = BlockState.Rooted; placed.RootProgress = Constants.RootTicks; }
				}
				var newb = _editorState.GetBlockAt(dp);
				action.After.Add(new CellSnapshot(dp.X, dp.Y, dc.Ground, dc.Terrain,
					newb?.Type, newb != null ? (int?)newb.PlayerId : null));
			}

		_actionStack.Push(action);
		CancelSelection();
		RefreshRenderer();
	}

	private void DeleteSelection()
	{
		if (_selPhase != SelectPhase.Ready || _selGround == null) return;
		var action = new EditorAction();
		var grid = _editorState.Grid;
		for (int row = 0; row < _selRect.Size.Y; row++)
			for (int col = 0; col < _selRect.Size.X; col++)
			{
				var p = new GridPos(_selRect.Position.X + col, _selRect.Position.Y + row);
				var c = grid[p.X, p.Y];
				var b = _editorState.GetBlockAt(p);
				action.Before.Add(new CellSnapshot(p.X, p.Y, c.Ground, c.Terrain,
					b?.Type, b != null ? (int?)b.PlayerId : null));
				if (b != null) _editorState.RemoveBlock(b);
				c.Ground = GroundType.Normal;
				c.Terrain = TerrainType.None;
				action.After.Add(new CellSnapshot(p.X, p.Y, GroundType.Normal, TerrainType.None, null, null));
			}
		_actionStack.Push(action);
		CancelSelection();
		RefreshRenderer();
	}

	private void UpdateSelOverlay()
	{
		_selOverlay.Phase = _selPhase;
		_selOverlay.Rect = _selRect;
		_selOverlay.Offset = _selOffset;
		_selOverlay.QueueRedraw();
	}

	// --- Map operations ---

	private void CreateNewMap(int width, int height, int slots)
	{
		_mapWidth = width;
		_mapHeight = height;
		_slotCount = slots;
		_editorState = new GameState(new Grid(width, height));
		_actionStack.Clear();

		// Add default players for slot rendering
		for (int i = 0; i < slots; i++)
			EnsurePlayerExists(i);
	}

	private MapData BuildMapData()
	{
		var ground = new List<GroundEntry>();
		var terrain = new List<TerrainEntry>();
		var units = new List<UnitEntry>();
		var grid = _editorState.Grid;

		for (int y = 0; y < grid.Height; y++)
		{
			for (int x = 0; x < grid.Width; x++)
			{
				var cell = grid[x, y];
				if (cell.Ground != GroundType.Normal)
					ground.Add(new GroundEntry(x, y, cell.Ground));
				if (cell.Terrain != TerrainType.None)
					terrain.Add(new TerrainEntry(x, y, cell.Terrain));
			}
		}

		foreach (var block in _editorState.Blocks)
			units.Add(new UnitEntry(block.Pos.X, block.Pos.Y, block.Type, block.PlayerId, block.IsFullyRooted && block.Type != BlockType.Wall));

		return new MapData(_mapName, _mapWidth, _mapHeight, _slotCount, ground, terrain, units);
	}

	private void LoadMapIntoEditor(MapData data)
	{
		_mapName = data.Name;
		_mapWidth = data.Width;
		_mapHeight = data.Height;
		_slotCount = data.SlotCount;

		_editorState = new GameState(new Grid(data.Width, data.Height));
		_actionStack.Clear();

		// Apply ground
		foreach (var g in data.Ground)
			_editorState.Grid[g.X, g.Y].Ground = g.Type;

		// Apply terrain
		foreach (var t in data.Terrain)
			_editorState.Grid[t.X, t.Y].Terrain = t.Type;

		// Place units
		foreach (var u in data.Units)
		{
			if (u.SlotId >= 0) EnsurePlayerExists(u.SlotId);
			var block = _editorState.AddBlock(u.Type, u.SlotId, new GridPos(u.X, u.Y));
			if (u.Rooted && u.Type != BlockType.Wall && u.Type != BlockType.Nugget)
			{
				block.State = BlockState.Rooted;
				block.RootProgress = Constants.RootTicks;
			}
		}

		// Update toolbar
		_toolbar.SetMapName(_mapName);
		_toolbar.SetSlotCount(_slotCount);

		CenterCamera();
		RefreshRenderer();
		RefreshGuides();
	}

	// --- Toolbar event handlers ---

	private void OnToolModeSelected(EditorMode mode)
	{
		_currentMode = mode;
		if (mode != EditorMode.Line) CancelLine();
		if (mode != EditorMode.Select) CancelSelection();
	}

	private void CancelLine()
	{
		_lineActive = false;
		_linePreviewPoints.Clear();
		_lineOverlay.Points = [];
		_lineOverlay.QueueRedraw();
	}

	private void CancelSelection()
	{
		_selPhase = SelectPhase.Idle;
		_selGround = null;
		_selTerrain = null;
		_selBlocks = null;
		_selRect = default;
		_selOffset = Vector2I.Zero;
		_selOverlay.Phase = SelectPhase.Idle;
		_selOverlay.QueueRedraw();
	}

	private void OnToolSelected(EditorTool tool) => _currentTool = tool;
	private void OnGroundSelected(GroundType ground)
	{
		_currentGround = ground;
		_currentTool = EditorTool.GroundPaint;
	}
	private void OnTerrainSelected(TerrainType terrain)
	{
		_currentTerrain = terrain;
		_currentTool = EditorTool.TerrainPaint;
	}
	private void OnBlockSelected(BlockType block, bool isRooted)
	{
		_currentBlock = block;
		_currentBlockRooted = isRooted;
		_currentTool = EditorTool.UnitPlace;
	}
	private void OnSlotSelected(int slot) => _currentSlot = slot;

	// --- Mirror operations ---

	private int MirrorSlot(int slot)
	{
		if (slot % 2 == 0 && slot + 1 < _slotCount) return slot + 1;
		if (slot % 2 == 1 && slot - 1 >= 0) return slot - 1;
		return slot;
	}

	private void CopyCellMirrored(int srcX, int srcY, int dstX, int dstY, EditorAction action)
	{
		var grid = _editorState.Grid;
		var src = grid[srcX, srcY];
		var dst = grid[dstX, dstY];
		var srcBlk = _editorState.GetBlockAt(new GridPos(srcX, srcY));
		var dstBlk = _editorState.GetBlockAt(new GridPos(dstX, dstY));

		action.Before.Add(new CellSnapshot(dstX, dstY, dst.Ground, dst.Terrain,
			dstBlk?.Type, dstBlk != null ? (int?)dstBlk.PlayerId : null));

		if (dstBlk != null) _editorState.RemoveBlock(dstBlk);
		dst.Ground = src.Ground;
		dst.Terrain = src.Terrain;

		if (srcBlk != null)
		{
			int mirroredSlot = _mirrorTeams ? MirrorSlot(srcBlk.PlayerId) : srcBlk.PlayerId;
			EnsurePlayerExists(mirroredSlot);
			var placed = _editorState.AddBlock(srcBlk.Type, mirroredSlot, new GridPos(dstX, dstY));
			if (srcBlk.IsFullyRooted && srcBlk.Type != BlockType.Wall)
			{ placed.State = BlockState.Rooted; placed.RootProgress = Constants.RootTicks; }
		}

		var newBlk = _editorState.GetBlockAt(new GridPos(dstX, dstY));
		action.After.Add(new CellSnapshot(dstX, dstY, dst.Ground, dst.Terrain,
			newBlk?.Type, newBlk != null ? (int?)newBlk.PlayerId : null));
	}

	private void MirrorLeftToRight()
	{
		var action = new EditorAction();
		for (int y = 0; y < _mapHeight; y++)
			for (int x = 0; x < _mapWidth / 2; x++)
				CopyCellMirrored(x, y, _mapWidth - 1 - x, y, action);
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void MirrorRightToLeft()
	{
		var action = new EditorAction();
		for (int y = 0; y < _mapHeight; y++)
			for (int x = _mapWidth - 1; x >= (_mapWidth + 1) / 2; x--)
				CopyCellMirrored(x, y, _mapWidth - 1 - x, y, action);
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void MirrorTopToBottom()
	{
		var action = new EditorAction();
		int halfH = _mapHeight / 2;
		for (int y = 0; y < halfH; y++)
			for (int x = 0; x < _mapWidth; x++)
				CopyCellMirrored(x, y, x, _mapHeight - 1 - y, action);
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void MirrorBottomToTop()
	{
		var action = new EditorAction();
		int halfH = _mapHeight / 2;
		for (int y = _mapHeight - 1; y >= (_mapHeight + 1) / 2; y--)
			for (int x = 0; x < _mapWidth; x++)
				CopyCellMirrored(x, y, x, _mapHeight - 1 - y, action);
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void MirrorDiagonalTLBR()
	{
		var action = new EditorAction();
		float wf = _mapWidth - 1f, hf = _mapHeight - 1f;
		if (wf <= 0 || hf <= 0) return;
		for (int y = 0; y < _mapHeight; y++)
			for (int x = 0; x < _mapWidth; x++)
			{
				if ((float)y * wf >= (float)x * hf) continue;
				int mx = (int)System.Math.Round(y * wf / hf);
				int my = (int)System.Math.Round(x * hf / wf);
				if (mx < 0 || mx >= _mapWidth || my < 0 || my >= _mapHeight) continue;
				CopyCellMirrored(x, y, mx, my, action);
			}
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void MirrorDiagonalTRBL()
	{
		var action = new EditorAction();
		float wf = _mapWidth - 1f, hf = _mapHeight - 1f;
		if (wf <= 0 || hf <= 0) return;
		for (int y = 0; y < _mapHeight; y++)
			for (int x = 0; x < _mapWidth; x++)
			{
				float normX = x / wf, normY = y / hf;
				if (normX + normY >= 1f) continue;
				int mx = (int)System.Math.Round((1f - normY) * wf);
				int my = (int)System.Math.Round((1f - normX) * hf);
				if (mx < 0 || mx >= _mapWidth || my < 0 || my >= _mapHeight) continue;
				CopyCellMirrored(x, y, mx, my, action);
			}
		if (action.Before.Count > 0) _actionStack.Push(action);
		RefreshRenderer();
	}

	private void OnMirrorRequested(MirrorDirection dir)
	{
		switch (dir)
		{
			case MirrorDirection.LR:       MirrorLeftToRight();   break;
			case MirrorDirection.RL:       MirrorRightToLeft();   break;
			case MirrorDirection.TB:       MirrorTopToBottom();   break;
			case MirrorDirection.BT:       MirrorBottomToTop();   break;
			case MirrorDirection.DiagTLBR: MirrorDiagonalTLBR(); break;
			case MirrorDirection.DiagTRBL: MirrorDiagonalTRBL(); break;
		}
	}

	private void OnNewMapRequested(int width, int height, int slots)
	{
		CreateNewMap(width, height, slots);
		_mapName = "Untitled";
		_toolbar.SetMapName(_mapName);
		_toolbar.SetSlotCount(slots);
		CenterCamera();
		RefreshRenderer();
		RefreshGuides();
	}

	private void OnResizeRequested(int newWidth, int newHeight)
	{
		var newGrid = new Grid(newWidth, newHeight);
		var oldGrid = _editorState.Grid;

		for (int x = 0; x < System.Math.Min(newWidth, _mapWidth); x++)
		{
			for (int y = 0; y < System.Math.Min(newHeight, _mapHeight); y++)
			{
				newGrid[x, y].Ground = oldGrid[x, y].Ground;
				newGrid[x, y].Terrain = oldGrid[x, y].Terrain;
			}
		}

		var newBlocks = new System.Collections.Generic.List<Block>();
		foreach (var b in _editorState.Blocks)
		{
			if (b.Pos.X < newWidth && b.Pos.Y < newHeight)
			{
				newBlocks.Add(b);
			}
		}

		_mapWidth = newWidth;
		_mapHeight = newHeight;
		_editorState = new GameState(newGrid);
		foreach (var b in newBlocks)
			_editorState.Blocks.Add(b);

		_actionStack.Clear();
		CenterCamera();
		RefreshRenderer();
		RefreshGuides();
	}

	private void OnTestMapRequested()
	{
		var data = BuildMapData();
		var assignments = new System.Collections.Generic.List<SlotAssignment>();
		for (int i = 0; i < data.SlotCount; i++)
			assignments.Add(new SlotAssignment(i, i)); // Assuming FFA for testing

		// Store current state to return to map editor
		Blocker.Game.UI.GameLaunchData.MapData = data;
		Blocker.Game.UI.GameLaunchData.Assignments = assignments;
		Blocker.Game.UI.GameLaunchData.ReturnToEditor = true;

		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnSaveRequested()
	{
		var data = BuildMapData();
		var fileName = SanitizeFileName(_mapName) + ".json";
		MapFileManager.Save(data, fileName);
	}

	private void OnLoadRequested(string fileName)
	{
		var data = MapFileManager.Load(fileName);
		if (data != null)
			LoadMapIntoEditor(data);
	}

	private void OnBackRequested()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}

	private void OnGuidesToggled(bool show)
	{
		_showGuides = show;
		if (_guideOverlay is GuideOverlay overlay)
		{
			overlay.ShowGuides = show;
			overlay.MapWidth = _mapWidth;
			overlay.MapHeight = _mapHeight;
			overlay.QueueRedraw();
		}
	}

	private static string SanitizeFileName(string name)
	{
		var invalid = System.IO.Path.GetInvalidFileNameChars();
		var sanitized = string.Join("_", name.Split(invalid, System.StringSplitOptions.RemoveEmptyEntries));
		return string.IsNullOrWhiteSpace(sanitized) ? "Untitled" : sanitized.Trim();
	}

	private void RefreshGuides()
	{
		if (_guideOverlay is GuideOverlay overlay)
		{
			overlay.MapWidth = _mapWidth;
			overlay.MapHeight = _mapHeight;
			overlay.QueueRedraw();
		}
	}

	private void UpdateStatusBar()
	{
		var local = _gridRenderer.ToLocal(GetGlobalMousePosition());
		int gx = (int)Mathf.Floor((local.X - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int gy = (int)Mathf.Floor((local.Y - GridRenderer.GridPadding) / GridRenderer.CellSize);

		string posStr = "—";
		string tileStr = "—";

		if (_editorState.Grid.InBounds(gx, gy))
		{
			posStr = $"x: {gx}  y: {gy}";
			var cell = _editorState.Grid[gx, gy];
			var blk = _editorState.GetBlockAt(new GridPos(gx, gy));
			if (blk != null)
				tileStr = $"{blk.Type} (slot {blk.PlayerId})";
			else if (cell.Terrain != TerrainType.None)
				tileStr = cell.Terrain.ToString();
			else
				tileStr = cell.Ground == GroundType.Normal ? "Normal" : cell.Ground.ToString();
		}

		string toolStr = _currentMode.ToString();
		string zoomStr = $"{ZoomLevels[_zoomIndex]:0.##}×";
		string sizeStr = $"{_mapWidth} × {_mapHeight}";

		_statusLabel.Text = $"  {posStr}    tile: {tileStr}    tool: {toolStr}    zoom: {zoomStr}    size: {sizeStr}";
	}
}

/// <summary>
/// Draws center guide lines over the grid when enabled.
/// </summary>
public partial class GuideOverlay : Node2D
{
	public bool ShowGuides { get; set; }
	public int MapWidth { get; set; }
	public int MapHeight { get; set; }

	private static readonly Color GuideColor = new(1f, 1f, 0f, 0.5f);

	public override void _Draw()
	{
		if (!ShowGuides || MapWidth == 0 || MapHeight == 0) return;

		float gridW = MapWidth * GridRenderer.CellSize;
		float gridH = MapHeight * GridRenderer.CellSize;
		float centerX = gridW * 0.5f + GridRenderer.GridPadding;
		float centerY = gridH * 0.5f + GridRenderer.GridPadding;

		// Vertical center line
		DrawLine(new Vector2(centerX, GridRenderer.GridPadding), new Vector2(centerX, gridH + GridRenderer.GridPadding), GuideColor, 2f);
		// Horizontal center line
		DrawLine(new Vector2(GridRenderer.GridPadding, centerY), new Vector2(gridW + GridRenderer.GridPadding, centerY), GuideColor, 2f);
	}
}
