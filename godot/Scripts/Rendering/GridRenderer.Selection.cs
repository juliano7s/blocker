using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Draws the grid: cell backgrounds by ground type, grid lines, and block placeholders.
/// Reads simulation GameState — never mutates it.
/// </summary>
public partial class GridRenderer : Node2D
{
	private void DrawDashedRect(Rect2 rect, Color color, float width, float dashLen, float gapLen)
	{
		var tl = rect.Position;
		var tr = tl + new Vector2(rect.Size.X, 0);
		var br = tl + rect.Size;
		var bl = tl + new Vector2(0, rect.Size.Y);
		DrawDashedLine(tl, tr, color, width, dashLen, gapLen);
		DrawDashedLine(tr, br, color, width, dashLen, gapLen);
		DrawDashedLine(br, bl, color, width, dashLen, gapLen);
		DrawDashedLine(bl, tl, color, width, dashLen, gapLen);
	}

	private void DrawDashedLine(Vector2 from, Vector2 to, Color color, float lineWidth, float dashLen, float gapLen)
	{
		var dir = to - from;
		var totalLen = dir.Length();
		if (totalLen < 0.01f) return;
		dir /= totalLen;

		float pos = 0;
		bool drawing = true;
		while (pos < totalLen)
		{
			var segLen = Mathf.Min(drawing ? dashLen : gapLen, totalLen - pos);
			if (drawing)
				DrawLine(from + dir * pos, from + dir * (pos + segLen), color, lineWidth, true);
			pos += segLen;
			drawing = !drawing;
		}
	}
}
