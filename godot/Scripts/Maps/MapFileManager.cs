using Blocker.Simulation.Maps;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace Blocker.Game.Maps;

/// <summary>
/// Manages loading/saving map JSON files from the maps directory.
/// </summary>
public static class MapFileManager
{
    // user:// = per-user writable storage (maps the player creates in the editor).
    // res:// = bundled with the game; ships to every install. Joiners must be able to
    // load the host's map by name, so default/shared maps live here.
    public const string MapsDirectory = "user://Maps";
    public const string BundledMapsDirectory = "res://Maps";

    public static void EnsureDirectoryExists()
    {
        if (!DirAccess.DirExistsAbsolute(MapsDirectory))
            DirAccess.MakeDirRecursiveAbsolute(MapsDirectory);
    }

    public static List<string> ListMaps()
    {
        // Union of bundled (res://) and user-created (user://) maps. Bundled wins on
        // name collision because that's what other peers will load too.
        var seen = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
        var maps = new List<string>();

        AppendDir(BundledMapsDirectory, maps, seen);
        AppendDir(MapsDirectory, maps, seen);

        maps.Sort(System.StringComparer.OrdinalIgnoreCase);
        return maps;
    }

    private static void AppendDir(string dirPath, List<string> maps, HashSet<string> seen)
    {
        var dir = DirAccess.Open(dirPath);
        if (dir == null) return;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() &&
                fileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase) &&
                seen.Add(fileName))
            {
                maps.Add(fileName);
            }
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
    }

    public static void Save(MapData data, string fileName)
    {
        EnsureDirectoryExists();
        var json = MapSerializer.Serialize(data);
        var path = MapsDirectory.PathJoin(fileName);
        var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Write);
        if (file == null)
        {
            GD.PrintErr($"Failed to save map to {path}: {GodotFileAccess.GetOpenError()}");
            return;
        }
        file.StoreString(json);
        file.Close();
        GD.Print($"Map saved to {path}");
    }

    public static MapData? Load(string fileName)
    {
        // Defensive: callers historically passed both "Lanes" (display name from
        // RoomState.MapName before we fixed the host) and "Lanes.json". Tolerate both.
        if (!fileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
            fileName += ".json";

        // user:// first (player-authored), res:// fallback (bundled with game).
        var path = MapsDirectory.PathJoin(fileName);
        if (!GodotFileAccess.FileExists(path))
            path = BundledMapsDirectory.PathJoin(fileName);

        if (!GodotFileAccess.FileExists(path))
        {
            GD.PrintErr($"Map file not found in user:// or res://: {fileName}");
            return null;
        }

        var file = GodotFileAccess.Open(path, GodotFileAccess.ModeFlags.Read);
        if (file == null)
        {
            GD.PrintErr($"Failed to open map: {path}: {GodotFileAccess.GetOpenError()}");
            return null;
        }
        var json = file.GetAsText();
        file.Close();
        return MapSerializer.Deserialize(json);
    }
}
