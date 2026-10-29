using Crystal.Chat;
using Crystal.Reasoning;

using CrystalCode.Engine.Sessions;

using Xunit;

namespace CrystalCode.Engine.Tests.Sessions;

public sealed class ReasoningStreamTextTests
{
    [Fact]
    public void Next_SeparatesSegmentsOfOneItem()
    {
        var text = new ReasoningStreamText();

        var parts = new[]
        {
            text.Next(Summary(0, 0, "**One**")),
            text.Next(Summary(0, 0, " more")),
            text.Next(Summary(0, 1, "**Two**"))
        };

        Assert.Equal("**One** more\n\n**Two**", string.Concat(parts));
    }

    [Fact]
    public void Next_JoinsFragmentsThatArriveAsSeparateItems()
    {
        var text = new ReasoningStreamText();

        var joined = text.Next(Summary(0, 0, "Thin")) + text.Next(Summary(1, 0, "king"));

        Assert.Equal("Thinking", joined);
    }

    [Fact]
    public void Reset_StartsWithoutASeparator()
    {
        var text = new ReasoningStreamText();
        text.Next(Summary(0, 0, "one"));

        text.Reset();

        Assert.Equal("two", text.Next(Summary(0, 1, "two")));
    }

    [Fact]
    public void Next_IgnoresEmptyDeltas()
    {
        var text = new ReasoningStreamText();
        text.Next(Summary(0, 0, "one"));

        Assert.Equal(string.Empty, text.Next(Summary(0, 1, string.Empty)));
        Assert.Equal("one", text.Next(Summary(0, 0, "one")));
    }

    [Fact]
    public void Join_SeparatesSegments()
    {
        var content = new ReasoningContent(
        [
            new ReasoningText("**One**", ReasoningTextKind.Summary),
            new ReasoningText("**Two**", ReasoningTextKind.Summary)
        ]);

        Assert.Equal("**One**\n\n**Two**", ReasoningStreamText.Join(content));
    }

    private static ChatReasoningTextDelta Summary(int item, int segment, string text) =>
        new(0, item, segment, ReasoningTextKind.Summary, text);
}
