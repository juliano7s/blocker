namespace Blocker.Simulation.Core;

/// <summary>
/// A pure C# orchestrator that handles accumulator math, drift capping,
/// and lag-spike protection (MaxAdvancePerFrame) for a game loop.
/// Decoupled from Godot so it can be rigorously tested.
/// </summary>
public class SimulationTicker
{
    private readonly GameState _gameState;
    
    public int TickRate { get; }
    public int MaxAdvancePerFrame { get; }
    public double TickInterval => 1.0 / TickRate;
    
    public double Accumulator { get; private set; }
    public float InterpolationFactor =>
        TickInterval > 0 ? (float)Math.Clamp(Accumulator / TickInterval, 0.0, 1.0) : 1f;

    /// <summary>
    /// Function that attempts to advance the simulation by one tick.
    /// Should return true if the tick executed, false if it stalled (e.g., waiting for network).
    /// </summary>
    private readonly Func<double, bool> _tryAdvance;

    /// <summary>
    /// Action to run before any ticks are attempted (e.g., draining network inbound).
    /// </summary>
    private readonly Action<double>? _preTick;

    public SimulationTicker(
        GameState gameState,
        int tickRate, 
        int maxAdvancePerFrame, 
        Func<double, bool> tryAdvance,
        Action<double>? preTick = null)
    {
        _gameState = gameState;
        TickRate = tickRate;
        MaxAdvancePerFrame = maxAdvancePerFrame;
        _tryAdvance = tryAdvance;
        _preTick = preTick;
    }

    /// <summary>
    /// Process a frame delta. Clears visual events once per frame, runs pre-tick logic,
    /// and pumps the simulation forward based on the accumulator.
    /// </summary>
    public void ProcessFrame(double delta)
    {
        // 1. Guard: Enforce clearing visual events exactly once per frame
        _gameState.ClearVisualEvents();

        // 2. Pre-tick (e.g., read incoming network packets)
        _preTick?.Invoke(delta);

        // 3. Accumulate time
        Accumulator += delta;

        // 4. Pump ticks
        int advanced = 0;
        while (Accumulator >= TickInterval && advanced < MaxAdvancePerFrame)
        {
            bool stepped = _tryAdvance(delta);
            if (!stepped) 
            {
                break; // Stalled (e.g., missing network packets)
            }
            
            Accumulator -= TickInterval;
            advanced++;
        }

        // 5. Guard: Cap accumulator drift if we fell far behind
        // (prevents burning through pent-up time later and freezing the renderer)
        if (Accumulator > 2 * TickInterval)
        {
            Accumulator = 2 * TickInterval;
        }
    }
}
