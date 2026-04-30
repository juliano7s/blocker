using System.Net;
using System.Net.WebSockets;
using Blocker.Simulation.Net;

namespace Blocker.Relay;

public sealed class RelayServer
{
    private readonly RelayOptions _opts;
    private readonly RoomRegistry _rooms = new();
    // connectionId → Connection (authoritative live-connection set).
    private readonly Dictionary<Guid, Connection> _connections = new();
    private readonly object _connectionsLock = new();

    private byte[]? _cachedRoomList;
    private DateTime _cachedRoomListExpiry = DateTime.MinValue;
    private readonly object _cacheLock = new();

    public RelayServer(RelayOptions opts) { _opts = opts; }

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
                    if (now - room.LastActivity <= limit) continue;
                    Logger.Info($"reaper closing idle room {room.Code} lifecycle={room.Lifecycle}");
                    foreach (var kv in room.Slots)
                    {
                        if (kv.Value.OwnerId is not Guid id) continue;
                        Connection? c;
                        lock (_connectionsLock) _connections.TryGetValue(id, out c);
                        if (c == null) continue;
                        try { await c.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "idle", ct); } catch { }
                    }
                    // Per-IP accounting is best-effort here; passing "" skips the decrement
                    // rather than risking a wrong one.
                    _rooms.Remove(room, "");
                }
            }
        }, ct);
    }

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
        // Start with a full token bucket so the first Hello isn't rejected
        // by the refill delta when it arrives within ~16ms of accept.
        conn.RateTokens = _opts.RateLimitMsgPerSec;

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
            var lostRoom = conn.CurrentRoom;
            if (lostRoom != null)
            {
                _ = HandleDisconnectRoomCleanup(conn, lostRoom, ct);
            }
        }
    }

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

            // If no players remain, drop the room.
            int remaining = room.Slots.Values.Count(s => s.OwnerId != null);
            if (remaining < 1) _rooms.Remove(room, conn.RemoteIp);
        }
        else if (room.Lifecycle == RoomLifecycle.Lobby)
        {
            if (room.HostId == conn.Id)
            {
                // Host left the lobby — notify joiners with HostLeft error, then close.
                foreach (var kv in room.Slots)
                {
                    if (kv.Value.OwnerId is not Guid id || id == conn.Id) continue;
                    Connection? other;
                    lock (_connectionsLock) _connections.TryGetValue(id, out other);
                    if (other == null) continue;
                    try
                    {
                        await SendError(other, ErrorCode.HostLeft, ct);
                        await other.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "host-left", ct);
                    }
                    catch { }
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
                try { await conn.Ws.CloseAsync(WebSocketCloseStatus.ProtocolError, "hello timeout", ct); } catch { }
                return;
            }

            var result = await ReceiveFullMessage(conn, buf, ct);
            if (result == null) return;
            int len = result.Value;
            conn.LastMessageAt = DateTime.UtcNow;

            if (len == 0) continue;
            if (!RateAllows(conn))
            {
                await SendError(conn, ErrorCode.RateLimit, ct);
                return;
            }
            byte type = buf[0];

            if (!helloSeen)
            {
                if (type != Protocol.Hello)
                {
                    await SendError(conn, ErrorCode.ProtocolMismatch, ct);
                    return;
                }
                if (!TryHandleHello(conn, new ReadOnlySpan<byte>(buf, 0, len)))
                {
                    await SendError(conn, ErrorCode.ProtocolMismatch, ct);
                    return;
                }
                await SendHelloAck(conn, ct);
                helloSeen = true;
                continue;
            }

            switch (type)
            {
                case Protocol.CreateRoom:  HandleCreateRoom(conn, new ReadOnlySpan<byte>(buf, 0, len), ct); break;
                case Protocol.JoinRoom:    await HandleJoinRoom(conn, buf, len, ct); break;
                case Protocol.LeaveRoom:   await HandleLeaveRoom(conn, ct); break;
                case Protocol.StartGame:   await HandleStartGame(conn, ct); break;
                case Protocol.Rematch:     await HandleRematch(conn, ct); break;
                case Protocol.UpdateRoom:  await HandleUpdateRoom(conn, buf, len, ct); break;
                case Protocol.KickPlayer:  await HandleKickPlayer(conn, buf, len, ct); break;
                case Protocol.ListRooms:   await HandleListRooms(conn, ct); break;
                case Protocol.SetReady:    await HandleSetReady(conn, buf, len, ct); break;
                case Protocol.Commands:    await FanOutCommands(conn, buf, len, ct); break;
                case Protocol.Hash:        await FanOutHash(conn, buf, len, ct); break;
                case Protocol.ChatMessage: await FanOutChat(conn, buf, len, ct); break;
                default:
                    await SendError(conn, ErrorCode.UnknownMessageType, ct);
                    return;
            }
        }
    }

    // Message layouts:
    //   CreateRoom: [0x03][slotCount:byte][gameMode:byte][roomNameLen:varint][roomName][mapNameLen:varint][mapName][mapBlobLen:varint][mapBlob]
    //   JoinRoom:   [0x04][codeLen:byte=4][code bytes][desiredSlot:byte]
    //   RoomState:  [0x05][code:4][hostId:16][slotCount:byte][sim:uint16 LE][gameMode:byte]
    //               [roomNameLen:varint][roomName]
    //               [mapNameLen:varint][mapName]
    //               per slot: [ownerNameLen:varint][ownerName][colorIdx:byte][teamId:byte][flags:byte(bit0=open,bit1=closed,bit2=ready)]
    //   GameStarted:[0x08][yourPlayerId:byte][activeCount:byte][playerIds...]
    //   Rematch:    [0x09]                  — host requests room reset to Lobby
    //   UpdateRoom: [0x0A][slotCount:byte][gameMode:byte][roomNameLen:varint][roomName][mapNameLen:varint][mapName][mapBlobLen:varint][mapBlob]
    //   (Surrender is no longer a wire-level message — clients send it as a
    //    Command via the existing Commands stream so the simulation applies it
    //    deterministically at the same tick on every peer.)

    private void HandleCreateRoom(Connection conn, ReadOnlySpan<byte> payload, CancellationToken ct)
    {
        if (payload.Length < 4) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
        byte slotCount = payload[1];
        byte gameMode = payload[2];
        // NEW: parse roomName first
        var (roomNameLen, cr) = Varint.Read(payload, 3);
        int pos = 3 + cr;
        if (pos + (int)roomNameLen > payload.Length || roomNameLen > 64) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
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

        // Host takes slot 0; remaining slots start open. TeamId is derived from
        // (slotId, gameMode) on every peer, so we just record what the host sent.
        room.Slots[0] = new SlotInfo(conn.Id, conn.ClientName, 0, TeamForSlot(0, gameMode), IsOpen: false, IsClosed: false, IsReady: false);
        for (byte i = 1; i < slotCount; i++)
            room.Slots[i] = new SlotInfo(null, "", i, TeamForSlot(i, gameMode), IsOpen: true, IsClosed: false, IsReady: false);

        conn.CurrentRoom = room;
        conn.AssignedPlayerId = 0;
        Logger.Info($"conn={conn.Id} event=room-created code={room.Code} map={mapName} slots={slotCount} mode={gameMode}");
        _ = BroadcastRoomState(room, ct);
    }

    // Mirrors GameModeExtensions.TeamForSlot in Blocker.Simulation, kept local
    // to avoid forcing the relay to depend on the simulation enum directly.
    private static byte TeamForSlot(byte slotId, byte gameMode) =>
        gameMode == (byte)GameMode.Teams ? (byte)(slotId / 2) : slotId;

    // Takes byte[] + len (not ReadOnlySpan<byte>): ref structs can't be async parameters.
    private async Task HandleJoinRoom(Connection conn, byte[] buf, int len, CancellationToken ct)
    {
        // Parse synchronously into locals before any await.
        string? code = null;
        byte desired = 0;
        {
            var payload = new ReadOnlySpan<byte>(buf, 0, len);
            if (payload.Length < 7) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
            byte codeLen = payload[1];
            if (codeLen != 4) { await SendError(conn, ErrorCode.RoomNotFound, ct); return; }
            code = System.Text.Encoding.ASCII.GetString(payload.Slice(2, 4));
            desired = payload[6];
        }

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

        // Preserve TeamId baked in at room creation — slot index → team is fixed.
        var existing = room.Slots[chosen];
        room.Slots[chosen] = new SlotInfo(conn.Id, conn.ClientName, existing.ColorIndex, existing.TeamId, IsOpen: false, IsClosed: false, IsReady: false);
        conn.CurrentRoom = room;
        conn.AssignedPlayerId = chosen;
        room.LastActivity = DateTime.UtcNow;
        Logger.Info($"conn={conn.Id} event=room-joined code={code} slot={chosen} team={existing.TeamId}");
        await BroadcastRoomState(room, ct);
    }

    private async Task HandleLeaveRoom(Connection conn, CancellationToken ct)
    {
        var room = conn.CurrentRoom;
        if (room == null) return;

        // Host leaving a lobby tears the room down — mirrors the disconnect path
        // in HandleDisconnectRoomCleanup. Without this, the per-IP room counter
        // leaks and `/back → Host` eventually trips TooManyRooms.
        if (room.Lifecycle == RoomLifecycle.Lobby && room.HostId == conn.Id)
        {
            foreach (var kv in room.Slots)
            {
                if (kv.Value.OwnerId is not Guid id || id == conn.Id) continue;
                Connection? other;
                lock (_connectionsLock) _connections.TryGetValue(id, out other);
                if (other == null) continue;
                try
                {
                    await SendError(other, ErrorCode.HostLeft, ct);
                    await other.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "host-left", ct);
                }
                catch { }
            }
            _rooms.Remove(room, conn.RemoteIp);
            conn.CurrentRoom = null;
            conn.AssignedPlayerId = null;
            Logger.Info($"conn={conn.Id} event=room-closed code={room.Code} reason=host-leave");
            return;
        }

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

    /// <summary>
    /// Host requests a rematch after a game ends. Reset the room back to Lobby
    /// state so the host can hit "Start Game" again with the same players. We
    /// reset HighestSeenTick so any straggling Commands/Hash messages from the
    /// previous game (which would arrive against tick 0 of the next one) get
    /// dropped at FanOutCommands' Lifecycle != Playing guard.
    /// </summary>
    private async Task HandleRematch(Connection conn, CancellationToken ct)
    {
        var room = conn.CurrentRoom;
        if (room == null) { await SendError(conn, ErrorCode.NotInRoom, ct); return; }
        if (room.HostId != conn.Id) { await SendError(conn, ErrorCode.NotHost, ct); return; }

        room.Lifecycle = RoomLifecycle.Lobby;
        room.HighestSeenTick = 0;
        room.LastActivity = DateTime.UtcNow;
        Logger.Info($"conn={conn.Id} event=rematch code={room.Code}");
        await BroadcastRoomState(room, ct);
    }

    /// <summary>
    /// Host updates room settings (map, mode, slot count) while in the lobby.
    /// Slot count must be >= the number of currently filled slots. Excess open
    /// slots are trimmed; new open slots are added if count increases.
    /// </summary>
    private async Task HandleUpdateRoom(Connection conn, byte[] buf, int len, CancellationToken ct)
    {
        var room = conn.CurrentRoom;
        if (room == null) { await SendError(conn, ErrorCode.NotInRoom, ct); return; }
        if (room.HostId != conn.Id) { await SendError(conn, ErrorCode.NotHost, ct); return; }
        if (room.Lifecycle != RoomLifecycle.Lobby) return;

        // Parse — same layout as CreateRoom: [0x0A][slotCount][gameMode][roomNameLen:varint][roomName][mapNameLen:varint][mapName][blobLen:varint][blob]
        ReadOnlySpan<byte> payload;
        byte newSlotCount;
        byte newGameMode;
        string newRoomName;
        string newMapName;
        byte[] newMapBlob;
        {
            payload = new ReadOnlySpan<byte>(buf, 0, len);
            if (payload.Length < 4) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
            newSlotCount = payload[1];
            newGameMode = payload[2];

            // NEW: parse roomName first
            var (roomNameLen, cr) = Varint.Read(payload, 3);
            int pos = 3 + cr;
            if (pos + (int)roomNameLen > payload.Length || roomNameLen > 64) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
            newRoomName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)roomNameLen));
            pos += (int)roomNameLen;

            // Then mapName
            var (nameLen, c1) = Varint.Read(payload, pos);
            pos += c1;
            if (pos + (int)nameLen > payload.Length) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
            newMapName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)nameLen));
            pos += (int)nameLen;
            var (blobLen, c2) = Varint.Read(payload, pos); pos += c2;
            if (pos + (int)blobLen > payload.Length) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
            newMapBlob = payload.Slice(pos, (int)blobLen).ToArray();
        }

        // Validate: new slot count must accommodate all currently filled slots.
        int filledCount = room.Slots.Values.Count(s => s.OwnerId != null);
        if (newSlotCount < filledCount || newSlotCount < 2)
        {
            await SendError(conn, ErrorCode.ProtocolMismatch, ct);
            return;
        }

        // Rebuild slot layout. Preserve filled slots (re-derive TeamIds), trim/add open slots.
        var filledSlots = room.Slots
            .Where(kv => kv.Value.OwnerId != null)
            .OrderBy(kv => kv.Key)
            .ToList();
        room.Slots.Clear();

        // Re-seat filled slots into lowest available slot indices.
        byte nextSlot = 0;
        foreach (var kv in filledSlots)
        {
            byte newTeam = TeamForSlot(nextSlot, newGameMode);
            room.Slots[nextSlot] = new SlotInfo(kv.Value.OwnerId, kv.Value.DisplayName,
                nextSlot, newTeam, IsOpen: false, IsClosed: false, IsReady: false);
            // Update the connection's assigned player ID to the new slot index.
            if (kv.Value.OwnerId is Guid oid)
            {
                Connection? c;
                lock (_connectionsLock) _connections.TryGetValue(oid, out c);
                if (c != null) c.AssignedPlayerId = nextSlot;
            }
            nextSlot++;
        }
        // Fill remaining with open slots.
        for (byte i = nextSlot; i < newSlotCount; i++)
            room.Slots[i] = new SlotInfo(null, "", i, TeamForSlot(i, newGameMode), IsOpen: true, IsClosed: false, IsReady: false);

        room.SlotCount = newSlotCount;
        room.GameMode = newGameMode;
        room.RoomName = newRoomName;
        room.MapName = newMapName;
        room.MapBlob = newMapBlob;
        room.LastActivity = DateTime.UtcNow;

        Logger.Info($"conn={conn.Id} event=room-updated code={room.Code} map={newMapName} slots={newSlotCount} mode={newGameMode}");
        await BroadcastRoomState(room, ct);
    }

    /// <summary>
    /// Host kicks a player from a lobby slot. The kicked player receives a
    /// Kicked error before their socket is closed.
    /// </summary>
    private async Task HandleKickPlayer(Connection conn, byte[] buf, int len, CancellationToken ct)
    {
        var room = conn.CurrentRoom;
        if (room == null) { await SendError(conn, ErrorCode.NotInRoom, ct); return; }
        if (room.HostId != conn.Id) { await SendError(conn, ErrorCode.NotHost, ct); return; }
        if (room.Lifecycle != RoomLifecycle.Lobby) return;
        if (len < 2) { await SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }

        byte targetSlot = buf[1];
        if (!room.Slots.TryGetValue(targetSlot, out var slotInfo) || slotInfo.OwnerId == null)
            return; // nothing to kick
        if (slotInfo.OwnerId == conn.Id)
            return; // can't kick yourself

        var targetId = slotInfo.OwnerId.Value;
        Connection? target;
        lock (_connectionsLock) _connections.TryGetValue(targetId, out target);

        // Open the slot before notifying — the broadcast will show it as open.
        room.Slots[targetSlot] = new SlotInfo(null, "", slotInfo.ColorIndex, slotInfo.TeamId, IsOpen: true, IsClosed: false, IsReady: false);

        if (target != null)
        {
            target.CurrentRoom = null;
            target.AssignedPlayerId = null;
            try
            {
                await SendError(target, ErrorCode.Kicked, ct);
                await target.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "kicked", ct);
            }
            catch { }
        }

        room.LastActivity = DateTime.UtcNow;
        Logger.Info($"conn={conn.Id} event=player-kicked code={room.Code} slot={targetSlot}");
        await BroadcastRoomState(room, ct);
    }

    private async Task HandleListRooms(Connection conn, CancellationToken ct)
    {
        byte[]? bytes;
        var now = DateTime.UtcNow;
        lock (_cacheLock)
        {
            if (_cachedRoomList != null && now < _cachedRoomListExpiry)
            {
                bytes = _cachedRoomList;
            }
            else
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

                bytes = ms.ToArray();
                _cachedRoomList = bytes;
                _cachedRoomListExpiry = now.AddSeconds(1);
            }
        }

        try { await conn.Ws.SendAsync(bytes, WebSocketMessageType.Binary, true, ct); } catch { }
    }

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

    private static void RemoveConnFromRoom(Connection conn, Room room)
    {
        foreach (var kv in room.Slots.ToList())
        {
            if (kv.Value.OwnerId == conn.Id)
                room.Slots[kv.Key] = new SlotInfo(null, "", kv.Value.ColorIndex, kv.Value.TeamId, IsOpen: true, IsClosed: false, IsReady: false);
        }
    }

    private async Task FanOutCommands(Connection conn, byte[] payload, int len, CancellationToken ct)
    {
        if (conn.CurrentRoom is not { Lifecycle: RoomLifecycle.Playing } room) return;
        if (conn.AssignedPlayerId is not byte assigned) return;

        // Peek tick + playerId from message body (skip 0x10 type byte).
        try
        {
            var body = new ReadOnlySpan<byte>(payload, 1, len - 1);
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

    private async Task FanOutChat(Connection conn, byte[] payload, int len, CancellationToken ct)
    {
        if (conn.CurrentRoom is not Room room) return;
        if (conn.AssignedPlayerId is not byte assigned) return;
        if (len < 3) return;
        byte textLen = payload[1];
        if (len < 2 + textLen) return;

        // Rewrite message to include senderId (client sends [0x30][len][text],
        // relay fans out [0x30][senderId][len][text]).
        var msg = new byte[3 + textLen];
        msg[0] = Protocol.ChatMessage;
        msg[1] = assigned;
        msg[2] = textLen;
        Array.Copy(payload, 2, msg, 3, textLen);

        var segment = new ArraySegment<byte>(msg);
        foreach (var kv in room.Slots)
        {
            if (kv.Value.OwnerId is not Guid id) continue;
            if (id == conn.Id) continue;
            Connection? other;
            lock (_connectionsLock) _connections.TryGetValue(id, out other);
            if (other == null) continue;
            try { await other.Ws.SendAsync(segment, WebSocketMessageType.Binary, true, ct); } catch { }
        }
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
        ms.WriteByte(room.GameMode);

        var varintBuf = new byte[5];
        // Room name (NEW)
        var roomNameBytes = System.Text.Encoding.UTF8.GetBytes(room.RoomName);
        int vl = Varint.Write(varintBuf, 0, (uint)roomNameBytes.Length);
        ms.Write(varintBuf, 0, vl);
        ms.Write(roomNameBytes, 0, roomNameBytes.Length);

        // Map name
        var mapNameBytes = System.Text.Encoding.UTF8.GetBytes(room.MapName);
        vl = Varint.Write(varintBuf, 0, (uint)mapNameBytes.Length);
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
            ms.WriteByte(s.TeamId);
            byte flags = (byte)((s.IsOpen ? 1 : 0) | (s.IsClosed ? 2 : 0) | (s.IsReady ? 4 : 0));
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

    private static bool TryHandleHello(Connection conn, ReadOnlySpan<byte> payload)
    {
        // [0x01][proto:byte][sim:uint16 LE][nameLen:varint][name bytes]
        if (payload.Length < 4) return false;
        byte protoVer = payload[1];
        if (protoVer != Protocol.ProtocolVersion) return false;
        ushort simVer = (ushort)(payload[2] | (payload[3] << 8));
        var (nameLen, consumed) = Varint.Read(payload, 4);
        if (4 + consumed + (int)nameLen > payload.Length) return false;
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
        var msg = new byte[2];
        msg[0] = Protocol.HelloAck;
        msg[1] = Protocol.ProtocolVersion;
        try { await conn.Ws.SendAsync(msg, WebSocketMessageType.Binary, true, ct); } catch { }
    }

    private static async Task SendError(Connection conn, ErrorCode code, CancellationToken ct)
    {
        var msg = new byte[] { Protocol.Error, (byte)code };
        try { await conn.Ws.SendAsync(msg, WebSocketMessageType.Binary, true, ct); } catch { }
    }

    /// <summary>
    /// Reads a complete WebSocket binary message into <paramref name="buf"/>.
    /// Returns the byte length, or null if the connection closed / sent garbage.
    /// </summary>
    private async Task<int?> ReceiveFullMessage(Connection conn, byte[] buf, CancellationToken ct)
    {
        int total = 0;
        while (true)
        {
            WebSocketReceiveResult r;
            try { r = await conn.Ws.ReceiveAsync(new ArraySegment<byte>(buf, total, buf.Length - total), ct); }
            catch { return null; }

            if (r.MessageType == WebSocketMessageType.Close)
            {
                try { await conn.Ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", ct); } catch { }
                return null;
            }
            if (r.MessageType != WebSocketMessageType.Binary) return null;

            total += r.Count;
            if (r.EndOfMessage) return total;
            if (total >= buf.Length)
            {
                await SendError(conn, ErrorCode.MessageTooLarge, ct);
                return null;
            }
        }
    }
}
