using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Game.Editor;

public record CellSnapshot(int X, int Y, GroundType Ground, TerrainType Terrain, BlockType? UnitType, int? UnitSlot);

public class EditorAction
{
    public List<CellSnapshot> Before { get; } = [];
    public List<CellSnapshot> After { get; } = [];
}
