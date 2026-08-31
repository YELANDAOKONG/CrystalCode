using CrystalCode.Display.Shell;

using Xunit;

namespace CrystalCode.Display.Tests.Shell;

public sealed class KeyBurstTests
{
    [Fact]
    public void NeedsEscapeHold_CompleteSgrDoesNotWait()
    {
        var burst = Csi("\u001b[<64;12;8M");

        Assert.False(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
    }

    [Fact]
    public void NeedsEscapeHold_IncompleteSgrWaits()
    {
        var burst = Csi("\u001b[<64");

        Assert.True(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
    }

    [Fact]
    public void NeedsEscapeHold_BareEscapeWaits()
    {
        var burst = Csi("\u001b");

        Assert.True(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
        Assert.False(KeyBurst.NeedsEscapeHold(burst, moreAvailable: true));
    }

    [Fact]
    public void NeedsEscapeHold_CompleteCsiArrowDoesNotWait()
    {
        var burst = Csi("\u001b[A");

        Assert.False(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
    }

    [Fact]
    public void NeedsEscapeHold_IncompleteX10Waits()
    {
        var burst = Csi("\u001b[M");

        Assert.True(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
    }

    [Fact]
    public async Task ReadAsync_CompleteSgrDoesNotHold()
    {
        var keys = new Queue<ConsoleKeyInfo>(Csi("\u001b[<64;12;8M"));
        var holds = 0;

        var burst = await KeyBurst.ReadAsync(
            () => keys.Count > 0,
            keys.Dequeue,
            _ =>
            {
                holds++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(0, holds);
        Assert.Equal(11, burst.Count);
    }

    [Fact]
    public async Task ReadAsync_BareEscapeHoldsOnce()
    {
        var keys = new Queue<ConsoleKeyInfo>(Csi("\u001b"));
        var holds = 0;

        var burst = await KeyBurst.ReadAsync(
            () => keys.Count > 0,
            keys.Dequeue,
            _ =>
            {
                holds++;
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, holds);
        Assert.Single(burst);
    }

    [Fact]
    public async Task ReadAsync_IncompleteSgrHoldsOnceThenDrains()
    {
        var keys = new Queue<ConsoleKeyInfo>(Csi("\u001b[<64"));
        var holds = 0;

        var burst = await KeyBurst.ReadAsync(
            () => keys.Count > 0,
            keys.Dequeue,
            _ =>
            {
                holds++;
                foreach (var key in Csi(";12;8M"))
                {
                    keys.Enqueue(key);
                }

                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(1, holds);
        Assert.False(KeyBurst.NeedsEscapeHold(burst, moreAvailable: false));
    }

    private static List<ConsoleKeyInfo> Csi(string text)
    {
        var burst = new List<ConsoleKeyInfo>();
        foreach (var ch in text)
        {
            var key = ch == '\u001b' ? ConsoleKey.Escape : ConsoleKey.None;
            burst.Add(new ConsoleKeyInfo(ch, key, false, false, false));
        }

        return burst;
    }
}
