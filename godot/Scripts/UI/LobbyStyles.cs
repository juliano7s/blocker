using Godot;

namespace Blocker.Game.UI;

public static class LobbyStyles
{
    public static readonly Color Cyan = new(0.267f, 0.667f, 1f);
    public static readonly Color Orange = new(1f, 0.416f, 0.2f);
    public static readonly Color PanelBg = new(0.04f, 0.04f, 0.06f, 0.92f);
    public static readonly Color SectionBg = new(0.06f, 0.06f, 0.08f, 0.6f);
    public static readonly Color InputBg = new(0.06f, 0.06f, 0.08f);
    public static readonly Color BorderColor = new(0.267f, 0.667f, 1f, 0.2f);
    public static readonly Color TextPrimary = new(0.85f, 0.88f, 0.92f);
    public static readonly Color TextDim = new(0.45f, 0.48f, 0.55f);
    public static readonly Color ReadyGreen = new(0.3f, 0.85f, 0.3f);
    public static readonly Color BtnNormalBg = new(0.267f, 0.667f, 1f, 0.12f);
    public static readonly Color BtnHoverBg = new(0.267f, 0.667f, 1f, 0.25f);
    public static readonly Color BtnPressedBg = new(0.267f, 0.667f, 1f, 0.35f);
    public static readonly Color BtnAccentBg = new(0.267f, 0.667f, 1f, 0.6f);
    public static readonly Color BtnAccentHover = new(0.267f, 0.667f, 1f, 0.75f);
    public static readonly Color BtnDangerBg = new(0.9f, 0.2f, 0.2f, 0.4f);
    public static readonly Color BtnDangerHover = new(0.9f, 0.2f, 0.2f, 0.6f);
    public static readonly Color SeparatorColor = new(0.267f, 0.667f, 1f, 0.12f);
    public static readonly Color RowHoverBg = new(0.267f, 0.667f, 1f, 0.06f);

    public const int FontTitle = 20;
    public const int FontBody = 13;
    public const int FontSmall = 11;
    public const int FontLabel = 12;

    public static StyleBoxFlat MakePanel(float marginH = 14, float marginV = 10)
    {
        return new StyleBoxFlat
        {
            BgColor = PanelBg,
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = marginH, ContentMarginRight = marginH,
            ContentMarginTop = marginV, ContentMarginBottom = marginV
        };
    }

    public static StyleBoxFlat MakeSection()
    {
        return new StyleBoxFlat
        {
            BgColor = SectionBg,
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 6, ContentMarginBottom = 6
        };
    }

    public static void StyleButton(Button btn, bool accent = false, bool danger = false)
    {
        var bg = danger ? BtnDangerBg : accent ? BtnAccentBg : BtnNormalBg;
        var hover = danger ? BtnDangerHover : accent ? BtnAccentHover : BtnHoverBg;
        var pressed = accent ? BtnAccentHover : BtnPressedBg;

        btn.AddThemeStyleboxOverride("normal", MakeBtnStyle(bg));
        btn.AddThemeStyleboxOverride("hover", MakeBtnStyle(hover));
        btn.AddThemeStyleboxOverride("pressed", MakeBtnStyle(pressed));
        btn.AddThemeStyleboxOverride("disabled", MakeBtnStyle(new Color(0.1f, 0.1f, 0.12f, 0.5f)));
        btn.AddThemeStyleboxOverride("focus", MakeBtnStyle(hover));
        btn.AddThemeFontSizeOverride("font_size", FontBody);
        btn.AddThemeColorOverride("font_color", accent ? new Color(0.95f, 0.95f, 0.95f) : Cyan);
        btn.AddThemeColorOverride("font_hover_color", accent ? Colors.White : Orange);
        btn.AddThemeColorOverride("font_disabled_color", TextDim);
    }

    public static void StyleInput(LineEdit input)
    {
        var style = new StyleBoxFlat
        {
            BgColor = InputBg,
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 2, ContentMarginBottom = 2
        };
        var focusStyle = new StyleBoxFlat
        {
            BgColor = InputBg,
            BorderColor = new Color(Cyan.R, Cyan.G, Cyan.B, 0.5f),
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 6, ContentMarginRight = 6,
            ContentMarginTop = 2, ContentMarginBottom = 2
        };
        input.AddThemeStyleboxOverride("normal", style);
        input.AddThemeStyleboxOverride("focus", focusStyle);
        input.AddThemeFontSizeOverride("font_size", FontBody);
        input.AddThemeColorOverride("font_color", TextPrimary);
        input.AddThemeColorOverride("font_placeholder_color", TextDim);
        input.AddThemeColorOverride("caret_color", Cyan);
    }

    public static void StyleDropdown(OptionButton dropdown)
    {
        var style = MakeBtnStyle(BtnNormalBg);
        dropdown.AddThemeStyleboxOverride("normal", style);
        dropdown.AddThemeStyleboxOverride("hover", MakeBtnStyle(BtnHoverBg));
        dropdown.AddThemeStyleboxOverride("pressed", MakeBtnStyle(BtnPressedBg));
        dropdown.AddThemeStyleboxOverride("focus", MakeBtnStyle(BtnHoverBg));
        dropdown.AddThemeFontSizeOverride("font_size", FontBody);
        dropdown.AddThemeColorOverride("font_color", TextPrimary);
    }

    public static void StyleLabel(Label label, bool dim = false, int fontSize = FontBody)
    {
        label.AddThemeFontSizeOverride("font_size", fontSize);
        label.AddThemeColorOverride("font_color", dim ? TextDim : TextPrimary);
    }

    public static void StyleTitle(Label label)
    {
        label.AddThemeFontSizeOverride("font_size", FontTitle);
        label.AddThemeColorOverride("font_color", Cyan);
    }

    public static void StyleSeparator(HSeparator sep)
    {
        var style = new StyleBoxFlat
        {
            BgColor = SeparatorColor,
            ContentMarginTop = 0, ContentMarginBottom = 0
        };
        style.SetContentMarginAll(0);
        sep.AddThemeStyleboxOverride("separator", style);
        sep.AddThemeConstantOverride("separation", 6);
    }

    public static void StyleScrollContainer(ScrollContainer scroll)
    {
        var bg = new StyleBoxFlat
        {
            BgColor = new Color(0.03f, 0.03f, 0.04f, 0.5f),
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 4, ContentMarginRight = 4,
            ContentMarginTop = 4, ContentMarginBottom = 4
        };
        scroll.AddThemeStyleboxOverride("panel", bg);
    }

    private static StyleBoxFlat MakeBtnStyle(Color bg)
    {
        return new StyleBoxFlat
        {
            BgColor = bg,
            BorderColor = BorderColor,
            BorderWidthLeft = 1, BorderWidthRight = 1,
            BorderWidthTop = 1, BorderWidthBottom = 1,
            ContentMarginLeft = 8, ContentMarginRight = 8,
            ContentMarginTop = 3, ContentMarginBottom = 3
        };
    }
}
