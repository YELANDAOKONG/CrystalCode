using System.Runtime.InteropServices;

using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class PromptEnvironmentTests
{
    [Fact]
    public void Render_IncludesWorkspaceGitAndPlatformButNoModelClockOrSession()
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
        Assert.DoesNotContain("Git root:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Today's date", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Local time", text, StringComparison.Ordinal);
        Assert.Contains("OS: " + RuntimeInformation.OSDescription.Trim(), text, StringComparison.Ordinal);
        Assert.Contains(
            "Architecture: " + RuntimeInformation.OSArchitecture.ToString().ToLowerInvariant(),
            text,
            StringComparison.Ordinal);
        Assert.DoesNotContain("Model:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("deepseek", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Session:", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Approval:", text, StringComparison.Ordinal);
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
        Assert.Contains("Git root: " + Path.GetFullPath(workspace.Path), text, StringComparison.Ordinal);
    }

    [Fact]
    public void Render_ReportsAncestorGitRootWithoutMarkingTheWorkspaceAsARepo()
    {
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);

        var text = PromptEnvironment.Render(nested, "openai", "gpt-4.1");

        Assert.Contains("Is git repo: no", text, StringComparison.Ordinal);
        Assert.Contains("Git root: " + Path.GetFullPath(workspace.Path), text, StringComparison.Ordinal);
    }
}
