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

            // SpawnDisabled — sorted for determinism (HashSet iteration order is not guaranteed)
            var disabledSorted = p.SpawnDisabled.OrderBy(t => t).ToArray();
            MixI32(ref h, disabledSorted.Length);
            foreach (var t in disabledSorted)
                MixI32(ref h, (int)t);
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

        // Nests — sorted by Id
        var nestsSorted = state.Nests.OrderBy(n => n.Id).ToArray();
        foreach (var n in nestsSorted)
        {
            MixI32(ref h, n.Id);
            MixI32(ref h, n.PlayerId);
            MixI32(ref h, (int)n.Type);
            MixI32(ref h, n.Center.X);
            MixI32(ref h, n.Center.Y);
            MixI32(ref h, n.SpawnProgress);
            MixI32(ref h, n.TeardownTimer);
            MixI32(ref h, n.IsPaused ? 1 : 0);
        }

        // Towers — sorted by Id
        var towersSorted = state.Towers.OrderBy(t => t.Id).ToArray();
        foreach (var t in towersSorted)
        {
            MixI32(ref h, t.Id);
            MixI32(ref h, t.PlayerId);
            MixI32(ref h, (int)t.Type);
            MixI32(ref h, t.CenterId);
            MixI32(ref h, t.FireTimer);
            MixI32(ref h, t.SweepIndex);
            MixI32(ref h, t.IsFiring ? 1 : 0);
            MixI32(ref h, t.TeardownTimer);
            
            // BuilderDirections — sort by Id
            var armsSorted = t.BuilderDirections.Keys.OrderBy(k => k).ToArray();
            MixI32(ref h, armsSorted.Length);
            foreach (var armId in armsSorted)
            {
                MixI32(ref h, armId);
                MixI32(ref h, (int)t.BuilderDirections[armId]);
            }
        }

        // Formations — sorted by Id
        var formationsSorted = state.Formations.OrderBy(f => f.Id).ToArray();
        foreach (var f in formationsSorted)
        {
            MixI32(ref h, f.Id);
            MixI32(ref h, f.PlayerId);
            MixI32(ref h, (int)f.Type);
            MixI32(ref h, f.TeardownTimer);
        }

        // Rays — sorted by Id
        var raysSorted = state.Rays.OrderBy(r => r.Id).ToArray();
        foreach (var r in raysSorted)
        {
            MixI32(ref h, r.Id);
            MixI32(ref h, r.PlayerId);
            MixI32(ref h, (int)r.Type);
            MixI32(ref h, r.HeadPos.X);
            MixI32(ref h, r.HeadPos.Y);
            MixI32(ref h, r.Distance);
            MixI32(ref h, r.TickCounter);
            MixI32(ref h, r.IsExpired ? 1 : 0);
        }

        // PushWaves — sorted by Id
        var wavesSorted = state.PushWaves.OrderBy(w => w.Id).ToArray();
        foreach (var w in wavesSorted)
        {
            MixI32(ref h, w.Id);
            MixI32(ref h, w.PlayerId);
            MixI32(ref h, w.HeadPos.X);
            MixI32(ref h, w.HeadPos.Y);
            MixI32(ref h, w.Distance);
            MixI32(ref h, w.TickCounter);
            MixI32(ref h, w.IsExpired ? 1 : 0);
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
