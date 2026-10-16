using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class MessageQueueTests
{
    [Fact]
    public void Enqueue_QuickPromptPreservesBodyWithoutInterrupting()
    {
        var queue = new MessageQueue();
        queue.Enqueue(" body\n", interruptAfterTools: false, preserveText: true);
        Assert.False(queue.InterruptAfterTools);
        Assert.Equal(" body\n", queue.Drain());
        queue.Enqueue("normal");
        Assert.True(queue.InterruptAfterTools);
        queue.Clear();
        Assert.False(queue.InterruptAfterTools);
    }

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
    public void Snapshot_DoesNotDrain()
    {
        var queue = new MessageQueue();
        queue.Enqueue("one");
        queue.Enqueue("two");

        Assert.Equal(["one", "two"], queue.Snapshot());
        Assert.Equal(2, queue.Count);
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
