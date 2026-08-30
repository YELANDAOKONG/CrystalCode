using CrystalHarness.Compaction;

using Xunit;

namespace CrystalHarness.Tests.Compaction;

public sealed class TokenEstimatorTests
{
    [Fact]
    public void Text_UsesFourCharactersPerToken()
    {
        Assert.Equal(0, TokenEstimator.Text(""));
        Assert.Equal(1, TokenEstimator.Text("abcd"));
        Assert.Equal(2, TokenEstimator.Text("abcdefgh"));
    }
}
