using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Floating panel with toggle buttons for global spawn control per unit type.
/// Positioned at top-right of game area, below top bar.
/// </summary>
public partial class SpawnToggles : Control
{
    [Signal] public delegate void SpawnToggleChangedEventHandler(int unitType, bool enabled);

    private readonly bool[] _spawnEnabled = [true, true, true]; // Builder, Soldier, Stunner
    private static readonly BlockType[] ToggleTypes = [BlockType.Builder, BlockType.Soldier, BlockType.Stunner];
    private static readonly Color[] ToggleColors =
    [
        new(0.231f, 0.510f, 0.965f), // #3b82f6 Builder blue
        new(0.133f, 0.773f, 0.369f), // #22c55e Soldier green
        new(0.659f, 0.333f, 0.969f), // #a855f7 Stunner purple
    ];
    private static readonly string[] Hotkeys = ["1", "2", "3"];

    private const float ButtonSize = 28f;
    private const float ButtonGap = 6f;
    private const float Padding = 8f;
    private const float PanelWidth = ButtonSize + Padding * 2;
    private const float PanelHeight = ButtonSize * 3 + ButtonGap * 2 + Padding * 2;

    public override void _Ready()
    {
        CustomMinimumSize = new Vector2(PanelWidth, PanelHeight);
        Size = CustomMinimumSize;
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);

        // Panel background
        DrawRect(rect, HudStyles.PanelBgBottom);
        DrawRect(rect, HudStyles.PanelBorder, false, HudStyles.PanelBorderWidth);

        // Shadow effect
        var shadowRect = new Rect2(rect.Position + new Vector2(2, 2), rect.Size);
        DrawRect(shadowRect, new Color(0, 0, 0, 0.3f));
        DrawRect(rect, HudStyles.PanelBgBottom);
        DrawRect(rect, HudStyles.PanelBorder, false, HudStyles.PanelBorderWidth);

        var font = ThemeDB.FallbackFont;

        // Draw toggle buttons
        for (int i = 0; i < 3; i++)
        {
            float y = Padding + i * (ButtonSize + ButtonGap);
            var btnRect = new Rect2(Padding, y, ButtonSize, ButtonSize);

            var color = ToggleColors[i];
            if (!_spawnEnabled[i])
                color = color with { A = 0.35f };

            DrawRect(btnRect, color);

            // Hotkey in corner
            string hotkey = Hotkeys[i];
            var hotkeyPos = new Vector2(btnRect.End.X - 8, btnRect.End.Y - 3);
            DrawString(font, hotkeyPos, hotkey, HorizontalAlignment.Right, -1,
                HudStyles.FontSizeHotkey, new Color(1, 1, 1, 0.6f));
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            int index = GetButtonIndexAt(mb.Position);
            if (index >= 0)
            {
                ToggleSpawn(index);
                AcceptEvent();
            }
        }
    }

    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey key && key.Pressed && !key.Echo)
        {
            int index = key.Keycode switch
            {
                Key.Key1 => 0,
                Key.Key2 => 1,
                Key.Key3 => 2,
                _ => -1
            };
            if (index >= 0)
            {
                ToggleSpawn(index);
                GetViewport().SetInputAsHandled();
            }
        }
    }

    private int GetButtonIndexAt(Vector2 pos)
    {
        for (int i = 0; i < 3; i++)
        {
            float y = Padding + i * (ButtonSize + ButtonGap);
            var btnRect = new Rect2(Padding, y, ButtonSize, ButtonSize);
            if (btnRect.HasPoint(pos))
                return i;
        }
        return -1;
    }

    private void ToggleSpawn(int index)
    {
        _spawnEnabled[index] = !_spawnEnabled[index];
        EmitSignal(SignalName.SpawnToggleChanged, (int)ToggleTypes[index], _spawnEnabled[index]);
        QueueRedraw();
    }

    public bool IsSpawnEnabled(BlockType type)
    {
        int index = type switch
        {
            BlockType.Builder => 0,
            BlockType.Soldier => 1,
            BlockType.Stunner => 2,
            _ => -1
        };
        return index >= 0 && _spawnEnabled[index];
    }
}
