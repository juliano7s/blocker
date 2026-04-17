using Blocker.Game.Audio;
using Blocker.Game.Config;
using Blocker.Game.Input;
using Blocker.Game.Net;
using Blocker.Game.Rendering;
using Blocker.Game.UI;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;
using Blocker.Simulation.Systems;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Entry point. Loads map, creates simulation state, wires up rendering and input.
/// </summary>
public partial class GameManager : Node2D
{
	[Export] public GameConfig Config { get; set; } = null!;

	private GridRenderer _gridRenderer = null!;
	private CameraController _camera = null!;
	private SelectionManager _selectionManager = null!;
	private TickRunner _tickRunner = null!;
	private HudOverlay _hud = null!;
	private HudBar _hudBar = null!;
	private SpawnToggles _spawnToggles = null!;
	private PostProcessingManager _postProcessing = null!;
	private EffectManager _effectManager = null!;
	private AudioManager _audioManager = null!;
	private readonly HashSet<int> _currentSelectedIds = new();

	// Game-state we hold so rematch can rebuild GameLaunchData on its own.
	private GameState _gameState = null!;
	private MapData? _launchMap;
	private List<SlotAssignment>? _launchAssignments;
	private MultiplayerSessionState? _launchSession;
	private LockstepCoordinator? _coord;
	private bool _gameOverShown;

	// MP rematch / scene-handoff handler — held as a field so _ExitTree can
	// detach it from the long-lived RelayClient when the scene is freed.
	private Action<RoomStatePayload>? _mpRoomStateHandler;

	public override void _Ready()
	{
		// Initialize config and simulation constants
		Config ??= GameConfig.CreateDefault();
		Constants.Initialize(Config.ToSimulationConfig());

		// Load map from GameLaunchData (set by SlotConfigScreen for both SP and MP)
		if (GameLaunchData.MapData == null || GameLaunchData.Assignments == null)
			throw new System.Exception("GameLaunchData.MapData and Assignments must be set before launching the game.");

		GameState gameState = MapLoader.Load(GameLaunchData.MapData, GameLaunchData.Assignments);
		GD.Print($"Map loaded from launcher: {GameLaunchData.MapData.Name} " +
				 $"{gameState.Grid.Width}x{gameState.Grid.Height}, {gameState.Blocks.Count} blocks");
		// Stash for rematch — _Ready clears the static slots so a fresh launch
		// after returning to MainMenu doesn't accidentally inherit stale data.
		_launchMap = GameLaunchData.MapData;
		_launchAssignments = GameLaunchData.Assignments;
		GameLaunchData.MapData = null;
		GameLaunchData.Assignments = null;
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

		// Tick runner: multiplayer when a session was handed in, single-player otherwise.
		if (GameLaunchData.MultiplayerSession is { } mp)
		{
			_launchSession = mp;
			// Drop the SP TickRunner so it doesn't double-tick the simulation.
			var spRunner = GetNodeOrNull<TickRunner>("TickRunner");
			if (spRunner != null) spRunner.QueueFree();

			_coord = new LockstepCoordinator(mp.LocalPlayerId, gameState, mp.Relay, mp.ActivePlayerIds);
			var mpRunner = new MultiplayerTickRunner { Name = "MultiplayerTickRunner", TickRate = 12 };
			AddChild(mpRunner);
			mpRunner.Initialize(_coord, mp.Relay, gameState);
			_gridRenderer.SetTickInterval((float)mpRunner.TickInterval);
			_selectionManager.ControllingPlayer = mp.LocalPlayerId;
			// Tab hot-seat is for single-player only — in MP it desyncs the sim.
			_selectionManager.AllowHotSeatSwitch = false;
			_selectionManager.SetCommandSink(mpRunner);
			_coord.StartGame();

			_coord.GameEnded += (winner) =>
			{
				GD.Print($"Game over. Winner: team {winner}");
				CallDeferred(nameof(ShowGameOverOverlayDeferred), winner);
			};
			_coord.DesyncDetected += () => GD.PrintErr("DESYNC DETECTED");
		}
		else
		{
			_tickRunner = GetNode<TickRunner>("TickRunner");
			_tickRunner.SetGameState(gameState);
			_tickRunner.SetSelectionManager(_selectionManager);
			_gridRenderer.SetTickInterval((float)_tickRunner.TickInterval);
		}

		// Set up HUD
		_hud = new HudOverlay();
		AddChild(_hud);
		_hud.SetGameState(gameState);
		_hud.SetConfig(Config);
		_hud.SetControllingPlayer(0);
		_hud.SetSurrenderHandler(() => _selectionManager.SubmitSurrender());

		// Set up bottom HUD bar with minimap
		_hudBar = new HudBar();
		AddChild(_hudBar);
		_hudBar.SetGameState(gameState);
		_hudBar.SetConfig(Config);
		_hudBar.MinimapCameraJump += pos => _camera.JumpTo(pos);
		_hudBar.UnitClicked += (blockId, shiftHeld) =>
		{
			if (shiftHeld)
				_selectionManager.DeselectBlockById(blockId);
			else
				_selectionManager.SelectBlockById(blockId);
		};
		_hudBar.CommandClicked += action => _selectionManager.IssueCommand(action);
		_hudBar.BlueprintClicked += type => _selectionManager.ToggleBlueprintMode(type);

		// Set up floating spawn toggles (top-right of game area)
		_spawnToggles = new SpawnToggles();
		var togglesLayer = new CanvasLayer { Layer = 10 };
		AddChild(togglesLayer);
		var togglesAnchor = new Control();
		togglesAnchor.SetAnchorsPreset(Control.LayoutPreset.TopRight);
		togglesAnchor.OffsetLeft = -70;
		togglesAnchor.OffsetTop = HudStyles.TopBarHeight + 20;
		togglesAnchor.OffsetRight = -20;
		togglesAnchor.OffsetBottom = HudStyles.TopBarHeight + 130;
		togglesLayer.AddChild(togglesAnchor);
		togglesAnchor.AddChild(_spawnToggles);

		// Tell camera about HUD coverage so it offsets the visible area
		_camera.SetHudInsets(HudStyles.TopBarHeight, HudStyles.MinimapSize + HudStyles.BottomPanelMargin);
		_camera.CenterOnGrid();

		// Set up effect manager
		_effectManager = GetNode<EffectManager>("EffectManager");
		_effectManager.SetGameState(gameState);
		_effectManager.SetConfig(Config);

		// Wire GridRenderer → EffectManager for death explosion callbacks
		_gridRenderer.SetEffectManager(_effectManager);

		// Set up audio manager
		_audioManager = GetNode<AudioManager>("AudioManager");
		_audioManager.SetGameState(gameState);

		// Set up post-processing
		_postProcessing = GetNode<PostProcessingManager>("PostProcessing");
		_postProcessing.SetGameState(gameState);
		_postProcessing.SetGridRenderer(_gridRenderer);

		// Set background color
		RenderingServer.SetDefaultClearColor(Config.BackgroundColor);

		// Stash gameState for game-over polling and rematch reconstruction.
		_gameState = gameState;

		// Clear after consuming so a return-to-menu round trip starts fresh.
		GameLaunchData.MultiplayerSession = null;

		// Exit button handled by HudOverlay
	}

	public override void _Process(double delta)
	{
		// Keep HUD, effects, and audio in sync with controlling player
		_hud.SetControllingPlayer(_selectionManager.ControllingPlayer);
		_effectManager.SetControllingPlayer(_selectionManager.ControllingPlayer);
		_audioManager.SetControllingPlayer(_selectionManager.ControllingPlayer);

		// Pass selected IDs to renderer so selection border tracks visual position
		_gridRenderer.SetSelectedIds(_selectionManager.SelectedBlocks);

		// Notify effect manager and audio manager of selection changes
		_currentSelectedIds.Clear();
		foreach (var b in _selectionManager.SelectedBlocks)
			_currentSelectedIds.Add(b.Id);
		_effectManager.OnSelectionChanged(_currentSelectedIds);
		_audioManager.OnSelectionChanged(_currentSelectedIds);

		// Feed camera position and visible area to HudBar minimap
		var viewSize = GetViewportRect().Size / _camera.Zoom;
		_hudBar.SetCameraView(_camera.Position, viewSize);

		// Update HUD with selection state
		_hudBar.SetSelection(_selectionManager.SelectedBlocks);
		_hudBar.SetControlGroups(_selectionManager.ControlGroups);

		// SP game-over polling. MP path uses _coord.GameEnded directly. We poll
		// here for the SP path because TickRunner doesn't expose a hook — and
		// EliminationSystem.GetWinningTeam is cheap (one O(N) scan).
		if (!_gameOverShown && _coord == null && _gameState != null)
		{
			var winner = EliminationSystem.GetWinningTeam(_gameState);
			if (winner is int t)
				ShowGameOverOverlayDeferred(t);
		}
	}

	private void ShowGameOverOverlayDeferred(int winningTeam)
	{
		if (_gameOverShown) return;
		_gameOverShown = true;

		var overlay = new GameOverOverlay();
		int localPid = _selectionManager?.ControllingPlayer ?? 0;

		// Play win/loss fanfare
		var localPlayer = _gameState.Players.Find(p => p.Id == localPid);
		bool localWon = localPlayer != null && localPlayer.TeamId == winningTeam;
		_audioManager.PlayGameEndFanfare(localWon);
		var (title, subtitle, accent) = GameOverOverlay.BuildWinnerLabels(
			winningTeam, localPid, _gameState, Config);

		bool isMp = _launchSession != null;
		bool isHost = isMp && _launchSession!.LocalPlayerId == 0; // host always owns slot 0
		bool showRematch = !isMp || isHost; // SP always; MP only host
		string rematchLabel = "Rematch";

		overlay.Configure(
			title, subtitle, accent,
			showRematch, rematchLabel,
			onRematch: isMp ? OnMpRematchPressed : OnSpRematchPressed,
			onLeave: OnLeavePressed);

		AddChild(overlay);

		// MP joiner / non-host: still wire RoomState so we follow the host into
		// the rematch lobby. The host wires it inside OnMpRematchPressed (after
		// the SendRematch click) so it doesn't accidentally trigger on stale
		// state during normal play.
		if (isMp && !isHost)
			SubscribeMpRematchHandlers();
	}

	private void OnSpRematchPressed()
	{
		// Re-set the launch slots and reload the scene. _Ready will pick up the
		// MapData/Assignments and rebuild the simulation from scratch.
		if (_launchMap == null || _launchAssignments == null) return;
		GameLaunchData.MapData = _launchMap;
		GameLaunchData.Assignments = _launchAssignments;
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnMpRematchPressed()
	{
		if (_launchSession == null) return;
		// Subscribe BEFORE sending rematch — relay broadcasts on the same
		// connection round-trip and we don't want to race the response.
		SubscribeMpRematchHandlers();
		_launchSession.Relay.SendRematch();
	}

	private void OnLeavePressed()
	{
		// MP: tear down the relay so a return to MultiplayerMenu starts fresh.
		// SP: just bail to the main menu.
		if (_launchSession != null)
		{
			try { _launchSession.Relay.SendLeaveRoom(); } catch { }
			_launchSession.Relay.Dispose();
			MultiplayerLaunchData.Relay = null;
		}
		GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
	}

	private void SubscribeMpRematchHandlers()
	{
		if (_launchSession == null) return;
		if (_mpRoomStateHandler != null) return; // idempotent

		var relay = _launchSession.Relay;
		_mpRoomStateHandler = (rs) =>
		{
			// Stash so the new SlotConfigScreen can seed its lobby panel
			// immediately without waiting for a second broadcast.
			MultiplayerLaunchData.PendingRoomState = rs;
			CallDeferred(nameof(OnMpRoomStateAfterGameEnded));
		};
		relay.RoomStateReceived += _mpRoomStateHandler;
	}

	private void OnMpRoomStateAfterGameEnded()
	{
		// Rematch arrived (relay reset to Lobby and broadcast RoomState). Hand
		// the user back to the lobby screen with the existing relay attached.
		if (_launchSession == null) return;
		bool isHost = _launchSession.LocalPlayerId == 0;

		MultiplayerLaunchData.Relay = _launchSession.Relay;
		MultiplayerLaunchData.Intent = isHost ? MultiplayerIntent.Host : MultiplayerIntent.Join;
		MultiplayerLaunchData.RematchReattach = isHost;

		// Detach our coordinator BEFORE the scene change — otherwise it stays
		// subscribed to relay events alongside the new coordinator the next
		// game spawns, and the dead coord will silently corrupt _buffers.
		_coord?.Detach();
		_coord = null;

		GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
	}

	public override void _ExitTree()
	{
		// Detach from the long-lived RelayClient so its background ReceiveLoop
		// doesn't fire our handlers on a freed scene.
		_coord?.Detach();
		_coord = null;
		if (_launchSession != null && _mpRoomStateHandler != null)
		{
			_launchSession.Relay.RoomStateReceived -= _mpRoomStateHandler;
			_mpRoomStateHandler = null;
		}
	}
}
