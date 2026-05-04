namespace Blocker.Game.Rendering;

public enum JumperStyle
{
    GlossyOrb,
    BronzeBall,
    BeveledSphere,
    FacetedGem,
}

public static class JumperStyleToggle
{
    public static JumperStyle Current { get; set; } = JumperStyle.GlossyOrb;
}
