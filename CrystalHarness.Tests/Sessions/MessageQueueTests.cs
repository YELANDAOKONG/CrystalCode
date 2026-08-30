using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class MessageQueueTests
{
    [Fact]
    public void Drain_JoinsQueuedMessages()
    {
        var queue = new MessageQueue();
        queue.Enqueue(" first ");
        queue.Enqueue("second");

        Assert.Equal(2, queue.Count);
        Assert.Equal("first\n\nsecond", queue.Drain());
        Assert.Equal(0, queue.Count);
        Assert.Null(queue.Drain());
    }

    [Fact]
    public void Clear_DropsQueuedMessages()
    {
        var queue = new MessageQueue();
        queue.Enqueue("keep me");
        queue.Clear();

        Assert.Equal(0, queue.Count);
        Assert.Null(queue.Drain());
    }
}
