using System.Net.WebSockets;

namespace Blocker.Relay;

public sealed class Connection
{
    public Guid Id { get; } = Guid.NewGuid();
    public required WebSocket Ws { get; init; }
    public required string RemoteIp { get; init; }
    public string ClientName { get; set; } = "";
    public byte ProtocolVersion { get; set; }
    public ushort SimulationVersion { get; set; }
    public Room? CurrentRoom { get; set; }
    public byte? AssignedPlayerId { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    // Token bucket state for rate limiting (Task 17).
    public double RateTokens { get; set; }
    public DateTime RateLastRefill { get; set; } = DateTime.UtcNow;
}
