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

                // Check spawn toggle — skip unit but reset progress for retry next cycle
                if (player != null && player.SpawnDisabled.Contains(spawnType))
                {
                    nest.SpawnProgress = 0;
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
        var dissolvedEvent = nest.Type switch
        {
            NestType.Builder => VisualEventType.BuilderNestDissolved,
            NestType.Soldier => VisualEventType.SoldierNestDissolved,
            NestType.Stunner => VisualEventType.StunnerNestDissolved,
            _ => VisualEventType.BuilderNestDissolved
        };
        state.VisualEvents.Add(new VisualEvent(
            dissolvedEvent, nest.Center, nest.PlayerId));
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

    /// <summary>
    /// 5-block cross pattern (game bible Section 6.2):
    ///   [a][w]
    ///      [a]  ← center (nest zone)
    ///   [a][w]
    /// 2 walls must be on the same side (adjacent diagonals).
    /// 3 main units fill the 3 orthogonal positions facing the walls.
    /// </summary>
    private static bool TryFormCrossNest(GameState state, GridPos center,
        List<Block> orthoBlocks, List<Block> diagBlocks,
        BlockType mainType, NestType nestType, int mainNeeded)
    {
        // The 4 valid cross orientations: (wall diag 1, wall diag 2, required ortho 1, ortho 2, ortho 3)
        var crossPatterns = new (GridPos wallD1, GridPos wallD2, GridPos o1, GridPos o2, GridPos o3)[]
        {
            // Walls right:  diag(1,-1),(1,1)  ortho: up, right, down
            (new(1,-1), new(1,1), new(0,-1), new(1,0), new(0,1)),
            // Walls left:   diag(-1,-1),(-1,1)  ortho: up, left, down
            (new(-1,-1), new(-1,1), new(0,-1), new(-1,0), new(0,1)),
            // Walls up:     diag(-1,-1),(1,-1)  ortho: left, up, right
            (new(-1,-1), new(1,-1), new(-1,0), new(0,-1), new(1,0)),
            // Walls down:   diag(-1,1),(1,1)  ortho: left, down, right
            (new(-1,1), new(1,1), new(-1,0), new(0,1), new(1,0)),
        };

        foreach (var (wallD1, wallD2, o1, o2, o3) in crossPatterns)
        {
            var w1 = diagBlocks.FirstOrDefault(b =>
                b.Pos == center + wallD1 && b.Type == BlockType.Wall);
            var w2 = diagBlocks.FirstOrDefault(b =>
                b.Pos == center + wallD2 && b.Type == BlockType.Wall);
            if (w1 == null || w2 == null) continue;
            if (w1.PlayerId != w2.PlayerId) continue;

            int playerId = w1.PlayerId;

            var m1 = orthoBlocks.FirstOrDefault(b =>
                b.Pos == center + o1 && b.Type == mainType && b.PlayerId == playerId);
            var m2 = orthoBlocks.FirstOrDefault(b =>
                b.Pos == center + o2 && b.Type == mainType && b.PlayerId == playerId);
            var m3 = orthoBlocks.FirstOrDefault(b =>
                b.Pos == center + o3 && b.Type == mainType && b.PlayerId == playerId);
            if (m1 == null || m2 == null || m3 == null) continue;

            // Form the nest
            var members = new List<Block> { m1, m2, m3, w1, w2 };
            var nest = new Nest
            {
                Id = state.NextNestId(),
                Type = nestType,
                PlayerId = playerId,
                Center = center,
                MemberIds = { Capacity = members.Count }
            };
            nest.MemberIds.AddRange(members.Select(b => b.Id).OrderBy(id => id));

            foreach (var member in members)
                member.FormationId = nest.Id;

            state.Nests.Add(nest);
            var formedEvent = nestType == NestType.Soldier
                ? VisualEventType.SoldierNestFormed
                : VisualEventType.StunnerNestFormed;
            state.VisualEvents.Add(new VisualEvent(
                formedEvent, center, playerId));
            return true;
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
                Id = state.NextNestId(),
                Type = NestType.Builder,
                PlayerId = playerId,
                Center = center,
                MemberIds = { Capacity = members.Count }
            };
            nest.MemberIds.AddRange(members.Select(b => b.Id).OrderBy(id => id));

            foreach (var member in members)
                member.FormationId = nest.Id;

            state.Nests.Add(nest);
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.BuilderNestFormed, center, playerId));
            return;
        }
    }

    /// <summary>
    /// Auto-upgrade Builder Nest → Soldier Nest when 2 Walls appear at adjacent
    /// diagonals forming a valid cross pattern (Section 6.3).
    /// </summary>
    private static void CheckAutoUpgrades(GameState state)
    {
        // Adjacent diagonal pairs (same side)
        var adjacentDiagPairs = new (GridPos, GridPos)[]
        {
            (new(1,-1), new(1,1)),   // right side
            (new(-1,-1), new(-1,1)), // left side
            (new(-1,-1), new(1,-1)), // top side
            (new(-1,1), new(1,1)),   // bottom side
        };

        foreach (var nest in state.Nests.ToList())
        {
            if (nest.Type != NestType.Builder) continue;

            foreach (var (d1, d2) in adjacentDiagPairs)
            {
                var w1 = state.GetBlockAt(nest.Center + d1);
                var w2 = state.GetBlockAt(nest.Center + d2);
                if (w1 == null || w2 == null) continue;
                if (w1.Type != BlockType.Wall || w2.Type != BlockType.Wall) continue;
                if (w1.PlayerId != nest.PlayerId || w2.PlayerId != nest.PlayerId) continue;
                if (!w1.IsFullyRooted || !w2.IsFullyRooted) continue;
                if (w1.IsInFormation || w2.IsInFormation) continue;

                // Upgrade to Soldier Nest
                nest.Type = NestType.Soldier;
                w1.FormationId = nest.Id;
                w2.FormationId = nest.Id;
                nest.MemberIds.Add(w1.Id);
                nest.MemberIds.Add(w2.Id);
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.SoldierNestFormed, nest.Center, nest.PlayerId));
                break;
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
