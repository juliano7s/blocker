using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.Rendering;

/// <summary>
/// Draws the grid: cell backgrounds by ground type, grid lines, and block placeholders.
/// Reads simulation GameState — never mutates it.
/// </summary>
public partial class GridRenderer : Node2D
{
	private void DrawFormations()
	{
		foreach (var formation in _gameState!.Formations)
		{
			var palette = _config.GetPalette(formation.PlayerId);
			var (outlineColor, outlineGlow, diamondColor) = GetFormationStyle(formation.Type, palette);
			float alpha = formation.TeardownTimer > 0 ? 0.45f : 0.9f;

			foreach (var id in formation.MemberIds)
			{
				var block = _gameState.GetBlock(id);
				if (block == null) continue;
				var blockRect = GetFormationBlockRect(block.Pos);
				DrawFormationBlock(blockRect, outlineColor with { A = alpha }, outlineGlow with { A = alpha * 0.35f }, diamondColor with { A = alpha });

				if (formation.TeardownTimer > 0)
					DrawTeardownOverlay(block, formation.TeardownTimer);
			}
		}

		foreach (var nest in _gameState!.Nests)
		{
			var palette = _config.GetPalette(nest.PlayerId);
			var (outlineColor, outlineGlow, diamondColor) = GetNestStyle(nest.Type, palette);
			float alpha = nest.TeardownTimer > 0 ? 0.45f : 0.9f;

			foreach (var id in nest.MemberIds)
			{
				var block = _gameState.GetBlock(id);
				if (block == null) continue;
				var blockRect = GetFormationBlockRect(block.Pos);
				DrawFormationBlock(blockRect, outlineColor with { A = alpha }, outlineGlow with { A = alpha * 0.35f }, diamondColor with { A = alpha });

				if (nest.TeardownTimer > 0)
					DrawTeardownOverlay(block, nest.TeardownTimer);
			}
		}
	}

	private Rect2 GetFormationBlockRect(GridPos pos)
	{
		var center = GridToWorld(pos);
		const float inset = 2f;
		return new Rect2(
			center.X - CellSize / 2f + inset,
			center.Y - CellSize / 2f + inset,
			CellSize - inset * 2,
			CellSize - inset * 2);
	}

	private void DrawFormationBlock(Rect2 blockRect, Color outlineColor, Color outlineGlow, Color diamondColor)
	{
		// Double-stroke glow: outer glow + inner solid (matching game bible 16.13)
		DrawRect(blockRect.Grow(1f), outlineGlow, false, 4f);
		DrawRect(blockRect, outlineColor, false, 1.5f);

		// Small corner brackets outside the outline
		DrawOuterCornerTicks(blockRect, 4f, outlineColor, 1.5f);

		// Static centered diamond
		var center = blockRect.GetCenter();
		DrawFormationGem(center, diamondColor);
	}

	/// <summary>
	/// Formation block: bright gem diamond in center.
	/// </summary>
	private void DrawFormationGem(Vector2 center, Color color)
	{
		float size = CellSize * 0.16f;
		// Outer glow (subtle)
		DrawRotatedDiamond(center, size * 1.3f, 0, color.Lightened(0.3f) with { A = 0.5f });
		// Main gem (bright)
		DrawRotatedDiamond(center, size, 0, color.Lightened(0.6f));
		// Inner highlight
		DrawRotatedDiamond(center, size * 0.4f, 0, Colors.White with { A = 0.9f });
	}

	private void DrawTeardownOverlay(Block block, int teardownTimer)
	{
		float tearProgress = (float)teardownTimer / Constants.TeardownTicks;
		var center = GridToWorld(block.Pos);
		var rect = new Rect2(
			center.X - CellSize * tearProgress * 0.5f,
			center.Y - CellSize * tearProgress * 0.5f,
			CellSize * tearProgress,
			CellSize * tearProgress);
		DrawRect(rect, new Color(0.5f, 0.5f, 0.5f, 0.3f));
	}

	private static (Color Outline, Color OutlineGlow, Color Diamond) GetFormationStyle(FormationType type, PlayerPalette palette) =>
		type switch
		{
			FormationType.Supply => (palette.SupplyOutline, palette.SupplyOutlineGlow, palette.SupplyDiamond),
			_ => (palette.SupplyOutline, palette.SupplyOutlineGlow, palette.SupplyDiamond),
		};

	private static (Color Outline, Color OutlineGlow, Color Diamond) GetNestStyle(NestType type, PlayerPalette palette) =>
		type switch
		{
			NestType.Builder => (palette.BuilderNestOutline, palette.BuilderNestOutlineGlow, palette.BuilderNestDiamond),
			NestType.Soldier => (palette.SoldierNestOutline, palette.SoldierNestOutlineGlow, palette.SoldierNestDiamond),
			NestType.Stunner => (palette.StunnerNestOutline, palette.StunnerNestOutlineGlow, palette.StunnerNestDiamond),
			_ => (palette.BuilderNestOutline, palette.BuilderNestOutlineGlow, palette.BuilderNestDiamond),
		};

	private void DrawNestProgress()
	{
		foreach (var nest in _gameState!.Nests)
		{
			if (nest.IsPaused) continue;
			if (nest.SpawnProgress <= 0) continue;

			var center = GridToWorld(nest.Center);
			int spawnTicks = nest.GetSpawnTicks(
				_gameState.Grid.InBounds(nest.Center) ? _gameState.Grid[nest.Center].Ground : GroundType.Normal);
			if (spawnTicks <= 0) continue;

			float progress = (float)nest.SpawnProgress / spawnTicks;

			var palette = _config.GetPalette(nest.PlayerId);
			Color nestColor = nest.Type switch
			{
				NestType.Builder => palette.BuilderNestSpawnBar,
				NestType.Soldier => palette.SoldierNestSpawnBar,
				NestType.Stunner => palette.StunnerNestSpawnBar,
				_ => Colors.White
			};

			// Fill the center cell from bottom up
			var cellLeft = center.X - CellSize / 2f + 2f;
			var cellInner = CellSize - 4f;
			var fillHeight = cellInner * progress;
			var fillY = center.Y + CellSize / 2f - 2f - fillHeight;
			DrawRect(new Rect2(cellLeft, fillY, cellInner, fillHeight),
				nestColor with { A = 0.35f });
		}
	}
}
