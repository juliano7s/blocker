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

    [Fact]
    public void RoomState_Serialization_Roundtrip()
    {
        // Arrange
        var slots = new SlotStateEntry[]
        {
            new("Host", 0, 0, false, false, false),
            new("Joiner", 1, 1, false, false, true),
            new("", 2, 0, true, false, false),
            new("", 3, 1, false, true, false)
        };
        var original = new RoomStatePayload(
            "CODE", Guid.NewGuid(), 1, GameMode.Teams, "My Room", "Map Name", slots);

        // Act - Simulate what RelayServer.BroadcastRoomState does (manually for test simplicity)
        var ms = new MemoryStream();
        ms.WriteByte(Protocol.RoomState);
        ms.Write(System.Text.Encoding.ASCII.GetBytes(original.Code));
        ms.Write(original.HostId.ToByteArray());
        ms.WriteByte((byte)original.Slots.Length);
        ms.WriteByte((byte)(original.SimulationVersion & 0xFF));
        ms.WriteByte((byte)((original.SimulationVersion >> 8) & 0xFF));
        ms.WriteByte((byte)original.GameMode);

        var varintBuf = new byte[5];
        var rnBytes = System.Text.Encoding.UTF8.GetBytes(original.RoomName);
        ms.Write(varintBuf, 0, Varint.Write(varintBuf, 0, (uint)rnBytes.Length));
        ms.Write(rnBytes);

        var mnBytes = System.Text.Encoding.UTF8.GetBytes(original.MapName);
        ms.Write(varintBuf, 0, Varint.Write(varintBuf, 0, (uint)mnBytes.Length));
        ms.Write(mnBytes);

        foreach (var s in original.Slots)
        {
            var nBytes = System.Text.Encoding.UTF8.GetBytes(s.DisplayName);
            ms.Write(varintBuf, 0, Varint.Write(varintBuf, 0, (uint)nBytes.Length));
            ms.Write(nBytes);
            ms.WriteByte(s.ColorIndex);
            ms.WriteByte(s.TeamId);
            byte flags = (byte)((s.IsOpen ? 1 : 0) | (s.IsClosed ? 2 : 0) | (s.IsReady ? 4 : 0));
            ms.WriteByte(flags);
        }

        // Act - Use RelayClient.ParseRoomState
        var parsed = ParseRoomState(ms.ToArray());

        // Assert
        Assert.Equal(original.Code, parsed.Code);
        Assert.Equal(original.HostId, parsed.HostId);
        Assert.Equal(original.SimulationVersion, parsed.SimulationVersion);
        Assert.Equal(original.GameMode, parsed.GameMode);
        Assert.Equal(original.RoomName, parsed.RoomName);
        Assert.Equal(original.MapName, parsed.MapName);
        Assert.Equal(original.Slots.Length, parsed.Slots.Length);
        for (int i = 0; i < original.Slots.Length; i++)
        {
            Assert.Equal(original.Slots[i].DisplayName, parsed.Slots[i].DisplayName);
            Assert.Equal(original.Slots[i].ColorIndex, parsed.Slots[i].ColorIndex);
            Assert.Equal(original.Slots[i].TeamId, parsed.Slots[i].TeamId);
            Assert.Equal(original.Slots[i].IsOpen, parsed.Slots[i].IsOpen);
            Assert.Equal(original.Slots[i].IsClosed, parsed.Slots[i].IsClosed);
            Assert.Equal(original.Slots[i].IsReady, parsed.Slots[i].IsReady);
        }
    }

    // Manual copy of ParseRoomState for test simplicity (avoids godot dependency)
    private static RoomStatePayload ParseRoomState(byte[] msg)
    {
        int o = 1;
        string code = System.Text.Encoding.ASCII.GetString(msg, o, 4); o += 4;
        var hostBytes = new byte[16]; Array.Copy(msg, o, hostBytes, 0, 16); o += 16;
        byte slotCount = msg[o++];
        ushort simVer = (ushort)(msg[o] | (msg[o + 1] << 8)); o += 2;
        GameMode gameMode = (GameMode)msg[o++];
        var (roomNameLen, cr) = Varint.Read(msg, o); o += cr;
        string roomName = System.Text.Encoding.UTF8.GetString(msg, o, (int)roomNameLen); o += (int)roomNameLen;
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
}
