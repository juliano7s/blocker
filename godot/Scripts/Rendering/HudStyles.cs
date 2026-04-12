using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Shared visual constants for HUD components.
/// </summary>
public static class HudStyles
{
    // Panel backgrounds
    public static readonly Color PanelBgTop = new(0.078f, 0.098f, 0.133f);    // #141922
    public static readonly Color PanelBgBottom = new(0.047f, 0.063f, 0.082f); // #0c1015
    public static readonly Color PanelBorder = new(0.176f, 0.216f, 0.282f);   // #2d3748
    public static readonly Color InnerPanelBg = new(0.039f, 0.055f, 0.078f);  // #0a0e14

    // Text colors
    public static readonly Color TextPrimary = new(0.898f, 0.898f, 0.898f);   // #e5e5e5
    public static readonly Color TextSecondary = new(0.533f, 0.533f, 0.533f); // #888888
    public static readonly Color TextDim = new(0.333f, 0.333f, 0.333f);       // #555555

    // Dimensions
    public const float TopBarHeight = 42f;
    public const float BottomBarHeight = 220f;
    public const float PanelBorderWidth = 2f;
    public const float PanelGap = 6f;

    // Bottom bar panel ratios (20% / 60% / 20%)
    public const float SidePanelRatio = 0.20f;
    public const float CenterPanelRatio = 0.60f;

    // Font sizes
    public const int FontSizeNormal = 13;
    public const int FontSizeSmall = 10;
    public const int FontSizeHotkey = 9;

    /// <summary>Draw a panel background with gradient and border.</summary>
    public static void DrawPanelBackground(Control control, Rect2 rect)
    {
        // Gradient from top to bottom
        var topRect = new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.5f));
        var bottomRect = new Rect2(
            new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f),
            new Vector2(rect.Size.X, rect.Size.Y * 0.5f));

        control.DrawRect(topRect, PanelBgTop);
        control.DrawRect(bottomRect, PanelBgBottom);
        control.DrawRect(rect, PanelBorder, false, PanelBorderWidth);
    }

    /// <summary>Draw an inner panel (darker, for sub-sections).</summary>
    public static void DrawInnerPanel(Control control, Rect2 rect)
    {
        control.DrawRect(rect, InnerPanelBg);
        control.DrawRect(rect, PanelBorder, false, PanelBorderWidth);
    }
}
