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

    // Pending disconnect bookkeeping (Task 8). Each entry says: when _currentTick
    // reaches `atTick`, mark the player IsEliminated; once we've passed `afterTick`,
    // remove them from the active set so PollAdvance no longer waits for their input.
    private readonly List<(int playerId, int atTick)> _disconnectEliminations = new();
    private readonly List<(int playerId, int afterTick)> _pendingRemovals = new();

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

        // 3.5. Apply deterministic disconnect eliminations at their effective tick.
        // Setting IsEliminated directly (bypassing EliminationSystem's 3-builder rule) is
        // intentional: a disconnected player is eliminated regardless of their unit count.
        // Every peer runs this on the same _currentTick, so state hashes stay identical.
        // Must run BEFORE hashing so the hash reflects the post-elimination state.
        for (int i = _disconnectEliminations.Count - 1; i >= 0; i--)
        {
            var (pid, atTick) = _disconnectEliminations[i];
            if (_currentTick == atTick)
            {
                var player = _state.Players.FirstOrDefault(p => p.Id == pid);
                if (player != null) player.IsEliminated = true;
                _disconnectEliminations.RemoveAt(i);
            }
        }
        // Remove disconnected players from the active set once we've reached their
        // effective tick. After this, PollAdvance won't wait for their buffers.
        for (int i = _pendingRemovals.Count - 1; i >= 0; i--)
        {
            var (pid, afterTick) = _pendingRemovals[i];
            if (_currentTick >= afterTick)
            {
                _activePlayers.Remove(pid);
                _pendingRemovals.RemoveAt(i);
            }
        }

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
        if (!_activePlayers.Contains(playerId)) return;

        // Fill the disconnected player's buffer from _currentTick+1 up to effectiveTick
        // with empty commands. This unblocks PollAdvance so the remaining players don't
        // stall waiting for input that will never arrive.
        if (!_buffers.TryGetValue(playerId, out var buf))
            _buffers[playerId] = buf = new SortedDictionary<int, IReadOnlyList<Command>>();
        for (int t = _currentTick + 1; t <= effectiveTick; t++)
            buf[t] = Array.Empty<Command>();

        // Schedule deterministic elimination at effectiveTick: every peer applies this
        // at the exact same tick, so hashes stay identical across clients.
        _disconnectEliminations.Add((playerId, effectiveTick));

        // Schedule removal from the active set once we've passed effectiveTick. We can't
        // remove immediately because we still need the filled buffers to participate in
        // tick merging for the [_currentTick+1, effectiveTick] window.
        _pendingRemovals.Add((playerId, effectiveTick));
    }

    private void CheckMajorityVote(int tick)
    {
        // Task 9 implements this fully.
    }
}
