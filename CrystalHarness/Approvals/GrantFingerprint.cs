using Crystal.Tools;

using CrystalHarness.Tools;

namespace CrystalHarness.Approvals;

internal static class GrantFingerprint
{
    public static string Create(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Name switch
        {
            WriteTool.ToolName when WriteTool.TryRead(call.Arguments, out var path, out _) =>
                path.Replace('\\', '/'),
            EditTool.ToolName when EditTool.TryRead(call.Arguments, out var path, out _, out _) =>
                path.Replace('\\', '/'),
            BashTool.ToolName when BashTool.TryReadCommand(call.Arguments, out var command) =>
                command,
            _ => call.Name
        };
    }
}
