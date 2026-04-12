using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Maps;

public record MapData(
    string Name,
    int Width,
    int Height,
    int SlotCount,
    List<GroundEntry> Ground,
    List<TerrainEntry> Terrain,
    List<UnitEntry> Units
);

public record GroundEntry(int X, int Y, GroundType Type);
public record TerrainEntry(int X, int Y, TerrainType Type);
public record UnitEntry(int X, int Y, BlockType Type, int SlotId, bool Rooted = false);
