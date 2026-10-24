using Crystal.Tools;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Assigns risk and authority to one model-generated tool call.
/// </summary>
public sealed class ToolClassifier
{
    private readonly Workspace _workspace;
    private readonly IReadOnlyList<IApprovalClassifier> _classifiers;
    private readonly SkillCatalog? _skills;
    private readonly Func<ToolCall, ToolClassification, ToolClassification>? _advise;

    public ToolClassifier(
        Workspace workspace,
        IReadOnlyList<IApprovalClassifier>? classifiers = null,
        SkillCatalog? skills = null,
        Func<ToolCall, ToolClassification, ToolClassification>? advise = null)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        _workspace = workspace;
        _classifiers = classifiers ?? [];
        _skills = skills;
        _advise = advise;
    }

    public ToolClassification Classify(ToolCall call)
    {
        ArgumentNullException.ThrowIfNull(call);
        var classification = ClassifyCore(call);
        return _advise?.Invoke(call, classification) ?? classification;
    }

    public async ValueTask<ToolClassification> ClassifyAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        cancellationToken.ThrowIfCancellationRequested();
        var classification = ClassifyBuiltIn(call);
        if (classification is null)
        {
            foreach (var classifier in _classifiers)
            {
                classification = await classifier.ClassifyAsync(call, _workspace, cancellationToken);
                if (classification is not null)
                {
                    break;
                }
            }
        }

        classification ??= new ToolClassification(
            Risk.Privileged, Authority.PrivilegedEscalation, "Unknown tool");
        cancellationToken.ThrowIfCancellationRequested();
        return _advise?.Invoke(call, classification) ?? classification;
    }

    private ToolClassification ClassifyCore(ToolCall call)
    {
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
            DescribeImageTool.ToolName => ClassifyRead(call.Arguments, "Describe image", pathRequired: true),
            ViewImageTool.ToolName => ClassifyRead(call.Arguments, "View image", pathRequired: true),
            ListTool.ToolName => ClassifyRead(call.Arguments, "List", pathRequired: false),
            TreeTool.ToolName => ClassifyRead(call.Arguments, "Tree", pathRequired: false),
            GlobTool.ToolName => ClassifyRead(call.Arguments, "Glob", pathRequired: false),
            GrepTool.ToolName => ClassifyRead(call.Arguments, "Grep", pathRequired: false),
            SearchTool.ToolName => ClassifyRead(call.Arguments, "Search", pathRequired: false),
            TaskTool.ToolName => new ToolClassification(Risk.Read, Authority.Workspace, "Delegate child task"),
            TodoWriteTool.ToolName or TodoReadTool.ToolName or QuestionTool.ToolName or SkillTool.ToolName =>
                new ToolClassification(Risk.Read, Authority.Workspace, "Read-only tool"),
            WriteTool.ToolName => ClassifyWrite(call.Arguments),
            EditTool.ToolName => ClassifyEdit(call.Arguments),
            ApplyPatchTool.ToolName => ClassifyPatch(call.Arguments),
            BashTool.ToolName => ClassifyBash(call.Arguments),
            BackgroundBashStartTool.ToolName => ClassifyBash(call.Arguments),
            BackgroundBashStatusTool.ToolName or BackgroundBashLogsTool.ToolName =>
                new ToolClassification(Risk.Read, Authority.Workspace, "Read background shell task"),
            BackgroundBashStopTool.ToolName =>
                new ToolClassification(Risk.Write, Authority.Workspace, "Stop background shell task"),
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
            || string.IsNullOrWhiteSpace(optionalPath))
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

        if (!_workspace.IsAccessible(fullPath))
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

    /// <summary>
    /// A patch is as risky as its riskiest path: one credential path makes
    /// the whole call Forbidden, and one path outside the session's
    /// directories makes it an outside write. A patch that cannot be read
    /// fails inside the tool before it writes anything.
    /// </summary>
    private ToolClassification ClassifyPatch(string arguments)
    {
        if (!ApplyPatchTool.TryReadPaths(arguments, out var paths))
        {
            return new ToolClassification(Risk.Write, Authority.Workspace, "Apply patch");
        }

        var outside = false;
        foreach (var path in paths)
        {
            var classification = ClassifyFilePath(path, "Patch");
            if (classification.Risk == Risk.Forbidden)
            {
                return classification;
            }

            outside |= classification.Authority == Authority.OutsideWorkspace;
        }

        return outside
            ? new ToolClassification(Risk.Write, Authority.OutsideWorkspace, "Patch outside workspace")
            : new ToolClassification(Risk.Write, Authority.Workspace, "Apply patch");
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

        if (_workspace.TryGetFullPath(path, out var fullPath, out _))
        {
            if (Workspace.IsCredentialPath(fullPath))
            {
                return new ToolClassification(
                    Risk.Forbidden,
                    Authority.PrivilegedEscalation,
                    $"{verb} credential path");
            }

            return new ToolClassification(
                Risk.Write,
                _workspace.IsAccessible(fullPath) ? Authority.Workspace : Authority.OutsideWorkspace,
                _workspace.IsAccessible(fullPath) ? $"{verb} session file" : $"{verb} outside workspace");
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
