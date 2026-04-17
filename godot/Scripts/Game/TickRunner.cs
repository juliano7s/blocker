using Blocker.Game.Input;
using Blocker.Simulation.Core;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Drives the simulation at a fixed tick rate from Godot's _Process loop.
/// Collects commands from SelectionManager and passes them to the pure C# SimulationTicker.
/// </summary>
public partial class TickRunner : Node
{
    [Export] public int TickRate = 12;
    private const int MaxAdvancePerFrame = 5;

    private SimulationTicker? _ticker;
    private SelectionManager? _selectionManager;

    public double TickInterval => _ticker?.TickInterval ?? 1.0 / TickRate;
    public float InterpolationFactor => _ticker?.InterpolationFactor ?? 1f;

    public void SetGameState(GameState state)
    {
        _ticker = new SimulationTicker(
            state,
            TickRate,
            MaxAdvancePerFrame,
            tryAdvance: (_) =>
            {
                var commands = _selectionManager?.FlushCommands();
                state.Tick(commands);
                return true; // Single-player never stalls
            }
        );
    }

    public void SetSelectionManager(SelectionManager sm)
    {
        _selectionManager = sm;
    }

    public override void _Process(double delta)
    {
        _ticker?.ProcessFrame(delta);
    }
}
