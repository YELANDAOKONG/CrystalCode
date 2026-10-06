using CrystalCode.Engine.Version;

using Xunit;

namespace CrystalCode.Engine.Tests.Version;

public sealed class BuildIdentityTextTests
{
    private const string Product = "3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a";
    private const string Library = "1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d";

    [Fact]
    public void Format_AlignsRecordedLines()
    {
        var text = BuildIdentityText.Format(new BuildIdentity(
            Product,
            Library,
            "10.0.201",
            ".NET 10.0.4"));

        Assert.Equal(
            """
            Crystal Code  3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a
            Crystal       1a2b3c4d5e6f708192a3b4c5d6e7f8091a2b3c4d
            SDK           10.0.201
            Runtime       .NET 10.0.4
            """.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            text);
    }

    [Fact]
    public void Format_OmitsBlankValues()
    {
        var text = BuildIdentityText.Format(new BuildIdentity(
            Product,
            "  ",
            null,
            ".NET 10.0.4"));

        Assert.Equal(
            """
            Crystal Code  3f2a1b0c9d8e7f6a5b4c3d2e1f0a9b8c7d6e5f4a
            Runtime       .NET 10.0.4
            """.Replace("\n", Environment.NewLine, StringComparison.Ordinal),
            text);
        Assert.DoesNotContain("1.0.0", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Format_ReturnsEmptyWhenNothingIsRecorded()
    {
        var text = BuildIdentityText.Format(new BuildIdentity(null, null, null, null));

        Assert.Equal(string.Empty, text);
    }
}
