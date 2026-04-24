using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Systems;

/// <summary>
/// Checks elimination conditions each tick.
/// Game bible Section 11.
/// </summary>
public static class EliminationSystem
{
    /// <summary>
    /// Check all players for elimination. A player is eliminated when ALL THREE
    /// conditions are true simultaneously:
    /// 1. No army (zero Soldiers, Stunners, Wardens, Jumpers)
    /// 2. No active nests
    /// 3. Fewer than 3 Builders
    /// </summary>
    public static void Tick(GameState state)
    {
        foreach (var player in state.Players)
        {
            if (player.IsEliminated) continue;

            bool hasArmy = false;
            int builderCount = 0;

            foreach (var block in state.Blocks)
            {
                if (block.PlayerId != player.Id) continue;
                if (block.Type == BlockType.Nugget) continue;

                if (block.Type is BlockType.Soldier or BlockType.Stunner
                    or BlockType.Warden or BlockType.Jumper)
                {
                    hasArmy = true;
                    break;
                }

                if (block.Type == BlockType.Builder)
                    builderCount++;
            }

            if (hasArmy) continue;

            bool hasNests = false;
            foreach (var nest in state.Nests)
            {
                if (nest.PlayerId == player.Id)
                {
                    hasNests = true;
                    break;
                }
            }

            if (hasNests) continue;
            if (builderCount >= 3) continue;

            // All three conditions met — eliminated
            player.IsEliminated = true;
            state.VisualEvents.Add(new VisualEvent(
                VisualEventType.PlayerEliminated, new GridPos(0, 0), player.Id));
        }
    }

    /// <summary>
    /// Check if the game is over. Returns the winning team ID, or null if still playing.
    /// In FFA each player is their own team.
    /// </summary>
    public static int? GetWinningTeam(GameState state)
    {
        var activeTeams = new HashSet<int>();
        foreach (var player in state.Players)
        {
            if (!player.IsEliminated)
                activeTeams.Add(player.TeamId);
        }

        if (activeTeams.Count == 1)
            return activeTeams.First();

        if (activeTeams.Count == 0)
        {
            // Simultaneous elimination — team with more total blocks wins
            var teamBlocks = new Dictionary<int, int>();
            foreach (var player in state.Players)
            {
                if (!teamBlocks.ContainsKey(player.TeamId))
                    teamBlocks[player.TeamId] = 0;

                foreach (var block in state.Blocks)
                {
                    if (block.PlayerId == player.Id)
                        teamBlocks[player.TeamId]++;
                }
            }

            return teamBlocks.OrderByDescending(kv => kv.Value).First().Key;
        }

        return null; // Game still in progress
    }
}
