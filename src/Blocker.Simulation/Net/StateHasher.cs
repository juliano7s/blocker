using Blocker.Simulation.Core;

namespace Blocker.Simulation.Net;

/// <summary>
/// FNV-1a over a canonicalized integer view of GameState.
/// Canonicalization: players sorted by Id, blocks sorted by Id.
/// Floats are never hashed (sim is integer-only by contract).
/// </summary>
public static class StateHasher
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public static uint Hash(GameState state)
    {
        uint h = FnvOffsetBasis;
        MixI32(ref h, state.TickNumber);
        MixI32(ref h, state.Players.Count);
        MixI32(ref h, state.Blocks.Count);

        // Players — sorted by Id
        var playersSorted = state.Players.OrderBy(p => p.Id).ToArray();
        foreach (var p in playersSorted)
        {
            MixI32(ref h, p.Id);
            MixI32(ref h, p.TeamId);
            MixI32(ref h, p.IsEliminated ? 1 : 0);
        }

        // Blocks — sorted by Id
        var blocksSorted = state.Blocks.OrderBy(b => b.Id).ToArray();
        foreach (var b in blocksSorted)
        {
            MixI32(ref h, b.Id);
            MixI32(ref h, b.PlayerId);
            MixI32(ref h, (int)b.Type);
            MixI32(ref h, (int)b.State);
            MixI32(ref h, b.Pos.X);
            MixI32(ref h, b.Pos.Y);
            MixI32(ref h, b.Hp);
            MixI32(ref h, b.Cooldown);
            MixI32(ref h, b.RootProgress);
            MixI32(ref h, b.MoveTarget.HasValue ? 1 : 0);
            if (b.MoveTarget.HasValue)
            {
                MixI32(ref h, b.MoveTarget.Value.X);
                MixI32(ref h, b.MoveTarget.Value.Y);
            }
        }
        return h;
    }

    private static void MixI32(ref uint h, int value)
    {
        uint u = unchecked((uint)value);
        h ^= (byte)(u & 0xFF);        h *= FnvPrime;
        h ^= (byte)((u >> 8) & 0xFF); h *= FnvPrime;
        h ^= (byte)((u >> 16) & 0xFF); h *= FnvPrime;
        h ^= (byte)((u >> 24) & 0xFF); h *= FnvPrime;
    }
}
