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

    [Fact]
    public void Resolve_SelectedPartialSetFallsBackToBuiltInPrompts()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(
            Path.Combine(home.Home.PromptSetsDirectory, "concise"),
            "work.md",
            "concise work");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, "concise");

        Assert.Equal("concise", resolution.PromptSet);
        Assert.Equal("concise work", resolution.Prompts.Work);
        Assert.Equal(PlanPrompt.Text, resolution.Prompts.Plan);
        Assert.Equal(ApprovalReviewPrompt.SystemText, resolution.Prompts.Review);
        Assert.Equal(PromptSource.PromptSet, resolution.WorkSource);
        Assert.Equal(PromptSource.BuiltIn, resolution.PlanSource);
        Assert.Equal(PromptSource.BuiltIn, resolution.ReviewSource);
        Assert.Equal(["concise"], resolution.AvailableSets);
    }

    [Fact]
    public void Resolve_DirectOverridesWinOverSelectedSet()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(
            Path.Combine(home.Home.PromptSetsDirectory, "concise"),
            "work.md",
            "set work");
        WritePrompt(home.Home.PromptsDirectory, "work.md", "home work");
        WritePrompt(
            Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName, "prompts"),
            "work.md",
            "project work");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, "concise");

        Assert.Equal("project work", resolution.Prompts.Work);
        Assert.Equal(PromptSource.ProjectOverride, resolution.WorkSource);
    }

    [Fact]
    public void Resolve_HomeOverrideWinsWhenProjectOverrideIsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(
            Path.Combine(home.Home.PromptSetsDirectory, "concise"),
            "plan.md",
            "set plan");
        WritePrompt(home.Home.PromptsDirectory, "plan.md", "home plan");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, "concise");

        Assert.Equal("home plan", resolution.Prompts.Plan);
        Assert.Equal(PromptSource.HomeOverride, resolution.PlanSource);
    }

    [Fact]
    public void Resolve_DoesNotDiscoverProjectPromptSets()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(
            Path.Combine(workspace.Path, ".crystal", "promptsets", "project-only"),
            "work.md",
            "project set work");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, "project-only");

        Assert.Equal(PromptSetNames.Default, resolution.PromptSet);
        Assert.Empty(resolution.AvailableSets);
        Assert.Equal(WorkPrompt.Text, resolution.Prompts.Work);
        Assert.Contains(
            resolution.Notes,
            note => note.Contains("was not found", StringComparison.Ordinal));
    }

    [Fact]
    public void Resolve_SkipsEmptyAndInvalidPromptSets()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        WritePrompt(Path.Combine(home.Home.PromptSetsDirectory, "empty"), "work.md", "   ");
        WritePrompt(Path.Combine(home.Home.PromptSetsDirectory, "Bad_Name"), "work.md", "bad");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, PromptSetNames.Default);

        Assert.Empty(resolution.AvailableSets);
        Assert.Equal(2, resolution.Notes.Count);
    }

    [Fact]
    public void Resolve_PromptSetAcceptsTxtButPrefersMd()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var directory = Path.Combine(home.Home.PromptSetsDirectory, "concise");
        WritePrompt(directory, "work.txt", "text work");
        WritePrompt(directory, "work.md", "markdown work");
        WritePrompt(directory, "plan.txt", "text plan");
        var store = CreateStore(home);

        var resolution = store.Resolve(workspace.Path, "concise");

        Assert.Equal("markdown work", resolution.Prompts.Work);
        Assert.Equal("text plan", resolution.Prompts.Plan);
    }

    private static PromptStore CreateStore(TemporaryHome home) =>
        new(home.Home, InstructionDiscovery.Isolated(home.Home));

    private static void WritePrompt(string directory, string fileName, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text);
    }
}
