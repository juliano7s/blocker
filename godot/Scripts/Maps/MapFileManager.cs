using Blocker.Simulation.Maps;
using Godot;
using GodotFileAccess = Godot.FileAccess;

namespace Blocker.Game.Maps;

/// <summary>
/// Manages loading/saving map JSON files from the maps directory.
/// </summary>
public static class MapFileManager
{
    public const string MapsDirectory = "user://Maps";

    public static void EnsureDirectoryExists()
    {
        if (!DirAccess.DirExistsAbsolute(MapsDirectory))
            DirAccess.MakeDirRecursiveAbsolute(MapsDirectory);
    }

    public static List<string> ListMaps()
    {
        var maps = new List<string>();
        var dir = DirAccess.Open(MapsDirectory);
        if (dir == null) return maps;

        dir.ListDirBegin();
        var fileName = dir.GetNext();
        while (!string.IsNullOrEmpty(fileName))
        {
            if (!dir.CurrentIsDir() && fileName.EndsWith(".json", System.StringComparison.OrdinalIgnoreCase))
                maps.Add(fileName);
            fileName = dir.GetNext();
        }
        dir.ListDirEnd();
        maps.Sort();
        return maps;
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
        var path = MapsDirectory.PathJoin(fileName);
        if (!GodotFileAccess.FileExists(path))
        {
            GD.PrintErr($"Map file not found: {path}");
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
