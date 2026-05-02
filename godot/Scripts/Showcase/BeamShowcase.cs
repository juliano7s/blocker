using Godot;
using System;
using System.Collections.Generic;
using Blocker.Game.Rendering;
using Blocker.Game.Rendering.Effects;

namespace Blocker.Game.Showcase;

public partial class BeamShowcase : Node2D
{
	private const float CellSize = GridRenderer.CellSize;
	private const float GridPadding = GridRenderer.GridPadding;
	private const int GridW = 30;
	private const int GridH = 20;

	private static readonly Color BackgroundColor = new(0.06f, 0.06f, 0.1f);
	private static readonly Color GridLineColor = new(0.12f, 0.12f, 0.18f);

	private readonly List<RayEffect> _effects = new();

	private Shader _beamShader = null!;
	private Shader _lightningShader = null!;
	private Shader _plasmaShader = null!;
	private Shader _trailShader = null!;
	private Shader _projectileShader = null!;

	// UI
	private VBoxContainer _buttonPanel = null!;
	private Label _titleLabel = null!;
	private CheckBox _autoLoop = null!;
	private float _loopTimer;
	private string _lastFired = "";

	// Color picker
	private ColorPickerButton _colorPicker = null!;
	private Color _effectColor = new(0.3f, 0.8f, 1f);

	// ─── Effect Definitions ─────────────────────────────────────────

	private record EffectDef(string Name, string Category, Action<BeamShowcase, int> Spawn);

	private static readonly EffectDef[] Defs =
	{
		// ── Laser Beams ──
		new("Thin Sniper", "LASER", (s, row) => s.SpawnBeam(row, coreEdge: 0.3f, glowEdge: 0.7f, flare: 0.02f, coreH: 1.5f, glowH: 4f)),
		new("Standard Beam", "LASER", (s, row) => s.SpawnBeam(row, coreEdge: 0.4f, glowEdge: 0.6f, flare: 0.04f, coreH: 2.5f, glowH: 6f)),
		new("Heavy Cannon", "LASER", (s, row) => s.SpawnBeam(row, coreEdge: 0.3f, glowEdge: 0.5f, flare: 0.06f, coreH: 4f, glowH: 9f)),
		new("Pulsing Beam", "LASER", (s, row) => s.SpawnBeam(row, coreEdge: 0.4f, glowEdge: 0.6f, flare: 0.04f, coreH: 2.5f, glowH: 6f, pulseSpeed: 12f, pulseAmp: 0.2f)),
		new("Rapid Thin", "LASER", (s, row) => s.SpawnBeam(row, coreEdge: 0.3f, glowEdge: 0.7f, flare: 0.02f, coreH: 1f, glowH: 3f, duration: 350f)),

		// ── Lightning Bolts ──
		new("Single Bolt", "LIGHTNING", (s, row) => s.SpawnLightning(row, forks: 0f, jagAmp: 0.12f)),
		new("Forked Bolt", "LIGHTNING", (s, row) => s.SpawnLightning(row, forks: 2f, jagAmp: 0.15f, forkSpread: 0.15f)),
		new("Chain Lightning", "LIGHTNING", (s, row) => s.SpawnLightning(row, forks: 4f, jagAmp: 0.10f, forkSpread: 0.10f, boltThick: 0.015f)),
		new("Wild Arc", "LIGHTNING", (s, row) => s.SpawnLightning(row, forks: 1f, jagAmp: 0.25f, jagFreq: 15f, reRand: 12f)),
		new("Smooth Zap", "LIGHTNING", (s, row) => s.SpawnLightning(row, forks: 0f, jagAmp: 0.08f, jagFreq: 5f, boltThick: 0.03f, glowFall: 0.20f)),

		// ── Plasma / Fire ──
		new("Tight Plasma", "PLASMA", (s, row) => s.SpawnPlasma(row, width: 0.08f, turb: 0.3f)),
		new("Wide Plasma", "PLASMA", (s, row) => s.SpawnPlasma(row, width: 0.18f, turb: 0.5f)),
		new("Fire Breath", "PLASMA", (s, row) => s.SpawnPlasma(row, width: 0.22f, turb: 0.8f, detail: 6f, speed: 5f)),
		new("Plasma Torch", "PLASMA", (s, row) => s.SpawnPlasma(row, width: 0.12f, turb: 0.4f, intensity: 1.8f, speed: 4f)),
		new("Ember Stream", "PLASMA", (s, row) => s.SpawnPlasma(row, width: 0.10f, turb: 1.0f, detail: 7f, speed: 6f)),

		// ── Jump Trails ──
		new("Energy Ribbon", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 0, width: 0.18f)),
		new("Wide Ribbon", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 0, width: 0.30f, shimmer: 8f)),
		new("Ghost Afterimage", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 1, width: 0.18f)),
		new("Warp Distortion", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 2, width: 0.15f, detail: 5f)),
		new("Warp (Intense)", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 2, width: 0.25f, detail: 3f, shimmer: 8f)),
		new("Spark Scatter", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 3, width: 0.20f)),
		new("Dense Sparks", "TRAIL", (s, row) => s.SpawnTrail(row, mode: 3, width: 0.30f, dissolve: 0.7f)),

		// ── Projectiles ──
		new("Energy Ball", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 0, size: 0.05f, trailLen: 0.2f)),
		new("Big Energy Ball", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 0, size: 0.08f, trailLen: 0.3f, trailWidth: 0.8f)),
		new("Lightning Orb", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 1, size: 0.06f, trailLen: 0.15f, flicker: 12f)),
		new("Lightning Orb (Wild)", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 1, size: 0.07f, trailLen: 0.20f, flicker: 18f, turb: 0.8f)),
		new("Fireball", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 2, size: 0.07f, trailLen: 0.25f, turb: 0.6f)),
		new("Fireball (Intense)", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 2, size: 0.09f, trailLen: 0.35f, turb: 1.0f, trailWidth: 0.8f)),
		new("Comet", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 3, size: 0.05f, trailLen: 0.4f, trailWidth: 0.5f)),
		new("Comet (Long Tail)", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 3, size: 0.04f, trailLen: 0.55f, trailWidth: 0.4f)),
		new("Blast Comet", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 3, size: 0.05f, trailLen: 0.4f, trailWidth: 0.5f, overrideColor: new Color(1f, 0.5f, 0.2f))),
		new("Lightning Comet", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 4, size: 0.05f, trailLen: 0.35f, trailWidth: 0.7f, flicker: 10f)),
		new("Lightning Comet (Small)", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 4, size: 0.035f, trailLen: 0.25f, trailWidth: 0.5f, flicker: 12f)),
		new("Lightning Comet (Wild)", "PROJECTILE", (s, row) => s.SpawnProjectile(row, mode: 4, size: 0.06f, trailLen: 0.45f, trailWidth: 0.8f, flicker: 16f)),
	};

	public override void _Ready()
	{
		_beamShader = GD.Load<Shader>("res://Assets/Shaders/beam_ray.gdshader");
		_lightningShader = GD.Load<Shader>("res://Assets/Shaders/lightning_ray.gdshader");
		_plasmaShader = GD.Load<Shader>("res://Assets/Shaders/plasma_ray.gdshader");
		_trailShader = GD.Load<Shader>("res://Assets/Shaders/jump_trail.gdshader");
		_projectileShader = GD.Load<Shader>("res://Assets/Shaders/projectile.gdshader");

		BuildUI();
	}

	public override void _Process(double delta)
	{
		float dtMs = (float)delta * 1000f;

		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var eff = _effects[i];
			eff.Age += dtMs;
			eff.Update();
			if (eff.Progress >= 1f)
			{
				eff.Destroy();
				_effects.RemoveAt(i);
			}
		}

		if (_autoLoop != null && _autoLoop.ButtonPressed && _lastFired != "")
		{
			_loopTimer += dtMs;
			if (_loopTimer > 1500f)
			{
				_loopTimer = 0f;
				FireByName(_lastFired);
			}
		}
	}

	public override void _Draw()
	{
		DrawRect(new Rect2(0, 0, GridW * CellSize + GridPadding * 2, GridH * CellSize + GridPadding * 2),
			BackgroundColor);

		for (int x = 0; x <= GridW; x++)
		{
			float px = x * CellSize + GridPadding;
			DrawLine(new Vector2(px, GridPadding), new Vector2(px, GridH * CellSize + GridPadding), GridLineColor);
		}
		for (int y = 0; y <= GridH; y++)
		{
			float py = y * CellSize + GridPadding;
			DrawLine(new Vector2(GridPadding, py), new Vector2(GridW * CellSize + GridPadding, py), GridLineColor);
		}
	}

	public override void _UnhandledInput(InputEvent @event)
	{
		if (@event is InputEventKey { Pressed: true } key)
		{
			if (key.Keycode == Key.Space && _lastFired != "")
				FireByName(_lastFired);
			else if (key.Keycode == Key.C)
				ClearAll();
		}
	}

	// ─── Multi-layer spawning ───────────────────────────────────────
	// Each effect creates 2 overlapping ColorRects:
	//   1. Glow layer (wide, dim, is_core=false)
	//   2. Core layer (narrow, bright, is_core=true)

	private RayEffect CreateLayeredEffect(Shader shader, float duration,
		int row, int lengthCells, float coreHeightCells, float glowHeightCells,
		Action<ShaderMaterial, bool> configureParams)
	{
		var eff = new RayEffect { Duration = duration };
		float startX = 3 * CellSize + GridPadding;
		float centerY = (2 + row * 3) * CellSize + GridPadding;
		float rectW = lengthCells * CellSize;

		// Layer 1: Wide glow
		float glowH = glowHeightCells * CellSize;
		var glowMat = new ShaderMaterial { Shader = shader };
		configureParams(glowMat, false);
		glowMat.SetShaderParameter("is_core", false);
		glowMat.SetShaderParameter("brightness", 0.8f);
		var glowRect = new ColorRect
		{
			Position = new Vector2(startX, centerY - glowH / 2),
			Size = new Vector2(rectW, glowH),
			Color = Colors.White,
			Material = glowMat,
		};
		AddChild(glowRect);
		eff.Layers.Add((glowMat, glowRect));

		// Layer 2: Narrow bright core
		float coreH = coreHeightCells * CellSize;
		var coreMat = new ShaderMaterial { Shader = shader };
		configureParams(coreMat, true);
		coreMat.SetShaderParameter("is_core", true);
		coreMat.SetShaderParameter("brightness", 1.5f);
		var coreRect = new ColorRect
		{
			Position = new Vector2(startX, centerY - coreH / 2),
			Size = new Vector2(rectW, coreH),
			Color = Colors.White,
			Material = coreMat,
		};
		AddChild(coreRect);
		eff.Layers.Add((coreMat, coreRect));

		_effects.Add(eff);
		return eff;
	}

	// ─── Beam ───────────────────────────────────────────────────────

	private void SpawnBeam(int row, float coreEdge = 0.4f, float glowEdge = 0.6f,
		float flare = 0.03f, float coreH = 2.5f, float glowH = 6f,
		float pulseSpeed = 0f, float pulseAmp = 0f, float duration = 600f)
	{
		CreateLayeredEffect(_beamShader, duration, row, 14, coreH, glowH,
			(mat, isCore) =>
			{
				mat.SetShaderParameter("beam_color", isCore ? Colors.White : _effectColor);
				mat.SetShaderParameter("edge_softness", isCore ? coreEdge : glowEdge);
				mat.SetShaderParameter("taper_tip", 0.06f);
				mat.SetShaderParameter("flare_size", flare);
				mat.SetShaderParameter("pulse_speed", pulseSpeed);
				mat.SetShaderParameter("pulse_amp", pulseAmp);
			});
	}

	// ─── Lightning ──────────────────────────────────────────────────

	private void SpawnLightning(int row, float forks = 1f, float jagAmp = 0.15f,
		float jagFreq = 10f, float boltThick = 0.02f, float glowFall = 0.12f,
		float forkSpread = 0.12f, float reRand = 6f, float duration = 600f)
	{
		CreateLayeredEffect(_lightningShader, duration, row, 14, 4f, 7f,
			(mat, isCore) =>
			{
				mat.SetShaderParameter("bolt_color", _effectColor);
				mat.SetShaderParameter("bolt_thickness", boltThick);
				mat.SetShaderParameter("glow_falloff", glowFall);
				mat.SetShaderParameter("jag_amplitude", jagAmp);
				mat.SetShaderParameter("jag_frequency", jagFreq);
				mat.SetShaderParameter("fork_count", forks);
				mat.SetShaderParameter("fork_spread", forkSpread);
				mat.SetShaderParameter("flicker_speed", 8f);
				mat.SetShaderParameter("re_randomize_rate", reRand);
			});
	}

	// ─── Plasma ─────────────────────────────────────────────────────

	private void SpawnPlasma(int row, float width = 0.15f, float turb = 0.5f,
		float speed = 3f, float detail = 4f, float intensity = 1f, float duration = 700f)
	{
		CreateLayeredEffect(_plasmaShader, duration, row, 14, 4f, 8f,
			(mat, isCore) =>
			{
				mat.SetShaderParameter("inner_color", new Color(1f, 0.95f, 0.8f));
				mat.SetShaderParameter("outer_color", _effectColor);
				mat.SetShaderParameter("plasma_width", width);
				mat.SetShaderParameter("turbulence", turb);
				mat.SetShaderParameter("speed", speed);
				mat.SetShaderParameter("detail", detail);
			});
	}

	// ─── Trail ──────────────────────────────────────────────────────

	private void SpawnTrail(int row, int mode = 0, float width = 0.18f,
		float shimmer = 5f, float dissolve = 1f, float detail = 4f, float duration = 800f)
	{
		CreateLayeredEffect(_trailShader, duration, row, 10, 3f, 6f,
			(mat, isCore) =>
			{
				mat.SetShaderParameter("trail_color", _effectColor);
				mat.SetShaderParameter("mode", mode);
				mat.SetShaderParameter("trail_width", width);
				mat.SetShaderParameter("shimmer_speed", shimmer);
				mat.SetShaderParameter("dissolve_rate", dissolve);
				mat.SetShaderParameter("detail_scale", detail);
			});
	}

	// ─── Projectile ─────────────────────────────────────────────────

	private void SpawnProjectile(int row, int mode = 0, float size = 0.06f,
		float trailLen = 0.25f, float trailWidth = 0.6f, float turb = 0.5f,
		float flicker = 8f, float duration = 800f, Color? overrideColor = null)
	{
		var color = overrideColor ?? _effectColor;
		CreateLayeredEffect(_projectileShader, duration, row, 14, 4f, 8f,
			(mat, isCore) =>
			{
				mat.SetShaderParameter("proj_color", color);
				mat.SetShaderParameter("mode", mode);
				mat.SetShaderParameter("proj_size", size);
				mat.SetShaderParameter("trail_length", trailLen);
				mat.SetShaderParameter("trail_width", trailWidth);
				mat.SetShaderParameter("turbulence", turb);
				mat.SetShaderParameter("flicker_speed", flicker);
			});
	}

	// ─── Helpers ────────────────────────────────────────────────────

	private void ClearAll()
	{
		foreach (var eff in _effects)
			eff.Destroy();
		_effects.Clear();
	}

	private void FireByName(string name)
	{
		for (int i = 0; i < Defs.Length; i++)
		{
			if (Defs[i].Name == name)
			{
				int row = GetRowForIndex(i);
				Defs[i].Spawn(this, row);
				return;
			}
		}
	}

	private int GetRowForIndex(int i)
	{
		string cat = Defs[i].Category;
		int row = 0;
		for (int j = 0; j < i; j++)
			if (Defs[j].Category == cat) row++;
		return row;
	}

	// ─── UI ─────────────────────────────────────────────────────────

	private void BuildUI()
	{
		var canvasLayer = new CanvasLayer();
		AddChild(canvasLayer);

		var marginContainer = new MarginContainer();
		marginContainer.AddThemeConstantOverride("margin_right", 12);
		marginContainer.AddThemeConstantOverride("margin_top", 12);
		marginContainer.AnchorRight = 1f;
		marginContainer.AnchorBottom = 1f;
		canvasLayer.AddChild(marginContainer);

		var outerHBox = new HBoxContainer();
		outerHBox.Alignment = BoxContainer.AlignmentMode.End;
		marginContainer.AddChild(outerHBox);

		var panel = new PanelContainer();
		var panelStyle = new StyleBoxFlat
		{
			BgColor = new Color(0.08f, 0.08f, 0.12f, 0.92f),
			CornerRadiusTopLeft = 8,
			CornerRadiusTopRight = 8,
			CornerRadiusBottomLeft = 8,
			CornerRadiusBottomRight = 8,
			ContentMarginLeft = 12,
			ContentMarginRight = 12,
			ContentMarginTop = 8,
			ContentMarginBottom = 8,
		};
		panel.AddThemeStyleboxOverride("panel", panelStyle);
		outerHBox.AddChild(panel);

		var scroll = new ScrollContainer();
		scroll.CustomMinimumSize = new Vector2(220, 0);
		panel.AddChild(scroll);

		_buttonPanel = new VBoxContainer();
		_buttonPanel.AddThemeConstantOverride("separation", 2);
		scroll.AddChild(_buttonPanel);

		_titleLabel = new Label { Text = "Beam & Trail Showcase" };
		_titleLabel.AddThemeColorOverride("font_color", Colors.White);
		_titleLabel.AddThemeFontSizeOverride("font_size", 16);
		_buttonPanel.AddChild(_titleLabel);

		var colorRow = new HBoxContainer();
		colorRow.AddChild(new Label { Text = "Color: " });
		_colorPicker = new ColorPickerButton
		{
			Color = _effectColor,
			CustomMinimumSize = new Vector2(40, 25),
		};
		_colorPicker.ColorChanged += c => _effectColor = c;
		colorRow.AddChild(_colorPicker);
		_buttonPanel.AddChild(colorRow);

		var presetRow = new HBoxContainer();
		presetRow.AddThemeConstantOverride("separation", 4);
		AddColorSwatch(presetRow, new Color(0.3f, 0.7f, 1f), "Cyan");
		AddColorSwatch(presetRow, new Color(1f, 0.5f, 0.2f), "Orange");
		AddColorSwatch(presetRow, new Color(0.4f, 1f, 0.5f), "Green");
		AddColorSwatch(presetRow, new Color(1f, 0.3f, 0.3f), "Red");
		AddColorSwatch(presetRow, new Color(0.7f, 0.4f, 1f), "Purple");
		AddColorSwatch(presetRow, new Color(1f, 1f, 0.4f), "Yellow");
		_buttonPanel.AddChild(presetRow);

		_autoLoop = new CheckBox { Text = "Auto-loop" };
		_buttonPanel.AddChild(_autoLoop);

		_buttonPanel.AddChild(new HSeparator());

		var instrLabel = new Label { Text = "Space=replay  C=clear" };
		instrLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
		instrLabel.AddThemeFontSizeOverride("font_size", 11);
		_buttonPanel.AddChild(instrLabel);

		_buttonPanel.AddChild(new HSeparator());

		string currentCat = "";
		for (int i = 0; i < Defs.Length; i++)
		{
			var def = Defs[i];
			if (def.Category != currentCat)
			{
				currentCat = def.Category;
				_buttonPanel.AddChild(new HSeparator());
				var catLabel = new Label { Text = currentCat };
				catLabel.AddThemeColorOverride("font_color", new Color(0.7f, 0.7f, 0.8f));
				catLabel.AddThemeFontSizeOverride("font_size", 13);
				_buttonPanel.AddChild(catLabel);
			}

			int idx = i;
			var btn = new Button { Text = def.Name };
			btn.Pressed += () =>
			{
				ClearAll();
				_lastFired = Defs[idx].Name;
				int row = GetRowForIndex(idx);
				Defs[idx].Spawn(this, row);
			};
			_buttonPanel.AddChild(btn);
		}

		_buttonPanel.AddChild(new HSeparator());
		foreach (var cat in new[] { "LASER", "LIGHTNING", "PLASMA", "TRAIL", "PROJECTILE" })
		{
			var catName = cat;
			var btn = new Button { Text = $"All {cat}" };
			btn.Pressed += () => FireAllInCategory(catName);
			_buttonPanel.AddChild(btn);
		}
	}

	private void FireAllInCategory(string category)
	{
		ClearAll();
		_lastFired = "";
		for (int i = 0; i < Defs.Length; i++)
		{
			if (Defs[i].Category == category)
			{
				int row = GetRowForIndex(i);
				Defs[i].Spawn(this, row);
			}
		}
	}

	private void AddColorSwatch(HBoxContainer row, Color color, string tooltip)
	{
		var swatch = new Button
		{
			CustomMinimumSize = new Vector2(22, 22),
			TooltipText = tooltip,
		};
		var style = new StyleBoxFlat
		{
			BgColor = color,
			CornerRadiusTopLeft = 3,
			CornerRadiusTopRight = 3,
			CornerRadiusBottomLeft = 3,
			CornerRadiusBottomRight = 3,
		};
		swatch.AddThemeStyleboxOverride("normal", style);
		swatch.AddThemeStyleboxOverride("hover", style);
		swatch.AddThemeStyleboxOverride("pressed", style);
		swatch.Pressed += () =>
		{
			_effectColor = color;
			_colorPicker.Color = color;
		};
		row.AddChild(swatch);
	}
}
