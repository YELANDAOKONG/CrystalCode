using CrystalCode.Prompts;
using CrystalCode.Tests.Home;
using CrystalCode.Tests.Tools;

using Xunit;

namespace CrystalCode.Tests.Prompts;

public sealed class PromptStoreTests
{
    [Fact]
    public void Load_UsesBuiltInTextWhenNoFilesExist()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var store = CreateStore(home);

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
        var store = CreateStore(home);

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
        var store = CreateStore(home);

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
        var store = CreateStore(home);

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
        File.WriteAllText(Path.Combine(workspace.Path, ".crystal.md"), "this repo is CrystalCode");
        var store = CreateStore(home);

        var prompts = store.Load(workspace.Path);

        Assert.Contains("prefer tests", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("use the workspace tools", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("this repo is CrystalCode", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("## Workspace instructions", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.DoesNotContain("Workspace instructions", prompts.Review, StringComparison.Ordinal);
        Assert.Equal(ApprovalReviewPrompt.SystemText, prompts.Review);
    }

    [Fact]
    public void Load_AppendsAgentsFileWithoutReplacingWorkPrompt()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "run tests with dotnet test");
        var store = CreateStore(home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal(WorkPrompt.Text, prompts.Work);
        Assert.Contains("run tests with dotnet test", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Contains("Instructions from:", prompts.WorkSystem, StringComparison.Ordinal);
        Assert.Equal(ApprovalReviewPrompt.SystemText, prompts.Review);
    }

    [Fact]
    public void Load_UsesClaudeWhenAgentsIsAbsent()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Claude), "follow the existing CLAUDE notes");
        var store = CreateStore(home);

        var prompts = store.Load(workspace.Path);

        Assert.Equal(WorkPrompt.Text, prompts.Work);
        Assert.Contains("follow the existing CLAUDE notes", prompts.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_PrefersAgentsOverClaudeInTheSameDirectory()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "agents rules");
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Claude), "claude rules");
        var store = CreateStore(home);

        var prompts = store.Load(workspace.Path);

        Assert.Contains("agents rules", prompts.Instructions, StringComparison.Ordinal);
        Assert.DoesNotContain("claude rules", prompts.Instructions, StringComparison.Ordinal);
    }

    [Fact]
    public void Load_CombinesNestedAgentsFilesUpToGitRoot()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Directory.CreateDirectory(Path.Combine(workspace.Path, ".git"));
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "root agents");
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);
        File.WriteAllText(Path.Combine(nested, InstructionNames.Agents), "nested agents");
        var store = CreateStore(home);

        var prompts = store.Load(nested);

        Assert.Contains("nested agents", prompts.Instructions, StringComparison.Ordinal);
        Assert.Contains("root agents", prompts.Instructions, StringComparison.Ordinal);
        Assert.Equal(WorkPrompt.Text, prompts.Work);
    }

    [Fact]
    public void Load_DoesNotWalkPastWorkspaceWhenGitIsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        File.WriteAllText(Path.Combine(workspace.Path, InstructionNames.Agents), "parent agents");
        var nested = Path.Combine(workspace.Path, "src");
        Directory.CreateDirectory(nested);
        var store = CreateStore(home);

        var prompts = store.Load(nested);

        Assert.DoesNotContain("parent agents", prompts.Instructions, StringComparison.Ordinal);
    }

    private static PromptStore CreateStore(TemporaryHome home) =>
        new(home.Home, InstructionDiscovery.Isolated(home.Home));

    private static void WritePrompt(string directory, string fileName, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text);
    }
}
