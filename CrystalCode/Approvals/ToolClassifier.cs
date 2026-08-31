using Crystal.Tools;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Skills;
using CrystalCode.Tools;

namespace CrystalCode.Approvals;

/// <summary>
/// Assigns risk and authority to one model-generated tool call.
/// </summary>
public sealed class ToolClassifier
{
    private readonly Workspace _workspace;
    private readonly IReadOnlyList<IApprovalClassifier> _classifiers;
    private readonly SkillCatalog? _skills;

    public ToolClassifier(
        Workspace workspace,
        IReadOnlyList<IApprovalClassifier>? classifiers = null,
        SkillCatalog? skills = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        _classifiers = classifiers ?? [];
        _skills = skills;
    }

    public ToolClassification Classify(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var builtIn = ClassifyBuiltIn(call);
        if (builtIn is not null)
        {
            return builtIn;
        }

        foreach (var classifier in _classifiers)
        {
            if (classifier.TryClassify(call, _workspace, out var extra))
            {
                return extra;
            }
        }

        return new ToolClassification(
            Risk.Privileged,
            Authority.PrivilegedEscalation,
            "Unknown tool");
    }

    private ToolClassification? ClassifyBuiltIn(ToolCall call) =>
        call.Name switch
        {
            ReadTool.ToolName => ClassifyRead(call.Arguments, "Read", pathRequired: true),
            GlobTool.ToolName => ClassifyRead(call.Arguments, "Glob", pathRequired: false),
            GrepTool.ToolName => ClassifyRead(call.Arguments, "Grep", pathRequired: false),
            TodoWriteTool.ToolName or QuestionTool.ToolName or SkillTool.ToolName =>
                new ToolClassification(Risk.Read, Authority.Workspace, "Read-only tool"),
            WriteTool.ToolName => ClassifyWrite(call.Arguments),
            EditTool.ToolName => ClassifyEdit(call.Arguments),
            BashTool.ToolName => ClassifyBash(call.Arguments),
            _ => null
        };

    private ToolClassification ClassifyRead(string arguments, string verb, bool pathRequired)
    {
        if (pathRequired)
        {
            if (!ToolArguments.TryReadRequiredString(arguments, "path", out var requiredPath))
            {
                return new ToolClassification(Risk.Read, Authority.Workspace, $"{verb} workspace file");
            }

            return ClassifyReadPath(requiredPath, verb);
        }

        if (!ToolArguments.TryReadOptionalString(arguments, "path", out var optionalPath)
            || optionalPath is null)
        {
            return new ToolClassification(Risk.Read, Authority.Workspace, $"{verb} workspace");
        }

        return ClassifyReadPath(optionalPath, verb);
    }

    private ToolClassification ClassifyReadPath(string path, string verb)
    {
        if (Workspace.IsCredentialPath(path))
        {
            return new ToolClassification(
                Risk.Forbidden,
                Authority.PrivilegedEscalation,
                $"{verb} credential path");
        }

        if (!_workspace.TryGetFullPath(path, out var fullPath, out _))
        {
            return new ToolClassification(Risk.Read, Authority.Workspace, $"{verb} workspace file");
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return new ToolClassification(
                Risk.Forbidden,
                Authority.PrivilegedEscalation,
                $"{verb} credential path");
        }

        if (_skills is not null && _skills.ContainsReadablePath(fullPath))
        {
            return new ToolClassification(
                Risk.Read,
                Authority.Workspace,
                $"{verb} skills path");
        }

        if (!_workspace.Contains(fullPath))
        {
            return new ToolClassification(
                Risk.Read,
                Authority.OutsideWorkspace,
                $"{verb} outside workspace");
        }

        return new ToolClassification(Risk.Read, Authority.Workspace, $"{verb} workspace file");
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
        if (Workspace.IsCredentialPath(path))
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
}
