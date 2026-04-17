using Blocker.Simulation.Blocks;
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
///     [flags: byte]         bit0 = hasTargetPos, bit1 = hasDirection, bit2 = queue, bit3 = hasUnitType
///     [targetX, targetY: varint × 2] if hasTargetPos
///     [direction: byte]               if hasDirection
///     [unitType: byte]                if hasUnitType
///
/// Determinism: no dictionary iteration, no locale, no floats, little-endian only.
/// </summary>
public static class CommandSerializer
{
    private const byte FlagHasTargetPos = 0x01;
    private const byte FlagHasDirection = 0x02;
    private const byte FlagQueue        = 0x04;
    private const byte FlagHasUnitType  = 0x08;

    public static byte[] Serialize(TickCommands tc)
    {
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
            if (c.TargetPos.HasValue)  flags |= FlagHasTargetPos;
            if (c.Direction.HasValue)  flags |= FlagHasDirection;
            if (c.Queue)               flags |= FlagQueue;
            if (c.UnitType.HasValue)   flags |= FlagHasUnitType;
            buf[i++] = flags;
            if (c.TargetPos.HasValue)
            {
                i += Varint.Write(buf, i, (uint)c.TargetPos.Value.X);
                i += Varint.Write(buf, i, (uint)c.TargetPos.Value.Y);
            }
            if (c.Direction.HasValue)
                buf[i++] = (byte)c.Direction.Value;
            if (c.UnitType.HasValue)
                buf[i++] = (byte)c.UnitType.Value;
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
            BlockType? unitType = null;
            if ((flags & FlagHasTargetPos) != 0)
            {
                var (x, n5) = Varint.Read(buf, i); i += n5;
                var (y, n6) = Varint.Read(buf, i); i += n6;
                target = new GridPos((int)x, (int)y);
            }
            if ((flags & FlagHasDirection) != 0)
                dir = (Direction)buf[i++];
            bool queue = (flags & FlagQueue) != 0;
            if ((flags & FlagHasUnitType) != 0)
                unitType = (BlockType)buf[i++];
            list.Add(new Command(playerId, type, ids, target, dir, queue, unitType));
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
