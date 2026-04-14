using Godot;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Blocker.Game.Rendering;
using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Core;

namespace Blocker.Game.Showcase;

/// <summary>
/// Interactive EffectFactory playground. Every factory method is listed as a button.
/// Clicking spawns the effect at the center cell. A collapsible parameter panel lets
/// you tweak every exposed parameter before spawning. A "Custom" tab lets you set
/// shader-level overrides (trail, fade_speed, reverse, dashed, etc.) on top of the
/// factory defaults.
/// </summary>
public partial class EffectPlayground : Node2D
{
	private const int GridW = 30;
	private const int GridH = 30;
	private const float CellSize = GridRenderer.CellSize;
	// Must match EffectFactory coordinate system: pixel = grid * CellSize + GridOffset
	private const float GridOffset = GridRenderer.GridPadding;
	private const int CX = 15;
	private const int CY = 15;

	private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.1f);
	private static readonly Color GridLineColor = new(0.15f, 0.15f, 0.22f);
	private static readonly Color CenterCellColor = new(0.2f, 0.2f, 0.3f);

	private readonly List<GpuEffect> _effects = new();

	// Auto-loop state: respawns the selected effect every second.
	private LineEffect? _autoEffect;
	private float _loopTimer;
	private const float LoopInterval = 1000f; // ms

	// ─── Parameter state ─────────────────────────────────────────────
	// Each effect type has its own set of params stored here.
	// We use a simple dictionary approach keyed by "EffectName.ParamName".

	private readonly Dictionary<string, float> _paramValues = new();
	// Godot.Range covers both HSlider and SpinBox (override rows use SpinBox for precision).
	private readonly Dictionary<string, Godot.Range> _paramSliders = new();
	private readonly Dictionary<string, CheckBox> _paramCheckBoxes = new();

	// ─── UI refs ─────────────────────────────────────────────────────

	private VBoxContainer _effectList = null!;
	private VBoxContainer _paramPanel = null!;
	private Label _currentEffectLabel = null!;
	private ScrollContainer _paramScroll = null!;
	private PanelContainer _paramPanelContainer = null!;

	// Which effect is selected
	private string _selectedEffect = "";

	// Shader overrides (apply on top of factory params)
	private bool _overrideTrail;
	private float _trailOverride = 0.15f;
	private bool _overrideFadeSpeed;
	private float _fadeSpeedOverride = 0.7f;
	private bool _overrideReverse;
	private bool _overrideDashed;
	private bool _overrideFlicker;
	private bool _overrideContract;
	private bool _overrideDuration;
	private float _durationOverride = 1000f;

	// Color picker
	private ColorPickerButton _colorPicker = null!;
	private Color _effectColor = new(0.3f, 0.8f, 1f);

	// Direction selector
	private int _dirX = 1;
	private int _dirY = 0;
	private Label _dirLabel = null!;

	// ─── Effect Definitions ──────────────────────────────────────────

	private static readonly EffectDef[] EffectDefs =
	{
		new("LightningBurst", "Lightning burst outward from all edges",
			new ParamDef("maxSegs", 56, 4, 200, 1),
			new ParamDef("duration", 1200, 100, 5000, 50),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f),
			new ParamDef("contProb", 0.90f, 0f, 1f, 0.01f),
			new ParamDef("branchProb", 0.55f, 0f, 1f, 0.01f)),

		new("LightningConverge", "Lightning contracting inward (outer fades first)",
			new ParamDef("maxSegs", 28, 4, 200, 1),
			new ParamDef("duration", 900, 100, 5000, 50),
			new ParamDef("trail", 0.12f, 0.01f, 0.5f, 0.01f),
			new ParamDef("contProb", 0.90f, 0f, 1f, 0.01f),
			new ParamDef("branchProb", 0.55f, 0f, 1f, 0.01f)),

		new("LightningTrail", "Lightning from one edge direction (movement trail)",
			new ParamDef("dx", 1, -1, 1, 1),
			new ParamDef("dy", 0, -1, 1, 1),
			new ParamDef("duration", 1200, 100, 5000, 50),
			new ParamDef("maxSegs", 30, 4, 200, 1),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f),
			new ParamDef("contProb", 0.90f, 0f, 1f, 0.01f),
			new ParamDef("branchProb", 0.55f, 0f, 1f, 0.01f)),

		new("SpiralTrace", "Clockwise spiral outward from center",
			new ParamDef("duration", 1800, 100, 5000, 50),
			new ParamDef("maxSegs", 40, 4, 200, 1),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("SquareShockwave", "Concentric square rings expanding outward",
			new ParamDef("maxRadius", 10, 1, 30, 1),
			new ParamDef("duration", 1000, 100, 5000, 50),
			new ParamDef("trail", 0.12f, 0.01f, 0.5f, 0.01f)),

		new("CrossContract", "Short arms contracting inward from all edges",
			new ParamDef("duration", 600, 100, 5000, 50),
			new ParamDef("armLen", 3, 1, 20, 1),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("CellPerimeter", "Single cell outline traced clockwise (2 passes)",
			new ParamDef("duration", 600, 100, 5000, 50),
			new ParamDef("trail", 0.25f, 0.01f, 0.5f, 0.01f)),

		new("JitterArms", "6 jittery random-walk arms from one edge direction",
			new ParamDef("dx", -1, -1, 1, 1),
			new ParamDef("dy", 0, -1, 1, 1),
			new ParamDef("duration", 1000, 100, 5000, 50),
			new ParamDef("armCount", 6, 1, 20, 1),
			new ParamDef("armLen", 4, 1, 20, 1),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("LineTrail", "Straight line from one position to another",
			new ParamDef("toX", 18, 0, 29, 1),
			new ParamDef("toY", 15, 0, 29, 1),
			new ParamDef("duration", 500, 100, 5000, 50),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("StraightTracer", "Parallel straight lines in one direction",
			new ParamDef("dx", 1, -1, 1, 1),
			new ParamDef("dy", 0, -1, 1, 1),
			new ParamDef("duration", 800, 100, 5000, 50),
			new ParamDef("reach", 12, 1, 30, 1),
			new ParamDef("trail", 0.4f, 0.01f, 0.5f, 0.01f)),

		new("StraightTracerAllDirs", "Parallel straight lines in all 4 directions",
			new ParamDef("duration", 800, 100, 5000, 50),
			new ParamDef("reach", 12, 1, 30, 1),
			new ParamDef("trail", 0.4f, 0.01f, 0.5f, 0.01f)),

		new("DashedTendrils", "Dashed tendrils crawling outward with right-angle turns",
			new ParamDef("duration", 1600, 100, 5000, 50),
			new ParamDef("tendrilCount", 8, 1, 20, 1),
			new ParamDef("minLen", 5, 1, 20, 1),
			new ParamDef("maxLen", 10, 1, 30, 1),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("StaggeredArms", "4 staggered single-line arms from each edge",
			new ParamDef("duration", 800, 100, 5000, 50),
			new ParamDef("armLen", 3, 1, 20, 1),
			new ParamDef("stagger", 2, 0, 10, 1),
			new ParamDef("trail", 0.25f, 0.01f, 0.5f, 0.01f)),

		new("SelectSquares", "3 concentric squares — blink outward on selection",
			new ParamDef("duration", 350, 50, 2000, 50),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("ConvergingDrain", "Lightning wave sweeping inward (reverse direction)",
			new ParamDef("maxSegs", 50, 4, 200, 1),
			new ParamDef("duration", 1000, 100, 5000, 50),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f),
			new ParamDef("contProb", 0.85f, 0f, 1f, 0.01f),
			new ParamDef("branchProb", 0.50f, 0f, 1f, 0.01f)),

		new("ArcChain", "Bezier arc chain to random nearby targets",
			new ParamDef("arcCount", 8, 2, 20, 1),
			new ParamDef("subSegs", 4, 2, 12, 1),
			new ParamDef("duration", 1200, 100, 5000, 50),
			new ParamDef("trail", 0.1f, 0.01f, 0.5f, 0.01f)),

		new("CircuitTrace", "BFS right-angle random walk, each segment its own path",
			new ParamDef("maxSegs", 50, 4, 200, 1),
			new ParamDef("duration", 1400, 100, 5000, 50),
			new ParamDef("trail", 0.15f, 0.01f, 0.5f, 0.01f)),

		new("WavePulse", "4-directional lines with sine displacement and branches",
			new ParamDef("reach", 12, 2, 30, 1),
			new ParamDef("duration", 1500, 100, 5000, 50),
			new ParamDef("trail", 0.2f, 0.01f, 0.5f, 0.01f)),

		new("SineRipple", "Parallel lanes in all 4 directions",
			new ParamDef("laneCount", 3, 1, 7, 1),
			new ParamDef("reach", 10, 2, 30, 1),
			new ParamDef("duration", 2000, 100, 5000, 50),
			new ParamDef("trail", 0.2f, 0.01f, 0.5f, 0.01f)),

		new("ZocDashedPulse", "Looping dashed ZoC — cardinal radials + diagonal staircases",
			new ParamDef("zocR", 6, 2, 15, 1),
			new ParamDef("duration", 2200, 100, 5000, 50),
			new ParamDef("trail", 0.12f, 0.01f, 0.5f, 0.01f)),
	};

	private readonly record struct ParamDef(string Name, float Default, float Min, float Max, float Step);

	private record EffectDef(string Name, string Description, params ParamDef[] Params);

	// ─── Lifecycle ───────────────────────────────────────────────────

	public override void _Ready()
	{
		RenderingServer.SetDefaultClearColor(BackgroundColor);

		var vp = GetViewportRect().Size;
		// Subtract GridOffset so the drawn grid (at GridOffset in local space) centers in the viewport.
		Position = new Vector2(
			(vp.X - GridW * CellSize) / 2f - GridOffset,
			(vp.Y - GridH * CellSize) / 2f - GridOffset
		);

		EffectFactory.Initialize();

		BuildUI();

		// Select first effect by default
		SelectEffect(EffectDefs[0].Name);
	}

	public override void _Process(double delta)
	{
		float dtMs = (float)delta * 1000f;

		// Auto-loop: respawn selected effect every LoopInterval ms.
		_loopTimer += dtMs;
		if (_loopTimer >= LoopInterval)
		{
			_loopTimer = 0f;
			_autoEffect?.Destroy();
			_autoEffect = SpawnEffect(_selectedEffect);
		}
		_autoEffect?.Update();
		if (_autoEffect != null) _autoEffect.Age += dtMs;

		// Manually spawned effects.
		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var e = _effects[i];
			e.Age += dtMs;
			e.Update();

			if (e.Progress >= 1f)
			{
				e.Destroy();
				_effects.RemoveAt(i);
			}
		}
	}

	public override void _Draw()
	{
		// All drawing uses GridOffset so coordinates match EffectFactory's pixel space.
		DrawRect(new Rect2(GridOffset + CX * CellSize, GridOffset + CY * CellSize, CellSize, CellSize), CenterCellColor);

		// Grid lines
		for (int x = 0; x <= GridW; x++)
			DrawLine(new Vector2(GridOffset + x * CellSize, GridOffset),
				new Vector2(GridOffset + x * CellSize, GridOffset + GridH * CellSize), GridLineColor, 1f);
		for (int y = 0; y <= GridH; y++)
			DrawLine(new Vector2(GridOffset, GridOffset + y * CellSize),
				new Vector2(GridOffset + GridW * CellSize, GridOffset + y * CellSize), GridLineColor, 1f);
	}

	public override void _UnhandledInput(InputEvent ev)
	{
		if (ev is InputEventKey { Pressed: true, Keycode: Key.Space })
			SpawnSelected();
		if (ev is InputEventKey { Pressed: true, Keycode: Key.C })
			ClearAll();
	}

	// ─── UI Construction ─────────────────────────────────────────────

	private void BuildUI()
	{
		var canvas = new CanvasLayer { Name = "UI", Layer = 10 };
		AddChild(canvas);

		// ── Left panel: Effect list ──
		var leftPanel = new PanelContainer();
		leftPanel.SetAnchorsPreset(Control.LayoutPreset.LeftWide);
		leftPanel.OffsetLeft = 8;
		leftPanel.OffsetTop = 8;
		leftPanel.OffsetRight = 230;
		leftPanel.OffsetBottom = -8;
		canvas.AddChild(leftPanel);

		var leftVBox = new VBoxContainer();
		leftVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		leftVBox.AddThemeConstantOverride("separation", 2);
		leftPanel.AddChild(leftVBox);

		var titleLabel = new Label { Text = "EffectPlayground" };
		titleLabel.AddThemeFontSizeOverride("font_size", 16);
		leftVBox.AddChild(titleLabel);

		var hintLabel = new Label { Text = "Space = spawn  |  C = clear" };
		hintLabel.AddThemeFontSizeOverride("font_size", 10);
		leftVBox.AddChild(hintLabel);

		leftVBox.AddChild(new HSeparator());

		var leftScroll = new ScrollContainer();
		leftScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		leftScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		leftVBox.AddChild(leftScroll);

		_effectList = new VBoxContainer();
		_effectList.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_effectList.AddThemeConstantOverride("separation", 1);
		leftScroll.AddChild(_effectList);

		// Build effect buttons
		foreach (var def in EffectDefs)
		{
			var btn = new Button { Text = def.Name };
			btn.AddThemeFontSizeOverride("font_size", 13);
			btn.Pressed += () => SelectEffect(def.Name);
			_effectList.AddChild(btn);

			var desc = new Label { Text = $"  {def.Description}" };
			desc.AddThemeFontSizeOverride("font_size", 10);
			desc.AutowrapMode = TextServer.AutowrapMode.WordSmart;
			_effectList.AddChild(desc);
		}

		leftVBox.AddChild(new HSeparator());
		AddBtn(leftVBox, "Clear All (C)", ClearAll);
		AddBtn(leftVBox, "Spawn All Effects", SpawnAll);

		// ── Right panel: Parameters ──
		_paramPanelContainer = new PanelContainer();
		_paramPanelContainer.SetAnchorsPreset(Control.LayoutPreset.RightWide);
		_paramPanelContainer.OffsetLeft = -320;
		_paramPanelContainer.OffsetTop = 8;
		_paramPanelContainer.OffsetRight = -8;
		_paramPanelContainer.OffsetBottom = -8;
		canvas.AddChild(_paramPanelContainer);

		var rightVBox = new VBoxContainer();
		rightVBox.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		rightVBox.AddThemeConstantOverride("separation", 3);
		_paramPanelContainer.AddChild(rightVBox);

		_currentEffectLabel = new Label { Text = "Select an effect..." };
		_currentEffectLabel.AddThemeFontSizeOverride("font_size", 15);
		rightVBox.AddChild(_currentEffectLabel);

		rightVBox.AddChild(new HSeparator());

		// Scrollable parameter area
		_paramScroll = new ScrollContainer();
		_paramScroll.SizeFlagsVertical = Control.SizeFlags.ExpandFill;
		_paramScroll.HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled;
		rightVBox.AddChild(_paramScroll);

		_paramPanel = new VBoxContainer();
		_paramPanel.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		_paramPanel.AddThemeConstantOverride("separation", 4);
		_paramScroll.AddChild(_paramPanel);

		// Color picker
		rightVBox.AddChild(new HSeparator());
		var colorRow = new HBoxContainer();
		var colorLabel = new Label { Text = "Color: " };
		colorLabel.AddThemeFontSizeOverride("font_size", 12);
		colorLabel.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter;
		colorRow.AddChild(colorLabel);

		_colorPicker = new ColorPickerButton();
		_colorPicker.CustomMinimumSize = new Vector2(120, 30);
		_colorPicker.Color = _effectColor;
		_colorPicker.ColorChanged += c => _effectColor = c;
		colorRow.AddChild(_colorPicker);

		// Preset colors
		var presetColors = new Color[]
		{
			new(0.3f, 0.8f, 1f),     // blue
			new(1f, 0.55f, 0.15f),   // orange
			new(0.4f, 0.85f, 0.6f),  // green
			new(0.8f, 0.3f, 1f),     // purple
			new(1f, 1f, 1f),         // white
			new(1f, 0.3f, 0.3f),     // red
			new(1f, 0.95f, 0.4f),    // yellow
		};
		foreach (var c in presetColors)
		{
			var pb = new Button { Text = "" };
			pb.CustomMinimumSize = new Vector2(24, 24);
			pb.TooltipText = $"#{c.ToHtml(false)}";
			// StyleBoxFlat makes the color visible — icon_normal_color only affects icons.
			var normal = new StyleBoxFlat { BgColor = c };
			normal.SetCornerRadiusAll(3);
			var hover = new StyleBoxFlat { BgColor = c * 1.3f };
			hover.SetCornerRadiusAll(3);
			pb.AddThemeStyleboxOverride("normal", normal);
			pb.AddThemeStyleboxOverride("hover", hover);
			pb.AddThemeStyleboxOverride("pressed", normal);
			var captured = c;
			pb.Pressed += () =>
			{
				_effectColor = captured;
				_colorPicker.Color = captured;
			};
			colorRow.AddChild(pb);
		}
		rightVBox.AddChild(colorRow);

		// Spawn button
		rightVBox.AddChild(new HSeparator());
		AddBtn(rightVBox, "  ▶  Spawn Effect (Space)", SpawnSelected);
	}

	private void AddBtn(Container parent, string text, Action onPressed)
	{
		var btn = new Button { Text = text };
		btn.AddThemeFontSizeOverride("font_size", 12);
		btn.Pressed += onPressed;
		parent.AddChild(btn);
	}

	// ─── Parameter Panel Rebuild ─────────────────────────────────────

	private void SelectEffect(string name)
	{
		_selectedEffect = name;
		_currentEffectLabel.Text = name;
		RebuildParamPanel();
		// Immediately spawn so you see it right away; reset timer for a clean interval.
		_autoEffect?.Destroy();
		_autoEffect = SpawnEffect(name);
		_loopTimer = 0f;
	}

	private void RebuildParamPanel()
	{
		// Clear existing widgets
		foreach (var child in _paramPanel.GetChildren())
			child.QueueFree();
		_paramSliders.Clear();
		_paramCheckBoxes.Clear();

		var def = EffectDefs.FirstOrDefault(d => d.Name == _selectedEffect);
		if (def == null) return;

		// Effect-specific params
		var header = new Label { Text = "Effect Parameters" };
		header.AddThemeFontSizeOverride("font_size", 12);
		header.AddThemeColorOverride("font_color", new Color(0.6f, 0.8f, 1f));
		_paramPanel.AddChild(header);

		foreach (var p in def.Params)
		{
			string key = $"{def.Name}.{p.Name}";
			if (!_paramValues.ContainsKey(key))
				_paramValues[key] = p.Default;

			AddParamRow(p, key);
		}

		// ── Shader Overrides ──
		_paramPanel.AddChild(new HSeparator());
		var shaderHeader = new Label { Text = "Shader Overrides" };
		shaderHeader.AddThemeFontSizeOverride("font_size", 12);
		shaderHeader.AddThemeColorOverride("font_color", new Color(1f, 0.7f, 0.5f));
		_paramPanel.AddChild(shaderHeader);

		// Duration override
		AddOverrideRow("Duration", ref _overrideDuration, ref _durationOverride, 50, 10000, 50);

		// Trail override
		AddOverrideRow("Trail", ref _overrideTrail, ref _trailOverride, 0.01f, 0.5f, 0.01f);

		// Fade speed override
		AddOverrideRow("Fade Speed", ref _overrideFadeSpeed, ref _fadeSpeedOverride, 0.1f, 2.0f, 0.05f);

		// Boolean overrides
		AddBoolOverrideRow("Reverse", ref _overrideReverse);
		AddBoolOverrideRow("Dashed", ref _overrideDashed);
		AddBoolOverrideRow("Flicker", ref _overrideFlicker);
		AddBoolOverrideRow("Contract", ref _overrideContract);
	}

	private void AddParamRow(ParamDef p, string key)
	{
		// Name label
		var nameLabel = new Label { Text = $"{p.Name}:" };
		nameLabel.AddThemeFontSizeOverride("font_size", 11);
		_paramPanel.AddChild(nameLabel);

		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);

		var slider = new HSlider();
		slider.MinValue = p.Min;
		slider.MaxValue = p.Max;
		slider.Step = p.Step;
		slider.Value = _paramValues[key];
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.CustomMinimumSize = new Vector2(0, 20);

		// Value label shows current number; format as int when step >= 1.
		bool isInt = p.Step >= 1f;
		var valLabel = new Label { Text = FormatValue(slider.Value, isInt) };
		valLabel.AddThemeFontSizeOverride("font_size", 11);
		valLabel.CustomMinimumSize = new Vector2(42, 0);

		slider.ValueChanged += v =>
		{
			_paramValues[key] = (float)v;
			valLabel.Text = FormatValue(v, isInt);
		};
		row.AddChild(slider);
		row.AddChild(valLabel);

		// Reset button
		var resetBtn = new Button { Text = "R" };
		resetBtn.AddThemeFontSizeOverride("font_size", 10);
		resetBtn.TooltipText = $"Reset to default ({FormatValue(p.Default, isInt)})";
		resetBtn.CustomMinimumSize = new Vector2(24, 0);
		resetBtn.Pressed += () =>
		{
			_paramValues[key] = p.Default;
			slider.Value = p.Default;
		};
		row.AddChild(resetBtn);

		_paramSliders[key] = slider;
		_paramPanel.AddChild(row);
	}

	private void AddOverrideRow(string label, ref bool enabled, ref float value,
		float min, float max, float step)
	{
		var row = new HBoxContainer();
		row.AddThemeConstantOverride("separation", 4);

		var cb = new CheckBox { Text = $"{label}:" };
		cb.ButtonPressed = enabled;
		cb.AddThemeFontSizeOverride("font_size", 11);
		cb.CustomMinimumSize = new Vector2(100, 0);
		row.AddChild(cb);

		bool isInt = step >= 1f;
		var slider = new HSlider();
		slider.MinValue = min;
		slider.MaxValue = max;
		slider.Step = step;
		slider.Value = value;
		slider.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
		slider.CustomMinimumSize = new Vector2(0, 20);
		slider.Editable = enabled;

		var valLabel = new Label { Text = FormatValue(slider.Value, isInt) };
		valLabel.AddThemeFontSizeOverride("font_size", 11);
		valLabel.CustomMinimumSize = new Vector2(42, 0);

		slider.ValueChanged += v => valLabel.Text = FormatValue(v, isInt);
		cb.Toggled += on => slider.Editable = on;

		row.AddChild(slider);
		row.AddChild(valLabel);
		_paramPanel.AddChild(row);

		var key = $"override.{label}";
		_paramCheckBoxes[key] = cb;
		_paramSliders[key] = slider;
	}

	private void AddBoolOverrideRow(string label, ref bool enabled)
	{
		var cb = new CheckBox { Text = label };
		cb.ButtonPressed = enabled;
		cb.AddThemeFontSizeOverride("font_size", 11);
		_paramPanel.AddChild(cb);

		var key = $"override.{label}";
		_paramCheckBoxes[key] = cb;
	}

	// ─── Read Override State ─────────────────────────────────────────

	private void ReadOverrides()
	{
		if (_paramCheckBoxes.TryGetValue("override.Duration", out var durCb))
			_overrideDuration = durCb.ButtonPressed;
		if (_paramSliders.TryGetValue("override.Duration", out var durSl))
			_durationOverride = (float)durSl.Value;

		if (_paramCheckBoxes.TryGetValue("override.Trail", out var trailCb))
			_overrideTrail = trailCb.ButtonPressed;
		if (_paramSliders.TryGetValue("override.Trail", out var trailSl))
			_trailOverride = (float)trailSl.Value;

		if (_paramCheckBoxes.TryGetValue("override.Fade Speed", out var fsCb))
			_overrideFadeSpeed = fsCb.ButtonPressed;
		if (_paramSliders.TryGetValue("override.Fade Speed", out var fsSl))
			_fadeSpeedOverride = (float)fsSl.Value;

		if (_paramCheckBoxes.TryGetValue("override.Reverse", out var revCb))
			_overrideReverse = revCb.ButtonPressed;
		if (_paramCheckBoxes.TryGetValue("override.Dashed", out var dashCb))
			_overrideDashed = dashCb.ButtonPressed;
		if (_paramCheckBoxes.TryGetValue("override.Flicker", out var flkCb))
			_overrideFlicker = flkCb.ButtonPressed;
		if (_paramCheckBoxes.TryGetValue("override.Contract", out var conCb))
			_overrideContract = conCb.ButtonPressed;
	}

	private static string FormatValue(double v, bool asInt) =>
		asInt ? ((int)v).ToString() : v.ToString("F2");

	private float P(string effect, string param) =>
		_paramValues.TryGetValue($"{effect}.{param}", out var v) ? v : 0f;

	// ─── Spawn Logic ─────────────────────────────────────────────────

	private void SpawnSelected()
	{
		var effect = SpawnEffect(_selectedEffect);
		if (effect != null)
			_effects.Add(effect);
	}

	private LineEffect? SpawnEffect(string name)
	{
		if (string.IsNullOrEmpty(name)) return null;
		ReadOverrides();
		var pos = new GridPos(CX, CY);

		LineEffect? effect = name switch
		{
			"LightningBurst" => EffectFactory.LightningBurst(this, pos, _effectColor,
				maxSegs: (int)P("LightningBurst", "maxSegs"),
				duration: P("LightningBurst", "duration"),
				trail: P("LightningBurst", "trail"),
				contProb: P("LightningBurst", "contProb"),
				branchProb: P("LightningBurst", "branchProb")),

			"LightningConverge" => EffectFactory.LightningConverge(this, pos, _effectColor,
				maxSegs: (int)P("LightningConverge", "maxSegs"),
				duration: P("LightningConverge", "duration"),
				trail: P("LightningConverge", "trail"),
				contProb: P("LightningConverge", "contProb"),
				branchProb: P("LightningConverge", "branchProb")),

			"LightningTrail" => EffectFactory.LightningTrail(this, pos,
				(int)P("LightningTrail", "dx"), (int)P("LightningTrail", "dy"),
				_effectColor,
				duration: P("LightningTrail", "duration"),
				maxSegs: (int)P("LightningTrail", "maxSegs"),
				trail: P("LightningTrail", "trail"),
				contProb: P("LightningTrail", "contProb"),
				branchProb: P("LightningTrail", "branchProb")),

			"SpiralTrace" => EffectFactory.SpiralTrace(this, pos, _effectColor,
				duration: P("SpiralTrace", "duration"),
				maxSegs: (int)P("SpiralTrace", "maxSegs"),
				trail: P("SpiralTrace", "trail")),

			"SquareShockwave" => EffectFactory.SquareShockwave(this, pos, _effectColor,
				maxRadius: (int)P("SquareShockwave", "maxRadius"),
				duration: P("SquareShockwave", "duration"),
				trail: P("SquareShockwave", "trail")),

			"CrossContract" => EffectFactory.CrossContract(this, pos, _effectColor,
				duration: P("CrossContract", "duration"),
				armLen: (int)P("CrossContract", "armLen"),
				trail: P("CrossContract", "trail")),

			"CellPerimeter" => EffectFactory.CellPerimeter(this, pos, _effectColor,
				duration: P("CellPerimeter", "duration"),
				trail: P("CellPerimeter", "trail")),

			"JitterArms" => EffectFactory.JitterArms(this, pos,
				(int)P("JitterArms", "dx"), (int)P("JitterArms", "dy"),
				_effectColor,
				duration: P("JitterArms", "duration"),
				armCount: (int)P("JitterArms", "armCount"),
				armLen: (int)P("JitterArms", "armLen"),
				trail: P("JitterArms", "trail")),

			"LineTrail" => EffectFactory.LineTrail(this, pos,
				new GridPos((int)P("LineTrail", "toX"), (int)P("LineTrail", "toY")),
				_effectColor,
				duration: P("LineTrail", "duration"),
				trail: P("LineTrail", "trail")),

			"StraightTracer" => EffectFactory.StraightTracer(this, pos,
				(int)P("StraightTracer", "dx"), (int)P("StraightTracer", "dy"),
				_effectColor,
				duration: P("StraightTracer", "duration"),
				reach: (int)P("StraightTracer", "reach"),
				trail: P("StraightTracer", "trail")),

			"StraightTracerAllDirs" => EffectFactory.StraightTracerAllDirs(this, pos, _effectColor,
				duration: P("StraightTracerAllDirs", "duration"),
				reach: (int)P("StraightTracerAllDirs", "reach"),
				trail: P("StraightTracerAllDirs", "trail")),

			"DashedTendrils" => EffectFactory.DashedTendrils(this, pos, _effectColor,
				duration: P("DashedTendrils", "duration"),
				tendrilCount: (int)P("DashedTendrils", "tendrilCount"),
				minLen: (int)P("DashedTendrils", "minLen"),
				maxLen: (int)P("DashedTendrils", "maxLen"),
				trail: P("DashedTendrils", "trail")),

			"StaggeredArms" => EffectFactory.StaggeredArms(this, pos, _effectColor,
				duration: P("StaggeredArms", "duration"),
				armLen: (int)P("StaggeredArms", "armLen"),
				stagger: (int)P("StaggeredArms", "stagger"),
				trail: P("StaggeredArms", "trail")),

			"SelectSquares" => EffectFactory.SelectSquares(this, pos, _effectColor,
				duration: P("SelectSquares", "duration"),
				trail: P("SelectSquares", "trail")),

			"ConvergingDrain" => EffectFactory.ConvergingDrain(this, pos, _effectColor,
				maxSegs: (int)P("ConvergingDrain", "maxSegs"),
				duration: P("ConvergingDrain", "duration"),
				trail: P("ConvergingDrain", "trail"),
				contProb: P("ConvergingDrain", "contProb"),
				branchProb: P("ConvergingDrain", "branchProb")),

			"ArcChain" => EffectFactory.ArcChain(this, pos, _effectColor,
				duration: P("ArcChain", "duration"),
				arcCount: (int)P("ArcChain", "arcCount"),
				subSegs: (int)P("ArcChain", "subSegs"),
				trail: P("ArcChain", "trail")),

			"CircuitTrace" => EffectFactory.CircuitTrace(this, pos, _effectColor,
				duration: P("CircuitTrace", "duration"),
				maxSegs: (int)P("CircuitTrace", "maxSegs"),
				trail: P("CircuitTrace", "trail")),

			"WavePulse" => EffectFactory.WavePulse(this, pos, _effectColor,
				duration: P("WavePulse", "duration"),
				reach: (int)P("WavePulse", "reach"),
				trail: P("WavePulse", "trail")),

			"SineRipple" => EffectFactory.SineRipple(this, pos, _effectColor,
				duration: P("SineRipple", "duration"),
				laneCount: (int)P("SineRipple", "laneCount"),
				reach: (int)P("SineRipple", "reach"),
				trail: P("SineRipple", "trail")),

			"ZocDashedPulse" => EffectFactory.ZocDashedPulse(this, pos, _effectColor,
				duration: P("ZocDashedPulse", "duration"),
				zocR: (int)P("ZocDashedPulse", "zocR"),
				trail: P("ZocDashedPulse", "trail")),

			_ => null
		};

		if (effect != null)
			ApplyShaderOverrides(effect);
		return effect;
	}

	private void SpawnAll()
	{
		foreach (var def in EffectDefs)
		{
			_selectedEffect = def.Name;
			SpawnSelected();
		}
		// Restore selection
		_selectedEffect = _currentEffectLabel.Text;
	}

	private void ClearAll()
	{
		foreach (var e in _effects)
			e.Destroy();
		_effects.Clear();
		_autoEffect?.Destroy();
		_autoEffect = null;
		_loopTimer = LoopInterval; // trigger immediate respawn next frame
	}

	// ─── Shader Override Application ─────────────────────────────────

	private void ApplyShaderOverrides(LineEffect effect)
	{
		if (_overrideDuration)
			effect.Duration = _durationOverride;

		// Apply shader-level overrides to both core and glow materials
		if (_overrideTrail)
		{
			effect.CoreMat.SetShaderParameter("trail", _trailOverride);
			effect.GlowMat.SetShaderParameter("trail", _trailOverride);
		}
		if (_overrideFadeSpeed)
		{
			effect.CoreMat.SetShaderParameter("fade_speed", _fadeSpeedOverride);
			effect.GlowMat.SetShaderParameter("fade_speed", _fadeSpeedOverride);
		}
		if (_overrideReverse)
		{
			effect.CoreMat.SetShaderParameter("reverse", true);
			effect.GlowMat.SetShaderParameter("reverse", true);
		}
		if (_overrideDashed)
		{
			effect.CoreMat.SetShaderParameter("dashed", true);
			effect.GlowMat.SetShaderParameter("dashed", true);
		}
		if (_overrideFlicker)
		{
			effect.CoreMat.SetShaderParameter("flicker", true);
			effect.GlowMat.SetShaderParameter("flicker", true);
		}
		if (_overrideContract)
		{
			effect.CoreMat.SetShaderParameter("contract", true);
			effect.GlowMat.SetShaderParameter("contract", true);
		}
	}
}
