using System.Text.Json;

using Crystal;
using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Engine.Home;
using CrystalCode.Engine.Sessions;
using CrystalCode.Run;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class RunJsonLogTests
{
    private const string SessionId = "parent-session";

    [Fact]
    public void StepFinish_ReportsTheTurnTotalSeparatelyFromTheLastRequest()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new TurnStarted());
        log.OnEvent(new TurnFinished(
            new TurnResult(
                TurnStopReason.Completed,
                2,
                1,
                new TokenUsage(30, 4, 1),
                [],
                new TokenUsage(50, 9, 3)),
            new TokenUsage(30, 4, 1),
            new TokenUsage(120, 20),
            128_000));

        var finish = Single(output, "step_finish");
        Assert.Equal("completed", finish.GetProperty("reason").GetString());
        Assert.Equal(2, finish.GetProperty("modelCalls").GetInt32());
        var usage = finish.GetProperty("usage");
        Assert.Equal(50, usage.GetProperty("inputTokens").GetInt64());
        Assert.Equal(9, usage.GetProperty("outputTokens").GetInt64());
        Assert.Equal(3, usage.GetProperty("reasoningTokens").GetInt64());
        Assert.Equal(30, finish.GetProperty("lastRequestUsage").GetProperty("inputTokens").GetInt64());
        var cumulative = finish.GetProperty("cumulativeUsage");
        Assert.Equal(120, cumulative.GetProperty("inputTokens").GetInt64());
        Assert.False(cumulative.TryGetProperty("reasoningTokens", out _));
        Assert.False(finish.TryGetProperty("inputTokens", out _));
    }

    [Fact]
    public void StepFinish_OmitsATotalThatAProviderDidNotReport()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new TurnFinished(
            new TurnResult(TurnStopReason.Completed, 2, 0, null, []),
            null,
            null,
            128_000));

        var finish = Single(output, "step_finish");
        Assert.False(finish.TryGetProperty("usage", out _));
        Assert.False(finish.TryGetProperty("lastRequestUsage", out _));
        Assert.False(finish.TryGetProperty("cumulativeUsage", out _));
    }

    [Fact]
    public void ToolUse_StripsTheTrustedMarkerPrefixAndListsTheImages()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new ToolCallsIssued([JsonToolCall("c1", "view_image", """{"path":"a.png"}""")]));
        log.OnEvent(new ToolResultsReceived(
        [
            new ToolResult("c1", "rendered\n\n" + ImageMarkerText.Tag(3), ToolResultStatus.Success)
        ]));

        var tool = Single(output, "tool_use");
        var text = tool.GetProperty("output").GetString();
        Assert.Equal("rendered\n\n[Image #3]", text);
        Assert.DoesNotContain(ImageMarkerText.Prefix, text!);
        Assert.Equal([3], tool.GetProperty("images").EnumerateArray().Select(static item => item.GetInt32()));
    }

    [Fact]
    public void ToolUse_DoesNotBorrowAnotherCallWhenTheIdDoesNotMatch()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new ToolCallsIssued([JsonToolCall("c1", "bash", """{"command":"rm x"}""")]));
        log.OnEvent(new ToolResultsReceived([new ToolResult("c9", "done", ToolResultStatus.Success)]));

        var tool = Single(output, "tool_use");
        Assert.Equal("c9", tool.GetProperty("callId").GetString());
        Assert.False(tool.TryGetProperty("name", out _));
        Assert.False(tool.TryGetProperty("arguments", out _));
        Assert.False(tool.TryGetProperty("images", out _));
    }

    [Fact]
    public void Retry_DiscardsTheFailedAttemptsPartialText()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "par")));
        log.OnEvent(new RetryScheduled(new SessionRetryAttempt(1, "Too Many Requests", TimeSpan.FromSeconds(2))));
        log.OnEvent(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "done")));
        log.OnEvent(new ModelRoundClosed());

        var events = Read(output);
        Assert.Equal(["retry", "text"], events.Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal("done", events[1].GetProperty("text").GetString());
    }

    [Fact]
    public void ChildEvents_UseTheParentShapesUnderAChildPrefix()
    {
        var output = new StringWriter();
        var log = new RunJsonLog(output, showThinking: true);
        log.BindSession(SessionId);
        var invocation = Guid.NewGuid();
        var running = new ChildSessionDocument { Id = "child-1", Agent = "explore", Label = "scan", Status = "running" };
        log.OnEvent(new ChildSessionChanged(running, invocation));
        Child(log, invocation, new StreamReceived(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "think")));
        Child(log, invocation, new StreamReceived(new ChatTextDelta(0, 1, ChatRole.Assistant, "Look")));
        Child(log, invocation, new StreamReceived(new ChatTextDelta(0, 1, ChatRole.Assistant, "ing.")));
        Child(log, invocation, new ToolCallsIssued([JsonToolCall("k1", "read", """{"path":"a.cs"}""")]));
        Child(log, invocation, new ToolResultsReceived([new ToolResult("k1", "body", ToolResultStatus.Failure)]));
        Child(log, invocation, new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "Found it.")));
        log.OnEvent(new ChildSessionChanged(
            running with
            {
                Status = "completed",
                ModelCalls = 2,
                ToolCalls = 1,
                CumulativeUsage = new SessionUsageDocument { InputTokenCount = 40, OutputTokenCount = 7 }
            },
            invocation));

        var events = Read(output);
        Assert.Equal(
            ["child_status", "child_reasoning", "child_text", "child_tool_use", "child_text", "child_status"],
            events.Select(static item => item.GetProperty("type").GetString()));
        Assert.All(events, item =>
        {
            Assert.Equal(SessionId, item.GetProperty("sessionID").GetString());
            Assert.Equal("child-1", item.GetProperty("childSessionID").GetString());
            Assert.Equal(invocation.ToString(), item.GetProperty("invocationID").GetString());
        });
        Assert.Equal("Looking.", events[2].GetProperty("text").GetString());
        var tool = events[3];
        Assert.Equal("k1", tool.GetProperty("callId").GetString());
        Assert.Equal("read", tool.GetProperty("name").GetString());
        Assert.Equal("a.cs", tool.GetProperty("arguments").GetProperty("path").GetString());
        Assert.Equal("failure", tool.GetProperty("status").GetString());
        Assert.Equal("Found it.", events[4].GetProperty("text").GetString());
        var settled = events[5];
        Assert.Equal("completed", settled.GetProperty("status").GetString());
        Assert.Equal(2, settled.GetProperty("modelCalls").GetInt32());
        Assert.Equal(40, settled.GetProperty("usage").GetProperty("inputTokens").GetInt64());
        Assert.Equal(7, settled.GetProperty("usage").GetProperty("outputTokens").GetInt64());
        Assert.False(events[0].TryGetProperty("usage", out _));
    }

    [Fact]
    public void ChildReasoning_IsOmittedUnlessRequested()
    {
        var output = new StringWriter();
        var log = Bound(output);
        var invocation = Guid.NewGuid();
        Child(log, invocation, new StreamReceived(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "trace-not-for-ci")));
        Child(log, invocation, new ModelRoundClosed());

        Assert.Empty(Read(output));
    }

    [Fact]
    public void Epilogue_ClosesAStepThatFaultedBeforeItFinished()
    {
        var output = new StringWriter();
        var log = Bound(output);
        log.OnEvent(new TurnStarted());
        log.OnEvent(new StreamReceived(new ChatTextDelta(0, 0, ChatRole.Assistant, "partial")));
        log.OnEvent(new ErrorWritten("boom"));
        log.WriteEpilogue("failed", "Session saved  parent-session");

        var events = Read(output);
        Assert.Equal(
            ["step_start", "text", "error", "step_finish", "stopped", "session"],
            events.Select(static item => item.GetProperty("type").GetString()));
        Assert.Equal("failed", events[3].GetProperty("reason").GetString());
        Assert.False(events[3].TryGetProperty("modelCalls", out _));
        Assert.Equal("failed", events[4].GetProperty("status").GetString());
        Assert.Null(log.StopReason);
    }

    private static RunJsonLog Bound(StringWriter output)
    {
        var log = new RunJsonLog(output, showThinking: false);
        log.BindSession(SessionId);
        return log;
    }

    private static void Child(RunJsonLog log, Guid invocation, SessionEvent value) =>
        log.OnEvent(new ChildEventReceived("child-1", invocation, value));

    private static JsonElement Single(StringWriter output, string type) =>
        Assert.Single(Read(output), item => item.GetProperty("type").GetString() == type);

    private static List<JsonElement> Read(StringWriter output)
    {
        var events = new List<JsonElement>();
        foreach (var line in output.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var document = JsonDocument.Parse(line);
            events.Add(document.RootElement.Clone());
        }

        return events;
    }
}
