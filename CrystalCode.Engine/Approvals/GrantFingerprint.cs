using Crystal.Tools;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Approvals;

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
            ReadTool.ToolName when ToolArguments.TryReadRequiredString(call.Arguments, "path", out var readPath) =>
                readPath.Replace('\\', '/'),
            DescribeImageTool.ToolName or ViewImageTool.ToolName
                when ToolArguments.TryReadRequiredString(call.Arguments, "path", out var imagePath) =>
                imagePath.Replace('\\', '/'),
            ListTool.ToolName when ToolArguments.TryReadOptionalString(call.Arguments, "path", out var listPath)
                && listPath is not null =>
                listPath.Replace('\\', '/'),
            GlobTool.ToolName when ToolArguments.TryReadRequiredString(call.Arguments, "path", out var globPath) =>
                globPath.Replace('\\', '/'),
            GrepTool.ToolName when ToolArguments.TryReadRequiredString(call.Arguments, "path", out var grepPath) =>
                grepPath.Replace('\\', '/'),
            BashTool.ToolName when BashTool.TryReadCommand(call.Arguments, out var command) =>
                command,
            _ => call.Name
        };
    }
}
