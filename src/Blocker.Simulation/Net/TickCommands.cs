using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// One player's commands for a specific tick. Empty list is valid (idle player).
/// </summary>
public record TickCommands(int PlayerId, int Tick, IReadOnlyList<Command> Commands)
{
    public static TickCommands Empty(int playerId, int tick) =>
        new(playerId, tick, Array.Empty<Command>());
}
