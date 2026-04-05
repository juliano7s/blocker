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
    private GroundType _currentGround = GroundType.Normal;
    private TerrainType _currentTerrain = TerrainType.Terrain;
    private BlockType _currentBlock = BlockType.Builder;
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

    // Camera settings
    private const float PanSpeed = 500f;
    private const float ZoomMin = 0.3f;
    private const float ZoomMax = 3.0f;
    private const float ZoomStep = 0.1f;

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
        _uiLayer.AddChild(_toolbar);

        // Wire up toolbar events
        _toolbar.ToolSelected += OnToolSelected;
        _toolbar.GroundSelected += OnGroundSelected;
        _toolbar.TerrainSelected += OnTerrainSelected;
        _toolbar.BlockSelected += OnBlockSelected;
        _toolbar.SlotSelected += OnSlotSelected;
        _toolbar.SymmetryChanged += OnSymmetryChanged;
        _toolbar.NewMapRequested += OnNewMapRequested;
        _toolbar.SaveRequested += OnSaveRequested;
        _toolbar.LoadRequested += OnLoadRequested;
        _toolbar.BackRequested += OnBackRequested;
        _toolbar.MapNameChanged += name => _mapName = name;
        _toolbar.SlotCountChanged += count => _slotCount = count;
        _toolbar.GuidesToggled += OnGuidesToggled;

        // Guide overlay (draws on top of grid)
        _guideOverlay = new GuideOverlay { Name = "GuideOverlay" };
        AddChild(_guideOverlay);

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

        if (velocity != Vector2.Zero)
        {
            _camera.Position += velocity.Normalized() * PanSpeed * dt / _camera.Zoom.X;
            ClampCamera();
        }
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

        // Mouse wheel zoom
        if (@event is InputEventMouseButton mouseButton)
        {
            if (mouseButton.Pressed)
            {
                if (mouseButton.ButtonIndex == MouseButton.WheelUp)
                {
                    _camera.Zoom = Vector2.One * Mathf.Min(_camera.Zoom.X + ZoomStep, ZoomMax);
                    ClampCamera();
                    GetViewport().SetInputAsHandled();
                    return;
                }
                if (mouseButton.ButtonIndex == MouseButton.WheelDown)
                {
                    _camera.Zoom = Vector2.One * Mathf.Max(_camera.Zoom.X - ZoomStep, ZoomMin);
                    ClampCamera();
                    GetViewport().SetInputAsHandled();
                    return;
                }
            }

            // Middle-mouse pan
            if (mouseButton.ButtonIndex == MouseButton.Middle)
            {
                if (mouseButton.Pressed)
                {
                    _isPanning = true;
                    _panStart = mouseButton.GlobalPosition;
                }
                else
                {
                    _isPanning = false;
                }
                GetViewport().SetInputAsHandled();
                return;
            }

            // Left-click: paint
            if (mouseButton.ButtonIndex == MouseButton.Left)
            {
                if (mouseButton.Pressed)
                {
                    StartDrag(false);
                    var gridPos = GetGridPos(mouseButton.GlobalPosition);
                    ApplyToolAt(gridPos, false);
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

        // Mouse motion for drag painting and middle-mouse pan
        if (@event is InputEventMouseMotion motion)
        {
            if (_isPanning)
            {
                _camera.Position -= motion.Relative / _camera.Zoom.X;
                ClampCamera();
                GetViewport().SetInputAsHandled();
                return;
            }

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
        int x = (int)Mathf.Floor(local.X / GridRenderer.CellSize);
        int y = (int)Mathf.Floor(local.Y / GridRenderer.CellSize);
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

    private void ApplyToolAt(GridPos pos, bool isErase)
    {
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
                        _editorState.AddBlock(_currentBlock, mirrorSlot, new GridPos(mx, my));
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
    }

    private void CenterCamera()
    {
        var gridPixelW = _mapWidth * GridRenderer.CellSize;
        var gridPixelH = _mapHeight * GridRenderer.CellSize;
        _camera.Position = new Vector2(gridPixelW * 0.5f, gridPixelH * 0.5f);
    }

    private void ClampCamera()
    {
        var gridPixelW = _mapWidth * GridRenderer.CellSize;
        var gridPixelH = _mapHeight * GridRenderer.CellSize;
        var halfView = GetViewportRect().Size / (2f * _camera.Zoom);

        float minX = halfView.X * 2 >= gridPixelW ? gridPixelW * 0.5f : halfView.X;
        float maxX = halfView.X * 2 >= gridPixelW ? gridPixelW * 0.5f : gridPixelW - halfView.X;
        float minY = halfView.Y * 2 >= gridPixelH ? gridPixelH * 0.5f : halfView.Y;
        float maxY = halfView.Y * 2 >= gridPixelH ? gridPixelH * 0.5f : gridPixelH - halfView.Y;

        _camera.Position = new Vector2(
            Mathf.Clamp(_camera.Position.X, minX, maxX),
            Mathf.Clamp(_camera.Position.Y, minY, maxY)
        );
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
            units.Add(new UnitEntry(block.Pos.X, block.Pos.Y, block.Type, block.PlayerId));

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
            EnsurePlayerExists(u.SlotId);
            _editorState.AddBlock(u.Type, u.SlotId, new GridPos(u.X, u.Y));
        }

        // Update toolbar
        _toolbar.SetMapName(_mapName);
        _toolbar.SetSlotCount(_slotCount);

        CenterCamera();
        RefreshRenderer();
        RefreshGuides();
    }

    // --- Toolbar event handlers ---

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
    private void OnBlockSelected(BlockType block)
    {
        _currentBlock = block;
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
        float centerX = gridW * 0.5f;
        float centerY = gridH * 0.5f;

        // Vertical center line
        DrawLine(new Vector2(centerX, 0), new Vector2(centerX, gridH), GuideColor, 2f);
        // Horizontal center line
        DrawLine(new Vector2(0, centerY), new Vector2(gridW, centerY), GuideColor, 2f);
    }
}
