using Blocker.Game.Config;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;
using System.Collections.Generic;
using System.Linq;

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
            if (formation.MemberIds.Count == 0) continue;
            var centerBlock = _gameState.GetBlock(formation.MemberIds[0]);
            if (centerBlock == null) continue;
            if (_localVisibility != null && !_localVisibility.IsVisible(centerBlock.Pos)) continue;

            var palette = _config.GetPalette(formation.PlayerId);
            var (outlineColor, outlineGlow, diamondColor) = GetFormationStyle(formation.Type, palette);

            _formationMembers.Clear();
            foreach (var id in formation.MemberIds)
            {
                var block = _gameState.GetBlock(id);
                if (block == null) continue;
                _formationMembers.Add((id, block.Pos));
                var blockRect = GetFormationBlockRect(block.Pos);
                DrawFormationBlock(blockRect, outlineColor, outlineGlow, diamondColor);

                if (formation.TeardownTimer > 0)
                    DrawTeardownOverlay(block, formation.TeardownTimer);
            }

            DrawFormationJoiners(_formationMembers, outlineColor, 1f);
        }

        foreach (var nest in _gameState!.Nests)
        {
            if (_localVisibility != null && !_localVisibility.IsVisible(nest.Center)) continue;

            var palette = _config.GetPalette(nest.PlayerId);
            var (outlineColor, outlineGlow, diamondColor) = GetNestStyle(nest.Type, palette);
            float alpha = nest.TeardownTimer > 0 ? 0.45f : 0.9f;

            _formationMembers.Clear();
            foreach (var id in nest.MemberIds)
            {
                var block = _gameState.GetBlock(id);
                if (block == null) continue;
                _formationMembers.Add((id, block.Pos));
                var blockRect = GetFormationBlockRect(block.Pos);
                DrawFormationBlock(blockRect, outlineColor with { A = alpha }, outlineGlow with { A = alpha * 0.35f }, diamondColor with { A = alpha });

                if (nest.TeardownTimer > 0)
                    DrawTeardownOverlay(block, nest.TeardownTimer);
            }

            DrawFormationJoiners(_formationMembers, outlineColor, alpha);
        }

        foreach (var tower in _gameState!.Towers)
        {
            var centerBlock = _gameState.GetBlock(tower.CenterId);
            if (centerBlock == null) continue;
            if (_localVisibility != null && !_localVisibility.IsVisible(centerBlock.Pos)) continue;

            var palette = _config.GetPalette(tower.PlayerId);
            var (outlineColor, outlineGlow, diamondColor) = GetTowerStyle(tower.Type, palette);
            float alpha = tower.TeardownTimer > 0 ? 0.45f : 0.9f;

            _formationMembers.Clear();
            _formationMembers.Add((tower.CenterId, centerBlock.Pos));
            var blockRect = GetFormationBlockRect(centerBlock.Pos);
            DrawFormationBlock(blockRect, outlineColor with { A = alpha }, outlineGlow with { A = alpha * 0.35f }, diamondColor with { A = alpha });

            if (tower.TeardownTimer > 0)
                DrawTeardownOverlay(centerBlock, tower.TeardownTimer);

            foreach (var builderId in tower.BuilderDirections.Keys)
            {
                var block = _gameState.GetBlock(builderId);
                if (block == null) continue;
                _formationMembers.Add((builderId, block.Pos));
                var blockRect2 = GetFormationBlockRect(block.Pos);
                DrawFormationBlock(blockRect2, outlineColor with { A = alpha }, outlineGlow with { A = alpha * 0.35f }, diamondColor with { A = alpha });

                if (tower.TeardownTimer > 0)
                    DrawTeardownOverlay(block, tower.TeardownTimer);
            }

            DrawFormationJoiners(_formationMembers, outlineColor, alpha);
        }
    }

    /// <summary>
    /// Draws darker joiner rectangles between adjacent formation blocks.
    /// </summary>
    private void DrawFormationJoiners(List<(int Id, GridPos Pos)> members, Color outlineColor, float alpha)
    {
        if (members.Count < 2) return;

        _formationPosSet.Clear();
        foreach (var (_, pos) in members)
            _formationPosSet.Add(pos);

        var joinerColor = outlineColor.Darkened(0.2f) with { A = alpha };

        foreach (var (_, pos) in members)
        {
            var rectA = GetFormationBlockRect(pos);

            // Check right neighbor
            var right = new GridPos(pos.X + 1, pos.Y);
            if (_formationPosSet.Contains(right))
            {
                var rectB = GetFormationBlockRect(right);
                float gapW = rectB.Position.X - rectA.End.X;
                float halfH = rectA.Size.Y * 0.75f;
                DrawRect(new Rect2(rectA.End.X, rectA.Position.Y + halfH * 0.16f, gapW, halfH), joinerColor);
            }

            // Check bottom neighbor
            var down = new GridPos(pos.X, pos.Y + 1);
            if (_formationPosSet.Contains(down))
            {
                var rectB = GetFormationBlockRect(down);
                float gapH = rectB.Position.Y - rectA.End.Y;
                float halfW = rectA.Size.X * 0.75f;
                DrawRect(new Rect2(rectA.Position.X + halfW * 0.16f, rectA.End.Y, halfW, gapH), joinerColor);
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
        var outlineWidth = 2.2f;
        var outlineRect = blockRect.Grow(-outlineWidth/2f);
        DrawRect(outlineRect, outlineColor, false, outlineWidth);

        // Small corner brackets outside the outline
        DrawOuterCornerTicks(blockRect, 3f, outlineColor, 1f);

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

    private static (Color Outline, Color OutlineGlow, Color Diamond) GetTowerStyle(TowerType type, PlayerPalette palette) =>
        type switch
        {
            TowerType.Stun => (palette.StunTowerOutline, palette.StunTowerOutlineGlow, palette.StunTowerDiamond),
            TowerType.Soldier => (palette.SoldierTowerOutline, palette.SoldierTowerOutlineGlow, palette.SoldierTowerDiamond),
            _ => (palette.StunTowerOutline, palette.StunTowerOutlineGlow, palette.StunTowerDiamond),
        };

    private void DrawNestProgress()
    {
        foreach (var nest in _gameState!.Nests)
        {
            if (nest.IsPaused) continue;
            if (nest.SpawnProgress <= 0) continue;
            if (_localVisibility != null && !_localVisibility.IsVisible(nest.Center)) continue;

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