using Blocker.Simulation.Maps;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game.UI;

public partial class MapMiniature : Control
{
    private MapData? _mapData;
    private float _cellSize;

    private static readonly Color BgColor = new(0.06f, 0.06f, 0.06f);
    private static readonly Color GridLineColor = new(0.267f, 0.667f, 1f, 0.08f);
    private static readonly Color NormalGroundColor = new(0.12f, 0.12f, 0.12f);
    private static readonly Color BootColor = new(0.18f, 0.32f, 0.11f, 0.75f);
    private static readonly Color OverloadColor = new(0.28f, 0.16f, 0.39f, 0.67f);
    private static readonly Color ProtoColor = new(0.17f, 0.17f, 0.14f, 0.81f);
    private static readonly Color NuggetColor = new(0.7f, 0.7f, 0.65f);
    private static readonly Color WallColor = new(0.5f, 0.5f, 0.5f);
    private static readonly Color BreakableColor = new(0.4f, 0.35f, 0.25f);
    private static readonly Color FragileColor = new(0.3f, 0.25f, 0.2f);
    private static readonly Color[] SlotColors =
    {
        new(0.25f, 0.55f, 1.0f),
        new(0.95f, 0.25f, 0.2f),
        new(0.95f, 0.85f, 0.2f),
        new(0.2f, 0.85f, 0.35f),
        new(0.9f, 0.45f, 0.1f),
        new(0.65f, 0.25f, 0.9f),
    };

    public void SetMap(MapData? map)
    {
        _mapData = map;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_mapData == null)
        {
            DrawRect(new Rect2(Vector2.Zero, Size), BgColor);
            DrawRect(new Rect2(Vector2.Zero, Size), new Color(0.267f, 0.667f, 1f, 0.12f), false, 1f);
            return;
        }

        var size = Size;
        float scaleX = size.X / _mapData.Width;
        float scaleY = size.Y / _mapData.Height;
        _cellSize = Mathf.Min(scaleX, scaleY);

        float totalW = _mapData.Width * _cellSize;
        float totalH = _mapData.Height * _cellSize;
        float ox = (size.X - totalW) * 0.5f;
        float oy = (size.Y - totalH) * 0.5f;

        // Background
        DrawRect(new Rect2(ox, oy, totalW, totalH), BgColor);

        // Ground tiles
        foreach (var g in _mapData.Ground)
        {
            var color = g.Type switch
            {
                GroundType.Boot => BootColor,
                GroundType.Overload => OverloadColor,
                GroundType.Proto => ProtoColor,
                _ => NormalGroundColor
            };
            DrawRect(new Rect2(ox + g.X * _cellSize, oy + g.Y * _cellSize, _cellSize, _cellSize), color);
        }

        // Terrain (walls)
        foreach (var t in _mapData.Terrain)
        {
            if (t.Type == TerrainType.None) continue;
            var color = t.Type switch
            {
                TerrainType.BreakableWall => BreakableColor,
                TerrainType.FragileWall => FragileColor,
                _ => WallColor
            };
            DrawRect(new Rect2(ox + t.X * _cellSize, oy + t.Y * _cellSize, _cellSize, _cellSize), color);
        }

        // Grid lines (only if cells are large enough to see)
        if (_cellSize >= 3f)
        {
            for (int x = 0; x <= _mapData.Width; x++)
                DrawLine(new Vector2(ox + x * _cellSize, oy), new Vector2(ox + x * _cellSize, oy + totalH), GridLineColor);
            for (int y = 0; y <= _mapData.Height; y++)
                DrawLine(new Vector2(ox, oy + y * _cellSize), new Vector2(ox + totalW, oy + y * _cellSize), GridLineColor);
        }

        // Spawn positions (unit entries) — colored by slot
        foreach (var u in _mapData.Units)
        {
            float cx = ox + u.X * _cellSize + _cellSize * 0.5f;
            float cy = oy + u.Y * _cellSize + _cellSize * 0.5f;
            float r = Mathf.Max(_cellSize * 0.3f, 1.5f);
            var color = u.Type == BlockType.Nugget
                ? NuggetColor
                : u.SlotId >= 0 && u.SlotId < SlotColors.Length
                    ? SlotColors[u.SlotId]
                    : SlotColors[0];
            DrawCircle(new Vector2(cx, cy), r, color);
        }

        // Border
        DrawRect(new Rect2(ox, oy, totalW, totalH), new Color(0.267f, 0.667f, 1f, 0.15f), false, 1f);
    }
}
