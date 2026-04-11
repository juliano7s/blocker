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
            // Task 16 adds room cleanup here.
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
