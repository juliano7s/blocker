using Blocker.Game.Config;
using Blocker.Game.Input;
using Blocker.Game.Rendering;
using Blocker.Game.UI;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Entry point. Loads map, creates simulation state, wires up rendering and input.
/// </summary>
public partial class GameManager : Node2D
{
	[Export] public string MapPath = "res://../maps/test-small.txt";
	[Export] public GameConfig Config { get; set; } = null!;

	private GridRenderer _gridRenderer = null!;
	private CameraController _camera = null!;
	private SelectionManager _selectionManager = null!;
	private TickRunner _tickRunner = null!;
	private HudOverlay _hud = null!;
	private HudBar _hudBar = null!;
	private PostProcessingManager _postProcessing = null!;
	private EffectManager _effectManager = null!;
	private readonly HashSet<int> _currentSelectedIds = new();

	public override void _Ready()
	{
		// Initialize config and simulation constants
		Config ??= GameConfig.CreateDefault();
		Constants.Initialize(Config.ToSimulationConfig());

		// Load map — either from GameLaunchData (Play vs AI flow) or legacy file
		GameState gameState;
		if (GameLaunchData.MapData != null && GameLaunchData.Assignments != null)
		{
			gameState = MapLoader.Load(GameLaunchData.MapData, GameLaunchData.Assignments);
			GD.Print($"Map loaded from launcher: {GameLaunchData.MapData.Name} " +
					 $"{gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks");
			GameLaunchData.MapData = null;
			GameLaunchData.Assignments = null;
		}
		else
		{
			var absolutePath = ProjectSettings.GlobalizePath(MapPath);
			if (!Godot.FileAccess.FileExists(MapPath) && !System.IO.File.Exists(absolutePath))
			{
				absolutePath = System.IO.Path.Combine(
					System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://"))!,
					"..", "maps", "test-small.txt"
				);
			}
			GD.Print($"Loading map from: {absolutePath}");
			gameState = MapLoader.LoadFromFile(absolutePath);
		}
		GD.Print($"Map loaded: {gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks, {gameState.Players.Count} players");
		foreach (var block in gameState.Blocks)
			GD.Print($"  Block id={block.Id} {block.Type} P{block.PlayerId} at {block.Pos}");

		// Set up grid renderer
		_gridRenderer = GetNode<GridRenderer>("GridRenderer");
		_gridRenderer.SetGameState(gameState);
		_gridRenderer.SetConfig(Config);

		// Set up camera
		_camera = GetNode<CameraController>("Camera");
		_camera.SetGridSize(gameState.Grid.Width, gameState.Grid.Height);

		// Set up selection
		_selectionManager = GetNode<SelectionManager>("SelectionManager");
		_selectionManager.SetGameState(gameState);
		_selectionManager.SetConfig(Config);

		// Set up tick runner
		_tickRunner = GetNode<TickRunner>("TickRunner");
		_tickRunner.SetGameState(gameState);
		_tickRunner.SetSelectionManager(_selectionManager);
		_gridRenderer.SetTickInterval((float)_tickRunner.TickInterval);

		// Set up HUD
		_hud = new HudOverlay();
		AddChild(_hud);
		_hud.SetGameState(gameState);
		_hud.SetConfig(Config);
		_hud.SetControllingPlayer(0);

		// Set up bottom HUD bar with minimap
		_hudBar = new HudBar();
		AddChild(_hudBar);
		_hudBar.SetGameState(gameState);
		_hudBar.SetConfig(Config);
		_hudBar.MinimapCameraJump += pos => _camera.JumpTo(pos);

		// Tell camera about HUD coverage so it offsets the visible area
		// Top bar: 32px bar + 6px ratio bar = 38px. Bottom bar: 150px.
		_camera.SetHudInsets(38f, 150f);
		_camera.CenterOnGrid();

		// Set up effect manager
		_effectManager = GetNode<EffectManager>("EffectManager");
		_effectManager.SetGameState(gameState);
		_effectManager.SetConfig(Config);

		// Set up post-processing
		_postProcessing = GetNode<PostProcessingManager>("PostProcessing");
		_postProcessing.SetGameState(gameState);
		_postProcessing.SetGridRenderer(_gridRenderer);

		// Set background color
		RenderingServer.SetDefaultClearColor(Config.BackgroundColor);

		// Exit button handled by HudOverlay
	}

	public override void _Process(double delta)
	{
		// Keep HUD in sync with controlling player and selection
		_hud.SetControllingPlayer(_selectionManager.ControllingPlayer);

		// Pass selected IDs to renderer so selection border tracks visual position
		_gridRenderer.SetSelectedIds(_selectionManager.SelectedBlocks);

		// Notify effect manager of selection changes (fires one-shot SelectSquares)
		_currentSelectedIds.Clear();
		foreach (var b in _selectionManager.SelectedBlocks)
			_currentSelectedIds.Add(b.Id);
		_effectManager.OnSelectionChanged(_currentSelectedIds);

		// Feed camera position and visible area to HudBar minimap
		var viewSize = GetViewportRect().Size / _camera.Zoom;
		_hudBar.SetCameraView(_camera.Position, viewSize);
	}
}
