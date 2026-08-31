using Crystal.Tools;
using CrystalCode.Approvals;
using CrystalCode.Plugins.Interfaces;

namespace CrystalCode.Tools.External;

/// <summary>
/// Classifies loaded external tools. Floor is Write + Workspace; path
/// arguments can raise authority or become Forbidden.
/// </summary>
public sealed class ExternalApprovalClassifier : IApprovalClassifier
{
    private readonly IReadOnlyDictionary<string, ExternalToolSpec> _tools;

    public ExternalApprovalClassifier(IReadOnlyDictionary<string, ExternalToolSpec> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
    }

    public bool TryClassify(
        ToolCall call,
        Workspace workspace,
        out ToolClassification classification)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(workspace);
        classification = null!;
        if (!_tools.TryGetValue(call.Name, out var spec))
        {
            return false;
        }

        classification = Classify(call.Arguments, spec, workspace);
        return true;
    }

    private static ToolClassification Classify(
        string arguments,
        ExternalToolSpec spec,
        Workspace workspace)
    {
        var risk = Risk.Write;
        var authority = Authority.Workspace;
        var summary = "External tool";
        foreach (var name in spec.PathArguments)
        {
            if (!ToolArguments.TryReadOptionalString(arguments, name, out var path)
                || path is null)
            {
                continue;
            }

            if (Workspace.IsCredentialPath(path))
            {
                return new ToolClassification(
                    Risk.Forbidden,
                    Authority.PrivilegedEscalation,
                    "External tool credential path");
            }

            if (!workspace.TryGetFullPath(path, out var fullPath, out _))
            {
                continue;
            }

            if (Workspace.IsCredentialPath(fullPath))
            {
                return new ToolClassification(
                    Risk.Forbidden,
                    Authority.PrivilegedEscalation,
                    "External tool credential path");
            }

            if (!workspace.Contains(fullPath))
            {
                risk = Risk.Write;
                authority = Authority.OutsideWorkspace;
                summary = "External tool outside workspace";
            }
        }

        return new ToolClassification(risk, authority, summary);
    }
}
