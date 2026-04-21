using Godot;

namespace Blocker.Game.Editor;

public enum SelectPhase { Idle, Drawing, Ready, Moving }

public partial class SelectionOverlay : Node2D
{
    public SelectPhase Phase { get; set; } = SelectPhase.Idle;
    public Rect2I Rect     { get; set; }
    public Vector2I Offset { get; set; }
    public float CellSize  { get; set; } = 24f;
    public float GridPadding { get; set; }

    private static readonly Color FillDrawing = new(0.29f, 0.62f, 1f, 0.18f);
    private static readonly Color FillReady   = new(0.29f, 0.62f, 1f, 0.08f);
    private static readonly Color Border      = new(0.29f, 0.62f, 1f, 1f);
    private static readonly Color SourceDim   = new(0f, 0f, 0f, 0.30f);

    public override void _Draw()
    {
        if (Phase == SelectPhase.Idle) return;

        float cs = CellSize, gp = GridPadding;

        if (Phase == SelectPhase.Drawing || Phase == SelectPhase.Ready)
        {
            var px = Rect.Position.X * cs + gp;
            var py = Rect.Position.Y * cs + gp;
            var pw = Rect.Size.X * cs;
            var ph = Rect.Size.Y * cs;
            var r = new Rect2(px, py, pw, ph);
            DrawRect(r, Phase == SelectPhase.Drawing ? FillDrawing : FillReady);
            DrawRect(r, Border, false, 1.5f);
            if (Phase == SelectPhase.Ready)
            {
                float hs = 5f;
                foreach (var corner in new[] {
                    r.Position,
                    r.Position + new Vector2(pw - hs, 0),
                    r.Position + new Vector2(0, ph - hs),
                    r.Position + new Vector2(pw - hs, ph - hs) })
                    DrawRect(new Rect2(corner, new Vector2(hs, hs)), Border);
            }
        }
        else if (Phase == SelectPhase.Moving)
        {
            var spx = Rect.Position.X * cs + gp;
            var spy = Rect.Position.Y * cs + gp;
            DrawRect(new Rect2(spx, spy, Rect.Size.X * cs, Rect.Size.Y * cs), SourceDim);

            var dx = (Rect.Position.X + Offset.X) * cs + gp;
            var dy = (Rect.Position.Y + Offset.Y) * cs + gp;
            var dw = Rect.Size.X * cs;
            var dh = Rect.Size.Y * cs;
            DrawRect(new Rect2(dx, dy, dw, dh), Border, false, 2f);
        }
    }
}
