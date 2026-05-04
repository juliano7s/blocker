using Blocker.Game.Config;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game.Rendering;

public enum MessageCategory { System, Alert, Chat }

public partial class MessageArea : CanvasLayer
{
    private const int MaxMessages = 50;
    private const int MaxVisible = 8;
    private const float DisplayDuration = 5f;
    private const float FadeDuration = 1f;
    private const float LineHeight = 18f;
    private const float AreaWidth = 420f;
    private const int MaxChatLength = 128;

    private readonly MessageEntry[] _buffer = new MessageEntry[MaxMessages];
    private int _head;
    private int _count;

    private GameState? _gameState;
    private GameConfig _config = GameConfig.CreateDefault();
    private int _controllingPlayer;
    private int _lastProcessedTick = -1;

    private IRelayClient? _relay;
    private Dictionary<int, string>? _slotNames;

    private bool _active;
    private MessageDrawControl _drawControl = null!;
    private LineEdit _chatInput = null!;
    private Action<int, string>? _chatHandler;

    public override void _Ready()
    {
        Layer = 10;

        _drawControl = new MessageDrawControl(this);
        _drawControl.AnchorsPreset = (int)Control.LayoutPreset.FullRect;
        _drawControl.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(_drawControl);

        _chatInput = new LineEdit();
        _chatInput.PlaceholderText = "Press Enter to chat...";
        _chatInput.MaxLength = MaxChatLength;
        _chatInput.Visible = false;
        _chatInput.MouseFilter = Control.MouseFilterEnum.Stop;

        var inputStyle = new StyleBoxFlat();
        inputStyle.BgColor = HudStyles.PanelBgTop;
        inputStyle.BorderColor = HudStyles.PanelBorder;
        inputStyle.SetBorderWidthAll(1);
        inputStyle.SetContentMarginAll(4);
        _chatInput.AddThemeStyleboxOverride("normal", inputStyle);
        _chatInput.AddThemeStyleboxOverride("focus", inputStyle);
        _chatInput.AddThemeColorOverride("font_color", HudStyles.TextPrimary);
        _chatInput.AddThemeFontSizeOverride("font_size", HudStyles.FontSizeNormal);

        _chatInput.AnchorLeft = 0.5f;
        _chatInput.AnchorRight = 0.5f;
        _chatInput.AnchorTop = 1f;
        _chatInput.AnchorBottom = 1f;
        float bottomOffset = HudStyles.BottomPanelMargin
            + Mathf.Max(HudStyles.MinimapSize, HudStyles.CommandCardHeight) + 8f;
        _chatInput.OffsetLeft = -AreaWidth / 2f;
        _chatInput.OffsetRight = AreaWidth / 2f;
        _chatInput.OffsetTop = -bottomOffset;
        _chatInput.OffsetBottom = -bottomOffset + 24f;

        _chatInput.TextSubmitted += OnChatSubmitted;
        AddChild(_chatInput);
    }

    public override void _ExitTree()
    {
        if (_relay != null && _chatHandler != null)
            _relay.ChatReceived -= _chatHandler;
    }

    public void SetGameState(GameState state) => _gameState = state;
    public void SetConfig(GameConfig config) => _config = config;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

    public void SetRelay(IRelayClient? relay, Dictionary<int, string>? slotNames)
    {
        if (_relay != null && _chatHandler != null)
            _relay.ChatReceived -= _chatHandler;

        _relay = relay;
        _slotNames = slotNames;

        if (_relay != null)
        {
            _chatHandler = (slotId, text) =>
            {
                Audio.UISoundManager.Instance?.PlayChatReceive();
                string name = _slotNames != null && _slotNames.TryGetValue(slotId, out var n)
                    ? n : $"Player {slotId}";
                Color color = _config.GetPalette(slotId).Base;
                AddMessage(MessageCategory.Chat, text, color, name);
            };
            _relay.ChatReceived += _chatHandler;
        }
    }

    public void AddSystemMessage(string text) =>
        AddMessage(MessageCategory.System, text, null, null);

    public void AddAlert(string text) =>
        AddMessage(MessageCategory.Alert, text, null, null);

    private void AddMessage(MessageCategory category, string text, Color? playerColor, string? playerName)
    {
        float gameTime = _gameState != null ? _gameState.TickNumber / 12f : 0f;
        _buffer[_head] = new MessageEntry
        {
            Category = category,
            Text = text,
            PlayerColor = playerColor,
            PlayerName = playerName,
            GameTime = gameTime,
            CreatedAtReal = Time.GetTicksMsec() / 1000.0,
        };
        _head = (_head + 1) % MaxMessages;
        if (_count < MaxMessages) _count++;
        _drawControl.QueueRedraw();
    }

    public override void _UnhandledInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.Keycode == Key.Enter)
        {
            if (!_active)
            {
                if (_relay != null)
                {
                    Activate();
                    GetViewport().SetInputAsHandled();
                }
            }
        }
        else if (@event is InputEventKey escKey && escKey.Pressed && !escKey.Echo && escKey.Keycode == Key.Escape)
        {
            if (_active)
            {
                Deactivate();
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private void Activate()
    {
        _active = true;
        _chatInput.Visible = true;
        _chatInput.Clear();
        _chatInput.GrabFocus();
        _drawControl.QueueRedraw();
    }

    private void Deactivate()
    {
        _active = false;
        _chatInput.Visible = false;
        _chatInput.ReleaseFocus();
        RefreshDisplayTimers();
        _drawControl.QueueRedraw();
    }

    private void OnChatSubmitted(string text)
    {
        text = text.Trim();
        if (!string.IsNullOrEmpty(text) && _relay != null)
        {
            Audio.UISoundManager.Instance?.PlayChatSend();
            _relay.SendChat(text);
            AddMessage(MessageCategory.Chat, text,
                _config.GetPalette(_controllingPlayer).Base, "You");
        }
        Deactivate();
    }

    private void RefreshDisplayTimers()
    {
        double now = Time.GetTicksMsec() / 1000.0;
        int visible = Mathf.Min(_count, MaxVisible);
        for (int i = 0; i < visible; i++)
        {
            int idx = ((_head - 1 - i) % MaxMessages + MaxMessages) % MaxMessages;
            _buffer[idx].CreatedAtReal = now;
        }
    }

    public void ProcessVisualEvents()
    {
        if (_gameState == null) return;
        if (_gameState.TickNumber == _lastProcessedTick) return;
        _lastProcessedTick = _gameState.TickNumber;

        foreach (var evt in _gameState.VisualEvents)
        {
            switch (evt.Type)
            {
                case VisualEventType.PlayerEliminated:
                    string pName = evt.PlayerId.HasValue ? $"Player {evt.PlayerId.Value}" : "A player";
                    AddSystemMessage($"{pName} eliminated");
                    break;
                case VisualEventType.PopCapWarning:
                    AddAlert("Population cap reached");
                    break;
                case VisualEventType.GameOver:
                    AddAlert("Game over");
                    break;
            }
        }
    }

    private struct MessageEntry
    {
        public MessageCategory Category;
        public string Text;
        public Color? PlayerColor;
        public string? PlayerName;
        public float GameTime;
        public double CreatedAtReal;
    }

    private partial class MessageDrawControl : Control
    {
        private readonly MessageArea _area;
        private bool _needsRedraw;

        private static readonly Color SystemColor = new(0.627f, 0.678f, 0.784f);   // #a0adc8
        private static readonly Color AlertColor = new(0.988f, 0.506f, 0.506f);    // #fc8181
        private static readonly Color ChatTextColor = new(0.886f, 0.910f, 0.941f); // #e2e8f0
        private static readonly Color TimestampColor = new(0.443f, 0.502f, 0.596f);// #718096

        public MessageDrawControl(MessageArea area) => _area = area;

        public override void _Process(double delta)
        {
            bool hasVisible = false;
            double now = Time.GetTicksMsec() / 1000.0;
            int visible = Mathf.Min(_area._count, MaxVisible);
            for (int i = 0; i < visible; i++)
            {
                int idx = ((_area._head - 1 - i) % MaxMessages + MaxMessages) % MaxMessages;
                float alpha = ComputeAlpha(ref _area._buffer[idx], now);
                if (alpha > 0f) { hasVisible = true; break; }
            }

            if (hasVisible || _area._active)
                QueueRedraw();
        }

        public override void _Draw()
        {
            if (_area._count == 0) return;

            var viewport = GetViewportRect().Size;
            var font = ThemeDB.FallbackFont;
            double now = Time.GetTicksMsec() / 1000.0;

            float bottomOffset = HudStyles.BottomPanelMargin
                + Mathf.Max(HudStyles.MinimapSize, HudStyles.CommandCardHeight) + 8f;
            float baseY = viewport.Y - bottomOffset - (_area._active ? 28f : 4f);
            float centerX = viewport.X / 2f;
            float leftX = centerX - AreaWidth / 2f;

            int visible = Mathf.Min(_area._count, MaxVisible);
            for (int i = 0; i < visible; i++)
            {
                int idx = ((_area._head - 1 - i) % MaxMessages + MaxMessages) % MaxMessages;
                ref var msg = ref _area._buffer[idx];
                float alpha = _area._active ? 1f : ComputeAlpha(ref msg, now);
                if (alpha <= 0f) continue;

                float y = baseY - (i * LineHeight);
                float x = leftX;

                // Timestamp
                int totalSec = (int)msg.GameTime;
                int min = totalSec / 60;
                int sec = totalSec % 60;
                string ts = $"[{min:D2}:{sec:D2}]";
                DrawString(font, new Vector2(x, y), ts, HorizontalAlignment.Left,
                    -1, HudStyles.FontSizeNormal, TimestampColor with { A = alpha });
                x += font.GetStringSize(ts, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X + 4f;

                // Player name (for chat)
                if (msg.PlayerName != null && msg.PlayerColor.HasValue)
                {
                    string prefix = msg.Category == MessageCategory.Chat
                        ? $"[{msg.PlayerName}]: "
                        : $"[{msg.PlayerName}] ";
                    DrawString(font, new Vector2(x, y), prefix, HorizontalAlignment.Left,
                        -1, HudStyles.FontSizeNormal, msg.PlayerColor.Value with { A = alpha });
                    x += font.GetStringSize(prefix, HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal).X;
                }

                // Message text
                Color textColor = msg.Category switch
                {
                    MessageCategory.System => SystemColor,
                    MessageCategory.Alert => AlertColor,
                    MessageCategory.Chat => ChatTextColor,
                    _ => SystemColor,
                };
                DrawString(font, new Vector2(x, y), msg.Text, HorizontalAlignment.Left,
                    -1, HudStyles.FontSizeNormal, textColor with { A = alpha });
            }
        }

        private static float ComputeAlpha(ref MessageEntry msg, double now)
        {
            double elapsed = now - msg.CreatedAtReal;
            if (elapsed < DisplayDuration) return 1f;
            if (elapsed < DisplayDuration + FadeDuration)
                return 1f - (float)((elapsed - DisplayDuration) / FadeDuration);
            return 0f;
        }
    }
}
