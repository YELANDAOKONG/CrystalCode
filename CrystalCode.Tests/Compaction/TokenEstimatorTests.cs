using CrystalCode.Compaction;

using Xunit;

namespace CrystalCode.Tests.Compaction;

public sealed class TokenEstimatorTests
{
    [Fact]
    public void Text_UsesFourCharactersPerToken()
    {
        Assert.Equal(0, TokenEstimator.Text(""));
        Assert.Equal(1, TokenEstimator.Text("abcd"));
        Assert.Equal(2, TokenEstimator.Text("abcdefgh"));
    }

    [Fact]
    public void Characters_MatchesTextLength()
    {
        Assert.Equal(0, TokenEstimator.Characters(0));
        Assert.Equal(0, TokenEstimator.Characters(-3));
        Assert.Equal(1, TokenEstimator.Characters(4));
        Assert.Equal(2, TokenEstimator.Characters(8));
    }
}
