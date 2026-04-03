using Godot;

namespace Blocker.Game.Config;

[Tool]
[GlobalClass]
public partial class PlayerPalette : Resource
{
    [ExportGroup("Base")]
    [Export] public Color Base { get; set; } = new(0.25f, 0.55f, 1.0f);

    /// <summary>
    /// Toggle this in the inspector to regenerate ALL colors from the current Base color.
    /// Overwrites any manual tweaks.
    /// </summary>
    [Export] public bool RegenerateFromBase
    {
        get => false;
        set
        {
            if (value)
            {
                ForceComputeFromBase();
                NotifyPropertyListChanged();
            }
        }
    }

    [ExportGroup("Builder")]
    [Export] public Color BuilderFill { get; set; }
    [Export] public Color BuilderGradientLight { get; set; }
    [Export] public Color BuilderGradientDark { get; set; }

    [ExportGroup("Soldier")]
    [Export] public Color SoldierFill { get; set; }
    [Export] public Color SoldierArmsColor { get; set; }
    [Export] public Color SoldierArmsGlow { get; set; }
    [Export] public Color SoldierCenterDot { get; set; }

    [ExportGroup("Stunner")]
    [Export] public Color StunnerFill { get; set; }
    [Export] public Color StunnerBevelLight { get; set; }
    [Export] public Color StunnerBevelShadow { get; set; }
    [Export] public Color StunnerDiamondOuter { get; set; }
    [Export] public Color StunnerDiamondInner { get; set; }
    [Export] public Color StunnerGlow { get; set; }

    [ExportGroup("Warden")]
    [Export] public Color WardenFill { get; set; }
    [Export] public Color WardenRing { get; set; }
    [Export] public Color WardenInnerHighlight { get; set; }
    [Export] public Color WardenGlow { get; set; }
    [Export] public Color WardenZocColor { get; set; }

    [ExportGroup("Jumper")]
    [Export] public Color JumperCore { get; set; }
    [Export] public Color JumperBright { get; set; }
    [Export] public Color JumperDark { get; set; }
    [Export] public Color JumperPulseGlow { get; set; }

    [ExportGroup("Wall")]
    [Export] public Color WallFill { get; set; }
    [Export] public Color WallHighlight { get; set; }
    [Export] public Color WallShadow { get; set; }
    [Export] public Color WallInner { get; set; }

    [ExportGroup("Builder Nest")]
    [Export] public Color BuilderNestOutline { get; set; }
    [Export] public Color BuilderNestOutlineGlow { get; set; }
    [Export] public Color BuilderNestDiamond { get; set; }
    [Export] public Color BuilderNestSpawnBar { get; set; }

    [ExportGroup("Soldier Nest")]
    [Export] public Color SoldierNestOutline { get; set; }
    [Export] public Color SoldierNestOutlineGlow { get; set; }
    [Export] public Color SoldierNestDiamond { get; set; }
    [Export] public Color SoldierNestSpawnBar { get; set; }

    [ExportGroup("Stunner Nest")]
    [Export] public Color StunnerNestOutline { get; set; }
    [Export] public Color StunnerNestOutlineGlow { get; set; }
    [Export] public Color StunnerNestDiamond { get; set; }
    [Export] public Color StunnerNestSpawnBar { get; set; }

    [ExportGroup("Supply Formation")]
    [Export] public Color SupplyOutline { get; set; }
    [Export] public Color SupplyOutlineGlow { get; set; }
    [Export] public Color SupplyDiamond { get; set; }

    [ExportGroup("Stun Tower")]
    [Export] public Color StunTowerOutline { get; set; }
    [Export] public Color StunTowerOutlineGlow { get; set; }
    [Export] public Color StunTowerDiamond { get; set; }

    [ExportGroup("Soldier Tower")]
    [Export] public Color SoldierTowerOutline { get; set; }
    [Export] public Color SoldierTowerOutlineGlow { get; set; }
    [Export] public Color SoldierTowerDiamond { get; set; }

    [ExportGroup("Effects")]
    [Export] public Color PushWaveColor { get; set; }
    [Export] public Color DeathColor { get; set; }
    [Export] public Color DeathFragmentColor { get; set; }
    [Export] public Color RootingBracketColor { get; set; }
    [Export] public Color CornerTickColor { get; set; }

    /// <summary>
    /// Compute all derived colors from Base. Call after setting Base.
    /// Only sets colors that are still at default (Color(0,0,0,0)).
    /// </summary>
    public void ComputeDefaults()
    {
        var b = Base;

        if (BuilderFill == default) BuilderFill = b;
        if (BuilderGradientLight == default) BuilderGradientLight = b.Lightened(0.28f);
        if (BuilderGradientDark == default) BuilderGradientDark = b.Darkened(0.18f);

        if (SoldierFill == default) SoldierFill = b.Darkened(0.3f);
        if (SoldierArmsColor == default) SoldierArmsColor = new Color(1f, 0.78f, 0.2f);
        if (SoldierArmsGlow == default) SoldierArmsGlow = new Color(1f, 0.78f, 0.2f, 0.25f);
        if (SoldierCenterDot == default) SoldierCenterDot = new Color(1f, 0.78f, 0.2f);

        if (StunnerFill == default) StunnerFill = b;
        if (StunnerBevelLight == default) StunnerBevelLight = b.Lightened(0.45f);
        if (StunnerBevelShadow == default) StunnerBevelShadow = b.Darkened(0.4f);
        if (StunnerDiamondOuter == default) StunnerDiamondOuter = b.Lightened(0.3f);
        if (StunnerDiamondInner == default) StunnerDiamondInner = new Color(1f, 1f, 1f, 0.5f);
        if (StunnerGlow == default) StunnerGlow = b;

        if (WardenFill == default) WardenFill = b with { A = 0.5f };
        if (WardenRing == default) WardenRing = b.Lightened(0.4f);
        if (WardenInnerHighlight == default) WardenInnerHighlight = b.Lightened(0.5f);
        if (WardenGlow == default) WardenGlow = b;
        if (WardenZocColor == default) WardenZocColor = b;

        if (JumperCore == default) JumperCore = b;
        if (JumperBright == default) JumperBright = b.Lightened(0.5f);
        if (JumperDark == default) JumperDark = b.Darkened(0.3f);
        if (JumperPulseGlow == default) JumperPulseGlow = b;

        if (WallFill == default) WallFill = b.Darkened(0.5f);
        if (WallHighlight == default) WallHighlight = b.Darkened(0.2f);
        if (WallShadow == default) WallShadow = b.Darkened(0.7f);
        if (WallInner == default) WallInner = b.Darkened(0.35f);

        if (BuilderNestOutline == default) BuilderNestOutline = b.Lightened(0.2f);
        if (BuilderNestOutlineGlow == default) BuilderNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (BuilderNestDiamond == default) BuilderNestDiamond = new Color(0.5f, 0.95f, 1f);
        if (BuilderNestSpawnBar == default) BuilderNestSpawnBar = new Color(0.4f, 0.7f, 1f, 0.35f);

        if (SoldierNestOutline == default) SoldierNestOutline = b.Lightened(0.2f);
        if (SoldierNestOutlineGlow == default) SoldierNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (SoldierNestDiamond == default) SoldierNestDiamond = new Color(1f, 0.88f, 0.3f);
        if (SoldierNestSpawnBar == default) SoldierNestSpawnBar = new Color(1f, 0.6f, 0.2f, 0.35f);

        if (StunnerNestOutline == default) StunnerNestOutline = b.Lightened(0.2f);
        if (StunnerNestOutlineGlow == default) StunnerNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        if (StunnerNestDiamond == default) StunnerNestDiamond = new Color(0.9f, 0.55f, 1f);
        if (StunnerNestSpawnBar == default) StunnerNestSpawnBar = new Color(0.8f, 0.3f, 1f, 0.35f);

        if (SupplyOutline == default) SupplyOutline = b.Lerp(new Color(0.55f, 0.55f, 0.55f), 0.5f);
        if (SupplyOutlineGlow == default) SupplyOutlineGlow = SupplyOutline with { A = 0.35f };
        if (SupplyDiamond == default) SupplyDiamond = Colors.White;

        if (StunTowerOutline == default) StunTowerOutline = b.Lightened(0.25f);
        if (StunTowerOutlineGlow == default) StunTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        if (StunTowerDiamond == default) StunTowerDiamond = b.Lightened(0.6f);

        if (SoldierTowerOutline == default) SoldierTowerOutline = b.Lightened(0.25f);
        if (SoldierTowerOutlineGlow == default) SoldierTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        if (SoldierTowerDiamond == default) SoldierTowerDiamond = b.Lightened(0.6f);

        if (PushWaveColor == default) PushWaveColor = new Color(
            b.R * 0.3f + 0.1f, b.G * 0.3f + 0.6f, b.B * 0.3f + 0.6f);
        if (DeathColor == default) DeathColor = b;
        if (DeathFragmentColor == default) DeathFragmentColor = b;
        if (RootingBracketColor == default) RootingBracketColor = new Color(0.5f, 0.5f, 0.5f);
        if (CornerTickColor == default) CornerTickColor = b.Lightened(0.2f);
    }

    /// <summary>
    /// Force-recompute ALL colors from Base, overwriting any manual tweaks.
    /// </summary>
    public void ForceComputeFromBase()
    {
        var b = Base;

        BuilderFill = b;
        BuilderGradientLight = b.Lightened(0.28f);
        BuilderGradientDark = b.Darkened(0.18f);

        SoldierFill = b.Darkened(0.3f);
        SoldierArmsColor = new Color(1f, 0.78f, 0.2f);
        SoldierArmsGlow = new Color(1f, 0.78f, 0.2f, 0.25f);
        SoldierCenterDot = new Color(1f, 0.78f, 0.2f);

        StunnerFill = b;
        StunnerBevelLight = b.Lightened(0.45f);
        StunnerBevelShadow = b.Darkened(0.4f);
        StunnerDiamondOuter = b.Lightened(0.3f);
        StunnerDiamondInner = new Color(1f, 1f, 1f, 0.5f);
        StunnerGlow = b;

        WardenFill = b with { A = 0.5f };
        WardenRing = b.Lightened(0.4f);
        WardenInnerHighlight = b.Lightened(0.5f);
        WardenGlow = b;
        WardenZocColor = b;

        JumperCore = b;
        JumperBright = b.Lightened(0.5f);
        JumperDark = b.Darkened(0.3f);
        JumperPulseGlow = b;

        WallFill = b.Darkened(0.5f);
        WallHighlight = b.Darkened(0.2f);
        WallShadow = b.Darkened(0.7f);
        WallInner = b.Darkened(0.35f);

        BuilderNestOutline = b.Lightened(0.2f);
        BuilderNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        BuilderNestDiamond = new Color(0.5f, 0.95f, 1f);
        BuilderNestSpawnBar = new Color(0.4f, 0.7f, 1f, 0.35f);

        SoldierNestOutline = b.Lightened(0.2f);
        SoldierNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        SoldierNestDiamond = new Color(1f, 0.88f, 0.3f);
        SoldierNestSpawnBar = new Color(1f, 0.6f, 0.2f, 0.35f);

        StunnerNestOutline = b.Lightened(0.2f);
        StunnerNestOutlineGlow = b.Lightened(0.2f) with { A = 0.35f };
        StunnerNestDiamond = new Color(0.9f, 0.55f, 1f);
        StunnerNestSpawnBar = new Color(0.8f, 0.3f, 1f, 0.35f);

        SupplyOutline = b.Lerp(new Color(0.55f, 0.55f, 0.55f), 0.5f);
        SupplyOutlineGlow = SupplyOutline with { A = 0.35f };
        SupplyDiamond = Colors.White;

        StunTowerOutline = b.Lightened(0.25f);
        StunTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        StunTowerDiamond = b.Lightened(0.6f);

        SoldierTowerOutline = b.Lightened(0.25f);
        SoldierTowerOutlineGlow = b.Lightened(0.25f) with { A = 0.35f };
        SoldierTowerDiamond = b.Lightened(0.6f);

        PushWaveColor = new Color(
            b.R * 0.3f + 0.1f, b.G * 0.3f + 0.6f, b.B * 0.3f + 0.6f);
        DeathColor = b;
        DeathFragmentColor = b;
        RootingBracketColor = new Color(0.7f, 0.7f, 0.7f);
        CornerTickColor = b.Lightened(0.2f);

        EmitChanged();
    }

    public static PlayerPalette FromBase(Color baseColor)
    {
        var p = new PlayerPalette { Base = baseColor };
        p.ForceComputeFromBase();
        return p;
    }
}
