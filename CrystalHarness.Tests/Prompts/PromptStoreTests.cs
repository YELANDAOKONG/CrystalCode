using CrystalHarness.Prompts;
using CrystalHarness.Tests.Home;
using CrystalHarness.Tests.Tools;

using Xunit;

namespace CrystalHarness.Tests.Prompts;

public sealed class PromptStoreTests
{
    [Fact]
    public void Load_UsesBuiltInTextWhenNoFilesExist()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var store = new PromptStore(home.Home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal(WorkPrompt.Text, prompts.Work);
        Assert.Equal(PlanPrompt.Text, prompts.Plan);
        Assert.Equal(ApprovalReviewPrompt.SystemText, prompts.Review);
        Assert.Equal(string.Empty, prompts.Instructions);
        Assert.Equal(WorkPrompt.Text, prompts.WorkSystem);
    }

    [Fact]
    public void Load_HomePromptReplacesBuiltIn()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(home.Home.PromptsDirectory, "work.md", "home work");
        var store = new PromptStore(home.Home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal("home work", prompts.Work);
        Assert.Equal(PlanPrompt.Text, prompts.Plan);
        Assert.Equal("home work", prompts.WorkSystem);
    }

    [Fact]
    public void Load_ProjectPromptWinsOverHome()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(home.Home.PromptsDirectory, "work.md", "home work");
        WritePrompt(
            Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName, "prompts"),
            "work.md",
            "project work");
        var store = new PromptStore(home.Home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal("project work", prompts.Work);
        Assert.Equal(PlanPrompt.Text, prompts.Plan);
    }

    [Fact]
    public void Load_IgnoresEmptyPromptFile()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(home.Home.PromptsDirectory, "work.md", "home work");
        WritePrompt(
            Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName, "prompts"),
            "work.md",
            "   ");
        var store = new PromptStore(home.Home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal("home work", prompts.Work);
    }

    [Fact]
    public void Load_AppendsHomeAndProjectInstructions()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        home.Home.EnsureCreated();
        File.WriteAllText(home.Home.InstructionsPath, "prefer tests");
        Directory.CreateDirectory(Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName));
        File.WriteAllText(
            Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName, "instructions.md"),
            "use the workspace tools");
        File.WriteAllText(Path.Combine(workspace.Path, ".crystal.md"), "this repo is CrystalHarness");
        var store = new PromptStore(home.Home);

        var prompts = store.Load(workspace.Path);

        Assert.Contains("prefer tests", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("use the workspace tools", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("this repo is CrystalHarness", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("## Workspace instructions", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace instructions", prompts.Review, StringComparison.Ordinal);
        Assert.Equal(ApprovalReviewPrompt.SystemText, prompts.Review);
    }

    private static void WritePrompt(string directory, string fileName, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text);
    }
}
