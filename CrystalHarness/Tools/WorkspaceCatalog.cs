using Crystal.Tools;

using CrystalHarness.Plugins;

namespace CrystalHarness.Tools;

/// <summary>
/// Builds Plan and Work tool catalogs for one workspace session.
/// </summary>
public static class WorkspaceCatalog
{
    public static ToolCatalog CreatePlan(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            (registry ?? PluginRegistry.CreateBuiltIn())
                .CreateTools(workspace, todos, prompt, plan: true));
    }

    public static ToolCatalog CreateWork(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            (registry ?? PluginRegistry.CreateBuiltIn())
                .CreateTools(workspace, todos, prompt, plan: false));
    }
}
