using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Game.Maps;
using Blocker.Game.Rendering;
using Blocker.Game.Config;
using Godot;

namespace Blocker.Game.Editor;

/// <summary>
/// UI overlay for the map editor. Provides tool selection, slot picker, symmetry options,
/// and map management (new, save, load, back). Communicates with MapEditorScene via C# events.
/// </summary>
public partial class EditorToolbar : Control
{
    // --- Events ---
    public event Action<EditorTool>? ToolSelected;
    public event Action<GroundType>? GroundSelected;
    public event Action<TerrainType>? TerrainSelected;
    public event Action<BlockType, bool>? BlockSelected;
    public event Action<int>? SlotSelected;
    public event Action<SymmetryMode>? SymmetryChanged;
    public event Action<int, int, int>? NewMapRequested; // width, height, slots
    public event Action<int, int>? ResizeRequested; // width, height
    public event Action? TestMapRequested;
    public event Action? SaveRequested;
    public event Action<string>? LoadRequested;
    public event Action? BackRequested;
    public event Action<string>? MapNameChanged;
    public event Action<int>? SlotCountChanged;
    public event Action<bool>? GuidesToggled;
    public event Action<EditorMode>? ToolModeSelected;

    // Top bar controls
    private LineEdit _mapNameEdit = null!;
    private SpinBox _slotCountSpin = null!;
    private List<Button> _toolModeButtons = new();

    // Sidebar state
    private Button? _activeToolButton;
    private readonly Color _activeColor = new(0.3f, 0.5f, 1.0f);
    private readonly Color _normalColor = new(0.2f, 0.2f, 0.25f);

    // Symmetry checkboxes
    private CheckBox _symLR = null!;
    private CheckBox _symTB = null!;
    private CheckBox _symDiagTLBR = null!;
    private CheckBox _symDiagTRBL = null!;

    // Slot buttons
    private readonly List<Button> _slotButtons = [];
    private readonly List<UnitButton> _unitButtons = new();
    private int _activeSlot;
    private GameConfig? _config;

    public void SetConfig(GameConfig config)
    {
        _config = config;
        foreach (var btn in _unitButtons)
        {
            btn.Config = config;
            btn.QueueRedraw();
        }
    }

    public override void _Ready()
    {
        // Fill entire screen for layout, but don't block input on transparent areas
        AnchorsPreset = (int)LayoutPreset.FullRect;
        MouseFilter = MouseFilterEnum.Ignore;

        BuildTopBar();
        BuildSidebar();
    }

    public void SetMapName(string name)
    {
        _mapNameEdit.Text = name;
    }

    public void SetSlotCount(int count)
    {
        _slotCountSpin.Value = count;
    }

    // --- Top Bar ---

    private void BuildTopBar()
    {
        var topBar = new PanelContainer();
        topBar.AnchorsPreset = (int)LayoutPreset.TopWide;
        topBar.CustomMinimumSize = new Vector2(0, 40);
        topBar.MouseFilter = MouseFilterEnum.Stop;

        var stylebox = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 0.92f) };
        topBar.AddThemeStyleboxOverride("panel", stylebox);

        var hbox = new HBoxContainer();
        hbox.AddThemeConstantOverride("separation", 8);
        topBar.AddChild(hbox);

        // Back button
        var backBtn = MakeButton("Back");
        backBtn.Pressed += () => BackRequested?.Invoke();
        hbox.AddChild(backBtn);

        AddSeparator(hbox);

        // New button
        var newBtn = MakeButton("New");
        newBtn.Pressed += ShowNewMapDialog;
        hbox.AddChild(newBtn);

        // Resize button
        var resizeBtn = MakeButton("Resize");
        resizeBtn.Pressed += ShowResizeDialog;
        hbox.AddChild(resizeBtn);

        // Save button
        var saveBtn = MakeButton("Save");
        saveBtn.Pressed += () => SaveRequested?.Invoke();
        hbox.AddChild(saveBtn);

        // Load button
        var loadBtn = MakeButton("Load");
        loadBtn.Pressed += ShowLoadDialog;
        hbox.AddChild(loadBtn);

        // Test Map button
        var testBtn = MakeButton("Test Map");
        testBtn.Pressed += () => TestMapRequested?.Invoke();
        hbox.AddChild(testBtn);

        AddSeparator(hbox);

        var toolModes = new (string Label, string Key, EditorMode Mode)[]
        {
            ("Paint", "P", EditorMode.Paint),
            ("Fill",  "F", EditorMode.Fill),
            ("Pick",  "K", EditorMode.Pick),
            ("Select","S", EditorMode.Select),
            ("Line",  "L", EditorMode.Line),
            ("Erase", "E", EditorMode.Erase),
        };

        _toolModeButtons = new List<Button>();
        foreach (var (label, key, mode) in toolModes)
        {
            var btn = new Button
            {
                Text = $"{label} [{key}]",
                CustomMinimumSize = new Vector2(0, 30),
                ToggleMode = true
            };
            var m = mode; // capture
            btn.Pressed += () =>
            {
                HighlightToolMode(m);
                ToolModeSelected?.Invoke(m);
            };
            hbox.AddChild(btn);
            _toolModeButtons.Add(btn);
        }
        // Highlight Paint as default
        if (_toolModeButtons.Count > 0)
            HighlightToolMode(EditorMode.Paint);

        AddSeparator(hbox);

        // Map name
        var nameLabel = new Label { Text = "Name:" };
        hbox.AddChild(nameLabel);

        _mapNameEdit = new LineEdit
        {
            Text = "Untitled",
            CustomMinimumSize = new Vector2(160, 0)
        };
        _mapNameEdit.TextChanged += text => MapNameChanged?.Invoke(text);
        hbox.AddChild(_mapNameEdit);

        // Slot count
        var slotLabel = new Label { Text = "Slots:" };
        hbox.AddChild(slotLabel);

        _slotCountSpin = new SpinBox
        {
            MinValue = 2,
            MaxValue = 6,
            Value = 2,
            Step = 1,
            CustomMinimumSize = new Vector2(70, 0)
        };
        _slotCountSpin.ValueChanged += val => SlotCountChanged?.Invoke((int)val);
        hbox.AddChild(_slotCountSpin);

        AddChild(topBar);
    }

    // --- Sidebar ---

    private void BuildSidebar()
    {
        var sidePanel = new PanelContainer();
        sidePanel.SetAnchorsPreset(LayoutPreset.LeftWide);
        sidePanel.OffsetTop = 44;
        sidePanel.CustomMinimumSize = new Vector2(130, 0);
        sidePanel.MouseFilter = MouseFilterEnum.Stop;

        var stylebox = new StyleBoxFlat { BgColor = new Color(0.1f, 0.1f, 0.12f, 0.92f) };
        sidePanel.AddThemeStyleboxOverride("panel", stylebox);

        var scroll = new ScrollContainer
        {
            CustomMinimumSize = new Vector2(130, 0),
            SizeFlagsVertical = SizeFlags.ExpandFill,
            HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled
        };
        sidePanel.AddChild(scroll);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 2);
        vbox.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(vbox);

        // Ground tools
        AddSectionLabel(vbox, "Ground");
        AddToolButton(vbox, "Normal", () => { GroundSelected?.Invoke(GroundType.Normal); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });
        AddToolButton(vbox, "Boot", () => { GroundSelected?.Invoke(GroundType.Boot); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });
        AddToolButton(vbox, "Overload", () => { GroundSelected?.Invoke(GroundType.Overload); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });
        AddToolButton(vbox, "Proto", () => { GroundSelected?.Invoke(GroundType.Proto); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });

        // Terrain tools
        AddSectionLabel(vbox, "Terrain");
        AddToolButton(vbox, "Solid Wall", () => { TerrainSelected?.Invoke(TerrainType.Terrain); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });
        AddToolButton(vbox, "Breakable", () => { TerrainSelected?.Invoke(TerrainType.BreakableWall); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });
        AddToolButton(vbox, "Fragile", () => { TerrainSelected?.Invoke(TerrainType.FragileWall); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });

        // Unit tools
        AddSectionLabel(vbox, "Uprooted Units");
        var uprootedGrid = new GridContainer { Columns = 2 };
        uprootedGrid.AddThemeConstantOverride("h_separation", 2);
        uprootedGrid.AddThemeConstantOverride("v_separation", 2);
        vbox.AddChild(uprootedGrid);

        _unitButtons.Add(AddUnitToolButton(uprootedGrid, BlockType.Builder, "Builder", false));
        _unitButtons.Add(AddUnitToolButton(uprootedGrid, BlockType.Soldier, "Soldier", false));
        _unitButtons.Add(AddUnitToolButton(uprootedGrid, BlockType.Stunner, "Stunner", false));
        _unitButtons.Add(AddUnitToolButton(uprootedGrid, BlockType.Warden, "Warden", false));
        _unitButtons.Add(AddUnitToolButton(uprootedGrid, BlockType.Jumper, "Jumper", false));

        AddSectionLabel(vbox, "Rooted Units");
        var rootedGrid = new GridContainer { Columns = 2 };
        rootedGrid.AddThemeConstantOverride("h_separation", 2);
        rootedGrid.AddThemeConstantOverride("v_separation", 2);
        vbox.AddChild(rootedGrid);

        _unitButtons.Add(AddUnitToolButton(rootedGrid, BlockType.Builder, "Rooted Builder", true));
        _unitButtons.Add(AddUnitToolButton(rootedGrid, BlockType.Soldier, "Rooted Soldier", true));
        _unitButtons.Add(AddUnitToolButton(rootedGrid, BlockType.Stunner, "Rooted Stunner", true));
        _unitButtons.Add(AddUnitToolButton(rootedGrid, BlockType.Warden, "Rooted Warden", true));
        _unitButtons.Add(AddUnitToolButton(rootedGrid, BlockType.Wall, "Wall", true));

        // Eraser
        AddSectionLabel(vbox, "");
        AddToolButton(vbox, "Eraser", () => { ToolSelected?.Invoke(EditorTool.Eraser); ToolModeSelected?.Invoke(EditorMode.Paint); HighlightToolMode(EditorMode.Paint); });

        // Slot selector
        AddSectionLabel(vbox, "Slot");
        var slotGrid = new GridContainer { Columns = 3 };
        slotGrid.AddThemeConstantOverride("h_separation", 2);
        slotGrid.AddThemeConstantOverride("v_separation", 2);
        for (int i = 0; i < 6; i++)
        {
            int slot = i; // capture
            var btn = new Button
            {
                Text = (i + 1).ToString(),
                CustomMinimumSize = new Vector2(36, 30),
                ToggleMode = true,
                ButtonPressed = i == 0
            };
            btn.Pressed += () => SelectSlot(slot, btn);
            slotGrid.AddChild(btn);
            _slotButtons.Add(btn);
        }
        vbox.AddChild(slotGrid);

        // Symmetry
        AddSectionLabel(vbox, "Symmetry");
        _symLR = AddCheckbox(vbox, "Left/Right");
        _symTB = AddCheckbox(vbox, "Top/Bottom");
        _symDiagTLBR = AddCheckbox(vbox, "Diag TL-BR");
        _symDiagTRBL = AddCheckbox(vbox, "Diag TR-BL");

        _symLR.Toggled += _ => EmitSymmetry();
        _symTB.Toggled += _ => EmitSymmetry();
        _symDiagTLBR.Toggled += _ => EmitSymmetry();
        _symDiagTRBL.Toggled += _ => EmitSymmetry();

        // Guides
        AddSectionLabel(vbox, "Guides");
        var guidesCheck = AddCheckbox(vbox, "Center Lines");
        guidesCheck.Toggled += pressed => GuidesToggled?.Invoke(pressed);

        AddChild(sidePanel);
    }

    private void SelectSlot(int slot, Button btn)
    {
        _activeSlot = slot;
        for (int i = 0; i < _slotButtons.Count; i++)
            _slotButtons[i].ButtonPressed = i == slot;
        foreach (var unitBtn in _unitButtons)
        {
            unitBtn.Slot = slot;
            unitBtn.QueueRedraw();
        }
        SlotSelected?.Invoke(slot);
    }

    public void HighlightToolMode(EditorMode mode)
    {
        int idx = (int)mode;
        for (int i = 0; i < _toolModeButtons.Count; i++)
            _toolModeButtons[i].ButtonPressed = i == idx;
    }

    private void EmitSymmetry()
    {
        var mode = SymmetryMode.None;
        if (_symLR.ButtonPressed) mode |= SymmetryMode.LeftRight;
        if (_symTB.ButtonPressed) mode |= SymmetryMode.TopBottom;
        if (_symDiagTLBR.ButtonPressed) mode |= SymmetryMode.DiagonalTLBR;
        if (_symDiagTRBL.ButtonPressed) mode |= SymmetryMode.DiagonalTRBL;
        SymmetryChanged?.Invoke(mode);
    }

    // --- Dialogs ---

    private void ShowNewMapDialog()
    {
        var dialog = new AcceptDialog { Title = "New Map", Size = new Vector2I(300, 200) };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        var widthSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 41, Step = 1, Prefix = "Width:" };
        vbox.AddChild(widthSpin);

        var heightSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 25, Step = 1, Prefix = "Height:" };
        vbox.AddChild(heightSpin);

        var slotsSpin = new SpinBox { MinValue = 2, MaxValue = 6, Value = 2, Step = 1, Prefix = "Slots:" };
        vbox.AddChild(slotsSpin);

        dialog.AddChild(vbox);
        dialog.Confirmed += () =>
        {
            NewMapRequested?.Invoke((int)widthSpin.Value, (int)heightSpin.Value, (int)slotsSpin.Value);
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void ShowResizeDialog()
    {
        var dialog = new AcceptDialog { Title = "Resize Map", Size = new Vector2I(300, 150) };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 8);

        var widthSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 41, Step = 1, Prefix = "Width:" };
        vbox.AddChild(widthSpin);

        var heightSpin = new SpinBox { MinValue = 10, MaxValue = 500, Value = 25, Step = 1, Prefix = "Height:" };
        vbox.AddChild(heightSpin);

        dialog.AddChild(vbox);
        dialog.Confirmed += () =>
        {
            ResizeRequested?.Invoke((int)widthSpin.Value, (int)heightSpin.Value);
            dialog.QueueFree();
        };
        dialog.Canceled += () => dialog.QueueFree();

        AddChild(dialog);
        dialog.PopupCentered();
    }

    private void ShowLoadDialog()
    {
        var dialog = new AcceptDialog { Title = "Load Map", Size = new Vector2I(350, 300) };

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 4);

        MapFileManager.EnsureDirectoryExists();
        var maps = MapFileManager.ListMaps();

        if (maps.Count == 0)
        {
            vbox.AddChild(new Label { Text = "No maps found." });
        }
        else
        {
            var itemList = new ItemList
            {
                CustomMinimumSize = new Vector2(0, 200),
                SizeFlagsVertical = SizeFlags.ExpandFill
            };
            foreach (var map in maps)
                itemList.AddItem(map);

            vbox.AddChild(itemList);

            dialog.Confirmed += () =>
            {
                var selected = itemList.GetSelectedItems();
                if (selected.Length > 0)
                {
                    var fileName = maps[selected[0]];
                    LoadRequested?.Invoke(fileName);
                }
                dialog.QueueFree();
            };
        }

        dialog.Canceled += () => dialog.QueueFree();
        dialog.AddChild(vbox);
        AddChild(dialog);
        dialog.PopupCentered();
    }

    // --- UI helpers ---

    private static Button MakeButton(string text)
    {
        return new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(60, 30)
        };
    }

    private UnitButton AddUnitToolButton(Container parent, BlockType type, string text, bool isRooted)
    {
        var btn = new UnitButton
        {
            TooltipText = text,
            CustomMinimumSize = new Vector2(36, 36),
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            BlockType = type,
            IsRooted = isRooted,
            Slot = _activeSlot
        };
        btn.Pressed += () =>
        {
            SetActiveToolButton(btn);
            BlockSelected?.Invoke(type, isRooted);
            ToolModeSelected?.Invoke(EditorMode.Paint);
            HighlightToolMode(EditorMode.Paint);
        };
        parent.AddChild(btn);
        return btn;
    }

    private void AddToolButton(VBoxContainer parent, string text, Action onPressed)
    {
        var btn = new Button
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 28),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        btn.Pressed += () =>
        {
            SetActiveToolButton(btn);
            onPressed();
        };
        parent.AddChild(btn);
    }

    private void SetActiveToolButton(Button btn)
    {
        if (_activeToolButton != null)
        {
            var normalStyle = new StyleBoxFlat { BgColor = _normalColor };
            _activeToolButton.AddThemeStyleboxOverride("normal", normalStyle);
        }

        _activeToolButton = btn;
        var activeStyle = new StyleBoxFlat { BgColor = _activeColor };
        btn.AddThemeStyleboxOverride("normal", activeStyle);
    }

    private static void AddSectionLabel(VBoxContainer parent, string text)
    {
        var label = new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Left
        };
        label.AddThemeColorOverride("font_color", new Color(0.6f, 0.65f, 0.8f));
        label.AddThemeFontSizeOverride("font_size", 11);
        parent.AddChild(label);
    }

    private static CheckBox AddCheckbox(VBoxContainer parent, string text)
    {
        var cb = new CheckBox { Text = text };
        parent.AddChild(cb);
        return cb;
    }

    private static void AddSeparator(HBoxContainer parent)
    {
        var sep = new VSeparator();
        parent.AddChild(sep);
    }
}

public partial class UnitButton : Button
{
    public BlockType BlockType { get; set; }
    public int Slot { get; set; }
    public GameConfig? Config { get; set; }
    public bool IsRooted { get; set; }

    public override void _Draw()
    {
        if (Config == null) return;
        var size = Size;
        float iconSize = 24f;
        float paddingX = (size.X - iconSize) / 2f;
        float paddingY = (size.Y - iconSize) / 2f;
        var rect = new Rect2(paddingX, paddingY, iconSize, iconSize);
        BlockIconPainter.Draw(this, BlockType, Slot, rect, Config, enabled: true, alpha: 1f, isRooted: IsRooted);
    }
}
