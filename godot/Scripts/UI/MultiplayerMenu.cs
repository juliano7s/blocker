using Blocker.Game.Net;
using Godot;

namespace Blocker.Game.UI;

public enum MultiplayerIntent { None, Host, Join }

/// <summary>
/// Staging area for handing state from MultiplayerMenu / SlotConfigScreen
/// into GameManager. Separate from GameLaunchData because the Relay client
/// outlives the lobby screen and needs to survive the scene change.
/// </summary>
public static class MultiplayerLaunchData
{
    public static MultiplayerIntent Intent;
    public static string JoinCode = "";
    public static RelayClient? Relay;

    /// <summary>
    /// True when SlotConfigScreen should reattach to an existing relay+room
    /// after a rematch (skip the Create Room step and jump straight to the
    /// lobby panel). Cleared by SlotConfigScreen on consumption.
    /// </summary>
    public static bool RematchReattach;

    /// <summary>
    /// Stashed RoomState from the rematch broadcast. Consumed by SlotConfigScreen
    /// on reattach to seed the lobby panel before a second broadcast arrives.
    /// </summary>
    public static RoomStatePayload? PendingRoomState;
}

public partial class MultiplayerMenu : Control
{
    private Label _statusLabel = null!;
    private Button _hostBtn = null!;
    private Button _joinBtn = null!;
    private LineEdit _codeEdit = null!;
    private RelayClient _relay = null!;

    public override async void _Ready()
    {
        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -180, OffsetRight = 180,
            OffsetTop = -160, OffsetBottom = 160
        };
        vbox.AddThemeConstantOverride("separation", 14);
        AddChild(vbox);

        var title = new Label { Text = "Multiplayer", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(title);

        _statusLabel = new Label { Text = "Connecting to server…", HorizontalAlignment = HorizontalAlignment.Center };
        vbox.AddChild(_statusLabel);

        _hostBtn = new Button { Text = "Host Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
        _hostBtn.Pressed += OnHostPressed;
        vbox.AddChild(_hostBtn);

        vbox.AddChild(new HSeparator());

        _codeEdit = new LineEdit { PlaceholderText = "Room code (4 chars)", MaxLength = 4 };
        vbox.AddChild(_codeEdit);
        _joinBtn = new Button { Text = "Join Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
        _joinBtn.Pressed += OnJoinPressed;
        vbox.AddChild(_joinBtn);

        vbox.AddChild(new HSeparator());

        var backBtn = new Button { Text = "< Back", CustomMinimumSize = new Vector2(0, 40) };
        backBtn.Pressed += () => {
            _relay?.Dispose();
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        vbox.AddChild(backBtn);

        // Connect + say hello.
        _relay = new RelayClient();
        bool ok = await _relay.ConnectAsync(RelayClientConfig.ResolvedUrl, "Player");
        if (!ok)
        {
            _statusLabel.Text = $"Cannot reach server: {_relay.LastError}";
            return;
        }
        _relay.HelloAcked += () => CallDeferred(nameof(OnHelloAcked));
        _relay.ConnectionClosed += () => CallDeferred(nameof(OnClosed));
        _relay.ServerError += (e) => CallDeferred(nameof(OnServerError), (int)e);

        // Drain inbound every frame while the menu is up.
        var drainTimer = new Godot.Timer { WaitTime = 0.016, Autostart = true };
        drainTimer.Timeout += () => _relay.DrainInbound();
        AddChild(drainTimer);
    }

    private void OnHelloAcked()
    {
        _statusLabel.Text = "Connected.";
        _hostBtn.Disabled = false;
        _joinBtn.Disabled = false;
    }

    private void OnClosed()
    {
        _statusLabel.Text = "Disconnected.";
        _hostBtn.Disabled = true;
        _joinBtn.Disabled = true;
    }

    private void OnServerError(int code)
    {
        _statusLabel.Text = $"Server error: {(Blocker.Simulation.Net.ErrorCode)code}";
    }

    private void OnHostPressed()
    {
        MultiplayerLaunchData.Intent = MultiplayerIntent.Host;
        MultiplayerLaunchData.Relay = _relay;
        GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
    }

    private void OnJoinPressed()
    {
        var code = _codeEdit.Text.ToUpperInvariant().Trim();
        if (code.Length != 4) { _statusLabel.Text = "Code must be 4 characters."; return; }
        MultiplayerLaunchData.Intent = MultiplayerIntent.Join;
        MultiplayerLaunchData.JoinCode = code;
        MultiplayerLaunchData.Relay = _relay;
        _relay.SendJoinRoom(code, 1);
        GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
    }
}
