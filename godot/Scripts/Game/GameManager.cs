using Blocker.Game.Input;
using Blocker.Game.Rendering;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Entry point. Loads map, creates simulation state, wires up rendering and input.
/// </summary>
public partial class GameManager : Node2D
{
	[Export] public string MapPath = "res://../maps/test-small.txt";

	private GridRenderer _gridRenderer = null!;
	private CameraController _camera = null!;
	private SelectionManager _selectionManager = null!;
	private TickRunner _tickRunner = null!;
	private HudOverlay _hud = null!;

	public override void _Ready()
	{
		// Load map
		var absolutePath = ProjectSettings.GlobalizePath(MapPath);
		if (!Godot.FileAccess.FileExists(MapPath) && !System.IO.File.Exists(absolutePath))
		{
			// Try finding it relative to the project
			absolutePath = System.IO.Path.Combine(
				System.IO.Path.GetDirectoryName(ProjectSettings.GlobalizePath("res://"))!,
				"..", "maps", "test-small.txt"
			);
		}

		GD.Print($"Loading map from: {absolutePath}");
		var gameState = MapLoader.LoadFromFile(absolutePath);
		GD.Print($"Map loaded: {gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks, {gameState.Players.Count} players");
		foreach (var block in gameState.Blocks)
			GD.Print($"  Block id={block.Id} {block.Type} P{block.PlayerId} at {block.Pos}");

		// Set up grid renderer
		_gridRenderer = GetNode<GridRenderer>("GridRenderer");
		_gridRenderer.SetGameState(gameState);

		// Set up camera
		_camera = GetNode<CameraController>("Camera");
		_camera.SetGridSize(gameState.Grid.Width, gameState.Grid.Height);

		// Set up selection
		_selectionManager = GetNode<SelectionManager>("SelectionManager");
		_selectionManager.SetGameState(gameState);

		// Set up tick runner
		_tickRunner = GetNode<TickRunner>("TickRunner");
		_tickRunner.SetGameState(gameState);
		_tickRunner.SetSelectionManager(_selectionManager);

		// Set up HUD
		_hud = new HudOverlay();
		AddChild(_hud);
		_hud.SetGameState(gameState);
		_hud.SetControllingPlayer(0);

		// Set background color
		RenderingServer.SetDefaultClearColor(new Color(0.08f, 0.08f, 0.10f));
	}

	public override void _Process(double delta)
	{
		// Keep HUD in sync with controlling player and selection
		_hud.SetControllingPlayer(_selectionManager.ControllingPlayer);
		_hud.SetSelectedBlocks(_selectionManager.SelectedBlocks);
	}
}
