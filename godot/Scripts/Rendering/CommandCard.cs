using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Right panel showing available commands for current selection.
/// </summary>
public partial class CommandCard : Control
{
    [Signal] public delegate void CommandClickedEventHandler(string commandKey);

    private IReadOnlyList<Block>? _selectedBlocks;

    private record CommandDef(string Key, string Name, string Icon, string Hotkey, Func<Block, bool> Available, Func<Block, bool>? Conditional = null);

    private static readonly CommandDef[] AllCommands =
    [
        new("root", "Root", "⌾", "F", b => b.Type != BlockType.Wall && !b.IsFullyRooted && b.State != BlockState.Rooting),
        new("uproot", "Uproot", "⊙", "U", b => b.IsFullyRooted || b.State == BlockState.Rooting),
        new("wall", "Wall", "▣", "W", b => b.Type == BlockType.Builder && b.IsFullyRooted),
        new("push", "Push", "↠", "G", b => b.Type == BlockType.Builder && b.IsFullyRooted && !b.IsInFormation),
        new("explode", "Explode", "✸", "D", b => b.Type == BlockType.Soldier, b => b.IsFullyRooted),
        new("stun", "Stun", "⚡", "S", b => b.Type == BlockType.Stunner && b.IsFullyRooted),
        new("jump", "Jump", "⤴", "J", b => b.Type == BlockType.Jumper),
        new("magnet", "Magnet", "🧲", "M", b => b.Type == BlockType.Warden && b.IsFullyRooted),
    ];

    private const float ButtonSize = 32f;
    private const float ButtonGap = 5f;
    private const int Columns = 3;

    public void SetSelection(IReadOnlyList<Block>? blocks) => _selectedBlocks = blocks;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Process(double delta)
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        var rect = new Rect2(Vector2.Zero, Size);
        HudStyles.DrawInnerPanel(this, rect);

        var font = ThemeDB.FallbackFont;

        // Label
        DrawString(font, new Vector2(10, 14), "COMMANDS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
            return;

        // Get available commands
        var available = GetAvailableCommands();

        float startY = 24f;
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var (cmd, isConditional) in available)
        {
            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);

            // Button background
            if (isConditional)
            {
                DrawRect(btnRect, HudStyles.PanelBgTop with { A = 0.4f });
                DrawRect(btnRect, HudStyles.PanelBorder with { A = 0.4f }, false, 1f);
            }
            else
            {
                DrawRect(btnRect, HudStyles.PanelBgTop);
                DrawRect(btnRect, HudStyles.PanelBorder, false, 1f);
            }

            // Icon
            var iconColor = isConditional ? HudStyles.TextDim : HudStyles.TextSecondary;
            var iconSize = font.GetStringSize(cmd.Icon, HorizontalAlignment.Left, -1, 14);
            DrawString(font, new Vector2(x + (ButtonSize - iconSize.X) / 2, y + 20), cmd.Icon,
                HorizontalAlignment.Left, -1, 14, iconColor);

            // Hotkey
            var hotkeyColor = isConditional ? HudStyles.TextDim with { A = 0.5f } : HudStyles.TextDim;
            DrawString(font, new Vector2(btnRect.End.X - 10, btnRect.End.Y - 4), cmd.Hotkey,
                HorizontalAlignment.Right, -1, HudStyles.FontSizeHotkey, hotkeyColor);

            col++;
            if (col >= Columns)
            {
                col = 0;
                x = 10f;
                y += ButtonSize + ButtonGap;
            }
            else
            {
                x += ButtonSize + ButtonGap;
            }
        }
    }

    private List<(CommandDef cmd, bool isConditional)> GetAvailableCommands()
    {
        var result = new List<(CommandDef, bool)>();
        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
            return result;

        foreach (var cmd in AllCommands)
        {
            bool anyAvailable = false;
            bool allConditional = true;

            foreach (var block in _selectedBlocks)
            {
                if (cmd.Available(block))
                {
                    anyAvailable = true;
                    if (cmd.Conditional == null || cmd.Conditional(block))
                        allConditional = false;
                }
            }

            if (anyAvailable)
            {
                bool isConditional = cmd.Conditional != null && allConditional;
                result.Add((cmd, isConditional));
            }
        }

        return result;
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            string? cmdKey = GetCommandAt(mb.Position);
            if (cmdKey != null)
            {
                EmitSignal(SignalName.CommandClicked, cmdKey);
                AcceptEvent();
            }
        }
    }

    private string? GetCommandAt(Vector2 pos)
    {
        var available = GetAvailableCommands();
        float startY = 24f;
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var (cmd, isConditional) in available)
        {
            if (isConditional) // Can't click conditional commands
            {
                col++;
                if (col >= Columns) { col = 0; x = 10f; y += ButtonSize + ButtonGap; }
                else { x += ButtonSize + ButtonGap; }
                continue;
            }

            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);
            if (btnRect.HasPoint(pos))
                return cmd.Key;

            col++;
            if (col >= Columns)
            {
                col = 0;
                x = 10f;
                y += ButtonSize + ButtonGap;
            }
            else
            {
                x += ButtonSize + ButtonGap;
            }
        }
        return null;
    }
}
