using Blocker.Game.Config;
using Blocker.Game.Input;
using Blocker.Simulation.Blocks;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Right panel showing available commands and blueprint icons.
/// Top section: unit commands. Bottom section: blueprint icons.
/// </summary>
public partial class CommandCard : Control
{
    [Signal] public delegate void CommandClickedEventHandler(CommandAction commandKey);
    [Signal] public delegate void BlueprintClickedEventHandler(BlueprintMode.BlueprintType blueprintType);

    private IReadOnlyList<Block>? _selectedBlocks;
    private GameConfig? _config;
    private int _controllingPlayer;

    public void SetConfig(GameConfig config) => _config = config;
    public void SetControllingPlayer(int playerId) => _controllingPlayer = playerId;

    // Hover state
    private CommandAction? _hoveredCommandKey;
    private BlueprintMode.BlueprintType? _hoveredBlueprintType;

    private record CommandDef(CommandAction Key, string Name, string Icon, string Hotkey, Func<Block, bool> Available, Func<Block, bool>? Conditional = null);
    private record BlueprintDef(BlueprintMode.BlueprintType Type, string Name, string Icon, string Hotkey);

    private static readonly CommandDef[] AllCommands =
    [
        new(CommandAction.Root, "Root", "⌾", "F", b => b.Type != BlockType.Wall && !b.IsFullyRooted && b.State != BlockState.Rooting),
        new(CommandAction.Uproot, "Uproot", "⊙", "F", b => b.IsFullyRooted || b.State == BlockState.Rooting),
        new(CommandAction.Wall, "Wall", "▣", "V", b => b.Type == BlockType.Builder && b.IsFullyRooted),
        new(CommandAction.Push, "Push", "↠", "G", b => b.Type == BlockType.Builder && b.IsFullyRooted && !b.IsInFormation),
        new(CommandAction.Explode, "Explode", "✸", "D", b => b.Type == BlockType.Soldier, b => b.IsFullyRooted),
        new(CommandAction.Stun, "Stun", "⚡", "S", b => b.Type == BlockType.Stunner && b.IsFullyRooted),
        new(CommandAction.Jump, "Jump", "⤴", "F", b => b.Type == BlockType.Jumper),
        new(CommandAction.Magnet, "Magnet", "🧲", "D", b => b.Type == BlockType.Warden && b.IsFullyRooted),
        new(CommandAction.Tower, "Tower", "🗼", "Z", b => b.Type == BlockType.Soldier || b.Type == BlockType.Stunner, b => b.IsFullyRooted && !b.IsInFormation),
    ];

    private static readonly BlueprintDef[] AllBlueprints =
    [
        new(BlueprintMode.BlueprintType.BuilderNest, "Builder Nest", "🏠", "Q"),
        new(BlueprintMode.BlueprintType.SoldierNest, "Soldier Nest", "⚔", "W"),
        new(BlueprintMode.BlueprintType.StunnerNest, "Stunner Nest", "⚡", "E"),
        new(BlueprintMode.BlueprintType.Supply, "Supply", "📦", "R"),
        new(BlueprintMode.BlueprintType.StunTower, "Stun Tower", "🗼", "T"),
        new(BlueprintMode.BlueprintType.SoldierTower, "Soldier Tower", "🏰", "Y"),
    ];

    private static readonly Dictionary<BlueprintMode.BlueprintType, Texture2D?> _blueprintSprites = new();
    private static readonly Dictionary<(BlueprintMode.BlueprintType, int), ImageTexture?> _tintCache = new();
    private static bool _spritesLoaded;

    private static Texture2D? GetBlueprintSprite(BlueprintMode.BlueprintType type, GameConfig? config, int playerId)
    {
        if (!_spritesLoaded)
        {
            _spritesLoaded = true;
            foreach (BlueprintMode.BlueprintType bt in System.Enum.GetValues<BlueprintMode.BlueprintType>())
            {
                var path = $"res://Assets/Sprites/{ToKebab(bt.ToString())}-blueprint.png";
                _blueprintSprites[bt] = ResourceLoader.Exists(path) ? GD.Load<Texture2D>(path) : null;
            }
        }

        if (config == null)
        {
            _blueprintSprites.TryGetValue(type, out var raw);
            return raw;
        }

        var key = (type, playerId);
        if (_tintCache.TryGetValue(key, out var cached)) return cached;

        if (!_blueprintSprites.TryGetValue(type, out var src) || src == null)
        {
            _tintCache[key] = null;
            return null;
        }

        var tinted = BakeTinted(src, config.GetPalette(playerId).Base);
        _tintCache[key] = tinted;
        return tinted;
    }

    /// <summary>
    /// Bake a team-tinted version: tint factor = (1 - saturation), so grayscale pixels
    /// fully take the team color while saturated colored pixels are preserved.
    /// </summary>
    private static ImageTexture BakeTinted(Texture2D src, Color tint)
    {
        var img = src.GetImage();
        if (img == null) return ImageTexture.CreateFromImage(Image.CreateEmpty(1, 1, false, Image.Format.Rgba8));
        if (img.IsCompressed()) img.Decompress();
        if (img.GetFormat() != Image.Format.Rgba8) img.Convert(Image.Format.Rgba8);

        int w = img.GetWidth(), h = img.GetHeight();
        for (int y = 0; y < h; y++)
        {
            for (int x = 0; x < w; x++)
            {
                var p = img.GetPixel(x, y);
                if (p.A <= 0f) continue;

                float maxC = Mathf.Max(p.R, Mathf.Max(p.G, p.B));
                float minC = Mathf.Min(p.R, Mathf.Min(p.G, p.B));
                float sat = maxC > 0.0001f ? (maxC - minC) / maxC : 0f;
                float t = 1f - sat;

                float r = Mathf.Lerp(p.R, p.R * tint.R, t);
                float g = Mathf.Lerp(p.G, p.G * tint.G, t);
                float b = Mathf.Lerp(p.B, p.B * tint.B, t);
                img.SetPixel(x, y, new Color(r, g, b, p.A));
            }
        }
        return ImageTexture.CreateFromImage(img);
    }

    private static string ToKebab(string pascal)
    {
        var sb = new System.Text.StringBuilder();
        for (int i = 0; i < pascal.Length; i++)
        {
            char c = pascal[i];
            if (i > 0 && char.IsUpper(c)) sb.Append('-');
            sb.Append(char.ToLowerInvariant(c));
        }
        return sb.ToString();
    }

    private const float ButtonSize = 38f;
    private const float ButtonGap = 4f;
    private const int Columns = 6;
    private const float SectionGap = 10f;

    private float _blueprintSectionY;

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
        float y = 8f;

        // === COMMANDS SECTION ===
        DrawString(font, new Vector2(10, y + 12), "COMMANDS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);
        y += 20f;

        if (_selectedBlocks != null && _selectedBlocks.Count > 0)
        {
            var available = GetAvailableCommands();
            y = DrawCommandGrid(font, available, y);
        }
        else
        {
            DrawString(font, new Vector2(10, y + 14), "No selection",
                HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextSecondary);
            y += 24f;
        }

        y += SectionGap;

        // === BLUEPRINTS SECTION ===
        _blueprintSectionY = y;
        DrawString(font, new Vector2(10, y + 12), "BLUEPRINTS",
            HorizontalAlignment.Left, -1, HudStyles.FontSizeSmall, HudStyles.TextDim);
        y += 20f;

        DrawBlueprintGrid(font, y);
    }

    private float DrawCommandGrid(Font font, List<(CommandDef cmd, bool isConditional)> commands, float startY)
    {
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var (cmd, isConditional) in commands)
        {
            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);
            bool hovered = !isConditional && _hoveredCommandKey == cmd.Key;

            // Button background
            if (isConditional)
            {
                DrawRect(btnRect, HudStyles.PanelBgTop with { A = 0.4f });
                DrawRect(btnRect, HudStyles.PanelBorder with { A = 0.4f }, false, 1f);
            }
            else if (hovered)
            {
                DrawRect(btnRect, HudStyles.PanelBgTop.Lightened(0.15f));
                DrawRect(btnRect, HudStyles.PanelBorder.Lightened(0.3f), false, 1f);
            }
            else
            {
                DrawRect(btnRect, HudStyles.PanelBgTop);
                DrawRect(btnRect, HudStyles.PanelBorder, false, 1f);
            }

            // Icon
            var iconColor = isConditional ? HudStyles.TextDim
                : hovered ? HudStyles.TextPrimary
                : HudStyles.TextSecondary;
            var iconSize = font.GetStringSize(cmd.Icon, HorizontalAlignment.Left, -1, 16);
            DrawString(font, new Vector2(x + (ButtonSize - iconSize.X) / 2, y + 24), cmd.Icon,
                HorizontalAlignment.Left, -1, 16, iconColor);

            // Hotkey
            var hotkeyColor = isConditional ? HudStyles.TextDim with { A = 0.5f }
                : hovered ? HudStyles.TextSecondary
                : HudStyles.TextDim;
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

        // Return Y position after last row
        return col > 0 ? y + ButtonSize + ButtonGap : y;
    }

    private void DrawBlueprintGrid(Font font, float startY)
    {
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var bp in AllBlueprints)
        {
            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);
            bool hovered = _hoveredBlueprintType == bp.Type;

            // Button background
            if (hovered)
            {
                DrawRect(btnRect, HudStyles.PanelBgTop.Lightened(0.15f));
                DrawRect(btnRect, HudStyles.PanelBorder.Lightened(0.3f), false, 1f);
            }
            else
            {
                DrawRect(btnRect, HudStyles.PanelBgTop);
                DrawRect(btnRect, HudStyles.PanelBorder, false, 1f);
            }

            // Icon: sprite if available, otherwise emoji fallback
            var sprite = GetBlueprintSprite(bp.Type, _config, _controllingPlayer);
            if (sprite != null)
            {
                float inset = 4f;
                var spriteRect = new Rect2(x + inset, y + inset, ButtonSize - inset * 2, ButtonSize - inset * 2);
                var tint = hovered ? Colors.White : new Color(1f, 1f, 1f, 0.85f);
                DrawTextureRect(sprite, spriteRect, false, tint);
            }
            else
            {
                var iconColor = hovered ? HudStyles.TextPrimary : HudStyles.TextSecondary;
                var iconSize = font.GetStringSize(bp.Icon, HorizontalAlignment.Left, -1, 16);
                DrawString(font, new Vector2(x + (ButtonSize - iconSize.X) / 2, y + 24), bp.Icon,
                    HorizontalAlignment.Left, -1, 16, iconColor);
            }

            // Hotkey
            DrawString(font, new Vector2(btnRect.End.X - 10, btnRect.End.Y - 4), bp.Hotkey,
                HorizontalAlignment.Right, -1, HudStyles.FontSizeHotkey, HudStyles.TextDim);

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
        if (@event is InputEventMouseMotion mm)
        {
            UpdateHover(mm.Position);
        }
        else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
        {
            // Check command buttons
            CommandAction? cmdKey = GetCommandAt(mb.Position);
            if (cmdKey != null)
            {
                EmitSignal(SignalName.CommandClicked, Variant.From(cmdKey.Value));
                AcceptEvent();
                return;
            }

            // Check blueprint buttons
            BlueprintMode.BlueprintType? bpType = GetBlueprintAt(mb.Position);
            if (bpType != null)
            {
                EmitSignal(SignalName.BlueprintClicked, Variant.From(bpType.Value));
                AcceptEvent();
            }
        }
    }

    private void UpdateHover(Vector2 pos)
    {
        CommandAction? cmd = GetCommandAt(pos);
        BlueprintMode.BlueprintType? bp = cmd != null ? null : GetBlueprintAt(pos);

        _hoveredCommandKey = cmd;
        _hoveredBlueprintType = bp;

        bool overClickable = cmd != null || bp != null;
        MouseDefaultCursorShape = overClickable
            ? Control.CursorShape.PointingHand
            : Control.CursorShape.Arrow;
    }

    private CommandAction? GetCommandAt(Vector2 pos)
    {
        if (_selectedBlocks == null || _selectedBlocks.Count == 0)
            return null;

        var available = GetAvailableCommands();
        float startY = 28f; // After label
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

    private BlueprintMode.BlueprintType? GetBlueprintAt(Vector2 pos)
    {
        float startY = _blueprintSectionY + 20f; // After label
        float x = 10f;
        float y = startY;
        int col = 0;

        foreach (var bp in AllBlueprints)
        {
            var btnRect = new Rect2(x, y, ButtonSize, ButtonSize);
            if (btnRect.HasPoint(pos))
                return bp.Type;

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
