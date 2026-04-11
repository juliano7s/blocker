using Blocker.Game.Input;
using Blocker.Game.Net;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game;

/// <summary>
/// Multiplayer counterpart of TickRunner. Pumps RelayClient.DrainInbound() each
/// frame, then asks LockstepCoordinator to advance as many ticks as the
/// accumulator allows. Implements ICommandSink so SelectionManager can push
/// player commands directly into the coordinator the moment they're created.
/// </summary>
public partial class MultiplayerTickRunner : Node, SelectionManager.ICommandSink
{
    [Export] public int TickRate = 12;
    private const int MaxAdvancePerFrame = 5;

    private LockstepCoordinator? _coord;
    private RelayClient? _relay;
    private GameState? _state;
    private double _accumulator;

    public double TickInterval => 1.0 / TickRate;
    public float InterpolationFactor =>
        TickInterval > 0 ? Mathf.Clamp((float)(_accumulator / TickInterval), 0f, 1f) : 1f;

    public void Initialize(LockstepCoordinator coord, RelayClient relay, GameState state)
    {
        _coord = coord; _relay = relay; _state = state;
    }

    public void Submit(Command cmd) => _coord?.QueueLocalCommand(cmd);

    public override void _Process(double delta)
    {
        if (_coord == null || _relay == null) return;

        _relay.DrainInbound();
        _accumulator += delta;

        int advanced = 0;
        while (_accumulator >= TickInterval && advanced < MaxAdvancePerFrame)
        {
            bool stepped = _coord.PollAdvance();
            if (!stepped) { _coord.ReportStallTime(delta * 1000); break; }
            _accumulator -= TickInterval;
            advanced++;
        }

        // Cap accumulator drift if we fell far behind so we don't burn through
        // pent-up time the moment the coordinator unblocks.
        if (_accumulator > 2 * TickInterval)
            _accumulator = 2 * TickInterval;
    }
}
