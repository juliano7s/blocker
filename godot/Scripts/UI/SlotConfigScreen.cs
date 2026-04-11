using Blocker.Game.Maps;
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class SlotConfigScreen : Control
{
	private MapData? _mapData;
	private readonly Dictionary<int, string> _slotAssignments = new();
	private VBoxContainer _slotContainer = null!;
	private int _playerSlot = 0;

	public override void _Ready()
	{
		if (MapSelection.SelectedMapFileName == null)
		{
			GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
			return;
		}

		_mapData = MapFileManager.Load(MapSelection.SelectedMapFileName);
		if (_mapData == null)
		{
			GD.PrintErr("Failed to load selected map");
			GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
			return;
		}

		var vbox = new VBoxContainer
		{
			AnchorLeft = 0.1f, AnchorRight = 0.9f,
			AnchorTop = 0.05f, AnchorBottom = 0.95f,
			GrowHorizontal = GrowDirection.Both,
			GrowVertical = GrowDirection.Both
		};
		vbox.AddThemeConstantOverride("separation", 12);
		AddChild(vbox);

		var header = new HBoxContainer();
		vbox.AddChild(header);

		var backBtn = new Button { Text = "< Back" };
		backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
		header.AddChild(backBtn);

		var title = new Label
		{
			Text = $"Configure: {_mapData.Name}",
			SizeFlagsHorizontal = SizeFlags.ExpandFill,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		title.AddThemeFontSizeOverride("font_size", 28);
		header.AddChild(title);
		header.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });

		var info = new Label { Text = $"Size: {_mapData.Width}x{_mapData.Height} — {_mapData.SlotCount} slots" };
		vbox.AddChild(info);

		vbox.AddChild(new HSeparator());

		_slotContainer = new VBoxContainer();
		_slotContainer.AddThemeConstantOverride("separation", 8);
		vbox.AddChild(_slotContainer);

		for (int i = 0; i < _mapData.SlotCount; i++)
			_slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
		RebuildSlotList();

		vbox.AddChild(new HSeparator());

		var startBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50) };
		startBtn.Pressed += OnStartPressed;
		vbox.AddChild(startBtn);
	}

	private void RebuildSlotList()
	{
		foreach (var child in _slotContainer.GetChildren())
			child.QueueFree();

		for (int i = 0; i < _mapData!.SlotCount; i++)
		{
			var row = new HBoxContainer();

			var label = new Label
			{
				Text = $"Slot {i + 1}:",
				CustomMinimumSize = new Vector2(80, 0)
			};
			row.AddChild(label);

			var btn = new Button
			{
				Text = _slotAssignments[i],
				CustomMinimumSize = new Vector2(200, 40),
				SizeFlagsHorizontal = SizeFlags.ExpandFill
			};
			int slot = i;
			btn.Pressed += () => ToggleSlotAssignment(slot);
			row.AddChild(btn);

			_slotContainer.AddChild(row);
		}
	}

	private void ToggleSlotAssignment(int slot)
	{
		if (_slotAssignments[slot] == "Player")
		{
			_slotAssignments[slot] = "AI (inactive)";
		}
		else
		{
			foreach (var key in _slotAssignments.Keys.ToList())
			{
				if (_slotAssignments[key] == "Player")
					_slotAssignments[key] = "AI (inactive)";
			}
			_slotAssignments[slot] = "Player";
			_playerSlot = slot;
		}
		RebuildSlotList();
	}

	private void OnStartPressed()
	{
		if (_mapData == null) return;

		var assignments = new List<SlotAssignment>();
		int nextPlayerId = 1; // Player gets 0, AI gets sequential 1, 2, 3...
		for (int i = 0; i < _mapData.SlotCount; i++)
		{
			if (_slotAssignments[i] == "Player")
				assignments.Add(new SlotAssignment(i, 0));
			else
				assignments.Add(new SlotAssignment(i, nextPlayerId++));
		}

		GameLaunchData.MapData = _mapData;
		GameLaunchData.Assignments = assignments;
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}
}

public static class GameLaunchData
{
	public static MapData? MapData { get; set; }
	public static List<SlotAssignment>? Assignments { get; set; }
	public static Blocker.Game.Net.MultiplayerSessionState? MultiplayerSession { get; set; }
}
