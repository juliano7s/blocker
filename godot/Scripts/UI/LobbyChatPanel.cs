using Blocker.Game.Net;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class LobbyChatPanel : VBoxContainer
{
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messageList = null!;
    private bool _needsScroll;
    private LineEdit _input = null!;
    private RelayClient? _relay;
    private Action<int, string>? _chatHandler;
    private readonly Dictionary<int, string> _slotNames = new();

    public override void _Ready()
    {
        AddThemeConstantOverride("separation", 3);

        var headerLabel = new Label { Text = "CHAT" };
        LobbyStyles.StyleLabel(headerLabel, dim: true, fontSize: LobbyStyles.FontSmall);
        AddChild(headerLabel);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 40)
        };
        LobbyStyles.StyleScrollContainer(_scroll);
        AddChild(_scroll);

        _messageList = new VBoxContainer();
        _messageList.AddThemeConstantOverride("separation", 1);
        _messageList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        _scroll.AddChild(_messageList);

        _input = new LineEdit
        {
            PlaceholderText = "Type a message…",
            MaxLength = 128,
            CustomMinimumSize = new Vector2(0, 24)
        };
        LobbyStyles.StyleInput(_input);
        _input.TextSubmitted += OnTextSubmitted;
        AddChild(_input);
    }

    public void SetRelay(RelayClient relay, Dictionary<int, string>? slotNames = null)
    {
        DetachRelay();
        _relay = relay;
        if (slotNames != null)
        {
            _slotNames.Clear();
            foreach (var kv in slotNames) _slotNames[kv.Key] = kv.Value;
        }
        _chatHandler = (slotId, text) => CallDeferred(nameof(OnChatDeferred), slotId, text);
        _relay.ChatReceived += _chatHandler;
    }

    public void UpdateSlotNames(Dictionary<int, string> names)
    {
        _slotNames.Clear();
        foreach (var kv in names) _slotNames[kv.Key] = kv.Value;
    }

    public void DetachRelay()
    {
        if (_relay != null && _chatHandler != null)
            _relay.ChatReceived -= _chatHandler;
        _relay = null;
        _chatHandler = null;
    }

    public override void _ExitTree()
    {
        DetachRelay();
    }

    private void OnChatDeferred(int slotId, string text)
    {
        Audio.UISoundManager.Instance?.PlayChatReceive();
        string name = _slotNames.TryGetValue(slotId, out var n) ? n : $"Player {slotId}";
        AddChatMessage(name, text);
    }

    public void AddChatMessage(string playerName, string text)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(0, 16),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.AddThemeFontSizeOverride("normal_font_size", LobbyStyles.FontSmall);
        label.Text = $"[color=#{LobbyStyles.Cyan.ToHtml(false)}]{playerName}[/color]: {text}";
        _messageList.AddChild(label);

        _needsScroll = true;

        while (_messageList.GetChildCount() > 50)
            _messageList.GetChild(0).QueueFree();
    }

    public void AddSystemMessage(string text)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 14)
        };
        LobbyStyles.StyleLabel(label, dim: true, fontSize: LobbyStyles.FontSmall);
        _messageList.AddChild(label);
        _needsScroll = true;
    }

    public override void _Process(double delta)
    {
        if (_needsScroll)
        {
            var bar = _scroll.GetVScrollBar();
            _scroll.ScrollVertical = (int)bar.MaxValue;
            _needsScroll = false;
        }
    }

    private void OnTextSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        Audio.UISoundManager.Instance?.PlayChatSend();
        _relay?.SendChat(text);
        AddChatMessage("You", text);
        _input.Text = "";
    }
}
