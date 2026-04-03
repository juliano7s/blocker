using Blocker.Game.Rendering;
using Godot;

namespace Blocker.Game.Input;

/// <summary>
/// Camera2D with edge scrolling, WASD/arrow pan, mouse wheel zoom.
/// Clamps to grid bounds.
/// </summary>
public partial class CameraController : Camera2D
{
	[Export] public float PanSpeed = 500f;
	[Export] public float EdgeScrollMargin = 20f;
	[Export] public float EdgeScrollSpeed = 400f;
	[Export] public float ZoomMin = 0.5f;
	[Export] public float ZoomMax = 3.0f;
	[Export] public float ZoomStep = 0.1f;

	private int _gridWidth;
	private int _gridHeight;

	public void SetGridSize(int width, int height)
	{
		_gridWidth = width;
		_gridHeight = height;

		// Center camera on grid
		var gridPixelW = _gridWidth * GridRenderer.CellSize;
		var gridPixelH = _gridHeight * GridRenderer.CellSize;
		Position = new Vector2(gridPixelW * 0.5f, gridPixelH * 0.5f);
		GD.Print($"Camera centered at {Position} for grid {width}x{height} ({gridPixelW}x{gridPixelH}px)");
	}

	public override void _Process(double delta)
	{
		var dt = (float)delta;
		var velocity = Vector2.Zero;

		// WASD / Arrow key panning
		if (Godot.Input.IsActionPressed("pan_up")) velocity.Y -= 1;
		if (Godot.Input.IsActionPressed("pan_down")) velocity.Y += 1;
		if (Godot.Input.IsActionPressed("pan_left")) velocity.X -= 1;
		if (Godot.Input.IsActionPressed("pan_right")) velocity.X += 1;

		// Edge scrolling
		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewportRect().Size;

		if (mousePos.X < EdgeScrollMargin) velocity.X -= 1;
		if (mousePos.X > viewportSize.X - EdgeScrollMargin) velocity.X += 1;
		if (mousePos.Y < EdgeScrollMargin) velocity.Y -= 1;
		if (mousePos.Y > viewportSize.Y - EdgeScrollMargin) velocity.Y += 1;

		if (velocity != Vector2.Zero)
		{
			var effectiveSpeed = PanSpeed;
			Position += velocity.Normalized() * effectiveSpeed * dt / Zoom.X;
			ClampPosition();
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventMouseButton mouseButton && mouseButton.Pressed)
		{
			float newZoom = Zoom.X;

			if (mouseButton.ButtonIndex == MouseButton.WheelUp)
				newZoom = Mathf.Min(Zoom.X + ZoomStep, ZoomMax);
			else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
				newZoom = Mathf.Max(Zoom.X - ZoomStep, ZoomMin);

			if (newZoom != Zoom.X)
			{
				Zoom = new Vector2(newZoom, newZoom);
				ClampPosition();
			}
		}
	}

	private void ClampPosition()
	{
		if (_gridWidth == 0 || _gridHeight == 0) return;

		var gridPixelW = _gridWidth * GridRenderer.CellSize;
		var gridPixelH = _gridHeight * GridRenderer.CellSize;
		var halfView = GetViewportRect().Size / (2f * Zoom);

		// If viewport is larger than grid, center on grid
		float minX, maxX, minY, maxY;

		if (halfView.X * 2 >= gridPixelW)
		{
			minX = maxX = gridPixelW * 0.5f;
		}
		else
		{
			minX = halfView.X;
			maxX = gridPixelW - halfView.X;
		}

		if (halfView.Y * 2 >= gridPixelH)
		{
			minY = maxY = gridPixelH * 0.5f;
		}
		else
		{
			minY = halfView.Y;
			maxY = gridPixelH - halfView.Y;
		}

		Position = new Vector2(
			Mathf.Clamp(Position.X, minX, maxX),
			Mathf.Clamp(Position.Y, minY, maxY)
		);
	}
}
