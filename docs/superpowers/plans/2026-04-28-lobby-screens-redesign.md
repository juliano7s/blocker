# Lobby Screens Redesign — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current MultiplayerMenu + SlotConfigScreen + MapSelectScreen with a lobby browser (LobbyListScreen) and a unified game lobby (GameLobbyScreen), adding room listing, ready state, room names, map miniature, and lobby chat.

**Architecture:** Protocol version bumps 2→3 to carry room names in CreateRoom/RoomState and ready flags per slot. Two new server handlers (ListRooms, SetReady) and three new client messages. UI is two new Godot scenes with programmatic layout using MenuGrid background + themed controls. MapMiniature renders a scaled-down grid from MapData. Lobby chat subscribes to the existing ChatMessage relay fan-out.

**Tech Stack:** C# (.NET 10), xUnit tests, Godot 4.6 + C#, WebSocket binary protocol with varint encoding.

**Spec:** `docs/superpowers/specs/2026-04-28-lobby-screens-redesign.md`

---

## File Structure

### New files
| File | Responsibility |
|------|---------------|
| `godot/Scripts/UI/LobbyListScreen.cs` | Lobby browser: relay connect, list rooms, host/join |
| `godot/Scripts/UI/GameLobbyScreen.cs` | Unified lobby: host, join, and single-player modes |
| `godot/Scripts/UI/MapMiniature.cs` | Mini GridRenderer that draws a scaled MapData preview |
| `godot/Scripts/UI/LobbyChatPanel.cs` | Chat panel: VBoxContainer with message labels + LineEdit |
| `godot/Scenes/LobbyList.tscn` | Minimal scene: Control root + LobbyListScreen.cs |
| `godot/Scenes/GameLobby.tscn` | Minimal scene: Control root + GameLobbyScreen.cs |
| `tests/Blocker.Simulation.Tests/Net/ReadyStateTests.cs` | Tests for ready state in FakeRelayClient |

### Modified files
| File | Changes |
|------|---------|
| `src/Blocker.Simulation/Net/Protocol.cs` | Add `ListRooms` (0x0C), `RoomList` (0x0D), `SetReady` (0x0E) constants; bump ProtocolVersion to 3 |
| `src/Blocker.Relay/Room.cs` | `SlotInfo` gains `bool IsReady`; `Room` gains `string RoomName` |
| `src/Blocker.Relay/RelayServer.cs` | CreateRoom parses roomName; BroadcastRoomState sends roomName + isReady per slot; new HandleListRooms + HandleSetReady |
| `godot/Scripts/Net/RelayClient.cs` | `RoomStatePayload` gains `RoomName`; `SlotStateEntry` gains `IsReady`; new `SendCreateRoom` overload with roomName; `SendListRooms`, `SendSetReady`; new `RoomListReceived` event; `RoomSummary` record |
| `src/Blocker.Simulation/Net/IRelayClient.cs` | Add `SendListRooms()`, `SendSetReady(bool)`, `RoomListReceived` event |
| `src/Blocker.Simulation/Net/FakeRelayClient.cs` | Implement new interface members; add room list + ready state simulation |
| `tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs` | `WrappedClient` gains new interface members |
| `tests/Blocker.Simulation.Tests/Net/ChatProtocolTests.cs` | (no changes needed) |
| `godot/Scripts/UI/MainMenu.cs` | Route "Play Multiplayer" to `LobbyList.tscn`, "Play vs AI" to `GameLobby.tscn` |

### Removed files (after new screens work)
| File | Replaced by |
|------|------------|
| `godot/Scripts/UI/MultiplayerMenu.cs` | `LobbyListScreen.cs` |
| `godot/Scripts/UI/SlotConfigScreen.cs` | `GameLobbyScreen.cs` |
| `godot/Scripts/UI/MapSelectScreen.cs` | Map selection integrated into `GameLobbyScreen` |
| `godot/Scenes/MultiplayerMenu.tscn` | `LobbyList.tscn` |
| `godot/Scenes/SlotConfig.tscn` | `GameLobby.tscn` |
| `godot/Scenes/MapSelect.tscn` | Removed |

---

## Task 1: Protocol Constants & Data Types

**Files:**
- Modify: `src/Blocker.Simulation/Net/Protocol.cs`

- [ ] **Step 1: Add new message constants and bump protocol version**

In `Protocol.cs`, bump the version and add three new constants in the session range:

```csharp
public const byte ProtocolVersion = 3; // was 2
```

After `KickPlayer = 0x0B`, add:

```csharp
public const byte ListRooms  = 0x0C;
public const byte RoomList   = 0x0D;
public const byte SetReady   = 0x0E;
```

- [ ] **Step 2: Run tests to verify nothing breaks**

Run: `dotnet test tests/Blocker.Simulation.Tests/`
Expected: all 208+ tests pass (the constant values are just new bytes; nothing references them yet except the ChatMessage constant test which is unaffected).

- [ ] **Step 3: Commit**

```bash
git add src/Blocker.Simulation/Net/Protocol.cs
git commit -m "proto: add ListRooms, RoomList, SetReady constants; bump protocol v3"
```

---

## Task 2: Room Model Changes

**Files:**
- Modify: `src/Blocker.Relay/Room.cs`

- [ ] **Step 1: Add RoomName to Room and IsReady to SlotInfo**

In `Room.cs`:

Add a new property to `Room` after `GameMode`:
```csharp
public string RoomName { get; set; } = "";
```

Update `SlotInfo` record to include `IsReady`:
```csharp
public sealed record SlotInfo(
    Guid? OwnerId,
    string DisplayName,
    byte ColorIndex,
    byte TeamId,
    bool IsOpen,
    bool IsClosed,
    bool IsReady);
```

- [ ] **Step 2: Fix all SlotInfo construction sites in RelayServer.cs**

Every `new SlotInfo(...)` call in `RelayServer.cs` needs the new `IsReady` parameter appended. Search for all occurrences. Each one should get `, IsReady: false` appended. There are approximately 7 construction sites:

1. `HandleCreateRoom` — host slot 0: `..., IsOpen: false, IsClosed: false, IsReady: false)`
2. `HandleCreateRoom` — open slots loop: `..., IsOpen: true, IsClosed: false, IsReady: false)`
3. `HandleJoinRoom` — chosen slot: `..., IsOpen: false, IsClosed: false, IsReady: false)`
4. `HandleUpdateRoom` — re-seated filled slots: `..., IsOpen: false, IsClosed: false, IsReady: false)` (ready resets on settings change per spec)
5. `HandleUpdateRoom` — new open slots: `..., IsOpen: true, IsClosed: false, IsReady: false)`
6. `HandleKickPlayer` — reopened slot: `..., IsOpen: true, IsClosed: false, IsReady: false)`
7. `RemoveConnFromRoom` — reopened slot: `..., IsOpen: true, IsClosed: false, IsReady: false)`

- [ ] **Step 3: Build to verify**

Run: `dotnet build src/Blocker.Relay/`
Expected: success (no test project for relay, but must compile).

- [ ] **Step 4: Commit**

```bash
git add src/Blocker.Relay/Room.cs src/Blocker.Relay/RelayServer.cs
git commit -m "relay: add RoomName to Room, IsReady to SlotInfo"
```

---

## Task 3: Server Wire Format — RoomName + IsReady in RoomState

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

- [ ] **Step 1: Update CreateRoom to parse roomName**

In `HandleCreateRoom`, the new wire layout is:
`[0x03][slotCount][gameMode][roomNameLen:varint][roomName][mapNameLen:varint][mapName][blobLen:varint][blob]`

The roomName is inserted before mapName. Update parsing in `HandleCreateRoom`:

```csharp
private void HandleCreateRoom(Connection conn, ReadOnlySpan<byte> payload, CancellationToken ct)
{
    if (payload.Length < 4) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    byte slotCount = payload[1];
    byte gameMode = payload[2];
    // NEW: parse roomName first
    var (roomNameLen, cr) = Varint.Read(payload, 3);
    int pos = 3 + cr;
    if (pos + (int)roomNameLen > payload.Length) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    string roomName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)roomNameLen));
    pos += (int)roomNameLen;
    // Then mapName
    var (nameLen, c1) = Varint.Read(payload, pos);
    pos += c1;
    if (pos + (int)nameLen > payload.Length) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    string mapName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)nameLen));
    pos += (int)nameLen;
    var (blobLen, c2) = Varint.Read(payload, pos); pos += c2;
    if (pos + (int)blobLen > payload.Length) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
    var mapBlob = payload.Slice(pos, (int)blobLen).ToArray();

    var room = _rooms.TryCreate(conn.Id, conn.RemoteIp, _opts.MaxRoomsPerIp,
                                 conn.SimulationVersion, mapBlob, mapName, slotCount, gameMode);
    if (room == null) { _ = SendError(conn, ErrorCode.TooManyRooms, ct); return; }

    room.RoomName = roomName; // NEW

    room.Slots[0] = new SlotInfo(conn.Id, conn.ClientName, 0, TeamForSlot(0, gameMode), IsOpen: false, IsClosed: false, IsReady: false);
    for (byte i = 1; i < slotCount; i++)
        room.Slots[i] = new SlotInfo(null, "", i, TeamForSlot(i, gameMode), IsOpen: true, IsClosed: false, IsReady: false);

    conn.CurrentRoom = room;
    conn.AssignedPlayerId = 0;
    Logger.Info($"conn={conn.Id} event=room-created code={room.Code} name={roomName} map={mapName} slots={slotCount} mode={gameMode}");
    _ = BroadcastRoomState(room, ct);
}
```

- [ ] **Step 2: Update BroadcastRoomState to include roomName + isReady**

New RoomState wire layout:
```
[0x05][code:4][hostId:16][slotCount][sim:uint16 LE][gameMode]
[roomNameLen:varint][roomName]   ← NEW
[mapNameLen:varint][mapName]
per slot: [ownerNameLen:varint][ownerName][colorIdx][teamId][flags]
```

Flags byte now uses bit 2 for IsReady: `(IsOpen ? 1 : 0) | (IsClosed ? 2 : 0) | (IsReady ? 4 : 0)`

In `BroadcastRoomState`, after writing `ms.WriteByte(room.GameMode);`, insert roomName encoding before mapName:

```csharp
// Room name (NEW)
var roomNameBytes = System.Text.Encoding.UTF8.GetBytes(room.RoomName);
vl = Varint.Write(varintBuf, 0, (uint)roomNameBytes.Length);
ms.Write(varintBuf, 0, vl);
ms.Write(roomNameBytes, 0, roomNameBytes.Length);
```

And update the flags line in the per-slot loop:
```csharp
byte flags = (byte)((s.IsOpen ? 1 : 0) | (s.IsClosed ? 2 : 0) | (s.IsReady ? 4 : 0));
```

- [ ] **Step 3: Update HandleUpdateRoom to parse roomName (same format change)**

`HandleUpdateRoom` parses the same layout as CreateRoom. Insert roomName parsing between gameMode and mapName, same pattern as Step 1. The parsed roomName should be stored:
```csharp
room.RoomName = newRoomName;
```

Note: the ready state is already reset by re-creating SlotInfo with `IsReady: false` in Task 2 Step 2.

- [ ] **Step 4: Update the wire format comment block**

At the top of the message handlers section (~line 224), update the comment:
```csharp
//   CreateRoom: [0x03][slotCount:byte][gameMode:byte][roomNameLen:varint][roomName][mapNameLen:varint][mapName][mapBlobLen:varint][mapBlob]
//   RoomState:  [0x05][code:4][hostId:16][slotCount:byte][sim:uint16 LE][gameMode:byte]
//               [roomNameLen:varint][roomName]
//               [mapNameLen:varint][mapName]
//               per slot: [ownerNameLen:varint][ownerName][colorIdx:byte][teamId:byte][flags:byte(bit0=open,bit1=closed,bit2=ready)]
```

- [ ] **Step 5: Build to verify**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 6: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs
git commit -m "relay: wire roomName into CreateRoom/UpdateRoom/RoomState, isReady flag in slot"
```

---

## Task 4: Server — ListRooms + SetReady Handlers

**Files:**
- Modify: `src/Blocker.Relay/RelayServer.cs`

- [ ] **Step 1: Add dispatch cases for new message types**

In the `switch` block inside `ProcessMessage` (around line 210), add before the `default:` case:

```csharp
case Protocol.ListRooms: await HandleListRooms(conn, ct); break;
case Protocol.SetReady:  await HandleSetReady(conn, buf, len, ct); break;
```

- [ ] **Step 2: Implement HandleListRooms**

Add this method near the other handlers:

```csharp
private async Task HandleListRooms(Connection conn, CancellationToken ct)
{
    var ms = new MemoryStream();
    ms.WriteByte(Protocol.RoomList);

    var lobbies = _rooms.All()
        .Where(r => r.Lifecycle == RoomLifecycle.Lobby)
        .ToList();

    var varintBuf = new byte[5];
    int vl = Varint.Write(varintBuf, 0, (uint)lobbies.Count);
    ms.Write(varintBuf, 0, vl);

    foreach (var room in lobbies)
    {
        // code: 4 ASCII bytes
        ms.Write(System.Text.Encoding.ASCII.GetBytes(room.Code));
        // roomName: varint + UTF-8
        var nameBytes = System.Text.Encoding.UTF8.GetBytes(room.RoomName);
        vl = Varint.Write(varintBuf, 0, (uint)nameBytes.Length);
        ms.Write(varintBuf, 0, vl);
        ms.Write(nameBytes, 0, nameBytes.Length);
        // playerCount: byte (filled slots)
        byte playerCount = (byte)room.Slots.Values.Count(s => s.OwnerId != null);
        ms.WriteByte(playerCount);
        // slotCount: byte
        ms.WriteByte((byte)room.SlotCount);
        // mapName: varint + UTF-8
        var mapBytes = System.Text.Encoding.UTF8.GetBytes(room.MapName);
        vl = Varint.Write(varintBuf, 0, (uint)mapBytes.Length);
        ms.Write(varintBuf, 0, vl);
        ms.Write(mapBytes, 0, mapBytes.Length);
        // gameMode: byte
        ms.WriteByte(room.GameMode);
    }

    var bytes = ms.ToArray();
    try { await conn.Ws.SendAsync(bytes, WebSocketMessageType.Binary, true, ct); } catch { }
}
```

- [ ] **Step 3: Implement HandleSetReady**

```csharp
private async Task HandleSetReady(Connection conn, byte[] buf, int len, CancellationToken ct)
{
    if (conn.CurrentRoom is not Room room) { await SendError(conn, ErrorCode.NotInRoom, ct); return; }
    if (room.Lifecycle != RoomLifecycle.Lobby) return;
    if (conn.AssignedPlayerId is not byte slotId) return;
    if (len < 2) return;

    // Host can't set ready (host controls the start button)
    if (conn.Id == room.HostId) return;

    bool ready = buf[1] != 0;
    if (!room.Slots.TryGetValue(slotId, out var slot)) return;
    room.Slots[slotId] = slot with { IsReady = ready };
    room.LastActivity = DateTime.UtcNow;
    Logger.Info($"conn={conn.Id} event=set-ready code={room.Code} slot={slotId} ready={ready}");
    await BroadcastRoomState(room, ct);
}
```

- [ ] **Step 4: Build to verify**

Run: `dotnet build src/Blocker.Relay/`
Expected: success.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Relay/RelayServer.cs
git commit -m "relay: implement ListRooms and SetReady handlers"
```

---

## Task 5: Client — Updated Payloads + New Methods

**Files:**
- Modify: `godot/Scripts/Net/RelayClient.cs`

- [ ] **Step 1: Update RoomStatePayload and SlotStateEntry records**

At the bottom of `RelayClient.cs`, update the records:

```csharp
public sealed record SlotStateEntry(
    string DisplayName, byte ColorIndex, byte TeamId,
    bool IsOpen, bool IsClosed, bool IsReady);

public sealed record RoomStatePayload(
    string Code, Guid HostId, ushort SimulationVersion,
    GameMode GameMode, string RoomName, string MapName,
    SlotStateEntry[] Slots);

public sealed record RoomSummary(
    string Code, string RoomName, byte PlayerCount,
    byte SlotCount, string MapName, byte GameMode);
```

- [ ] **Step 2: Update ParseRoomState to read roomName + isReady**

```csharp
public static RoomStatePayload ParseRoomState(byte[] msg)
{
    int o = 1;
    string code = System.Text.Encoding.ASCII.GetString(msg, o, 4); o += 4;
    var hostBytes = new byte[16]; Array.Copy(msg, o, hostBytes, 0, 16); o += 16;
    byte slotCount = msg[o++];
    ushort simVer = (ushort)(msg[o] | (msg[o + 1] << 8)); o += 2;
    GameMode gameMode = (GameMode)msg[o++];
    // Room name (NEW)
    var (roomNameLen, cr) = Varint.Read(msg, o); o += cr;
    string roomName = System.Text.Encoding.UTF8.GetString(msg, o, (int)roomNameLen); o += (int)roomNameLen;
    // Map name
    var (mapNameLen, c1) = Varint.Read(msg, o); o += c1;
    string mapName = System.Text.Encoding.UTF8.GetString(msg, o, (int)mapNameLen); o += (int)mapNameLen;
    var slots = new SlotStateEntry[slotCount];
    for (int i = 0; i < slotCount; i++)
    {
        var (nLen, c2) = Varint.Read(msg, o); o += c2;
        string name = System.Text.Encoding.UTF8.GetString(msg, o, (int)nLen); o += (int)nLen;
        byte colorIdx = msg[o++];
        byte teamId = msg[o++];
        byte flags = msg[o++];
        slots[i] = new SlotStateEntry(name, colorIdx, teamId,
            IsOpen: (flags & 1) != 0, IsClosed: (flags & 2) != 0,
            IsReady: (flags & 4) != 0);
    }
    return new RoomStatePayload(code, new Guid(hostBytes), simVer, gameMode, roomName, mapName, slots);
}
```

- [ ] **Step 3: Update SendCreateRoom to include roomName**

```csharp
public void SendCreateRoom(byte slotCount, GameMode gameMode, string roomName, string mapName, byte[] mapBlob)
{
    var roomNameBytes = System.Text.Encoding.UTF8.GetBytes(roomName);
    var nameBytes = System.Text.Encoding.UTF8.GetBytes(mapName);
    var varBuf = new byte[5];
    int vr = Varint.Write(varBuf, 0, (uint)roomNameBytes.Length);
    var varBuf2 = new byte[5];
    int vn = Varint.Write(varBuf2, 0, (uint)nameBytes.Length);
    var varBuf3 = new byte[5];
    int vb = Varint.Write(varBuf3, 0, (uint)mapBlob.Length);
    var msg = new byte[3 + vr + roomNameBytes.Length + vn + nameBytes.Length + vb + mapBlob.Length];
    int o = 0;
    msg[o++] = Protocol.CreateRoom;
    msg[o++] = slotCount;
    msg[o++] = (byte)gameMode;
    Array.Copy(varBuf, 0, msg, o, vr); o += vr;
    Array.Copy(roomNameBytes, 0, msg, o, roomNameBytes.Length); o += roomNameBytes.Length;
    Array.Copy(varBuf2, 0, msg, o, vn); o += vn;
    Array.Copy(nameBytes, 0, msg, o, nameBytes.Length); o += nameBytes.Length;
    Array.Copy(varBuf3, 0, msg, o, vb); o += vb;
    Array.Copy(mapBlob, 0, msg, o, mapBlob.Length);
    _outbox.Writer.TryWrite(msg);
}
```

- [ ] **Step 4: Update SendUpdateRoom to include roomName (same format)**

Same change as SendCreateRoom — add a `string roomName` parameter and insert it before mapName in the wire format. The message type byte is `Protocol.UpdateRoom` (0x0A) instead of `Protocol.CreateRoom`.

```csharp
public void SendUpdateRoom(byte slotCount, GameMode gameMode, string roomName, string mapName, byte[] mapBlob)
{
    // Same body as SendCreateRoom but msg[0] = Protocol.UpdateRoom
    // ... (identical serialization logic)
}
```

- [ ] **Step 5: Add SendListRooms and SendSetReady**

```csharp
public void SendListRooms() =>
    _outbox.Writer.TryWrite(new byte[] { Protocol.ListRooms });

public void SendSetReady(bool ready) =>
    _outbox.Writer.TryWrite(new byte[] { Protocol.SetReady, (byte)(ready ? 1 : 0) });
```

- [ ] **Step 6: Add RoomListReceived event and parsing**

Add the event field near the other lobby events:
```csharp
public event Action<RoomSummary[]>? RoomListReceived;
```

Add a dispatch case in the `Dispatch` method's switch block:
```csharp
case Protocol.RoomList:
{
    var (count, vc) = Varint.Read(msg, 1);
    int o = 1 + vc;
    var summaries = new RoomSummary[(int)count];
    for (int i = 0; i < (int)count; i++)
    {
        string code = System.Text.Encoding.ASCII.GetString(msg, o, 4); o += 4;
        var (rnLen, rc) = Varint.Read(msg, o); o += rc;
        string roomName = System.Text.Encoding.UTF8.GetString(msg, o, (int)rnLen); o += (int)rnLen;
        byte playerCount = msg[o++];
        byte slotCount = msg[o++];
        var (mnLen, mc) = Varint.Read(msg, o); o += mc;
        string mapName = System.Text.Encoding.UTF8.GetString(msg, o, (int)mnLen); o += (int)mnLen;
        byte gameMode = msg[o++];
        summaries[i] = new RoomSummary(code, roomName, playerCount, slotCount, mapName, gameMode);
    }
    RoomListReceived?.Invoke(summaries);
    break;
}
```

- [ ] **Step 7: Build to check for compile errors**

Run: `dotnet build godot/Blocker.Game.csproj` (or open in Godot). This will likely fail because `SlotConfigScreen.cs` and other callers use the old `SendCreateRoom(4 args)` signature and old `RoomStatePayload` constructor. That's expected — we'll fix callers in subsequent steps.

Note: if building via `dotnet build` from root fails on the Godot project (missing Godot SDK), the simulation library tests are the gating check. We verify Godot compilation via Godot editor later.

- [ ] **Step 8: Commit**

```bash
git add godot/Scripts/Net/RelayClient.cs
git commit -m "client: updated wire format - roomName, isReady, ListRooms, SetReady, RoomList parsing"
```

---

## Task 6: Interface + FakeRelayClient + Test Adapter

**Files:**
- Modify: `src/Blocker.Simulation/Net/IRelayClient.cs`
- Modify: `src/Blocker.Simulation/Net/FakeRelayClient.cs`
- Modify: `tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs`

- [ ] **Step 1: Add new members to IRelayClient**

```csharp
void SendListRooms();
void SendSetReady(bool ready);
event Action<int /*slotId*/, string /*text*/>? ChatReceived;
// Note: RoomListReceived is lobby-level, not on IRelayClient (it's on RelayClient directly,
// same pattern as HelloAcked/RoomStateReceived/GameStarted — those are not on IRelayClient).
```

Wait — check current IRelayClient. It has SendChat and ChatReceived already (from the working tree diff). SendListRooms and SendSetReady are new additions:

```csharp
void SendListRooms();
void SendSetReady(bool ready);
```

- [ ] **Step 2: Implement in FakeRelayClient**

```csharp
public void SendListRooms() { /* noop in tests — no room registry */ }
public void SendSetReady(bool ready) { /* noop in tests */ }
```

- [ ] **Step 3: Update WrappedClient in LockstepCoordinatorTests**

Add the forwarding members:
```csharp
public void SendListRooms() => _inner.SendListRooms();
public void SendSetReady(bool ready) => _inner.SendSetReady(ready);
```

- [ ] **Step 4: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/`
Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/Blocker.Simulation/Net/IRelayClient.cs src/Blocker.Simulation/Net/FakeRelayClient.cs tests/Blocker.Simulation.Tests/Net/LockstepCoordinatorTests.cs
git commit -m "interface: add SendListRooms, SendSetReady to IRelayClient"
```

---

## Task 7: Ready State Tests

**Files:**
- Create: `tests/Blocker.Simulation.Tests/Net/ReadyStateTests.cs`

- [ ] **Step 1: Write tests for new protocol constants**

```csharp
using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class ReadyStateTests
{
    [Fact]
    public void Protocol_Constants_In_Session_Range()
    {
        Assert.Equal(0x0C, Protocol.ListRooms);
        Assert.Equal(0x0D, Protocol.RoomList);
        Assert.Equal(0x0E, Protocol.SetReady);
    }

    [Fact]
    public void Protocol_Version_Is_3()
    {
        Assert.Equal(3, Protocol.ProtocolVersion);
    }

    [Fact]
    public void FakeRelayClient_SendSetReady_Does_Not_Throw()
    {
        var c = new FakeRelayClient(0);
        c.SendSetReady(true);
        c.SendSetReady(false);
    }

    [Fact]
    public void FakeRelayClient_SendListRooms_Does_Not_Throw()
    {
        var c = new FakeRelayClient(0);
        c.SendListRooms();
    }
}
```

- [ ] **Step 2: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/`
Expected: all pass including 4 new tests.

- [ ] **Step 3: Commit**

```bash
git add tests/Blocker.Simulation.Tests/Net/ReadyStateTests.cs
git commit -m "test: add ReadyState protocol tests"
```

---

## Task 8: Scene Files

**Files:**
- Create: `godot/Scenes/LobbyList.tscn`
- Create: `godot/Scenes/GameLobby.tscn`

- [ ] **Step 1: Create LobbyList.tscn**

```
[gd_scene load_steps=2 format=3 uid="uid://lobby_list"]

[ext_resource type="Script" path="res://Scripts/UI/LobbyListScreen.cs" id="1"]

[node name="LobbyListScreen" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
script = ExtResource("1")
```

- [ ] **Step 2: Create GameLobby.tscn**

```
[gd_scene load_steps=2 format=3 uid="uid://game_lobby"]

[ext_resource type="Script" path="res://Scripts/UI/GameLobbyScreen.cs" id="1"]

[node name="GameLobbyScreen" type="Control"]
layout_mode = 3
anchors_preset = 15
anchor_right = 1.0
anchor_bottom = 1.0
script = ExtResource("1")
```

- [ ] **Step 3: Commit**

```bash
git add godot/Scenes/LobbyList.tscn godot/Scenes/GameLobby.tscn
git commit -m "scenes: add LobbyList and GameLobby scene files"
```

---

## Task 9: MapMiniature Component

**Files:**
- Create: `godot/Scripts/UI/MapMiniature.cs`

The MapMiniature draws a scaled-down preview of a `MapData` object — terrain, ground types, spawn positions — using Godot's `_Draw()` override. It scales to fit its container.

- [ ] **Step 1: Implement MapMiniature**

```csharp
using Blocker.Simulation.Maps;
using Godot;

namespace Blocker.Game.UI;

public partial class MapMiniature : Control
{
    private MapData? _mapData;
    private float _cellSize;

    private static readonly Color BgColor = new(0.06f, 0.06f, 0.06f);
    private static readonly Color GridLineColor = new(0.267f, 0.667f, 1f, 0.08f);
    private static readonly Color NormalGroundColor = new(0.12f, 0.12f, 0.12f);
    private static readonly Color BootColor = new(0.15f, 0.35f, 0.15f);
    private static readonly Color OverloadColor = new(0.35f, 0.15f, 0.15f);
    private static readonly Color ProtoColor = new(0.15f, 0.15f, 0.35f);
    private static readonly Color WallColor = new(0.5f, 0.5f, 0.5f);
    private static readonly Color BreakableColor = new(0.4f, 0.35f, 0.25f);
    private static readonly Color FragileColor = new(0.3f, 0.25f, 0.2f);
    private static readonly Color SpawnColor = new(1f, 0.667f, 0.2f);

    public void SetMap(MapData? map)
    {
        _mapData = map;
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_mapData == null) return;

        var size = Size;
        float scaleX = size.X / _mapData.Width;
        float scaleY = size.Y / _mapData.Height;
        _cellSize = Mathf.Min(scaleX, scaleY);

        float totalW = _mapData.Width * _cellSize;
        float totalH = _mapData.Height * _cellSize;
        float ox = (size.X - totalW) * 0.5f;
        float oy = (size.Y - totalH) * 0.5f;

        // Background
        DrawRect(new Rect2(ox, oy, totalW, totalH), BgColor);

        // Ground tiles
        foreach (var g in _mapData.Ground)
        {
            var color = g.Type switch
            {
                GroundType.Boot => BootColor,
                GroundType.Overload => OverloadColor,
                GroundType.Proto => ProtoColor,
                _ => NormalGroundColor
            };
            DrawRect(new Rect2(ox + g.X * _cellSize, oy + g.Y * _cellSize, _cellSize, _cellSize), color);
        }

        // Terrain (walls)
        foreach (var t in _mapData.Terrain)
        {
            if (t.Type == TerrainType.None) continue;
            var color = t.Type switch
            {
                TerrainType.Breakable => BreakableColor,
                TerrainType.Fragile => FragileColor,
                _ => WallColor
            };
            DrawRect(new Rect2(ox + t.X * _cellSize, oy + t.Y * _cellSize, _cellSize, _cellSize), color);
        }

        // Grid lines (only if cells are large enough to see)
        if (_cellSize >= 3f)
        {
            for (int x = 0; x <= _mapData.Width; x++)
                DrawLine(new Vector2(ox + x * _cellSize, oy), new Vector2(ox + x * _cellSize, oy + totalH), GridLineColor);
            for (int y = 0; y <= _mapData.Height; y++)
                DrawLine(new Vector2(ox, oy + y * _cellSize), new Vector2(ox + totalW, oy + y * _cellSize), GridLineColor);
        }

        // Spawn positions (unit entries)
        foreach (var u in _mapData.Units)
        {
            float cx = ox + u.X * _cellSize + _cellSize * 0.5f;
            float cy = oy + u.Y * _cellSize + _cellSize * 0.5f;
            float r = Mathf.Max(_cellSize * 0.3f, 1.5f);
            DrawCircle(new Vector2(cx, cy), r, SpawnColor);
        }
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/UI/MapMiniature.cs
git commit -m "ui: add MapMiniature component for map preview in lobby"
```

---

## Task 10: LobbyChatPanel Component

**Files:**
- Create: `godot/Scripts/UI/LobbyChatPanel.cs`

A simple chat panel: a VBoxContainer with ScrollContainer for messages and a LineEdit for input. Subscribes to `RelayClient.ChatReceived`.

- [ ] **Step 1: Implement LobbyChatPanel**

```csharp
using Blocker.Game.Net;
using Godot;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public partial class LobbyChatPanel : VBoxContainer
{
    private ScrollContainer _scroll = null!;
    private VBoxContainer _messageList = null!;
    private LineEdit _input = null!;
    private RelayClient? _relay;
    private Action<int, string>? _chatHandler;
    private readonly Dictionary<int, string> _slotNames = new();

    private static readonly Color ChatBg = new(0.04f, 0.04f, 0.06f);
    private static readonly Color InputBg = new(0.08f, 0.08f, 0.1f);
    private static readonly Color TextColor = new(0.8f, 0.85f, 0.9f);
    private static readonly Color NameColor = new(0.267f, 0.667f, 1f);

    public override void _Ready()
    {
        var headerLabel = new Label { Text = "Chat" };
        headerLabel.AddThemeFontSizeOverride("font_size", 14);
        AddChild(headerLabel);

        _scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 120)
        };
        AddChild(_scroll);

        _messageList = new VBoxContainer();
        _messageList.AddThemeConstantOverride("separation", 2);
        _scroll.AddChild(_messageList);

        _input = new LineEdit
        {
            PlaceholderText = "Type a message…",
            MaxLength = 128,
            CustomMinimumSize = new Vector2(0, 30)
        };
        _input.TextSubmitted += OnTextSubmitted;
        AddChild(_input);
    }

    public void SetRelay(RelayClient relay, Dictionary<int, string>? slotNames = null)
    {
        DetachRelay();
        _relay = relay;
        if (slotNames != null)
        {
            _slotNames.Clear();
            foreach (var kv in slotNames) _slotNames[kv.Key] = kv.Value;
        }
        _chatHandler = (slotId, text) => CallDeferred(nameof(OnChatDeferred), slotId, text);
        _relay.ChatReceived += _chatHandler;
    }

    public void UpdateSlotNames(Dictionary<int, string> names)
    {
        _slotNames.Clear();
        foreach (var kv in names) _slotNames[kv.Key] = kv.Value;
    }

    public void DetachRelay()
    {
        if (_relay != null && _chatHandler != null)
            _relay.ChatReceived -= _chatHandler;
        _relay = null;
        _chatHandler = null;
    }

    public override void _ExitTree()
    {
        DetachRelay();
    }

    private void OnChatDeferred(int slotId, string text)
    {
        string name = _slotNames.TryGetValue(slotId, out var n) ? n : $"Player {slotId}";
        AddChatMessage(name, text);
    }

    public void AddChatMessage(string playerName, string text)
    {
        var label = new RichTextLabel
        {
            BbcodeEnabled = true,
            FitContent = true,
            ScrollActive = false,
            CustomMinimumSize = new Vector2(0, 20),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        label.Text = $"[color=#{NameColor.ToHtml(false)}]{playerName}[/color]: {text}";
        _messageList.AddChild(label);

        // Auto-scroll to bottom
        CallDeferred(nameof(ScrollToBottom));

        // Limit visible messages
        while (_messageList.GetChildCount() > 50)
            _messageList.GetChild(0).QueueFree();
    }

    public void AddSystemMessage(string text)
    {
        var label = new Label
        {
            Text = text,
            CustomMinimumSize = new Vector2(0, 18)
        };
        label.AddThemeColorOverride("font_color", new Color(0.5f, 0.6f, 0.7f));
        label.AddThemeFontSizeOverride("font_size", 12);
        _messageList.AddChild(label);
        CallDeferred(nameof(ScrollToBottom));
    }

    private void ScrollToBottom()
    {
        _scroll.ScrollVertical = (int)_scroll.GetVScrollBar().MaxValue;
    }

    private void OnTextSubmitted(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _relay?.SendChat(text);
        // Show own message locally (relay doesn't echo back to sender)
        AddChatMessage("You", text);
        _input.Text = "";
    }
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/UI/LobbyChatPanel.cs
git commit -m "ui: add LobbyChatPanel for lobby screens"
```

---

## Task 11: LobbyListScreen

**Files:**
- Create: `godot/Scripts/UI/LobbyListScreen.cs`

This screen replaces `MultiplayerMenu`. It connects to the relay, polls for rooms, and lets the player host or join. Uses MenuGrid background.

- [ ] **Step 1: Implement LobbyListScreen**

```csharp
using Blocker.Game.Net;
using Blocker.Game.Rendering;
using Blocker.Simulation.Net;
using Godot;

namespace Blocker.Game.UI;

public partial class LobbyListScreen : Control
{
    private MenuGrid _menuGrid = null!;
    private LineEdit _playerNameEdit = null!;
    private LineEdit _lobbyNameEdit = null!;
    private VBoxContainer _lobbyList = null!;
    private Label _statusLabel = null!;
    private Label _countLabel = null!;
    private RelayClient? _relay;
    private Timer? _pollTimer;
    private bool _connected;

    // Stored delegates for cleanup
    private Action? _helloAckedHandler;
    private Action? _closedHandler;
    private Action<ErrorCode>? _errorHandler;
    private Action<RoomSummary[]>? _roomListHandler;
    private Action<RoomStatePayload>? _roomStateHandler;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Grid background
        _menuGrid = new MenuGrid { Name = "MenuGrid" };
        AddChild(_menuGrid);

        // Main layout panel (centered, semi-transparent)
        var panel = new PanelContainer();
        panel.AnchorLeft = 0.05f; panel.AnchorRight = 0.95f;
        panel.AnchorTop = 0.05f; panel.AnchorBottom = 0.95f;
        var panelStyle = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.06f, 0.92f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 20, ContentMarginRight = 20,
            ContentMarginTop = 16, ContentMarginBottom = 16
        };
        panel.AddThemeStyleboxOverride("panel", panelStyle);
        AddChild(panel);

        var vbox = new VBoxContainer();
        vbox.AddThemeConstantOverride("separation", 10);
        panel.AddChild(vbox);

        // Title
        var title = new Label
        {
            Text = "MULTIPLAYER LOBBY",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        title.AddThemeFontSizeOverride("font_size", 28);
        title.AddThemeColorOverride("font_color", new Color(0.267f, 0.667f, 1f));
        vbox.AddChild(title);

        // Top bar: player name | lobby name | HOST button
        var topBar = new HBoxContainer();
        topBar.AddThemeConstantOverride("separation", 10);
        vbox.AddChild(topBar);

        topBar.AddChild(new Label { Text = "Name:" });
        _playerNameEdit = new LineEdit
        {
            PlaceholderText = "Your name",
            Text = "Player",
            MaxLength = 16,
            CustomMinimumSize = new Vector2(140, 0)
        };
        topBar.AddChild(_playerNameEdit);

        topBar.AddChild(new Label { Text = "Lobby:" });
        _lobbyNameEdit = new LineEdit
        {
            PlaceholderText = "Lobby name",
            Text = "My Game",
            MaxLength = 32,
            CustomMinimumSize = new Vector2(180, 0),
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        topBar.AddChild(_lobbyNameEdit);

        var hostBtn = new Button
        {
            Text = "HOST NEW",
            CustomMinimumSize = new Vector2(120, 36)
        };
        var hostStyle = new StyleBoxFlat { BgColor = new Color(0.267f, 0.667f, 1f, 0.8f) };
        hostBtn.AddThemeStyleboxOverride("normal", hostStyle);
        hostBtn.Pressed += OnHostPressed;
        topBar.AddChild(hostBtn);

        vbox.AddChild(new HSeparator());

        // Status
        _statusLabel = new Label
        {
            Text = "Connecting to server…",
            HorizontalAlignment = HorizontalAlignment.Center
        };
        vbox.AddChild(_statusLabel);

        // Lobby table header
        var headerRow = new HBoxContainer();
        headerRow.AddThemeConstantOverride("separation", 8);
        headerRow.AddChild(new Label { Text = "Lobby Name", CustomMinimumSize = new Vector2(200, 0), SizeFlagsHorizontal = SizeFlags.ExpandFill });
        headerRow.AddChild(new Label { Text = "Players", CustomMinimumSize = new Vector2(70, 0) });
        headerRow.AddChild(new Label { Text = "Map", CustomMinimumSize = new Vector2(140, 0) });
        headerRow.AddChild(new Label { Text = "", CustomMinimumSize = new Vector2(80, 0) }); // JOIN button column
        vbox.AddChild(headerRow);

        // Scrollable lobby list
        var scroll = new ScrollContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 200)
        };
        vbox.AddChild(scroll);

        _lobbyList = new VBoxContainer();
        _lobbyList.AddThemeConstantOverride("separation", 4);
        _lobbyList.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        scroll.AddChild(_lobbyList);

        vbox.AddChild(new HSeparator());

        // Bottom bar: BACK button + lobby count
        var bottomBar = new HBoxContainer();
        vbox.AddChild(bottomBar);

        var backBtn = new Button
        {
            Text = "< BACK",
            CustomMinimumSize = new Vector2(100, 36)
        };
        backBtn.Pressed += () =>
        {
            _relay?.Dispose();
            GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        };
        bottomBar.AddChild(backBtn);

        bottomBar.AddChild(new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill }); // spacer

        _countLabel = new Label { Text = "0 lobbies" };
        bottomBar.AddChild(_countLabel);

        // Connect to relay
        ConnectToRelay();
    }

    private async void ConnectToRelay()
    {
        _relay = new RelayClient();
        string playerName = _playerNameEdit.Text.Trim();
        if (string.IsNullOrEmpty(playerName)) playerName = "Player";
        bool ok = await _relay.ConnectAsync(RelayClientConfig.ResolvedUrl, playerName);
        if (!ok)
        {
            _statusLabel.Text = $"Cannot reach server: {_relay.LastError}";
            return;
        }

        _helloAckedHandler = () => CallDeferred(nameof(OnHelloAcked));
        _closedHandler = () => CallDeferred(nameof(OnClosed));
        _errorHandler = (e) => CallDeferred(nameof(OnServerError), (int)e);
        _roomListHandler = (rooms) => CallDeferred(nameof(OnRoomListDeferred), new Godot.Collections.Array(
            rooms.Select(r => new Godot.Collections.Array { r.Code, r.RoomName, (int)r.PlayerCount, (int)r.SlotCount, r.MapName, (int)r.GameMode }).ToArray()
        ));
        _roomStateHandler = (rs) => CallDeferred(nameof(OnRoomJoinedDeferred));

        _relay.HelloAcked += _helloAckedHandler;
        _relay.ConnectionClosed += _closedHandler;
        _relay.ServerError += _errorHandler;
        _relay.RoomListReceived += _roomListHandler;
        _relay.RoomStateReceived += _roomStateHandler;

        var drain = new Timer { WaitTime = 0.016, Autostart = true };
        drain.Timeout += () => _relay?.DrainInbound();
        AddChild(drain);
    }

    private void OnHelloAcked()
    {
        _connected = true;
        _statusLabel.Text = "Connected. Fetching lobbies…";
        _relay?.SendListRooms();

        // Poll every 3 seconds
        _pollTimer = new Timer { WaitTime = 3.0, Autostart = true };
        _pollTimer.Timeout += () => _relay?.SendListRooms();
        AddChild(_pollTimer);
    }

    private void OnClosed()
    {
        _connected = false;
        _statusLabel.Text = "Disconnected.";
    }

    private void OnServerError(int code)
    {
        _statusLabel.Text = $"Server error: {(ErrorCode)code}";
    }

    private void OnRoomListDeferred(Godot.Collections.Array roomData)
    {
        // Clear and rebuild lobby list
        foreach (var child in _lobbyList.GetChildren())
            child.QueueFree();

        _statusLabel.Text = _connected ? "" : "Disconnected.";

        int count = roomData.Count;
        _countLabel.Text = $"{count} {(count == 1 ? "lobby" : "lobbies")}";

        if (count == 0)
        {
            var emptyLabel = new Label
            {
                Text = "No lobbies found. Host a new game!",
                HorizontalAlignment = HorizontalAlignment.Center
            };
            emptyLabel.AddThemeColorOverride("font_color", new Color(0.5f, 0.5f, 0.6f));
            _lobbyList.AddChild(emptyLabel);
            return;
        }

        foreach (Godot.Collections.Array room in roomData)
        {
            string code = (string)room[0];
            string roomName = (string)room[1];
            int playerCount = (int)room[2];
            int slotCount = (int)room[3];
            string mapName = (string)room[4];

            var row = new HBoxContainer();
            row.AddThemeConstantOverride("separation", 8);

            var nameLabel = new Label
            {
                Text = string.IsNullOrEmpty(roomName) ? code : roomName,
                CustomMinimumSize = new Vector2(200, 0),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            row.AddChild(nameLabel);

            row.AddChild(new Label
            {
                Text = $"{playerCount}/{slotCount}",
                CustomMinimumSize = new Vector2(70, 0)
            });

            row.AddChild(new Label
            {
                Text = mapName,
                CustomMinimumSize = new Vector2(140, 0)
            });

            var joinBtn = new Button
            {
                Text = "JOIN",
                CustomMinimumSize = new Vector2(80, 28)
            };
            string joinCode = code;
            joinBtn.Pressed += () => OnJoinPressed(joinCode);
            row.AddChild(joinBtn);

            _lobbyList.AddChild(row);
        }
    }

    private void OnHostPressed()
    {
        if (!_connected || _relay == null) return;
        MultiplayerLaunchData.Intent = MultiplayerIntent.Host;
        MultiplayerLaunchData.Relay = _relay;
        MultiplayerLaunchData.LobbyName = _lobbyNameEdit.Text.Trim();
        MultiplayerLaunchData.PlayerName = _playerNameEdit.Text.Trim();
        GetTree().ChangeSceneToFile("res://Scenes/GameLobby.tscn");
    }

    private void OnJoinPressed(string code)
    {
        if (!_connected || _relay == null) return;
        MultiplayerLaunchData.Intent = MultiplayerIntent.Join;
        MultiplayerLaunchData.JoinCode = code;
        MultiplayerLaunchData.Relay = _relay;
        MultiplayerLaunchData.PlayerName = _playerNameEdit.Text.Trim();
        _relay.SendJoinRoom(code, 1);
        // Navigate on RoomState received
    }

    private void OnRoomJoinedDeferred()
    {
        // RoomState received after JoinRoom — navigate to lobby
        if (MultiplayerLaunchData.Intent == MultiplayerIntent.Join)
            GetTree().ChangeSceneToFile("res://Scenes/GameLobby.tscn");
    }

    public override void _ExitTree()
    {
        if (_relay != null)
        {
            if (_helloAckedHandler != null) _relay.HelloAcked -= _helloAckedHandler;
            if (_closedHandler != null) _relay.ConnectionClosed -= _closedHandler;
            if (_errorHandler != null) _relay.ServerError -= _errorHandler;
            if (_roomListHandler != null) _relay.RoomListReceived -= _roomListHandler;
            if (_roomStateHandler != null) _relay.RoomStateReceived -= _roomStateHandler;
        }
    }
}
```

**Note:** This adds `LobbyName` and `PlayerName` fields to `MultiplayerLaunchData`. These need to be added:

```csharp
// In MultiplayerMenu.cs, add to MultiplayerLaunchData:
public static string LobbyName = "";
public static string PlayerName = "Player";
```

- [ ] **Step 2: Add LobbyName and PlayerName to MultiplayerLaunchData**

In `godot/Scripts/UI/MultiplayerMenu.cs`, add two fields to the `MultiplayerLaunchData` class:

```csharp
public static string LobbyName = "";
public static string PlayerName = "Player";
```

- [ ] **Step 3: Commit**

```bash
git add godot/Scripts/UI/LobbyListScreen.cs godot/Scripts/UI/MultiplayerMenu.cs
git commit -m "ui: add LobbyListScreen with room browser, host, and join"
```

---

## Task 12: GameLobbyScreen — Core Structure + Single-Player Mode

**Files:**
- Create: `godot/Scripts/UI/GameLobbyScreen.cs`

This is the largest file. It handles three modes (host, join, single-player) with a two-column layout. We build the skeleton and single-player mode first.

- [ ] **Step 1: Implement GameLobbyScreen skeleton + single-player mode**

```csharp
using Blocker.Game.Maps;
using Blocker.Game.Net;
using Blocker.Game.Rendering;
using Blocker.Simulation.Maps;
using Blocker.Simulation.Net;
using Godot;
using System.Collections.Generic;
using System.Linq;

namespace Blocker.Game.UI;

public partial class GameLobbyScreen : Control
{
    // Shared state
    private MapData? _mapData;
    private readonly Dictionary<int, string> _slotAssignments = new();
    private VBoxContainer _slotContainer = null!;
    private MapMiniature _mapMiniature = null!;
    private OptionButton _mapDropdown = null!;
    private Label _headerLabel = null!;
    private int _playerSlot = 0;
    private readonly List<(string FileName, MapData Data)> _mapCatalog = new();

    // Multiplayer state
    private RelayClient? _relay;
    private RoomStatePayload? _latestRoomState;
    private int _pendingLocalId;
    private int[] _pendingActiveIds = Array.Empty<int>();
    private LobbyChatPanel? _chatPanel;

    // Host-only
    private Button? _startBtn;
    private OptionButton? _modeDropdown;
    private Label? _roomCodeLabel;
    private string _lobbyName = "";
    private GameMode _selectedMode = GameMode.Ffa;
    private bool _roomCreated;
    private bool _suppressMapSignal;

    // Join-only
    private Button? _readyBtn;
    private bool _isReady;
    private Label? _joinStatusLabel;
    private bool _navigatingAway;

    // Relay handlers
    private Action<RoomStatePayload>? _relayRoomStateHandler;
    private Action<int, int[]>? _relayGameStartedHandler;
    private Action<ErrorCode>? _relayErrorHandler;
    private Action? _relayClosedHandler;

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Ignore;

        // Grid background
        var menuGrid = new MenuGrid { Name = "MenuGrid" };
        AddChild(menuGrid);

        var intent = MultiplayerLaunchData.Intent;
        if (intent == MultiplayerIntent.Host)
            SetupHostMode();
        else if (intent == MultiplayerIntent.Join)
            SetupJoinMode();
        else
            SetupSinglePlayerMode();
    }

    // ------------------------------------------------------------------
    // Layout helpers (shared by all modes)
    // ------------------------------------------------------------------

    private (VBoxContainer leftCol, VBoxContainer rightCol) BuildTwoColumnLayout(string titleText)
    {
        var panel = new PanelContainer();
        panel.AnchorLeft = 0.03f; panel.AnchorRight = 0.97f;
        panel.AnchorTop = 0.03f; panel.AnchorBottom = 0.97f;
        var style = new StyleBoxFlat
        {
            BgColor = new Color(0.04f, 0.04f, 0.06f, 0.92f),
            CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
            CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
            ContentMarginLeft = 16, ContentMarginRight = 16,
            ContentMarginTop = 12, ContentMarginBottom = 12
        };
        panel.AddThemeStyleboxOverride("panel", style);
        AddChild(panel);

        var outerVbox = new VBoxContainer();
        outerVbox.AddThemeConstantOverride("separation", 8);
        panel.AddChild(outerVbox);

        // Header
        _headerLabel = new Label
        {
            Text = titleText,
            HorizontalAlignment = HorizontalAlignment.Center
        };
        _headerLabel.AddThemeFontSizeOverride("font_size", 24);
        _headerLabel.AddThemeColorOverride("font_color", new Color(0.267f, 0.667f, 1f));
        outerVbox.AddChild(_headerLabel);

        outerVbox.AddChild(new HSeparator());

        // Two columns
        var hbox = new HBoxContainer
        {
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        hbox.AddThemeConstantOverride("separation", 16);
        outerVbox.AddChild(hbox);

        var leftCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        leftCol.SizeFlagsStretchRatio = 1.2f;
        leftCol.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(leftCol);

        var rightCol = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        rightCol.AddThemeConstantOverride("separation", 8);
        hbox.AddChild(rightCol);

        return (leftCol, rightCol);
    }

    private void BuildMapDropdown(VBoxContainer container, bool enabled)
    {
        _mapCatalog.Clear();
        foreach (var fileName in MapFileManager.ListMaps())
        {
            var data = MapFileManager.Load(fileName);
            if (data != null) _mapCatalog.Add((fileName, data));
        }

        var row = new HBoxContainer();
        row.AddChild(new Label { Text = "Map:", CustomMinimumSize = new Vector2(50, 0) });
        _mapDropdown = new OptionButton
        {
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
            Disabled = !enabled
        };
        PopulateMapDropdown(0);
        if (_mapCatalog.Count > 0)
        {
            _mapDropdown.Selected = 0;
            _mapData = _mapCatalog[0].Data;
        }
        if (enabled)
            _mapDropdown.ItemSelected += OnMapSelected;
        row.AddChild(_mapDropdown);
        container.AddChild(row);
    }

    private void PopulateMapDropdown(int minSlots)
    {
        if (_mapDropdown == null) return;
        _mapDropdown.Clear();
        foreach (var (fileName, data) in _mapCatalog)
        {
            if (data.SlotCount < minSlots) continue;
            _mapDropdown.AddItem($"{data.Name} ({data.SlotCount} slots)");
            _mapDropdown.SetItemId(_mapDropdown.ItemCount - 1, _mapCatalog.IndexOf((fileName, data)));
        }
    }

    private void OnMapSelected(long idx)
    {
        if (_suppressMapSignal) return;
        int catIdx = _mapDropdown!.GetItemId((int)idx);
        if (catIdx < 0 || catIdx >= _mapCatalog.Count) return;
        _mapData = _mapCatalog[catIdx].Data;
        _mapMiniature.SetMap(_mapData);
        if (_roomCreated) SendUpdateRoom();
    }

    private void BuildMapMiniature(VBoxContainer container)
    {
        _mapMiniature = new MapMiniature
        {
            CustomMinimumSize = new Vector2(200, 200),
            SizeFlagsVertical = SizeFlags.ExpandFill
        };
        container.AddChild(_mapMiniature);
        _mapMiniature.SetMap(_mapData);
    }

    // ------------------------------------------------------------------
    // Single-player mode
    // ------------------------------------------------------------------

    private void SetupSinglePlayerMode()
    {
        var (leftCol, rightCol) = BuildTwoColumnLayout("PLAY VS AI");

        // Left: slot list
        _slotContainer = new VBoxContainer();
        _slotContainer.AddThemeConstantOverride("separation", 6);
        leftCol.AddChild(new Label { Text = "Slots (click to toggle)" });
        leftCol.AddChild(_slotContainer);

        leftCol.AddChild(new Control { SizeFlagsVertical = SizeFlags.ExpandFill }); // spacer

        var startBtn = new Button
        {
            Text = "START GAME",
            CustomMinimumSize = new Vector2(0, 44)
        };
        startBtn.Pressed += OnSinglePlayerStart;
        leftCol.AddChild(startBtn);

        // Right: map dropdown + miniature
        BuildMapDropdown(rightCol, enabled: true);
        BuildMapMiniature(rightCol);

        // Bottom: back button
        var backBtn = new Button { Text = "< BACK", CustomMinimumSize = new Vector2(100, 32) };
        backBtn.Pressed += () => GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        leftCol.AddChild(backBtn);

        if (_mapData != null)
        {
            for (int i = 0; i < _mapData.SlotCount; i++)
                _slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
            RebuildSinglePlayerSlotList();
        }
    }

    private void RebuildSinglePlayerSlotList()
    {
        foreach (var child in _slotContainer.GetChildren())
            child.QueueFree();

        if (_mapData == null) return;
        for (int i = 0; i < _mapData.SlotCount; i++)
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = $"Slot {i + 1}:", CustomMinimumSize = new Vector2(70, 0) });

            var btn = new Button
            {
                Text = _slotAssignments[i],
                CustomMinimumSize = new Vector2(160, 32),
                SizeFlagsHorizontal = SizeFlags.ExpandFill
            };
            int slot = i;
            btn.Pressed += () => ToggleSlotAssignment(slot);
            row.AddChild(btn);

            _slotContainer.AddChild(row);
        }
    }

    private void ToggleSlotAssignment(int slot)
    {
        if (_slotAssignments[slot] == "Player")
        {
            _slotAssignments[slot] = "AI (inactive)";
        }
        else
        {
            foreach (var key in _slotAssignments.Keys.ToList())
                if (_slotAssignments[key] == "Player") _slotAssignments[key] = "AI (inactive)";
            _slotAssignments[slot] = "Player";
            _playerSlot = slot;
        }

        // Rebuild slot list AND refresh map preview (slot count may require map re-filter)
        RebuildSinglePlayerSlotList();
    }

    private void OnSinglePlayerStart()
    {
        if (_mapData == null) return;

        var assignments = new List<SlotAssignment>();
        int nextAiId = 1;
        for (int i = 0; i < _mapData.SlotCount; i++)
        {
            if (_slotAssignments[i] == "Player")
                assignments.Add(new SlotAssignment(i, 0));
            else
                assignments.Add(new SlotAssignment(i, nextAiId++));
        }

        GameLaunchData.MapData = _mapData;
        GameLaunchData.Assignments = assignments;
        GetTree().ChangeSceneToFile("res://Scenes/Main.tscn");
    }

    // Map change also refreshes miniature + slot list
    // (handled in OnMapSelected above — updates _mapData and calls _mapMiniature.SetMap)

    public override void _ExitTree()
    {
        if (_relay != null)
        {
            if (_relayRoomStateHandler != null) _relay.RoomStateReceived -= _relayRoomStateHandler;
            if (_relayGameStartedHandler != null) _relay.GameStarted -= _relayGameStartedHandler;
            if (_relayErrorHandler != null) _relay.ServerError -= _relayErrorHandler;
            if (_relayClosedHandler != null) _relay.ConnectionClosed -= _relayClosedHandler;
        }
    }
}
```

Note: `OnMapSelected` when in single-player mode should also update slot assignments if slot count changes. Add this to `OnMapSelected`:
```csharp
// When map changes in single-player mode, rebuild slot assignments
if (MultiplayerLaunchData.Intent == MultiplayerIntent.None && _mapData != null)
{
    _slotAssignments.Clear();
    for (int i = 0; i < _mapData.SlotCount; i++)
        _slotAssignments[i] = i == 0 ? "Player" : "AI (inactive)";
    _playerSlot = 0;
    RebuildSinglePlayerSlotList();
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/UI/GameLobbyScreen.cs
git commit -m "ui: add GameLobbyScreen with single-player mode"
```

---

## Task 13: GameLobbyScreen — Host Mode

**Files:**
- Modify: `godot/Scripts/UI/GameLobbyScreen.cs`

- [ ] **Step 1: Implement SetupHostMode**

Add this method to `GameLobbyScreen`:

```csharp
private void SetupHostMode()
{
    _relay = MultiplayerLaunchData.Relay;
    if (_relay == null)
    {
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        return;
    }

    _lobbyName = MultiplayerLaunchData.LobbyName;
    if (string.IsNullOrEmpty(_lobbyName)) _lobbyName = "Game";

    bool rematchReattach = MultiplayerLaunchData.RematchReattach;
    MultiplayerLaunchData.RematchReattach = false;

    var (leftCol, rightCol) = BuildTwoColumnLayout(
        rematchReattach ? "REMATCH LOBBY" : $"HOST — {_lobbyName}");

    // Left: room code + slot list + start button
    _roomCodeLabel = new Label { Text = "Creating room…" };
    _roomCodeLabel.AddThemeFontSizeOverride("font_size", 16);
    leftCol.AddChild(_roomCodeLabel);

    leftCol.AddChild(new Label { Text = "Players" });
    _slotContainer = new VBoxContainer();
    _slotContainer.AddThemeConstantOverride("separation", 4);
    var slotScroll = new ScrollContainer
    {
        SizeFlagsVertical = SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(0, 120)
    };
    slotScroll.AddChild(_slotContainer);
    leftCol.AddChild(slotScroll);

    // Chat panel
    _chatPanel = new LobbyChatPanel();
    _chatPanel.CustomMinimumSize = new Vector2(0, 140);
    leftCol.AddChild(_chatPanel);

    _startBtn = new Button
    {
        Text = "START GAME",
        CustomMinimumSize = new Vector2(0, 44),
        Disabled = true
    };
    _startBtn.Pressed += () => _relay!.SendStartGame();
    leftCol.AddChild(_startBtn);

    var backBtn = new Button { Text = "< BACK", CustomMinimumSize = new Vector2(100, 32) };
    backBtn.Pressed += () =>
    {
        _relay?.SendLeaveRoom();
        _relay?.Dispose();
        MultiplayerLaunchData.Relay = null;
        GetTree().ChangeSceneToFile("res://Scenes/LobbyList.tscn");
    };
    leftCol.AddChild(backBtn);

    // Right: map dropdown + mode dropdown + miniature
    BuildMapDropdown(rightCol, enabled: true);

    var modeRow = new HBoxContainer();
    modeRow.AddChild(new Label { Text = "Mode:", CustomMinimumSize = new Vector2(50, 0) });
    _modeDropdown = new OptionButton();
    _modeDropdown.AddItem("Free-for-all", (int)GameMode.Ffa);
    _modeDropdown.AddItem("Teams", (int)GameMode.Teams);
    _modeDropdown.Selected = 0;
    _modeDropdown.ItemSelected += OnModeSelected;
    modeRow.AddChild(_modeDropdown);
    rightCol.AddChild(modeRow);

    BuildMapMiniature(rightCol);

    // Wire up relay events
    _relayRoomStateHandler = (rs) =>
    {
        _latestRoomState = rs;
        CallDeferred(nameof(OnHostRoomStateDeferred));
    };
    _relayGameStartedHandler = (localId, activeIds) =>
    {
        _pendingLocalId = localId;
        _pendingActiveIds = activeIds;
        CallDeferred(nameof(OnGameStartedDeferred));
    };
    _relayErrorHandler = (code) =>
        CallDeferred(nameof(OnHostErrorDeferred), code.ToString());

    _relay.RoomStateReceived += _relayRoomStateHandler;
    _relay.GameStarted += _relayGameStartedHandler;
    _relay.ServerError += _relayErrorHandler;

    var drain = new Timer { WaitTime = 0.016, Autostart = true };
    drain.Timeout += () => _relay.DrainInbound();
    AddChild(drain);

    // Set up chat
    _chatPanel.SetRelay(_relay);

    // Create room (or reattach on rematch)
    if (!rematchReattach)
    {
        _roomCreated = true;
        if (_mapData != null)
        {
            var mapFileName = _mapCatalog.Count > 0 ? _mapCatalog[0].FileName : (_mapData.Name + ".json");
            var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
            _relay.SendCreateRoom((byte)_mapData.SlotCount, _selectedMode, _lobbyName, mapFileName, mapBlob);
        }
    }

    if (MultiplayerLaunchData.PendingRoomState is { } pending)
    {
        _latestRoomState = pending;
        MultiplayerLaunchData.PendingRoomState = null;
        SyncDropdownsFromRoomState(pending);
        OnHostRoomStateDeferred();
    }
}

private void OnModeSelected(long idx)
{
    _selectedMode = (GameMode)_modeDropdown!.GetItemId((int)idx);
    if (_roomCreated) SendUpdateRoom();
}

private void SendUpdateRoom()
{
    if (_relay == null || _mapData == null) return;
    var catEntry = _mapCatalog.Find(e => e.Data == _mapData);
    var mapFileName = catEntry.FileName ?? (_mapData.Name + ".json");
    var mapBlob = System.Text.Encoding.UTF8.GetBytes(mapFileName);
    _relay.SendUpdateRoom((byte)_mapData.SlotCount, _selectedMode, _lobbyName, mapFileName, mapBlob);
}

private void OnHostRoomStateDeferred()
{
    if (_latestRoomState == null) return;
    var rs = _latestRoomState;
    int filled = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
    bool allReady = rs.Slots.Where((s, i) => i != 0 && !s.IsOpen && !s.IsClosed)
                            .All(s => s.IsReady);
    bool canStart = filled >= 2 && allReady;

    _roomCodeLabel!.Text = $"Room Code: {rs.Code}   ({filled}/{rs.Slots.Length} players)";
    _startBtn!.Disabled = !canStart;
    if (!canStart && filled >= 2)
        _startBtn.Text = "WAITING FOR READY…";
    else
        _startBtn.Text = "START GAME";

    // Re-filter maps
    if (_mapDropdown != null)
    {
        _suppressMapSignal = true;
        PopulateMapDropdown(filled);
        if (_mapData != null)
        {
            for (int i = 0; i < _mapDropdown.ItemCount; i++)
            {
                int catIdx = _mapDropdown.GetItemId(i);
                if (catIdx >= 0 && catIdx < _mapCatalog.Count && _mapCatalog[catIdx].Data == _mapData)
                { _mapDropdown.Selected = i; break; }
            }
        }
        _suppressMapSignal = false;
    }

    RebuildHostSlotList(rs);
    UpdateChatSlotNames(rs);
}

private void RebuildHostSlotList(RoomStatePayload rs)
{
    foreach (var child in _slotContainer.GetChildren())
        child.QueueFree();

    for (int i = 0; i < rs.Slots.Length; i++)
    {
        var slot = rs.Slots[i];
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        string who;
        if (slot.IsClosed) who = "(closed)";
        else if (slot.IsOpen || string.IsNullOrEmpty(slot.DisplayName)) who = "(open)";
        else who = slot.DisplayName;

        string teamTag = rs.GameMode == GameMode.Teams ? $"T{slot.TeamId + 1}" : "";
        string readyTag = (!slot.IsOpen && !slot.IsClosed && i != 0) ? (slot.IsReady ? " [READY]" : " [not ready]") : "";

        row.AddChild(new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(24, 0) });
        var nameLabel = new Label
        {
            Text = who + readyTag,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        if (slot.IsReady && i != 0)
            nameLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
        row.AddChild(nameLabel);

        if (!string.IsNullOrEmpty(teamTag))
            row.AddChild(new Label { Text = teamTag });

        // Kick button for non-host occupied slots
        bool isOccupied = !slot.IsOpen && !slot.IsClosed && !string.IsNullOrEmpty(slot.DisplayName);
        if (isOccupied && i != 0)
        {
            byte kickSlot = (byte)i;
            var kickBtn = new Button { Text = "X", CustomMinimumSize = new Vector2(30, 0) };
            kickBtn.Pressed += () => _relay?.SendKickPlayer(kickSlot);
            row.AddChild(kickBtn);
        }

        _slotContainer.AddChild(row);
    }
}

private void OnHostErrorDeferred(string error)
{
    _roomCodeLabel!.Text = $"Error: {error}";
    if (_startBtn != null) _startBtn.Disabled = true;
}

private void SyncDropdownsFromRoomState(RoomStatePayload rs)
{
    _selectedMode = rs.GameMode;
    if (_modeDropdown != null)
    {
        for (int i = 0; i < _modeDropdown.ItemCount; i++)
            if (_modeDropdown.GetItemId(i) == (int)rs.GameMode)
            { _modeDropdown.Selected = i; break; }
    }
    var mapEntry = _mapCatalog.Find(e => e.FileName == rs.MapName || e.FileName == rs.MapName + ".json");
    if (mapEntry.Data != null) _mapData = mapEntry.Data;
    _mapMiniature?.SetMap(_mapData);
}

private void UpdateChatSlotNames(RoomStatePayload rs)
{
    var names = new Dictionary<int, string>();
    for (int i = 0; i < rs.Slots.Length; i++)
    {
        if (!rs.Slots[i].IsOpen && !rs.Slots[i].IsClosed && !string.IsNullOrEmpty(rs.Slots[i].DisplayName))
            names[i] = rs.Slots[i].DisplayName;
    }
    _chatPanel?.UpdateSlotNames(names);
}
```

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/UI/GameLobbyScreen.cs
git commit -m "ui: add host mode to GameLobbyScreen"
```

---

## Task 14: GameLobbyScreen — Join Mode

**Files:**
- Modify: `godot/Scripts/UI/GameLobbyScreen.cs`

- [ ] **Step 1: Implement SetupJoinMode and OnGameStartedDeferred**

```csharp
private void SetupJoinMode()
{
    _relay = MultiplayerLaunchData.Relay;
    if (_relay == null)
    {
        GetTree().ChangeSceneToFile("res://Scenes/MainMenu.tscn");
        return;
    }

    var (leftCol, rightCol) = BuildTwoColumnLayout($"JOINED — {MultiplayerLaunchData.JoinCode}");

    // Left: status + slot list + ready button + chat
    _joinStatusLabel = new Label { Text = "Waiting for host…" };
    leftCol.AddChild(_joinStatusLabel);

    _slotContainer = new VBoxContainer();
    _slotContainer.AddThemeConstantOverride("separation", 4);
    var slotScroll = new ScrollContainer
    {
        SizeFlagsVertical = SizeFlags.ExpandFill,
        CustomMinimumSize = new Vector2(0, 120)
    };
    slotScroll.AddChild(_slotContainer);
    leftCol.AddChild(slotScroll);

    // Chat panel
    _chatPanel = new LobbyChatPanel();
    _chatPanel.CustomMinimumSize = new Vector2(0, 140);
    leftCol.AddChild(_chatPanel);

    // Ready button
    _readyBtn = new Button
    {
        Text = "READY",
        CustomMinimumSize = new Vector2(0, 40)
    };
    _readyBtn.Pressed += OnReadyToggle;
    leftCol.AddChild(_readyBtn);

    var backBtn = new Button { Text = "< LEAVE", CustomMinimumSize = new Vector2(100, 32) };
    backBtn.Pressed += () =>
    {
        _relay?.SendLeaveRoom();
        _relay?.Dispose();
        MultiplayerLaunchData.Relay = null;
        GetTree().ChangeSceneToFile("res://Scenes/LobbyList.tscn");
    };
    leftCol.AddChild(backBtn);

    // Right: map + mode as labels (read-only) + miniature
    _joinStatusLabel = new Label { Text = "Waiting for room info…" };
    rightCol.AddChild(_joinStatusLabel);
    BuildMapMiniature(rightCol);

    // Wire relay events
    _relayRoomStateHandler = (rs) =>
    {
        _latestRoomState = rs;
        CallDeferred(nameof(OnJoinRoomStateDeferred));
    };
    _relayGameStartedHandler = (localId, activeIds) =>
    {
        _pendingLocalId = localId;
        _pendingActiveIds = activeIds;
        CallDeferred(nameof(OnGameStartedDeferred));
    };
    _relayErrorHandler = (code) =>
    {
        if (code == ErrorCode.HostLeft)
            CallDeferred(nameof(OnJoinDisconnectedDeferred), "Host left the lobby.");
        else if (code == ErrorCode.Kicked)
            CallDeferred(nameof(OnJoinDisconnectedDeferred), "You were kicked.");
        else
            CallDeferred(nameof(OnJoinErrorDeferred), code.ToString());
    };
    _relayClosedHandler = () =>
        CallDeferred(nameof(OnJoinDisconnectedDeferred), "Connection lost.");

    _relay.RoomStateReceived += _relayRoomStateHandler;
    _relay.GameStarted += _relayGameStartedHandler;
    _relay.ServerError += _relayErrorHandler;
    _relay.ConnectionClosed += _relayClosedHandler;

    var drain = new Timer { WaitTime = 0.016, Autostart = true };
    drain.Timeout += () => _relay.DrainInbound();
    AddChild(drain);

    _chatPanel.SetRelay(_relay);

    // Seed from stashed RoomState on rematch
    if (MultiplayerLaunchData.PendingRoomState is { } pending)
    {
        _latestRoomState = pending;
        MultiplayerLaunchData.PendingRoomState = null;
        OnJoinRoomStateDeferred();
    }
}

private void OnReadyToggle()
{
    _isReady = !_isReady;
    _relay?.SendSetReady(_isReady);
    _readyBtn!.Text = _isReady ? "UNREADY" : "READY";
    var readyStyle = new StyleBoxFlat
    {
        BgColor = _isReady ? new Color(0.2f, 0.6f, 0.2f, 0.8f) : new Color(0.267f, 0.667f, 1f, 0.3f)
    };
    _readyBtn.AddThemeStyleboxOverride("normal", readyStyle);
}

private void OnJoinRoomStateDeferred()
{
    if (_latestRoomState == null) return;
    var rs = _latestRoomState;
    int filled = rs.Slots.Count(s => !s.IsOpen && !s.IsClosed);
    string modeLabel = rs.GameMode == GameMode.Teams ? "Teams" : "FFA";
    _joinStatusLabel!.Text = $"Map: {rs.MapName}  |  Mode: {modeLabel}  |  {filled}/{rs.Slots.Length} players";
    _headerLabel.Text = $"JOINED — {rs.Code}" + (rs.RoomName != "" ? $" ({rs.RoomName})" : "");

    // Load map for miniature preview
    var md = MapFileManager.Load(rs.MapName);
    if (md != null)
    {
        _mapData = md;
        _mapMiniature?.SetMap(md);
    }

    // Rebuild slot list (read-only — no kick buttons)
    foreach (var child in _slotContainer.GetChildren())
        child.QueueFree();

    for (int i = 0; i < rs.Slots.Length; i++)
    {
        var slot = rs.Slots[i];
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);

        string who;
        if (slot.IsClosed) who = "(closed)";
        else if (slot.IsOpen) who = "(open)";
        else who = string.IsNullOrEmpty(slot.DisplayName) ? "(open)" : slot.DisplayName;

        string readyTag = (!slot.IsOpen && !slot.IsClosed && i != 0) ? (slot.IsReady ? " [READY]" : "") : "";
        string teamTag = rs.GameMode == GameMode.Teams ? $"T{slot.TeamId + 1}" : "";

        row.AddChild(new Label { Text = $"{i + 1}.", CustomMinimumSize = new Vector2(24, 0) });
        var nameLabel = new Label
        {
            Text = who + readyTag,
            SizeFlagsHorizontal = SizeFlags.ExpandFill
        };
        if (slot.IsReady && i != 0)
            nameLabel.AddThemeColorOverride("font_color", new Color(0.3f, 0.9f, 0.3f));
        row.AddChild(nameLabel);
        if (!string.IsNullOrEmpty(teamTag))
            row.AddChild(new Label { Text = teamTag });

        _slotContainer.AddChild(row);
    }

    UpdateChatSlotNames(rs);

    // Reset ready state if host changed settings (server clears isReady on UpdateRoom)
    if (_isReady)
    {
        bool mySlotStillReady = false;
        for (int i = 0; i < rs.Slots.Length; i++)
        {
            if (!rs.Slots[i].IsOpen && !rs.Slots[i].IsClosed && rs.Slots[i].IsReady)
                mySlotStillReady = true;
        }
        if (!mySlotStillReady)
        {
            _isReady = false;
            _readyBtn!.Text = "READY";
        }
    }
}

private void OnJoinErrorDeferred(string error)
{
    if (_joinStatusLabel != null)
        _joinStatusLabel.Text = $"Error: {error}";
}

private void OnJoinDisconnectedDeferred(string reason)
{
    if (_navigatingAway) return;
    _navigatingAway = true;
    _relay?.Dispose();
    MultiplayerLaunchData.Relay = null;
    if (_joinStatusLabel != null) _joinStatusLabel.Text = reason;
    var timer = GetTree().CreateTimer(1.5);
    timer.Timeout += () => GetTree().ChangeSceneToFile("res://Scenes/LobbyList.tscn");
}

// Shared by host + join: launch into the game
private void OnGameStartedDeferred()
{
    string mapName = _latestRoomState?.MapName ?? "";
    var md = _mapData ?? MapFileManager.Load(mapName);
    if (md == null)
    {
        if (_joinStatusLabel != null) _joinStatusLabel.Text = $"Map '{mapName}' not found locally.";
        if (_roomCodeLabel != null) _roomCodeLabel.Text = $"Map '{mapName}' not found locally.";
        return;
    }

    var mode = _latestRoomState?.GameMode ?? GameMode.Ffa;
    var assignments = new List<SlotAssignment>();
    foreach (var pid in _pendingActiveIds)
    {
        int teamId = mode.TeamForSlot(pid);
        assignments.Add(new SlotAssignment(pid, pid, teamId));
    }

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

- [ ] **Step 2: Commit**

```bash
git add godot/Scripts/UI/GameLobbyScreen.cs
git commit -m "ui: add join mode to GameLobbyScreen with ready toggle"
```

---

## Task 15: MainMenu Wiring + Rematch Flow

**Files:**
- Modify: `godot/Scripts/UI/MainMenu.cs`
- Modify: `godot/Scripts/Game/GameManager.cs` (rematch flow — change scene paths)

- [ ] **Step 1: Update MainMenu scene routes**

In `MainMenu.cs`, find where "Play Multiplayer" button is created and change its route from `res://Scenes/MultiplayerMenu.tscn` to `res://Scenes/LobbyList.tscn`.

Find where "Play vs AI" button is created and change its route from `res://Scenes/MapSelect.tscn` to `res://Scenes/GameLobby.tscn`. Also set `MultiplayerLaunchData.Intent = MultiplayerIntent.None;` before navigating.

- [ ] **Step 2: Update rematch flow scene paths**

In `GameManager.cs`, search for any references to `MultiplayerMenu.tscn` or `SlotConfig.tscn` and replace:
- `res://Scenes/SlotConfig.tscn` → `res://Scenes/GameLobby.tscn`
- `res://Scenes/MultiplayerMenu.tscn` → `res://Scenes/LobbyList.tscn`

Also search in any other files that reference these old scene paths. Check `SlotConfigScreen.cs` back-button handlers — those reference `res://Scenes/MultiplayerMenu.tscn` and should be updated to `res://Scenes/LobbyList.tscn`, but since we're replacing SlotConfigScreen entirely, this only matters if GameManager or other files reference the old paths.

Use grep to find all references:
```bash
grep -r "MultiplayerMenu.tscn\|SlotConfig.tscn\|MapSelect.tscn" godot/Scripts/ --include="*.cs" -l
```

- [ ] **Step 3: Run tests**

Run: `dotnet test tests/Blocker.Simulation.Tests/`
Expected: all pass (scene path changes don't affect simulation tests).

- [ ] **Step 4: Commit**

```bash
git add godot/Scripts/UI/MainMenu.cs godot/Scripts/Game/GameManager.cs
git commit -m "ui: wire MainMenu to new lobby screens, update rematch scene paths"
```

---

## Task 16: Remove Old Screens

**Files:**
- Delete: `godot/Scripts/UI/MultiplayerMenu.cs` (keep `MultiplayerLaunchData` + `MultiplayerIntent` — move to own file or into GameLobbyScreen)
- Delete: `godot/Scripts/UI/SlotConfigScreen.cs` (keep `GameLaunchData` — move to own file or into GameLobbyScreen)
- Delete: `godot/Scripts/UI/MapSelectScreen.cs` (keep `MapSelection` if still used, or remove)
- Delete: `godot/Scenes/MultiplayerMenu.tscn`
- Delete: `godot/Scenes/SlotConfig.tscn`
- Delete: `godot/Scenes/MapSelect.tscn`

- [ ] **Step 1: Extract shared statics before deleting**

The following static classes are defined in files we're about to delete and are still used:
- `MultiplayerLaunchData` (in `MultiplayerMenu.cs`) — used by `LobbyListScreen`, `GameLobbyScreen`, `GameManager`
- `MultiplayerIntent` enum (in `MultiplayerMenu.cs`) — same
- `GameLaunchData` (in `SlotConfigScreen.cs`) — used by `GameLobbyScreen`, `GameManager`
- `MapSelection` (in `MapSelectScreen.cs`) — check if still referenced; if not, skip

Move `MultiplayerLaunchData`, `MultiplayerIntent`, and `GameLaunchData` into a new file:

Create `godot/Scripts/UI/LaunchData.cs`:

```csharp
using Blocker.Game.Net;
using Blocker.Simulation.Maps;
using System.Collections.Generic;

namespace Blocker.Game.UI;

public enum MultiplayerIntent { None, Host, Join }

public static class MultiplayerLaunchData
{
    public static MultiplayerIntent Intent;
    public static string JoinCode = "";
    public static RelayClient? Relay;
    public static bool RematchReattach;
    public static RoomStatePayload? PendingRoomState;
    public static string LobbyName = "";
    public static string PlayerName = "Player";
}

public static class GameLaunchData
{
    public static MapData? MapData { get; set; }
    public static List<SlotAssignment>? Assignments { get; set; }
    public static MultiplayerSessionState? MultiplayerSession { get; set; }
    public static bool ReturnToEditor { get; set; }
}
```

- [ ] **Step 2: Delete old files**

```bash
git rm godot/Scripts/UI/MultiplayerMenu.cs
git rm godot/Scripts/UI/SlotConfigScreen.cs
git rm godot/Scripts/UI/MapSelectScreen.cs
git rm godot/Scenes/MultiplayerMenu.tscn
git rm godot/Scenes/SlotConfig.tscn
git rm godot/Scenes/MapSelect.tscn
```

- [ ] **Step 3: Check for any remaining references to deleted types**

```bash
grep -r "SlotConfigScreen\|MultiplayerMenu\|MapSelectScreen\|MapSelection\." godot/Scripts/ --include="*.cs"
```

Fix any remaining references. `MapSelection.SelectedMapFileName` may be used in `GameManager.cs` or the map editor — if so, it should either be moved to `LaunchData.cs` or replaced with a direct field.

- [ ] **Step 4: Run tests + verify build**

Run: `dotnet test tests/Blocker.Simulation.Tests/`
Expected: all pass.

Verify Godot project builds by opening in Godot editor or running `dotnet build godot/Blocker.Game.csproj` (if .NET SDK and Godot SDK are on PATH).

- [ ] **Step 5: Commit**

```bash
git add godot/Scripts/UI/LaunchData.cs
git add -A
git commit -m "cleanup: remove old lobby screens, extract LaunchData statics"
```

---

## Task 17: Manual Integration Test

- [ ] **Step 1: Launch Godot and verify single-player flow**

1. Open `godot/project.godot` in Godot 4.6
2. Run the game from MainMenu
3. Click "PLAY VS AI" — should navigate to GameLobbyScreen in single-player mode
4. Verify: two-column layout, map dropdown, map miniature, slot toggle buttons
5. Select a map, toggle slots, click START GAME
6. Verify: game launches and plays normally

- [ ] **Step 2: Verify multiplayer host flow**

1. From MainMenu, click "PLAY MULTIPLAYER"
2. Should navigate to LobbyListScreen
3. Enter a player name and lobby name, click HOST NEW
4. Should navigate to GameLobbyScreen in host mode
5. Verify: room code shows, slot list updates, map dropdown works, mode dropdown works
6. Verify: map miniature shows the selected map
7. Verify: chat panel is visible and functional

- [ ] **Step 3: Verify multiplayer join flow (requires two instances or a test peer)**

1. Host a game from one instance
2. From a second instance, LobbyListScreen should list the hosted room
3. Click JOIN on the listed room
4. Should navigate to GameLobbyScreen in join mode
5. Verify: read-only settings, slot list shows both players
6. Click READY — host's start button should enable
7. Host clicks START — both instances launch into the game

- [ ] **Step 4: Verify rematch flow**

1. Complete a multiplayer game
2. Host clicks rematch
3. Both players should return to GameLobbyScreen (not the old SlotConfigScreen)
4. Verify room state is preserved, lobby works correctly

---

## Callout: Protocol Version & Relay Deployment

The protocol version bump from 2→3 means the deployed relay on DigitalOcean will reject v3 clients until it's redeployed. Plan the relay deployment before multiplayer testing:

```bash
# On the droplet (julianoschroeder.com):
# 1. Pull latest code
# 2. Build relay: dotnet publish src/Blocker.Relay -c Release
# 3. Restart systemd service
```

The relay server validates protocol version in `TryHandleHello` — the `ProtocolVersion` constant lives in `Protocol.cs` which is shared. After deploying the new relay, old v2 clients will be rejected. This is a clean break since the wire format for RoomState and CreateRoom changed.
