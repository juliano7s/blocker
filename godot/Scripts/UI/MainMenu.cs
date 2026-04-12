using Blocker.Game.Maps;
using Blocker.Simulation.Maps;
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

		var playMpBtn = new Button { Text = "Play Multiplayer", CustomMinimumSize = new Vector2(0, 50) };
		playMpBtn.Pressed += OnPlayMultiplayerPressed;
		vbox.AddChild(playMpBtn);

		var editorBtn = new Button { Text = "Map Editor", CustomMinimumSize = new Vector2(0, 50) };
		editorBtn.Pressed += OnMapEditorPressed;
		vbox.AddChild(editorBtn);

		var exitBtn = new Button { Text = "Exit Game", CustomMinimumSize = new Vector2(0, 50) };
		exitBtn.Pressed += OnExitPressed;
		vbox.AddChild(exitBtn);
	}

	private void OnPlayTestPressed()
	{
		var data = MapFileManager.Load("overload-test.json");
		if (data == null)
		{
			GD.PrintErr("Failed to load overload-test.json for Play Test");
			return;
		}

		var assignments = new System.Collections.Generic.List<SlotAssignment>();
		for (int i = 0; i < data.SlotCount; i++)
			assignments.Add(new SlotAssignment(i, i));

		GameLaunchData.MapData = data;
		GameLaunchData.Assignments = assignments;
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnPlayVsAiPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
	}

	private void OnPlayMultiplayerPressed()
	{
		GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
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
