using CrystalCode.Engine.Prompts;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tools;

using Xunit;

namespace CrystalCode.Engine.Tests.Tools;

public sealed class WorkspaceCatalogTests
{
    [Fact]
    public void CreatePlan_OmitsSideEffectTools()
    {
        using var root = new TemporaryWorkspace();
        var catalog = WorkspaceCatalog.CreatePlan(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"));

        Assert.NotNull(catalog.Find(ReadTool.ToolName));
        Assert.NotNull(catalog.Find(GlobTool.ToolName));
        Assert.NotNull(catalog.Find(GrepTool.ToolName));
        Assert.NotNull(catalog.Find(SearchTool.ToolName));
        Assert.NotNull(catalog.Find(ListTool.ToolName));
        Assert.NotNull(catalog.Find(TreeTool.ToolName));
        Assert.NotNull(catalog.Find(TodoWriteTool.ToolName));
        Assert.NotNull(catalog.Find(TodoReadTool.ToolName));
        Assert.NotNull(catalog.Find(QuestionTool.ToolName));
        Assert.Null(catalog.Find(EditTool.ToolName));
        Assert.Null(catalog.Find(WriteTool.ToolName));
        Assert.Null(catalog.Find(BashTool.ToolName));
        Assert.Null(catalog.Find(SkillTool.ToolName));
    }

    [Fact]
    public void CreateWork_IncludesEditWriteAndBash()
    {
        using var root = new TemporaryWorkspace();
        var catalog = WorkspaceCatalog.CreateWork(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            backgroundTasks: new BackgroundTaskRegistry(new Workspace(root.Path)));

        Assert.NotNull(catalog.Find(EditTool.ToolName));
        Assert.NotNull(catalog.Find(WriteTool.ToolName));
        Assert.NotNull(catalog.Find(BashTool.ToolName));
        Assert.NotNull(catalog.Find(BackgroundBashStartTool.ToolName));
        Assert.NotNull(catalog.Find(BackgroundBashStatusTool.ToolName));
        Assert.NotNull(catalog.Find(BackgroundBashLogsTool.ToolName));
        Assert.NotNull(catalog.Find(BackgroundBashStopTool.ToolName));
        Assert.NotNull(catalog.Find(ListTool.ToolName));
        Assert.NotNull(catalog.Find(TreeTool.ToolName));
        Assert.NotNull(catalog.Find(SearchTool.ToolName));
        Assert.Contains(
            "600 second timeout",
            catalog.Find(BashTool.ToolName)!.Definition.Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not use it to read, write, or search files",
            catalog.Find(BashTool.ToolName)!.Definition.Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "when you are uncertain",
            catalog.Find(QuestionTool.ToolName)!.Definition.Description,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "before multi-step work",
            catalog.Find(TodoWriteTool.ToolName)!.Definition.Description,
            StringComparison.Ordinal);
        Assert.Contains(
            "without changing the list",
            catalog.Find(TodoReadTool.ToolName)!.Definition.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlanAndWork_IncludeSkillWhenCatalogIsSupplied()
    {
        using var root = new TemporaryWorkspace();
        var skills = SkillCatalog.Empty;
        var plan = WorkspaceCatalog.CreatePlan(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            skills: skills);
        var work = WorkspaceCatalog.CreateWork(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            skills: skills);

        Assert.NotNull(plan.Find(SkillTool.ToolName));
        Assert.NotNull(work.Find(SkillTool.ToolName));
    }

    [Fact]
    public void CreateWork_UsesConfiguredBashTimeout()
    {
        using var root = new TemporaryWorkspace();
        var catalog = WorkspaceCatalog.CreateWork(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"),
            bashTimeoutSeconds: null);

        Assert.Contains(
            "no per-command timeout",
            catalog.Find(BashTool.ToolName)!.Definition.Description,
            StringComparison.Ordinal);
    }

    [Fact]
    public void CreatePlanAndWork_IncludeDescribeImageOnlyWhenConfigured()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        var todos = new TodoList();
        var prompt = new FixedUserPrompt("ok");
        var without = WorkspaceCatalog.CreatePlan(workspace, todos, prompt);
        var workWithout = WorkspaceCatalog.CreateWork(workspace, todos, prompt);

        Assert.Null(without.Find(DescribeImageTool.ToolName));
        Assert.Null(workWithout.Find(DescribeImageTool.ToolName));

        var describer = new ImageDescriber(
            () => throw new InvalidOperationException("The image client is not used by catalog construction."),
            ImageDescriptionPrompt.SystemText,
            reasoning: null);
        var plan = WorkspaceCatalog.CreatePlan(
            workspace,
            todos,
            prompt,
            imageDescriber: describer);
        var work = WorkspaceCatalog.CreateWork(
            workspace,
            todos,
            prompt,
            imageDescriber: describer);

        Assert.NotNull(plan.Find(DescribeImageTool.ToolName));
        Assert.NotNull(work.Find(DescribeImageTool.ToolName));
    }

    [Fact]
    public void CreatePlanAndWork_OmitNamedBuiltInTools()
    {
        using var root = new TemporaryWorkspace();
        var workspace = new Workspace(root.Path);
        var todos = new TodoList();
        var prompt = new FixedUserPrompt("ok");
        var describer = new ImageDescriber(
            () => throw new InvalidOperationException("The image client is not used by catalog construction."),
            ImageDescriptionPrompt.SystemText,
            reasoning: null);
        var disabled = new[] { " read ", "bash", "bash_start", "describe_image", "skill", "read" };
        var plan = WorkspaceCatalog.CreatePlan(
            workspace,
            todos,
            prompt,
            skills: SkillCatalog.Empty,
            imageDescriber: describer,
            disabledBuiltInTools: disabled);
        var work = WorkspaceCatalog.CreateWork(
            workspace,
            todos,
            prompt,
            skills: SkillCatalog.Empty,
            imageDescriber: describer,
            backgroundTasks: new BackgroundTaskRegistry(workspace),
            disabledBuiltInTools: disabled);

        Assert.Null(plan.Find(ReadTool.ToolName));
        Assert.Null(plan.Find(SkillTool.ToolName));
        Assert.Null(plan.Find(DescribeImageTool.ToolName));
        Assert.NotNull(plan.Find(QuestionTool.ToolName));
        Assert.Null(work.Find(ReadTool.ToolName));
        Assert.Null(work.Find(BashTool.ToolName));
        Assert.Null(work.Find(BackgroundBashStartTool.ToolName));
        Assert.Null(work.Find(SkillTool.ToolName));
        Assert.Null(work.Find(DescribeImageTool.ToolName));
        Assert.NotNull(work.Find(EditTool.ToolName));
        Assert.NotNull(work.Find(BackgroundBashStatusTool.ToolName));
        Assert.NotNull(work.Find(BackgroundBashLogsTool.ToolName));
        Assert.NotNull(work.Find(BackgroundBashStopTool.ToolName));
    }
}
