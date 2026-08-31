using CrystalCode.Display.Paint;

using Xunit;

namespace CrystalCode.Display.Tests.Paint;

public sealed class StreamToolNamesTests
{
    [Fact]
    public void Apply_CoalescesDeltasOfTheSameCall()
    {
        var names = new StreamToolNames();

        var name = names.Apply(0, 0, "r");
        name = names.Apply(0, 0, "ead");

        Assert.Equal("read", name);
        Assert.Equal("Read", DisplayCase.Token(name));
    }

    [Fact]
    public void Apply_KeepsSnapshotInsteadOfRepeating()
    {
        var names = new StreamToolNames();

        var name = names.Apply(0, 0, "read");
        name = names.Apply(0, 0, "read");
        name = names.Apply(0, 0, "read");

        Assert.Equal("read", name);
    }

    [Fact]
    public void Apply_SequentialCallsNeverConcatenate()
    {
        var names = new StreamToolNames();

        names.Apply(0, 0, "todowrite");
        var name = names.Apply(0, 1, "bash");
        name = names.Apply(0, 2, "grep");
        name = names.Apply(0, 3, "grep");

        Assert.Equal("grep", name);
        Assert.Equal("TodoWrite", DisplayCase.Token(names.Apply(0, 0, "todowrite")));
        Assert.Equal("Bash", DisplayCase.Token(names.Apply(0, 1, "bash")));
        Assert.Equal("Grep", DisplayCase.Token(names.Apply(0, 2, "grep")));
    }

    [Fact]
    public void Apply_InterleavedDeltasStayWithTheirCall()
    {
        var names = new StreamToolNames();

        names.Apply(0, 0, "re");
        names.Apply(0, 1, "ba");
        var first = names.Apply(0, 0, "ad");
        var second = names.Apply(0, 1, "sh");

        Assert.Equal("read", first);
        Assert.Equal("bash", second);
    }

    [Fact]
    public void Clear_ForgetsAccumulatedNames()
    {
        var names = new StreamToolNames();
        names.Apply(0, 0, "read");

        names.Clear();

        Assert.Equal("write", names.Apply(0, 0, "write"));
    }
}
