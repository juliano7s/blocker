using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Detects nest formations, manages spawn timers, and spawns units.
/// Game bible Sections 6.1–6.5.
/// </summary>
public static class NestSystem
{
    /// <summary>
    /// Full nest tick: validate existing nests, detect new ones, advance timers, spawn.
    /// Called during tick step 2 (formations) and step 14 (spawning).
    /// </summary>
    public static void DetectNests(GameState state)
    {
        // Remove nests whose members are no longer valid
        ValidateExistingNests(state);

        // Scan nest zones for new formations
        ScanForNewNests(state);

        // Check for Builder→Soldier auto-upgrades
        CheckAutoUpgrades(state);
    }

    public static void TickSpawning(GameState state)
    {
        var toRemove = new List<int>();

        foreach (var nest in state.Nests)
        {
            // Check if any member is stunned → pause
            nest.IsPaused = nest.MemberIds.Any(id =>
            {
                var b = state.GetBlock(id);
                return b != null && b.IsStunned;
            });

            if (nest.IsPaused) continue;
            if (nest.IsTearingDown) continue; // No spawning during teardown

            var ground = state.Grid[nest.Center].Ground;
            int spawnTicks = nest.GetSpawnTicks(ground);

            nest.SpawnProgress++;

            if (nest.SpawnProgress >= spawnTicks)
            {
                var player = state.Players.Find(p => p.Id == nest.PlayerId);
                var spawnType = nest.GetSpawnBlockType(ground);
                int popCost = spawnType switch
                {
                    BlockType.Builder => Constants.PopCostBuilder,
                    BlockType.Soldier => Constants.PopCostSoldier,
                    BlockType.Stunner => Constants.PopCostStunner,
                    BlockType.Warden => Constants.PopCostWarden,
                    BlockType.Jumper => Constants.PopCostJumper,
                    _ => 0
                };

                int currentPop = state.GetPopulation(nest.PlayerId);
                if (player != null && currentPop + popCost > player.MaxPopulation)
                {
                    // Pop capped — hold progress at max, retry next tick
                    nest.SpawnProgress = spawnTicks;
                    continue;
                }

                // Find spawn cell: center first, then BFS outward up to 3 cells
                var spawnPos = FindSpawnCell(state, nest.Center);
                if (!spawnPos.HasValue)
                {
                    // Congested — hold and retry
                    nest.SpawnProgress = spawnTicks;
                    continue;
                }

                // Spawn the unit (AddBlock handles HP for soldier/jumper)
                var spawned = state.AddBlock(spawnType, nest.PlayerId, spawnPos.Value);

                // Auto-move away from center if spawned at center
                if (spawnPos.Value == nest.Center)
                {
                    // Find a free adjacent cell to move to
                    foreach (var offset in GridPos.OrthogonalOffsets)
                    {
                        var moveTarget = nest.Center + offset;
                        if (state.Grid.InBounds(moveTarget) && !state.Grid[moveTarget].BlockId.HasValue
                            && state.Grid[moveTarget].IsPassable)
                        {
                            spawned.MoveTarget = moveTarget;
                            break;
                        }
                    }
                }

                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.BlockSpawned, spawnPos.Value, nest.PlayerId, BlockId: spawned.Id));

                nest.SpawnProgress = 0;
            }
        }
    }

    private static void ValidateExistingNests(GameState state)
    {
        var toRemove = new List<int>();

        foreach (var nest in state.Nests)
        {
            bool memberDead = false;
            bool memberUprooting = false;

            foreach (var memberId in nest.MemberIds)
            {
                var block = state.GetBlock(memberId);
                if (block == null || block.PlayerId != nest.PlayerId)
                {
                    memberDead = true;
                    break;
                }
                if (block.State == BlockState.Uprooting || block.State == BlockState.Mobile)
                {
                    memberUprooting = true;
                }
            }

            if (memberDead)
            {
                // Instant dissolution — member killed
                DissolveNest(state, nest, toRemove);
                continue;
            }

            if (memberUprooting && !nest.IsTearingDown)
            {
                // Start teardown timer (voluntary uproot)
                nest.TeardownTimer = Constants.TeardownTicks;
            }

            if (nest.IsTearingDown)
            {
                nest.TeardownTimer--;
                if (nest.TeardownTimer <= 0)
                {
                    DissolveNest(state, nest, toRemove);
                    continue;
                }

                // Check if uprooting member cancelled (re-rooted) — cancel teardown
                bool stillUprooting = nest.MemberIds.Any(id =>
                {
                    var b = state.GetBlock(id);
                    return b != null && (b.State == BlockState.Uprooting || b.State == BlockState.Mobile);
                });
                if (!stillUprooting)
                    nest.TeardownTimer = 0; // Cancel teardown
            }
        }

        state.Nests.RemoveAll(n => toRemove.Contains(n.Id));
    }

    private static void DissolveNest(GameState state, Nest nest, List<int> toRemove)
    {
        foreach (var memberId in nest.MemberIds)
        {
            var block = state.GetBlock(memberId);
            if (block != null && block.FormationId == nest.Id)
                block.FormationId = null;
        }
        toRemove.Add(nest.Id);
        state.VisualEvents.Add(new VisualEvent(
            VisualEventType.FormationDissolved, nest.Center, nest.PlayerId));
    }

    private static void ScanForNewNests(GameState state)
    {
        for (int y = 0; y < state.Grid.Height; y++)
        {
            for (int x = 0; x < state.Grid.Width; x++)
            {
                var cell = state.Grid[x, y];
                if (!cell.IsNestZone) continue;

                var center = new GridPos(x, y);

                // Skip if this cell is already a nest center
                if (state.Nests.Any(n => n.Center == center)) continue;

                // Try to detect nest patterns at this zone cell
                TryDetectNestAt(state, center);
            }
        }
    }

    private static void TryDetectNestAt(GameState state, GridPos center)
    {
        // Collect rooted blocks in orthogonal positions
        var orthoBlocks = new List<Block>();
        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var block = state.GetBlockAt(center + offset);
            if (block != null && block.IsFullyRooted && !block.IsInFormation)
                orthoBlocks.Add(block);
        }

        // Collect rooted blocks in diagonal positions
        var diagBlocks = new List<Block>();
        foreach (var offset in GridPos.DiagonalOffsets)
        {
            var block = state.GetBlockAt(center + offset);
            if (block != null && block.IsFullyRooted && !block.IsInFormation)
                diagBlocks.Add(block);
        }

        // Check for Stunner Nest: 3 Soldiers ortho + 2 Walls diag (same owner)
        if (TryFormCrossNest(state, center, orthoBlocks, diagBlocks, BlockType.Soldier, NestType.Stunner, 3))
            return;

        // Check for Soldier Nest: 3 Builders ortho + 2 Walls diag (same owner)
        if (TryFormCrossNest(state, center, orthoBlocks, diagBlocks, BlockType.Builder, NestType.Soldier, 3))
            return;

        // Check for Builder Nest: 3 Builders ortho (same owner), no wall requirement
        TryFormBuilderNest(state, center, orthoBlocks);
    }

    private static bool TryFormCrossNest(GameState state, GridPos center,
        List<Block> orthoBlocks, List<Block> diagBlocks,
        BlockType mainType, NestType nestType, int mainNeeded)
    {
        // Group orthogonal main-type blocks by player
        var playerGroups = orthoBlocks
            .Where(b => b.Type == mainType)
            .GroupBy(b => b.PlayerId);

        foreach (var group in playerGroups)
        {
            var mainBlocks = group.ToList();
            if (mainBlocks.Count < mainNeeded) continue;

            int playerId = group.Key;

            // Find 2 walls in diagonal positions owned by same player
            var walls = diagBlocks
                .Where(b => b.Type == BlockType.Wall && b.PlayerId == playerId)
                .Take(2)
                .ToList();

            if (walls.Count < 2) continue;

            // Form the nest
            var members = mainBlocks.Take(mainNeeded).Concat(walls).ToList();
            var nest = new Nest
            {
                Type = nestType,
                PlayerId = playerId,
                Center = center,
                MemberIds = { Capacity = members.Count }
            };
            nest.MemberIds.AddRange(members.Select(b => b.Id));

            foreach (var member in members)
                member.FormationId = nest.Id;

            state.Nests.Add(nest);
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.FormationFormed, center, playerId));
            return true; // One nest per center
        }

        return false;
    }

    private static void TryFormBuilderNest(GameState state, GridPos center, List<Block> orthoBlocks)
    {
        // 3 Builders in orthogonal positions, same owner
        var playerGroups = orthoBlocks
            .Where(b => b.Type == BlockType.Builder)
            .GroupBy(b => b.PlayerId);

        foreach (var group in playerGroups)
        {
            if (group.Count() < 3) continue;

            int playerId = group.Key;
            var members = group.Take(3).ToList();

            var nest = new Nest
            {
                Type = NestType.Builder,
                PlayerId = playerId,
                Center = center,
                MemberIds = { Capacity = members.Count }
            };
            nest.MemberIds.AddRange(members.Select(b => b.Id));

            foreach (var member in members)
                member.FormationId = nest.Id;

            state.Nests.Add(nest);
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.FormationFormed, center, playerId));
            return;
        }
    }

    /// <summary>
    /// Auto-upgrade Builder Nest → Soldier Nest when 2 Walls appear at diagonals (Section 6.3).
    /// </summary>
    private static void CheckAutoUpgrades(GameState state)
    {
        foreach (var nest in state.Nests.ToList())
        {
            if (nest.Type != NestType.Builder) continue;

            // Check diagonal positions for 2 walls owned by same player
            var walls = new List<Block>();
            foreach (var offset in GridPos.DiagonalOffsets)
            {
                var block = state.GetBlockAt(nest.Center + offset);
                if (block != null && block.Type == BlockType.Wall
                    && block.PlayerId == nest.PlayerId
                    && block.IsFullyRooted && !block.IsInFormation)
                {
                    walls.Add(block);
                }
            }

            if (walls.Count >= 2)
            {
                // Upgrade to Soldier Nest
                nest.Type = NestType.Soldier;
                var upgradeWalls = walls.Take(2).ToList();
                foreach (var wall in upgradeWalls)
                {
                    wall.FormationId = nest.Id;
                    nest.MemberIds.Add(wall.Id);
                }
                // Preserve spawn progress (partial progress carries over)
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.FormationFormed, nest.Center, nest.PlayerId));
            }
        }
    }

    /// <summary>
    /// BFS outward from center to find nearest free, passable cell (up to 3 cells away).
    /// </summary>
    private static GridPos? FindSpawnCell(GameState state, GridPos center)
    {
        // Try center first
        if (state.Grid.InBounds(center) && !state.Grid[center].BlockId.HasValue
            && state.Grid[center].IsPassable)
            return center;

        // BFS
        var visited = new HashSet<GridPos> { center };
        var queue = new Queue<(GridPos pos, int dist)>();

        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var next = center + offset;
            if (state.Grid.InBounds(next) && visited.Add(next))
                queue.Enqueue((next, 1));
        }

        while (queue.Count > 0)
        {
            var (pos, dist) = queue.Dequeue();

            if (!state.Grid[pos].BlockId.HasValue && state.Grid[pos].IsPassable)
                return pos;

            if (dist >= 3) continue;

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var next = pos + offset;
                if (state.Grid.InBounds(next) && visited.Add(next))
                    queue.Enqueue((next, dist + 1));
            }
        }

        return null;
    }
}
