using Crystal.Chat;
using Crystal.Reasoning;
using Crystal.Tools;

using CrystalCode.Engine.Events;
using CrystalCode.Run;

using Xunit;

namespace CrystalCode.Tests.Run;

public sealed class RunLogTests
{
    [Fact]
    public void OnEvent_PrintsTheReplyThenEachToolWithItsResult()
    {
        var output = new StringWriter();
        var log = new RunLog(output, showThinking: true);
        log.OnEvent(new StreamReceived(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "trace")));
        log.OnEvent(new StreamReceived(
            new ChatTextDelta(0, 0, ChatRole.Assistant, "Looking.")));
        log.OnEvent(new ToolCallsIssued(
        [
            new ToolCall("1", "read", """{"path":"a.cs"}"""),
            new ToolCall("2", "read", """{"path":"b.cs"}""")
        ]));
        log.OnEvent(new ToolResultsReceived(
        [
            new ToolResult("2", "missing", ToolResultStatus.Failure),
            new ToolResult("1", "one\ntwo", ToolResultStatus.Success)
        ]));

        var text = output.ToString().Replace("\r\n", "\n", StringComparison.Ordinal);
        var thinking = text.IndexOf("[Thinking]", StringComparison.Ordinal);
        var reply = text.IndexOf("Looking.", StringComparison.Ordinal);
        var missing = text.IndexOf("[Read] b.cs", StringComparison.Ordinal);
        var found = text.IndexOf("[Read] a.cs", StringComparison.Ordinal);
        Assert.True(thinking >= 0 && thinking < reply && reply < missing && missing < found);
        Assert.Contains("[Thinking]\ntrace\n\nLooking.\n\n[Read] b.cs\n  missing", text, StringComparison.Ordinal);
        Assert.Contains("[Read] a.cs\n  one\n  two", text, StringComparison.Ordinal);
        Assert.Contains("trace", text, StringComparison.Ordinal);
        Assert.DoesNotContain("\nResult", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnEvent_OmitsThinkingUnlessRequested()
    {
        var output = new StringWriter();
        var log = new RunLog(output, showThinking: false);
        log.OnEvent(new StreamReceived(
            new ChatReasoningTextDelta(0, 0, 0, ReasoningTextKind.Trace, "trace-not-for-ci")));
        log.OnEvent(new StreamReceived(
            new ChatTextDelta(0, 0, ChatRole.Assistant, "Hello there.")));
        log.OnEvent(new ToolCallsIssued([]));

        var text = output.ToString();
        Assert.Contains("Hello there.", text, StringComparison.Ordinal);
        Assert.DoesNotContain("trace-not-for-ci", text, StringComparison.Ordinal);
        Assert.DoesNotContain("[Thinking]", text, StringComparison.Ordinal);
    }

    [Fact]
    public void OnEvent_ShortensALongReadUnderItsCall()
    {
        var output = new StringWriter();
        var log = new RunLog(output, showThinking: false);
        var lines = new string[14];
        for (var index = 0; index < lines.Length; index++)
        {
            lines[index] = "L" + index.ToString("00");
        }

        log.OnEvent(new ToolCallsIssued(
        [
            new ToolCall("1", "read", """{"path":"notes.txt"}""")
        ]));
        log.OnEvent(new ToolResultsReceived(
        [
            new ToolResult("1", string.Join('\n', lines), ToolResultStatus.Success)
        ]));

        var text = output.ToString();
        Assert.Contains("[Read] notes.txt", text, StringComparison.Ordinal);
        Assert.Contains("L00", text, StringComparison.Ordinal);
        Assert.DoesNotContain("L13", text, StringComparison.Ordinal);
        Assert.Contains("<2 lines omitted>", text, StringComparison.Ordinal);
    }
}
