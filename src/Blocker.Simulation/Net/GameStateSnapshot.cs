using Blocker.Simulation.Core;

namespace Blocker.Simulation.Net;

/// <summary>
/// Minimal diagnostic snapshot written to disk on desync. Enough to post-mortem
/// a diverged state. Not a replay format.
/// </summary>
public record GameStateSnapshot(
    int Tick,
    uint Hash,
    int PlayerCount,
    int BlockCount,
    IReadOnlyList<(int Id, int PlayerId, int X, int Y, int Hp, int Type)> Blocks);

public static class GameStateSnapshotExtensions
{
    public static GameStateSnapshot Snapshot(this GameState state)
    {
        var blocks = new List<(int, int, int, int, int, int)>(state.Blocks.Count);
        foreach (var b in state.Blocks)
            blocks.Add((b.Id, b.PlayerId, b.Pos.X, b.Pos.Y, b.Hp, (int)b.Type));
        return new GameStateSnapshot(
            state.TickNumber,
            StateHasher.Hash(state),
            state.Players.Count,
            state.Blocks.Count,
            blocks);
    }
}
