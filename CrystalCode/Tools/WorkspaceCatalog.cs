using Crystal.Tools;
using CrystalCode.Plugins;
using CrystalCode.Skills;
using CrystalCode.Tools.External;

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
        SkillCatalog? skills = null,
        ExternalCatalog? external = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            CreateTools(workspace, todos, prompt, registry, skills, external, plan: true));
    }

    public static ToolCatalog CreateWork(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null,
        SkillCatalog? skills = null,
        ExternalCatalog? external = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            CreateTools(workspace, todos, prompt, registry, skills, external, plan: false));
    }

    private static IReadOnlyList<ITool> CreateTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry,
        SkillCatalog? skills,
        ExternalCatalog? external,
        bool plan)
    {
        var tools = new List<ITool>(
            (registry ?? PluginRegistry.CreateBuiltIn())
                .CreateTools(workspace, todos, prompt, plan));
        if (external is not null)
        {
            tools.AddRange(plan ? external.PlanTools : external.WorkTools);
        }

        if (skills is not null)
        {
            tools.Add(new SkillTool(skills));
        }

        return tools;
    }
}
