using Crystal.Tools;

using CrystalCode.Engine.Tools;
using CrystalCode.Engine.Tools.External;

namespace CrystalCode.Engine.Modes;

/// <summary>
/// One operator-defined mode: a system prompt and the built-in tools it keeps.
/// </summary>
internal sealed record ModeDefinition(
    string Name,
    string Description,
    string Prompt,
    IReadOnlyList<string> Tools)
{
    private static readonly string[] WriteTools =
    [
        EditTool.ToolName,
        WriteTool.ToolName,
        BashTool.ToolName,
        BackgroundBashStartTool.ToolName,
        BackgroundBashStatusTool.ToolName,
        BackgroundBashLogsTool.ToolName,
        BackgroundBashStopTool.ToolName
    ];

    /// <summary>
    /// True when the mode lists a tool that can change files or run commands.
    /// Such a mode draws plugin, external, and MCP tools from the Work
    /// catalogs. Any other mode draws them from the Plan catalogs, so a
    /// Write + Workspace tool does not appear in a read-only mode.
    /// </summary>
    public bool HasWriteTools => Tools.Any(tool => WriteTools.Contains(tool, StringComparer.Ordinal));

    public bool Lists(string toolName) => Tools.Contains(toolName, StringComparer.Ordinal);

    /// <summary>
    /// Keeps every listed built-in and every tool that is not a built-in name.
    /// </summary>
    public IReadOnlyList<ITool> Select(ToolCatalog catalog)
    {
        ArgumentNullException.ThrowIfNull(catalog);
        var selected = new List<ITool>();
        foreach (var definition in catalog.Definitions)
        {
            if (ExternalToolNames.IsReserved(definition.Name) && !Lists(definition.Name))
            {
                continue;
            }

            selected.Add(catalog.Find(definition.Name)!);
        }

        return selected;
    }
}
