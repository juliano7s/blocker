using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Detects non-nest formations (Supply) and updates population caps.
/// Game bible Sections 7.4, 8.
/// </summary>
public static class FormationSystem
{
    /// <summary>
    /// Validate existing formations and detect new ones.
    /// Called during tick step 2 (formations).
    /// </summary>
    public static void DetectFormations(GameState state)
    {
        ValidateExisting(state);
        ScanForSupplyFormations(state);
        UpdatePopulationCaps(state);
    }

    private static void ValidateExisting(GameState state)
    {
        var toRemove = new List<int>();

        foreach (var formation in state.Formations)
        {
            bool memberDead = false;
            bool memberUprooting = false;

            foreach (var memberId in formation.MemberIds)
            {
                var block = state.GetBlock(memberId);
                if (block == null || block.PlayerId != formation.PlayerId)
                {
                    memberDead = true;
                    break;
                }
                if (block.State == BlockState.Uprooting || block.State == BlockState.Mobile)
                    memberUprooting = true;
            }

            if (memberDead)
            {
                DissolveFormation(state, formation, toRemove);
                continue;
            }

            if (memberUprooting && !formation.IsTearingDown)
                formation.TeardownTimer = Constants.TeardownTicks;

            if (formation.IsTearingDown)
            {
                formation.TeardownTimer--;
                if (formation.TeardownTimer <= 0)
                {
                    DissolveFormation(state, formation, toRemove);
                    continue;
                }

                bool stillUprooting = formation.MemberIds.Any(id =>
                {
                    var b = state.GetBlock(id);
                    return b != null && (b.State == BlockState.Uprooting || b.State == BlockState.Mobile);
                });
                if (!stillUprooting)
                    formation.TeardownTimer = 0;
            }
        }

        state.Formations.RemoveAll(f => toRemove.Contains(f.Id));
    }

    private static void DissolveFormation(GameState state, Formation formation, List<int> toRemove)
    {
        foreach (var memberId in formation.MemberIds)
        {
            var block = state.GetBlock(memberId);
            if (block != null && block.FormationId == formation.Id)
                block.FormationId = null;
        }
        toRemove.Add(formation.Id);
    }

    /// <summary>
    /// Scan for L-shaped 3-Wall patterns to form Supply Formations.
    /// An L-shape is: one corner wall with two perpendicular wall neighbors.
    /// </summary>
    private static void ScanForSupplyFormations(GameState state)
    {
        // Check each rooted wall as potential corner of an L
        foreach (var wall in state.Blocks.ToList())
        {
            if (wall.Type != BlockType.Wall) continue;
            if (!wall.IsFullyRooted) continue;
            if (wall.IsInFormation) continue;

            // Check all 4 L-shape orientations (corner at this wall)
            // L-shapes: corner + one horizontal + one vertical neighbor
            var lPatterns = new (GridPos, GridPos)[]
            {
                (new(1, 0), new(0, 1)),   // right + down
                (new(1, 0), new(0, -1)),  // right + up
                (new(-1, 0), new(0, 1)),  // left + down
                (new(-1, 0), new(0, -1)), // left + up
            };

            foreach (var (offset1, offset2) in lPatterns)
            {
                var pos1 = wall.Pos + offset1;
                var pos2 = wall.Pos + offset2;

                var neighbor1 = state.GetBlockAt(pos1);
                var neighbor2 = state.GetBlockAt(pos2);

                if (neighbor1 == null || neighbor2 == null) continue;
                if (neighbor1.Type != BlockType.Wall || neighbor2.Type != BlockType.Wall) continue;
                if (!neighbor1.IsFullyRooted || !neighbor2.IsFullyRooted) continue;
                if (neighbor1.IsInFormation || neighbor2.IsInFormation) continue;
                if (neighbor1.PlayerId != wall.PlayerId || neighbor2.PlayerId != wall.PlayerId) continue;

                // Form Supply Formation
                var formation = new Formation
                {
                    Type = FormationType.Supply,
                    PlayerId = wall.PlayerId,
                };
                formation.MemberIds.AddRange([wall.Id, neighbor1.Id, neighbor2.Id]);

                wall.FormationId = formation.Id;
                neighbor1.FormationId = formation.Id;
                neighbor2.FormationId = formation.Id;

                state.Formations.Add(formation);
                state.VisualEvents.Add(new VisualEvent(
                    VisualEventType.FormationFormed, wall.Pos, wall.PlayerId));
                break; // This wall is now in a formation
            }
        }
    }

    /// <summary>
    /// Recalculate MaxPopulation for all players based on Supply Formations.
    /// Base cap = 0, each Supply adds +7.
    /// </summary>
    private static void UpdatePopulationCaps(GameState state)
    {
        foreach (var player in state.Players)
        {
            int supplyCount = state.Formations.Count(f =>
                f.Type == FormationType.Supply && f.PlayerId == player.Id);
            player.MaxPopulation = supplyCount * Constants.SupplyPopCap;
        }
    }
}
