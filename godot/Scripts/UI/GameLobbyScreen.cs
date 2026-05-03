using Blocker.Game.Maps;
using Blocker.Game.Net;
using Blocker.Game.Rendering;
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Blocker.Game.UI;

public partial class GameLobbyScreen : Control
{
    // Shared state
    private MapData? _mapData;
    private readonly Dictionary<int, string> _slotAssignments = new();
    private VBoxContainer _slotContainer = null!;
    private MapMiniature _mapMiniature = null!;
    private OptionButton _mapDropdown = null!;
    private Label _headerLabel = null!;
    private int _playerSlot = 0;
    private readonly List<(string FileName, MapData Data)> _mapCatalog = new();

    // Multiplayer state
    private RelayClient? _relay;
    private RoomStatePayload? _latestRoomState;
    private int _pendingLocalId;
    private int[] _pendingActiveIds = Array.Empty<int>();
    private LobbyChatPanel? _chatPanel;

    // Host-only
    private Button? _startBtn;
    private OptionButton? _modeDropdown;
    private Label? _roomCodeLabel;
    private string _lobbyName = "";
    private GameMode _selectedMode = GameMode.Ffa;
    private bool _roomCreated;
    private bool _suppressMapSignal;

    // Join-only
    private Button? _readyBtn;
    private bool _isReady;
    private Label? _joinStatusLabel;
    private Label? _mapInfoLabel;
    private bool _navigatingAway;

    // Ping display
    private Label? _pingLabel;
    private Godot.Timer? _pingTimer;

    // Relay handlers
    private Action<RoomStatePayload>? _relayRoomStateHandler;
    private Action<int, int[]>? _relayGameStartedHandler;
    private Action<ErrorCode>? _relayErrorHandler;
    private Action? _relayClosedHandler;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        var menuGrid = new MenuGrid { Name = "MenuGrid" };
        AddChild(menuGrid);

        var intent = MultiplayerLaunchData.Intent;
        if (intent == MultiplayerIntent.Host)
            SetupHostMode();
        else if (intent == MultiplayerIntent.Join)
            SetupJoinMode();
        else
            SetupSinglePlayerMode();
    }

    // ------------------------------------------------------------------
    // Layout helpers
    // ------------------------------------------------------------------

    private (VBoxContainer leftCol, VBoxContainer rightCol) BuildTwoColumnLayout(string titleText)
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0.15f; panel.AnchorRight = 0.85f;
        panel.AnchorTop = 0.12f; panel.AnchorBottom = 0.88f;
        panel.AddThemeStyleboxOverride("panel", LobbyStyles.MakePanel(14, 10));
        AddChild(panel);

        var outerVbox = new VBoxContainer();
        outerVbox.AddThemeConstantOverride("separation", 6);
        panel.AddChild(outerVbox);

        _headerLabel = new Label
        {
            Text = titleText,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        LobbyStyles.StyleTitle(_headerLabel);
        outerVbox.AddChild(_headerLabel);

        var sep = new HSeparator();
        LobbyStyles.StyleSeparator(sep);
        outerVbox.AddChild(sep);

        var hbox = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        hbox.AddThemeConstantOverride("separation", 12);
        outerVbox.AddChild(hbox);

        var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftCol.SizeFlagsStretchRatio = 1.2f;
        leftCol.AddThemeConstantOverride("separation", 5);
        hbox.AddChild(leftCol);

        var rightCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rightCol.AddThemeConstantOverride("separation", 5);
        hbox.AddChild(rightCol);

        return (leftCol, rightCol);
    }

    private void LoadMapCatalog()
    {
        _mapCatalog.Clear();
        foreach (var fileName in MapFileManager.ListMaps())
        {
            var data = MapFileManager.Load(fileName);
            if (data != null) _mapCatalog.Add((fileName, data));
        }
    }

    private void BuildMapDropdown(VBoxContainer container, bool enabled)
    {
        LoadMapCatalog();

        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var mapLabel = new Label { Text = "Map:", CustomMinimumSize = new Vector2(40, 0) };
        LobbyStyles.StyleLabel(mapLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        row.AddChild(mapLabel);

        _mapDropdown = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Disabled = !enabled
        };
        LobbyStyles.StyleDropdown(_mapDropdown);
        PopulateMapDropdown(0);
        if (_mapCatalog.Count > 0)
        {
            _mapDropdown.Selected = 0;
            _mapData = _mapCatalog[0].Data;
        }
        if (enabled)
            _mapDropdown.ItemSelected += OnMapSelected;
        row.AddChild(_mapDropdown);
        container.AddChild(row);
    }

    private void PopulateMapDropdown(int minSlots)
    {
        if (_mapDropdown == null) return;
        _mapDropdown.Clear();
        foreach (var (fileName, data) in _mapCatalog)
        {
            if (data.SlotCount < minSlots) continue;
            _mapDropdown.AddItem($"{data.Name} ({data.SlotCount}p)");
            _mapDropdown.SetItemId(_mapDropdown.ItemCount - 1, _mapCatalog.IndexOf((fileName, data)));
        }
    }

    private void OnMapSelected(long idx)
    {
        if (_suppressMapSignal) return;
        int catIdx = _mapDropdown!.GetItemId((int)idx);
        if (catIdx < 0 || catIdx >= _mapCatalog.Count) return;
        _mapData = _mapCatalog[catIdx].Data;
        _mapMiniature.SetMap(_mapData);

        if (MultiplayerLaunchData.Intent == MultiplayerIntent.None && _mapData != null)
        {
            _slotAssignments.Clear();
            for (int i = 0; i < _mapData.SlotCount; i++)
                _slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
            _playerSlot = 0;
            RebuildSinglePlayerSlotList();
        }

        if (_roomCreated) SendUpdateRoom();
    }

    private void BuildMapMiniature(VBoxContainer container)
    {
        _mapMiniature = new MapMiniature
        {
            CustomMinimumSize = new Vector2(100, 100),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        container.AddChild(_mapMiniature);
        _mapMiniature.SetMap(_mapData);
    }

    private void SetupPingDisplay(VBoxContainer container)
    {
        if (_relay == null) return;

        _pingLabel = new Label { Text = "Ping: …" };
        LobbyStyles.StyleLabel(_pingLabel, dim: true, fontSize: LobbyStyles.FontSmall);
        container.AddChild(_pingLabel);

        _relay.SendPing();

        _pingTimer = new Godot.Timer { WaitTime = 2.0, Autostart = true };
        _pingTimer.Timeout += () =>
        {
            if (_relay == null) return;
            float ms = _relay.PingMs;
            if (ms >= 0)
            {
                _pingLabel!.Text = $"Ping: {ms:F0} ms";
                _pingLabel.AddThemeColorOverride("font_color",
                    ms <= 80 ? LobbyStyles.ReadyGreen :
                    ms <= 150 ? new Color(0.9f, 0.9f, 0.3f) :
                    new Color(0.9f, 0.3f, 0.3f));
            }
            _relay.SendPing();
        };
        AddChild(_pingTimer);
    }

    private Label MakeSectionHeader(string text)
    {
        var label = new Label { Text = text };
        LobbyStyles.StyleLabel(label, dim: true, fontSize: LobbyStyles.FontSmall);
        return label;
    }

    // ------------------------------------------------------------------
    // Single-player mode
    // ------------------------------------------------------------------

    private void SetupSinglePlayerMode()
    {
        var (leftCol, rightCol) = BuildTwoColumnLayout("PLAY VS AI");

        leftCol.AddChild(MakeSectionHeader("SLOTS (click to toggle)"));

        _slotContainer = new VBoxContainer();
        _slotContainer.AddThemeConstantOverride("separation", 2);
        var slotScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 60)
        };
        LobbyStyles.StyleScrollContainer(slotScroll);
        slotScroll.AddChild(_slotContainer);
        leftCol.AddChild(slotScroll);

        leftCol.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill });

        var startBtn = new Button
        {
            Text = "START GAME",
            CustomMinimumSize = new Vector2(0, 30)
        };
        LobbyStyles.StyleButton(startBtn, accent: true);
        startBtn.Pressed += OnSinglePlayerStart;
        leftCol.AddChild(startBtn);

        var backBtn = new Button { Text = "< BACK", CustomMinimumSize = new Vector2(80, 26) };
        LobbyStyles.StyleButton(backBtn);
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        leftCol.AddChild(backBtn);

        // Right: map dropdown + miniature
        BuildMapDropdown(rightCol, enabled: true);
        BuildMapMiniature(rightCol);

        if (_mapData != null)
        {
            for (int i = 0; i < _mapData.SlotCount; i++)
                _slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
            RebuildSinglePlayerSlotList();
        }
    }

    private void RebuildSinglePlayerSlotList()
    {
        foreach (var child in _slotContainer.GetChildren())
            child.QueueFree();

        if (_mapData == null) return;
        for (int i = 0; i < _mapData.SlotCount; i++)
        {
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);
            var slotLabel = new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(20, 0) };
            LobbyStyles.StyleLabel(slotLabel, dim: true, fontSize: LobbyStyles.FontSmall);
            row.AddChild(slotLabel);

            var btn = new Button
            {
                Text = _slotAssignments[i],
                CustomMinimumSize = new Vector2(120, 24),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            LobbyStyles.StyleButton(btn);
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
                if (_slotAssignments[key] == "Player") _slotAssignments[key] = "AI (inactive)";
            _slotAssignments[slot] = "Player";
            _playerSlot = slot;
        }

        RebuildSinglePlayerSlotList();
    }

    private void OnSinglePlayerStart()
    {
        if (_mapData == null) return;

        var assignments = new List<SlotAssignment>();
        int nextAiId = 1;
        for (int i = 0; i < _mapData.SlotCount; i++)
        {
            if (_slotAssignments[i] == "Player")
                assignments.Add(new SlotAssignment(i, 0));
            else
                assignments.Add(new SlotAssignment(i, nextAiId++));
        }

        GameLaunchData.MapData = _mapData;
        GameLaunchData.Assignments = assignments;
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    // ------------------------------------------------------------------
    // Host mode
    // ------------------------------------------------------------------

    private void SetupHostMode()
    {
        _relay = MultiplayerLaunchData.Relay;
        if (_relay == null)
        {
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
            return;
        }

        _lobbyName = MultiplayerLaunchData.LobbyName;
        if (string.IsNullOrEmpty(_lobbyName)) _lobbyName = "Game";

        bool rematchReattach = MultiplayerLaunchData.RematchReattach;
        MultiplayerLaunchData.RematchReattach = false;

        var (leftCol, rightCol) = BuildTwoColumnLayout(
            rematchReattach ? "REMATCH LOBBY" : $"HOST — {_lobbyName}");

        // Left: room code + slot list + start button
        _roomCodeLabel = new Label { Text = "Creating room…" };
        LobbyStyles.StyleLabel(_roomCodeLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        leftCol.AddChild(_roomCodeLabel);

        leftCol.AddChild(MakeSectionHeader("PLAYERS"));

        _slotContainer = new VBoxContainer();
        _slotContainer.AddThemeConstantOverride("separation", 2);
        var slotScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30)
        };
        LobbyStyles.StyleScrollContainer(slotScroll);
        slotScroll.AddChild(_slotContainer);
        leftCol.AddChild(slotScroll);

        // Chat panel
        _chatPanel = new LobbyChatPanel();
        _chatPanel.CustomMinimumSize = new Vector2(0, 120);
        leftCol.AddChild(_chatPanel);

        _startBtn = new Button
        {
            Text = "START GAME",
            CustomMinimumSize = new Vector2(0, 30),
            Disabled = true
        };
        LobbyStyles.StyleButton(_startBtn, accent: true);
        _startBtn.Pressed += () => _relay!.SendStartGame();
        leftCol.AddChild(_startBtn);

        var backBtn = new Button { Text = "< BACK", CustomMinimumSize = new Vector2(80, 26) };
        LobbyStyles.StyleButton(backBtn);
        backBtn.Pressed += () =>
        {
            _relay?.SendLeaveRoom();
            _relay?.Dispose();
            MultiplayerLaunchData.Relay = null;
            GetTree().ChangeSceneToFile("res://Scenes/LobbyList.tscn");
        };
        leftCol.AddChild(backBtn);

        SetupPingDisplay(leftCol);

        // Right: map dropdown + mode dropdown + miniature
        BuildMapDropdown(rightCol, enabled: true);

        var modeRow = new HBoxContainer();
        modeRow.AddThemeConstantOverride("separation", 6);
        var modeLabel = new Label { Text = "Mode:", CustomMinimumSize = new Vector2(40, 0) };
        LobbyStyles.StyleLabel(modeLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        modeRow.AddChild(modeLabel);
        _modeDropdown = new OptionButton();
        _modeDropdown.AddItem("Free-for-all", (int)GameMode.Ffa);
        _modeDropdown.AddItem("Teams", (int)GameMode.Teams);
        _modeDropdown.Selected = 0;
        _modeDropdown.ItemSelected += OnModeSelected;
        LobbyStyles.StyleDropdown(_modeDropdown);
        modeRow.AddChild(_modeDropdown);
        rightCol.AddChild(modeRow);

        BuildMapMiniature(rightCol);

        // Wire up relay events
        _relayRoomStateHandler = (rs) =>
        {
            _latestRoomState = rs;
            CallDeferred(nameof(OnHostRoomStateDeferred));
        };
        _relayGameStartedHandler = (localId, activeIds) =>
        {
            _pendingLocalId = localId;
            _pendingActiveIds = activeIds;
            CallDeferred(nameof(OnGameStartedDeferred));
        };
        _relayErrorHandler = (code) =>
            CallDeferred(nameof(OnHostErrorDeferred), (int)code);

        _relay.RoomStateReceived += _relayRoomStateHandler;
        _relay.GameStarted += _relayGameStartedHandler;
        _relay.ServerError += _relayErrorHandler;

        var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
        drain.Timeout += () => _relay.DrainInbound();
        AddChild(drain);

        _chatPanel.SetRelay(_relay);

        if (!rematchReattach)
        {
            _roomCreated = true;
            if (_mapData != null)
            {
                var catEntry = _mapCatalog.Find(e => e.Data == _mapData);
                var mapFileName = catEntry.FileName ?? (_mapData.Name + ".json");
                var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
                _relay.SendCreateRoom((byte)_mapData.SlotCount, _selectedMode, _lobbyName, mapFileName, mapBlob);
            }
        }

        if (MultiplayerLaunchData.PendingRoomState is { } pending)
        {
            _latestRoomState = pending;
            MultiplayerLaunchData.PendingRoomState = null;
            SyncDropdownsFromRoomState(pending);
            OnHostRoomStateDeferred();
        }
    }

    private void OnModeSelected(long idx)
    {
        _selectedMode = (GameMode)_modeDropdown!.GetItemId((int)idx);
        if (_roomCreated) SendUpdateRoom();
    }

    private void SendUpdateRoom()
    {
        if (_relay == null || _mapData == null) return;
        var catEntry = _mapCatalog.Find(e => e.Data == _mapData);
        var mapFileName = catEntry.FileName ?? (_mapData.Name + ".json");
        var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
        _relay.SendUpdateRoom((byte)_mapData.SlotCount, _selectedMode, _lobbyName, mapFileName, mapBlob);
    }

    private void OnHostRoomStateDeferred()
    {
        if (_latestRoomState == null) return;
        var rs = _latestRoomState;
        int filled = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
        bool allReady = rs.Slots.Where((s, i) => i != 0 && !s.IsOpen && !s.IsClosed)
                                .All(s => s.IsReady);
        bool canStart = filled >= 2 && allReady;

        _roomCodeLabel!.Text = $"Code: {rs.Code}  ({filled}/{rs.Slots.Length})";
        _startBtn!.Disabled = !canStart;
        _startBtn.Text = (!canStart && filled >= 2) ? "WAITING FOR READY…" : "START GAME";

        if (_mapDropdown != null)
        {
            _suppressMapSignal = true;
            PopulateMapDropdown(filled);
            if (_mapData != null)
            {
                for (int i = 0; i < _mapDropdown.ItemCount; i++)
                {
                    int catIdx = _mapDropdown.GetItemId(i);
                    if (catIdx >= 0 && catIdx < _mapCatalog.Count && _mapCatalog[catIdx].Data == _mapData)
                    { _mapDropdown.Selected = i; break; }
                }
            }
            _suppressMapSignal = false;
        }

        RebuildHostSlotList(rs);
        UpdateChatSlotNames(rs);
    }

    private void RebuildHostSlotList(RoomStatePayload rs)
    {
        foreach (var child in _slotContainer.GetChildren())
            child.QueueFree();

        for (int i = 0; i < rs.Slots.Length; i++)
        {
            var slot = rs.Slots[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            string who;
            if (slot.IsClosed) who = "(closed)";
            else if (slot.IsOpen || string.IsNullOrEmpty(slot.DisplayName)) who = "(open)";
            else who = slot.DisplayName;

            string teamTag = rs.GameMode == GameMode.Teams ? $"T{slot.TeamId + 1}" : "";

            var numLabel = new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(20, 0) };
            LobbyStyles.StyleLabel(numLabel, dim: true, fontSize: LobbyStyles.FontSmall);
            row.AddChild(numLabel);

            var nameLabel = new Label
            {
                Text = who,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            bool isOccupied = !slot.IsOpen && !slot.IsClosed && !string.IsNullOrEmpty(slot.DisplayName);
            if (slot.IsReady && i != 0)
            {
                nameLabel.Text = who + "  [READY]";
                LobbyStyles.StyleLabel(nameLabel, fontSize: LobbyStyles.FontBody);
                nameLabel.AddThemeColorOverride("font_color", LobbyStyles.ReadyGreen);
            }
            else if (isOccupied && i != 0)
            {
                nameLabel.Text = who + "  [not ready]";
                LobbyStyles.StyleLabel(nameLabel, dim: true);
            }
            else
            {
                LobbyStyles.StyleLabel(nameLabel, dim: slot.IsOpen || slot.IsClosed);
            }
            row.AddChild(nameLabel);

            if (!string.IsNullOrEmpty(teamTag))
            {
                var teamLabel = new Label { Text = teamTag };
                LobbyStyles.StyleLabel(teamLabel, dim: true, fontSize: LobbyStyles.FontSmall);
                row.AddChild(teamLabel);
            }

            if (isOccupied && i != 0)
            {
                byte kickSlot = (byte)i;
                var kickBtn = new Button { Text = "X", CustomMinimumSize = new Vector2(24, 20) };
                LobbyStyles.StyleButton(kickBtn, danger: true);
                kickBtn.Pressed += () => _relay?.SendKickPlayer(kickSlot);
                row.AddChild(kickBtn);
            }

            _slotContainer.AddChild(row);
        }
    }

    private void OnHostErrorDeferred(int code)
    {
        _roomCodeLabel!.Text = $"Error: {(ErrorCode)code}";
        if (_startBtn != null) _startBtn.Disabled = true;
    }

    private void SyncDropdownsFromRoomState(RoomStatePayload rs)
    {
        _selectedMode = rs.GameMode;
        if (_modeDropdown != null)
        {
            for (int i = 0; i < _modeDropdown.ItemCount; i++)
                if (_modeDropdown.GetItemId(i) == (int)rs.GameMode)
                { _modeDropdown.Selected = i; break; }
        }
        var mapEntry = _mapCatalog.Find(e => e.FileName == rs.MapName || e.FileName == rs.MapName + ".json");
        if (mapEntry.Data != null) _mapData = mapEntry.Data;
        _mapMiniature?.SetMap(_mapData);
    }

    private void UpdateChatSlotNames(RoomStatePayload rs)
    {
        var names = new Dictionary<int, string>();
        for (int i = 0; i < rs.Slots.Length; i++)
        {
            if (!rs.Slots[i].IsOpen && !rs.Slots[i].IsClosed && !string.IsNullOrEmpty(rs.Slots[i].DisplayName))
                names[i] = rs.Slots[i].DisplayName;
        }
        _chatPanel?.UpdateSlotNames(names);
    }

    // ------------------------------------------------------------------
    // Join mode
    // ------------------------------------------------------------------

    private void SetupJoinMode()
    {
        _relay = MultiplayerLaunchData.Relay;
        if (_relay == null)
        {
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
            return;
        }

        LoadMapCatalog();

        var (leftCol, rightCol) = BuildTwoColumnLayout($"JOINED — {MultiplayerLaunchData.JoinCode}");

        // Left: status + slot list + ready button + chat
        _joinStatusLabel = new Label { Text = "Waiting for host…" };
        LobbyStyles.StyleLabel(_joinStatusLabel, dim: true, fontSize: LobbyStyles.FontLabel);
        leftCol.AddChild(_joinStatusLabel);

        leftCol.AddChild(MakeSectionHeader("PLAYERS"));

        _slotContainer = new VBoxContainer();
        _slotContainer.AddThemeConstantOverride("separation", 2);
        var slotScroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 30)
        };
        LobbyStyles.StyleScrollContainer(slotScroll);
        slotScroll.AddChild(_slotContainer);
        leftCol.AddChild(slotScroll);

        // Chat panel
        _chatPanel = new LobbyChatPanel();
        _chatPanel.CustomMinimumSize = new Vector2(0, 120);
        leftCol.AddChild(_chatPanel);

        // Ready button
        _readyBtn = new Button
        {
            Text = "READY",
            CustomMinimumSize = new Vector2(0, 30)
        };
        LobbyStyles.StyleButton(_readyBtn, accent: true);
        _readyBtn.Pressed += OnReadyToggle;
        leftCol.AddChild(_readyBtn);

        var backBtn = new Button { Text = "< LEAVE", CustomMinimumSize = new Vector2(80, 26) };
        LobbyStyles.StyleButton(backBtn);
        backBtn.Pressed += () =>
        {
            _relay?.SendLeaveRoom();
            _relay?.Dispose();
            MultiplayerLaunchData.Relay = null;
            GetTree().ChangeSceneToFile("res://Scenes/LobbyList.tscn");
        };
        leftCol.AddChild(backBtn);

        SetupPingDisplay(leftCol);

        // Right: map info + miniature
        _mapInfoLabel = new Label { Text = "Waiting for room info…" };
        LobbyStyles.StyleLabel(_mapInfoLabel, dim: true);
        rightCol.AddChild(_mapInfoLabel);
        BuildMapMiniature(rightCol);

        // Wire relay events
        _relayRoomStateHandler = (rs) =>
        {
            _latestRoomState = rs;
            CallDeferred(nameof(OnJoinRoomStateDeferred));
        };
        _relayGameStartedHandler = (localId, activeIds) =>
        {
            _pendingLocalId = localId;
            _pendingActiveIds = activeIds;
            CallDeferred(nameof(OnGameStartedDeferred));
        };
        _relayErrorHandler = (code) =>
        {
            if (code == ErrorCode.HostLeft)
                CallDeferred(nameof(OnJoinDisconnectedDeferred), "Host left the lobby.");
            else if (code == ErrorCode.Kicked)
                CallDeferred(nameof(OnJoinDisconnectedDeferred), "You were kicked.");
            else
                CallDeferred(nameof(OnJoinErrorDeferred), (int)code);
        };
        _relayClosedHandler = () =>
            CallDeferred(nameof(OnJoinDisconnectedDeferred), "Connection lost.");

        _relay.RoomStateReceived += _relayRoomStateHandler;
        _relay.GameStarted += _relayGameStartedHandler;
        _relay.ServerError += _relayErrorHandler;
        _relay.ConnectionClosed += _relayClosedHandler;

        var drain = new Godot.Timer { WaitTime = 0.016, Autostart = true };
        drain.Timeout += () => _relay.DrainInbound();
        AddChild(drain);

        _chatPanel.SetRelay(_relay);

        if (MultiplayerLaunchData.PendingRoomState is { } pending)
        {
            _latestRoomState = pending;
            MultiplayerLaunchData.PendingRoomState = null;
            OnJoinRoomStateDeferred();
        }
    }

    private void OnReadyToggle()
    {
        _isReady = !_isReady;
        _relay?.SendSetReady(_isReady);
        _readyBtn!.Text = _isReady ? "UNREADY" : "READY";
        if (_isReady)
        {
            var readyStyle = new StyleBoxFlat
            {
                BgColor = new Color(0.2f, 0.55f, 0.2f, 0.6f),
                BorderColor = new Color(0.3f, 0.7f, 0.3f, 0.4f),
                BorderWidthLeft = 1, BorderWidthRight = 1,
                BorderWidthTop = 1, BorderWidthBottom = 1,
                ContentMarginLeft = 8, ContentMarginRight = 8,
                ContentMarginTop = 3, ContentMarginBottom = 3
            };
            _readyBtn.AddThemeStyleboxOverride("normal", readyStyle);
            _readyBtn.AddThemeColorOverride("font_color", LobbyStyles.ReadyGreen);
        }
        else
        {
            LobbyStyles.StyleButton(_readyBtn, accent: true);
        }
    }

    private void OnJoinRoomStateDeferred()
    {
        if (_latestRoomState == null) return;
        var rs = _latestRoomState;

        _headerLabel.Text = string.IsNullOrEmpty(rs.RoomName) ? "MULTIPLAYER LOBBY" : rs.RoomName.ToUpper();
        _mapInfoLabel!.Text = $"Map: {rs.MapName}   Mode: {rs.GameMode}";

        var mapEntry = _mapCatalog.Find(e => e.FileName == rs.MapName || e.FileName == rs.MapName + ".json");
        if (mapEntry.Data != null)
        {
            _mapData = mapEntry.Data;
            _mapMiniature.SetMap(_mapData);
        }

        RebuildJoinSlotList(rs);
        UpdateChatSlotNames(rs);
    }

    private void RebuildJoinSlotList(RoomStatePayload rs)
    {
        foreach (var child in _slotContainer.GetChildren())
            child.QueueFree();

        for (int i = 0; i < rs.Slots.Length; i++)
        {
            var slot = rs.Slots[i];
            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 4);

            string who;
            if (slot.IsClosed) who = "(closed)";
            else if (slot.IsOpen || string.IsNullOrEmpty(slot.DisplayName)) who = "(open)";
            else who = slot.DisplayName;

            string teamTag = rs.GameMode == GameMode.Teams ? $"T{slot.TeamId + 1}" : "";

            var numLabel = new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(20, 0) };
            LobbyStyles.StyleLabel(numLabel, dim: true, fontSize: LobbyStyles.FontSmall);
            row.AddChild(numLabel);

            var nameLabel = new Label
            {
                Text = who,
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            bool isOccupied = !slot.IsOpen && !slot.IsClosed && !string.IsNullOrEmpty(slot.DisplayName);
            if (slot.IsReady && i != 0)
            {
                nameLabel.Text = who + "  [READY]";
                LobbyStyles.StyleLabel(nameLabel, fontSize: LobbyStyles.FontBody);
                nameLabel.AddThemeColorOverride("font_color", LobbyStyles.ReadyGreen);
            }
            else if (isOccupied && i != 0)
            {
                nameLabel.Text = who + "  [not ready]";
                LobbyStyles.StyleLabel(nameLabel, dim: true);
            }
            else
            {
                LobbyStyles.StyleLabel(nameLabel, dim: slot.IsOpen || slot.IsClosed);
            }
            row.AddChild(nameLabel);

            if (!string.IsNullOrEmpty(teamTag))
            {
                var teamLabel = new Label { Text = teamTag };
                LobbyStyles.StyleLabel(teamLabel, dim: true, fontSize: LobbyStyles.FontSmall);
                row.AddChild(teamLabel);
            }

            _slotContainer.AddChild(row);
        }
    }

    private void OnJoinErrorDeferred(int code)
    {
        _joinStatusLabel!.Text = $"Error: {(ErrorCode)code}";
    }

    private void OnJoinDisconnectedDeferred(string reason)
    {
        if (_navigatingAway) return;
        _joinStatusLabel!.Text = reason;
        if (_readyBtn != null) _readyBtn.Disabled = true;
    }

    private void OnGameStartedDeferred()
    {
        _navigatingAway = true;
        GameLaunchData.MapData = _mapData;
        var assignments = _latestRoomState?.Slots
            .Select((s, i) => new SlotAssignment(i, !s.IsOpen && !s.IsClosed ? i : -1, s.TeamId))
            .Where(a => a.PlayerId != -1)
            .ToList() ?? new List<SlotAssignment>();
        GameLaunchData.Assignments = assignments;

        if (_relay != null && _mapData != null)
        {
            GameLaunchData.MultiplayerSession = new MultiplayerSessionState
            {
                Relay = _relay,
                LocalPlayerId = _pendingLocalId,
                ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
                Map = _mapData,
                Assignments = assignments
            };
        }

        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

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
}
