using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Manages post-processing shader effects via a CanvasLayer with ColorRects.
/// Each effect can be toggled on/off from the inspector.
/// </summary>
public partial class PostProcessingManager : CanvasLayer
{
	[ExportGroup("Toggle Effects")]
	[Export] public bool EnableVignette { get; set; } = true;
	[Export] public bool EnableFilmGrain { get; set; } = true;
	[Export] public bool EnableColorGrading { get; set; } = true;
	[Export] public bool EnableDirectionalBloom { get; set; } = true;
	[Export] public bool EnableScreenDistortion { get; set; } = true;

	private ColorRect _distortionRect = null!;
	private ColorRect _bloomRect = null!;
	private ColorRect _gradingRect = null!;
	private ColorRect _vignetteRect = null!;
	private ColorRect _grainRect = null!;

	private ShaderMaterial _distortionMat = null!;

	// Distortion sources: screen-space position, intensity, remaining life (0-1)
	private record struct DistortionSource(Vector2 ScreenPos, float Intensity, float Life, float MaxLife);
	private readonly List<DistortionSource> _distortionSources = [];

	private GameState? _gameState;
	private GridRenderer? _gridRenderer;

	public void SetGameState(GameState state) => _gameState = state;
	public void SetGridRenderer(GridRenderer renderer) => _gridRenderer = renderer;

	public override void _Ready()
	{
		// CanvasLayer renders on top of everything
		Layer = 100;
		FollowViewportEnabled = false;

		// Create effect ColorRects in stack order (bottom to top)
		_distortionRect = CreateEffectRect("res://Assets/Shaders/screen_distortion.gdshader");
		_bloomRect = CreateEffectRect("res://Assets/Shaders/directional_bloom.gdshader");
		_gradingRect = CreateEffectRect("res://Assets/Shaders/color_grading.gdshader");
		_vignetteRect = CreateEffectRect("res://Assets/Shaders/vignette.gdshader");
		_grainRect = CreateEffectRect("res://Assets/Shaders/film_grain.gdshader");

		_distortionMat = (ShaderMaterial)_distortionRect.Material;
	}

	private ColorRect CreateEffectRect(string shaderPath)
	{
		var shader = GD.Load<Shader>(shaderPath);
		var mat = new ShaderMaterial { Shader = shader };
		var rect = new ColorRect
		{
			Material = mat,
			MouseFilter = Control.MouseFilterEnum.Ignore,
		};
		AddChild(rect);
		// Must call after AddChild so the rect is in the tree
		rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
		return rect;
	}

	public override void _Process(double delta)
	{
		// Toggle visibility
		_vignetteRect.Visible = EnableVignette;
		_grainRect.Visible = EnableFilmGrain;
		_gradingRect.Visible = EnableColorGrading;
		_bloomRect.Visible = EnableDirectionalBloom;
		_distortionRect.Visible = EnableScreenDistortion;

		// Process distortion sources from visual events
		if (_gameState != null && _gridRenderer != null && EnableScreenDistortion)
		{
			ConsumeVisualEvents();
			UpdateDistortionSources((float)delta);
		}
	}

	private void ConsumeVisualEvents()
	{
		foreach (var evt in _gameState!.VisualEvents)
		{
			float intensity = evt.Type switch
			{
				VisualEventType.BlockDied => 1.0f,
				VisualEventType.SelfDestructed => 1.5f,
				VisualEventType.StunRayHit => 0.6f,
				VisualEventType.BlastRayFired => 0.8f,
				VisualEventType.PushWaveFired => 0.5f,
				VisualEventType.JumpExecuted => 0.4f,
				_ => 0f
			};

			if (intensity <= 0f) continue;

			// Convert grid position to screen-space UV
			var worldPos = new Vector2(
				evt.Position.X * GridRenderer.CellSize + GridRenderer.CellSize * 0.5f,
				evt.Position.Y * GridRenderer.CellSize + GridRenderer.CellSize * 0.5f
			);
			var screenPos = WorldToScreenUV(worldPos);

			float duration = 0.6f;
			_distortionSources.Add(new DistortionSource(screenPos, intensity, duration, duration));
		}
	}

	private Vector2 WorldToScreenUV(Vector2 worldPos)
	{
		// Transform world position through the camera to get screen UV
		var viewport = GetViewport();
		var canvas = viewport.GetCamera2D();
		if (canvas == null)
			return new Vector2(0.5f, 0.5f);

		var viewportSize = viewport.GetVisibleRect().Size;
		var camPos = canvas.GlobalPosition;
		var zoom = canvas.Zoom;

		// World to screen pixel
		var screenPixel = (worldPos - camPos) * zoom + viewportSize * 0.5f;
		// Screen pixel to UV
		return screenPixel / viewportSize;
	}

	private void UpdateDistortionSources(float delta)
	{
		// Age and remove expired sources
		for (int i = _distortionSources.Count - 1; i >= 0; i--)
		{
			var src = _distortionSources[i];
			float newLife = src.Life - delta;
			if (newLife <= 0f)
				_distortionSources.RemoveAt(i);
			else
				_distortionSources[i] = src with { Life = newLife };
		}

		// Pack into shader uniforms (max 8)
		int count = Mathf.Min(_distortionSources.Count, 8);
		var sources = new Vector4[8];
		for (int i = 0; i < count; i++)
		{
			var src = _distortionSources[i];
			float normalizedLife = src.Life / src.MaxLife;
			sources[i] = new Vector4(src.ScreenPos.X, src.ScreenPos.Y, src.Intensity, normalizedLife);
		}

		_distortionMat.SetShaderParameter("sources", sources);
		_distortionMat.SetShaderParameter("source_count", count);
	}
}
