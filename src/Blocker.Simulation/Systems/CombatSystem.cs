using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Resolves combat: standard surrounding kills + soldier adjacency kills.
/// Game bible Sections 5.1, 5.2, 5.3.
/// </summary>
public static class CombatSystem
{
    public static void Tick(GameState state)
    {
        var toKill = new HashSet<int>();
        var soldierHpLoss = new Dictionary<int, int>();

        // Pass 1: Standard surrounding (Section 5.1)
        foreach (var block in state.Blocks)
        {
            if (block.Type == BlockType.Wall) continue; // Walls immune
            if (block.Type == BlockType.Nugget) continue; // Nuggets immune to combat
            if (toKill.Contains(block.Id)) continue;

            if (ShouldDieFromSurrounding(state, block))
                toKill.Add(block.Id);
        }

        // Pass 2: Soldier adjacency kills (Section 5.2)
        foreach (var block in state.Blocks)
        {
            if (block.Type == BlockType.Wall) continue; // Walls immune to soldier kills
            if (block.Type == BlockType.Nugget) continue; // Nuggets immune to combat
            if (toKill.Contains(block.Id)) continue;

            int soldiersNeeded = GetSoldiersNeededToKill(block);
            if (soldiersNeeded <= 0)
            {
                // Mobile soldiers attacked by rooted soldiers: 1 rooted soldier kills a mobile one
                // (mobile-vs-mobile is handled by mutual kill in Pass 3)
                if (block.Type == BlockType.Soldier && block.IsMobile)
                {
                    bool hasRootedEnemySoldier = false;
                    foreach (var offset in GridPos.OrthogonalOffsets)
                    {
                        var neighbor = state.GetBlockAt(block.Pos + offset);
                        if (neighbor != null && neighbor.Type == BlockType.Soldier
                            && !neighbor.IsStunned
                            && IsEnemy(state, block, neighbor) && !neighbor.IsMobile
                            && !toKill.Contains(neighbor.Id))
                        {
                            hasRootedEnemySoldier = true;
                            break;
                        }
                    }
                    if (hasRootedEnemySoldier)
                    {
                        toKill.Add(block.Id);
                        // HP loss for killing soldiers
                        foreach (var offset in GridPos.OrthogonalOffsets)
                        {
                            var pos = block.Pos + offset;
                            var neighbor = state.GetBlockAt(pos);
                            if (neighbor != null && neighbor.Type == BlockType.Soldier
                                && !neighbor.IsStunned
                                && IsEnemy(state, block, neighbor) && !neighbor.IsMobile)
                            {
                                soldierHpLoss.TryGetValue(neighbor.Id, out int loss);
                                soldierHpLoss[neighbor.Id] = loss + 1;
                            }
                        }
                    }
                }
                continue;
            }

            bool useAll8 = block.State is BlockState.Rooted or BlockState.Rooting or BlockState.Uprooting
                           || block.IsInFormation;
            var adjacentSoldiers = CountAdjacentEnemySoldiers(state, block, useAll8);
            if (adjacentSoldiers >= soldiersNeeded)
            {
                toKill.Add(block.Id);

                // Mark soldiers for HP loss
                foreach (var soldierPos in GetAdjacentEnemySoldierPositions(state, block, useAll8))
                {
                    var soldier = state.GetBlockAt(soldierPos);
                    if (soldier != null && soldier.Type == BlockType.Soldier)
                    {
                        soldierHpLoss.TryGetValue(soldier.Id, out int loss);
                        soldierHpLoss[soldier.Id] = loss + 1;
                    }
                }
            }
        }

        // Pass 3: Soldier mutual kills (mobile soldier vs mobile soldier)
        var soldierPairs = new HashSet<(int, int)>();
        foreach (var block in state.Blocks)
        {
            if (block.Type != BlockType.Soldier) continue;
            if (!block.IsMobile) continue;
            if (block.IsStunned) continue; // Stunned soldiers can't attack
            if (toKill.Contains(block.Id)) continue;

            foreach (var offset in GridPos.OrthogonalOffsets)
            {
                var neighborPos = block.Pos + offset;
                var neighbor = state.GetBlockAt(neighborPos);
                if (neighbor == null) continue;
                if (neighbor.Type != BlockType.Soldier) continue;
                if (neighbor.IsStunned) continue;
                if (!state.AreEnemies(block, neighbor)) continue;
                if (toKill.Contains(neighbor.Id)) continue;

                var pair = block.Id < neighbor.Id ? (block.Id, neighbor.Id) : (neighbor.Id, block.Id);
                if (soldierPairs.Add(pair))
                {
                    // Mutual kill — both die
                    toKill.Add(block.Id);
                    toKill.Add(neighbor.Id);
                }
            }
        }

        // Pass 4: Soldier combo — start or reset combo timer instead of immediate HP loss
        foreach (var soldierId in soldierHpLoss.Keys)
        {
            var soldier = state.GetBlock(soldierId);
            if (soldier == null) continue;
            bool isNewCombo = soldier.SwordComboTimer <= 0;
            soldier.SwordComboTimer = Constants.SoldierComboTicks + 1;
            if (isNewCombo)
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.SoldierComboStarted, soldier.Pos, soldier.PlayerId, BlockId: soldier.Id));
        }

        // Pass 5: Neutral obstacle combat (Section 5.3)
        ResolvNeutralObstacles(state, toKill);

        // Apply kills
        var deadBlocks = state.Blocks.Where(b => toKill.Contains(b.Id)).ToList();
        foreach (var block in deadBlocks)
        {
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.BlockDied, block.Pos, block.PlayerId, BlockId: block.Id));
            state.RemoveBlock(block);
        }
    }

    /// <summary>
    /// Standard surrounding kill check (Section 5.1).
    /// Only Soldiers count as "enemies" for surrounding thresholds.
    /// </summary>
    private static bool ShouldDieFromSurrounding(GameState state, Block target)
    {
        if (target.Type == BlockType.Nugget) return false;

        int orthoEnemySoldiers = 0;
        int orthoFriendly = 0;
        int diagEnemySoldiers = 0;

        foreach (var offset in GridPos.OrthogonalOffsets)
        {
            var neighbor = state.GetBlockAt(target.Pos + offset);
            if (neighbor == null) continue;

            if (IsEnemy(state, target, neighbor))
            {
                // Stunned soldiers can't attack.
                if (neighbor.Type == BlockType.Soldier && !neighbor.IsStunned)
                    orthoEnemySoldiers++;
            }
            else
            {
                orthoFriendly++;
            }
        }

        // Diagonal enemies (soldiers only)
        var diagonalOffsets = new GridPos[]
        {
            new(1, -1), new(1, 1), new(-1, 1), new(-1, -1)
        };
        foreach (var offset in diagonalOffsets)
        {
            var neighbor = state.GetBlockAt(target.Pos + offset);
            if (neighbor == null) continue;
            if (IsEnemy(state, target, neighbor) && neighbor.Type == BlockType.Soldier && !neighbor.IsStunned)
                diagEnemySoldiers++;
        }

        // 3+ orthogonal enemy soldiers
        if (orthoEnemySoldiers >= 3) return true;

        // 2 orthogonal enemies + 2 friendly neighbors (overcrowding)
        if (orthoEnemySoldiers >= 2 && orthoFriendly >= 2) return true;

        // 2 orthogonal enemies + 1 diagonal enemy
        if (orthoEnemySoldiers >= 2 && diagEnemySoldiers >= 1) return true;

        return false;
    }

    /// <summary>
    /// How many adjacent soldiers needed to kill this block type (Section 5.2).
    /// Returns 0 if soldiers can't kill this type through adjacency.
    /// </summary>
    private static int GetSoldiersNeededToKill(Block target)
    {
        if (target.Type == BlockType.Nugget) return 0;

        // Uprooted/mobile units: 1 soldier orthogonally
        if (target.IsMobile)
        {
            return target.Type switch
            {
                BlockType.Builder => 1,
                BlockType.Warden => 1,
                BlockType.Jumper => 1,
                BlockType.Stunner => 1,
                BlockType.Soldier => 0, // Handled by mutual kill (Pass 3)
                _ => 0
            };
        }

        // Rooted units: 2 soldiers (all 8 directions)
        if (target.IsFullyRooted && !target.IsInFormation)
        {
            return target.Type switch
            {
                BlockType.Builder => 2,
                BlockType.Soldier => 2,
                BlockType.Warden => 2,
                BlockType.Stunner => 3, // Rooted stunner needs 3
                _ => 0
            };
        }

        // Rooting/uprooting — treat as rooted for thresholds
        if (target.State is BlockState.Rooting or BlockState.Uprooting)
        {
            return target.Type switch
            {
                BlockType.Builder => 2,
                BlockType.Soldier => 2,
                BlockType.Warden => 2,
                BlockType.Stunner => 3,
                _ => 0
            };
        }

        // Formation members: 3 orthogonal
        if (target.IsInFormation)
            return 3;

        return 0;
    }

    private static int CountAdjacentEnemySoldiers(GameState state, Block target, bool useAll8)
    {
        var offsets = useAll8 ? GridPos.AllOffsets : GridPos.OrthogonalOffsets;
        int count = 0;
        foreach (var offset in offsets)
        {
            var neighbor = state.GetBlockAt(target.Pos + offset);
            if (neighbor != null && neighbor.Type == BlockType.Soldier
                && !neighbor.IsStunned
                && IsEnemy(state, target, neighbor))
                count++;
        }
        return count;
    }

    private static IEnumerable<GridPos> GetAdjacentEnemySoldierPositions(GameState state, Block target, bool useAll8)
    {
        var offsets = useAll8 ? GridPos.AllOffsets : GridPos.OrthogonalOffsets;
        foreach (var offset in offsets)
        {
            var pos = target.Pos + offset;
            var neighbor = state.GetBlockAt(pos);
            if (neighbor != null && neighbor.Type == BlockType.Soldier
                && !neighbor.IsStunned
                && IsEnemy(state, target, neighbor))
                yield return pos;
        }
    }

    /// <summary>
    /// Neutral obstacle combat (Section 5.3).
    /// Fragile walls destroyed by 2+ adjacent (non-stunned) soldiers (any player).
    /// </summary>
    private static void ResolvNeutralObstacles(GameState state, HashSet<int> toKill)
    {
        for (int y = 0; y < state.Grid.Height; y++)
        {
            for (int x = 0; x < state.Grid.Width; x++)
            {
                var cell = state.Grid[x, y];
                if (cell.Terrain != TerrainType.FragileWall) continue;

                var pos = new GridPos(x, y);
                int adjacentSoldiers = 0;
                foreach (var offset in GridPos.AllOffsets)
                {
                    var neighbor = state.GetBlockAt(pos + offset);
                    if (neighbor != null && neighbor.Type == BlockType.Soldier && !neighbor.IsStunned)
                        adjacentSoldiers++;
                }

                if (adjacentSoldiers >= Constants.FragileWallSoldierThreshold)
                {
                    cell.Terrain = TerrainType.None;
                    state.VisualEvents.Add(new VisualEvent(
                        VisualEventType.WallDestroyed, pos, null));
                }
            }
        }
    }

    // Local thunk: combat ran most checks via IsEnemy(a,b) before teams existed.
    // We now defer to GameState.AreEnemies which understands team membership;
    // call sites pass `state` through.
    private static bool IsEnemy(GameState state, Block a, Block b) => state.AreEnemies(a, b);
}
