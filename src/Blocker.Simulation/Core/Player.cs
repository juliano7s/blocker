namespace Blocker.Simulation.Core;

public class Player
{
    public int Id { get; init; }
    public int TeamId { get; init; }
    public int Population { get; set; }
    public int MaxPopulation { get; set; }
    public bool IsEliminated { get; set; }
}
