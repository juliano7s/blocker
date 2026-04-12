using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// Transport abstraction used by LockstepCoordinator. All events fire on the
/// main (game) thread — implementations are responsible for marshalling.
/// </summary>
public interface IRelayClient
{
    void SendCommands(int tick, IReadOnlyList<Command> commands);
    void SendHash(int tick, uint hash);
    void SendDesyncReport(int tick, GameStateSnapshot snapshot);

    event Action<int /*playerId*/, int /*tick*/, IReadOnlyList<Command>>? CommandsReceived;
    event Action<int /*playerId*/, int /*tick*/, uint>? HashReceived;
    event Action<int /*playerId*/, int /*effectiveTick*/, LeaveReason>? PlayerLeft;
}
