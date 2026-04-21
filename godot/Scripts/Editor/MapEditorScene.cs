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
	private readonly SymmetryMirror _symmetry = new();

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

	// Middle-mouse pan state
	private bool _isPanning;
	private Vector2 _panStart;

	// Guide lines
	private bool _showGuides;
	private Node2D _guideOverlay = null!;

	// Minimap
	private MinimapPanel _minimap = null!;

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
		_toolbar.SymmetryChanged += OnSymmetryChanged;
		_toolbar.NewMapRequested += OnNewMapRequested;
		_toolbar.ResizeRequested += OnResizeRequested;
		_toolbar.TestMapRequested += OnTestMapRequested;
		_toolbar.SaveRequested += OnSaveRequested;
		_toolbar.LoadRequested += OnLoadRequested;
		_toolbar.BackRequested += OnBackRequested;
		_toolbar.MapNameChanged += name => _mapName = name;
		_toolbar.SlotCountChanged += count => {
			_slotCount = count;
			UpdateSymmetryMap(count);
		};
		_toolbar.GuidesToggled += OnGuidesToggled;
		_toolbar.ToolModeSelected += OnToolModeSelected;

		// Guide overlay (draws on top of grid)
		_guideOverlay = new GuideOverlay { Name = "GuideOverlay" };
		AddChild(_guideOverlay);

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

	private GridPos GetGridPos(Vector2 globalMousePos)
	{
		var local = _gridRenderer.ToLocal(GetGlobalMousePosition());
		int x = (int)Mathf.Floor((local.X - GridRenderer.GridPadding) / GridRenderer.CellSize);
		int y = (int)Mathf.Floor((local.Y - GridRenderer.GridPadding) / GridRenderer.CellSize);
		return new GridPos(x, y);
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

		// Get mirrored positions
		var positions = _symmetry.GetMirroredPositions(pos.X, pos.Y, _currentSlot, _mapWidth, _mapHeight);

		foreach (var (mx, my, mirrorSlot) in positions)
		{
			if (!_editorState.Grid.InBounds(mx, my)) continue;
			if (_dragVisited.Contains((mx, my))) continue;
			_dragVisited.Add((mx, my));

			var cell = _editorState.Grid[mx, my];
			var existingBlock = _editorState.GetBlockAt(new GridPos(mx, my));

			// Record before state
			var before = new CellSnapshot(mx, my, cell.Ground, cell.Terrain,
				existingBlock?.Type, existingBlock != null ? (int?)existingBlock.PlayerId : null);

			if (isErase)
			{
				// Erase: reset ground to Normal, terrain to None, remove unit
				cell.Ground = GroundType.Normal;
				cell.Terrain = TerrainType.None;
				if (existingBlock != null)
					_editorState.RemoveBlock(existingBlock);
			}
			else
			{
				switch (_currentTool)
				{
					case EditorTool.GroundPaint:
						cell.Ground = _currentGround;
						break;

					case EditorTool.TerrainPaint:
						// Remove existing block if placing terrain
						if (existingBlock != null)
							_editorState.RemoveBlock(existingBlock);
						cell.Terrain = _currentTerrain;
						break;

					case EditorTool.UnitPlace:
						// Remove existing block first
						if (existingBlock != null)
							_editorState.RemoveBlock(existingBlock);
						// Can't place on terrain
                        if (cell.Terrain != TerrainType.None) break;
                        EnsurePlayerExists(mirrorSlot);
                        var placedBlock = _editorState.AddBlock(_currentBlock, mirrorSlot, new GridPos(mx, my));
                        if (_currentBlockRooted && placedBlock.Type != BlockType.Wall)
                        {
                            placedBlock.State = BlockState.Rooted;
                            placedBlock.RootProgress = Constants.RootTicks;
                        }
                        break;

                    case EditorTool.Eraser:
                        cell.Ground = GroundType.Normal;
                        cell.Terrain = TerrainType.None;
                        if (existingBlock != null)
                            _editorState.RemoveBlock(existingBlock);
                        break;
                }
            }

            // Record after state
            var newBlock = _editorState.GetBlockAt(new GridPos(mx, my));
            var after = new CellSnapshot(mx, my, cell.Ground, cell.Terrain,
                newBlock?.Type, newBlock != null ? (int?)newBlock.PlayerId : null);

            // Only record if something changed
            if (before != after)
            {
                _dragAction?.Before.Add(before);
                _dragAction?.After.Add(after);
            }
        }

        RefreshRenderer();
    }

    private void EnsurePlayerExists(int slotId)
    {
        if (_editorState.Players.Any(p => p.Id == slotId)) return;
        _editorState.Players.Add(new Player { Id = slotId, TeamId = slotId });
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

    // --- Map operations ---

    private void UpdateSymmetryMap(int slots)
    {
        _symmetry.SlotMirrorMap.Clear();
        for (int i = 0; i < slots; i++)
        {
            if (i % 2 == 0)
                _symmetry.SlotMirrorMap[i] = i + 1 < slots ? i + 1 : i;
            else
                _symmetry.SlotMirrorMap[i] = i - 1;
        }
    }

    private void CreateNewMap(int width, int height, int slots)
    {
        _mapWidth = width;
        _mapHeight = height;
        _slotCount = slots;
        UpdateSymmetryMap(slots);
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
        UpdateSymmetryMap(data.SlotCount);

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
            EnsurePlayerExists(u.SlotId);
            var block = _editorState.AddBlock(u.Type, u.SlotId, new GridPos(u.X, u.Y));
            if (u.Rooted && u.Type != BlockType.Wall)
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

    private void CancelLine() { }
    private void CancelSelection() { }

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
    private void OnSymmetryChanged(SymmetryMode mode) => _symmetry.Mode = mode;

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
