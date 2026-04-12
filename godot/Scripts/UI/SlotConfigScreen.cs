using Blocker.Game.Maps;
using Blocker.Game.Net;
using Blocker.Simulation.Net;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class SlotConfigScreen : Control
{
	private MapData? _mapData;
	private readonly Dictionary<int, string> _slotAssignments = new();
	private VBoxContainer _slotContainer = null!;
	private int _playerSlot = 0;

	// Multiplayer state (host + join share this).
	private RelayClient? _relay;
	private int _pendingLocalId;
	private int[] _pendingActiveIds = Array.Empty<int>();
	// Cached so OnGameStarted can build SlotAssignments with correct TeamIds
	// without needing a second wire round-trip.
	private RoomStatePayload? _latestRoomState;

	// Host-only state.
	private Label? _hostStatusLabel;
	private Button? _hostStartBtn;
	private Button? _hostCreateBtn;
	private OptionButton? _hostGameModeOption;
	private VBoxContainer? _hostLobbyContainer;
	private string _hostRoomCode = "";
	private int _hostLatestFilledSlots;
	private string _hostPendingError = "";
	private GameMode _hostSelectedMode = GameMode.Ffa;
	private bool _hostRoomCreated;

	// Join-only state.
	private Label? _joinStatusLabel;
	private VBoxContainer? _joinLobbyContainer;
	private string _joinMapName = "";
	private string _pendingError = "";

	// Relay event handlers — assigned to fields so _ExitTree can unsubscribe
	// when the scene is freed. Anonymous lambdas can't be detached, which
	// strands them on the long-lived RelayClient and fires them against a
	// freed scene the next time RoomState/GameStarted broadcasts (rematch
	// flow exposes this — M1 was lucky because nothing fired post-Start).
	private Action<RoomStatePayload>? _relayRoomStateHandler;
	private Action<int, int[]>? _relayGameStartedHandler;
	private Action<ErrorCode>? _relayErrorHandler;

	public override void _Ready()
	{
		// Join mode skips MapSelect entirely — the map name arrives on RoomState.
		var intent = MultiplayerLaunchData.Intent;
		if (intent == MultiplayerIntent.Join)
		{
			SetupJoinMode();
			return;
		}

		if (MapSelection.SelectedMapFileName == null)
		{
			GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
			return;
		}

		_mapData = MapFileManager.Load(MapSelection.SelectedMapFileName);
		if (_mapData == null)
		{
			GD.PrintErr("Failed to load selected map");
			GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
			return;
		}

		if (intent == MultiplayerIntent.Host)
		{
			SetupHostMode();
			return;
		}

		var vbox = new VBoxContainer
		{
			AnchorLeft = 0.1f, AnchorRight = 0.9f,
			AnchorTop = 0.05f, AnchorBottom = 0.95f,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.Both
		};
		vbox.AddThemeConstantOverride("separation", 12);
		AddChild(vbox);

		var header = new HBoxContainer();
		vbox.AddChild(header);

		var backBtn = new Button { Text = "< Back" };
		backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
		header.AddChild(backBtn);

		var title = new Label
		{
			Text = $"Configure: {_mapData.Name}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);
		header.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });

		var info = new Label { Text = $"Size: {_mapData.Width}x{_mapData.Height} — {_mapData.SlotCount} slots" };
		vbox.AddChild(info);

		vbox.AddChild(new HSeparator());

		_slotContainer = new VBoxContainer();
		_slotContainer.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(_slotContainer);

		for (int i = 0; i < _mapData.SlotCount; i++)
			_slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
		RebuildSlotList();

		vbox.AddChild(new HSeparator());

		var startBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50) };
		startBtn.Pressed += OnStartPressed;
		vbox.AddChild(startBtn);
	}

	private void RebuildSlotList()
	{
		foreach (var child in _slotContainer.GetChildren())
			child.QueueFree();

		for (int i = 0; i < _mapData!.SlotCount; i++)
		{
			var row = new HBoxContainer();

			var label = new Label
			{
				Text = $"Slot {i + 1}:",
				CustomMinimumSize = new Vector2(80, 0)
			};
			row.AddChild(label);

			var btn = new Button
			{
				Text = _slotAssignments[i],
				CustomMinimumSize = new Vector2(200, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			int slot = i;
			btn.Pressed += () => ToggleSlotAssignment(slot);
			row.AddChild(btn);

			_slotContainer.AddChild(row);
		}
	}

	private void ToggleSlotAssignment(int slot)
	{
		if (_slotAssignments[slot] == "Player")
		{
			_slotAssignments[slot] = "AI (inactive)";
		}
		else
		{
			foreach (var key in _slotAssignments.Keys.ToList())
			{
				if (_slotAssignments[key] == "Player")
					_slotAssignments[key] = "AI (inactive)";
			}
			_slotAssignments[slot] = "Player";
			_playerSlot = slot;
		}
		RebuildSlotList();
	}

	private void OnStartPressed()
	{
		if (_mapData == null) return;

		var assignments = new List<SlotAssignment>();
		int nextPlayerId = 1; // Player gets 0, AI gets sequential 1, 2, 3...
		for (int i = 0; i < _mapData.SlotCount; i++)
		{
			if (_slotAssignments[i] == "Player")
				assignments.Add(new SlotAssignment(i, 0));
			else
				assignments.Add(new SlotAssignment(i, nextPlayerId++));
		}

		GameLaunchData.MapData = _mapData;
		GameLaunchData.Assignments = assignments;
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	// ------------------------------------------------------------------
	// Multiplayer host mode
	// ------------------------------------------------------------------

	private void SetupHostMode()
	{
		_relay = MultiplayerLaunchData.Relay;
		if (_relay == null || _mapData == null)
		{
			GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
			return;
		}

		var vbox = new VBoxContainer
		{
			AnchorLeft = 0.1f, AnchorRight = 0.9f,
			AnchorTop = 0.05f, AnchorBottom = 0.95f
		};
		vbox.AddThemeConstantOverride("separation", 12);
		AddChild(vbox);

		var header = new HBoxContainer();
		vbox.AddChild(header);
		var backBtn = new Button { Text = "< Back" };
		backBtn.Pressed += () =>
		{
			// Tear down the connection entirely — re-entering MultiplayerMenu
			// spins up a fresh RelayClient. Keeping the old one around leaks
			// background ReceiveLoop/SendLoop tasks against a dead socket.
			_relay?.SendLeaveRoom();
			_relay?.Dispose();
			MultiplayerLaunchData.Relay = null;
			GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
		};
		header.AddChild(backBtn);
		var title = new Label
		{
			Text = $"Host: {_mapData.Name} ({_mapData.SlotCount} slots)",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);

		// Rematch reattach: the relay already has a Lobby room from the previous
		// game, so the Create Room step would double-create. Skip the picker and
		// land directly on the lobby panel — the next RoomState fan-out from the
		// rematch will populate it.
		bool rematchReattach = MultiplayerLaunchData.RematchReattach;
		MultiplayerLaunchData.RematchReattach = false;

		if (!rematchReattach)
		{
			// Pre-creation panel: GameMode picker + Create button.
			// Locked once the room is created (creating bakes mode/slot count into
			// the room's TeamId assignments and the relay would have to tear it down
			// to change them).
			var modeRow = new HBoxContainer();
			modeRow.AddChild(new Label { Text = "Game mode:" });
			_hostGameModeOption = new OptionButton();
			_hostGameModeOption.AddItem("Free-for-all", (int)GameMode.Ffa);
			// Teams requires an even slot count: 2 → 1v1, 4 → 2v2, 6 → 3v3.
			// Leave the option present but disabled for odd-slot maps so the host
			// can see *why* it's not allowed instead of silently missing it.
			_hostGameModeOption.AddItem("Teams", (int)GameMode.Teams);
			if (_mapData.SlotCount % 2 != 0)
				_hostGameModeOption.SetItemDisabled(1, true);
			_hostGameModeOption.Selected = 0;
			_hostGameModeOption.ItemSelected += (idx) =>
				_hostSelectedMode = (GameMode)_hostGameModeOption.GetItemId((int)idx);
			modeRow.AddChild(_hostGameModeOption);
			vbox.AddChild(modeRow);

			_hostCreateBtn = new Button { Text = "Create Room", CustomMinimumSize = new Vector2(0, 40) };
			_hostCreateBtn.Pressed += OnHostCreateRoomPressed;
			vbox.AddChild(_hostCreateBtn);

			vbox.AddChild(new HSeparator());
		}
		else
		{
			// Already room-bound from the previous game; lock state accordingly.
			_hostRoomCreated = true;
		}

		// Lobby panel — populated after RoomState arrives.
		_hostStatusLabel = new Label
		{
			Text = rematchReattach
				? "Rematch lobby — waiting for player slots…"
				: "Pick a game mode and click Create Room."
		};
		vbox.AddChild(_hostStatusLabel);

		_hostLobbyContainer = new VBoxContainer();
		_hostLobbyContainer.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(_hostLobbyContainer);

		vbox.AddChild(new HSeparator());
		_hostStartBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
		_hostStartBtn.Pressed += OnHostStartPressed;
		vbox.AddChild(_hostStartBtn);

		// Relay events fire from the receive loop on a background thread. Capture
		// the payload into fields and bounce to the main thread via CallDeferred
		// with a 0-arg handler — Variant can't wrap arbitrary records.
		_relayRoomStateHandler = (rs) =>
		{
			_latestRoomState = rs;
			_hostRoomCode = rs.Code;
			_hostLatestFilledSlots = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
			CallDeferred(nameof(OnHostRoomStateDeferred));
		};
		_relayGameStartedHandler = (localId, activeIds) =>
		{
			_pendingLocalId = localId;
			_pendingActiveIds = activeIds;
			CallDeferred(nameof(OnHostGameStartedDeferred));
		};
		_relayErrorHandler = (code) =>
		{
			_hostPendingError = code.ToString();
			CallDeferred(nameof(OnHostErrorDeferred));
		};
		_relay.RoomStateReceived += _relayRoomStateHandler;
		_relay.GameStarted += _relayGameStartedHandler;
		_relay.ServerError += _relayErrorHandler;

		var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
		drain.Timeout += () => _relay.DrainInbound();
		AddChild(drain);

		// If we're reattaching after a rematch, seed the lobby panel from the
		// stashed RoomState so the host doesn't see an empty list.
		if (MultiplayerLaunchData.PendingRoomState is { } pending)
		{
			_latestRoomState = pending;
			_hostRoomCode = pending.Code;
			_hostLatestFilledSlots = pending.Slots.Count(s => !s.IsOpen && !s.IsClosed);
			MultiplayerLaunchData.PendingRoomState = null;
			OnHostRoomStateDeferred();
		}
	}

	private void OnHostCreateRoomPressed()
	{
		if (_relay == null || _mapData == null || _hostRoomCreated) return;

		// Lock the picker — every peer derives team from (slot, mode), so changing
		// mode after slots have been claimed would silently rewrite teams under
		// joiners. Easier to lock than to broadcast a re-team event.
		_hostRoomCreated = true;
		_hostGameModeOption!.Disabled = true;
		_hostCreateBtn!.Disabled = true;
		_hostStatusLabel!.Text = "Creating room…";

		// Map blob is opaque to the relay; joiners load by name for M1/M2.
		// We send the *file name* (e.g. "Lanes.json"), not the display name from the
		// JSON metadata — joiners look the file up on disk by that exact name.
		var mapFileName = MapSelection.SelectedMapFileName ?? (_mapData.Name + ".json");
		var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
		_relay.SendCreateRoom((byte)_mapData.SlotCount, _hostSelectedMode, mapFileName, mapBlob);
	}

	private void OnHostRoomStateDeferred()
	{
		if (_mapData == null || _latestRoomState == null) return;
		int total = _mapData.SlotCount;
		bool ready = _hostLatestFilledSlots == total;
		_hostStatusLabel!.Text = ready
			? $"Room code: {_hostRoomCode} — all {total} slots filled, ready to start"
			: $"Room code: {_hostRoomCode} — waiting ({_hostLatestFilledSlots}/{total})…";
		_hostStartBtn!.Disabled = !ready;
		RebuildLobbyList(_hostLobbyContainer!, _latestRoomState);
	}

	private void OnHostErrorDeferred()
	{
		if (_hostStatusLabel != null)
			_hostStatusLabel.Text = $"Error: {_hostPendingError}";
		if (_hostStartBtn != null)
			_hostStartBtn.Disabled = true;
	}

	private void OnHostStartPressed() => _relay!.SendStartGame();

	private void OnHostGameStartedDeferred() => LaunchMultiplayerGame(_mapData);

	// ------------------------------------------------------------------
	// Multiplayer join mode
	// ------------------------------------------------------------------

	private void SetupJoinMode()
	{
		_relay = MultiplayerLaunchData.Relay;
		if (_relay == null)
		{
			GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
			return;
		}

		var vbox = new VBoxContainer
		{
			AnchorLeft = 0.1f, AnchorRight = 0.9f,
			AnchorTop = 0.05f, AnchorBottom = 0.95f
		};
		vbox.AddThemeConstantOverride("separation", 12);
		AddChild(vbox);

		var header = new HBoxContainer();
		vbox.AddChild(header);
		var backBtn = new Button { Text = "< Back" };
		backBtn.Pressed += () =>
		{
			_relay?.SendLeaveRoom();
			_relay?.Dispose();
			MultiplayerLaunchData.Relay = null;
			GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
		};
		header.AddChild(backBtn);
		var title = new Label
		{
			Text = $"Joined: {MultiplayerLaunchData.JoinCode}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);

		_joinStatusLabel = new Label { Text = "Waiting for host to start…" };
		vbox.AddChild(_joinStatusLabel);

		_joinLobbyContainer = new VBoxContainer();
		_joinLobbyContainer.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(_joinLobbyContainer);

		_relayRoomStateHandler = (rs) =>
		{
			_latestRoomState = rs;
			_joinMapName = rs.MapName;
			CallDeferred(nameof(OnJoinRoomStateDeferred));
		};
		_relayGameStartedHandler = (localId, activeIds) =>
		{
			_pendingLocalId = localId;
			_pendingActiveIds = activeIds;
			CallDeferred(nameof(OnJoinGameStartedDeferred));
		};
		_relayErrorHandler = (code) =>
		{
			_pendingError = code.ToString();
			CallDeferred(nameof(OnJoinErrorDeferred));
		};
		_relay.RoomStateReceived += _relayRoomStateHandler;
		_relay.GameStarted += _relayGameStartedHandler;
		_relay.ServerError += _relayErrorHandler;

		var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
		drain.Timeout += () => _relay.DrainInbound();
		AddChild(drain);

		// Seed from stashed RoomState on rematch reattach.
		if (MultiplayerLaunchData.PendingRoomState is { } pending)
		{
			_latestRoomState = pending;
			_joinMapName = pending.MapName;
			MultiplayerLaunchData.PendingRoomState = null;
			OnJoinRoomStateDeferred();
		}
	}

	private void OnJoinRoomStateDeferred()
	{
		if (_latestRoomState == null) return;
		var rs = _latestRoomState;
		int filled = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
		string modeLabel = rs.GameMode == GameMode.Teams ? "Teams" : "FFA";
		_joinStatusLabel!.Text =
			$"Map: {rs.MapName}  |  Mode: {modeLabel}  |  {filled}/{rs.Slots.Length} players — waiting for host…";
		RebuildLobbyList(_joinLobbyContainer!, rs);
	}

	private void OnJoinErrorDeferred() => _joinStatusLabel!.Text = $"Error: {_pendingError}";

	private void OnJoinGameStartedDeferred()
	{
		// M1/M2: the joiner loads the same map by name from local disk. Host and
		// joiner must have identical map files on disk today (map sharing is
		// deferred per jjack's curated-maps plan).
		var md = MapFileManager.Load(_joinMapName);
		if (md == null)
		{
			_joinStatusLabel!.Text = $"Map '{_joinMapName}' not found locally.";
			return;
		}
		LaunchMultiplayerGame(md);
	}

	// ------------------------------------------------------------------
	// Shared multiplayer helpers
	// ------------------------------------------------------------------

	public override void _ExitTree()
	{
		// Detach from the long-lived RelayClient before this scene is freed.
		// Without this, lambdas captured in SetupHostMode/SetupJoinMode keep
		// firing on the next RoomState/GameStarted broadcast and try to call
		// CallDeferred on a freed node — exposed by the rematch flow.
		if (_relay != null)
		{
			if (_relayRoomStateHandler != null) _relay.RoomStateReceived -= _relayRoomStateHandler;
			if (_relayGameStartedHandler != null) _relay.GameStarted -= _relayGameStartedHandler;
			if (_relayErrorHandler != null) _relay.ServerError -= _relayErrorHandler;
		}
	}

	/// <summary>
	/// Build slot assignments using the GameMode from the latest RoomState
	/// and hand off to Main.tscn. Both host and joiner share this code path
	/// so the team derivation stays in one place.
	/// </summary>
	private void LaunchMultiplayerGame(MapData? map)
	{
		if (map == null || _relay == null) return;

		var mode = _latestRoomState?.GameMode ?? GameMode.Ffa;
		var assignments = new List<SlotAssignment>();
		foreach (var pid in _pendingActiveIds)
		{
			// pid is also the slot index in M2 (relay sends slot key as playerId).
			int teamId = mode.TeamForSlot(pid);
			assignments.Add(new SlotAssignment(pid, pid, teamId));
		}

		GameLaunchData.MapData = map;
		GameLaunchData.Assignments = assignments;
		GameLaunchData.MultiplayerSession = new MultiplayerSessionState
		{
			Relay = _relay,
			LocalPlayerId = _pendingLocalId,
			ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
			Map = map,
			Assignments = assignments
		};
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	/// <summary>
	/// Render the per-slot list (player name + team tag) into the given container.
	/// Used by both host and joiner lobby panels — they want the same info.
	/// </summary>
	private static void RebuildLobbyList(VBoxContainer container, RoomStatePayload rs)
	{
		foreach (var child in container.GetChildren())
			child.QueueFree();

		string modeLabel = rs.GameMode == GameMode.Teams ? "Teams" : "FFA";
		var header = new Label { Text = $"Mode: {modeLabel}  |  Slots: {rs.Slots.Length}" };
		header.AddThemeFontSizeOverride("font_size", 16);
		container.AddChild(header);

		for (int i = 0; i < rs.Slots.Length; i++)
		{
			var slot = rs.Slots[i];
			string who;
			if (slot.IsClosed) who = "(closed)";
			else if (slot.IsOpen || string.IsNullOrEmpty(slot.DisplayName)) who = "(open)";
			else who = slot.DisplayName;

			string teamTag = rs.GameMode == GameMode.Teams
				? $"Team {slot.TeamId + 1}"
				: $"Player {slot.TeamId + 1}";

			var row = new HBoxContainer();
			var slotLabel = new Label
			{
				Text = $"Slot {i + 1}",
				CustomMinimumSize = new Vector2(70, 0)
			};
			row.AddChild(slotLabel);
			var nameLabel = new Label
			{
				Text = who,
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			row.AddChild(nameLabel);
			row.AddChild(new Label { Text = teamTag });
			container.AddChild(row);
		}
	}
}

public static class GameLaunchData
{
	public static MapData? MapData { get; set; }
	public static List<SlotAssignment>? Assignments { get; set; }
	public static Blocker.Game.Net.MultiplayerSessionState? MultiplayerSession { get; set; }
}
