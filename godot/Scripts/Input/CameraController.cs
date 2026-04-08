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

	// Discrete zoom levels — chosen so CellSize * zoom is always an integer,
	// which keeps grid lines pixel-aligned at every level.
	private static readonly float[] ZoomLevels =
		[0.5f, 0.75f, 1.0f, 1.25f, 1.5f, 1.75f, 2.0f, 2.5f, 3.0f];
	private int _zoomIndex = 2; // start at 1.0

	private int _gridWidth;
	private int _gridHeight;
	private bool _isMiddleDragging;

	// HUD insets in screen pixels — the camera treats the viewport as smaller
	// so grid content isn't hidden behind HUD elements
	private float _hudInsetTop;
	private float _hudInsetBottom;

	public void SetGridSize(int width, int height)
	{
		_gridWidth = width;
		_gridHeight = height;
		CenterOnGrid();
	}

	public void CenterOnGrid()
	{
		var gridPixelW = _gridWidth * GridRenderer.CellSize;
		var gridPixelH = _gridHeight * GridRenderer.CellSize;
		var pad = GridRenderer.GridPadding;

		// Offset Y to account for asymmetric HUD (top bar vs bottom bar)
		float insetTopWorld = _hudInsetTop / Zoom.Y;
		float insetBottomWorld = _hudInsetBottom / Zoom.Y;
		float centerOffsetY = (insetTopWorld - insetBottomWorld) * 0.5f;

		Position = new Vector2(gridPixelW * 0.5f + pad, gridPixelH * 0.5f + pad + centerOffsetY);
		GD.Print($"Camera centered at {Position} for grid {_gridWidth}x{_gridHeight}");
	}

	/// <summary>
	/// Tell the camera how much screen space is covered by HUD overlays.
	/// The camera offsets its center and clamp bounds so grid content
	/// remains visible between the HUD bars.
	/// </summary>
	public void SetHudInsets(float top, float bottom)
	{
		_hudInsetTop = top;
		_hudInsetBottom = bottom;
	}

	public void JumpTo(Vector2 worldPos)
	{
		Position = worldPos;
		ClampPosition();
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
		if (@event is InputEventMouseButton mouseButton)
		{
			if (mouseButton.Pressed)
			{
				int newIndex = _zoomIndex;

				if (mouseButton.ButtonIndex == MouseButton.WheelUp)
					newIndex = Mathf.Min(_zoomIndex + 1, ZoomLevels.Length - 1);
				else if (mouseButton.ButtonIndex == MouseButton.WheelDown)
					newIndex = Mathf.Max(_zoomIndex - 1, 0);

				if (newIndex != _zoomIndex)
				{
					_zoomIndex = newIndex;
					float z = ZoomLevels[_zoomIndex];
					Zoom = new Vector2(z, z);
					ClampPosition();
				}
			}

			// Middle-mouse drag panning
			if (mouseButton.ButtonIndex == MouseButton.Middle)
			{
				_isMiddleDragging = mouseButton.Pressed;
				GetViewport().SetInputAsHandled();
			}
		}

		if (@event is InputEventMouseMotion motion && _isMiddleDragging)
		{
			Position -= motion.Relative / Zoom.X;
			ClampPosition();
			GetViewport().SetInputAsHandled();
		}
	}

	private void ClampPosition()
	{
		if (_gridWidth == 0 || _gridHeight == 0) return;

		var gridPixelW = _gridWidth * GridRenderer.CellSize;
		var gridPixelH = _gridHeight * GridRenderer.CellSize;
		var viewportSize = GetViewportRect().Size;
	var pad = GridRenderer.GridPadding;

		// Convert HUD insets from screen pixels to world units
		float insetTopWorld = _hudInsetTop / Zoom.Y;
		float insetBottomWorld = _hudInsetBottom / Zoom.Y;

		// The effective visible area (between HUD bars) in world units
		float effectiveViewW = viewportSize.X / Zoom.X;
		float effectiveViewH = (viewportSize.Y - _hudInsetTop - _hudInsetBottom) / Zoom.Y;

		// The camera center is offset from the middle of the effective area
		// because the top bar and bottom bar have different heights.
		// Camera "center" in world is shifted down by (insetTop - insetBottom) / 2
		// relative to the true viewport center.
		float centerOffsetY = (insetTopWorld - insetBottomWorld) * 0.5f;

		// Allow panning ~quarter-viewport past each grid edge so corners
		// are reachable but you don't scroll into pure void.
		float marginX = effectiveViewW * 0.25f;
		float marginY = effectiveViewH * 0.25f;

		// Same clamping formula regardless of whether the grid fits the
		// viewport — the player can always scroll to reposition the map.
		float minX = effectiveViewW * 0.5f - marginX + pad;
		float maxX = gridPixelW - effectiveViewW * 0.5f + marginX + pad;
		float minY = effectiveViewH * 0.5f - marginY + centerOffsetY + pad;
		float maxY = gridPixelH - effectiveViewH * 0.5f + marginY + centerOffsetY + pad;

		Position = new Vector2(
			Mathf.Clamp(Position.X, minX, maxX),
			Mathf.Clamp(Position.Y, minY, maxY)
		);
	}
}
