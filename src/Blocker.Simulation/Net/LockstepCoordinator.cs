using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Systems;

namespace Blocker.Simulation.Net;

public enum CoordinatorFsm
{
    Lobby,
    Running,
    Stalled,
    Desynced,
    Ended,
}

/// <summary>
/// Pure-C# N-player lockstep coordinator. Drives GameState.Tick() when all
/// active players have submitted commands for the next tick.
/// </summary>
public class LockstepCoordinator
{
    public CoordinatorFsm Fsm { get; private set; } = CoordinatorFsm.Lobby;
    public int CurrentTick => _currentTick;
    public int InputDelay { get; set; } = 1;  // M1: fixed
    public int LocalPlayerId { get; }

    /// <summary>Milliseconds spent in Stalled state since last successful advance.</summary>
    public double StallMs { get; private set; }

    private readonly GameState _state;
    private readonly IRelayClient _relay;
    private readonly HashSet<int> _activePlayers;

    // playerId → (tick → commands)
    private readonly Dictionary<int, SortedDictionary<int, IReadOnlyList<Command>>> _buffers = new();
    // tick → (playerId → hash)
    private readonly Dictionary<int, Dictionary<int, uint>> _hashBuffer = new();

    // Local pending commands staged by input (applied to the next schedulable tick)
    private readonly List<Command> _pendingLocal = new();

    private int _currentTick = 0;
    private int _highestSubmittedLocalTick = -1;

    public event Action<int /*winnerTeamId*/>? GameEnded;
    public event Action? DesyncDetected;

    public LockstepCoordinator(int localPlayerId, GameState state, IRelayClient relay, HashSet<int> activePlayers)
    {
        LocalPlayerId = localPlayerId;
        _state = state;
        _relay = relay;
        _activePlayers = new HashSet<int>(activePlayers);
        foreach (var pid in _activePlayers)
            _buffers[pid] = new SortedDictionary<int, IReadOnlyList<Command>>();

        _relay.CommandsReceived += OnCommandsReceived;
        _relay.HashReceived += OnHashReceived;
        _relay.PlayerLeft += OnPlayerLeft;
    }

    public void StartGame()
    {
        if (Fsm != CoordinatorFsm.Lobby) return;
        Fsm = CoordinatorFsm.Running;
    }

    /// <summary>Queue a command from local input. Applied to the next schedulable tick.</summary>
    public void QueueLocalCommand(Command cmd)
    {
        _pendingLocal.Add(cmd);
    }

    /// <summary>
    /// Called every frame by MultiplayerTickRunner (or repeatedly in tests).
    /// Advances at most one simulation tick. Returns true if a tick was executed.
    /// </summary>
    public bool PollAdvance()
    {
        if (Fsm == CoordinatorFsm.Desynced || Fsm == CoordinatorFsm.Ended) return false;

        // 1. Submit local commands for the next schedulable tick (if not yet submitted).
        //    With InputDelay=1, the first submission targets tick 1: commands issued while
        //    rendering tick N apply at tick N+InputDelay.
        int localTarget = _currentTick + InputDelay;
        if (_highestSubmittedLocalTick < localTarget)
        {
            var batch = _pendingLocal.ToArray();
            _pendingLocal.Clear();
            _buffers[LocalPlayerId][localTarget] = batch;
            _relay.SendCommands(localTarget, batch);
            _highestSubmittedLocalTick = localTarget;
        }

        // 2. Can we advance? Every active player must have submitted commands for _currentTick + 1.
        int nextTick = _currentTick + 1;
        foreach (var pid in _activePlayers)
        {
            if (!_buffers[pid].ContainsKey(nextTick))
            {
                Fsm = CoordinatorFsm.Stalled;
                return false;
            }
        }

        // 3. Merge, sorted by playerId (deterministic order).
        var merged = new List<Command>();
        foreach (var pid in _activePlayers.OrderBy(x => x))
            merged.AddRange(_buffers[pid][nextTick]);

        _state.Tick(merged);
        _currentTick = nextTick;
        Fsm = CoordinatorFsm.Running;
        StallMs = 0;

        // 4. Hash + broadcast
        uint h = StateHasher.Hash(_state);
        _relay.SendHash(_currentTick, h);
        if (!_hashBuffer.TryGetValue(_currentTick, out var map))
            _hashBuffer[_currentTick] = map = new Dictionary<int, uint>();
        map[LocalPlayerId] = h;
        CheckMajorityVote(_currentTick);

        // 5. GC — drop buffers older than 10 ticks behind current.
        int cutoff = _currentTick - 10;
        foreach (var buf in _buffers.Values)
        {
            var toRemove = buf.Keys.Where(k => k < cutoff).ToList();
            foreach (var k in toRemove) buf.Remove(k);
        }
        var hashesToRemove = _hashBuffer.Keys.Where(k => k < cutoff).ToList();
        foreach (var k in hashesToRemove) _hashBuffer.Remove(k);

        // 6. Game end via elimination.
        var winningTeam = EliminationSystem.GetWinningTeam(_state);
        if (winningTeam.HasValue && Fsm != CoordinatorFsm.Ended)
        {
            Fsm = CoordinatorFsm.Ended;
            GameEnded?.Invoke(winningTeam.Value);
        }

        return true;
    }

    /// <summary>Called by MultiplayerTickRunner when stalled, to track stall time for UI.</summary>
    public void ReportStallTime(double deltaMs) { if (Fsm == CoordinatorFsm.Stalled) StallMs += deltaMs; }

    private void OnCommandsReceived(int playerId, int tick, IReadOnlyList<Command> commands)
    {
        if (!_activePlayers.Contains(playerId)) return;
        if (tick <= _currentTick) return; // late/duplicate — drop
        if (tick > _currentTick + 50) return; // implausibly far ahead — drop
        _buffers[playerId][tick] = commands;
    }

    private void OnHashReceived(int playerId, int tick, uint hash)
    {
        if (tick > _currentTick + 10 || tick < _currentTick - 10) return;
        if (!_hashBuffer.TryGetValue(tick, out var map))
            _hashBuffer[tick] = map = new Dictionary<int, uint>();
        map[playerId] = hash;
        if (tick <= _currentTick) CheckMajorityVote(tick);
    }

    private void OnPlayerLeft(int playerId, int effectiveTick, LeaveReason reason)
    {
        // Task 8 implements this fully.
    }

    private void CheckMajorityVote(int tick)
    {
        // Task 9 implements this fully.
    }
}
