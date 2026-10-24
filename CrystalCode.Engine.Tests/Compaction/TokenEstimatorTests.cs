using System.Text;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Compaction;

using Xunit;

namespace CrystalCode.Engine.Tests.Compaction;

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

    [Fact]
    public void Item_CountsReasoningStateOnceWhenItRepeatsTheText()
    {
        var text = new string('a', 4_000);
        var state = new OpaqueReasoningState("test", Encoding.UTF8.GetBytes(text + new string('b', 400)));
        var reasoning = new ChatReasoningItem(new ReasoningContent([new ReasoningText(text, ReasoningTextKind.Trace)], state));

        Assert.Equal(1_100, TokenEstimator.Item(reasoning));
    }

    [Fact]
    public void Item_CountsReasoningTextWhenThereIsNoState()
    {
        var reasoning = new ChatReasoningItem(
            new ReasoningContent([new ReasoningText("abcdefgh", ReasoningTextKind.Trace)]));

        Assert.Equal(2, TokenEstimator.Item(reasoning));
    }

    [Fact]
    public void Item_CountsOpaqueStateWhenThereIsNoReadableText()
    {
        var reasoning = new ChatReasoningItem(
            new ReasoningContent(state: new OpaqueReasoningState("test", new byte[400])));

        Assert.Equal(100, TokenEstimator.Item(reasoning));
    }

    [Fact]
    public void Context_AddsOnlyItemsAfterTheAnchor()
    {
        var reply = new ChatMessage(ChatRole.Assistant, new string('x', 8_000));
        var call = new ToolCall("c1", "read", "{}", ToolInputKind.Json);
        var result = new ToolResult("c1", new string('y', 400), ToolResultStatus.Success);
        ChatItem[] items = [new ChatMessage(ChatRole.User, new string('u', 400_000)), reply, call, result];
        var anchor = new ContextAnchor(30_000, call);

        // The 400,000 character user message is inside the report and is not counted again.
        Assert.Equal(30_000 + 100, TokenEstimator.Context(items, anchor));
        Assert.True(TokenEstimator.Items(items) > 100_000);
    }

    [Fact]
    public void Context_EstimatesTheWholeListWhenTheAnchorIsGone()
    {
        ChatItem[] items = [new ChatMessage(ChatRole.User, new string('u', 4_000))];
        var removed = new ChatMessage(ChatRole.Assistant, "gone");
        var anchor = new ContextAnchor(30_000, removed);

        Assert.False(TokenEstimator.TryContext(items, anchor, out _));
        Assert.Equal(1_000, TokenEstimator.Context(items, anchor));
        Assert.Equal(1_000, TokenEstimator.Context(items, null));
    }

    [Fact]
    public void Anchor_RequiresUsageAndAnItem()
    {
        ChatItem[] items = [new ChatMessage(ChatRole.User, "hi")];

        Assert.Null(ContextAnchor.From(null, items));
        Assert.Null(ContextAnchor.From(new TokenUsage(10, 5), []));
        var anchor = ContextAnchor.From(new TokenUsage(10, 5), items);
        Assert.Equal(15, anchor!.Tokens);
        Assert.Same(items[0], anchor.Last);
    }
}
