using Godot;

namespace Blocker.Game.Config;

[GlobalClass]
public partial class CursorConfig : Resource
{
	[ExportGroup("Arrow")]
	[Export] public Texture2D? Arrow { get; set; }
	[Export] public Vector2 ArrowHotspot { get; set; } = Vector2.Zero;

	[ExportGroup("Pointing Hand")]
	[Export] public Texture2D? PointingHand { get; set; }
	[Export] public Vector2 PointingHandHotspot { get; set; } = new(5, 0);

	public void Apply()
	{
		if (Arrow != null)
			Godot.Input.SetCustomMouseCursor(Arrow, Godot.Input.CursorShape.Arrow, ArrowHotspot);
		if (PointingHand != null)
			Godot.Input.SetCustomMouseCursor(PointingHand, Godot.Input.CursorShape.PointingHand, PointingHandHotspot);
	}

	private static bool _defaultsApplied;

	public static void ApplyDefaults()
	{
		if (_defaultsApplied) return;
		_defaultsApplied = true;
		var hand = ResourceLoader.Exists("res://Assets/Sprites/hand_small_point.png")
			? GD.Load<Texture2D>("res://Assets/Sprites/hand_small_point.png") : null;
		if (hand != null)
			Godot.Input.SetCustomMouseCursor(hand, Godot.Input.CursorShape.PointingHand, new Vector2(5, 0));
	}
}
