using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Additive-blend child of GridRenderer. Draws all glow/emission effects
/// (soldier arm glow, stunner aura, death bursts, rays, ZoC, ghost trails)
/// with CanvasItemMaterial.BlendMode = Add for luminous overlap.
/// </summary>
public partial class GlowLayer : Node2D
{
    private readonly List<GlowCommand> _commands = new();
    private ImageTexture? _glowTex;

    /// <summary>
    /// Simple draw command — avoids passing complex state across nodes.
    /// GridRenderer enqueues glow draws during its _Draw(); GlowLayer replays them with additive blend.
    /// </summary>
    public record struct GlowCommand
    {
        public enum Kind { Line, Circle, Texture }
        public Kind Type;
        public Vector2 From, To;
        public Color Color;
        public float Width;
        public float Radius;
        public Rect2 Rect;
        public bool RoundCaps;

        public static GlowCommand MakeLine(Vector2 from, Vector2 to, Color color, float width, bool roundCaps = false)
            => new() { Type = Kind.Line, From = from, To = to, Color = color, Width = width, RoundCaps = roundCaps };

        public static GlowCommand MakeCircle(Vector2 center, float radius, Color color)
            => new() { Type = Kind.Circle, From = center, Radius = radius, Color = color };

        public static GlowCommand MakeTexture(Rect2 rect, Color color)
            => new() { Type = Kind.Texture, Rect = rect, Color = color };
    }

    public override void _Ready()
    {
        Material = new CanvasItemMaterial
        {
            BlendMode = CanvasItemMaterial.BlendModeEnum.Add
        };
        _glowTex = SpriteFactory.GetRadialGlow();
    }

    /// <summary>
    /// Clear command buffer. Called by GridRenderer at the start of each _Draw().
    /// </summary>
    public void BeginFrame()
    {
        _commands.Clear();
    }

    /// <summary>
    /// Enqueue a glow draw command.
    /// </summary>
    public void Add(GlowCommand cmd)
    {
        _commands.Add(cmd);
    }

    /// <summary>
    /// Signal that GridRenderer is done enqueuing. Triggers redraw.
    /// </summary>
    public void EndFrame()
    {
        QueueRedraw();
    }

    public override void _Draw()
    {
        foreach (var cmd in _commands)
        {
            switch (cmd.Type)
            {
                case GlowCommand.Kind.Line:
                    DrawLine(cmd.From, cmd.To, cmd.Color, cmd.Width, true);
                    if (cmd.RoundCaps)
                    {
                        DrawCircle(cmd.From, cmd.Width * 0.5f, cmd.Color);
                        DrawCircle(cmd.To, cmd.Width * 0.5f, cmd.Color);
                    }
                    break;

                case GlowCommand.Kind.Circle:
                    DrawCircle(cmd.From, cmd.Radius, cmd.Color);
                    break;

                case GlowCommand.Kind.Texture:
                    if (_glowTex != null)
                        DrawTextureRect(_glowTex, cmd.Rect, false, cmd.Color);
                    break;
            }
        }
    }
}
