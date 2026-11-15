using CrystalCode.Providers.Compatible;

using Xunit;

namespace CrystalCode.Providers.Tests.Compatible;

public sealed class CompatibleWireErrorBodyTests
{
    [Fact]
    public void ReadError_UsesATopLevelDetail()
    {
        var (code, message) = CompatibleWire.ReadError("""{"detail":"System messages are not allowed"}""");

        Assert.Null(code);
        Assert.Equal("System messages are not allowed", message);
    }

    [Fact]
    public void DescribeBody_FlattensAndTruncatesUnrecognizedText()
    {
        Assert.Equal("a b", CompatibleWire.DescribeBody("a\n  b\n"));
        Assert.Null(CompatibleWire.DescribeBody("  \n"));
        Assert.Equal(503, CompatibleWire.DescribeBody(new string('x', 900))!.Length);
    }
}
