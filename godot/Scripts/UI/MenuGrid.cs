using Godot;

namespace Blocker.Game.UI;

public partial class MenuGrid : Node2D
{
	public const float CellSize = 28f;
	public const float GridLineAlpha = 0.12f;
	private static readonly Color GridLineColor = new(0.267f, 0.667f, 1f, GridLineAlpha);

	private int _cols;
	private int _rows;
	private float _offsetX;
	private float _offsetY;

	public int Cols => _cols;
	public int Rows => _rows;
	public float OffsetX => _offsetX;
	public float OffsetY => _offsetY;

	public override void _Ready()
	{
		GetViewport().SizeChanged += RecalculateGrid;
		RecalculateGrid();
	}

	private void RecalculateGrid()
	{
		var viewportSize = GetViewportRect().Size;
		_cols = (int)(viewportSize.X / CellSize) + 1;
		_rows = (int)(viewportSize.Y / CellSize) + 1;
		_offsetX = (viewportSize.X - _cols * CellSize) / 2f;
		_offsetY = (viewportSize.Y - _rows * CellSize) / 2f;
		QueueRedraw();
	}

	public Vector2 GridToPixel(float gx, float gy) =>
		new(_offsetX + gx * CellSize, _offsetY + gy * CellSize);

	public (int gx, int gy) PixelToGrid(Vector2 pixel)
	{
		int gx = (int)((pixel.X - _offsetX) / CellSize);
		int gy = (int)((pixel.Y - _offsetY) / CellSize);
		return (gx, gy);
	}

	public override void _Draw()
	{
		var viewportSize = GetViewportRect().Size;
		DrawRect(new Rect2(0, 0, viewportSize.X, viewportSize.Y), new Color(0.04f, 0.04f, 0.04f));

		for (int c = 0; c <= _cols; c++)
		{
			float x = _offsetX + c * CellSize;
			DrawLine(new Vector2(x, 0), new Vector2(x, viewportSize.Y), GridLineColor, 1f);
		}

		for (int r = 0; r <= _rows; r++)
		{
			float y = _offsetY + r * CellSize;
			DrawLine(new Vector2(0, y), new Vector2(viewportSize.X, y), GridLineColor, 1f);
		}
	}
}
