using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Classifies MCP tool calls. The floor is Write + Workspace, so every call
/// is reviewed. A tool is Read only when the operator trusts the server's
/// read-only declaration for its source.
/// </summary>
internal sealed class McpApprovalClassifier : IApprovalClassifier
{
    private readonly IReadOnlySet<string> _tools;
    private readonly IReadOnlySet<string> _readOnlyTools;

    public McpApprovalClassifier(IReadOnlySet<string> tools, IReadOnlySet<string> readOnlyTools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        ArgumentNullException.ThrowIfNull(readOnlyTools);
        _tools = tools;
        _readOnlyTools = readOnlyTools;
    }

    public bool TryClassify(
        ToolCall call,
        Workspace workspace,
        out ToolClassification classification)
    {
        ArgumentNullException.ThrowIfNull(call);
        classification = null!;
        if (!_tools.Contains(call.Name))
        {
            return false;
        }

        classification = _readOnlyTools.Contains(call.Name)
            ? new ToolClassification(Risk.Read, Authority.Workspace, "MCP read-only tool")
            : new ToolClassification(Risk.Write, Authority.Workspace, "MCP tool");
        return true;
    }
}
