using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

public static class NuggetSystem
{
    public static void Tick(GameState state)
    {
        TickMining(state);
        TickConsumption(state);
        TickCapture(state);
        TickAutoRally(state);
    }

    private static void TickMining(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: false }) continue;
            if (block.PlayerId == -1) continue; // No one is mining

            int minerCount = 0;
            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = state.GetBlockAt(block.Pos + offset);
                if (neighbor != null
                    && neighbor.Type == BlockType.Builder
                    && neighbor.MiningTargetId == block.Id
                    && neighbor.PlayerId == block.PlayerId)
                {
                    minerCount++;
                }
            }

            if (minerCount == 0) continue;

            block.NuggetState.MiningProgress += minerCount;

            if (block.NuggetState.MiningProgress >= Constants.NuggetMiningTicks)
            {
                block.NuggetState.IsMined = true;
                block.NuggetState.MiningProgress = Constants.NuggetMiningTicks;

                foreach (var b in state.Blocks)
                {
                    if (b.MiningTargetId == block.Id)
                        b.MiningTargetId = null;
                }

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.NuggetFreed, block.Pos, block.PlayerId, BlockId: block.Id));

                SetAutoRallyTarget(state, block);
            }
        }
    }

    private static void TickConsumption(GameState state)
    {
        var toRemove = new List<int>();

        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: true }) continue;
            if (toRemove.Contains(block.Id)) continue;

            if (TryConsumeNestRefine(state, block))
            {
                toRemove.Add(block.Id);
                continue;
            }
        }

        foreach (var id in toRemove)
        {
            var block = state.GetBlock(id);
            if (block != null) state.RemoveBlock(block);
        }
    }

    private static bool TryConsumeNestRefine(GameState state, Block nugget)
    {
        foreach (var nest in state.Nests)
        {
            if (nest.PlayerId != nugget.PlayerId) continue;

            int chebyshev = Math.Max(
                Math.Abs(nugget.Pos.X - nest.Center.X),
                Math.Abs(nugget.Pos.Y - nest.Center.Y));

            if (chebyshev > Constants.NuggetRefineRadius) continue;

            var ground = state.Grid[nest.Center].Ground;
            var spawnType = nest.GetSpawnBlockType(ground);
            int bonus = Constants.Config.GetNuggetSpawnBonus(spawnType);

            if (bonus > 0)
            {
                int spawnTicks = nest.GetSpawnTicks(ground);
                nest.SpawnProgress += spawnTicks * bonus / 100;
            }

            if (Constants.Config.GetNuggetRequired(spawnType))
                nest.NuggetLoaded = true;

            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.NuggetRefineConsumed, nugget.Pos, nugget.PlayerId, BlockId: nugget.Id));

            return true;
        }

        return false;
    }

    private static void TickCapture(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: true }) continue;

            bool hasEnemyBuilder = false;
            int enemyPlayerId = -1;
            bool hasFriendlyBuilder = false;

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighbor = state.GetBlockAt(block.Pos + offset);
                if (neighbor == null || neighbor.Type != BlockType.Builder) continue;

                if (state.AreEnemies(neighbor.PlayerId, block.PlayerId))
                {
                    hasEnemyBuilder = true;
                    enemyPlayerId = neighbor.PlayerId;
                }
                else
                {
                    hasFriendlyBuilder = true;
                }
            }

            if (hasEnemyBuilder && !hasFriendlyBuilder)
            {
                block.PlayerId = enemyPlayerId;
                block.MoveTarget = null;
                block.NuggetState.HealTargetId = null;
                block.NuggetState.FortifyTargetPos = null;

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.NuggetCaptured, block.Pos, enemyPlayerId, BlockId: block.Id));

                SetAutoRallyTarget(state, block);
            }
        }
    }

    private static void TickAutoRally(GameState state)
    {
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Nugget) continue;
            if (block.NuggetState is not { IsMined: true }) continue;
            if (block.MoveTarget.HasValue) continue;
            if (block.NuggetState.HealTargetId.HasValue) continue;
            if (block.NuggetState.FortifyTargetPos.HasValue) continue;

            SetAutoRallyTarget(state, block);
        }
    }

    private static void SetAutoRallyTarget(GameState state, Block nugget)
    {
        GridPos? nearest = null;
        int bestDist = int.MaxValue;
        int bestNestId = int.MaxValue;

        foreach (var nest in state.Nests)
        {
            if (nest.PlayerId != nugget.PlayerId) continue;
            int dist = nugget.Pos.ChebyshevDistance(nest.Center);
            if (dist < bestDist || (dist == bestDist && nest.Id < bestNestId))
            {
                bestDist = dist;
                bestNestId = nest.Id;
                nearest = nest.Center;
            }
        }

        if (nearest.HasValue)
            nugget.MoveTarget = nearest.Value;
    }
}
