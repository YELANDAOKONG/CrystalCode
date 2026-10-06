using CrystalCode.Engine.Version;

using Xunit;

namespace CrystalCode.Engine.Tests.Version;

public sealed class SourceRevisionTests
{
    private const string Commit = "3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a";

    [Fact]
    public void FromInformationalVersion_ReturnsCommitAfterPlaceholder()
    {
        var revision = SourceRevision.FromInformationalVersion("1.0.0+" + Commit);

        Assert.Equal(Commit, revision);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1.0.0")]
    [InlineData("1.0.0+")]
    [InlineData("1.0.0+3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4")]
    [InlineData("1.0.0+3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a0")]
    [InlineData("1.0.0+gggggggggggggggggggggggggggggggggggggggg")]
    public void FromInformationalVersion_IgnoresPlaceholderAndNonCommits(string? informationalVersion)
    {
        Assert.Null(SourceRevision.FromInformationalVersion(informationalVersion));
    }
}
