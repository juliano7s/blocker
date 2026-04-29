using Blocker.Simulation.Net;
using Xunit;

namespace Blocker.Simulation.Tests.Net;

public class ChatProtocolTests
{
    [Fact]
    public void FakeRelayClient_Chat_Delivers_To_Peers()
    {
        var c0 = new FakeRelayClient(0);
        var c1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(c0, c1);

        int receivedFrom = -1;
        string receivedText = "";
        c1.ChatReceived += (slot, text) =>
        {
            receivedFrom = slot;
            receivedText = text;
        };

        c0.SendChat("hello world");

        Assert.Equal(0, receivedFrom);
        Assert.Equal("hello world", receivedText);
    }

    [Fact]
    public void FakeRelayClient_Chat_Not_Delivered_To_Sender()
    {
        var c0 = new FakeRelayClient(0);
        var c1 = new FakeRelayClient(1);
        FakeRelayClient.Connect(c0, c1);

        bool selfReceived = false;
        c0.ChatReceived += (_, _) => selfReceived = true;

        c0.SendChat("test");

        Assert.False(selfReceived);
    }

    [Fact]
    public void FakeRelayClient_Chat_Broadcast_To_All_Peers()
    {
        var c0 = new FakeRelayClient(0);
        var c1 = new FakeRelayClient(1);
        var c2 = new FakeRelayClient(2);
        FakeRelayClient.Connect(c0, c1, c2);

        int count = 0;
        c1.ChatReceived += (_, _) => count++;
        c2.ChatReceived += (_, _) => count++;

        c0.SendChat("all chat");

        Assert.Equal(2, count);
    }

    [Fact]
    public void ChatMessage_Protocol_Constant_Is_In_Social_Range()
    {
        Assert.Equal(0x30, Protocol.ChatMessage);
    }
}
