using Blocker.Game.Maps;
using Blocker.Game.Net;
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

	// Host-only state.
	private Label? _hostStatusLabel;
	private Button? _hostStartBtn;
	private string _hostRoomCode = "";
	private int _hostLatestFilledSlots;
	private string _hostPendingError = "";

	// Join-only state.
	private Label? _joinStatusLabel;
	private string _joinMapName = "";
	private string _pendingError = "";

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
			Text = $"Host: {_mapData.Name}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);

		_hostStatusLabel = new Label { Text = "Creating room…" };
		vbox.AddChild(_hostStatusLabel);

		vbox.AddChild(new HSeparator());
		_hostStartBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
		_hostStartBtn.Pressed += OnHostStartPressed;
		vbox.AddChild(_hostStartBtn);

		// Relay events fire from the receive loop on a background thread. Capture
		// the payload into fields and bounce to the main thread via CallDeferred
		// with a 0-arg handler — Variant can't wrap arbitrary records.
		_relay.RoomStateReceived += (rs) =>
		{
			_hostRoomCode = rs.Code;
			_hostLatestFilledSlots = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
			CallDeferred(nameof(OnHostRoomStateDeferred));
		};
		_relay.GameStarted += (localId, activeIds) =>
		{
			_pendingLocalId = localId;
			_pendingActiveIds = activeIds;
			CallDeferred(nameof(OnHostGameStartedDeferred));
		};
		_relay.ServerError += (code) =>
		{
			_hostPendingError = code.ToString();
			CallDeferred(nameof(OnHostErrorDeferred));
		};

		// Create the room. Map blob is opaque to the relay; joiners load by name for M1.
		// We send the *file name* (e.g. "Lanes.json"), not the display name from the JSON
		// metadata — the joiner needs to look the file up on disk by that exact name.
		var mapFileName = MapSelection.SelectedMapFileName ?? (_mapData.Name + ".json");
		var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
		_relay.SendCreateRoom((byte)2, mapFileName, mapBlob);

		var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
		drain.Timeout += () => _relay.DrainInbound();
		AddChild(drain);
	}

	private void OnHostRoomStateDeferred()
	{
		_hostStatusLabel!.Text = _hostLatestFilledSlots == 2
			? $"Room code: {_hostRoomCode} — ready to start"
			: $"Room code: {_hostRoomCode} — waiting for opponent…";
		_hostStartBtn!.Disabled = _hostLatestFilledSlots < 2;
	}

	private void OnHostErrorDeferred()
	{
		if (_hostStatusLabel != null)
			_hostStatusLabel.Text = $"Error: {_hostPendingError}";
		if (_hostStartBtn != null)
			_hostStartBtn.Disabled = true;
	}

	private void OnHostStartPressed() => _relay!.SendStartGame();

	private void OnHostGameStartedDeferred()
	{
		// Map player 0 → slot 0, player 1 → slot 1. M1 assumes slot count >= active player count.
		var assignments = new List<SlotAssignment>();
		foreach (var pid in _pendingActiveIds)
			assignments.Add(new SlotAssignment(pid, pid));

		GameLaunchData.MapData = _mapData;
		GameLaunchData.Assignments = assignments;
		GameLaunchData.MultiplayerSession = new MultiplayerSessionState
		{
			Relay = _relay!,
			LocalPlayerId = _pendingLocalId,
			ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
			Map = _mapData!,
			Assignments = assignments
		};
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

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

		_relay.RoomStateReceived += (rs) =>
		{
			_joinMapName = rs.MapName;
			CallDeferred(nameof(OnJoinRoomStateDeferred));
		};
		_relay.GameStarted += (localId, activeIds) =>
		{
			_pendingLocalId = localId;
			_pendingActiveIds = activeIds;
			CallDeferred(nameof(OnJoinGameStartedDeferred));
		};
		_relay.ServerError += (code) =>
		{
			_pendingError = code.ToString();
			CallDeferred(nameof(OnJoinErrorDeferred));
		};

		var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
		drain.Timeout += () => _relay.DrainInbound();
		AddChild(drain);
	}

	private void OnJoinRoomStateDeferred() => _joinStatusLabel!.Text = $"Map: {_joinMapName} — waiting…";

	private void OnJoinErrorDeferred() => _joinStatusLabel!.Text = $"Error: {_pendingError}";

	private void OnJoinGameStartedDeferred()
	{
		// M1: the joiner loads the same map by name from local disk. M2 will add
		// a MapData wire message so hosts can share custom maps. Host and joiner
		// must have identical map files on disk today.
		var md = MapFileManager.Load(_joinMapName);
		if (md == null)
		{
			_joinStatusLabel!.Text = $"Map '{_joinMapName}' not found locally.";
			return;
		}

		var assignments = new List<SlotAssignment>();
		foreach (var pid in _pendingActiveIds)
			assignments.Add(new SlotAssignment(pid, pid));

		GameLaunchData.MapData = md;
		GameLaunchData.Assignments = assignments;
		GameLaunchData.MultiplayerSession = new MultiplayerSessionState
		{
			Relay = _relay!,
			LocalPlayerId = _pendingLocalId,
			ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
			Map = md,
			Assignments = assignments
		};
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}
}

public static class GameLaunchData
{
	public static MapData? MapData { get; set; }
	public static List<SlotAssignment>? Assignments { get; set; }
	public static Blocker.Game.Net.MultiplayerSessionState? MultiplayerSession { get; set; }
}
