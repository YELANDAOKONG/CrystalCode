using Spectre.Console;

using CrystalCode.Display.Paint;
using CrystalCode.Display.Transcript;

using Xunit;

namespace CrystalCode.Display.Tests.Transcript;

public sealed class TranscriptLogVerboseTests
{
    [Fact]
    public void BuildLines_OmitsReadResultWhenToolVerboseIsOff()
    {
        var log = new TranscriptLog { VerboseTools = false };
        log.Add(TranscriptKind.Tool, "Read  note.txt");
        log.Add(TranscriptKind.Result, "hello", toolName: "read");

        var text = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("Read", text, StringComparison.Ordinal);
        Assert.Contains("note.txt", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Result", text, StringComparison.Ordinal);
        Assert.DoesNotContain("hello", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_CompactsBashResultWhenCommandVerboseIsOff()
    {
        var log = new TranscriptLog { VerboseCommands = false };
        log.Add(TranscriptKind.Tool, "Bash  dotnet test");
        log.Add(
            TranscriptKind.Result,
            "exit 0\nline1\nline2\npassed",
            toolName: "bash");

        var text = string.Join('\n', log.BuildLines(80).Select(line => line.Plain));

        Assert.Contains("Bash", text, StringComparison.Ordinal);
        Assert.Contains("2 output lines hidden", text, StringComparison.Ordinal);
        Assert.Contains("passed", text, StringComparison.Ordinal);
        Assert.DoesNotContain("line1", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_KeepsEditResultWhenToolVerboseIsOff()
    {
        var log = new TranscriptLog { VerboseTools = false };
        log.Add(TranscriptKind.Tool, "Edit  src/App.cs");
        log.Add(TranscriptKind.Result, "Edited src/App.cs.", toolName: "edit");

        var text = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("Edited src/App.cs.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_OmitsApprovalCardWhenApprovalVerboseIsOff()
    {
        var log = new TranscriptLog { VerboseApprovals = false };
        log.Add(TranscriptKind.Tool, "Glob  *.cs");
        log.Add(TranscriptKind.Approval, string.Empty, new Markup("Status Allowed"));

        var text = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("Glob", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Allowed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_RestoresApprovalCardWhenApprovalVerboseTurnsOn()
    {
        var log = new TranscriptLog();
        log.Add(TranscriptKind.Approval, string.Empty, new Markup("Status Allowed"));
        log.BuildLines(60);
        log.VerboseApprovals = false;
        log.VerboseApprovals = true;

        var text = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("Allowed", text, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_OmitsThinkingWhileTextStaysAvailable()
    {
        var log = new TranscriptLog { VerboseThinking = false };
        log.AppendLive(TranscriptKind.Thinking, "hidden thought");

        var hidden = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.DoesNotContain("hidden thought", hidden, StringComparison.Ordinal);
        Assert.DoesNotContain("Thinking", hidden, StringComparison.Ordinal);

        log.CommitLive();
        log.VerboseThinking = true;
        var shown = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("Thinking", shown, StringComparison.Ordinal);
        Assert.Contains("hidden thought", shown, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildLines_HidesLiveThinkingUntilTheSwitchTurnsOn()
    {
        var log = new TranscriptLog();
        log.AppendLive(TranscriptKind.Thinking, "live thought");
        log.VerboseThinking = false;

        var hidden = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.DoesNotContain("live thought", hidden, StringComparison.Ordinal);

        log.AppendLive(TranscriptKind.Thinking, " more");
        log.VerboseThinking = true;
        var shown = string.Join('\n', log.BuildLines(60).Select(line => line.Plain));

        Assert.Contains("live thought more", shown, StringComparison.Ordinal);
    }
}
