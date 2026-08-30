using Crystal.Chat;
using Crystal.Tools;

using CrystalHarness.Approvals;
using CrystalHarness.Tests.Tools;
using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Approvals;

public sealed class ModelApprovalReviewerTests
{
    [Fact]
    public async Task ReviewAsync_ParsesAllowDecision()
    {
        using var root = new TemporaryWorkspace();
        var client = new FixedChatClient(
            """{"decision":"allow","reason":"Adds the requested test."}""");
        var reviewer = new ModelApprovalReviewer(client);
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "Add a failing test."));

        Assert.True(verdict.IsAllow);
        Assert.Equal("Adds the requested test.", verdict.Reason);
        Assert.NotNull(client.LastRequest);
        Assert.Contains(
            client.LastRequest.Items.OfType<ChatMessage>(),
            message => message.Role == ChatRole.User
                && message.Text.Contains("Add a failing test.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ReviewAsync_AsksWhenUserRequestIsMissing()
    {
        using var root = new TemporaryWorkspace();
        var reviewer = new ModelApprovalReviewer(new FixedChatClient("unused"));
        var classification = new ToolClassifier(new Workspace(root.Path))
            .Classify(WriteCall());

        var verdict = await reviewer.ReviewAsync(
            new ApprovalReviewRequest(WriteCall(), classification, "  "));

        Assert.False(verdict.IsAllow);
        Assert.False(verdict.IsDeny);
        Assert.Equal("No user request is available to review against.", verdict.Reason);
    }

    private static ToolCall WriteCall() =>
        new("1", WriteTool.ToolName, """{"path":"src/App.cs","contents":"x"}""");
}
