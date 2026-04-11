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

    private int _localPlayerId;
    public void SetLocalPlayerId(int id) => _localPlayerId = id;

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
