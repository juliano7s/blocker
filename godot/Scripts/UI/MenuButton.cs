using Godot;
using System;

namespace Blocker.Game.UI;

public partial class MenuButton : Node2D
{
	private static readonly Color IdleColor = new(0.267f, 0.667f, 1f);
	private static readonly Color HoverColor = new(1f, 0.416f, 0.2f);
	private static readonly Color IdleTextColor = new(0.267f, 0.667f, 1f, 0.8f);
	private static readonly Color HoverTextColor = new(1f, 0.416f, 0.2f, 1f);

	private const int BlockCount = 3;
	private static readonly float[] BlockAlphas = { 0.7f, 0.5f, 0.3f };

	private string _label = "";
	private Action? _onActivated;
	private int _gridX, _gridY;
	private float _cellSize;
	private Func<float, float, Vector2>? _gridToPixel;

	private bool _hovered;
	private float _hoverT;

	private bool _clicked;
	private float _clickTimer;
	private const float ClickDelay = 400f;

	private Rect2 _hitRect;

	[Signal]
	public delegate void ClickedEventHandler();

	public void Initialize(string label, int gridX, int gridY, float cellSize,
		Func<float, float, Vector2> gridToPixel, Action onActivated)
	{
		_label = label;
		_gridX = gridX;
		_gridY = gridY;
		_cellSize = cellSize;
		_gridToPixel = gridToPixel;
		_onActivated = onActivated;

		var topLeft = gridToPixel(gridX, gridY);
		float labelWidth = _label.Length * 10f + 40f;
		_hitRect = new Rect2(topLeft.X - 4, topLeft.Y - 4,
			BlockCount * cellSize + labelWidth + 8, cellSize + 8);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta * 1000f;

		float target = _hovered ? 1f : 0f;
		_hoverT = Mathf.MoveToward(_hoverT, target, dt / 150f);

		if (_clicked)
		{
			_clickTimer -= dt;
			if (_clickTimer <= 0)
			{
				_clicked = false;
				_onActivated?.Invoke();
			}
		}

		QueueRedraw();
	}

	public override void _Input(InputEvent @event)
	{
		if (_gridToPixel == null || _clicked) return;

		if (@event is InputEventMouseMotion motion)
		{
			_hovered = _hitRect.HasPoint(motion.Position);
		}
		else if (@event is InputEventMouseButton mb && mb.Pressed && mb.ButtonIndex == MouseButton.Left)
		{
			if (_hovered && !_clicked)
			{
				_clicked = true;
				_clickTimer = ClickDelay;
				EmitSignal(SignalName.Clicked);
			}
		}
	}

	public Vector2 GetGridCenter()
	{
		if (_gridToPixel == null) return Vector2.Zero;
		return _gridToPixel(_gridX + 1, _gridY);
	}

	public (int X, int Y) GridPosition => (_gridX + 1, _gridY);

	public override void _Draw()
	{
		if (_gridToPixel == null) return;

		Color blockColor = IdleColor.Lerp(HoverColor, _hoverT);
		Color textColor = IdleTextColor.Lerp(HoverTextColor, _hoverT);

		for (int i = 0; i < BlockCount; i++)
		{
			var pos = _gridToPixel(_gridX + i, _gridY);
			float alpha = BlockAlphas[i] + _hoverT * 0.2f;
			var color = new Color(blockColor.R, blockColor.G, blockColor.B, alpha);
			DrawRect(new Rect2(pos.X + 1, pos.Y + 1, _cellSize - 2, _cellSize - 2), color);

			if (_hoverT > 0.01f)
			{
				float glowAlpha = _hoverT * 0.15f * BlockAlphas[i];
				var glowColor = new Color(blockColor.R, blockColor.G, blockColor.B, glowAlpha);
				DrawRect(new Rect2(pos.X - 2, pos.Y - 2, _cellSize + 4, _cellSize + 4), glowColor);
			}
		}

		var labelPos = _gridToPixel(_gridX + BlockCount, _gridY);
		var font = ThemeDB.FallbackFont;
		int fontSize = 14;
		DrawString(font, new Vector2(labelPos.X + 8, labelPos.Y + _cellSize * 0.65f),
			_label, HorizontalAlignment.Left, -1, fontSize, textColor);
	}
}
