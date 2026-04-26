using Blocker.Game.Rendering.Effects;
using Blocker.Simulation.Core;
using Godot;
using System;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class MenuAmbience : Node2D
{
	private static readonly Color BlockColor = new(0.267f, 0.667f, 1f, 0.5f);
	private static readonly Color EffectColor = new(1f, 0.667f, 0.2f);

	private const int MaxBlocks = 3;
	private const float MoveIntervalMs = 1500f;
	private const float ExplosionMinMs = 5000f;
	private const float ExplosionMaxMs = 10000f;
	private const float RespawnDelayMs = 2000f;

	private record struct AmbientBlock(int Gx, int Gy, float MoveTimer, float ExplodeTimer, bool Alive, float RespawnTimer);

	private readonly List<AmbientBlock> _blocks = new();
	private readonly Random _rng = new();
	private int _gridCols, _gridRows;
	private float _cellSize;
	private Func<float, float, Vector2>? _gridToPixel;
	private Node2D? _effectLayer;

	private readonly List<GpuEffect> _effects = new();

	public void Initialize(int gridCols, int gridRows, float cellSize,
		Func<float, float, Vector2> gridToPixel, Node2D effectLayer)
	{
		_gridCols = gridCols;
		_gridRows = gridRows;
		_cellSize = cellSize;
		_gridToPixel = gridToPixel;
		_effectLayer = effectLayer;

		EffectFactory.Initialize();

		for (int i = 0; i < MaxBlocks; i++)
			_blocks.Add(SpawnBlock());
	}

	private AmbientBlock SpawnBlock()
	{
		int side = _rng.Next(4);
		int gx, gy;
		switch (side)
		{
			case 0: gx = 0; gy = _rng.Next(_gridRows); break;
			case 1: gx = _gridCols - 1; gy = _rng.Next(_gridRows); break;
			case 2: gx = _rng.Next(_gridCols); gy = 0; break;
			default: gx = _rng.Next(_gridCols); gy = _gridRows - 1; break;
		}
		float explodeTimer = ExplosionMinMs + (float)_rng.NextDouble() * (ExplosionMaxMs - ExplosionMinMs);
		return new AmbientBlock(gx, gy, MoveIntervalMs, explodeTimer, true, 0);
	}

	public override void _Process(double delta)
	{
		float dt = (float)delta * 1000f;

		for (int i = 0; i < _blocks.Count; i++)
		{
			var b = _blocks[i];

			if (!b.Alive)
			{
				b.RespawnTimer -= dt;
				if (b.RespawnTimer <= 0)
					b = SpawnBlock();
				_blocks[i] = b;
				continue;
			}

			b.MoveTimer -= dt;
			if (b.MoveTimer <= 0)
			{
				b.MoveTimer = MoveIntervalMs + (float)_rng.NextDouble() * 500f;
				int dir = _rng.Next(4);
				int dx = dir switch { 0 => 1, 1 => -1, _ => 0 };
				int dy = dir switch { 2 => 1, 3 => -1, _ => 0 };
				int nx = Math.Clamp(b.Gx + dx, 1, _gridCols - 2);
				int ny = Math.Clamp(b.Gy + dy, 1, _gridRows - 2);
				b = b with { Gx = nx, Gy = ny };
			}

			b.ExplodeTimer -= dt;
			if (b.ExplodeTimer <= 0)
			{
				Explode(b.Gx, b.Gy);
				b = b with { Alive = false, RespawnTimer = RespawnDelayMs };
			}

			_blocks[i] = b;
		}

		for (int i = _effects.Count - 1; i >= 0; i--)
		{
			var effect = _effects[i];
			effect.Age += dt;
			effect.Update();
			if (effect.Progress >= 1f && !effect.Looping)
			{
				effect.Destroy();
				_effects.RemoveAt(i);
			}
		}

		QueueRedraw();
	}

	private void Explode(int gx, int gy)
	{
		if (_effectLayer == null) return;

		var pos = new GridPos(gx, gy);
		int effectType = _rng.Next(5);
		LineEffect effect = effectType switch
		{
			0 => EffectFactory.LightningBurst(_effectLayer, pos, EffectColor, maxSegs: 30, duration: 1000f),
			1 => EffectFactory.SpiralTrace(_effectLayer, pos, EffectColor, duration: 1200f, maxSegs: 25),
			2 => EffectFactory.SquareShockwave(_effectLayer, pos, EffectColor, maxRadius: 5, duration: 800f),
			3 => EffectFactory.StaggeredArms(_effectLayer, pos, EffectColor, duration: 700f),
			_ => EffectFactory.DashedTendrils(_effectLayer, pos, EffectColor, duration: 1000f, tendrilCount: 5),
		};
		_effects.Add(effect);
	}

	public override void _Draw()
	{
		if (_gridToPixel == null) return;

		foreach (var b in _blocks)
		{
			if (!b.Alive) continue;
			var pos = _gridToPixel(b.Gx, b.Gy);
			DrawRect(new Rect2(pos.X + 2, pos.Y + 2, _cellSize - 4, _cellSize - 4), BlockColor);
			DrawRect(new Rect2(pos.X - 1, pos.Y - 1, _cellSize + 2, _cellSize + 2),
				new Color(BlockColor.R, BlockColor.G, BlockColor.B, 0.1f));
		}
	}
}
