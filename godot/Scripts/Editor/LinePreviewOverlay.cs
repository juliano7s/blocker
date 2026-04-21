using Godot;
using System.Collections.Generic;

namespace Blocker.Game.Editor;

public partial class LinePreviewOverlay : Node2D
{
    public List<Vector2I> Points { get; set; } = [];
    public float CellSize { get; set; } = 24f;
    public float GridPadding { get; set; }

    private static readonly Color PreviewFill   = new(1f, 1f, 1f, 0.22f);
    private static readonly Color PreviewBorder = new(1f, 1f, 1f, 0.55f);

    public override void _Draw()
    {
        if (Points.Count == 0) return;

        foreach (var p in Points)
        {
            float px = p.X * CellSize + GridPadding;
            float py = p.Y * CellSize + GridPadding;
            DrawRect(new Rect2(px + 1, py + 1, CellSize - 2, CellSize - 2), PreviewFill);
        }

        if (Points.Count > 1)
        {
            var first = Points[0];
            var last  = Points[^1];
            var from = new Vector2(first.X * CellSize + GridPadding + CellSize * 0.5f,
                                   first.Y * CellSize + GridPadding + CellSize * 0.5f);
            var to   = new Vector2(last.X  * CellSize + GridPadding + CellSize * 0.5f,
                                   last.Y  * CellSize + GridPadding + CellSize * 0.5f);
            DrawDashedLine(from, to, PreviewBorder, 1.5f, 4f);
        }
    }
}
