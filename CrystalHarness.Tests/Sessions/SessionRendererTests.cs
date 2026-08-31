using CrystalHarness.Sessions;

using Xunit;

namespace CrystalHarness.Tests.Sessions;

public sealed class SessionRendererTests
{
    [Fact]
    public void TryClearComposer_ClearsTextThenReturnsFalse()
    {
        var renderer = new SessionRenderer();
        renderer.SeedComposer("draft");

        Assert.True(renderer.TryClearComposer());
        Assert.False(renderer.TryClearComposer());
    }

    [Fact]
    public void TryClearComposer_IsFalseWhenEmpty()
    {
        var renderer = new SessionRenderer();

        Assert.False(renderer.TryClearComposer());
    }
}
