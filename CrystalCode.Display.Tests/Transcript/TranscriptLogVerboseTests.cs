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
}
