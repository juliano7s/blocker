using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Horizontal row of spawn-toggle buttons centered in the top bar.
/// One button per spawnable unit type. Reads toggle state from GameState.
/// </summary>
public partial class SpawnToggles : Control
{
    [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType);

    private GameState? _gameState;
    private int _controllingPlayer;
    private int _hoveredIndex = -1;

    public const float ButtonSize = 30f;
    public const float ButtonGap = 8f;
    public const int UnitCount = 5;
    public const float TotalWidth = UnitCount * ButtonSize + (UnitCount - 1) * ButtonGap;

    private static readonly BlockType[] UnitTypes =
        [BlockType.Builder, BlockType.Soldier, BlockType.Stunner, BlockType.Warden, BlockType.Jumper];

    private static readonly Color[] GlowColors =
    [
        new(0.231f, 0.510f, 0.965f), // Builder  #3b82f6
        new(0.133f, 0.773f, 0.369f), // Soldier  #22c55e
        new(0.659f, 0.333f, 0.969f), // Stunner  #a855f7
        new(0.231f, 0.510f, 0.965f), // Warden   same as Builder
        new(0.133f, 0.773f, 0.369f), // Jumper   same as Soldier
    ];

    private static readonly Key[] HotkeyKeys =
        [Key.Q, Key.W, Key.E, Key.A, Key.S];

    private static readonly string[] HotkeyLabels =
        ["Alt+Q", "Alt+W", "Alt+E", "Alt+A", "Alt+S"];

    private static readonly string[] UnitNames =
        ["Builder", "Soldier", "Stunner", "Warden", "Jumper"];

    public void SetGameState(GameState state) => _gameState = state;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

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

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;

        for (int i = 0; i < UnitCount; i++)
        {
            var btnRect = GetButtonRect(i);
            bool enabled = IsEnabled(i);
            bool hovered = i == _hoveredIndex;
            var glowColor = GlowColors[i];

            // Button background
            DrawRect(btnRect, new Color(0f, 0f, 0f, hovered ? 0.35f : 0.2f));

            // Sprite
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

            // Glow ring when enabled
            if (enabled)
            {
                DrawRect(btnRect, glowColor, false, 2f);
                // Outer soft glow
                var outerRect = btnRect.Grow(1.5f);
                DrawRect(outerRect, glowColor with { A = 0.25f }, false, 1f);
            }

            // Hover brightness overlay
            if (hovered)
                DrawRect(btnRect, new Color(1f, 1f, 1f, 0.08f));
        }

        // Tooltip for hovered button
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
                EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                AcceptEvent();
                QueueRedraw();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo && key.AltPressed)
        {
            int index = -1;
            for (int i = 0; i < HotkeyKeys.Length; i++)
            {
                if (key.Keycode == HotkeyKeys[i]) { index = i; break; }
            }
            if (index >= 0)
            {
                EmitSignal(SignalName.SpawnToggleChanged, (int)UnitTypes[index]);
                GetViewport().SetInputAsHandled();
                QueueRedraw();
            }
        }
    }

    private bool IsEnabled(int index)
    {
        if (_gameState == null) return true;
        var player = _gameState.Players.Find(p => p.Id == _controllingPlayer);
        return player == null || !player.SpawnDisabled.Contains(UnitTypes[index]);
    }

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
