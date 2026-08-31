using CrystalHarness.Tools;

using Xunit;

namespace CrystalHarness.Tests.Tools;

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
        Assert.NotNull(catalog.Find(TodoWriteTool.ToolName));
        Assert.NotNull(catalog.Find(QuestionTool.ToolName));
        Assert.Null(catalog.Find(EditTool.ToolName));
        Assert.Null(catalog.Find(WriteTool.ToolName));
        Assert.Null(catalog.Find(BashTool.ToolName));
    }

    [Fact]
    public void CreateWork_IncludesEditWriteAndBash()
    {
        using var root = new TemporaryWorkspace();
        var catalog = WorkspaceCatalog.CreateWork(
            new Workspace(root.Path),
            new TodoList(),
            new FixedUserPrompt("ok"));

        Assert.NotNull(catalog.Find(EditTool.ToolName));
        Assert.NotNull(catalog.Find(WriteTool.ToolName));
        Assert.NotNull(catalog.Find(BashTool.ToolName));
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
    }
}
