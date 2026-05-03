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
    public event Action<int, string>? ChatReceived;

    // Lobby-level events — also fired on main thread.
    public event Action? HelloAcked;
    public event Action<RoomSummary[]>? RoomListReceived;
    public event Action<RoomStatePayload>? RoomStateReceived;
    public event Action<int /*localPlayerId*/, int[] /*activePlayerIds*/>? GameStarted;
    public event Action<ErrorCode>? ServerError;
    public event Action? ConnectionClosed;

    private int _localPlayerId;
    public void SetLocalPlayerId(int id) => _localPlayerId = id;

    private bool _closedNotified;

    // Ping measurement
    private long _pingSentTicks;
    private float _pingMs = -1;
    public float PingMs => _pingMs;

    public void SendPing()
    {
        _pingSentTicks = System.Diagnostics.Stopwatch.GetTimestamp();
        _outbox.Writer.TryWrite(new byte[] { Protocol.Ping });
    }

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
        // M1: log-only, minimal payload. Relay only records that we reported.
        var msg = new byte[] { Protocol.DesyncReport, (byte)_localPlayerId };
        _outbox.Writer.TryWrite(msg);
    }

    public void SendChat(string text)
    {
        if (string.IsNullOrEmpty(text)) return;
        var textBytes = System.Text.Encoding.UTF8.GetBytes(text);
        if (textBytes.Length > 128) textBytes = textBytes[..128];
        var msg = new byte[2 + textBytes.Length];
        msg[0] = Protocol.ChatMessage;
        msg[1] = (byte)textBytes.Length;
        Array.Copy(textBytes, 0, msg, 2, textBytes.Length);
        _outbox.Writer.TryWrite(msg);
    }

    public void SendCreateRoom(byte slotCount, GameMode gameMode, string roomName, string mapName, byte[] mapBlob) =>
        SendRoomConfig(Protocol.CreateRoom, slotCount, gameMode, roomName, mapName, mapBlob);

    public void SendRematch() =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.Rematch });

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

    public void SendUpdateRoom(byte slotCount, GameMode gameMode, string roomName, string mapName, byte[] mapBlob) =>
        SendRoomConfig(Protocol.UpdateRoom, slotCount, gameMode, roomName, mapName, mapBlob);

    private void SendRoomConfig(byte type, byte slotCount, GameMode gameMode, string roomName, string mapName, byte[] mapBlob)
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
        msg[o++] = type;
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

    public void SendListRooms() =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.ListRooms });

    public void SendSetReady(bool ready) =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.SetReady, (byte)(ready ? 1 : 0) });

    public void SendKickPlayer(byte slotId) =>
        _outbox.Writer.TryWrite(new byte[] { Protocol.KickPlayer, slotId });

    /// <summary>Must be called on main thread every frame by MultiplayerTickRunner.</summary>
    public void DrainInbound()
    {
        while (_inbox.TryDequeue(out var msg))
            Dispatch(msg);

        // ReceiveLoop sets State=Closed from a background thread; we surface
        // the transition on the main thread (exactly once) so subscribers
        // don't have to think about threading.
        if (State == ConnState.Closed && !_closedNotified)
        {
            _closedNotified = true;
            ConnectionClosed?.Invoke();
        }
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
                case Protocol.ChatMessage:
                {
                    byte senderId = msg[1];
                    byte textLen = msg[2];
                    string text = System.Text.Encoding.UTF8.GetString(msg, 3, textLen);
                    ChatReceived?.Invoke(senderId, text);
                    break;
                }
                case Protocol.Error: ServerError?.Invoke((ErrorCode)msg[1]); break;
                case Protocol.Ping: _outbox.Writer.TryWrite(new byte[] { Protocol.Pong }); break;
                case Protocol.Pong:
                    if (_pingSentTicks > 0)
                    {
                        long now = System.Diagnostics.Stopwatch.GetTimestamp();
                        _pingMs = (float)((now - _pingSentTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency);
                        _pingSentTicks = 0;
                    }
                    break;
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

public sealed record RoomSummary(
    string Code, string RoomName, byte PlayerCount,
    byte SlotCount, string MapName, byte GameMode);
