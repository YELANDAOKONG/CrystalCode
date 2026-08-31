using Crystal.Tools;
using CrystalCode.Plugins;
using CrystalCode.Skills;

namespace CrystalCode.Tools;

/// <summary>
/// Builds Plan and Work tool catalogs for one workspace session.
/// </summary>
public static class WorkspaceCatalog
{
    public static ToolCatalog CreatePlan(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null,
        SkillCatalog? skills = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            CreateTools(workspace, todos, prompt, registry, skills, plan: true));
    }

    public static ToolCatalog CreateWork(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null,
        SkillCatalog? skills = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            CreateTools(workspace, todos, prompt, registry, skills, plan: false));
    }

    private static IReadOnlyList<ITool> CreateTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry,
        SkillCatalog? skills,
        bool plan)
    {
        var tools = new List<ITool>(
            (registry ?? PluginRegistry.CreateBuiltIn())
                .CreateTools(workspace, todos, prompt, plan));
        if (skills is not null)
        {
            tools.Add(new SkillTool(skills));
        }

        return tools;
    }
}
