using Crystal.Tools;

using CrystalHarness.Tools;

namespace CrystalHarness.Approvals;

/// <summary>
/// Assigns risk and authority to one model-generated tool call.
/// </summary>
public sealed class ToolClassifier
{
    private readonly Workspace _workspace;

    public ToolClassifier(Workspace workspace)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
    }

    public ToolClassification Classify(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        return call.Name switch
        {
            ReadTool.ToolName or GlobTool.ToolName or GrepTool.ToolName
                or TodoWriteTool.ToolName or QuestionTool.ToolName =>
                new ToolClassification(Risk.Read, Authority.Workspace, "Read-only tool"),
            WriteTool.ToolName => ClassifyWrite(call.Arguments),
            EditTool.ToolName => ClassifyEdit(call.Arguments),
            BashTool.ToolName => ClassifyBash(call.Arguments),
            _ => new ToolClassification(
                Risk.Privileged,
                Authority.PrivilegedEscalation,
                "Unknown tool")
        };
    }

    private ToolClassification ClassifyWrite(string arguments)
    {
        if (!WriteTool.TryRead(arguments, out var path, out _))
        {
            return new ToolClassification(
                Risk.Write,
                Authority.Workspace,
                "Write workspace file");
        }

        return ClassifyFilePath(path, "Write");
    }

    private ToolClassification ClassifyEdit(string arguments)
    {
        if (!EditTool.TryRead(arguments, out var path, out _, out _))
        {
            return new ToolClassification(
                Risk.Write,
                Authority.Workspace,
                "Edit workspace file");
        }

        return ClassifyFilePath(path, "Edit");
    }

    private ToolClassification ClassifyFilePath(string path, string verb)
    {
        if (IsCredentialPath(path))
        {
            return new ToolClassification(
                Risk.Forbidden,
                Authority.PrivilegedEscalation,
                $"{verb} credential path");
        }

        if (!_workspace.TryResolveWritablePath(path, out _, out var error)
            && error.Contains("outside", StringComparison.OrdinalIgnoreCase))
        {
            return new ToolClassification(
                Risk.Write,
                Authority.OutsideWorkspace,
                $"{verb} outside workspace");
        }

        return new ToolClassification(
            Risk.Write,
            Authority.Workspace,
            $"{verb} workspace file");
    }

    private static ToolClassification ClassifyBash(string arguments)
    {
        if (!BashTool.TryReadCommand(arguments, out var command))
        {
            return new ToolClassification(
                Risk.Write,
                Authority.Workspace,
                "Workspace shell command");
        }

        var (risk, authority, summary) = ShellRisk.Classify(command);
        return new ToolClassification(risk, authority, summary);
    }

    private static bool IsCredentialPath(string path)
    {
        var expanded = Workspace.Expand(path).Replace('\\', '/');
        return expanded.Contains("/.ssh/", StringComparison.OrdinalIgnoreCase)
            || expanded.EndsWith("/.ssh", StringComparison.OrdinalIgnoreCase)
            || expanded.Contains("/.gnupg/", StringComparison.OrdinalIgnoreCase)
            || expanded.EndsWith("/.gnupg", StringComparison.OrdinalIgnoreCase)
            || expanded.Contains("/.crystal/credentials.json", StringComparison.OrdinalIgnoreCase);
    }
}
