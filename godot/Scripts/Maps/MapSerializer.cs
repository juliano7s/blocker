using System.Text.Json;
using System.Text.Json.Serialization;
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Maps;

namespace Blocker.Game.Maps;

public static class MapSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public static string Serialize(MapData data)
    {
        var json = new JsonMapFile
        {
            Meta = new JsonMeta
            {
                Name = data.Name,
                Version = 1,
                Width = data.Width,
                Height = data.Height,
                Slots = data.SlotCount
            },
            Ground = data.Ground.Select(g => new JsonGroundEntry { X = g.X, Y = g.Y, Type = g.Type }).ToList(),
            Terrain = data.Terrain.Select(t => new JsonTerrainEntry { X = t.X, Y = t.Y, Type = t.Type }).ToList(),
            Units = data.Units.Select(u => new JsonUnitEntry { X = u.X, Y = u.Y, Type = u.Type, Slot = u.SlotId }).ToList()
        };
        return JsonSerializer.Serialize(json, Options);
    }

    public static MapData Deserialize(string json)
    {
        var file = JsonSerializer.Deserialize<JsonMapFile>(json, Options)
            ?? throw new JsonException("Failed to deserialize map file");

        return new MapData(
            Name: file.Meta.Name,
            Width: file.Meta.Width,
            Height: file.Meta.Height,
            SlotCount: file.Meta.Slots,
            Ground: file.Ground.Select(g => new GroundEntry(g.X, g.Y, g.Type)).ToList(),
            Terrain: file.Terrain.Select(t => new TerrainEntry(t.X, t.Y, t.Type)).ToList(),
            Units: file.Units.Select(u => new UnitEntry(u.X, u.Y, u.Type, u.Slot)).ToList()
        );
    }

    // Internal JSON DTOs
    private class JsonMapFile
    {
        public JsonMeta Meta { get; set; } = new();
        public List<JsonGroundEntry> Ground { get; set; } = [];
        public List<JsonTerrainEntry> Terrain { get; set; } = [];
        public List<JsonUnitEntry> Units { get; set; } = [];
    }

    private class JsonMeta
    {
        public string Name { get; set; } = "";
        public int Version { get; set; } = 1;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Slots { get; set; }
    }

    private class JsonGroundEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public GroundType Type { get; set; }
    }

    private class JsonTerrainEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public TerrainType Type { get; set; }
    }

    private class JsonUnitEntry
    {
        public int X { get; set; }
        public int Y { get; set; }
        public BlockType Type { get; set; }
        public int Slot { get; set; }
    }
}
