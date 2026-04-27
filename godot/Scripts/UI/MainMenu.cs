using Blocker.Game.Maps;
using Blocker.Game.Rendering;
using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MainMenu : Control
{
	private static readonly Color EffectColor = new(1f, 0.667f, 0.2f);

	private MenuGrid _menuGrid = null!;
	private MenuTitle _menuTitle = null!;
	private MenuAmbience _menuAmbience = null!;
	private Node2D _effectLayer = null!;
	private readonly List<MenuButton> _buttons = new();
	private readonly List<GpuEffect> _clickEffects = new();

	public override void _Ready()
	{
		_menuGrid = new MenuGrid { Name = "MenuGrid" };
		AddChild(_menuGrid);

		_effectLayer = new Node2D { Name = "EffectLayer" };
		AddChild(_effectLayer);

		_menuTitle = new MenuTitle { Name = "MenuTitle" };
		AddChild(_menuTitle);

		CallDeferred(MethodName.InitializeComponents);
	}

	private void InitializeComponents()
	{
		float cellSize = MenuGrid.CellSize;
		int cols = _menuGrid.Cols;
		int rows = _menuGrid.Rows;

		_effectLayer.Position = new Vector2(
			_menuGrid.OffsetX - GridRenderer.GridPadding,
			_menuGrid.OffsetY - GridRenderer.GridPadding);

		EffectFactory.Initialize();

		int titleRow = 3;
		int titleGridWidth = MenuTitle.MeasureWidth();
		int titleOffsetX = (cols - titleGridWidth) / 2;
		_menuTitle.Initialize(titleOffsetX, titleRow, cellSize, _menuGrid.GridToPixel);

		int buttonStartRow = titleRow + _menuTitle.TotalHeight + 3;
		var buttonDefs = new (string Label, System.Action Action)[]
		{
			("PLAY TEST", OnPlayTestPressed),
			("PLAY VS AI", OnPlayVsAiPressed),
			("PLAY MULTIPLAYER", OnPlayMultiplayerPressed),
			("MAP EDITOR", OnMapEditorPressed),
			("EXIT GAME", OnExitPressed),
		};

		int buttonBlockWidth = 3;
		int buttonGridX = (cols - buttonBlockWidth) / 2 - 2;

		for (int i = 0; i < buttonDefs.Length; i++)
		{
			var (label, action) = buttonDefs[i];
			var btn = new MenuButton { Name = $"MenuButton_{label.Replace(" ", "")}" };
			AddChild(btn);
			btn.Initialize(label, buttonGridX, buttonStartRow + i * 2, cellSize,
				_menuGrid.GridToPixel, action);

			btn.Clicked += () => OnButtonClicked(btn);
			_buttons.Add(btn);
		}

		_menuAmbience = new MenuAmbience { Name = "MenuAmbience" };
		AddChild(_menuAmbience);
		_menuAmbience.Initialize(cols, rows, cellSize, _menuGrid.GridToPixel, _effectLayer);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta * 1000f;
		for (int i = _clickEffects.Count - 1; i >= 0; i--)
		{
			var effect = _clickEffects[i];
			effect.Age += dt;
			effect.Update();
			if (effect.Progress >= 1f)
			{
				effect.Destroy();
				_clickEffects.RemoveAt(i);
			}
		}
	}

	private void OnButtonClicked(MenuButton btn)
	{
		var (gx, gy) = btn.GridPosition;
		var effect = EffectFactory.LightningBurst(_effectLayer, new GridPos(gx, gy),
			EffectColor, maxSegs: 25, duration: 500f);
		_clickEffects.Add(effect);
	}

	private void OnPlayTestPressed()
	{
		var data = MapFileManager.Load("overload-test.json");
		if (data == null)
		{
			GD.PrintErr("Failed to load overload-test.json for Play Test");
			return;
		}

		var assignments = new List<SlotAssignment>();
		for (int i = 0; i < data.SlotCount; i++)
			assignments.Add(new SlotAssignment(i, i));

		GameLaunchData.MapData = data;
		GameLaunchData.Assignments = assignments;
		GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
	}

	private void OnPlayVsAiPressed() =>
		GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");

	private void OnPlayMultiplayerPressed() =>
		GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");

	private void OnMapEditorPressed() =>
		GetTree().ChangeSceneToFile("res://Scenes/MapEditor.tscn");

	private void OnExitPressed() =>
		GetTree().Quit();
}
