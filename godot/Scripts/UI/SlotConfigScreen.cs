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
	private OptionButton? _hostMapOption;
	private VBoxContainer? _hostLobbyContainer;
	private string _hostRoomCode = "";
	private int _hostLatestFilledSlots;
	private string _hostPendingError = "";
	private GameMode _hostSelectedMode = GameMode.Ffa;
	private bool _hostRoomCreated;
	private bool _hostSuppressMapSignal;
	// Map catalog: filename → loaded MapData, populated once at setup.
	private readonly List<(string FileName, MapData Data)> _mapCatalog = new();

	// Join-only state.
	private Label? _joinStatusLabel;
	private VBoxContainer? _joinLobbyContainer;
	private string _joinMapName = "";
	private string _pendingError = "";
	private Action? _relayClosedHandler;
	private bool _joinNavigatingAway;

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

		// Host mode: map selection is built into the lobby screen.
		if (intent == MultiplayerIntent.Host)
		{
			SetupHostMode();
			return;
		}

		// Single-player: still uses MapSelectScreen → SlotConfigScreen flow.
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
		if (_relay == null)
		{
			GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
			return;
		}

		// Build the map catalog once — load every available map so we can show
		// names + slot counts in the dropdown and filter dynamically.
		foreach (var fileName in MapFileManager.ListMaps())
		{
			var data = MapFileManager.Load(fileName);
			if (data != null) _mapCatalog.Add((fileName, data));
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
			Text = "Host Lobby",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);

		bool rematchReattach = MultiplayerLaunchData.RematchReattach;
		MultiplayerLaunchData.RematchReattach = false;

		// Map picker row.
		var mapRow = new HBoxContainer();
		mapRow.AddChild(new Label { Text = "Map:", CustomMinimumSize = new Vector2(60, 0) });
		_hostMapOption = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
		PopulateMapDropdown(0); // no filter yet — 0 filled slots
		if (_mapCatalog.Count > 0)
		{
			_hostMapOption.Selected = 0;
			_mapData = _mapCatalog[0].Data;
			MapSelection.SelectedMapFileName = _mapCatalog[0].FileName;
		}
		_hostMapOption.ItemSelected += OnHostMapSelected;
		mapRow.AddChild(_hostMapOption);
		vbox.AddChild(mapRow);

		// Mode picker row.
		var modeRow = new HBoxContainer();
		modeRow.AddChild(new Label { Text = "Mode:", CustomMinimumSize = new Vector2(60, 0) });
		_hostGameModeOption = new OptionButton();
		_hostGameModeOption.AddItem("Free-for-all", (int)GameMode.Ffa);
		_hostGameModeOption.AddItem("Teams", (int)GameMode.Teams);
		_hostGameModeOption.Selected = 0;
		_hostGameModeOption.ItemSelected += OnHostModeSelected;
		modeRow.AddChild(_hostGameModeOption);
		vbox.AddChild(modeRow);

		if (!rematchReattach)
		{
			_hostCreateBtn = new Button { Text = "Create Room", CustomMinimumSize = new Vector2(0, 40) };
			_hostCreateBtn.Pressed += OnHostCreateRoomPressed;
			vbox.AddChild(_hostCreateBtn);
		}
		else
		{
			_hostRoomCreated = true;
		}

		vbox.AddChild(new HSeparator());

		_hostStatusLabel = new Label
		{
			Text = rematchReattach
				? "Rematch lobby — waiting for player slots…"
				: "Select a map and click Create Room."
		};
		vbox.AddChild(_hostStatusLabel);

		_hostLobbyContainer = new VBoxContainer();
		_hostLobbyContainer.AddThemeConstantOverride("separation", 6);
		vbox.AddChild(_hostLobbyContainer);

		vbox.AddChild(new HSeparator());
		_hostStartBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
		_hostStartBtn.Pressed += OnHostStartPressed;
		vbox.AddChild(_hostStartBtn);

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

		if (MultiplayerLaunchData.PendingRoomState is { } pending)
		{
			_latestRoomState = pending;
			_hostRoomCode = pending.Code;
			_hostLatestFilledSlots = pending.Slots.Count(s => !s.IsOpen && !s.IsClosed);
			MultiplayerLaunchData.PendingRoomState = null;

			// Sync map/mode dropdowns from the existing room state on rematch.
			_hostSelectedMode = pending.GameMode;
			if (_hostGameModeOption != null)
			{
				for (int i = 0; i < _hostGameModeOption.ItemCount; i++)
				{
					if (_hostGameModeOption.GetItemId(i) == (int)pending.GameMode)
					{ _hostGameModeOption.Selected = i; break; }
				}
			}
			// Find the map by name in the catalog.
			var mapEntry = _mapCatalog.Find(e => e.FileName == pending.MapName
				|| e.FileName == pending.MapName + ".json");
			if (mapEntry.Data != null)
			{
				_mapData = mapEntry.Data;
				MapSelection.SelectedMapFileName = mapEntry.FileName;
			}

			OnHostRoomStateDeferred();
		}
	}

	/// <summary>Populate the map dropdown with maps that have at least <paramref name="minSlots"/> slots.</summary>
	private void PopulateMapDropdown(int minSlots)
	{
		if (_hostMapOption == null) return;
		_hostMapOption.Clear();
		foreach (var (fileName, data) in _mapCatalog)
		{
			if (data.SlotCount < minSlots) continue;
			_hostMapOption.AddItem($"{data.Name} ({data.SlotCount} slots)");
			// Metadata index stored as item ID so we can look up the catalog entry.
			_hostMapOption.SetItemId(_hostMapOption.ItemCount - 1, _mapCatalog.IndexOf((fileName, data)));
		}
	}

	private void OnHostMapSelected(long idx)
	{
		if (_hostSuppressMapSignal) return;
		int catIdx = _hostMapOption!.GetItemId((int)idx);
		if (catIdx < 0 || catIdx >= _mapCatalog.Count) return;
		var (fileName, data) = _mapCatalog[catIdx];
		_mapData = data;
		MapSelection.SelectedMapFileName = fileName;

		if (_hostRoomCreated)
			SendUpdateRoom();
	}

	private void OnHostModeSelected(long idx)
	{
		_hostSelectedMode = (GameMode)_hostGameModeOption!.GetItemId((int)idx);
		if (_hostRoomCreated)
			SendUpdateRoom();
	}

	private void SendUpdateRoom()
	{
		if (_relay == null || _mapData == null) return;
		var mapFileName = MapSelection.SelectedMapFileName ?? (_mapData.Name + ".json");
		var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
		_relay.SendUpdateRoom((byte)_mapData.SlotCount, _hostSelectedMode, mapFileName, mapBlob);
	}

	private void OnHostCreateRoomPressed()
	{
		if (_relay == null || _mapData == null || _hostRoomCreated) return;

		_hostRoomCreated = true;
		_hostCreateBtn!.Disabled = true;
		_hostStatusLabel!.Text = "Creating room…";

		var mapFileName = MapSelection.SelectedMapFileName ?? (_mapData.Name + ".json");
		var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
		_relay.SendCreateRoom((byte)_mapData.SlotCount, _hostSelectedMode, mapFileName, mapBlob);
	}

	private void OnHostRoomStateDeferred()
	{
		if (_latestRoomState == null) return;
		int total = _latestRoomState.Slots.Length;
		bool ready = _hostLatestFilledSlots >= 2;
		_hostStatusLabel!.Text = ready
			? $"Room code: {_hostRoomCode} — {_hostLatestFilledSlots}/{total} players, ready to start"
			: $"Room code: {_hostRoomCode} — waiting for players ({_hostLatestFilledSlots}/{total})…";
		_hostStartBtn!.Disabled = !ready;

		// Re-filter the map dropdown: can't pick maps with fewer slots than filled players.
		// Suppress ItemSelected signal to avoid feedback loop (RoomState → repopulate → signal → UpdateRoom → RoomState).
		if (_hostMapOption != null)
		{
			_hostSuppressMapSignal = true;
			PopulateMapDropdown(_hostLatestFilledSlots);
			if (_mapData != null)
			{
				for (int i = 0; i < _hostMapOption.ItemCount; i++)
				{
					int catIdx = _hostMapOption.GetItemId(i);
					if (catIdx >= 0 && catIdx < _mapCatalog.Count && _mapCatalog[catIdx].Data == _mapData)
					{ _hostMapOption.Selected = i; break; }
				}
			}
			_hostSuppressMapSignal = false;
		}

		RebuildHostLobbyList();
	}

	/// <summary>Render the lobby list with kick buttons for non-host occupied slots.</summary>
	private void RebuildHostLobbyList()
	{
		if (_hostLobbyContainer == null || _latestRoomState == null) return;
		foreach (var child in _hostLobbyContainer.GetChildren())
			child.QueueFree();

		var rs = _latestRoomState;
		string modeLabel = rs.GameMode == GameMode.Teams ? "Teams" : "FFA";
		var header = new Label { Text = $"Mode: {modeLabel}  |  Slots: {rs.Slots.Length}" };
		header.AddThemeFontSizeOverride("font_size", 16);
		_hostLobbyContainer.AddChild(header);

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
			row.AddChild(new Label { Text = $"Slot {i + 1}", CustomMinimumSize = new Vector2(70, 0) });
			row.AddChild(new Label { Text = who, SizeFlagsHorizontal = SizeFlags.ExpandFill });
			row.AddChild(new Label { Text = teamTag });

			// Kick button for occupied non-host slots.
			bool isOccupied = !slot.IsOpen && !slot.IsClosed && !string.IsNullOrEmpty(slot.DisplayName);
			bool isHost = (i == 0 && !_hostRoomCreated) || (_latestRoomState != null && slot.DisplayName == rs.Slots[0].DisplayName && i == 0);
			// Simpler: slot 0 is always the host after room creation (relay assigns host to slot 0).
			// But after UpdateRoom the host may be re-seated. Check via the slot index in activeIds.
			// Actually the host's connection is slot 0 on CreateRoom and re-seated to the lowest slot
			// on UpdateRoom. The reliable check: the host is whoever is in the first filled slot.
			// For now, just don't show kick on slot 0 (host is always re-seated there by the relay).
			if (isOccupied && i != 0)
			{
				byte kickSlot = (byte)i;
				var kickBtn = new Button { Text = "Kick", CustomMinimumSize = new Vector2(60, 0) };
				kickBtn.Pressed += () => _relay?.SendKickPlayer(kickSlot);
				row.AddChild(kickBtn);
			}

			_hostLobbyContainer.AddChild(row);
		}
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
			if (code == ErrorCode.HostLeft)
			{
				_pendingError = "Host left the lobby.";
				CallDeferred(nameof(OnJoinDisconnectedDeferred));
			}
			else if (code == ErrorCode.Kicked)
			{
				_pendingError = "You were kicked from the lobby.";
				CallDeferred(nameof(OnJoinDisconnectedDeferred));
			}
			else
			{
				_pendingError = code.ToString();
				CallDeferred(nameof(OnJoinErrorDeferred));
			}
		};
		_relayClosedHandler = () =>
		{
			// Fallback: if the connection closes without a HostLeft/Kicked error
			// (e.g. network failure), still navigate back with a generic message.
			if (string.IsNullOrEmpty(_pendingError))
				_pendingError = "Connection lost.";
			CallDeferred(nameof(OnJoinDisconnectedDeferred));
		};
		_relay.RoomStateReceived += _relayRoomStateHandler;
		_relay.GameStarted += _relayGameStartedHandler;
		_relay.ServerError += _relayErrorHandler;
		_relay.ConnectionClosed += _relayClosedHandler;

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

	private void OnJoinDisconnectedDeferred()
	{
		if (_joinNavigatingAway) return;
		_joinNavigatingAway = true;
		// Clean up relay — it's dead or about to die.
		_relay?.Dispose();
		MultiplayerLaunchData.Relay = null;
		// Show the reason briefly, then navigate back.
		_joinStatusLabel!.Text = _pendingError;
		var timer = GetTree().CreateTimer(1.5);
		timer.Timeout += () => GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
	}

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
		if (_relay != null)
		{
			if (_relayRoomStateHandler != null) _relay.RoomStateReceived -= _relayRoomStateHandler;
			if (_relayGameStartedHandler != null) _relay.GameStarted -= _relayGameStartedHandler;
			if (_relayErrorHandler != null) _relay.ServerError -= _relayErrorHandler;
			if (_relayClosedHandler != null) _relay.ConnectionClosed -= _relayClosedHandler;
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
