using CrystalHarness.Display;

using Xunit;

namespace CrystalHarness.Tests.Display;

public sealed class StreamNameTests
{
    [Fact]
    public void Apply_KeepsSnapshotInsteadOfRepeating()
    {
        var name = string.Empty;
        name = StreamName.Apply(name, "read");
        name = StreamName.Apply(name, "read");
        name = StreamName.Apply(name, "read");

        Assert.Equal("read", name);
        Assert.Equal("Read", DisplayCase.Token(name));
    }

    [Fact]
    public void Apply_AcceptsIncrementalDeltas()
    {
        var name = StreamName.Apply(string.Empty, "r");
        name = StreamName.Apply(name, "e");
        name = StreamName.Apply(name, "ad");

        Assert.Equal("read", name);
    }
}
