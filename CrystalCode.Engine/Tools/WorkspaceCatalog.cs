using Crystal.Tools;
using CrystalCode.Engine.Plugins;
using CrystalCode.Engine.Plugins.Disk;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Tools;

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
        ExternalCatalog? external = null,
        PluginCatalog? disk = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        return new ToolCatalog(
            CreateTools(workspace, todos, prompt, registry, skills, external, plan: true, disk: disk));
    }

    public static ToolCatalog CreateWork(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry = null,
        SkillCatalog? skills = null,
        ExternalCatalog? external = null,
        int? bashTimeoutSeconds = WorkspaceLimits.BashTimeoutSeconds,
        PluginCatalog? disk = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(todos);
        ArgumentNullException.ThrowIfNull(prompt);
        if (!BashTool.IsSupported(bashTimeoutSeconds))
        {
            throw new ArgumentOutOfRangeException(
                nameof(bashTimeoutSeconds),
                bashTimeoutSeconds,
                "Bash timeout must be a positive supported number of seconds, or unlimited.");
        }

        return new ToolCatalog(
            CreateTools(
                workspace,
                todos,
                prompt,
                registry,
                skills,
                external,
                plan: false,
                bashTimeoutSeconds,
                disk));
    }

    private static IReadOnlyList<ITool> CreateTools(
        Workspace workspace,
        TodoList todos,
        IUserPrompt prompt,
        PluginRegistry? registry,
        SkillCatalog? skills,
        ExternalCatalog? external,
        bool plan,
        int? bashTimeoutSeconds = WorkspaceLimits.BashTimeoutSeconds,
        PluginCatalog? disk = null)
    {
        var tools = new List<ITool>(
            (registry ?? PluginRegistry.CreateBuiltIn())
                .CreateTools(workspace, todos, prompt, plan));
        if (!plan)
        {
            ApplyBashTimeout(tools, workspace, bashTimeoutSeconds);
        }

        if (disk is not null)
        {
            tools.AddRange(plan ? disk.PlanTools : disk.WorkTools);
        }

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

    private static void ApplyBashTimeout(
        List<ITool> tools,
        Workspace workspace,
        int? timeoutSeconds)
    {
        for (var index = 0; index < tools.Count; index++)
        {
            if (tools[index] is not BashTool)
            {
                continue;
            }

            tools[index] = new BashTool(workspace, timeoutSeconds);
            return;
        }
    }
}
