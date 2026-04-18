using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Horizontal row of spawn-toggle buttons centered in the top bar.
/// One button per spawnable unit type. Reads toggle state from local optimistic state
/// (synced from GameState on init/player-change; updated immediately on click/hotkey).
/// </summary>
public partial class SpawnToggles : Control
{
    [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType);

    private GameState? _gameState;
    private GameConfig? _config;
    private int _controllingPlayer;
    private int _hoveredIndex = -1;
    private readonly HashSet<BlockType> _localDisabled = new();

    public const float ButtonSize = 30f;
    public const float ButtonGap = 8f;
    public const int UnitCount = 5;
    public const float TotalWidth = UnitCount * ButtonSize + (UnitCount - 1) * ButtonGap;

    private static readonly BlockType[] UnitTypes =
        [BlockType.Builder, BlockType.Soldier, BlockType.Stunner, BlockType.Warden, BlockType.Jumper];

    private static readonly Key[] HotkeyKeys =
        [Key.Q, Key.W, Key.E, Key.A, Key.S];

    private static readonly string[] HotkeyLabels =
        ["Alt+Q", "Alt+W", "Alt+E", "Alt+A", "Alt+S"];

    private static readonly string[] UnitNames =
        ["Builder", "Soldier", "Stunner", "Warden", "Jumper"];

    public void SetGameState(GameState state)
    {
        _gameState = state;
        SyncLocalFromState();
    }

    public void SetConfig(GameConfig config)
    {
        _config = config;
        QueueRedraw();
    }

    public void SetControllingPlayer(int playerId)
    {
        _controllingPlayer = playerId;
        SyncLocalFromState();
        QueueRedraw();
    }

    private void SyncLocalFromState()
    {
        _localDisabled.Clear();
        if (_gameState == null) return;
        var player = _gameState.Players.Find(p => p.Id == _controllingPlayer);
        if (player != null)
            foreach (var t in player.SpawnDisabled)
                _localDisabled.Add(t);
    }

    private void ToggleLocal(int index)
    {
        var t = UnitTypes[index];
        if (!_localDisabled.Remove(t))
            _localDisabled.Add(t);
        QueueRedraw();
    }

    // Glow color sourced from player palette — same colors the in-game sprites use.
    private Color GetGlowColor(int index)
    {
        if (_config == null) return HudStyles.PanelBorder;
        var palette = _config.GetPalette(_controllingPlayer);
        return UnitTypes[index] switch
        {
            BlockType.Stunner => palette.StunnerFill,
            BlockType.Soldier or BlockType.Jumper => palette.SoldierFill,
            _ => palette.BuilderFill
        };
    }

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(TotalWidth, ButtonSize);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Notification(int what)
    {
        if (what == NotificationMouseExit)
        {
            _hoveredIndex = -1;
            MouseDefaultCursorShape = CursorShape.Arrow;
            QueueRedraw();
        }
    }

    // _Input fires before _UnhandledInput so this intercepts Alt+key before SelectionManager sees it.
    // PhysicalKeycode avoids Windows Alt-key remapping that changes Keycode when modifier is held.
    public override void _Input(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.AltPressed)
        {
            int index = -1;
            for (int i = 0; i < HotkeyKeys.Length; i++)
            {
                if (key.PhysicalKeycode == HotkeyKeys[i]) { index = i; break; }
            }
            if (index >= 0)
            {
                ToggleLocal(index);
                EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        for (int i = 0; i < UnitCount; i++)
        {
            var btnRect = GetButtonRect(i);
            bool enabled = IsEnabled(i);
            bool hovered = i == _hoveredIndex;
            var glowColor = GetGlowColor(i);

            DrawRect(btnRect, new Color(0f, 0f, 0f, hovered ? 0.35f : 0.2f));

            var sprite = SpriteFactory.GetSprite(UnitTypes[i], _controllingPlayer);
            if (sprite != null)
            {
                float spriteInset = (ButtonSize - 22f) / 2f;
                var spriteRect = new Rect2(
                    btnRect.Position + new Vector2(spriteInset, spriteInset),
                    new Vector2(22f, 22f));
                DrawTextureRect(sprite, spriteRect, false,
                    enabled ? Colors.White : new Color(1f, 1f, 1f, 0.28f));
            }

            if (enabled)
            {
                DrawRect(btnRect, glowColor, false, 2f);
                DrawRect(btnRect.Grow(1.5f), glowColor with { A = 0.25f }, false, 1f);
            }

            if (hovered)
                DrawRect(btnRect, new Color(1f, 1f, 1f, 0.08f));
        }

        if (_hoveredIndex >= 0)
        {
            var btnRect = GetButtonRect(_hoveredIndex);
            string tipText = $"{HotkeyLabels[_hoveredIndex]} — {UnitNames[_hoveredIndex]}";
            var tipSize = font.GetStringSize(tipText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
            float tipPad = 5f;
            float tipW = tipSize.X + tipPad * 2;
            float tipH = tipSize.Y + tipPad * 2;
            float tipX = btnRect.GetCenter().X - tipW / 2f;
            float tipY = btnRect.Position.Y - tipH - 4f;
            var tipRect = new Rect2(tipX, tipY, tipW, tipH);
            DrawRect(tipRect, new Color(0.05f, 0.07f, 0.10f, 0.92f));
            DrawRect(tipRect, HudStyles.PanelBorder, false, 1f);
            DrawString(font, new Vector2(tipX + tipPad, tipY + tipPad + tipSize.Y - 2f),
                tipText, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextPrimary);
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseMotion mm)
        {
            int newHover = GetButtonIndexAt(mm.Position);
            if (newHover != _hoveredIndex)
            {
                _hoveredIndex = newHover;
                MouseDefaultCursorShape = newHover >= 0 ? CursorShape.PointingHand : CursorShape.Arrow;
                QueueRedraw();
            }
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int index = GetButtonIndexAt(mb.Position);
            if (index >= 0)
            {
                ToggleLocal(index);
                EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                AcceptEvent();
            }
        }
    }

    private bool IsEnabled(int index) => !_localDisabled.Contains(UnitTypes[index]);

    private static Rect2 GetButtonRect(int index)
    {
        float x = index * (ButtonSize + ButtonGap);
        return new Rect2(x, 0, ButtonSize, ButtonSize);
    }

    private int GetButtonIndexAt(Vector2 pos)
    {
        for (int i = 0; i < UnitCount; i++)
            if (GetButtonRect(i).HasPoint(pos)) return i;
        return -1;
    }
}
