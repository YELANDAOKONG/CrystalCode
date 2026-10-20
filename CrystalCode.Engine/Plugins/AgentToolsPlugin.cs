using Crystal.Tools;

using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Plugins;

internal sealed class AgentToolsPlugin(TaskTool task) : IPlugin
{
    public string Name => "agents";

    public PluginContribution Contribute() => new(tools:
    [new FactoryToolContribution(TaskTool.ToolName, HostToolCatalogs.PlanAndWork, (_, _, _) => task)]);
}
