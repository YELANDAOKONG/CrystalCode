using CrystalCode.Sessions;

using Xunit;

namespace CrystalCode.Tests.Sessions;

public sealed class ResumeHintTests
{
    [Fact]
    public void ForSaved_IncludesIdAndResumeCommand()
    {
        var text = ResumeHint.ForSaved("abc123");

        Assert.Contains("Session saved  abc123", text, StringComparison.Ordinal);
        Assert.Contains("crystal --resume abc123", text, StringComparison.Ordinal);
        Assert.Contains("/resume", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ForWorkspace_ExplainsLatestAndId()
    {
        var text = ResumeHint.ForWorkspace();

        Assert.Contains("/resume", text, StringComparison.Ordinal);
        Assert.Contains("crystal --resume <id>", text, StringComparison.Ordinal);
    }
}
