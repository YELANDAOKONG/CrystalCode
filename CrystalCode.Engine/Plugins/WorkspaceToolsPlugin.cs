using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins;

/// <summary>
/// Built-in Plan/Work tools registered on the plugin table.
/// </summary>
public sealed class WorkspaceToolsPlugin : IPlugin
{
    public string Name => "workspace";

    public PluginContribution Contribute() =>
        new(
            tools:
            [
                new FactoryToolContribution(
                    ReadTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (workspace, _, _) => new ReadTool(workspace)),
                new FactoryToolContribution(
                    GlobTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (workspace, _, _) => new GlobTool(workspace)),
                new FactoryToolContribution(
                    GrepTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (workspace, _, _) => new GrepTool(workspace)),
                new FactoryToolContribution(
                    TodoWriteTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (_, todos, _) => new TodoWriteTool(todos)),
                new FactoryToolContribution(
                    TodoReadTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (_, todos, _) => new TodoReadTool(todos)),
                new FactoryToolContribution(
                    QuestionTool.ToolName,
                    HostToolCatalogs.PlanAndWork,
                    (_, _, prompt) => new QuestionTool(prompt)),
                new FactoryToolContribution(
                    EditTool.ToolName,
                    HostToolCatalogs.Work,
                    (workspace, _, _) => new EditTool(workspace)),
                new FactoryToolContribution(
                    WriteTool.ToolName,
                    HostToolCatalogs.Work,
                    (workspace, _, _) => new WriteTool(workspace)),
                new FactoryToolContribution(
                    BashTool.ToolName,
                    HostToolCatalogs.Work,
                    (workspace, _, _) => new BashTool(workspace))
            ]);
}
