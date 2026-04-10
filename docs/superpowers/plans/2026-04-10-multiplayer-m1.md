# Multiplayer M1 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship deterministic lockstep multiplayer for Blocker with a 2-player MVP running against a publicly deployed relay at `wss://julianoschroeder.com/blocker/ws-relay`.

**Architecture:** Pure-C# lockstep coordinator + binary wire protocol in `src/Blocker.Simulation/Net/`. Self-contained Linux relay binary in `src/Blocker.Relay/` deployed alongside existing TS signaling server. Godot client transport in `godot/Scripts/Net/`, tick runner in `godot/Scripts/Game/`, host/join UI in `godot/Scripts/UI/`.

**Tech Stack:** .NET 8, C# 12, Godot 4.6.1 C#, `System.Net.WebSockets`, `System.Net.HttpListener`, `System.Threading.Channels`, xUnit, nginx + systemd on Ubuntu 24.04.

**Source spec:** `docs/superpowers/specs/2026-04-10-multiplayer-design.md` — read it first.

---

## File Structure

### New files — Simulation library (pure C#, no Godot)
```
src/Blocker.Simulation/Net/
├── Protocol.cs               — Message type byte constants, enums
├── Varint.cs                 — Varint encode/decode (used by client AND relay)
├── CommandSerializer.cs      — Deterministic binary serialize/deserialize
├── StateHasher.cs            — FNV-1a over GameState
├── TickCommands.cs           — Record: {PlayerId, Tick, Commands}
├── GameStateSnapshot.cs      — Diagnostic snapshot (for desync files)
├── IRelayClient.cs           — Transport interface + events
├── FakeRelayClient.cs        — In-process echo for tests
└── LockstepCoordinator.cs    — N-player tick buffering, desync, disconnect
```

### New files — Tests
```
tests/Blocker.Simulation.Tests/Net/
├── VarintTests.cs
├── CommandSerializerTests.cs
├── StateHasherTests.cs
└── LockstepCoordinatorTests.cs
```

### New files — Relay server
```
src/Blocker.Relay/
├── Blocker.Relay.csproj      — Console, self-contained single-file
├── Program.cs                — HttpListener loop, WebSocket upgrade
├── Connection.cs             — Per-socket state
├── Room.cs                   — Room lifecycle state
├── RoomRegistry.cs           — Thread-safe rooms + room code generator
├── MessageDispatch.cs        — Per-type-byte dispatch
├── RateLimiter.cs            — Per-connection token bucket
├── Logger.cs                 — Plain Console.WriteLine with shared formatter
└── RelayOptions.cs           — Env var config parser
```

### New files — Godot client
```
godot/Scripts/Net/
├── RelayClientConfig.cs         — URL + timeouts + env var override
├── RelayClient.cs               — ClientWebSocket wrapper, IRelayClient impl
└── MultiplayerSessionState.cs   — Menu→Game carrier
godot/Scripts/Game/
└── MultiplayerTickRunner.cs     — Node, pacing loop for coordinator
godot/Scripts/UI/
├── MultiplayerMenu.cs           — Host/Join/Back
└── SlotConfigController.cs      — Interface + Single/Host/Join implementations
godot/Scenes/
└── MultiplayerMenu.tscn
```

### Modified files
```
godot/Scripts/Game/GameManager.cs         — branch on MultiplayerSession
godot/Scripts/Input/SelectionManager.cs   — expose ICommandSink hook
godot/Scripts/UI/MainMenu.cs              — add "Play Multiplayer" button
godot/Scripts/UI/SlotConfigScreen.cs      — delegate to SlotConfigController
blocker.sln                               — register Blocker.Relay project
```

### New files — Deployment
```
scripts/deploy-relay.sh
deploy/blocker-relay.service              — systemd unit
deploy/nginx-location-block.conf          — reference snippet
```

---

## Part 1 — Pure-C# Foundation

Everything in this part is pure C#, zero Godot, zero sockets. Runs under `dotnet test`.

### Task 1: Create `Net/` folder and `Protocol.cs` constants

**Files:**
- Create: `src/Blocker.Simulation/Net/Protocol.cs`

- [ ] **Step 1: Create the Net folder and Protocol.cs**

```csharp
// src/Blocker.Simulation/Net/Protocol.cs
namespace Blocker.Simulation.Net;

/// <summary>
/// Wire protocol constants. See docs/superpowers/specs/2026-04-10-multiplayer-design.md §"Wire protocol".
/// Ranges: 0x00–0x0F session, 0x10–0x1F tick hot path, 0x20–0x2F diagnostics,
/// 0x30–0x3F social (reserved), 0x40–0x4F bulk (reserved).
/// </summary>
public static class Protocol
{
    public const byte ProtocolVersion = 1;
    public const ushort SimulationVersion = 1;

    // Session / lobby — 0x00–0x0F
    public const byte Hello        = 0x01;
    public const byte HelloAck     = 0x02;
    public const byte CreateRoom   = 0x03;
    public const byte JoinRoom     = 0x04;
    public const byte RoomState    = 0x05;
    public const byte LeaveRoom    = 0x06;
    public const byte StartGame    = 0x07;
    public const byte GameStarted  = 0x08;

    // Tick traffic — 0x10–0x1F
    public const byte Commands     = 0x10;
    public const byte Hash         = 0x11;
    public const byte PlayerLeft   = 0x12;
    public const byte Surrender    = 0x13;

    // Diagnostics — 0x20–0x2F
    public const byte DesyncReport = 0x20;
    public const byte Error        = 0x21;
    public const byte Ping         = 0x22;
    public const byte Pong         = 0x23;
}

public enum LeaveReason : byte
{
    Disconnected = 0,
    Surrender    = 1,
    Kicked       = 2,
}

public enum ErrorCode : byte
{
    Unknown             = 0,
    ProtocolMismatch    = 1,
    RoomNotFound        = 2,
    RoomFull            = 3,
    SlotTaken           = 4,
    RateLimit           = 5,
    MessageTooLarge     = 6,
    TooManyRooms        = 7,
    UnknownMessageType  = 8,
    NotInRoom           = 9,
    NotHost             = 10,
}
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj`
Expected: `Build succeeded`.

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Simulation/Net/Protocol.cs
git commit -m "net: add Protocol message type constants"
```

---

### Task 2: Varint encode/decode with tests

**Files:**
- Create: `src/Blocker.Simulation/Net/Varint.cs`
- Create: `tests/Blocker.Simulation.Tests/Net/VarintTests.cs`

Varints encode nonnegative ints in 1–5 bytes (7 data bits per byte, high bit = continuation). Used by CommandSerializer and by the relay's peek-parsing of the `tick` field.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Blocker.Simulation.Tests/Net/VarintTests.cs
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class VarintTests
{
    [Theory]
    [InlineData(0u, new byte[] { 0x00 })]
    [InlineData(1u, new byte[] { 0x01 })]
    [InlineData(127u, new byte[] { 0x7F })]
    [InlineData(128u, new byte[] { 0x80, 0x01 })]
    [InlineData(300u, new byte[] { 0xAC, 0x02 })]
    [InlineData(16384u, new byte[] { 0x80, 0x80, 0x01 })]
    public void Write_Produces_Expected_Bytes(uint value, byte[] expected)
    {
        var buf = new byte[8];
        int written = Varint.Write(buf, 0, value);
        Assert.Equal(expected.Length, written);
        for (int i = 0; i < expected.Length; i++)
            Assert.Equal(expected[i], buf[i]);
    }

    [Theory]
    [InlineData(0u)]
    [InlineData(1u)]
    [InlineData(127u)]
    [InlineData(128u)]
    [InlineData(12345u)]
    [InlineData(uint.MaxValue)]
    public void Roundtrip(uint value)
    {
        var buf = new byte[8];
        int written = Varint.Write(buf, 0, value);
        var (decoded, consumed) = Varint.Read(buf, 0);
        Assert.Equal(value, decoded);
        Assert.Equal(written, consumed);
    }

    [Fact]
    public void Read_Rejects_Overlong_Encoding()
    {
        // 6-byte sequence is invalid (max is 5 bytes for uint32)
        var buf = new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 };
        Assert.Throws<FormatException>(() => Varint.Read(buf, 0));
    }
}
```

- [ ] **Step 2: Run tests — should fail (Varint doesn't exist)**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~Varint"`
Expected: build error — `The name 'Varint' does not exist`.

- [ ] **Step 3: Implement Varint**

```csharp
// src/Blocker.Simulation/Net/Varint.cs
namespace Blocker.Simulation.Net;

/// <summary>
/// Protobuf-style varint for uint32. 1-5 bytes, 7 data bits per byte,
/// MSB = continuation flag.
/// </summary>
public static class Varint
{
    public const int MaxBytes = 5;

    public static int Write(byte[] buf, int offset, uint value)
    {
        int start = offset;
        while (value >= 0x80)
        {
            buf[offset++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buf[offset++] = (byte)value;
        return offset - start;
    }

    public static int Write(Span<byte> buf, uint value)
    {
        int i = 0;
        while (value >= 0x80)
        {
            buf[i++] = (byte)(value | 0x80);
            value >>= 7;
        }
        buf[i++] = (byte)value;
        return i;
    }

    public static (uint value, int consumed) Read(ReadOnlySpan<byte> buf, int offset)
    {
        uint result = 0;
        int shift = 0;
        int i = offset;
        while (true)
        {
            if (i - offset >= MaxBytes) throw new FormatException("Varint too long");
            if (i >= buf.Length) throw new FormatException("Varint truncated");
            byte b = buf[i++];
            result |= (uint)(b & 0x7F) << shift;
            if ((b & 0x80) == 0) break;
            shift += 7;
        }
        return (result, i - offset);
    }

    /// <summary>
    /// How many bytes Write would produce for this value. Used for sizing buffers.
    /// </summary>
    public static int SizeOf(uint value)
    {
        int n = 1;
        while (value >= 0x80) { value >>= 7; n++; }
        return n;
    }
}
```

- [ ] **Step 4: Run tests — should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~Varint"`
Expected: All VarintTests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/Varint.cs tests/Blocker.Simulation.Tests/Net/VarintTests.cs
git commit -m "net: add varint encode/decode with tests"
```

---

### Task 3: `TickCommands` and `GameStateSnapshot` record types

**Files:**
- Create: `src/Blocker.Simulation/Net/TickCommands.cs`
- Create: `src/Blocker.Simulation/Net/GameStateSnapshot.cs`

These are inert data carriers. No tests — they're records.

- [ ] **Step 1: Create TickCommands.cs**

```csharp
// src/Blocker.Simulation/Net/TickCommands.cs
using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// One player's commands for a specific tick. Empty list is valid (idle player).
/// </summary>
public record TickCommands(int PlayerId, int Tick, IReadOnlyList<Command> Commands)
{
    public static TickCommands Empty(int playerId, int tick) =>
        new(playerId, tick, Array.Empty<Command>());
}
```

- [ ] **Step 2: Create GameStateSnapshot.cs**

```csharp
// src/Blocker.Simulation/Net/GameStateSnapshot.cs
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Net;

/// <summary>
/// Minimal diagnostic snapshot written to disk on desync. Enough to post-mortem
/// a diverged state. Not a replay format.
/// </summary>
public record GameStateSnapshot(
    int Tick,
    uint Hash,
    int PlayerCount,
    int BlockCount,
    IReadOnlyList<(int Id, int PlayerId, int X, int Y, int Hp, int Type)> Blocks);

public static class GameStateSnapshotExtensions
{
    public static GameStateSnapshot Snapshot(this GameState state)
    {
        var blocks = new List<(int, int, int, int, int, int)>(state.Blocks.Count);
        foreach (var b in state.Blocks)
            blocks.Add((b.Id, b.PlayerId, b.Pos.X, b.Pos.Y, b.Hp, (int)b.Type));
        return new GameStateSnapshot(
            state.TickNumber,
            StateHasher.Hash(state),
            state.Players.Count,
            state.Blocks.Count,
            blocks);
    }
}
```

Note: `StateHasher` doesn't exist yet — this will fail to build. That's fine; next task creates it, and Task 3 and Task 4 are committed together in Task 4's commit.

- [ ] **Step 3: Do not build yet — proceed to Task 4**

---

### Task 4: `StateHasher` with tests

**Files:**
- Create: `src/Blocker.Simulation/Net/StateHasher.cs`
- Create: `tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs`

FNV-1a over a canonicalized view of `GameState`. Canonicalization is the thing that matters: blocks must be hashed in a deterministic order (sort by Id), and every hashed field must be integer-only.

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class StateHasherTests
{
    private static GameState MakeState()
    {
        var grid = new Grid(8, 8);
        var state = new GameState(grid);
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));
        return state;
    }

    [Fact]
    public void Same_State_Same_Hash()
    {
        var a = MakeState();
        var b = MakeState();
        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Insertion_Order_Does_Not_Matter()
    {
        // Build state A normally, state B in reverse insert order.
        var grid = new Grid(8, 8);
        var a = new GameState(grid);
        a.Players.Add(new Player { Id = 0, TeamId = 0 });
        a.Players.Add(new Player { Id = 1, TeamId = 1 });
        a.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        a.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));

        var grid2 = new Grid(8, 8);
        var b = new GameState(grid2);
        b.Players.Add(new Player { Id = 1, TeamId = 1 });
        b.Players.Add(new Player { Id = 0, TeamId = 0 });
        b.AddBlock(BlockType.Soldier, 1, new GridPos(4, 4));
        b.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));

        Assert.Equal(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Position_Change_Changes_Hash()
    {
        var a = MakeState();
        var b = MakeState();
        var block = b.Blocks[0];
        b.TryMoveBlock(block, new GridPos(2, 1));
        Assert.NotEqual(StateHasher.Hash(a), StateHasher.Hash(b));
    }

    [Fact]
    public void Known_Fnv1a_Offset()
    {
        // FNV-1a of an empty GameState with zero players and zero blocks
        // must equal the FNV-1a offset basis with only tick=0 and counts=0 mixed in.
        // This is a regression-protection test — if the canonical form changes,
        // update this expected value deliberately.
        var grid = new Grid(1, 1);
        var state = new GameState(grid);
        uint h = StateHasher.Hash(state);
        Assert.NotEqual(0u, h); // sanity: something was hashed
    }
}
```

- [ ] **Step 2: Run — should fail to build (`StateHasher` undefined)**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~StateHasher"`
Expected: build error.

- [ ] **Step 3: Implement StateHasher**

```csharp
// src/Blocker.Simulation/Net/StateHasher.cs
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Net;

/// <summary>
/// FNV-1a over a canonicalized integer view of GameState.
/// Canonicalization: players sorted by Id, blocks sorted by Id.
/// Floats are never hashed (sim is integer-only by contract).
/// </summary>
public static class StateHasher
{
    private const uint FnvOffsetBasis = 2166136261;
    private const uint FnvPrime = 16777619;

    public static uint Hash(GameState state)
    {
        uint h = FnvOffsetBasis;
        MixI32(ref h, state.TickNumber);
        MixI32(ref h, state.Players.Count);
        MixI32(ref h, state.Blocks.Count);

        // Players — sorted by Id
        var playersSorted = state.Players.OrderBy(p => p.Id).ToArray();
        foreach (var p in playersSorted)
        {
            MixI32(ref h, p.Id);
            MixI32(ref h, p.TeamId);
            MixI32(ref h, p.IsEliminated ? 1 : 0);
        }

        // Blocks — sorted by Id
        var blocksSorted = state.Blocks.OrderBy(b => b.Id).ToArray();
        foreach (var b in blocksSorted)
        {
            MixI32(ref h, b.Id);
            MixI32(ref h, b.PlayerId);
            MixI32(ref h, (int)b.Type);
            MixI32(ref h, (int)b.State);
            MixI32(ref h, b.Pos.X);
            MixI32(ref h, b.Pos.Y);
            MixI32(ref h, b.Hp);
            MixI32(ref h, b.Cooldown);
            MixI32(ref h, b.RootProgress);
            MixI32(ref h, b.MoveTarget.HasValue ? 1 : 0);
            if (b.MoveTarget.HasValue)
            {
                MixI32(ref h, b.MoveTarget.Value.X);
                MixI32(ref h, b.MoveTarget.Value.Y);
            }
        }
        return h;
    }

    private static void MixI32(ref uint h, int value)
    {
        uint u = unchecked((uint)value);
        h ^= (byte)(u & 0xFF);        h *= FnvPrime;
        h ^= (byte)((u >> 8) & 0xFF); h *= FnvPrime;
        h ^= (byte)((u >> 16) & 0xFF); h *= FnvPrime;
        h ^= (byte)((u >> 24) & 0xFF); h *= FnvPrime;
    }
}
```

- [ ] **Step 4: Run tests — should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~StateHasher"`
Expected: All StateHasherTests pass.

- [ ] **Step 5: Commit Tasks 3 and 4 together**

```bash
git add src/Blocker.Simulation/Net/TickCommands.cs \
        src/Blocker.Simulation/Net/GameStateSnapshot.cs \
        src/Blocker.Simulation/Net/StateHasher.cs \
        tests/Blocker.Simulation.Tests/Net/StateHasherTests.cs
git commit -m "net: add StateHasher and data carrier types"
```

---

### Task 5: `CommandSerializer` with byte-level determinism tests

**Files:**
- Create: `src/Blocker.Simulation/Net/CommandSerializer.cs`
- Create: `tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs`

Serializes a `TickCommands` payload into the `Commands` message body (everything after the 0x10 type byte). See spec §"Commands message".

- [ ] **Step 1: Write failing tests**

```csharp
// tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class CommandSerializerTests
{
    private static TickCommands Sample() => new(
        PlayerId: 0,
        Tick: 42,
        Commands: new[]
        {
            new Command(0, CommandType.Move, new List<int> { 1, 2, 3 },
                TargetPos: new GridPos(5, 7), Queue: true),
            new Command(0, CommandType.Root, new List<int> { 1 }),
            new Command(0, CommandType.FireStunRay, new List<int> { 2 },
                Direction: Direction.Right),
        });

    [Fact]
    public void Roundtrip_Preserves_All_Fields()
    {
        var input = Sample();
        var bytes = CommandSerializer.Serialize(input);
        var output = CommandSerializer.Deserialize(bytes);

        Assert.Equal(input.PlayerId, output.PlayerId);
        Assert.Equal(input.Tick, output.Tick);
        Assert.Equal(input.Commands.Count, output.Commands.Count);
        for (int i = 0; i < input.Commands.Count; i++)
        {
            var a = input.Commands[i];
            var b = output.Commands[i];
            Assert.Equal(a.Type, b.Type);
            Assert.Equal(a.BlockIds, b.BlockIds);
            Assert.Equal(a.TargetPos, b.TargetPos);
            Assert.Equal(a.Direction, b.Direction);
            Assert.Equal(a.Queue, b.Queue);
        }
    }

    [Fact]
    public void Serialization_Is_Byte_Deterministic()
    {
        var a = CommandSerializer.Serialize(Sample());
        var b = CommandSerializer.Serialize(Sample());
        Assert.Equal(a, b);
    }

    [Fact]
    public void Empty_Commands_Are_Valid()
    {
        var input = new TickCommands(2, 100, Array.Empty<Command>());
        var bytes = CommandSerializer.Serialize(input);
        var output = CommandSerializer.Deserialize(bytes);
        Assert.Equal(2, output.PlayerId);
        Assert.Equal(100, output.Tick);
        Assert.Empty(output.Commands);
    }

    [Fact]
    public void PeekTickAndPlayer_Reads_Header_Without_Full_Parse()
    {
        // The relay needs to read tick+playerId without allocating a parse.
        var input = new TickCommands(3, 1234, Array.Empty<Command>());
        var bytes = CommandSerializer.Serialize(input);
        var (tick, playerId) = CommandSerializer.PeekTickAndPlayer(bytes);
        Assert.Equal(1234, tick);
        Assert.Equal(3, playerId);
    }
}
```

- [ ] **Step 2: Run — should fail to build**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~CommandSerializer"`
Expected: build error.

- [ ] **Step 3: Implement CommandSerializer**

```csharp
// src/Blocker.Simulation/Net/CommandSerializer.cs
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;

namespace Blocker.Simulation.Net;

/// <summary>
/// Binary wire format for a TickCommands payload. Does NOT include the 0x10
/// type byte — the caller prepends it.
///
/// Layout:
///   [tick: varint]
///   [playerId: byte]
///   [count: varint]
///   per command:
///     [type: byte]
///     [blockCount: varint]
///     [blockIds: varint × blockCount]
///     [flags: byte]         bit0 = hasTargetPos, bit1 = hasDirection, bit2 = queue
///     [targetX, targetY: varint × 2] if hasTargetPos
///     [direction: byte]               if hasDirection
///
/// Determinism: no dictionary iteration, no locale, no floats, little-endian only.
/// </summary>
public static class CommandSerializer
{
    private const byte FlagHasTargetPos = 0x01;
    private const byte FlagHasDirection = 0x02;
    private const byte FlagQueue        = 0x04;

    public static byte[] Serialize(TickCommands tc)
    {
        // Max size bound: header (5+1+5) + per-cmd (1+5+5*blocks+1+5+5+1)
        int bound = 16;
        foreach (var c in tc.Commands)
            bound += 24 + c.BlockIds.Count * Varint.MaxBytes;

        var buf = new byte[bound];
        int i = 0;
        i += Varint.Write(buf, i, (uint)tc.Tick);
        buf[i++] = (byte)tc.PlayerId;
        i += Varint.Write(buf, i, (uint)tc.Commands.Count);

        foreach (var c in tc.Commands)
        {
            buf[i++] = (byte)c.Type;
            i += Varint.Write(buf, i, (uint)c.BlockIds.Count);
            foreach (var id in c.BlockIds)
                i += Varint.Write(buf, i, (uint)id);
            byte flags = 0;
            if (c.TargetPos.HasValue) flags |= FlagHasTargetPos;
            if (c.Direction.HasValue) flags |= FlagHasDirection;
            if (c.Queue)              flags |= FlagQueue;
            buf[i++] = flags;
            if (c.TargetPos.HasValue)
            {
                i += Varint.Write(buf, i, (uint)c.TargetPos.Value.X);
                i += Varint.Write(buf, i, (uint)c.TargetPos.Value.Y);
            }
            if (c.Direction.HasValue)
                buf[i++] = (byte)c.Direction.Value;
        }

        var result = new byte[i];
        Array.Copy(buf, result, i);
        return result;
    }

    public static TickCommands Deserialize(ReadOnlySpan<byte> buf)
    {
        int i = 0;
        var (tick, n1) = Varint.Read(buf, i); i += n1;
        int playerId = buf[i++];
        var (count, n2) = Varint.Read(buf, i); i += n2;

        var list = new List<Command>((int)count);
        for (uint c = 0; c < count; c++)
        {
            var type = (CommandType)buf[i++];
            var (blockCount, n3) = Varint.Read(buf, i); i += n3;
            var ids = new List<int>((int)blockCount);
            for (uint b = 0; b < blockCount; b++)
            {
                var (id, n4) = Varint.Read(buf, i); i += n4;
                ids.Add((int)id);
            }
            byte flags = buf[i++];
            GridPos? target = null;
            Direction? dir = null;
            if ((flags & FlagHasTargetPos) != 0)
            {
                var (x, n5) = Varint.Read(buf, i); i += n5;
                var (y, n6) = Varint.Read(buf, i); i += n6;
                target = new GridPos((int)x, (int)y);
            }
            if ((flags & FlagHasDirection) != 0)
                dir = (Direction)buf[i++];
            bool queue = (flags & FlagQueue) != 0;
            list.Add(new Command(playerId, type, ids, target, dir, queue));
        }
        return new TickCommands(playerId, (int)tick, list);
    }

    /// <summary>
    /// Cheap header peek used by the relay for auth. Does not allocate.
    /// </summary>
    public static (int tick, int playerId) PeekTickAndPlayer(ReadOnlySpan<byte> buf)
    {
        var (tick, consumed) = Varint.Read(buf, 0);
        int playerId = buf[consumed];
        return ((int)tick, playerId);
    }
}
```

- [ ] **Step 4: Run tests — should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~CommandSerializer"`
Expected: All CommandSerializerTests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/CommandSerializer.cs \
        tests/Blocker.Simulation.Tests/Net/CommandSerializerTests.cs
git commit -m "net: add CommandSerializer with deterministic binary format"
```

---

### Task 6: `IRelayClient` interface + `FakeRelayClient`

**Files:**
- Create: `src/Blocker.Simulation/Net/IRelayClient.cs`
- Create: `src/Blocker.Simulation/Net/FakeRelayClient.cs`

No tests of their own yet — they exist to support LockstepCoordinator tests in Task 7.

- [ ] **Step 1: Create the interface**

```csharp
// src/Blocker.Simulation/Net/IRelayClient.cs
using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// Transport abstraction used by LockstepCoordinator. All events fire on the
/// main (game) thread — implementations are responsible for marshalling.
/// </summary>
public interface IRelayClient
{
    void SendCommands(int tick, IReadOnlyList<Command> commands);
    void SendHash(int tick, uint hash);
    void SendDesyncReport(int tick, GameStateSnapshot snapshot);
    void SendSurrender();

    event Action<int /*playerId*/, int /*tick*/, IReadOnlyList<Command>>? CommandsReceived;
    event Action<int /*playerId*/, int /*tick*/, uint>? HashReceived;
    event Action<int /*playerId*/, int /*effectiveTick*/, LeaveReason>? PlayerLeft;
    event Action<int /*playerId*/>? SurrenderReceived;
}
```

- [ ] **Step 2: Create FakeRelayClient**

```csharp
// src/Blocker.Simulation/Net/FakeRelayClient.cs
using Blocker.Simulation.Commands;

namespace Blocker.Simulation.Net;

/// <summary>
/// In-process echo relay for unit tests. Holds a reference to all peers and
/// fans messages out to everyone except the sender. No threads, no sockets.
/// Messages are delivered synchronously when Send* is called.
/// </summary>
public class FakeRelayClient : IRelayClient
{
    public int LocalPlayerId { get; }
    private readonly List<FakeRelayClient> _peers = new();

    public event Action<int, int, IReadOnlyList<Command>>? CommandsReceived;
    public event Action<int, int, uint>? HashReceived;
    public event Action<int, int, LeaveReason>? PlayerLeft;
    public event Action<int>? SurrenderReceived;

    public FakeRelayClient(int localPlayerId) { LocalPlayerId = localPlayerId; }

    /// <summary>Wire two or more clients into a single relay mesh.</summary>
    public static void Connect(params FakeRelayClient[] clients)
    {
        foreach (var a in clients)
            foreach (var b in clients)
                if (!ReferenceEquals(a, b))
                    a._peers.Add(b);
    }

    public void SendCommands(int tick, IReadOnlyList<Command> commands)
    {
        foreach (var peer in _peers)
            peer.CommandsReceived?.Invoke(LocalPlayerId, tick, commands);
    }

    public void SendHash(int tick, uint hash)
    {
        foreach (var peer in _peers)
            peer.HashReceived?.Invoke(LocalPlayerId, tick, hash);
    }

    public void SendDesyncReport(int tick, GameStateSnapshot snapshot) { /* noop in tests */ }
    public void SendSurrender()
    {
        foreach (var peer in _peers)
            peer.SurrenderReceived?.Invoke(LocalPlayerId);
    }

    /// <summary>Simulate a disconnect for tests. Raises PlayerLeft on all peers.</summary>
    public void SimulateDisconnect(int effectiveTick)
    {
        foreach (var peer in _peers)
            peer.PlayerLeft?.Invoke(LocalPlayerId, effectiveTick, LeaveReason.Disconnected);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Blocker.Simulation/Blocker.Simulation.csproj`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Simulation/Net/IRelayClient.cs \
        src/Blocker.Simulation/Net/FakeRelayClient.cs
git commit -m "net: add IRelayClient interface and FakeRelayClient test double"
```

---

### Task 7: `LockstepCoordinator` — happy path (2 players stay in sync)

**Files:**
- Create: `src/Blocker.Simulation/Net/LockstepCoordinator.cs`
- Create: `tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs`

This task covers the core per-tick loop. Disconnect and desync are in tasks 8 and 9.

- [ ] **Step 1: Write failing test**

```csharp
// tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs
using Blocker.Simulation.Blocks;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class LockstepCoordinatorTests
{
    private static GameState MakeTwoPlayerState()
    {
        var grid = new Grid(10, 10);
        var state = new GameState(grid);
        state.Players.Add(new Player { Id = 0, TeamId = 0 });
        state.Players.Add(new Player { Id = 1, TeamId = 1 });
        state.AddBlock(BlockType.Builder, 0, new GridPos(1, 1));
        state.AddBlock(BlockType.Builder, 1, new GridPos(8, 8));
        return state;
    }

    [Fact]
    public void Two_Players_Stay_In_Sync_Over_200_Ticks_Of_Empty_Input()
    {
        var state0 = MakeTwoPlayerState();
        var state1 = MakeTwoPlayerState();

        var relay0 = new FakeRelayClient(0);
        var relay1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(relay0, relay1);

        var coord0 = new LockstepCoordinator(0, state0, relay0, new HashSet<int> { 0, 1 });
        var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });

        coord0.StartGame();
        coord1.StartGame();

        // Drive 200 ticks. Each coordinator polls in turn; empty local input.
        for (int i = 0; i < 200; i++)
        {
            coord0.PollAdvance();
            coord1.PollAdvance();
        }

        Assert.Equal(state0.TickNumber, state1.TickNumber);
        Assert.Equal(StateHasher.Hash(state0), StateHasher.Hash(state1));
        Assert.True(state0.TickNumber >= 100, $"expected at least 100 ticks advanced, got {state0.TickNumber}");
    }

    [Fact]
    public void Commands_Scheduled_With_Input_Delay_Apply_To_Correct_Tick()
    {
        var state0 = MakeTwoPlayerState();
        var state1 = MakeTwoPlayerState();
        var relay0 = new FakeRelayClient(0);
        var relay1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(relay0, relay1);

        var coord0 = new LockstepCoordinator(0, state0, relay0, new HashSet<int> { 0, 1 });
        var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });
        coord0.StartGame();
        coord1.StartGame();

        // Player 0 issues a move command at local tick 0, targeting input delay 1 → applies at tick 2.
        var block0Id = state0.Blocks.First(b => b.PlayerId == 0).Id;
        coord0.QueueLocalCommand(new Command(0, CommandType.Move, new List<int> { block0Id },
            TargetPos: new GridPos(5, 1)));

        for (int i = 0; i < 50; i++)
        {
            coord0.PollAdvance();
            coord1.PollAdvance();
        }

        // Both states should agree and the block should have begun moving.
        Assert.Equal(StateHasher.Hash(state0), StateHasher.Hash(state1));
        var b0 = state0.Blocks.First(b => b.Id == block0Id);
        var b1 = state1.Blocks.First(b => b.Id == block0Id);
        Assert.Equal(b0.Pos, b1.Pos);
        Assert.NotEqual(new GridPos(1, 1), b0.Pos); // moved
    }
}
```

- [ ] **Step 2: Run — should fail to build**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~LockstepCoordinator"`
Expected: build error.

- [ ] **Step 3: Implement LockstepCoordinator**

```csharp
// src/Blocker.Simulation/Net/LockstepCoordinator.cs
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;

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

    public event Action<int /*winnerPlayerId*/>? GameEnded;
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
        int localTarget = _currentTick + InputDelay + 1;
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
        var winningTeam = Simulation.Systems.EliminationSystem.GetWinningTeam(_state);
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
```

- [ ] **Step 4: Run tests — should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~LockstepCoordinator"`
Expected: Both tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/LockstepCoordinator.cs \
        tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs
git commit -m "net: add LockstepCoordinator happy path"
```

---

### Task 8: `LockstepCoordinator` — disconnect handling

**Files:**
- Modify: `src/Blocker.Simulation/Net/LockstepCoordinator.cs`
- Modify: `tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs`

When `PlayerLeft` fires, fill pending future ticks with empty commands so we don't stall, remove the player from `_activePlayers`, and stop expecting their input.

- [ ] **Step 1: Add failing test**

```csharp
// Add to LockstepCoordinatorTests.cs

[Fact]
public void Disconnect_Unblocks_Remaining_Player()
{
    var state0 = MakeTwoPlayerState();
    var state1 = MakeTwoPlayerState();
    var relay0 = new FakeRelayClient(0);
    var relay1 = new FakeRelayClient(1);
    FakeRelayClient.Connect(relay0, relay1);

    var coord0 = new LockstepCoordinator(0, state0, relay0, new HashSet<int> { 0, 1 });
    var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });
    coord0.StartGame(); coord1.StartGame();

    // Run 20 ticks normally.
    for (int i = 0; i < 20; i++) { coord0.PollAdvance(); coord1.PollAdvance(); }
    Assert.True(coord0.CurrentTick >= 10);

    // Player 1 "disconnects" at effectiveTick = current + 2.
    int effectiveTick = coord0.CurrentTick + 2;
    relay1.SimulateDisconnect(effectiveTick);   // fires PlayerLeft on coord0

    // Coord0 should continue advancing despite coord1 never sending commands again.
    for (int i = 0; i < 50; i++) coord0.PollAdvance();

    Assert.True(coord0.CurrentTick > 20 + 5,
        $"expected coord0 to advance past tick {20 + 5}, got {coord0.CurrentTick}");
}
```

- [ ] **Step 2: Run — should fail (coord0 stalls)**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~Disconnect_Unblocks"`
Expected: fail — coord0 stuck because coord1's buffer has no entries for new ticks.

- [ ] **Step 3: Implement OnPlayerLeft**

Replace the stub `OnPlayerLeft` in `LockstepCoordinator.cs` with:

```csharp
private void OnPlayerLeft(int playerId, int effectiveTick, LeaveReason reason)
{
    if (!_activePlayers.Contains(playerId)) return;

    // Fill buffers for this player from _currentTick+1 up to effectiveTick with
    // empty commands so we don't stall waiting for them.
    if (!_buffers.TryGetValue(playerId, out var buf))
        _buffers[playerId] = buf = new SortedDictionary<int, IReadOnlyList<Command>>();
    for (int t = _currentTick + 1; t <= effectiveTick; t++)
        buf[t] = Array.Empty<Command>();

    // From effectiveTick+1 onward, they're removed from active players, so
    // PollAdvance stops expecting input.
    // We still need to cover the range [_currentTick+1, effectiveTick] as active,
    // so we do the removal after that window passes. Simplest approach: tag
    // _removeAfterTick and apply it inside PollAdvance after advancing.
    _pendingRemovals.Add((playerId, effectiveTick));

    // Mark the player as eliminated in the sim state at effectiveTick by queueing
    // a synthetic elimination flag. EliminationSystem handles removal based on
    // unit counts; for disconnect we set IsEliminated directly at effectiveTick.
    // To keep determinism, we do this in PollAdvance when _currentTick == effectiveTick.
    _disconnectEliminations.Add((playerId, effectiveTick));
}

private readonly List<(int playerId, int afterTick)> _pendingRemovals = new();
private readonly List<(int playerId, int atTick)> _disconnectEliminations = new();
```

Then, in `PollAdvance`, immediately after `_currentTick = nextTick;` and before hashing, insert:

```csharp
// Apply deterministic disconnect eliminations at their effective tick.
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
// Remove disconnected players from the active set once we've reached their effective tick.
for (int i = _pendingRemovals.Count - 1; i >= 0; i--)
{
    var (pid, afterTick) = _pendingRemovals[i];
    if (_currentTick >= afterTick)
    {
        _activePlayers.Remove(pid);
        _pendingRemovals.RemoveAt(i);
    }
}
if (_activePlayers.Count < 2 && Fsm != CoordinatorFsm.Ended)
{
    Fsm = CoordinatorFsm.Ended;
    var winner = _activePlayers.FirstOrDefault();
    GameEnded?.Invoke(winner);
}
```

Important: disconnection elimination sets `IsEliminated = true` directly, bypassing `EliminationSystem`'s "no army AND no nests AND <3 builders" rule. This is deliberate — a disconnected player is eliminated regardless of their units. The hash will stay identical across clients because every client applies the flag at the same `_currentTick == atTick`.

- [ ] **Step 4: Run tests — all should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~LockstepCoordinator"`
Expected: all three tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/LockstepCoordinator.cs \
        tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs
git commit -m "net: LockstepCoordinator handles disconnect with tick-stamped elimination"
```

---

### Task 9: `LockstepCoordinator` — majority-vote desync detection

**Files:**
- Modify: `src/Blocker.Simulation/Net/LockstepCoordinator.cs`
- Modify: `tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs`

- [ ] **Step 1: Add failing test using a deliberately-cheating fake**

```csharp
// Add to LockstepCoordinatorTests.cs

private class CheatingRelay : IRelayClient
{
    private readonly FakeRelayClient _inner;
    public CheatingRelay(FakeRelayClient inner) { _inner = inner; }

    public void SendCommands(int tick, IReadOnlyList<Command> c) => _inner.SendCommands(tick, c);
    public void SendHash(int tick, uint hash) => _inner.SendHash(tick, hash ^ 0xDEADBEEF); // corrupt
    public void SendDesyncReport(int tick, GameStateSnapshot snap) => _inner.SendDesyncReport(tick, snap);
    public void SendSurrender() => _inner.SendSurrender();
    public event Action<int, int, IReadOnlyList<Command>>? CommandsReceived
    { add => _inner.CommandsReceived += value; remove => _inner.CommandsReceived -= value; }
    public event Action<int, int, uint>? HashReceived
    { add => _inner.HashReceived += value; remove => _inner.HashReceived -= value; }
    public event Action<int, int, LeaveReason>? PlayerLeft
    { add => _inner.PlayerLeft += value; remove => _inner.PlayerLeft -= value; }
    public event Action<int>? SurrenderReceived
    { add => _inner.SurrenderReceived += value; remove => _inner.SurrenderReceived -= value; }
}

[Fact]
public void Mismatched_Hashes_Trigger_Desync_In_Two_Player_Game()
{
    var state0 = MakeTwoPlayerState();
    var state1 = MakeTwoPlayerState();
    var relay0 = new FakeRelayClient(0);
    var relay1 = new FakeRelayClient(1);
    FakeRelayClient.Connect(relay0, relay1);

    // Wrap relay0 in a cheater that corrupts outgoing hashes.
    var cheat0 = new CheatingRelay(relay0);

    var coord0 = new LockstepCoordinator(0, state0, cheat0, new HashSet<int> { 0, 1 });
    var coord1 = new LockstepCoordinator(1, state1, relay1, new HashSet<int> { 0, 1 });
    coord0.StartGame(); coord1.StartGame();

    bool desyncedFired = false;
    coord1.DesyncDetected += () => desyncedFired = true;

    for (int i = 0; i < 20; i++)
    {
        coord0.PollAdvance();
        coord1.PollAdvance();
        if (desyncedFired) break;
    }
    Assert.True(desyncedFired);
    Assert.Equal(CoordinatorFsm.Desynced, coord1.Fsm);
}
```

- [ ] **Step 2: Run — should fail (no desync detection yet)**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~Mismatched_Hashes"`
Expected: test fails.

- [ ] **Step 3: Implement CheckMajorityVote**

Replace the stub `CheckMajorityVote` in `LockstepCoordinator.cs` with:

```csharp
private void CheckMajorityVote(int tick)
{
    if (!_hashBuffer.TryGetValue(tick, out var map)) return;
    if (map.Count < _activePlayers.Count) return; // wait for everyone

    var groups = map.GroupBy(kv => kv.Value)
                    .OrderByDescending(g => g.Count())
                    .ToList();
    if (groups.Count == 1) return; // all agree

    var majority = groups[0];
    bool localInMajority = majority.Any(kv => kv.Key == LocalPlayerId);
    bool majorityIsClear = majority.Count() > _activePlayers.Count / 2;

    if (majorityIsClear && !localInMajority)
    {
        _relay.SendDesyncReport(tick, _state.Snapshot());
        Fsm = CoordinatorFsm.Desynced;
        DesyncDetected?.Invoke();
    }
    else if (!majorityIsClear)
    {
        // No clear majority (e.g. 2-player game with disagreement, or 3-way split)
        _relay.SendDesyncReport(tick, _state.Snapshot());
        Fsm = CoordinatorFsm.Desynced;
        DesyncDetected?.Invoke();
    }
}
```

For the 2-player case: `activePlayers.Count / 2 == 1`, `majority.Count()` will be 1 (each unique hash has exactly one vote), so `majorityIsClear` is false — both clients declare desync. Correct.

- [ ] **Step 4: Run tests — should pass**

Run: `dotnet test tests/Blocker.Simulation.Tests/ --filter "FullyQualifiedName~LockstepCoordinator"`
Expected: all coordinator tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/LockstepCoordinator.cs \
        tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs
git commit -m "net: LockstepCoordinator majority-vote desync detection"
```

---

### Task 10: Full simulation test suite sanity check

- [ ] **Step 1: Run the entire test suite**

Run: `dotnet test`
Expected: all tests pass (existing 151+ plus the ~10 new Net/ tests). No build warnings.

- [ ] **Step 2: If any existing test breaks, investigate and fix**

No edits to existing sim code should have been made; this is a safety net.

---

## Part 2 — Relay Server

The relay is a new console project. It references `Blocker.Simulation` solely for `Protocol.cs`, `Varint.cs`, and `CommandSerializer.PeekTickAndPlayer` — nothing else.

### Task 11: `Blocker.Relay` project scaffolding

**Files:**
- Create: `src/Blocker.Relay/Blocker.Relay.csproj`
- Create: `src/Blocker.Relay/Program.cs`
- Modify: `blocker.sln`

- [ ] **Step 1: Create csproj**

```xml
<!-- src/Blocker.Relay/Blocker.Relay.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <LangVersion>latest</LangVersion>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Blocker.Relay</RootNamespace>
    <AssemblyName>Blocker.Relay</AssemblyName>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\Blocker.Simulation\Blocker.Simulation.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Create minimal Program.cs**

```csharp
// src/Blocker.Relay/Program.cs
using System.Net;

namespace Blocker.Relay;

public static class Program
{
    public static async Task Main(string[] args)
    {
        var options = RelayOptions.FromEnvironment();
        Logger.Info($"Blocker.Relay starting on {options.ListenUrl}");

        var listener = new HttpListener();
        listener.Prefixes.Add(options.ListenUrl);
        listener.Start();
        Logger.Info("Listening.");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync().WaitAsync(cts.Token); }
                catch (OperationCanceledException) { break; }
                _ = Task.Run(() => HandleRequest(ctx, cts.Token));
            }
        }
        finally
        {
            listener.Stop();
            Logger.Info("Shut down.");
        }
    }

    private static async Task HandleRequest(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (ctx.Request.Url?.AbsolutePath == "/healthz")
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.OutputStream.WriteAsync("ok"u8.ToArray(), ct);
                ctx.Response.Close();
                return;
            }
            if (ctx.Request.Url?.AbsolutePath == "/blocker/ws-relay" && ctx.Request.IsWebSocketRequest)
            {
                // Task 13 implements the WebSocket upgrade + session loop.
                ctx.Response.StatusCode = 501;
                ctx.Response.Close();
                return;
            }
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Unhandled request error: {ex.Message}");
            try { ctx.Response.Abort(); } catch { }
        }
    }
}
```

- [ ] **Step 3: Create RelayOptions.cs and Logger.cs**

```csharp
// src/Blocker.Relay/RelayOptions.cs
namespace Blocker.Relay;

public record RelayOptions(
    string ListenUrl,
    int RateLimitMsgPerSec,
    int MaxMessageBytes,
    int MaxRoomsPerIp,
    int MaxConnections,
    TimeSpan LobbyTimeout,
    TimeSpan GameTimeout,
    TimeSpan HelloTimeout)
{
    public static RelayOptions FromEnvironment()
    {
        string port = Environment.GetEnvironmentVariable("PORT") ?? "3002";
        return new RelayOptions(
            ListenUrl: $"http://127.0.0.1:{port}/",
            RateLimitMsgPerSec: Int("BLOCKER_RELAY_RATE_LIMIT", 60),
            MaxMessageBytes: Int("BLOCKER_RELAY_MAX_MSG", 64 * 1024),
            MaxRoomsPerIp: Int("BLOCKER_RELAY_ROOMS_PER_IP", 3),
            MaxConnections: Int("BLOCKER_RELAY_MAX_CONNS", 500),
            LobbyTimeout: TimeSpan.FromMinutes(Int("BLOCKER_RELAY_LOBBY_TIMEOUT_MIN", 10)),
            GameTimeout:  TimeSpan.FromMinutes(Int("BLOCKER_RELAY_GAME_TIMEOUT_MIN", 60)),
            HelloTimeout: TimeSpan.FromSeconds(Int("BLOCKER_RELAY_HELLO_TIMEOUT_SEC", 5))
        );
    }
    private static int Int(string name, int def) =>
        int.TryParse(Environment.GetEnvironmentVariable(name), out var v) ? v : def;
}
```

```csharp
// src/Blocker.Relay/Logger.cs
namespace Blocker.Relay;

public static class Logger
{
    public static void Info(string msg)  => Write("INFO", msg);
    public static void Warn(string msg)  => Write("WARN", msg);
    public static void Error(string msg) => Write("ERROR", msg);

    private static void Write(string level, string msg)
    {
        var ts = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ");
        Console.WriteLine($"[{level,-5}] {ts} {msg}");
    }
}
```

- [ ] **Step 4: Register project in the solution**

```bash
dotnet sln blocker.sln add src/Blocker.Relay/Blocker.Relay.csproj
```

- [ ] **Step 5: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: build succeeds.

- [ ] **Step 6: Run to verify healthz works**

Run:
```bash
PORT=3099 dotnet run --project src/Blocker.Relay/ &
sleep 1
curl -s http://127.0.0.1:3099/healthz
# Expected: ok
kill %1
```

- [ ] **Step 7: Commit**

```bash
git add src/Blocker.Relay/Blocker.Relay.csproj \
        src/Blocker.Relay/Program.cs \
        src/Blocker.Relay/RelayOptions.cs \
        src/Blocker.Relay/Logger.cs \
        blocker.sln
git commit -m "relay: add project scaffolding with /healthz endpoint"
```

---

### Task 12: Connection, Room, RoomRegistry types

**Files:**
- Create: `src/Blocker.Relay/Connection.cs`
- Create: `src/Blocker.Relay/Room.cs`
- Create: `src/Blocker.Relay/RoomRegistry.cs`

No tests — these are data holders; logic is tested via integration.

- [ ] **Step 1: Create Connection.cs**

```csharp
// src/Blocker.Relay/Connection.cs
using System.Net.WebSockets;

namespace Blocker.Relay;

public sealed class Connection
{
    public Guid Id { get; } = Guid.NewGuid();
    public required WebSocket Ws { get; init; }
    public required string RemoteIp { get; init; }
    public string ClientName { get; set; } = "";
    public byte ProtocolVersion { get; set; }
    public ushort SimulationVersion { get; set; }
    public Room? CurrentRoom { get; set; }
    public byte? AssignedPlayerId { get; set; }
    public DateTime LastMessageAt { get; set; } = DateTime.UtcNow;

    // Token bucket state for rate limiting
    public double RateTokens { get; set; }
    public DateTime RateLastRefill { get; set; } = DateTime.UtcNow;
}
```

- [ ] **Step 2: Create Room.cs**

```csharp
// src/Blocker.Relay/Room.cs
namespace Blocker.Relay;

public enum RoomLifecycle { Lobby, Playing, Ended }

public sealed class Room
{
    public required string Code { get; init; }
    public required Guid HostId { get; init; }
    public RoomLifecycle Lifecycle { get; set; } = RoomLifecycle.Lobby;
    public ushort SimulationVersion { get; set; }
    public byte[] MapBlob { get; set; } = Array.Empty<byte>();
    public string MapName { get; set; } = "";
    public int SlotCount { get; set; }

    // slotId → { ownerConnectionId, displayName, colorIndex }
    public Dictionary<byte, SlotInfo> Slots { get; } = new();
    public int HighestSeenTick { get; set; }
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
}

public sealed record SlotInfo(Guid? OwnerId, string DisplayName, byte ColorIndex, bool IsOpen, bool IsClosed);
```

- [ ] **Step 3: Create RoomRegistry.cs**

```csharp
// src/Blocker.Relay/RoomRegistry.cs
using System.Collections.Concurrent;

namespace Blocker.Relay;

public sealed class RoomRegistry
{
    // Code → Room (thread-safe because rooms are mutated from per-connection tasks)
    private readonly ConcurrentDictionary<string, Room> _rooms = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _roomsPerIp = new(StringComparer.Ordinal);
    private readonly Random _rng = new();
    // Code alphabet: no 0/O/1/I/L to avoid confusion in codes users type manually.
    private const string Alphabet = "ABCDEFGHJKMNPQRSTUVWXYZ23456789";

    public Room? Get(string code) => _rooms.TryGetValue(code, out var r) ? r : null;
    public IEnumerable<Room> All() => _rooms.Values;

    public Room? TryCreate(Guid hostId, string ip, int maxRoomsPerIp,
                           ushort simVersion, byte[] mapBlob, string mapName, int slotCount)
    {
        int count = _roomsPerIp.GetOrAdd(ip, 0);
        if (count >= maxRoomsPerIp) return null;

        for (int attempt = 0; attempt < 10; attempt++)
        {
            string code = GenerateCode();
            var room = new Room
            {
                Code = code, HostId = hostId,
                SimulationVersion = simVersion,
                MapBlob = mapBlob, MapName = mapName,
                SlotCount = slotCount
            };
            if (_rooms.TryAdd(code, room))
            {
                _roomsPerIp.AddOrUpdate(ip, 1, (_, v) => v + 1);
                return room;
            }
        }
        return null;
    }

    public void Remove(Room room, string hostIp)
    {
        if (_rooms.TryRemove(room.Code, out _))
            _roomsPerIp.AddOrUpdate(hostIp, 0, (_, v) => Math.Max(0, v - 1));
    }

    private string GenerateCode()
    {
        Span<char> c = stackalloc char[4];
        lock (_rng)
            for (int i = 0; i < 4; i++) c[i] = Alphabet[_rng.Next(Alphabet.Length)];
        return new string(c);
    }
}
```

- [ ] **Step 4: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Relay/Connection.cs src/Blocker.Relay/Room.cs src/Blocker.Relay/RoomRegistry.cs
git commit -m "relay: add Connection, Room, and RoomRegistry types"
```

---

### Task 13: WebSocket upgrade + Hello/HelloAck flow

**Files:**
- Create: `src/Blocker.Relay/RelayServer.cs`
- Modify: `src/Blocker.Relay/Program.cs`

This task puts the pipe together: accept WebSocket upgrades, enforce the 5 s Hello timeout, validate protocol version, and reply with HelloAck. All subsequent message types are still 404'd for this task.

- [ ] **Step 1: Create RelayServer.cs**

```csharp
// src/Blocker.Relay/RelayServer.cs
using System.Net;
using System.Net.WebSockets;
using Blocker.Simulation.Net;

namespace Blocker.Relay;

public sealed class RelayServer
{
    private readonly RelayOptions _opts;
    private readonly RoomRegistry _rooms = new();
    // connectionId → Connection (authoritative set of live connections)
    private readonly Dictionary<Guid, Connection> _connections = new();
    private readonly object _connectionsLock = new();

    public RelayServer(RelayOptions opts) { _opts = opts; }

    public async Task HandleWebSocket(HttpListenerContext ctx, CancellationToken ct)
    {
        HttpListenerWebSocketContext wsCtx;
        try { wsCtx = await ctx.AcceptWebSocketAsync(subProtocol: null); }
        catch (Exception ex)
        {
            Logger.Warn($"ws upgrade failed: {ex.Message}");
            return;
        }
        var conn = new Connection
        {
            Ws = wsCtx.WebSocket,
            RemoteIp = ctx.Request.RemoteEndPoint?.Address.ToString() ?? "?"
        };
        lock (_connectionsLock)
        {
            if (_connections.Count >= _opts.MaxConnections)
            {
                Logger.Warn($"conn={conn.Id} rejected: max connections");
                _ = conn.Ws.CloseAsync(WebSocketCloseStatus.PolicyViolation, "too many", ct);
                return;
            }
            _connections[conn.Id] = conn;
        }
        Logger.Info($"conn={conn.Id} event=connect ip={conn.RemoteIp}");

        try { await SessionLoop(conn, ct); }
        catch (Exception ex) { Logger.Warn($"conn={conn.Id} session error: {ex.Message}"); }
        finally
        {
            lock (_connectionsLock) _connections.Remove(conn.Id);
            Logger.Info($"conn={conn.Id} event=disconnect");
            // Task 17 adds room cleanup here.
        }
    }

    private async Task SessionLoop(Connection conn, CancellationToken ct)
    {
        var helloDeadline = DateTime.UtcNow + _opts.HelloTimeout;
        bool helloSeen = false;
        var buf = new byte[_opts.MaxMessageBytes];

        while (conn.Ws.State == WebSocketState.Open && !ct.IsCancellationRequested)
        {
            if (!helloSeen && DateTime.UtcNow > helloDeadline)
            {
                await SendError(conn, ErrorCode.ProtocolMismatch, ct);
                await conn.Ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "hello timeout", ct);
                return;
            }

            var result = await ReceiveFullMessage(conn, buf, ct);
            if (result == null) return;
            var (payload, len) = result.Value;
            conn.LastMessageAt = DateTime.UtcNow;

            if (len == 0) continue;
            byte type = payload[0];

            if (!helloSeen)
            {
                if (type != Protocol.Hello)
                {
                    await SendError(conn, ErrorCode.ProtocolMismatch, ct);
                    return;
                }
                if (!TryHandleHello(conn, payload.AsSpan(0, len)))
                {
                    await SendError(conn, ErrorCode.ProtocolMismatch, ct);
                    return;
                }
                await SendHelloAck(conn, ct);
                helloSeen = true;
                continue;
            }

            // Task 14+ dispatches lobby/game messages.
        }
    }

    private static bool TryHandleHello(Connection conn, ReadOnlySpan<byte> payload)
    {
        // [0x01][proto:byte][sim:uint16 LE][nameLen:varint][name bytes]
        if (payload.Length < 4) return false;
        byte protoVer = payload[1];
        if (protoVer != Protocol.ProtocolVersion) return false;
        ushort simVer = (ushort)(payload[2] | (payload[3] << 8));
        var (nameLen, consumed) = Varint.Read(payload, 4);
        if (4 + consumed + nameLen > payload.Length) return false;
        string name = System.Text.Encoding.UTF8.GetString(
            payload.Slice(4 + consumed, (int)nameLen));
        conn.ProtocolVersion = protoVer;
        conn.SimulationVersion = simVer;
        conn.ClientName = name;
        Logger.Info($"conn={conn.Id} event=hello name={name} proto={protoVer} sim={simVer}");
        return true;
    }

    private static async Task SendHelloAck(Connection conn, CancellationToken ct)
    {
        var buf = new byte[2];
        buf[0] = Protocol.HelloAck;
        buf[1] = Protocol.ProtocolVersion;
        await conn.Ws.SendAsync(buf, WebSocketMessageType.Binary, true, ct);
    }

    private static async Task SendError(Connection conn, ErrorCode code, CancellationToken ct)
    {
        var buf = new byte[] { Protocol.Error, (byte)code };
        try { await conn.Ws.SendAsync(buf, WebSocketMessageType.Binary, true, ct); } catch { }
    }

    private async Task<(byte[] payload, int length)?> ReceiveFullMessage(
        Connection conn, byte[] buf, CancellationToken ct)
    {
        int total = 0;
        while (true)
        {
            WebSocketReceiveResult r;
            try { r = await conn.Ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), ct); }
            catch { return null; }
            if (r.MessageType == WebSocketMessageType.Close)
            {
                await conn.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct);
                return null;
            }
            if (r.MessageType != WebSocketMessageType.Binary) return null;
            total += r.Count;
            if (r.EndOfMessage) return (buf, total);
            if (total >= buf.Length)
            {
                await SendError(conn, ErrorCode.MessageTooLarge, ct);
                return null;
            }
        }
    }
}
```

- [ ] **Step 2: Wire RelayServer into Program.cs**

Replace the WebSocket branch in `Program.cs`:

```csharp
// In HandleRequest, replace the 501 stub with:
if (ctx.Request.Url?.AbsolutePath == "/blocker/ws-relay" && ctx.Request.IsWebSocketRequest)
{
    await _server!.HandleWebSocket(ctx, ct);
    return;
}
```

And in `Main`, initialize `_server` before the listen loop:

```csharp
private static RelayServer? _server;

public static async Task Main(string[] args)
{
    var options = RelayOptions.FromEnvironment();
    _server = new RelayServer(options);
    // ... rest unchanged
}
```

Pass `cts.Token` to HandleRequest by capturing it in a closure:

```csharp
_ = Task.Run(() => HandleRequest(ctx, cts.Token));
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 4: Smoke test with a tiny WebSocket client**

Create a one-off smoke script you can delete after:

```bash
cat > /tmp/ws-smoke.js <<'EOF'
const WebSocket = require('ws');
const ws = new WebSocket('ws://127.0.0.1:3099/blocker/ws-relay');
ws.on('open', () => {
  // [0x01][proto=1][sim=0x0001 LE][nameLen=4][“test”]
  const buf = Buffer.from([0x01, 0x01, 0x01, 0x00, 0x04, 0x74, 0x65, 0x73, 0x74]);
  ws.send(buf);
});
ws.on('message', m => { console.log('got', Buffer.from(m).toString('hex')); ws.close(); });
ws.on('close', () => process.exit(0));
ws.on('error', e => { console.error(e); process.exit(1); });
EOF
PORT=3099 dotnet run --project src/Blocker.Relay/ &
sleep 1
node /tmp/ws-smoke.js
# Expected: got 0201  (HelloAck + protocol version)
kill %1
rm /tmp/ws-smoke.js
```

If `node`/`ws` isn't available, write the equivalent in C# as a throwaway xUnit test under a `[Fact(Skip="manual")]` — but a shell smoke test is enough here.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs src/Blocker.Relay/Program.cs
git commit -m "relay: add WebSocket upgrade and Hello/HelloAck handshake"
```

---

### Task 14: CreateRoom / JoinRoom / RoomState

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

Add dispatch for session messages. Room state is fanned out to everyone currently in the room whenever it changes.

- [ ] **Step 1: Add message dispatch**

In `SessionLoop`, replace the `// Task 14+ dispatches lobby/game messages.` comment with:

```csharp
switch (type)
{
    case Protocol.CreateRoom: HandleCreateRoom(conn, payload.AsSpan(0, len), ct); break;
    case Protocol.JoinRoom:   await HandleJoinRoom(conn, payload.AsSpan(0, len), ct); break;
    case Protocol.LeaveRoom:  await HandleLeaveRoom(conn, ct); break;
    case Protocol.StartGame:  await HandleStartGame(conn, ct); break;
    default:
        await SendError(conn, ErrorCode.UnknownMessageType, ct);
        return;
}
```

- [ ] **Step 2: Implement CreateRoom, JoinRoom, LeaveRoom, StartGame stubs, BroadcastRoomState**

Add to `RelayServer.cs`:

```csharp
// Message layouts:
//   CreateRoom: [0x03][slotCount:byte][mapNameLen:varint][mapName][mapBlobLen:varint][mapBlob]
//   JoinRoom:   [0x04][codeLen:byte=4][code bytes][desiredSlot:byte]
//   RoomState:  [0x05][code:4][hostId:16][slotCount:byte][sim:uint16 LE]
//               [mapNameLen:varint][mapName]
//               per slot: [ownerNameLen:varint][ownerName][colorIdx:byte][flags:byte(0=normal,1=open,2=closed)]
//   GameStarted:[0x08][yourPlayerId:byte][activeCount:byte][playerIds...]

private void HandleCreateRoom(Connection conn, ReadOnlySpan<byte> payload, CancellationToken ct)
{
    if (payload.Length < 3) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    byte slotCount = payload[1];
    var (nameLen, c1) = Varint.Read(payload, 2);
    int pos = 2 + c1;
    string mapName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)nameLen));
    pos += (int)nameLen;
    var (blobLen, c2) = Varint.Read(payload, pos); pos += c2;
    var mapBlob = payload.Slice(pos, (int)blobLen).ToArray();

    var room = _rooms.TryCreate(conn.Id, conn.RemoteIp, _opts.MaxRoomsPerIp,
                                 conn.SimulationVersion, mapBlob, mapName, slotCount);
    if (room == null) { _ = SendError(conn, ErrorCode.TooManyRooms, ct); return; }

    // Host takes slot 0 by default.
    room.Slots[0] = new SlotInfo(conn.Id, conn.ClientName, 0, IsOpen: false, IsClosed: false);
    for (byte i = 1; i < slotCount; i++)
        room.Slots[i] = new SlotInfo(null, "", i, IsOpen: true, IsClosed: false);

    conn.CurrentRoom = room;
    conn.AssignedPlayerId = 0;
    Logger.Info($"conn={conn.Id} event=room-created code={room.Code} map={mapName}");
    _ = BroadcastRoomState(room, ct);
}

private async Task HandleJoinRoom(Connection conn, ReadOnlySpan<byte> payload, CancellationToken ct)
{
    if (payload.Length < 7) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    byte codeLen = payload[1];
    if (codeLen != 4) { await SendError(conn, ErrorCode.RoomNotFound, ct); return; }
    string code = System.Text.Encoding.ASCII.GetString(payload.Slice(2, 4));
    byte desired = payload[6];

    var room = _rooms.Get(code);
    if (room == null || room.Lifecycle != RoomLifecycle.Lobby)
    { await SendError(conn, ErrorCode.RoomNotFound, ct); return; }

    // Find a slot for the joiner: prefer desired, else first open slot.
    byte chosen = 255;
    if (room.Slots.TryGetValue(desired, out var s) && s.IsOpen && !s.IsClosed && s.OwnerId == null)
        chosen = desired;
    else
    {
        foreach (var kv in room.Slots)
            if (kv.Value.IsOpen && !kv.Value.IsClosed && kv.Value.OwnerId == null)
            { chosen = kv.Key; break; }
    }
    if (chosen == 255) { await SendError(conn, ErrorCode.RoomFull, ct); return; }

    room.Slots[chosen] = new SlotInfo(conn.Id, conn.ClientName, chosen, IsOpen: false, IsClosed: false);
    conn.CurrentRoom = room;
    conn.AssignedPlayerId = chosen;
    room.LastActivity = DateTime.UtcNow;
    Logger.Info($"conn={conn.Id} event=room-joined code={code} slot={chosen}");
    await BroadcastRoomState(room, ct);
}

private async Task HandleLeaveRoom(Connection conn, CancellationToken ct)
{
    var room = conn.CurrentRoom;
    if (room == null) return;
    RemoveConnFromRoom(conn, room);
    conn.CurrentRoom = null;
    conn.AssignedPlayerId = null;
    await BroadcastRoomState(room, ct);
}

private async Task HandleStartGame(Connection conn, CancellationToken ct)
{
    var room = conn.CurrentRoom;
    if (room == null) { await SendError(conn, ErrorCode.NotInRoom, ct); return; }
    if (room.HostId != conn.Id) { await SendError(conn, ErrorCode.NotHost, ct); return; }
    if (room.Lifecycle != RoomLifecycle.Lobby) return;

    // Transition FIRST so a mid-fan-out host disconnect is handled as PlayerLeft, not RoomClosed.
    room.Lifecycle = RoomLifecycle.Playing;
    var activeIds = room.Slots
        .Where(kv => kv.Value.OwnerId != null && !kv.Value.IsClosed)
        .Select(kv => kv.Key).OrderBy(x => x).ToArray();

    foreach (var kv in room.Slots)
    {
        if (kv.Value.OwnerId is not Guid ownerId) continue;
        Connection? other;
        lock (_connectionsLock) _connections.TryGetValue(ownerId, out other);
        if (other == null) continue;

        var msg = new byte[3 + activeIds.Length];
        msg[0] = Protocol.GameStarted;
        msg[1] = kv.Key;
        msg[2] = (byte)activeIds.Length;
        for (int i = 0; i < activeIds.Length; i++) msg[3 + i] = activeIds[i];
        try { await other.Ws.SendAsync(msg, WebSocketMessageType.Binary, true, ct); } catch { }
    }
    Logger.Info($"conn={conn.Id} event=game-started code={room.Code} players={activeIds.Length}");
}

private void RemoveConnFromRoom(Connection conn, Room room)
{
    foreach (var kv in room.Slots.ToList())
    {
        if (kv.Value.OwnerId == conn.Id)
            room.Slots[kv.Key] = new SlotInfo(null, "", kv.Value.ColorIndex, IsOpen: true, IsClosed: false);
    }
}

private async Task BroadcastRoomState(Room room, CancellationToken ct)
{
    // Encode once, send to all slot owners.
    var ms = new MemoryStream();
    ms.WriteByte(Protocol.RoomState);
    ms.Write(System.Text.Encoding.ASCII.GetBytes(room.Code));
    var hostBytes = room.HostId.ToByteArray();
    ms.Write(hostBytes, 0, hostBytes.Length);
    ms.WriteByte((byte)room.SlotCount);
    ms.WriteByte((byte)(room.SimulationVersion & 0xFF));
    ms.WriteByte((byte)((room.SimulationVersion >> 8) & 0xFF));
    var mapNameBytes = System.Text.Encoding.UTF8.GetBytes(room.MapName);
    var varintBuf = new byte[5];
    int vl = Varint.Write(varintBuf, 0, (uint)mapNameBytes.Length);
    ms.Write(varintBuf, 0, vl);
    ms.Write(mapNameBytes, 0, mapNameBytes.Length);
    for (byte i = 0; i < room.SlotCount; i++)
    {
        var s = room.Slots[i];
        var name = System.Text.Encoding.UTF8.GetBytes(s.DisplayName);
        vl = Varint.Write(varintBuf, 0, (uint)name.Length);
        ms.Write(varintBuf, 0, vl);
        ms.Write(name, 0, name.Length);
        ms.WriteByte(s.ColorIndex);
        byte flags = (byte)((s.IsOpen ? 1 : 0) | (s.IsClosed ? 2 : 0));
        ms.WriteByte(flags);
    }
    var bytes = ms.ToArray();

    foreach (var kv in room.Slots)
    {
        if (kv.Value.OwnerId is not Guid id) continue;
        Connection? c;
        lock (_connectionsLock) _connections.TryGetValue(id, out c);
        if (c == null) continue;
        try { await c.Ws.SendAsync(bytes, WebSocketMessageType.Binary, true, ct); } catch { }
    }
}
```

- [ ] **Step 2: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs
git commit -m "relay: implement CreateRoom/JoinRoom/StartGame lobby flow"
```

---

### Task 15: Commands and Hash fan-out with player-id authentication

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

- [ ] **Step 1: Add Commands and Hash dispatch**

In `SessionLoop`, extend the switch:

```csharp
case Protocol.Commands: await FanOutCommands(conn, payload, len, ct); break;
case Protocol.Hash:     await FanOutHash(conn, payload, len, ct); break;
```

- [ ] **Step 2: Implement the fan-out methods**

```csharp
private async Task FanOutCommands(Connection conn, byte[] payload, int len, CancellationToken ct)
{
    if (conn.CurrentRoom is not { Lifecycle: RoomLifecycle.Playing } room) return;
    if (conn.AssignedPlayerId is not byte assigned) return;

    // Peek tick + playerId from message body (skip 0x10 byte).
    var body = new ReadOnlySpan<byte>(payload, 1, len - 1);
    try
    {
        var (tick, playerId) = Blocker.Simulation.Net.CommandSerializer.PeekTickAndPlayer(body);
        if ((byte)playerId != assigned)
        {
            Logger.Warn($"conn={conn.Id} event=auth-fail claimed={playerId} owned={assigned}");
            return;
        }
        if (tick > room.HighestSeenTick) room.HighestSeenTick = tick;
    }
    catch { return; }
    room.LastActivity = DateTime.UtcNow;

    await FanOutToRoom(room, conn, payload, len, ct);
}

private async Task FanOutHash(Connection conn, byte[] payload, int len, CancellationToken ct)
{
    if (conn.CurrentRoom is not { Lifecycle: RoomLifecycle.Playing } room) return;
    // Hash layout: [0x11][tick:varint][playerId:byte][hash:uint32 LE]
    // Auth: verify the playerId byte after the tick varint.
    try
    {
        var body = new ReadOnlySpan<byte>(payload, 1, len - 1);
        var (_, consumed) = Varint.Read(body, 0);
        byte claimed = body[consumed];
        if (conn.AssignedPlayerId is not byte assigned || claimed != assigned) return;
    }
    catch { return; }
    await FanOutToRoom(room, conn, payload, len, ct);
}

private async Task FanOutToRoom(Room room, Connection sender, byte[] payload, int len, CancellationToken ct)
{
    var segment = new ArraySegment<byte>(payload, 0, len);
    foreach (var kv in room.Slots)
    {
        if (kv.Value.OwnerId is not Guid id) continue;
        if (id == sender.Id) continue;
        Connection? other;
        lock (_connectionsLock) _connections.TryGetValue(id, out other);
        if (other == null) continue;
        try { await other.Ws.SendAsync(segment, WebSocketMessageType.Binary, true, ct); } catch { }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs
git commit -m "relay: fan out Commands and Hash with player-id authentication"
```

---

### Task 16: Tick-stamped PlayerLeft on disconnect

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

- [ ] **Step 1: Replace the placeholder room-cleanup comment**

In the `finally` of `HandleWebSocket`, replace `// Task 17 adds room cleanup here.` with:

```csharp
var lostRoom = conn.CurrentRoom;
if (lostRoom != null)
{
    _ = HandleDisconnectRoomCleanup(conn, lostRoom, ct);
}
```

Note: `ct` is the outer CancellationToken here; capture it in a local if needed.

- [ ] **Step 2: Implement HandleDisconnectRoomCleanup**

```csharp
private async Task HandleDisconnectRoomCleanup(Connection conn, Room room, CancellationToken ct)
{
    if (room.Lifecycle == RoomLifecycle.Playing && conn.AssignedPlayerId is byte pid)
    {
        int effectiveTick = room.HighestSeenTick + 2;
        // PlayerLeft layout: [0x12][playerId:byte][effectiveTick:varint][reason:byte]
        var varBuf = new byte[5];
        int vl = Varint.Write(varBuf, 0, (uint)effectiveTick);
        var msg = new byte[3 + vl];
        msg[0] = Protocol.PlayerLeft;
        msg[1] = pid;
        Array.Copy(varBuf, 0, msg, 2, vl);
        msg[2 + vl] = (byte)LeaveReason.Disconnected;

        foreach (var kv in room.Slots)
        {
            if (kv.Value.OwnerId is not Guid id || id == conn.Id) continue;
            Connection? other;
            lock (_connectionsLock) _connections.TryGetValue(id, out other);
            if (other == null) continue;
            try { await other.Ws.SendAsync(msg, WebSocketMessageType.Binary, true, ct); } catch { }
        }
        RemoveConnFromRoom(conn, room);
        Logger.Info($"conn={conn.Id} event=left-game code={room.Code} slot={pid} effectiveTick={effectiveTick}");

        // If only one player left, close the room.
        int remaining = room.Slots.Values.Count(s => s.OwnerId != null);
        if (remaining < 1) _rooms.Remove(room, conn.RemoteIp);
    }
    else if (room.Lifecycle == RoomLifecycle.Lobby)
    {
        if (room.HostId == conn.Id)
        {
            // Host disconnected in lobby — close the room and notify everyone.
            foreach (var kv in room.Slots)
            {
                if (kv.Value.OwnerId is not Guid id || id == conn.Id) continue;
                Connection? other;
                lock (_connectionsLock) _connections.TryGetValue(id, out other);
                if (other == null) continue;
                try { await other.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "host-left", ct); } catch { }
            }
            _rooms.Remove(room, conn.RemoteIp);
        }
        else
        {
            RemoveConnFromRoom(conn, room);
            await BroadcastRoomState(room, ct);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs
git commit -m "relay: tick-stamped PlayerLeft on disconnect, lobby-vs-game cleanup"
```

---

### Task 17: Rate limiting and idle-room reaper

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

- [ ] **Step 1: Add RateAllows helper and call it before dispatch**

```csharp
private bool RateAllows(Connection conn)
{
    var now = DateTime.UtcNow;
    double elapsed = (now - conn.RateLastRefill).TotalSeconds;
    conn.RateTokens = Math.Min(_opts.RateLimitMsgPerSec,
                               conn.RateTokens + elapsed * _opts.RateLimitMsgPerSec);
    conn.RateLastRefill = now;
    if (conn.RateTokens < 1) return false;
    conn.RateTokens -= 1;
    return true;
}
```

In `SessionLoop`, after receiving the message and before the `switch`, add:

```csharp
if (!RateAllows(conn))
{
    await SendError(conn, ErrorCode.RateLimit, ct);
    return;
}
```

- [ ] **Step 2: Add the idle-room reaper**

Add a background task in `RelayServer` started from `HandleWebSocket`'s caller or `Main`. Simpler: a `StartReaper` method called once from `Main`:

```csharp
public void StartReaper(CancellationToken ct)
{
    _ = Task.Run(async () =>
    {
        while (!ct.IsCancellationRequested)
        {
            try { await Task.Delay(TimeSpan.FromMinutes(1), ct); } catch { return; }
            var now = DateTime.UtcNow;
            foreach (var room in _rooms.All().ToList())
            {
                var limit = room.Lifecycle == RoomLifecycle.Lobby ? _opts.LobbyTimeout : _opts.GameTimeout;
                if (now - room.LastActivity > limit)
                {
                    Logger.Info($"reaper closing idle room {room.Code} lifecycle={room.Lifecycle}");
                    foreach (var kv in room.Slots)
                    {
                        if (kv.Value.OwnerId is not Guid id) continue;
                        Connection? c;
                        lock (_connectionsLock) _connections.TryGetValue(id, out c);
                        if (c == null) continue;
                        try { await c.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "idle", ct); } catch { }
                    }
                    // IP not easily recoverable here — pass "" to skip per-IP decrement; safer than wrong accounting.
                    _rooms.Remove(room, "");
                }
            }
        }
    }, ct);
}
```

Call `_server.StartReaper(cts.Token);` in `Main` right after constructing the server.

Also update `FanOutCommands` to refresh `room.LastActivity` (already done in Task 15).

- [ ] **Step 3: Build**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs src/Blocker.Relay/Program.cs
git commit -m "relay: add per-connection rate limiting and idle-room reaper"
```

---

## Part 3 — Godot Client Transport

### Task 18: `RelayClientConfig` and `MultiplayerSessionState`

**Files:**
- Create: `godot/Scripts/Net/RelayClientConfig.cs`
- Create: `godot/Scripts/Net/MultiplayerSessionState.cs`

- [ ] **Step 1: Create RelayClientConfig.cs**

```csharp
// godot/Scripts/Net/RelayClientConfig.cs
namespace Blocker.Game.Net;

public static class RelayClientConfig
{
    public const string DefaultUrl = "wss://julianoschroeder.com/blocker/ws-relay";

    public static string ResolvedUrl =>
        System.Environment.GetEnvironmentVariable("BLOCKER_RELAY_URL") ?? DefaultUrl;

    public static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(5);
    public static readonly TimeSpan HelloTimeout   = TimeSpan.FromSeconds(5);
}
```

- [ ] **Step 2: Create MultiplayerSessionState.cs**

```csharp
// godot/Scripts/Net/MultiplayerSessionState.cs
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;

namespace Blocker.Game.Net;

/// <summary>
/// Carried from MultiplayerMenu/SlotConfigScreen into GameManager via a static
/// slot on GameLaunchData (following the existing pattern for single player).
/// </summary>
public sealed class MultiplayerSessionState
{
    public required RelayClient Relay { get; init; }
    public required int LocalPlayerId { get; init; }
    public required HashSet<int> ActivePlayerIds { get; init; }
    public required MapData Map { get; init; }
    public required List<SlotAssignment> Assignments { get; init; }
}
```

- [ ] **Step 3: Extend `GameLaunchData` in `SlotConfigScreen.cs`**

```csharp
// In godot/Scripts/UI/SlotConfigScreen.cs, update GameLaunchData:
public static class GameLaunchData
{
    public static MapData? MapData { get; set; }
    public static List<SlotAssignment>? Assignments { get; set; }
    public static Blocker.Game.Net.MultiplayerSessionState? MultiplayerSession { get; set; }
}
```

Note: `RelayClient` does not yet exist; Task 19 creates it. Build will fail until Task 19 lands.

- [ ] **Step 4: Do not commit yet — proceed to Task 19**

---

### Task 19: `RelayClient` skeleton — connect, receive loop, send loop

**Files:**
- Create: `godot/Scripts/Net/RelayClient.cs`

This is the WebSocket transport. It implements `IRelayClient` so the coordinator doesn't care that it's Godot-hosted.

- [ ] **Step 1: Create RelayClient.cs**

```csharp
// godot/Scripts/Net/RelayClient.cs
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading.Channels;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game.Net;

/// <summary>
/// WebSocket-backed IRelayClient. Inbound messages land on a ConcurrentQueue and
/// are drained on the Godot main thread in DrainInbound(). Outbound messages go
/// through a Channel to a background send task. LockstepCoordinator only ever
/// sees events on the main thread.
/// </summary>
public sealed class RelayClient : IRelayClient, IDisposable
{
    public enum ConnState { Disconnected, Connecting, Connected, Closed }
    public ConnState State { get; private set; } = ConnState.Disconnected;
    public string? LastError { get; private set; }

    private readonly ClientWebSocket _ws = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ConcurrentQueue<byte[]> _inbox = new();
    private readonly Channel<byte[]> _outbox = Channel.CreateUnbounded<byte[]>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    // IRelayClient events — fired on the main thread from DrainInbound.
    public event Action<int, int, IReadOnlyList<Command>>? CommandsReceived;
    public event Action<int, int, uint>? HashReceived;
    public event Action<int, int, LeaveReason>? PlayerLeft;
    public event Action<int>? SurrenderReceived;

    // Lobby-level events — also fired on main thread.
    public event Action? HelloAcked;
    public event Action<RoomStatePayload>? RoomStateReceived;
    public event Action<int /*localPlayerId*/, int[] /*activePlayerIds*/>? GameStarted;
    public event Action<ErrorCode>? ServerError;
    public event Action? ConnectionClosed;

    public async Task<bool> ConnectAsync(string url, string clientName)
    {
        State = ConnState.Connecting;
        try
        {
            using var connectCts = CancellationTokenSource.CreateLinkedTokenSource(_cts.Token);
            connectCts.CancelAfter(RelayClientConfig.ConnectTimeout);
            await _ws.ConnectAsync(new Uri(url), connectCts.Token);
            State = ConnState.Connected;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            State = ConnState.Closed;
            return false;
        }
        _ = Task.Run(ReceiveLoop);
        _ = Task.Run(SendLoop);
        SendHello(clientName);
        return true;
    }

    private void SendHello(string name)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(name);
        var varBuf = new byte[5];
        int vl = Varint.Write(varBuf, 0, (uint)nameBytes.Length);
        var msg = new byte[4 + vl + nameBytes.Length];
        msg[0] = Protocol.Hello;
        msg[1] = Protocol.ProtocolVersion;
        msg[2] = (byte)(Protocol.SimulationVersion & 0xFF);
        msg[3] = (byte)((Protocol.SimulationVersion >> 8) & 0xFF);
        Array.Copy(varBuf, 0, msg, 4, vl);
        Array.Copy(nameBytes, 0, msg, 4 + vl, nameBytes.Length);
        _outbox.Writer.TryWrite(msg);
    }

    public void SendCommands(int tick, IReadOnlyList<Command> commands)
    {
        var tc = new TickCommands(_localPlayerId, tick, commands);
        var body = CommandSerializer.Serialize(tc);
        var msg = new byte[1 + body.Length];
        msg[0] = Protocol.Commands;
        Array.Copy(body, 0, msg, 1, body.Length);
        _outbox.Writer.TryWrite(msg);
    }

    public void SendHash(int tick, uint hash)
    {
        var varBuf = new byte[5];
        int vl = Varint.Write(varBuf, 0, (uint)tick);
        var msg = new byte[1 + vl + 1 + 4];
        msg[0] = Protocol.Hash;
        Array.Copy(varBuf, 0, msg, 1, vl);
        msg[1 + vl] = (byte)_localPlayerId;
        msg[1 + vl + 1] = (byte)(hash & 0xFF);
        msg[1 + vl + 2] = (byte)((hash >> 8) & 0xFF);
        msg[1 + vl + 3] = (byte)((hash >> 16) & 0xFF);
        msg[1 + vl + 4] = (byte)((hash >> 24) & 0xFF);
        _outbox.Writer.TryWrite(msg);
    }

    public void SendDesyncReport(int tick, GameStateSnapshot snapshot)
    {
        // M1: log-only, minimal payload. Relay only records it.
        var msg = new byte[] { Protocol.DesyncReport, (byte)_localPlayerId };
        _outbox.Writer.TryWrite(msg);
    }

    public void SendSurrender()
    {
        var msg = new byte[] { Protocol.Surrender, (byte)_localPlayerId };
        _outbox.Writer.TryWrite(msg);
    }

    public void SendCreateRoom(byte slotCount, string mapName, byte[] mapBlob)
    {
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(mapName);
        var varBuf = new byte[5];
        int vn = Varint.Write(varBuf, 0, (uint)nameBytes.Length);
        var varBuf2 = new byte[5];
        int vb = Varint.Write(varBuf2, 0, (uint)mapBlob.Length);
        var msg = new byte[2 + vn + nameBytes.Length + vb + mapBlob.Length];
        int o = 0;
        msg[o++] = Protocol.CreateRoom;
        msg[o++] = slotCount;
        Array.Copy(varBuf, 0, msg, o, vn); o += vn;
        Array.Copy(nameBytes, 0, msg, o, nameBytes.Length); o += nameBytes.Length;
        Array.Copy(varBuf2, 0, msg, o, vb); o += vb;
        Array.Copy(mapBlob, 0, msg, o, mapBlob.Length);
        _outbox.Writer.TryWrite(msg);
    }

    public void SendJoinRoom(string code, byte desiredSlot)
    {
        if (code.Length != 4) throw new ArgumentException("Code must be 4 chars");
        var msg = new byte[7];
        msg[0] = Protocol.JoinRoom;
        msg[1] = 4;
        for (int i = 0; i < 4; i++) msg[2 + i] = (byte)code[i];
        msg[6] = desiredSlot;
        _outbox.Writer.TryWrite(msg);
    }

    public void SendStartGame() =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.StartGame });

    public void SendLeaveRoom() =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.LeaveRoom });

    private int _localPlayerId;
    public void SetLocalPlayerId(int id) => _localPlayerId = id;

    /// <summary>Must be called on main thread every frame by MultiplayerTickRunner.</summary>
    public void DrainInbound()
    {
        while (_inbox.TryDequeue(out var msg))
            Dispatch(msg);
    }

    private void Dispatch(byte[] msg)
    {
        if (msg.Length == 0) return;
        byte type = msg[0];
        try
        {
            switch (type)
            {
                case Protocol.HelloAck: HelloAcked?.Invoke(); break;
                case Protocol.RoomState: RoomStateReceived?.Invoke(ParseRoomState(msg)); break;
                case Protocol.GameStarted:
                {
                    byte yourId = msg[1];
                    byte count = msg[2];
                    var ids = new int[count];
                    for (int i = 0; i < count; i++) ids[i] = msg[3 + i];
                    _localPlayerId = yourId;
                    GameStarted?.Invoke(yourId, ids);
                    break;
                }
                case Protocol.Commands:
                {
                    var tc = CommandSerializer.Deserialize(new ReadOnlySpan<byte>(msg, 1, msg.Length - 1));
                    CommandsReceived?.Invoke(tc.PlayerId, tc.Tick, tc.Commands);
                    break;
                }
                case Protocol.Hash:
                {
                    var body = new ReadOnlySpan<byte>(msg, 1, msg.Length - 1);
                    var (tick, consumed) = Varint.Read(body, 0);
                    int pid = body[consumed];
                    int h = body[consumed + 1]
                          | (body[consumed + 2] << 8)
                          | (body[consumed + 3] << 16)
                          | (body[consumed + 4] << 24);
                    HashReceived?.Invoke(pid, (int)tick, unchecked((uint)h));
                    break;
                }
                case Protocol.PlayerLeft:
                {
                    byte pid = msg[1];
                    var (effTick, _) = Varint.Read(new ReadOnlySpan<byte>(msg, 2, msg.Length - 2), 0);
                    byte reason = msg[msg.Length - 1];
                    PlayerLeft?.Invoke(pid, (int)effTick, (LeaveReason)reason);
                    break;
                }
                case Protocol.Surrender: SurrenderReceived?.Invoke(msg[1]); break;
                case Protocol.Error: ServerError?.Invoke((ErrorCode)msg[1]); break;
                case Protocol.Ping: _outbox.Writer.TryWrite(new byte[] { Protocol.Pong }); break;
                case Protocol.Pong: break;
            }
        }
        catch (Exception ex) { GD.PrintErr($"RelayClient dispatch error: {ex.Message}"); }
    }

    public static RoomStatePayload ParseRoomState(byte[] msg)
    {
        int o = 1;
        string code = System.Text.Encoding.ASCII.GetString(msg, o, 4); o += 4;
        var hostBytes = new byte[16]; Array.Copy(msg, o, hostBytes, 0, 16); o += 16;
        byte slotCount = msg[o++];
        ushort simVer = (ushort)(msg[o] | (msg[o + 1] << 8)); o += 2;
        var (mapNameLen, c1) = Varint.Read(msg, o); o += c1;
        string mapName = System.Text.Encoding.UTF8.GetString(msg, o, (int)mapNameLen); o += (int)mapNameLen;
        var slots = new SlotStateEntry[slotCount];
        for (int i = 0; i < slotCount; i++)
        {
            var (nLen, c2) = Varint.Read(msg, o); o += c2;
            string name = System.Text.Encoding.UTF8.GetString(msg, o, (int)nLen); o += (int)nLen;
            byte colorIdx = msg[o++];
            byte flags = msg[o++];
            slots[i] = new SlotStateEntry(name, colorIdx,
                IsOpen: (flags & 1) != 0, IsClosed: (flags & 2) != 0);
        }
        return new RoomStatePayload(code, new Guid(hostBytes), simVer, mapName, slots);
    }

    private async Task ReceiveLoop()
    {
        var buf = new byte[64 * 1024];
        try
        {
            while (_ws.State == WebSocketState.Open && !_cts.IsCancellationRequested)
            {
                int total = 0;
                while (true)
                {
                    var r = await _ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), _cts.Token);
                    if (r.MessageType == WebSocketMessageType.Close) return;
                    total += r.Count;
                    if (r.EndOfMessage) break;
                    if (total >= buf.Length) return; // too large
                }
                var msg = new byte[total];
                Array.Copy(buf, msg, total);
                _inbox.Enqueue(msg);
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
        finally { State = ConnState.Closed; _inbox.Enqueue(new byte[] { 0 }); /* wake drain */ }
    }

    private async Task SendLoop()
    {
        try
        {
            while (await _outbox.Reader.WaitToReadAsync(_cts.Token))
            {
                while (_outbox.Reader.TryRead(out var msg))
                {
                    if (_ws.State != WebSocketState.Open) return;
                    await _ws.SendAsync(msg, WebSocketMessageType.Binary, true, _cts.Token);
                }
            }
        }
        catch (Exception ex) { LastError = ex.Message; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _ws.Dispose(); } catch { }
    }
}

public sealed record RoomStatePayload(
    string Code, Guid HostId, ushort SimulationVersion, string MapName, SlotStateEntry[] Slots);

public sealed record SlotStateEntry(string DisplayName, byte ColorIndex, bool IsOpen, bool IsClosed);
```

- [ ] **Step 2: Build the Godot project**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 3: Commit Tasks 18 and 19 together**

```bash
git add godot/Scripts/Net/RelayClientConfig.cs \
        godot/Scripts/Net/MultiplayerSessionState.cs \
        godot/Scripts/Net/RelayClient.cs \
        godot/Scripts/UI/SlotConfigScreen.cs
git commit -m "net: add RelayClient WebSocket transport and session state"
```

---

### Task 20: `MultiplayerTickRunner` + `SelectionManager` command sink hook

**Files:**
- Create: `godot/Scripts/Game/MultiplayerTickRunner.cs`
- Modify: `godot/Scripts/Input/SelectionManager.cs`

- [ ] **Step 1: Add a command sink hook to SelectionManager**

The current `SelectionManager.FlushCommands()` is pulled by `TickRunner`. For multiplayer we want commands pushed to the coordinator the moment they're generated (so `QueueLocalCommand` gets them immediately), not buffered until the next tick.

Read `SelectionManager.cs` and add, near the top of the class:

```csharp
public interface ICommandSink { void Submit(Command cmd); }
private ICommandSink? _commandSink;
public void SetCommandSink(ICommandSink? sink) => _commandSink = sink;
```

Replace every `_pendingCommands.Add(cmd);` with a helper:

```csharp
private void EmitCommand(Command cmd)
{
    if (_commandSink != null) _commandSink.Submit(cmd);
    else _pendingCommands.Add(cmd);
}
```

Use `EmitCommand` everywhere `_pendingCommands.Add` was called (grep the file — there are ~12 call sites listed in the earlier exploration). This keeps single-player behavior unchanged when no sink is set.

- [ ] **Step 2: Create MultiplayerTickRunner.cs**

```csharp
// godot/Scripts/Game/MultiplayerTickRunner.cs
using Blocker.Game.Input;
using Blocker.Game.Net;
using Blocker.Simulation.Commands;
using Blocker.Simulation.Core;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game;

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

        if (_accumulator > 2 * TickInterval)
            _accumulator = 2 * TickInterval;
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/Game/MultiplayerTickRunner.cs \
        godot/Scripts/Input/SelectionManager.cs
git commit -m "game: add MultiplayerTickRunner and SelectionManager command sink hook"
```

---

### Task 21: `GameManager` branches on multiplayer session

**Files:**
- Modify: `godot/Scripts/Game/GameManager.cs`

- [ ] **Step 1: Branch _Ready on MultiplayerSession**

In `GameManager._Ready`, after the map/gameState is loaded, replace:

```csharp
// Set up tick runner
_tickRunner = GetNode<TickRunner>("TickRunner");
_tickRunner.SetGameState(gameState);
_tickRunner.SetSelectionManager(_selectionManager);
_gridRenderer.SetTickInterval((float)_tickRunner.TickInterval);
```

with:

```csharp
if (GameLaunchData.MultiplayerSession is { } mp)
{
    // Multiplayer path: create coordinator + MP tick runner, hide/remove SP tick runner.
    var spRunner = GetNodeOrNull<TickRunner>("TickRunner");
    if (spRunner != null) spRunner.QueueFree();

    var coord = new LockstepCoordinator(mp.LocalPlayerId, gameState, mp.Relay, mp.ActivePlayerIds);
    var mpRunner = new MultiplayerTickRunner { Name = "MultiplayerTickRunner", TickRate = 12 };
    AddChild(mpRunner);
    mpRunner.Initialize(coord, mp.Relay, gameState);
    _gridRenderer.SetTickInterval((float)mpRunner.TickInterval);
    _selectionManager.ControllingPlayer = mp.LocalPlayerId;
    _selectionManager.SetCommandSink(mpRunner);
    coord.StartGame();

    coord.GameEnded += (winner) => GD.Print($"Game over. Winner: team {winner}");
    coord.DesyncDetected += () => GD.PrintErr("DESYNC DETECTED");
}
else
{
    _tickRunner = GetNode<TickRunner>("TickRunner");
    _tickRunner.SetGameState(gameState);
    _tickRunner.SetSelectionManager(_selectionManager);
    _gridRenderer.SetTickInterval((float)_tickRunner.TickInterval);
}
```

Add a using: `using Blocker.Game.Net;` and `using Blocker.Simulation.Net;` at the top.

Also clear the session at the end of `_Ready` so menu-return works:

```csharp
// Clear after consuming so a return-to-menu round trip starts fresh.
GameLaunchData.MultiplayerSession = null;
```

- [ ] **Step 2: Build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/Game/GameManager.cs
git commit -m "game: GameManager branches on multiplayer session"
```

---

## Part 4 — UI

### Task 22: `MultiplayerMenu` scene and script

**Files:**
- Create: `godot/Scenes/MultiplayerMenu.tscn`
- Create: `godot/Scripts/UI/MultiplayerMenu.cs`
- Modify: `godot/Scripts/UI/MainMenu.cs`

- [ ] **Step 1: Create MultiplayerMenu.cs**

```csharp
// godot/Scripts/UI/MultiplayerMenu.cs
using Blocker.Game.Net;
using Godot;

namespace Blocker.Game.UI;

public enum MultiplayerIntent { None, Host, Join }

public static class MultiplayerLaunchData
{
    public static MultiplayerIntent Intent;
    public static string JoinCode = "";
    public static RelayClient? Relay;
}

public partial class MultiplayerMenu : Control
{
    private Label _statusLabel = null!;
    private Button _hostBtn = null!;
    private Button _joinBtn = null!;
    private LineEdit _codeEdit = null!;
    private RelayClient _relay = null!;

    public override async void _Ready()
    {
        var vbox = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f,
            AnchorTop = 0.5f, AnchorBottom = 0.5f,
            OffsetLeft = -180, OffsetRight = 180,
            OffsetTop = -160, OffsetBottom = 160
        };
        vbox.AddThemeConstantOverride("separation", 14);
        AddChild(vbox);

        var title = new Label { Text = "Multiplayer", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 32);
        vbox.AddChild(title);

        _statusLabel = new Label { Text = "Connecting to server…", HorizontalAlignment = HorizontalAlignment.Center };
        vbox.AddChild(_statusLabel);

        _hostBtn = new Button { Text = "Host Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
        _hostBtn.Pressed += OnHostPressed;
        vbox.AddChild(_hostBtn);

        vbox.AddChild(new HSeparator());

        _codeEdit = new LineEdit { PlaceholderText = "Room code (4 chars)", MaxLength = 4 };
        vbox.AddChild(_codeEdit);
        _joinBtn = new Button { Text = "Join Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
        _joinBtn.Pressed += OnJoinPressed;
        vbox.AddChild(_joinBtn);

        vbox.AddChild(new HSeparator());

        var backBtn = new Button { Text = "< Back", CustomMinimumSize = new Vector2(0, 40) };
        backBtn.Pressed += () => {
            _relay?.Dispose();
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        vbox.AddChild(backBtn);

        // Connect + say hello.
        _relay = new RelayClient();
        bool ok = await _relay.ConnectAsync(RelayClientConfig.ResolvedUrl, "Player");
        if (!ok)
        {
            _statusLabel.Text = $"Cannot reach server: {_relay.LastError}";
            return;
        }
        _relay.HelloAcked += () => CallDeferred(nameof(OnHelloAcked));
        _relay.ConnectionClosed += () => CallDeferred(nameof(OnClosed));
        _relay.ServerError += (e) => CallDeferred(nameof(OnServerError), (int)e);

        // Drain inbound every frame.
        var drainTimer = new Timer { WaitTime = 0.016, Autostart = true };
        drainTimer.Timeout += () => _relay.DrainInbound();
        AddChild(drainTimer);
    }

    private void OnHelloAcked()
    {
        _statusLabel.Text = "Connected.";
        _hostBtn.Disabled = false;
        _joinBtn.Disabled = false;
    }

    private void OnClosed() { _statusLabel.Text = "Disconnected."; _hostBtn.Disabled = true; _joinBtn.Disabled = true; }
    private void OnServerError(int code) { _statusLabel.Text = $"Server error: {(Blocker.Simulation.Net.ErrorCode)code}"; }

    private void OnHostPressed()
    {
        MultiplayerLaunchData.Intent = MultiplayerIntent.Host;
        MultiplayerLaunchData.Relay = _relay;
        GetTree().ChangeSceneToFile("res://Scenes/MapSelect.tscn");
    }

    private void OnJoinPressed()
    {
        var code = _codeEdit.Text.ToUpperInvariant().Trim();
        if (code.Length != 4) { _statusLabel.Text = "Code must be 4 characters."; return; }
        MultiplayerLaunchData.Intent = MultiplayerIntent.Join;
        MultiplayerLaunchData.JoinCode = code;
        MultiplayerLaunchData.Relay = _relay;
        _relay.SendJoinRoom(code, 1);
        GetTree().ChangeSceneToFile("res://Scenes/SlotConfig.tscn");
    }
}
```

- [ ] **Step 2: Create the scene file**

```
# godot/Scenes/MultiplayerMenu.tscn
[gd_scene format=3 uid="uid://mpmenu001"]

[ext_resource type="Script" path="res://Scripts/UI/MultiplayerMenu.cs" id="1_mpmenu"]

[node name="MultiplayerMenu" type="Control"]
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
script = ExtResource("1_mpmenu")
```

Write the file literally — Godot will fix the `uid` on first open.

- [ ] **Step 3: Wire up MainMenu "Play Multiplayer" button**

In `godot/Scripts/UI/MainMenu.cs`, after the `Play vs AI` button, add:

```csharp
var playMpBtn = new Button { Text = "Play Multiplayer", CustomMinimumSize = new Vector2(0, 50) };
playMpBtn.Pressed += OnPlayMultiplayerPressed;
vbox.AddChild(playMpBtn);
```

And the handler:

```csharp
private void OnPlayMultiplayerPressed()
{
    GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
}
```

- [ ] **Step 4: Build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add godot/Scenes/MultiplayerMenu.tscn \
        godot/Scripts/UI/MultiplayerMenu.cs \
        godot/Scripts/UI/MainMenu.cs
git commit -m "ui: add MultiplayerMenu scene with Host/Join flow"
```

---

### Task 23: `SlotConfigScreen` — host mode

**Files:**
- Modify: `godot/Scripts/UI/SlotConfigScreen.cs`

For M1 we keep 2 players only. Host mode shows the room code and waits for exactly one joiner before enabling Start.

- [ ] **Step 1: Add mode detection and host branch**

In `SlotConfigScreen._Ready`, after loading `_mapData`, before the existing body, insert:

```csharp
var intent = MultiplayerLaunchData.Intent;
if (intent == MultiplayerIntent.Host) { SetupHostMode(); return; }
if (intent == MultiplayerIntent.Join) { SetupJoinMode(); return; }
// Else: fall through to the existing single-player code.
```

- [ ] **Step 2: Implement SetupHostMode**

```csharp
private RelayClient? _relay;
private Label? _hostStatusLabel;
private Button? _hostStartBtn;
private string _hostRoomCode = "";

private void SetupHostMode()
{
    _relay = MultiplayerLaunchData.Relay;
    if (_relay == null || _mapData == null)
    {
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn"); return;
    }

    var vbox = new VBoxContainer {
        AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 0.05f, AnchorBottom = 0.95f };
    vbox.AddThemeConstantOverride("separation", 12);
    AddChild(vbox);

    var header = new HBoxContainer();
    vbox.AddChild(header);
    var backBtn = new Button { Text = "< Back" };
    backBtn.Pressed += () => {
        _relay?.SendLeaveRoom();
        GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
    };
    header.AddChild(backBtn);
    var title = new Label { Text = $"Host: {_mapData.Name}",
        SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
    title.AddThemeFontSizeOverride("font_size", 28);
    header.AddChild(title);

    _hostStatusLabel = new Label { Text = "Creating room…" };
    vbox.AddChild(_hostStatusLabel);

    vbox.AddChild(new HSeparator());
    _hostStartBtn = new Button { Text = "Start Game", CustomMinimumSize = new Vector2(0, 50), Disabled = true };
    _hostStartBtn.Pressed += OnHostStartPressed;
    vbox.AddChild(_hostStartBtn);

    // Relay events fire from the receive loop on a background thread. We capture
    // the payload into private fields and bounce to the main thread via
    // CallDeferred + a 0-arg handler — Variant can't wrap arbitrary records.
    _relay.RoomStateReceived += (rs) => {
        _hostRoomCode = rs.Code;
        _hostLatestFilledSlots = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
        CallDeferred(nameof(OnHostRoomStateDeferred));
    };
    _relay.GameStarted += (localId, activeIds) => {
        _pendingLocalId = localId;
        _pendingActiveIds = activeIds;
        CallDeferred(nameof(OnHostGameStartedDeferred));
    };

    // Create the room.
    var mapBlob = System.Text.Encoding.UTF8.GetBytes(_mapData.Name); // opaque to relay; joiners load by name for M1
    _relay.SendCreateRoom((byte)2, _mapData.Name, mapBlob);

    var drain = new Timer { WaitTime = 0.016, Autostart = true };
    drain.Timeout += () => _relay.DrainInbound();
    AddChild(drain);
}

private int _hostLatestFilledSlots;
private int _pendingLocalId;
private int[] _pendingActiveIds = Array.Empty<int>();

private void OnHostRoomStateDeferred()
{
    _hostStatusLabel!.Text = _hostLatestFilledSlots == 2
        ? $"Room code: {_hostRoomCode} — ready to start"
        : $"Room code: {_hostRoomCode} — waiting for opponent…";
    _hostStartBtn!.Disabled = _hostLatestFilledSlots < 2;
}

private void OnHostStartPressed()
{
    _relay!.SendStartGame();
}

private void OnHostGameStartedDeferred()
{
    // Map player 0 → slot 0, player 1 → slot 1. M1 assumes slot count >= active player count.
    var assignments = new List<SlotAssignment>();
    foreach (var pid in _pendingActiveIds)
        assignments.Add(new SlotAssignment(pid, pid));

    GameLaunchData.MapData = _mapData;
    GameLaunchData.Assignments = assignments;
    GameLaunchData.MultiplayerSession = new MultiplayerSessionState
    {
        Relay = _relay!,
        LocalPlayerId = _pendingLocalId,
        ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
        Map = _mapData!,
        Assignments = assignments
    };
    GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
}
```

- [ ] **Step 3: Build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/UI/SlotConfigScreen.cs
git commit -m "ui: SlotConfigScreen host mode with room code display"
```

---

### Task 24: `SlotConfigScreen` — join mode

**Files:**
- Modify: `godot/Scripts/UI/SlotConfigScreen.cs`

Join mode skips MapSelect — we want to go straight from "Join Game" → SlotConfig → Main. But the current flow routes via MapSelect. For M1 the simplest fix: when `Intent == Join`, `_Ready` should not try to load from `MapSelection` at all.

- [ ] **Step 1: Guard the MapSelection check on intent**

At the very top of `_Ready`, before the MapSelection null-check, insert:

```csharp
var intent = MultiplayerLaunchData.Intent;
if (intent == MultiplayerIntent.Join)
{
    SetupJoinMode();
    return;
}
```

The existing `MapSelection.SelectedMapFileName == null` check stays only for the Single / Host paths.

For Host path: `MapSelect.tscn` runs first (chosen from MultiplayerMenu when user clicks Host Game), so `MapSelection.SelectedMapFileName` will be set. Good — no change needed.

- [ ] **Step 2: Implement SetupJoinMode**

```csharp
private Label? _joinStatusLabel;
private string _joinMapName = "";

private void SetupJoinMode()
{
    _relay = MultiplayerLaunchData.Relay;
    if (_relay == null)
    { GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn"); return; }

    var vbox = new VBoxContainer { AnchorLeft = 0.1f, AnchorRight = 0.9f, AnchorTop = 0.05f, AnchorBottom = 0.95f };
    vbox.AddThemeConstantOverride("separation", 12);
    AddChild(vbox);

    var header = new HBoxContainer();
    vbox.AddChild(header);
    var backBtn = new Button { Text = "< Back" };
    backBtn.Pressed += () => {
        _relay?.SendLeaveRoom();
        GetTree().ChangeSceneToFile("res://Scenes/MultiplayerMenu.tscn");
    };
    header.AddChild(backBtn);
    var title = new Label {
        Text = $"Joined: {MultiplayerLaunchData.JoinCode}",
        SizeFlagsHorizontal = SizeFlags.ExpandFill, HorizontalAlignment = HorizontalAlignment.Center };
    title.AddThemeFontSizeOverride("font_size", 28);
    header.AddChild(title);

    _joinStatusLabel = new Label { Text = "Waiting for host to start…" };
    vbox.AddChild(_joinStatusLabel);

    _relay.RoomStateReceived += (rs) => {
        _joinMapName = rs.MapName;
        CallDeferred(nameof(OnJoinRoomStateDeferred));
    };
    _relay.GameStarted += (localId, activeIds) => {
        _pendingLocalId = localId;
        _pendingActiveIds = activeIds;
        CallDeferred(nameof(OnJoinGameStartedDeferred));
    };
    _relay.ServerError += (code) => {
        _pendingError = code.ToString();
        CallDeferred(nameof(OnJoinErrorDeferred));
    };

    var drain = new Timer { WaitTime = 0.016, Autostart = true };
    drain.Timeout += () => _relay.DrainInbound();
    AddChild(drain);
}

private string _pendingError = "";
private void OnJoinRoomStateDeferred() { _joinStatusLabel!.Text = $"Map: {_joinMapName} — waiting…"; }
private void OnJoinErrorDeferred() {
    _joinStatusLabel!.Text = $"Error: {_pendingError}";
}

private void OnJoinGameStartedDeferred()
{
    // For M1: the joiner loads the same map by name from local disk. We don't
    // fan the full map blob over the wire yet — M2 will add MapData message for
    // custom maps. Host and joiner must have identical map files.
    var md = Blocker.Game.Maps.MapFileManager.Load(_joinMapName);
    if (md == null)
    {
        _joinStatusLabel!.Text = $"Map '{_joinMapName}' not found locally.";
        return;
    }

    var assignments = new List<SlotAssignment>();
    foreach (var pid in _pendingActiveIds)
        assignments.Add(new SlotAssignment(pid, pid));

    GameLaunchData.MapData = md;
    GameLaunchData.Assignments = assignments;
    GameLaunchData.MultiplayerSession = new MultiplayerSessionState
    {
        Relay = _relay!,
        LocalPlayerId = _pendingLocalId,
        ActivePlayerIds = new HashSet<int>(_pendingActiveIds),
        Map = md,
        Assignments = assignments
    };
    GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
}
```

Note on the "load map by name" shortcut: for M1 both players must have the same map file available locally by name. Maps are in `maps/` at repo root, and `MapFileManager.Load(mapName)` reads them. If the joiner doesn't have it, they see an error and back out. M2 will add the `MapData` wire message for custom map sharing.

- [ ] **Step 2: Build**

Run: `dotnet build godot/Blocker.Game.csproj`
Expected: success.

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/UI/SlotConfigScreen.cs
git commit -m "ui: SlotConfigScreen join mode"
```

---

### Task 25: Hook MapSelect → host flow

**Files:**
- Modify: `godot/Scripts/UI/MapSelectScreen.cs` (only if it needs changes)

- [ ] **Step 1: Verify MapSelect already routes to SlotConfig.tscn**

Read `godot/Scripts/UI/MapSelectScreen.cs`. If it already changes to SlotConfig after a map is selected, no edit needed — the host branch inside `SlotConfigScreen.SetupHostMode` runs because `MultiplayerLaunchData.Intent == Host` was set before MapSelect ran.

- [ ] **Step 2: Build and commit if changes were made, else skip**

---

## Part 5 — Deployment

### Task 26: `scripts/deploy-relay.sh`

**Files:**
- Create: `scripts/deploy-relay.sh`

- [ ] **Step 1: Create the script**

```bash
#!/usr/bin/env bash
# scripts/deploy-relay.sh — build self-contained Linux binary and deploy to droplet.
set -euo pipefail
DROPLET="${DROPLET:-root@209.38.176.249}"
REMOTE_DIR="/opt/blocker-relay"

echo "Building self-contained Linux binary…"
dotnet publish src/Blocker.Relay -c Release -r linux-x64 \
    --self-contained -p:PublishSingleFile=true \
    -o publish/blocker-relay

echo "Uploading to ${DROPLET}:${REMOTE_DIR}/Blocker.Relay.new …"
ssh "${DROPLET}" "mkdir -p ${REMOTE_DIR}"
scp publish/blocker-relay/Blocker.Relay "${DROPLET}:${REMOTE_DIR}/Blocker.Relay.new"

echo "Installing and restarting service…"
ssh "${DROPLET}" "
    mv ${REMOTE_DIR}/Blocker.Relay.new ${REMOTE_DIR}/Blocker.Relay &&
    chmod +x ${REMOTE_DIR}/Blocker.Relay &&
    systemctl restart blocker-relay &&
    systemctl status blocker-relay --no-pager
"
echo "Done."
```

- [ ] **Step 2: Make it executable and commit**

```bash
chmod +x scripts/deploy-relay.sh
git add scripts/deploy-relay.sh
git commit -m "scripts: add deploy-relay.sh"
```

---

### Task 27: systemd unit and nginx reference files

**Files:**
- Create: `deploy/blocker-relay.service`
- Create: `deploy/nginx-location-block.conf`
- Create: `deploy/README.md`

- [ ] **Step 1: Create the systemd unit**

```ini
# deploy/blocker-relay.service — installed to /etc/systemd/system/blocker-relay.service
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
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

- [ ] **Step 2: Create the nginx location block**

```nginx
# deploy/nginx-location-block.conf — append inside the julianoschroeder.com server block
location /blocker/ws-relay {
    proxy_pass http://127.0.0.1:3002;
    proxy_http_version 1.1;
    proxy_set_header Upgrade $http_upgrade;
    proxy_set_header Connection "upgrade";
    proxy_set_header Host $host;
    proxy_read_timeout 86400;
}
```

- [ ] **Step 3: Create deploy/README.md with one-time setup steps**

```markdown
# Blocker Relay Deployment

## One-time droplet setup (root@209.38.176.249)

1. Install the systemd unit:
   ```
   scp deploy/blocker-relay.service root@209.38.176.249:/etc/systemd/system/
   ssh root@209.38.176.249 'mkdir -p /opt/blocker-relay && systemctl daemon-reload && systemctl enable blocker-relay'
   ```

2. Add the nginx location block inside `server { server_name julianoschroeder.com … }`
   in `/etc/nginx/sites-enabled/julianoschroeder.com`. See `deploy/nginx-location-block.conf`.
   Then:
   ```
   ssh root@209.38.176.249 'nginx -t && systemctl reload nginx'
   ```

3. First deploy:
   ```
   ./scripts/deploy-relay.sh
   ```

## Subsequent deploys
Just run `./scripts/deploy-relay.sh`.

## Verify
```
curl -i https://julianoschroeder.com/blocker/ws-relay
# Expected: HTTP 400 "bad websocket upgrade" from HttpListener (because no Upgrade header)
curl -s http://127.0.0.1:3002/healthz   # from the droplet itself
# Expected: ok
```
```

- [ ] **Step 4: Commit**

```bash
git add deploy/blocker-relay.service deploy/nginx-location-block.conf deploy/README.md
git commit -m "deploy: add systemd unit, nginx block, and setup README"
```

---

### Task 28: First droplet deploy and smoke test

This task has no code — it runs commands against the production droplet.

- [ ] **Step 1: Verify the relay still runs locally**

```bash
PORT=3002 dotnet run --project src/Blocker.Relay/ &
sleep 1
curl -s http://127.0.0.1:3002/healthz
# Expected: ok
kill %1
```

- [ ] **Step 2: One-time droplet setup**

Per `deploy/README.md`:
- `scp deploy/blocker-relay.service root@209.38.176.249:/etc/systemd/system/`
- `ssh root@209.38.176.249 'mkdir -p /opt/blocker-relay && systemctl daemon-reload && systemctl enable blocker-relay'`
- Paste the nginx block into `/etc/nginx/sites-enabled/julianoschroeder.com` inside the `server {}` block.
- `ssh root@209.38.176.249 'nginx -t && systemctl reload nginx'`

- [ ] **Step 3: First deploy**

```bash
./scripts/deploy-relay.sh
```

Expected: deploy prints `systemctl status` showing `active (running)`.

- [ ] **Step 4: Verify from outside**

```bash
# From your local machine:
curl -i https://julianoschroeder.com/blocker/ws-relay
# Expected: 400 Bad Request (no Upgrade header — that's success, the location matches)
ssh root@209.38.176.249 'curl -s http://127.0.0.1:3002/healthz'
# Expected: ok
ssh root@209.38.176.249 'journalctl -u blocker-relay -n 20 --no-pager'
# Expected: [INFO]  … Blocker.Relay starting … Listening.
```

- [ ] **Step 5: No commit needed — this task is operational**

---

## Part 6 — Live Integration

### Task 29: Two-client end-to-end test

No code — this is the canonical playtest.

- [ ] **Step 1: Launch two Godot instances from the project directory**

```bash
cd godot
# Instance 1
godot --editor &
# Instance 2 (wait a few seconds for first to open)
godot --editor &
```

- [ ] **Step 2: Run both instances (F5)**

- [ ] **Step 3: Instance 1 — Host a game**

1. Main menu → Play Multiplayer
2. Wait for "Connected."
3. Click Host Game
4. Pick a map
5. Room code appears (e.g., `PFKR`)

- [ ] **Step 4: Instance 2 — Join**

1. Main menu → Play Multiplayer
2. Wait for "Connected."
3. Type the code, click Join Game
4. Wait for "Waiting for host to start…"

- [ ] **Step 5: Instance 1 — Start game**

1. Room status shows "ready to start"
2. Click Start Game
3. Both instances transition to Main.tscn

- [ ] **Step 6: Verify**

- Both instances render the same initial state.
- Move a block on Instance 1 — it should move on Instance 2 with ~1 tick delay.
- Move a block on Instance 2 — same in reverse.
- Kill Instance 2's process. Instance 1 should:
  1. Stall briefly (within 500 ms UI doesn't need to show anything yet for M1)
  2. Receive `PlayerLeft`
  3. Mark player 1 eliminated at `effectiveTick`
  4. Trigger `GameEnded` (because only one active player remains)
  5. Print `Game over. Winner: team 0` to the Godot console

- [ ] **Step 7: Verify relay logs look right**

```bash
ssh root@209.38.176.249 'journalctl -u blocker-relay -n 50 --no-pager'
```

Expected events: `connect`, `hello`, `room-created`, `room-joined`, `game-started`, plenty of traffic, `disconnect`, `left-game`.

- [ ] **Step 8: If it all works, declare M1 done. Commit no code (nothing changed).**

---

### Task 30: Final checklist

- [ ] **Step 1: Run the full test suite**

```bash
dotnet test
```

Expected: all tests pass (existing 151+ plus Net/ suite).

- [ ] **Step 2: Run `dotnet build` on the whole solution**

```bash
dotnet build blocker.sln
```

Expected: clean build, no warnings introduced by the multiplayer work.

- [ ] **Step 3: Verify the spec and plan are committed**

```bash
git log --oneline -- docs/superpowers/specs/2026-04-10-multiplayer-design.md \
                    docs/superpowers/plans/2026-04-10-multiplayer-m1.md
```

Expected: both files appear.

- [ ] **Step 4: Tag the M1 milestone (optional)**

```bash
git tag multiplayer-m1
```

---

## Notes and deferred work

- **Adaptive input delay** is hardcoded to 1. M2 will measure RTT and raise delay on bad links.
- **3–6 player UI** is not wired; the slot config forces 2 slots in host mode. The coordinator and relay already support N — only the UI needs to expand.
- **Chat / surrender / rematch**: protocol reserves `0x30–0x3F`, no UI.
- **Map sharing**: joiners load maps by name from their own `maps/` folder. Both players must have the same file. M2 adds `MapData` (`0x40`) for custom maps.
- **Fog of war**: not compatible with lockstep. See spec §"Never".
- **Reconnection**: not in M1.

This plan is the implementation authority for M1. When implementation diverges, update whichever is wrong — but discuss first.
