using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Maps;

public static class MapLoader
{
    /// <summary>
    /// Load a map from the two-layer text format.
    /// Layer 1: ground types. Layer 2 (after "---" separator): unit placement.
    /// If no separator exists, the single layer encodes both ground and units.
    /// </summary>
    public static GameState Load(string mapText)
    {
        var lines = mapText.Split('\n').Select(l => l.TrimEnd('\r')).ToList();

        // Find separator
        int separatorIndex = lines.IndexOf("---");
        List<string> groundLines;
        List<string> unitLines;

        if (separatorIndex >= 0)
        {
            groundLines = lines.Take(separatorIndex).Where(l => l.Length > 0).ToList();
            unitLines = lines.Skip(separatorIndex + 1).Where(l => l.Length > 0).ToList();
        }
        else
        {
            groundLines = lines.Where(l => l.Length > 0).ToList();
            unitLines = groundLines; // Same layer for both
        }

        int height = groundLines.Count;
        int width = groundLines.Max(l => l.Length);
        var grid = new Grid(width, height);
        var state = new GameState(grid);

        // Parse ground layer
        for (int y = 0; y < height; y++)
        {
            var line = groundLines[y];
            for (int x = 0; x < width; x++)
            {
                char c = x < line.Length ? line[x] : '.';
                grid[x, y].Ground = ParseGround(c);
                grid[x, y].Terrain = ParseTerrain(c);
            }
        }

        // Parse unit layer (if two-layer format)
        if (separatorIndex >= 0)
        {
            for (int y = 0; y < Math.Min(unitLines.Count, height); y++)
            {
                var line = unitLines[y];
                for (int x = 0; x < Math.Min(line.Length, width); x++)
                {
                    char c = line[x];
                    if (c == '.' || c == ' ') continue;
                    TryParseBlock(c, state, new GridPos(x, y));
                }
            }
        }
        else
        {
            // Single-layer: parse blocks from the same characters (only player block chars)
            for (int y = 0; y < height; y++)
            {
                var line = groundLines[y];
                for (int x = 0; x < width; x++)
                {
                    char c = x < line.Length ? line[x] : '.';
                    TryParseBlock(c, state, new GridPos(x, y));
                }
            }
        }

        // Ensure players exist for any blocks placed
        var playerIds = state.Blocks.Select(b => b.PlayerId).Distinct().OrderBy(id => id);
        foreach (int pid in playerIds)
        {
            if (state.Players.All(p => p.Id != pid))
                state.Players.Add(new Player { Id = pid, TeamId = pid });
        }

        return state;
    }

    public static GameState LoadFromFile(string path) => Load(File.ReadAllText(path));

    private static GroundType ParseGround(char c) => c switch
    {
        'f' => GroundType.Boot,
        'o' => GroundType.Overload,
        'p' => GroundType.Proto,
        _ => GroundType.Normal
    };

    private static TerrainType ParseTerrain(char c) => c switch
    {
        '#' => TerrainType.Terrain,
        '~' => TerrainType.BreakableWall,
        '=' => TerrainType.FragileWall,
        _ => TerrainType.None
    };

    private static void TryParseBlock(char c, GameState state, GridPos pos)
    {
        // Lowercase = player 0, uppercase = player 1
        var (type, playerId) = c switch
        {
            'b' => (BlockType.Builder, 0),
            'B' => (BlockType.Builder, 1),
            'w' => (BlockType.Wall, 0),
            'W' => (BlockType.Wall, 1),
            's' => (BlockType.Soldier, 0),
            'S' => (BlockType.Soldier, 1),
            'n' => (BlockType.Stunner, 0),
            'N' => (BlockType.Stunner, 1),
            _ => ((BlockType?)null, -1)
        };

        if (type.HasValue)
        {
            state.AddBlock(type.Value, playerId, pos);
        }
    }
}
