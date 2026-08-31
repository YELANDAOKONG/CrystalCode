using CrystalHarness.Plugins.Interfaces;
using CrystalHarness.Tools;

namespace CrystalHarness.Plugins;

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
                    true,
                    (workspace, _, _) => new ReadTool(workspace)),
                new FactoryToolContribution(
                    GlobTool.ToolName,
                    true,
                    (workspace, _, _) => new GlobTool(workspace)),
                new FactoryToolContribution(
                    GrepTool.ToolName,
                    true,
                    (workspace, _, _) => new GrepTool(workspace)),
                new FactoryToolContribution(
                    TodoWriteTool.ToolName,
                    true,
                    (_, todos, _) => new TodoWriteTool(todos)),
                new FactoryToolContribution(
                    QuestionTool.ToolName,
                    true,
                    (_, _, prompt) => new QuestionTool(prompt)),
                new FactoryToolContribution(
                    EditTool.ToolName,
                    false,
                    (workspace, _, _) => new EditTool(workspace)),
                new FactoryToolContribution(
                    WriteTool.ToolName,
                    false,
                    (workspace, _, _) => new WriteTool(workspace)),
                new FactoryToolContribution(
                    BashTool.ToolName,
                    false,
                    (workspace, _, _) => new BashTool(workspace))
            ]);
}
