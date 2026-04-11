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
                // Host left the lobby — close the room and notify everyone.
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
                case Protocol.CreateRoom: HandleCreateRoom(conn, new ReadOnlySpan<byte>(buf, 0, len), ct); break;
                case Protocol.JoinRoom:   await HandleJoinRoom(conn, buf, len, ct); break;
                case Protocol.LeaveRoom:  await HandleLeaveRoom(conn, ct); break;
                case Protocol.StartGame:  await HandleStartGame(conn, ct); break;
                case Protocol.Commands:   await FanOutCommands(conn, buf, len, ct); break;
                case Protocol.Hash:       await FanOutHash(conn, buf, len, ct); break;
                default:
                    await SendError(conn, ErrorCode.UnknownMessageType, ct);
                    return;
            }
        }
    }

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
        if (pos + (int)nameLen > payload.Length) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
        string mapName = System.Text.Encoding.UTF8.GetString(payload.Slice(pos, (int)nameLen));
        pos += (int)nameLen;
        var (blobLen, c2) = Varint.Read(payload, pos); pos += c2;
        if (pos + (int)blobLen > payload.Length) { _ = SendError(conn, ErrorCode.ProtocolMismatch, ct); return; }
        var mapBlob = payload.Slice(pos, (int)blobLen).ToArray();

        var room = _rooms.TryCreate(conn.Id, conn.RemoteIp, _opts.MaxRoomsPerIp,
                                     conn.SimulationVersion, mapBlob, mapName, slotCount);
        if (room == null) { _ = SendError(conn, ErrorCode.TooManyRooms, ct); return; }

        // Host takes slot 0 by default; remaining slots start open.
        room.Slots[0] = new SlotInfo(conn.Id, conn.ClientName, 0, IsOpen: false, IsClosed: false);
        for (byte i = 1; i < slotCount; i++)
            room.Slots[i] = new SlotInfo(null, "", i, IsOpen: true, IsClosed: false);

        conn.CurrentRoom = room;
        conn.AssignedPlayerId = 0;
        Logger.Info($"conn={conn.Id} event=room-created code={room.Code} map={mapName}");
        _ = BroadcastRoomState(room, ct);
    }

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

    private static void RemoveConnFromRoom(Connection conn, Room room)
    {
        foreach (var kv in room.Slots.ToList())
        {
            if (kv.Value.OwnerId == conn.Id)
                room.Slots[kv.Key] = new SlotInfo(null, "", kv.Value.ColorIndex, IsOpen: true, IsClosed: false);
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
