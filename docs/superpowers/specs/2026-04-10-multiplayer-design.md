# Multiplayer — Design Spec

## Goal

Deterministic lockstep multiplayer for Blocker, shipping in two phases. Phase M1 proves the full pipe end-to-end with 2 players against a publicly deployed relay. Phase M2 unlocks the 2–6 player UI and adds quality-of-life features that the protocol already supports.

This spec is authoritative for both phases but the implementation plan will scope only M1.

## Why lockstep

The simulation layer (`src/Blocker.Simulation/`) is already fully deterministic: integer-only grid coordinates, no floats, no system RNG, validated commands processed in a fixed resolution order. This is the exact shape a lockstep architecture needs. Per the game bible §12.3 and the architecture doc §4, we exchange player commands rather than game state — every client runs the same simulation from the same inputs and produces identical results.

The alternative (server-authoritative with state replication) would need a server that runs the full simulation, filters state per player, and handles rollback. That's many times more code, requires a much heavier server, and doesn't fit the existing "pure C# deterministic sim" design. Lockstep matches what we already have.

## Phase 0 — Prerequisite: fix win conditions

Multiplayer is pointless if games don't end correctly, and N-player lockstep specifically depends on `EliminationSystem` behaving correctly: when a player disconnects, they are marked eliminated and the remaining players keep playing until one team remains. This needs its own brainstorm → fix → verify cycle **before** multiplayer implementation starts. Not in scope for this spec, but blocks it.

## Scope

### In (Phase M1 — ships first)
- Pure-C# lockstep coordinator in `src/Blocker.Simulation/Net/` with unit tests
- `Blocker.Relay` console project — self-contained Linux binary, deployed to DigitalOcean droplet alongside the existing TS signaling server
- Godot client integration: `RelayClient`, `MultiplayerTickRunner`, `MultiplayerMenu`, extensions to `SlotConfigScreen`
- Wire protocol with version fields and reserved type-byte ranges for future features
- Binary command/hash serialization with deterministic byte output
- FNV-1a state hash every tick with client-side majority-vote desync detection
- Disconnect handling with tick-stamped `PlayerLeft` so elimination applies deterministically
- Diagnostic file on desync (`user://blocker/desync-{tick}.bin`)
- Systemd unit, nginx location block, deploy script for the droplet
- 2-player matches only in the UI for M1 — the coordinator and relay support N from day one

### Out (deferred to M2+)
- 3–6 player UI (code supports it, just not enabled)
- Adaptive input delay (ship fixed at 1 tick for M1)
- Chat, surrender button, rematch button (protocol reserves byte ranges)
- Reconnection after disconnect (per game bible §19 "Future/Maybe")
- Replay recording (per game bible §19)
- Lobby browser (code-based join only)
- Spectator mode

### Never (without an architectural rethink)
- Fog of war. Lockstep cannot protect hidden information — all clients hold the full `GameState` in memory. A future fog-of-war feature requires either accepting that maphacks are possible (fine for friends, bad for competitive) or pivoting to a server-authoritative architecture with per-player state filtering. The current spec explicitly does not paint us into a corner that makes that future pivot harder: command validation and tick resolution stay in pure C# so they can move to a server later if needed.

## Architecture overview

```
┌─────────────────────────────────────────────────────────────────┐
│                      Godot Client (per player)                  │
│                                                                  │
│   SelectionManager  →  LocalInputProvider                        │
│         │                    │                                  │
│         │                    ▼                                  │
│         │            LockstepCoordinator ◄──── RelayClient ◄──┐  │
│         │                    │                    │          │  │
│         │                    ▼                    │          │  │
│         │              GameState.Tick()           │          │  │
│         │                    │                    │          │  │
│         ▼                    ▼                    │          │  │
│   GridRenderer (interpolated)                     │          │  │
└────────────────────────────────────────────────┬──┼──────────┼──┘
                                                 │  │          │
                                          WebSocket (binary)   │
                                                 │  │          │
                                                 ▼  │          │
                 ┌──────────────────────────────────┴────────┐ │
                 │                                           │ │
                 │   nginx (TLS termination, /blocker/…)     │ │
                 │                  │                        │ │
                 │                  ▼                        │ │
                 │   Blocker.Relay  (127.0.0.1:3002)         │ │
                 │                                           │ │
                 │   Rooms → authenticate → fan out ─────────┼─┘
                 │                                           │
                 │   DigitalOcean droplet (Ubuntu 24.04)     │
                 └───────────────────────────────────────────┘
```

Three new places code lives:

1. **`src/Blocker.Simulation/Net/`** — pure C# lockstep, serialization, hashing, protocol. Zero Godot, zero sockets. Unit-testable.
2. **`src/Blocker.Relay/`** — new console project, self-contained deployable. References `Net/Protocol.cs` and `Net/CommandSerializer.cs` from `Blocker.Simulation` for message-type byte constants and peek-parsing.
3. **`godot/Scripts/Net/`** — Godot-side WebSocket transport and Godot node for the multiplayer tick loop.

## Wire protocol

Binary framing over WebSocket binary messages. One protocol message per WebSocket frame. All integers little-endian.

### Message types

Types are grouped into reserved ranges so future features land in predictable neighborhoods.

| Range | Purpose |
|---|---|
| `0x00–0x0F` | Session / lobby |
| `0x10–0x1F` | Tick traffic (hot path) |
| `0x20–0x2F` | Server / diagnostics |
| `0x30–0x3F` | Social (chat, emotes, surrender, rematch) |
| `0x40–0x4F` | Bulk data (map sharing, replay bundles) |
| `0x50–0xEF` | Reserved |
| `0xF0–0xFF` | Debug / protocol extension |

M1 uses these specific types:

```
0x01  Hello            — client → relay, first message
0x02  HelloAck         — relay → client
0x03  CreateRoom       — client → relay (host)
0x04  JoinRoom         — client → relay (joiner)
0x05  RoomState        — relay → clients in room
0x06  LeaveRoom        — client → relay
0x07  StartGame        — client → relay (host)
0x08  GameStarted      — relay → clients

0x10  Commands         — client ↔ relay ↔ clients
0x11  Hash             — client ↔ relay ↔ clients
0x12  PlayerLeft       — relay → clients
0x13  Surrender        — client → relay → clients  (protocol only, no UI in M1)

0x20  DesyncReport     — client → relay (logging only)
0x21  Error            — relay → client
0x22  Ping / 0x23 Pong — RTT measurement
```

M2+ reserves `0x30 ChatMessage`, `0x31 Rematch`, `0x40 MapData`, and has room for more.

### Hello message — includes version fields

```
0x01 Hello
  [protocolVersion: byte]       relay enforces exact match
  [simulationVersion: uint16]   relay passes through; clients verify
  [clientNameLen: varint]
  [clientName: UTF-8 bytes × clientNameLen]
```

- **Protocol version** is enforced by the relay: mismatched clients are rejected at connect time with `Error(PROTOCOL_MISMATCH)`.
- **Simulation version** is opaque to the relay but checked by clients before the host sends `StartGame`. Mismatches in the lobby surface as "Player X is on sim v0x43, you're on v0x42" and keep Start disabled until resolved.

### Commands message — the high-bandwidth hot path

```
0x10 Commands
  [tick:       varint]
  [playerId:   byte]                  relay rewrites/validates
  [count:      varint]
  per command:
    [type:     byte]                  CommandType enum
    [blockCount: varint]
    [blockIds:   varint × blockCount]
    [flags:      byte]                bit0=hasTargetPos bit1=hasDirection bit2=queue
    [targetX:    varint]              if flags.bit0
    [targetY:    varint]              if flags.bit0
    [direction:  byte]                if flags.bit1
```

A typical single-block move is ~7 bytes (vs ~80 bytes for JSON). At 6 players × 12 tps, the entire relay handles ~500 B/s per room — trivial.

### Hash message

```
0x11 Hash
  [tick:     varint]
  [playerId: byte]
  [hash:     uint32]
```

### PlayerLeft message — tick-stamped for determinism

```
0x12 PlayerLeft
  [playerId:     byte]
  [effectiveTick: varint]             when elimination takes effect
  [reason:       byte]                Disconnected | Surrender | Kicked
```

The relay computes `effectiveTick = max(observed tick across all clients) + 2`. Every remaining client applies the elimination at that exact tick regardless of when they received the message, preserving determinism.

### Determinism contract

`CommandSerializer.Serialize(commands)` must produce identical bytes for identical input regardless of platform, locale, endianness, or dictionary iteration order. Non-determinism here shows up as ghost desyncs. Explicitly unit-tested with a round-trip plus byte-level comparison.

### Why hand-rolled binary instead of Protobuf / MessagePack / FlatBuffers

All three add dependencies and codegen. The M1 message set is ~12 types, only 2 of them hot (`Commands`, `Hash`). Hand-rolled serializers in ~250 lines of pure C# are simpler, faster, dependency-free, and give us byte-for-byte control which matters for hashing. If the protocol grows to 50+ types later, reconsider.

## LockstepCoordinator

Lives in `src/Blocker.Simulation/Net/LockstepCoordinator.cs`. Pure C#, no Godot, no sockets. Drives the multiplayer tick loop.

### Responsibilities

1. Collect local commands and hand them to the relay (via `IRelayClient`)
2. Receive remote commands from the relay and buffer them per player
3. Decide "are we ready to advance to tick N+1?" (yes iff every active player has submitted commands for that tick)
4. Call `GameState.Tick(commands)` when ready, compute hash, broadcast
5. Detect desync via majority vote and halt
6. Handle mid-game disconnects

### Key state

```csharp
class LockstepCoordinator {
    int _localPlayerId;
    GameState _state;
    IRelayClient _relay;

    HashSet<int> _activePlayers;                                     // shrinks on disconnect/elimination
    Dictionary<int, SortedDictionary<int, TickCommands>> _buffers;   // playerId → (tick → cmds)
    Dictionary<int, Dictionary<int, uint>> _hashBuffer;              // tick → (playerId → hash)

    int _currentTick;          // last completed tick
    int _inputDelay = 1;       // fixed at 1 for M1, adaptive in M2
    CoordinatorState _fsm;     // Lobby | Running | Stalled | Desynced | Ended
}
```

### State machine

```
Lobby ──StartGame──▶ Running ⇄ Stalled
                       │          │
                       │          └── (all commands arrive) ──▶ Running
                       │
                       ├── (majority-vote mismatch) ──▶ Desynced (terminal)
                       │
                       └── (win condition / <2 teams) ──▶ Ended (terminal)
```

- **Stalled** is transient: waiting for at least one player's commands for the next tick. Visuals freeze at interpolation = 1.0. After 500 ms in Stalled, UI shows "Waiting for Alice…". After 30 s, modal offers "Alice appears to have disconnected — continue without them? [Yes] [Abort]".
- **Desynced** and **Ended** are terminal.

### Per-tick loop

```csharp
public void PollAdvance() {
    if (_fsm != Running && _fsm != Stalled) return;

    // 1. Collect local input scheduled for _currentTick + _inputDelay + 1
    var targetTick = _currentTick + _inputDelay + 1;
    var localCmds = _localInputProvider.FlushCommands(targetTick);
    SubmitLocal(new TickCommands(_localPlayerId, targetTick, localCmds));

    // 2. Try to advance
    if (!AllSubmitted(_currentTick + 1)) { _fsm = Stalled; return; }

    _fsm = Running;
    var merged = MergeBuffered(_currentTick + 1);   // ordered by playerId (deterministic)
    _state.Tick(merged);
    _currentTick++;

    // 3. Hash and broadcast
    var h = StateHasher.Hash(_state);
    _relay.SendHash(_currentTick, h);
    _hashBuffer[_currentTick][_localPlayerId] = h;
    CheckMajorityVote(_currentTick);

    // 4. GC
    GC(_currentTick - 10);
}
```

### Input delay

When you press move at local tick 100 with `_inputDelay = 1`, your command is scheduled for tick 102 (`100 + 1 + 1`). That gives the relay → other clients → their buffer pipeline 1 tick (~83 ms) of headroom.

**Adaptive delay is per-client, not synchronized** (M2 feature). Each client independently chooses how far ahead it schedules its own commands. Because `targetTick` is embedded in the message, clients using different input delays still converge — each client buffers each player's commands at the tick the player chose.

**Empty commands are valid and required.** Every player sends a `Commands` message for every tick, even if the command list is empty. This is the only way to distinguish "player is idle" from "player is lagging."

### Disconnect handling

When the relay delivers `PlayerLeft(playerId, effectiveTick)`:

```csharp
void OnPlayerLeft(int playerId, int effectiveTick, LeaveReason reason) {
    _activePlayers.Remove(playerId);

    // Fill any pending future ticks for this player with empty commands
    // so we don't stall waiting for them.
    for (int t = _currentTick + 1; t <= effectiveTick; t++)
        _buffers[playerId][t] = TickCommands.Empty(playerId, t);

    // Queue a synthetic EliminateCommand at effectiveTick.
    // All remaining clients apply it at the same tick deterministically.
    _syntheticCommands.Add(new EliminateCommand(playerId, effectiveTick));

    if (_activePlayers.Count < 2) _fsm = Ended;
}
```

### Desync detection — majority vote on client

```csharp
void CheckMajorityVote(int tick) {
    var hashes = _hashBuffer[tick];
    if (hashes.Count < _activePlayers.Count) return;   // wait for everyone

    var groups = hashes.GroupBy(kv => kv.Value).OrderByDescending(g => g.Count()).ToList();
    if (groups.Count == 1) return;                     // all agree, happy path

    var majority = groups[0];
    bool localInMajority = majority.Any(kv => kv.Key == _localPlayerId);
    bool majorityIsClear = majority.Count() > _activePlayers.Count / 2;

    if (majorityIsClear && !localInMajority) {
        _relay.SendDesyncReport(tick, _state.Snapshot());
        _fsm = Desynced;
    }
    else if (!majorityIsClear) {
        // 3-way split or worse — sim is broken, everyone halts
        _fsm = Desynced;
    }
}
```

For M1's 2-player case, any mismatch halts both clients — there's no "majority." Both save diagnostic files. Correct behavior.

### IRelayClient interface

```csharp
interface IRelayClient {
    void SendCommands(int tick, IReadOnlyList<Command> cmds);
    void SendHash(int tick, uint hash);
    void SendDesyncReport(int tick, GameStateSnapshot snap);
    void SendSurrender();

    event Action<int /*playerId*/, int /*tick*/, IReadOnlyList<Command>> CommandsReceived;
    event Action<int /*playerId*/, int /*tick*/, uint> HashReceived;
    event Action<int /*playerId*/, int /*effectiveTick*/, LeaveReason> PlayerLeft;
    event Action<int /*playerId*/> SurrenderReceived;
}
```

Production implementation is `RelayClient` (Godot-side, WebSocket-backed). Test implementation is `FakeRelayClient` that echoes messages between two coordinator instances in-process, no sockets, no Godot.

## Relay server

### Hosting stack

`System.Net.HttpListener` directly — no Kestrel, no ASP.NET Core. HttpListener supports WebSockets on Linux since .NET 5, publishes to a ~15 MB self-contained binary, and for a single-endpoint relay the absence of routing/middleware is a feature. The relay binds to `127.0.0.1:3002`; nginx handles `wss://` termination on the droplet. The server never sees TLS.

Endpoints:

```
GET  /blocker/ws-relay   → WebSocket upgrade
GET  /healthz            → 200 OK (systemd / uptime probes)
*                        → 404
```

### Core types

```csharp
class Connection {
    Guid Id;
    WebSocket Ws;
    string ClientName;
    byte ProtocolVersion;
    ushort SimulationVersion;
    Room? CurrentRoom;
    byte? AssignedPlayerId;
    DateTime LastMessageAt;
    int MessagesInLastSecond;
}

class Room {
    string Code;                        // 4-char, unambiguous alphabet
    Guid HostId;
    RoomLifecycle Lifecycle;            // Lobby | Playing | Ended
    ushort SimulationVersion;
    byte[] MapText;                     // opaque to relay
    SlotConfig[] Slots;
    Dictionary<byte, Guid> SlotOwners;  // slotId → Connection.Id
    int HighestSeenTick;
    DateTime LastActivity;
}
```

### Fan-out with authentication

The relay does **not** parse command payloads. It only reads the `tick` varint and `playerId` byte to do auth and tick tracking. The rest is opaque bytes fanned out as-is.

```csharp
void FanOutCommands(Connection conn, byte[] msg) {
    if (conn.CurrentRoom is not { Lifecycle: RoomLifecycle.Playing } room) return;
    if (conn.AssignedPlayerId is not byte assigned) return;

    var (tick, playerIdOffset) = Varint.ReadAfter(msg, 1);
    var claimedPlayerId = msg[playerIdOffset];
    if (claimedPlayerId != assigned) {
        Log.Warn($"Conn {conn.Id} claimed playerId {claimedPlayerId} but owns {assigned}");
        return;   // drop silently
    }

    room.HighestSeenTick = Math.Max(room.HighestSeenTick, tick);

    foreach (var (_, otherId) in room.SlotOwners)
        if (otherId != conn.Id)
            _connections[otherId].SendAsync(msg);
}
```

### Disconnect handling with tick-stamped PlayerLeft

```csharp
void HandleDisconnect(Connection conn) {
    var room = conn.CurrentRoom;
    if (room?.Lifecycle == RoomLifecycle.Playing && conn.AssignedPlayerId is byte pid) {
        int effectiveTick = room.HighestSeenTick + 2;

        var payload = Protocol.EncodePlayerLeft(pid, effectiveTick, LeaveReason.Disconnected);
        foreach (var (_, otherId) in room.SlotOwners)
            if (otherId != conn.Id)
                _connections[otherId].SendAsync(payload);

        room.SlotOwners.Remove(FindSlotByConn(room, conn.Id));
        if (room.SlotOwners.Count == 0) CloseRoom(room);
    }
    else if (room?.Lifecycle == RoomLifecycle.Lobby) {
        if (room.HostId == conn.Id) CloseRoom(room);
        else { room.SlotOwners.Remove(FindSlotByConn(room, conn.Id)); BroadcastRoomState(room); }
    }
}
```

### DoS hardening (baked in from day one)

| Limit | Value | Configurable via env var |
|---|---|---|
| Max messages per connection per second | 60 | `BLOCKER_RELAY_RATE_LIMIT` |
| Max message size | 64 KB | `BLOCKER_RELAY_MAX_MSG` |
| Max rooms per IP | 3 | `BLOCKER_RELAY_ROOMS_PER_IP` |
| Max concurrent connections | 500 | `BLOCKER_RELAY_MAX_CONNS` |
| Idle room timeout (Lobby) | 10 min | `BLOCKER_RELAY_LOBBY_TIMEOUT` |
| Idle room timeout (Playing) | 60 min | `BLOCKER_RELAY_GAME_TIMEOUT` |
| Hello timeout after connect | 5 s | `BLOCKER_RELAY_HELLO_TIMEOUT` |

### Logging

`stdout` → systemd journal → `journalctl -u blocker-relay`. Plain `Console.WriteLine` with a shared formatter, no structured-logging dependency.

```
[INFO]  2026-04-10T14:22:31Z conn=abc123 event=connect ip=203.0.113.5
[INFO]  2026-04-10T14:22:35Z conn=abc123 event=room-created code=PFKR map="standard_2p"
[WARN]  2026-04-10T14:23:10Z conn=xyz789 event=desync tick=487 room=PFKR
[INFO]  2026-04-10T14:25:01Z conn=abc123 event=disconnect room=PFKR slot=0 effectiveTick=3612
```

### Resource footprint

- Binary: ~15 MB self-contained
- Memory at rest: ~20 MB
- Memory per active room: ~30 KB
- CPU at 10 concurrent 6-player rooms: <1% of one core

Fits alongside the existing droplet workload with huge margin.

## Godot client integration

### New files

```
godot/Scripts/Net/
├── RelayClient.cs              — WebSocket transport, implements IRelayClient
├── RelayClientConfig.cs        — URL, retry policy, timeouts
└── MultiplayerSessionState.cs  — Stored during menu flow, passed to GameManager

godot/Scripts/Game/
└── MultiplayerTickRunner.cs    — Replaces TickRunner in MP mode

godot/Scripts/UI/
├── MultiplayerMenu.cs          — Host/Join dialog between MainMenu and SlotConfigScreen
└── SlotConfigScreen.cs         — Extended: Single / Host / Join modes
```

### RelayClient threading model

Godot game logic runs on the main thread. `ClientWebSocket` is async. The boundary:

- **Inbound:** background `ReceiveLoop` task → `ConcurrentQueue<byte[]> _inbox` → main thread drains each frame via `DrainInbound()`, which fires coordinator events synchronously.
- **Outbound:** main-thread `Send*` calls → `Channel<byte[]> _outbox` (signal-on-write wakeup, no spinning) → background `SendLoop` task writes to socket.

`LockstepCoordinator` only ever sees events on the main thread. No locks in sim/coordinator code. The only concurrent primitives are `ConcurrentQueue` and `Channel`, both well-tested in `System.Threading.Channels`.

### MultiplayerTickRunner — critical pacing loop

```csharp
public partial class MultiplayerTickRunner : Node {
    [Export] public int TickRate = 12;
    const int MaxAdvancePerFrame = 5;

    LockstepCoordinator _coord;
    RelayClient _relay;
    double _accumulator;

    double TickInterval => 1.0 / TickRate;

    public override void _Process(double delta) {
        _relay.DrainInbound();
        _accumulator += delta;

        int advanced = 0;
        while (_accumulator >= TickInterval && advanced < MaxAdvancePerFrame) {
            if (!_coord.PollAdvance()) break;   // stalled, retry next frame
            _accumulator -= TickInterval;
            advanced++;
        }

        if (_accumulator > 2 * TickInterval)
            _accumulator = 2 * TickInterval;     // cap runaway catch-up
    }
}
```

Two embedded behaviors:

1. **Stall handling.** When the coordinator can't advance, accumulator keeps growing but is capped at `2 × TickInterval`. When commands arrive, we process at most 5 ticks per frame — enough to catch up 5/12 s of missed time without freezing the render loop.
2. **Interpolation stays smooth across stalls.** The existing `GridRenderer` interpolates between `PrevPos` and `Pos` using `accumulator / tickInterval`. At stall, blocks sit at their final position; movement just pauses.

### GameManager branching

Minimal change: check for an active multiplayer session and wire up the right tick runner.

```csharp
public override void _Ready() {
    // ... existing setup ...

    if (GameLaunchData.MultiplayerSession is { } mp) {
        var coord = new LockstepCoordinator(mp.LocalPlayerId, gameState, mp.RelayClient);
        var tickRunner = new MultiplayerTickRunner { Coord = coord, Relay = mp.RelayClient };
        AddChild(tickRunner);
        _selectionManager.SetCommandSink(coord.LocalInputProvider);
    } else {
        // Existing single-player path
        _tickRunner = GetNode<TickRunner>("TickRunner");
        _tickRunner.SetGameState(gameState);
        _tickRunner.SetSelectionManager(_selectionManager);
    }
}
```

`SelectionManager` itself doesn't know it's in multiplayer — good separation.

### MultiplayerMenu flow

```
MainMenu
  ├─ Play Single Player ────▶ MapSelect ──▶ SlotConfigScreen (Single) ──▶ Game
  └─ Play Multiplayer ──▶ MultiplayerMenu
                           ├─ Host Game ────▶ MapSelect ──▶ SlotConfigScreen (Host) ──▶ Game
                           └─ Join Game ──▶ [code dialog] ──▶ SlotConfigScreen (Join) ──▶ Game
```

`MultiplayerMenu` is a small scene: Host, Join, Back buttons, plus a connection-status indicator that shows relay health. Host and Join are disabled while the relay is unreachable.

### SlotConfigScreen — three modes

The existing screen has one mode (single-player). We extend it to three, sharing ~80% of render code via a `SlotConfigMode` enum and a `SlotConfigController` interface.

**Single mode** (unchanged): slots toggle between "You" and "AI"; Start enabled when ≥1 You slot exists.

**Host mode**:
- Top banner: `Room code: PFKR` (copyable)
- Slots cycle: `Open` → `You` → `AI` → `Closed` → `Open`
- Open slots show "Waiting…" until a joiner connects, then "Alice (Blue)"
- Start enabled when every non-Closed slot is filled
- Map picker inline; changing map broadcasts updated `RoomState` to joiners

**Join mode**:
- Header: "Joined PFKR — hosted by jjack"
- Slot list read-only except the joiner's own slot choice
- Color picker for the joiner's slot
- No Start button; joiner waits for host
- Host disconnect / room close kicks back to `MultiplayerMenu` with a message

Controllers:
- **Single:** local-only, mutates `GameLaunchData` directly
- **Host:** wraps `RelayClient`, broadcasts every change as `RoomState`
- **Join:** read-only for most fields, sends only the joiner's own slot updates

### M1 configuration

- Relay URL hard-coded to `wss://julianoschroeder.com/blocker/ws-relay`
- Hidden debug override: env var `BLOCKER_RELAY_URL` lets you point at `ws://localhost:3002/blocker/ws-relay` during development
- Fixed 1-tick input delay (adaptive is M2)

## Error handling

### Network failures

| Case | Response |
|---|---|
| Relay unreachable on connect | MultiplayerMenu shows "Cannot reach server"; Host/Join disabled; retry button |
| Socket drops in lobby | Kicked to MainMenu with toast "Lost connection to server" |
| Socket drops mid-game | Modal: "Disconnected from server. [Return to menu]"; coordinator → Ended |
| 30 s with no inbound message | Client treats as socket drop; relay pings every 10 s so this shouldn't fire on healthy links |

### Version mismatches

| Case | Detected where | Response |
|---|---|---|
| Protocol version | Relay on Hello | `Error(PROTOCOL_MISMATCH)`, socket closed |
| Simulation version | Client on RoomState | "Host is on sim v0x43, you're on v0x42 — update to play"; Start disabled |

### Desync

| Case | Response |
|---|---|
| Local client in minority | Modal: "Your game has drifted out of sync. [Save diagnostic] [Return to menu]"; writes `user://blocker/desync-{tick}.bin` containing GameState snapshot and last 20 ticks of received commands |
| No clear majority (3-way split) | All clients → Desynced, same diagnostic file |
| `DesyncReport` received by relay | One-line journal entry, no game-state action |

### Room / lobby edge cases

| Case | Response |
|---|---|
| Host disconnects during Lobby | Relay broadcasts `RoomClosed`, closes room |
| Host disconnects during Playing | Treated as normal player disconnect (tick-stamped `PlayerLeft`); game continues |
| Joiner disconnects during Lobby | Slot → Open, `RoomState` broadcast |
| Joiner picks a slot already taken (race) | `Error(SLOT_TAKEN)`, `RoomState` broadcast to refresh everyone |
| Room code collision on generate | Relay retries up to 10 times |
| Expired / nonexistent code | `Error(ROOM_NOT_FOUND)` |
| Room full | `Error(ROOM_FULL)` |

### Rate limiting

| Trigger | Response |
|---|---|
| >60 msg/s from one connection | Kick with `Error(RATE_LIMIT)` |
| Message >64 KB | Kick with `Error(MESSAGE_TOO_LARGE)` |
| >3 rooms per IP | `Error(TOO_MANY_ROOMS)` |
| Unknown message type byte | Kick with `Error(UNKNOWN_MSG_TYPE)` |

### Tick-loop edge cases

| Case | Response |
|---|---|
| Commands for a tick already processed | Drop silently (late message during catch-up) |
| Commands for a tick >50 ticks ahead | Drop and log |
| Hash for a tick we haven't processed yet | Buffer; consulted on processing |
| Hash for a tick >10 ticks in past | Drop (buffer window) |
| `GameState.Tick()` throws | Treat as desync, save diagnostic |

### The host-disconnect-mid-StartGame race

If the host clicks Start and disconnects before all `GameStarted` messages fan out, some joiners may see `GameStarted` and others may see `RoomClosed`. Fix: the relay transitions the room to `Playing` state atomically **before** sending any `GameStarted`. `Playing`-state disconnects always emit `PlayerLeft`, never `RoomClosed`. Worst case: some joiners enter the game and immediately see `PlayerLeft(host, effectiveTick=2)`, proceeding with the host pre-eliminated. That's correct behavior.

## Testing strategy

Primary loop: run `Blocker.Relay` locally, launch two Godot instances, play a match. That's the integration test and it catches everything visible.

Three small xUnit test files on top, because these specific failures are silent and waste days of debugging if they regress:

1. **`CommandSerializerTests`** — serialize → deserialize → serialize, assert byte-for-byte identical. Catches encoding nondeterminism (dictionary iteration, locale, etc.).
2. **`StateHasherTests`** — two `GameState`s with the same content in different construction order (blocks added in reverse). Assert hashes match. Catches "I hashed a `List<>` in insertion order" class of bugs.
3. **`LockstepCoordinatorTests`** — two coordinators wired through `FakeRelayClient` that echoes messages between them. Run 1000 ticks with scripted commands on both sides, assert final state hashes match. Runs in <1 s, no Godot dependency.

Total: ~200 lines of test code. Runs in `dotnet test`.

## Deployment

### Build

```bash
dotnet publish src/Blocker.Relay -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true \
    -o publish/blocker-relay
```

Produces a single `Blocker.Relay` binary (~15 MB) with no runtime dependencies.

### Droplet setup (one-time)

1. `scp publish/blocker-relay/Blocker.Relay root@209.38.176.249:/opt/blocker-relay/`
2. Install systemd unit at `/etc/systemd/system/blocker-relay.service`:

```ini
[Unit]
Description=Blocker Relay Server
After=network.target

[Service]
Type=simple
WorkingDirectory=/opt/blocker-relay
Environment=PORT=3002
ExecStart=/opt/blocker-relay/Blocker.Relay
Restart=on-failure
RestartSec=5

[Install]
WantedBy=multi-user.target
```

3. Add nginx location block to `/etc/nginx/sites-enabled/julianoschroeder.com`:

```nginx
location /blocker/ws-relay {
    proxy_pass http://127.0.0.1:3002;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 86400;
}
```

4. `systemctl daemon-reload && systemctl enable --now blocker-relay`
5. `nginx -t && systemctl reload nginx`

TLS: zero new work. The existing Let's Encrypt cert for `julianoschroeder.com` already covers the new path.

### Deploy script

`scripts/deploy-relay.sh` automates build + scp + remote restart:

```bash
#!/usr/bin/env bash
set -euo pipefail
dotnet publish src/Blocker.Relay -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true \
    -o publish/blocker-relay
scp publish/blocker-relay/Blocker.Relay root@209.38.176.249:/opt/blocker-relay/Blocker.Relay.new
ssh root@209.38.176.249 '
    mv /opt/blocker-relay/Blocker.Relay.new /opt/blocker-relay/Blocker.Relay &&
    chmod +x /opt/blocker-relay/Blocker.Relay &&
    systemctl restart blocker-relay &&
    systemctl status blocker-relay --no-pager
'
```

## Open questions and future work

- **Adaptive input delay (M2):** ship fixed 1-tick for M1. Add per-client adaptive logic in M2 after measuring real RTT distributions.
- **3–6 player UI (M2):** coordinator and relay support it from day one; UI needs the slot/color management polish.
- **Chat, surrender button, rematch (M2):** protocol reserves `0x30–0x3F`; UI and handlers added in M2.
- **Fog of war (far future):** if added, revisit architecture. Lockstep cannot protect hidden information. Options: accept maphacks, or pivot to server-authoritative. The pure-C# sim is positioned to move to a server later.
- **Reconnection (far future):** per game bible §19. Requires state snapshot + replay of missed commands on rejoin.
- **Replays (far future):** per game bible §19, §20. The command stream is already the minimal replay format; implementation is orthogonal to multiplayer.

---

*This spec is the design authority for multiplayer. When implementation diverges, update whichever is wrong — but discuss first.*
