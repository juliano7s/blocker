using Blocker.Game.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class MapSelectScreen : Control
{
    private ItemList _mapList = null!;

    public override void _Ready()
    {
        MapFileManager.EnsureDirectoryExists();

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
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        header.AddChild(backBtn);

        var title = new Label { Text = "Select Map", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        title.HorizontalAlignment = HorizontalAlignment.Center;
        title.AddThemeFontSizeOverride("font_size", 32);
        header.AddChild(title);

        header.AddChild(new Control { CustomMinimumSize = new Vector2(80, 0) });

        _mapList = new ItemList
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 300)
        };
        _mapList.ItemActivated += OnMapActivated;
        vbox.AddChild(_mapList);

        var startBtn = new Button { Text = "Select", CustomMinimumSize = new Vector2(0, 50) };
        startBtn.Pressed += OnSelectPressed;
        vbox.AddChild(startBtn);

        RefreshMapList();
    }

    private void RefreshMapList()
    {
        _mapList.Clear();
        var maps = MapFileManager.ListMaps();
        foreach (var map in maps)
            _mapList.AddItem(map);

        if (maps.Count == 0)
            _mapList.AddItem("(No maps found — create one in the editor)");
    }

    private void OnMapActivated(long index) => OnSelectPressed();

    private void OnSelectPressed()
    {
        var selected = _mapList.GetSelectedItems();
        if (selected.Length == 0) return;

        var fileName = _mapList.GetItemText(selected[0]);
        if (fileName.StartsWith("(")) return;

        MapSelection.SelectedMapFileName = fileName;
        GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
    }
}

public static class MapSelection
{
    public static string? SelectedMapFileName { get; set; }
}
