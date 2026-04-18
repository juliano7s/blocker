using Blocker.Game.Config;
using Blocker.Game.Input;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Bottom-of-screen HUD with two floating groups:
/// Left = minimap panel, Right = selection info + command card.
/// The center area is transparent, showing the game grid.
/// </summary>
public partial class HudBar : CanvasLayer
{
    private MinimapPanel _minimap = null!;
    private SelectionPanel _selectionPanel = null!;
    private CommandCard _commandCard = null!;

    [Signal] public delegate void MinimapCameraJumpEventHandler(Vector2 worldPos);
    [Signal] public delegate void ControlGroupClickedEventHandler(int groupIndex, bool ctrlHeld);
    [Signal] public delegate void UnitClickedEventHandler(int blockId, bool shiftHeld);
    [Signal] public delegate void CommandClickedEventHandler(CommandAction commandKey);
    [Signal] public delegate void BlueprintClickedEventHandler(BlueprintMode.BlueprintType blueprintType);

    public override void _Ready()
    {
        Layer = 10;
        float margin = HudStyles.BottomPanelMargin;

        // === Left group: Minimap (bottom-left corner) ===
        var leftAnchor = new Control();
        leftAnchor.SetAnchorsPreset(Control.LayoutPreset.BottomLeft);
        float mapSize = HudStyles.MinimapSize;
        leftAnchor.OffsetLeft = margin;
        leftAnchor.OffsetTop = -(mapSize + margin);
        leftAnchor.OffsetRight = margin + mapSize;
        leftAnchor.OffsetBottom = -margin;
        leftAnchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(leftAnchor);

        _minimap = new MinimapPanel
        {
            MouseFilter = Control.MouseFilterEnum.Stop
        };
        _minimap.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _minimap.CameraJumpRequested += pos => EmitSignal(SignalName.MinimapCameraJump, pos);
        leftAnchor.AddChild(_minimap);

        // === Command card (bottom-right, independent height) ===
        var cmdAnchor = new Control();
        cmdAnchor.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        float cmdW = HudStyles.CommandCardWidth;
        float cmdH = HudStyles.CommandCardHeight;
        cmdAnchor.OffsetLeft = -margin - cmdW;
        cmdAnchor.OffsetTop = -(cmdH + margin);
        cmdAnchor.OffsetRight = -margin;
        cmdAnchor.OffsetBottom = -margin;
        cmdAnchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(cmdAnchor);

        _commandCard = new CommandCard();
        _commandCard.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _commandCard.CommandClicked += key => EmitSignal(SignalName.CommandClicked, Variant.From(key));
        _commandCard.BlueprintClicked += type => EmitSignal(SignalName.BlueprintClicked, Variant.From(type));
        cmdAnchor.AddChild(_commandCard);

        // === Info panel (bottom-right, left of command card, shorter) ===
        var infoAnchor = new Control();
        infoAnchor.SetAnchorsPreset(Control.LayoutPreset.BottomRight);
        float infoW = HudStyles.InfoPanelWidth;
        float infoH = HudStyles.InfoPanelHeight;
        infoAnchor.OffsetLeft = -margin - cmdW - HudStyles.PanelGap - infoW;
        infoAnchor.OffsetTop = -(infoH + margin);
        infoAnchor.OffsetRight = -margin - cmdW - HudStyles.PanelGap;
        infoAnchor.OffsetBottom = -margin;
        infoAnchor.MouseFilter = Control.MouseFilterEnum.Ignore;
        AddChild(infoAnchor);

        _selectionPanel = new SelectionPanel();
        _selectionPanel.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        _selectionPanel.ControlGroupClicked += (idx, ctrl) => EmitSignal(SignalName.ControlGroupClicked, idx, ctrl);
        _selectionPanel.UnitClicked += (id, shift) => EmitSignal(SignalName.UnitClicked, id, shift);
        infoAnchor.AddChild(_selectionPanel);
    }

    public void SetGameState(GameState state) => _minimap.SetGameState(state);
    public void SetConfig(GameConfig config)
    {
        _minimap.SetConfig(config);
        _selectionPanel.SetConfig(config);
        _commandCard.SetConfig(config);
    }

    public void SetControllingPlayer(int playerId) => _commandCard.SetControllingPlayer(playerId);

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
}
