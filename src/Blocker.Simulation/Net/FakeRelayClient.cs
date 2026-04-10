using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// In-process echo relay for unit tests. Holds a reference to all peers and
/// fans messages out to everyone except the sender. No threads, no sockets.
/// Messages are delivered synchronously when Send* is called.
/// </summary>
public class FakeRelayClient : IRelayClient
{
    public int LocalPlayerId { get; }
    private readonly List<FakeRelayClient> _peers = new();

    public event Action<int, int, IReadOnlyList<Command>>? CommandsReceived;
    public event Action<int, int, uint>? HashReceived;
    public event Action<int, int, LeaveReason>? PlayerLeft;
    public event Action<int>? SurrenderReceived;

    public FakeRelayClient(int localPlayerId) { LocalPlayerId = localPlayerId; }

    /// <summary>Wire two or more clients into a single relay mesh.</summary>
    public static void Connect(params FakeRelayClient[] clients)
    {
        foreach (var a in clients)
            foreach (var b in clients)
                if (!ReferenceEquals(a, b))
                    a._peers.Add(b);
    }

    public void SendCommands(int tick, IReadOnlyList<Command> commands)
    {
        foreach (var peer in _peers)
            peer.CommandsReceived?.Invoke(LocalPlayerId, tick, commands);
    }

    public void SendHash(int tick, uint hash)
    {
        foreach (var peer in _peers)
            peer.HashReceived?.Invoke(LocalPlayerId, tick, hash);
    }

    public void SendDesyncReport(int tick, GameStateSnapshot snapshot) { /* noop in tests */ }
    public void SendSurrender()
    {
        foreach (var peer in _peers)
            peer.SurrenderReceived?.Invoke(LocalPlayerId);
    }

    /// <summary>Simulate a disconnect for tests. Raises PlayerLeft on all peers.</summary>
    public void SimulateDisconnect(int effectiveTick)
    {
        foreach (var peer in _peers)
            peer.PlayerLeft?.Invoke(LocalPlayerId, effectiveTick, LeaveReason.Disconnected);
    }
}
