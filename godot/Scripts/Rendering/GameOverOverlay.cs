using Blocker.Game.Config;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Post-game modal: shows the winning team, plus Rematch / Leave buttons.
/// GameManager spawns a single instance once a winner is detected; the overlay
/// itself is dumb (no game-state polling) — it just renders the data passed in
/// and routes button clicks to handlers wired by GameManager.
/// </summary>
public partial class GameOverOverlay : CanvasLayer
{
    private Action? _onRematch;
    private Action? _onLeave;
    private string _title = "Game over";
    private string _subtitle = "";
    private Color _accent = new(0.7f, 0.7f, 0.75f);
    private bool _showRematch;
    private string _rematchLabel = "Rematch";

    public override void _Ready()
    {
        Layer = 20; // above HudOverlay (Layer 10)

        // Dimmer
        var dim = new ColorRect
        {
            Color = new Color(0, 0, 0, 0.55f),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        dim.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        AddChild(dim);

        // Center panel
        var panel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(420, 220),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        panel.SetAnchorsPreset(Control.LayoutPreset.Center);
        // Center the panel around its midpoint
        panel.GrowHorizontal = Control.GrowDirection.Both;
        panel.GrowVertical = Control.GrowDirection.Both;
        panel.OffsetLeft = -210;
        panel.OffsetTop = -110;
        panel.OffsetRight = 210;
        panel.OffsetBottom = 110;

        var styleBox = new StyleBoxFlat
        {
            BgColor = new Color(0.07f, 0.07f, 0.10f, 0.96f),
            BorderColor = _accent,
            BorderWidthLeft = 2,
            BorderWidthRight = 2,
            BorderWidthTop = 2,
            BorderWidthBottom = 2,
            CornerRadiusBottomLeft = 6,
            CornerRadiusBottomRight = 6,
            CornerRadiusTopLeft = 6,
            CornerRadiusTopRight = 6,
        };
        panel.AddThemeStyleboxOverride("panel", styleBox);

        // PanelContainer expects a single content child; wrap the VBox in a
        // MarginContainer so the panel gets a sane interior padding.
        var margin = new MarginContainer();
        margin.AddThemeConstantOverride("margin_left", 24);
        margin.AddThemeConstantOverride("margin_right", 24);
        margin.AddThemeConstantOverride("margin_top", 24);
        margin.AddThemeConstantOverride("margin_bottom", 24);
        panel.AddChild(margin);

        var vbox = new VBoxContainer
        {
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
        };
        vbox.AddThemeConstantOverride("separation", 16);
        margin.AddChild(vbox);

        var titleLbl = new Label
        {
            Text = _title,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        titleLbl.AddThemeFontSizeOverride("font_size", 36);
        titleLbl.AddThemeColorOverride("font_color", _accent);
        vbox.AddChild(titleLbl);

        var subLbl = new Label
        {
            Text = _subtitle,
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        subLbl.AddThemeFontSizeOverride("font_size", 18);
        subLbl.AddThemeColorOverride("font_color", new Color(0.8f, 0.8f, 0.85f));
        vbox.AddChild(subLbl);

        vbox.AddChild(new Control { CustomMinimumSize = new Vector2(0, 12) });

        var btnRow = new HBoxContainer
        {
            Alignment = BoxContainer.AlignmentMode.Center,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
        };
        btnRow.AddThemeConstantOverride("separation", 16);
        vbox.AddChild(btnRow);

        if (_showRematch)
        {
            var rematchBtn = new Button
            {
                Text = _rematchLabel,
                CustomMinimumSize = new Vector2(140, 44),
                MouseFilter = Control.MouseFilterEnum.Stop,
            };
            rematchBtn.Pressed += () => _onRematch?.Invoke();
            btnRow.AddChild(rematchBtn);
        }

        var leaveBtn = new Button
        {
            Text = "Leave",
            CustomMinimumSize = new Vector2(140, 44),
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        leaveBtn.Pressed += () => _onLeave?.Invoke();
        btnRow.AddChild(leaveBtn);

        AddChild(panel);
    }

    /// <summary>
    /// Configure the overlay before adding to the scene tree. Must be called
    /// BEFORE _Ready (i.e. before AddChild) — _Ready bakes the values into the
    /// constructed nodes.
    /// </summary>
    public void Configure(
        string title,
        string subtitle,
        Color accent,
        bool showRematch,
        string rematchLabel,
        Action? onRematch,
        Action? onLeave)
    {
        _title = title;
        _subtitle = subtitle;
        _accent = accent;
        _showRematch = showRematch;
        _rematchLabel = rematchLabel;
        _onRematch = onRematch;
        _onLeave = onLeave;
    }

    /// <summary>
    /// Build a winner subtitle string from a winning team id, the local player,
    /// and the active player set. "Victory! / Defeat. / Team N wins" depending
    /// on whether the local player belongs to the winning team.
    /// </summary>
    public static (string title, string subtitle, Color accent) BuildWinnerLabels(
        int winningTeam,
        int localPlayerId,
        GameState state,
        GameConfig config)
    {
        var localPlayer = state.Players.Find(p => p.Id == localPlayerId);
        bool localWon = localPlayer != null && localPlayer.TeamId == winningTeam;

        // Pick a representative color: any block of any player on the winning team.
        // Falls back to the local player's palette if no winners are still on the field.
        int? winnerPid = state.Players.Find(p => p.TeamId == winningTeam)?.Id;
        var palette = config.GetPalette(winnerPid ?? localPlayerId);

        string title = localWon ? "Victory!" : "Defeat";
        string subtitle = $"Team {winningTeam + 1} wins";
        return (title, subtitle, palette.Base);
    }
}
