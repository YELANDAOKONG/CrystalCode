using Crystal.Tools;

namespace CrystalHarness.Tools;

/// <summary>
/// Builds Plan and Work tool catalogs for one workspace session.
/// </summary>
public static class WorkspaceCatalog
{
    public static ToolCatalog CreatePlan(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(PlanTools(workspace, todos, prompt));
    }

    public static ToolCatalog CreateWork(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
        [
            .. PlanTools(workspace, todos, prompt),
            new EditTool(workspace),
            new WriteTool(workspace),
            new BashTool(workspace)
        ]);
    }

    private static ITool[] PlanTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt) =>
    [
        new ReadTool(workspace),
        new GlobTool(workspace),
        new GrepTool(workspace),
        new TodoWriteTool(todos),
        new QuestionTool(prompt)
    ];
}
