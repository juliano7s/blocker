namespace Blocker.Relay;

public enum RoomLifecycle { Lobby, Playing, Ended }

public sealed class Room
{
    public required string Code { get; init; }
    public required Guid HostId { get; init; }
    public RoomLifecycle Lifecycle { get; set; } = RoomLifecycle.Lobby;
    public ushort SimulationVersion { get; set; }
    public byte[] MapBlob { get; set; } = Array.Empty<byte>();
    public string MapName { get; set; } = "";
    public int SlotCount { get; set; }

    // slotId → slot info
    public Dictionary<byte, SlotInfo> Slots { get; } = new();
    public int HighestSeenTick { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public sealed record SlotInfo(Guid? OwnerId, string DisplayName, byte ColorIndex, bool IsOpen, bool IsClosed);
