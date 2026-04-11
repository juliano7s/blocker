using System.Collections.Concurrent;

namespace Blocker.Relay;

public sealed class RoomRegistry
{
    // Code → Room (thread-safe; rooms are mutated from per-connection tasks).
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _roomsPerIp = new(StringComparer.Ordinal);
    private readonly Random _rng = new();
    // No 0/O/1/I/L — avoids confusion in user-typed codes.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public Room? Get(string code) => _rooms.TryGetValue(code, out var r) ? r : null;
    public IEnumerable<Room> All() => _rooms.Values;

    public Room? TryCreate(Guid hostId, string ip, int maxRoomsPerIp,
                           ushort simVersion, byte[] mapBlob, string mapName, int slotCount)
    {
        int count = _roomsPerIp.GetOrAdd(ip, 0);
        if (count >= maxRoomsPerIp) return null;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            string code = GenerateCode();
            var room = new Room
            {
                Code = code,
                HostId = hostId,
                SimulationVersion = simVersion,
                MapBlob = mapBlob,
                MapName = mapName,
                SlotCount = slotCount
            };
            if (_rooms.TryAdd(code, room))
            {
                _roomsPerIp.AddOrUpdate(ip, 1, (_, v) => v + 1);
                return room;
            }
        }
        return null;
    }

    public void Remove(Room room, string hostIp)
    {
        if (_rooms.TryRemove(room.Code, out _))
            _roomsPerIp.AddOrUpdate(hostIp, 0, (_, v) => Math.Max(0, v - 1));
    }

    private string GenerateCode()
    {
        Span<char> c = stackalloc char[4];
        lock (_rng)
            for (int i = 0; i < 4; i++) c[i] = Alphabet[_rng.Next(Alphabet.Length)];
        return new string(c);
    }
}
