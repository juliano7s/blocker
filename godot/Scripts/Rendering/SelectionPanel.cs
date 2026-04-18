using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Center panel showing control groups and selection info.
/// </summary>
public partial class SelectionPanel : Control
{
    [Signal] public delegate void ControlGroupClickedEventHandler(int groupIndex, bool ctrlHeld);
    [Signal] public delegate void UnitClickedEventHandler(int blockId, bool shiftHeld);

    private IReadOnlyList<Block>? _selectedBlocks;
    private IReadOnlyDictionary<int, IReadOnlyList<int>>? _controlGroups;
    private GameConfig _config = GameConfig.CreateDefault();

    private const float ControlGroupHeight = 26f;
    private const float ControlGroupWidth = 40f;
    private const float UnitIconSize = 16f;
    private const float UnitIconGap = 4f;
    private const float SingleUnitSpriteSize = 44f;

    public void SetSelection(IReadOnlyList<Block>? blocks) => _selectedBlocks = blocks;
    public void SetControlGroups(IReadOnlyDictionary<int, IReadOnlyList<int>>? groups) => _controlGroups = groups;
    public void SetConfig(GameConfig config) => _config = config;

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
        float y = 8f;

        // Control groups row
        DrawControlGroups(font, ref y);

        y += 8f;

        // Selection info
        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
        {
            DrawString(font, new Vector2(10, y + 14), "No selection",
                HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextSecondary);
        }
        else if (_selectedBlocks.Count == 1)
        {
            DrawSingleUnitInfo(font, y);
        }
        else
        {
            DrawMultiUnitInfo(font, y);
        }
    }

    private void DrawControlGroups(Font font, ref float y)
    {
        DrawString(font, new Vector2(10, y + 14), "GROUPS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        float x = 70f;
        for (int i = 1; i <= 9; i++)
        {
            int count = 0;
            if (_controlGroups != null && _controlGroups.TryGetValue(i, out var ids))
                count = ids.Count;

            var groupRect = new Rect2(x, y, ControlGroupWidth, ControlGroupHeight);

            if (count > 0)
            {
                DrawRect(groupRect, HudStyles.PanelBgTop);
                DrawRect(groupRect, HudStyles.PanelBorder, false, 1f);

                string text = $"{i}:{count}";
                var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
                DrawString(font, new Vector2(x + (ControlGroupWidth - textSize.X) / 2, y + 18), text,
                    HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextPrimary);
            }
            else
            {
                DrawRect(groupRect, HudStyles.InnerPanelBg);
                DrawRect(groupRect, HudStyles.PanelBorder with { A = 0.3f }, false, 1f);

                string text = $"{i}";
                var textSize = font.GetStringSize(text, HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall);
                DrawString(font, new Vector2(x + (ControlGroupWidth - textSize.X) / 2, y + 18), text,
                    HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);
            }

            x += ControlGroupWidth + 4f;
        }

        y += ControlGroupHeight;
    }

    private void DrawSingleUnitInfo(Font font, float y)
    {
        var block = _selectedBlocks![0];

        // Unit sprite on left
        var spriteRect = new Rect2(10, y, SingleUnitSpriteSize, SingleUnitSpriteSize);
        BlockIconPainter.Draw(this, block.Type, block.PlayerId, spriteRect, _config, enabled: true);

        float textX = 10 + SingleUnitSpriteSize + 10;
        float textY = y;

        DrawString(font, new Vector2(textX, textY + 16), block.Type.ToString().ToUpper(),
            HorizontalAlignment.Left, -1, HudStyles.FontSizeNormal, HudStyles.TextPrimary);

        textY += 22f;

        // Stats
        string speedText = block.Type switch
        {
            BlockType.Builder => "Speed: Normal",
            BlockType.Soldier => "Speed: Slow",
            BlockType.Stunner => "Speed: Fast",
            BlockType.Jumper => "Speed: Normal",
            BlockType.Warden => "Speed: Fast",
            BlockType.Wall => "Immobile",
            _ => ""
        };
        DrawString(font, new Vector2(textX, textY + 14), speedText,
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);

        // HP for Soldier/Jumper — below sprite
        float belowY = y + SingleUnitSpriteSize + 4f;
        if (block.Type == BlockType.Soldier || block.Type == BlockType.Jumper)
        {
            string hpText = $"HP: {block.Hp}";
            DrawString(font, new Vector2(10, belowY + 14), hpText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);
            belowY += 18f;
        }

        // Root progress
        if (block.State == BlockState.Rooting || block.IsFullyRooted)
        {
            string rootText = block.IsFullyRooted ? "Rooted" : $"Rooting: {block.RootProgress * 100 / 36:F0}%";
            DrawString(font, new Vector2(10, belowY + 14), rootText,
                HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);
        }
    }

    private void DrawMultiUnitInfo(Font font, float y)
    {
        // Count by type
        var counts = new Dictionary<BlockType, int>();
        foreach (var block in _selectedBlocks!)
        {
            counts.TryGetValue(block.Type, out int c);
            counts[block.Type] = c + 1;
        }

        string label = counts.Count == 1
            ? $"SELECTED: {_selectedBlocks.Count} {counts.Keys.First()}S"
            : $"SELECTED: {_selectedBlocks.Count} UNITS";

        DrawString(font, new Vector2(10, y + 16), label,
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);

        y += 28f;

        // Unit icons grid
        float x = 10f;
        float maxX = Size.X - 10f;
        foreach (var block in _selectedBlocks)
        {
            if (x + UnitIconSize > maxX)
            {
                x = 10f;
                y += UnitIconSize + UnitIconGap;
            }

            var iconRect = new Rect2(x, y, UnitIconSize, UnitIconSize);
            BlockIconPainter.Draw(this, block.Type, block.PlayerId, iconRect, _config, enabled: true);

            x += UnitIconSize + UnitIconGap;
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Check control group clicks
            int groupIndex = GetControlGroupAt(mb.Position);
            if (groupIndex >= 0)
            {
                EmitSignal(SignalName.ControlGroupClicked, groupIndex, mb.CtrlPressed);
                AcceptEvent();
                return;
            }

            // Check unit icon clicks
            int blockId = GetUnitIconAt(mb.Position);
            if (blockId >= 0)
            {
                EmitSignal(SignalName.UnitClicked, blockId, mb.ShiftPressed);
                AcceptEvent();
            }
        }
    }

    private int GetControlGroupAt(Vector2 pos)
    {
        float y = 8f;
        float x = 70f;
        for (int i = 1; i <= 9; i++)
        {
            var rect = new Rect2(x, y, ControlGroupWidth, ControlGroupHeight);
            if (rect.HasPoint(pos))
                return i;
            x += ControlGroupWidth + 4f;
        }
        return -1;
    }

    private int GetUnitIconAt(Vector2 pos)
    {
        if (_selectedBlocks == null || _selectedBlocks.Count <= 1)
            return -1;

        float y = 8f + ControlGroupHeight + 8f + 28f;
        float x = 10f;
        float maxX = Size.X - 10f;

        foreach (var block in _selectedBlocks)
        {
            if (x + UnitIconSize > maxX)
            {
                x = 10f;
                y += UnitIconSize + UnitIconGap;
            }

            var iconRect = new Rect2(x, y, UnitIconSize, UnitIconSize);
            if (iconRect.HasPoint(pos))
                return block.Id;

            x += UnitIconSize + UnitIconGap;
        }
        return -1;
    }
}
