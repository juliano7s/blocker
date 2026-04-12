using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Bottom-of-screen HUD bar with three panels:
/// Left = minimap, Center = selection info, Right = command card.
/// </summary>
public partial class HudBar : CanvasLayer
{
    private MinimapPanel _minimap = null!;
    private SelectionPanel _selectionPanel = null!;
    private CommandCard _commandCard = null!;

    [Signal] public delegate void MinimapCameraJumpEventHandler(Vector2 worldPos);
    [Signal] public delegate void ControlGroupClickedEventHandler(int groupIndex, bool ctrlHeld);
    [Signal] public delegate void UnitClickedEventHandler(int blockId, bool shiftHeld);
    [Signal] public delegate void CommandClickedEventHandler(string commandKey);
    [Signal] public delegate void BlueprintClickedEventHandler(int blueprintType);

    public override void _Ready()
    {
        Layer = 10;

        // Anchor bar to bottom of screen
        var anchor = new Control();
        anchor.SetAnchorsPreset(Control.LayoutPreset.BottomWide);
        anchor.OffsetTop = -HudStyles.BottomBarHeight;
        anchor.OffsetBottom = 0;
        anchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(anchor);

        // Background panel
        var bg = new HudBarBackground();
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        anchor.AddChild(bg);

        // Margin container for padding
        var margin = new MarginContainer();
        margin.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        margin.AddThemeConstantOverride("margin_left", 8);
        margin.AddThemeConstantOverride("margin_right", 8);
        margin.AddThemeConstantOverride("margin_top", 8);
        margin.AddThemeConstantOverride("margin_bottom", 8);
        anchor.AddChild(margin);

        // HBox for three panels
        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", (int)HudStyles.PanelGap);
        hbox.MouseFilter = Control.MouseFilterEnum.Ignore;
        margin.AddChild(hbox);

        // Left: Minimap (20%)
        _minimap = new MinimapPanel
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _minimap.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _minimap.SizeFlagsStretchRatio = HudStyles.SidePanelRatio;
        _minimap.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _minimap.CameraJumpRequested += pos => EmitSignal(SignalName.MinimapCameraJump, pos);
        hbox.AddChild(_minimap);

        // Center: Selection panel (60%)
        _selectionPanel = new SelectionPanel();
        _selectionPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _selectionPanel.SizeFlagsStretchRatio = HudStyles.CenterPanelRatio;
        _selectionPanel.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _selectionPanel.ControlGroupClicked += (idx, ctrl) => EmitSignal(SignalName.ControlGroupClicked, idx, ctrl);
        _selectionPanel.UnitClicked += (id, shift) => EmitSignal(SignalName.UnitClicked, id, shift);
        hbox.AddChild(_selectionPanel);

        // Right: Command card (20%)
        _commandCard = new CommandCard();
        _commandCard.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _commandCard.SizeFlagsStretchRatio = HudStyles.SidePanelRatio;
        _commandCard.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
        _commandCard.CommandClicked += key => EmitSignal(SignalName.CommandClicked, key);
        _commandCard.BlueprintClicked += type => EmitSignal(SignalName.BlueprintClicked, type);
        hbox.AddChild(_commandCard);
    }

    public void SetGameState(GameState state) => _minimap.SetGameState(state);
    public void SetConfig(GameConfig config) => _minimap.SetConfig(config);

    public void SetCameraView(Vector2 worldPos, Vector2 viewSize)
    {
        _minimap.SetCameraView(worldPos, viewSize);
    }

    public void SetSelection(IReadOnlyList<Block>? blocks)
    {
        _selectionPanel.SetSelection(blocks);
        _commandCard.SetSelection(blocks);
    }

    public void SetControlGroups(IReadOnlyDictionary<int, IReadOnlyList<int>>? groups)
    {
        _selectionPanel.SetControlGroups(groups);
    }

    /// <summary>Inner control for drawing the bar background.</summary>
    private partial class HudBarBackground : Control
    {
        public override void _Draw()
        {
            var rect = new Rect2(Vector2.Zero, Size);
            // Draw gradient background
            var topRect = new Rect2(rect.Position, new Vector2(rect.Size.X, rect.Size.Y * 0.5f));
            var bottomRect = new Rect2(
                new Vector2(rect.Position.X, rect.Position.Y + rect.Size.Y * 0.5f),
                new Vector2(rect.Size.X, rect.Size.Y * 0.5f));

            DrawRect(topRect, HudStyles.PanelBgTop);
            DrawRect(bottomRect, HudStyles.PanelBgBottom);

            // Top border
            DrawLine(new Vector2(0, 0), new Vector2(Size.X, 0), HudStyles.PanelBorder, HudStyles.PanelBorderWidth);
        }
    }
}
