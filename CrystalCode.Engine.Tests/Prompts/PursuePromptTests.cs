using CrystalCode.Engine.Home;
using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Tests.Home;
using CrystalCode.Engine.Tests.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Prompts;

public sealed class PursuePromptTests
{
    [Fact]
    public void Load_UsesBuiltInTextWhenNoFilesExist()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();

        var prompts = CreateStore(home).Load(workspace.Path);

        Assert.Equal(PursuePrompt.SystemText, prompts.PursueSystem);
        Assert.Equal(PursuePrompt.UserTemplate, prompts.PursueUser);
        Assert.Equal(PursuePrompt.ContinueTemplate, prompts.PursueContinue);
    }

    [Fact]
    public void Resolve_NamedFilesFollowSetThenHomeThenProject()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var set = Path.Combine(home.Home.PromptSetsDirectory, "strict");
        Write(set, "pursue.system.md", "set system");
        Write(set, "pursue.user.md", "set user");
        Write(set, "prompt.json", "{ \"enabled\": true }");
        Write(home.Home.PromptsDirectory, "pursue.user.md", "home user");
        Write(Path.Combine(workspace.Path, PromptStore.ProjectDirectoryName, "prompts"), "pursue.continue.md", "project continue");

        var prompts = CreateStore(home).Load(workspace.Path);

        Assert.Equal("set system", prompts.PursueSystem);
        Assert.Equal("home user", prompts.PursueUser);
        Assert.Equal("project continue", prompts.PursueContinue);
    }

    [Fact]
    public void Resolve_EmptyFileIsTreatedAsMissing()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        Write(home.Home.PromptsDirectory, "pursue.system.md", "   ");

        var prompts = CreateStore(home).Load(workspace.Path);

        Assert.Equal(PursuePrompt.SystemText, prompts.PursueSystem);
    }

    [Fact]
    public void Resolve_AttachmentsAppendToTheSystemTextOnly()
    {
        using var home = new TemporaryHome();
        using var workspace = new TemporaryWorkspace();
        var attachment = Path.Combine(home.Home.PromptAttachmentsDirectory, "extra");
        Write(attachment, "pursue.system.md", "EXTRA RULE");
        Write(attachment, "pursue.user.md", "IGNORED USER");
        Write(attachment, "prompt.json", "{ \"enabled\": true, \"order\": 0 }");

        var prompts = CreateStore(home).Load(workspace.Path);
        var context = PromptContext.InstructionsOnly(string.Empty);
        var system = prompts.ComposePursue(context);

        Assert.EndsWith("EXTRA RULE", system, StringComparison.Ordinal);
        Assert.Equal(PursuePrompt.UserTemplate, prompts.PursueUser);
        Assert.DoesNotContain("IGNORED USER", system, StringComparison.Ordinal);
    }

    [Fact]
    public void ComposePursue_BindsTheHostOwnedVerdictFormat()
    {
        var prompts = new PromptSet("work", "plan", "review", string.Empty);

        var system = prompts.ComposePursue(PromptContext.InstructionsOnly(string.Empty));

        Assert.Contains(PursuePrompt.VerdictFormat, system, StringComparison.Ordinal);
        Assert.DoesNotContain("{{verdict_format}}", system, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_BindsTheUserAndContinuationSlots()
    {
        var context = new PursuePromptContext(
            "Fix the build",
            "[1] hello",
            "work",
            Reason: "Tests were not run.",
            NextStep: "Run the tests.",
            RemainingContinuations: "3",
            Resumed: "yes");

        var user = PromptBinder.Apply(PursuePrompt.UserTemplate, new PromptBinding(Pursue: context));
        var next = PromptBinder.Apply(PursuePrompt.ContinueTemplate, new PromptBinding(Pursue: context));

        Assert.Contains("Fix the build", user, StringComparison.Ordinal);
        Assert.Contains("[1] hello", user, StringComparison.Ordinal);
        Assert.Contains("Run the tests.", next, StringComparison.Ordinal);
        Assert.Contains("remaining: 3", next, StringComparison.Ordinal);
        Assert.DoesNotContain("{{", next, StringComparison.Ordinal);
    }

    private static PromptStore CreateStore(TemporaryHome home) =>
        new(home.Home, InstructionDiscovery.Isolated(home.Home));

    private static void Write(string directory, string fileName, string text)
    {
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, fileName), text);
    }
}
