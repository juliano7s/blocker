using Blocker.Game.Config;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Bottom-of-screen HUD bar with three panels:
/// Left = minimap, Center = unit info (placeholder), Right = command card (placeholder).
/// Sits on a CanvasLayer above the game world.
/// </summary>
public partial class HudBar : CanvasLayer
{
    private MinimapPanel _minimap = null!;
    private PanelContainer _unitInfoPanel = null!;
    private PanelContainer _commandPanel = null!;

    private const float BarHeight = 150f;
    private const float MinimapWidth = 200f;

    [Signal] public delegate void MinimapCameraJumpEventHandler(Vector2 worldPos);

    public override void _Ready()
    {
        Layer = 10;

        // Anchor bar to bottom of screen
        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.OffsetTop = -BarHeight;
        anchor.OffsetBottom = 0;
        anchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(anchor);

        // Background panel
        var bg = new ColorRect
        {
            Color = new Color(0.05f, 0.05f, 0.08f, 0.85f),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        anchor.AddChild(bg);

        // HBox for three panels
        var hbox = new HBoxContainer();
        hbox.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        hbox.AddThemeConstantOverride("separation", 4);
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        anchor.AddChild(hbox);

        // Left: Minimap
        _minimap = new MinimapPanel
        {
            CustomMinimumSize = new Vector2(MinimapWidth, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _minimap.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        _minimap.CameraJumpRequested += pos => EmitSignal(SignalName.MinimapCameraJump, pos);
        hbox.AddChild(_minimap);

        // Center: Unit info placeholder
        _unitInfoPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(0, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _unitInfoPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        hbox.AddChild(_unitInfoPanel);

        // Right: Command card placeholder
        _commandPanel = new PanelContainer
        {
            CustomMinimumSize = new Vector2(MinimapWidth, BarHeight),
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        _commandPanel.SizeFlagsHorizontal = Control.SizeFlags.Fill;
        hbox.AddChild(_commandPanel);
    }

    public void SetGameState(GameState state) => _minimap.SetGameState(state);
    public void SetConfig(GameConfig config) => _minimap.SetConfig(config);

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _minimap.SetCameraView(worldPos, viewSize);
    }
}
