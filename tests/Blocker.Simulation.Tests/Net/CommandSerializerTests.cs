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
