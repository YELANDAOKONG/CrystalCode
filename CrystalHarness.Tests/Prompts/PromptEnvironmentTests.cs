using CrystalHarness.Prompts;
using CrystalHarness.Tests.Tools;

using Xunit;

namespace CrystalHarness.Tests.Prompts;

public sealed class PromptEnvironmentTests
{
    [Fact]
    public void Render_IncludesWorkspaceGitPlatformDateAndModel()
    {
        using var workspace = new TemporaryWorkspace();
        var now = new DateTimeOffset(2026, 8, 31, 12, 0, 0, TimeSpan.Zero);

        var text = PromptEnvironment.Render(
            workspace.Path,
            "deepseek",
            "deepseek-v4-flash",
            now);

        Assert.Contains("<env>", text, StringComparison.Ordinal);
        Assert.Contains(Path.GetFullPath(workspace.Path), text, StringComparison.Ordinal);
        Assert.Contains("Is git repo: no", text, StringComparison.Ordinal);
        Assert.Contains("Today's date: Monday Aug 31, 2026", text, StringComparison.Ordinal);
        Assert.Contains("Model: deepseek / deepseek-v4-flash", text, StringComparison.Ordinal);
        Assert.True(
            text.Contains("Platform: linux", StringComparison.Ordinal)
            || text.Contains("Platform: windows", StringComparison.Ordinal)
            || text.Contains("Platform: osx", StringComparison.Ordinal));
    }

    [Fact]
    public void Render_DetectsGitDirectory()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));

        var text = PromptEnvironment.Render(workspace.Path, "openai", "gpt-4.1");

        Assert.Contains("Is git repo: yes", text, StringComparison.Ordinal);
    }
}
