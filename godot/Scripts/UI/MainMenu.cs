using Godot;

namespace Blocker.Game.UI;

public partial class MainMenu : Control
{
	public override void _Ready()
	{
		var vbox = new VBoxContainer
		{
			AnchorLeft = 0.5f, AnchorRight = 0.5f,
			AnchorTop = 0.5f, AnchorBottom = 0.5f,
			OffsetLeft = -150, OffsetRight = 150,
			OffsetTop = -100, OffsetBottom = 100,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.Both
		};
		vbox.AddThemeConstantOverride("separation", 16);
		AddChild(vbox);

		var title = new Label
		{
			Text = "BLOCKER",
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 48);
		vbox.AddChild(title);

		vbox.AddChild(new HSeparator());

		var playTestBtn = new Button { Text = "Play Test", CustomMinimumSize = new Vector2(0, 50) };
		playTestBtn.Pressed += OnPlayTestPressed;
		vbox.AddChild(playTestBtn);

		var playVsAiBtn = new Button { Text = "Play vs AI", CustomMinimumSize = new Vector2(0, 50) };
		playVsAiBtn.Pressed += OnPlayVsAiPressed;
		vbox.AddChild(playVsAiBtn);

		var editorBtn = new Button { Text = "Map Editor", CustomMinimumSize = new Vector2(0, 50) };
		editorBtn.Pressed += OnMapEditorPressed;
		vbox.AddChild(editorBtn);

		var exitBtn = new Button { Text = "Exit Game", CustomMinimumSize = new Vector2(0, 50) };
		exitBtn.Pressed += OnExitPressed;
		vbox.AddChild(exitBtn);
	}

	private void OnPlayTestPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnPlayVsAiPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
	}

	private void OnMapEditorPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MapEditor.tscn");
	}

	private void OnExitPressed()
	{
		GetTree().Quit();
	}
}
