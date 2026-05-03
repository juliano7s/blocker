using Blocker.Game.Net;
using Blocker.Game.Rendering;
using Blocker.Simulation.Net;
using Godot;
using System;
using System.Linq;

namespace Blocker.Game.UI;

public partial class LobbyListScreen : Control
{
    private MenuGrid _menuGrid = null!;
    private LineEdit _playerNameEdit = null!;
    private LineEdit _lobbyNameEdit = null!;
    private VBoxContainer _lobbyList = null!;
    private Label _statusLabel = null!;
    private Label _countLabel = null!;
    private RelayClient? _relay;
    private Godot.Timer? _pollTimer;
    private bool _connected;
    private RoomSummary[] _summaries = Array.Empty<RoomSummary>();

    // Stored delegates for cleanup
    private Action? _helloAckedHandler;
    private Action? _closedHandler;
    private Action<ErrorCode>? _errorHandler;
    private Action<RoomSummary[]>? _roomListHandler;
    private Action<RoomStatePayload>? _roomStateHandler;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        _menuGrid = new MenuGrid { Name = "MenuGrid" };
        AddChild(_menuGrid);

        var panel = new PanelContainer();
        panel.AnchorLeft = 0.15f; panel.AnchorRight = 0.85f;
        panel.AnchorTop = 0.12f; panel.AnchorBottom = 0.88f;
        panel.AddThemeStyleboxOverride("panel", LobbyStyles.MakePanel(16, 12));
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(vbox);

        // Title
        var title = new Label
        {
            Text = "MULTIPLAYER",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        LobbyStyles.StyleTitle(title);
        vbox.AddChild(title);

        var sep1 = new HSeparator();
        LobbyStyles.StyleSeparator(sep1);
        vbox.AddChild(sep1);

        // Top bar: player name | lobby name | HOST button
        var topBar = new HBoxContainer();
        topBar.AddThemeConstantOverride("separation", 8);
        vbox.AddChild(topBar);

        var nameLabel = new Label { Text = "Name:" };
        LobbyStyles.StyleLabel(nameLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        topBar.AddChild(nameLabel);

        _playerNameEdit = new LineEdit
        {
            PlaceholderText = "Your name",
            Text = MultiplayerLaunchData.PlayerName,
            MaxLength = 16,
            CustomMinimumSize = new Vector2(120, 0)
        };
        LobbyStyles.StyleInput(_playerNameEdit);
        topBar.AddChild(_playerNameEdit);

        var lobbyLabel = new Label { Text = "Lobby:" };
        LobbyStyles.StyleLabel(lobbyLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        topBar.AddChild(lobbyLabel);

        _lobbyNameEdit = new LineEdit
        {
            PlaceholderText = "Lobby name",
            Text = "My Game",
            MaxLength = 32,
            CustomMinimumSize = new Vector2(140, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        LobbyStyles.StyleInput(_lobbyNameEdit);
        topBar.AddChild(_lobbyNameEdit);

        var hostBtn = new Button
        {
            Text = "HOST NEW",
            CustomMinimumSize = new Vector2(100, 28)
        };
        LobbyStyles.StyleButton(hostBtn, accent: true);
        hostBtn.Pressed += OnHostPressed;
        topBar.AddChild(hostBtn);

        var sep2 = new HSeparator();
        LobbyStyles.StyleSeparator(sep2);
        vbox.AddChild(sep2);

        // Status
        _statusLabel = new Label
        {
            Text = "Connecting to server…",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        LobbyStyles.StyleLabel(_statusLabel, dim: true, fontSize: LobbyStyles.FontSmall);
        vbox.AddChild(_statusLabel);

        // Lobby table header
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 6);
        var h1 = new Label { Text = "LOBBY", CustomMinimumSize = new Vector2(180, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill };
        LobbyStyles.StyleLabel(h1, dim: true, fontSize: LobbyStyles.FontSmall);
        headerRow.AddChild(h1);
        var h2 = new Label { Text = "PLAYERS", CustomMinimumSize = new Vector2(60, 0) };
        LobbyStyles.StyleLabel(h2, dim: true, fontSize: LobbyStyles.FontSmall);
        headerRow.AddChild(h2);
        var h3 = new Label { Text = "MAP", CustomMinimumSize = new Vector2(120, 0) };
        LobbyStyles.StyleLabel(h3, dim: true, fontSize: LobbyStyles.FontSmall);
        headerRow.AddChild(h3);
        headerRow.AddChild(new Control { CustomMinimumSize = new Vector2(64, 0) });
        vbox.AddChild(headerRow);

        // Scrollable lobby list
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 100)
        };
        LobbyStyles.StyleScrollContainer(scroll);
        vbox.AddChild(scroll);

        _lobbyList = new VBoxContainer();
        _lobbyList.AddThemeConstantOverride("separation", 2);
        _lobbyList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_lobbyList);

        var sep3 = new HSeparator();
        LobbyStyles.StyleSeparator(sep3);
        vbox.AddChild(sep3);

        // Bottom bar
        var bottomBar = new HBoxContainer();
        vbox.AddChild(bottomBar);

        var backBtn = new Button
        {
            Text = "< BACK",
            CustomMinimumSize = new Vector2(80, 26)
        };
        LobbyStyles.StyleButton(backBtn);
        backBtn.Pressed += () =>
        {
            Audio.UISoundManager.Instance?.PlayClick();
            _relay?.Dispose();
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        bottomBar.AddChild(backBtn);

        bottomBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill });

        _countLabel = new Label { Text = "0 lobbies" };
        LobbyStyles.StyleLabel(_countLabel, dim: true, fontSize: LobbyStyles.FontSmall);
        bottomBar.AddChild(_countLabel);

        ConnectToRelay();
    }

    private async void ConnectToRelay()
    {
        _relay = new RelayClient();
        string playerName = _playerNameEdit.Text.Trim();
        if (string.IsNullOrEmpty(playerName)) playerName = "Player";
        bool ok = await _relay.ConnectAsync(RelayClientConfig.ResolvedUrl, playerName);
        if (!ok)
        {
            _statusLabel.Text = $"Cannot reach server: {_relay.LastError}";
            return;
        }

        _helloAckedHandler = () => CallDeferred(nameof(OnHelloAcked));
        _closedHandler = () => CallDeferred(nameof(OnClosed));
        _errorHandler = (e) => CallDeferred(nameof(OnServerError), (int)e);
        _roomListHandler = (rooms) =>
        {
            _summaries = rooms;
            CallDeferred(nameof(OnRoomListDeferred));
        };
        _roomStateHandler = (rs) =>
        {
            MultiplayerLaunchData.PendingRoomState = rs;
            CallDeferred(nameof(OnRoomJoinedDeferred));
        };

        _relay.HelloAcked += _helloAckedHandler;
        _relay.ConnectionClosed += _closedHandler;
        _relay.ServerError += _errorHandler;
        _relay.RoomListReceived += _roomListHandler;
        _relay.RoomStateReceived += _roomStateHandler;

        var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
        drain.Timeout += () => _relay?.DrainInbound();
        AddChild(drain);
    }

    private void OnHelloAcked()
    {
        _connected = true;
        _statusLabel.Text = "";
        _relay?.SendListRooms();

        _pollTimer = new Godot.Timer { WaitTime = 3.0, Autostart = true };
        _pollTimer.Timeout += () => _relay?.SendListRooms();
        AddChild(_pollTimer);
    }

    private void OnClosed()
    {
        _connected = false;
        _statusLabel.Text = "Disconnected.";
    }

    private void OnServerError(int code)
    {
        _statusLabel.Text = $"Server error: {(ErrorCode)code}";
    }

    private void OnRoomListDeferred()
    {
        foreach (var child in _lobbyList.GetChildren())
            child.QueueFree();

        _statusLabel.Text = _connected ? "" : "Disconnected.";

        int count = _summaries.Length;
        _countLabel.Text = $"{count} {(count == 1 ? "lobby" : "lobbies")}";

        if (count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No lobbies found. Host a new game!",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            LobbyStyles.StyleLabel(emptyLabel, dim: true);
            _lobbyList.AddChild(emptyLabel);
            return;
        }

        foreach (var room in _summaries)
        {
            string code = room.Code;
            string roomName = room.RoomName;
            int playerCount = room.PlayerCount;
            int slotCount = room.SlotCount;
            string mapName = room.MapName;

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 6);

            var nameL = new Label
            {
                Text = string.IsNullOrEmpty(roomName) ? code : roomName,
                CustomMinimumSize = new Vector2(180, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            LobbyStyles.StyleLabel(nameL);
            row.AddChild(nameL);

            var playersL = new Label
            {
                Text = $"{playerCount}/{slotCount}",
                CustomMinimumSize = new Vector2(60, 0)
            };
            LobbyStyles.StyleLabel(playersL);
            row.AddChild(playersL);

            var mapL = new Label
            {
                Text = mapName,
                CustomMinimumSize = new Vector2(120, 0)
            };
            LobbyStyles.StyleLabel(mapL, dim: true);
            row.AddChild(mapL);

            var joinBtn = new Button
            {
                Text = "JOIN",
                CustomMinimumSize = new Vector2(64, 24)
            };
            LobbyStyles.StyleButton(joinBtn, accent: true);
            string joinCode = code;
            joinBtn.Pressed += () => OnJoinPressed(joinCode);
            row.AddChild(joinBtn);

            _lobbyList.AddChild(row);
        }
    }

    private void OnHostPressed()
    {
        Audio.UISoundManager.Instance?.PlayClick();
        if (!_connected || _relay == null) return;
        MultiplayerLaunchData.Intent = MultiplayerIntent.Host;
        MultiplayerLaunchData.Relay = _relay;
        MultiplayerLaunchData.LobbyName = _lobbyNameEdit.Text.Trim();
        MultiplayerLaunchData.PlayerName = _playerNameEdit.Text.Trim();
        GetTree().ChangeSceneToFile("res://Scenes/GameLobby.tscn");
    }

    private void OnJoinPressed(string code)
    {
        Audio.UISoundManager.Instance?.PlayClick();
        if (!_connected || _relay == null) return;
        MultiplayerLaunchData.Intent = MultiplayerIntent.Join;
        MultiplayerLaunchData.JoinCode = code;
        MultiplayerLaunchData.Relay = _relay;
        MultiplayerLaunchData.PlayerName = _playerNameEdit.Text.Trim();
        _relay.SendJoinRoom(code, 1);
    }

    private void OnRoomJoinedDeferred()
    {
        if (MultiplayerLaunchData.Intent == MultiplayerIntent.Join)
            GetTree().ChangeSceneToFile("res://Scenes/GameLobby.tscn");
    }

    public override void _ExitTree()
    {
        if (_relay != null)
        {
            if (_helloAckedHandler != null) _relay.HelloAcked -= _helloAckedHandler;
            if (_closedHandler != null) _relay.ConnectionClosed -= _closedHandler;
            if (_errorHandler != null) _relay.ServerError -= _errorHandler;
            if (_roomListHandler != null) _relay.RoomListReceived -= _roomListHandler;
            if (_roomStateHandler != null) _relay.RoomStateReceived -= _roomStateHandler;
        }
    }
}
