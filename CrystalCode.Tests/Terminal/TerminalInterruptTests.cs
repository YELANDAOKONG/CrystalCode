using CrystalCode.Terminal;

using Xunit;

namespace CrystalCode.Tests.Terminal;

public sealed class TerminalInterruptTests
{
    [Fact]
    public void CancelReads_CancelsTheLinkedToken()
    {
        using var interrupt = TerminalInterrupt.Link(CancellationToken.None);

        interrupt.CancelReads();

        Assert.True(interrupt.Token.IsCancellationRequested);
    }

    [Fact]
    public void CancelReads_AfterDispose_DoesNotThrow()
    {
        var interrupt = TerminalInterrupt.Link(CancellationToken.None);
        interrupt.Dispose();

        interrupt.CancelReads();
    }
}
