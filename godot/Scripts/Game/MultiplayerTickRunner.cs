using Blocker.Game.Input;
using Blocker.Game.Net;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Multiplayer counterpart of TickRunner. Pumps RelayClient.DrainInbound() each
/// frame via SimulationTicker, then asks LockstepCoordinator to advance as many
/// ticks as the accumulator allows. Implements ICommandSink so SelectionManager
/// can push player commands directly into the coordinator the moment they're created.
/// </summary>
public partial class MultiplayerTickRunner : Node, SelectionManager.ICommandSink
{
    [Export] public int TickRate = 12;
    private const int MaxAdvancePerFrame = 5;

    private LockstepCoordinator? _coord;
    private SimulationTicker? _ticker;

    public double TickInterval => _ticker?.TickInterval ?? 1.0 / TickRate;
    public float InterpolationFactor => _ticker?.InterpolationFactor ?? 1f;

    public void Initialize(LockstepCoordinator coord, RelayClient relay, GameState state)
    {
        _coord = coord;
        
        _ticker = new SimulationTicker(
            state,
            TickRate,
            MaxAdvancePerFrame,
            preTick: (_) => relay.DrainInbound(),
            tryAdvance: (delta) =>
            {
                bool stepped = coord.PollAdvance();
                if (!stepped)
                {
                    coord.ReportStallTime(delta * 1000);
                }
                return stepped;
            }
        );
    }

    public void Submit(Command cmd) => _coord?.QueueLocalCommand(cmd);

    public override void _Process(double delta)
    {
        _ticker?.ProcessFrame(delta);
    }
}
