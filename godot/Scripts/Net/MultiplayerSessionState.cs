using Blocker.Simulation.Maps;

namespace Blocker.Game.Net;

/// <summary>
/// Carried from LobbyListScreen/GameLobbyScreen into GameManager via a static
/// slot on GameLaunchData (mirrors the existing single-player launch pattern).
/// </summary>
public sealed class MultiplayerSessionState
{
    public required RelayClient Relay { get; init; }
    public required int LocalPlayerId { get; init; }
    public required HashSet<int> ActivePlayerIds { get; init; }
    public required MapData Map { get; init; }
    public required List<SlotAssignment> Assignments { get; init; }
}
