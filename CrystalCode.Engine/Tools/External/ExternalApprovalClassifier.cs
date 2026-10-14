using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Tools;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Classifies loaded external tools. The floor is Write + Workspace and
/// declared path arguments can raise authority or become Forbidden. A
/// tool-provided classifier can raise risk or authority for the call; it
/// can never lower them, and a failure fails closed.
/// </summary>
public sealed class ExternalApprovalClassifier : IApprovalClassifier
{
    private readonly IReadOnlyDictionary<string, ExternalToolSpec> _tools;
    private readonly IReadOnlyDictionary<string, IHostToolApprovalClassifier> _approvalClassifiers;
    private readonly SessionToolHost? _host;

    public ExternalApprovalClassifier(
        IReadOnlyDictionary<string, ExternalToolSpec> tools,
        IReadOnlyDictionary<string, IHostToolApprovalClassifier>? approvalClassifiers = null,
        SessionToolHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        _approvalClassifiers = approvalClassifiers
            ?? new Dictionary<string, IHostToolApprovalClassifier>(StringComparer.Ordinal);
        _host = host;
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

        var floor = Classify(call.Arguments, spec, workspace);
        classification = _approvalClassifiers.TryGetValue(call.Name, out var approvalClassifier)
            ? Merge(floor, ClassifyWithTool(approvalClassifier, call, CreateContext(workspace)))
            : floor;
        return true;
    }

    /// <summary>
    /// Captures the live host facts for one classifier call. The data
    /// directories stay empty because the classifier runs before the call
    /// and nothing is created for it.
    /// </summary>
    private ToolHostContext CreateContext(Workspace workspace) =>
        _host is null
            ? new ToolHostContext(workspace.Root, string.Empty, string.Empty)
            : new ToolHostContext(_host.WorkspaceRoot, _host.SessionId, _host.Approval);

    /// <summary>
    /// Combines the host classification with the tool classification. The
    /// result is at least as strict as the host classification on both axes;
    /// the tool can raise risk or authority, never lower them.
    /// </summary>
    private static ToolClassification Merge(ToolClassification floor, ToolClassification proposal)
    {
        var risk = Rank(proposal.Risk) > Rank(floor.Risk) ? proposal.Risk : floor.Risk;
        var authority = Rank(proposal.Authority) > Rank(floor.Authority)
            ? proposal.Authority
            : floor.Authority;
        var summary = risk == proposal.Risk && authority == proposal.Authority
            ? proposal.Summary
            : floor.Summary;
        return new ToolClassification(
            risk,
            authority,
            summary,
            floor.RequirePrompt || proposal.RequirePrompt);
    }

    private static int Rank(Risk risk)
    {
        if (risk == Risk.Read)
        {
            return 0;
        }

        if (risk == Risk.Write)
        {
            return 1;
        }

        if (risk == Risk.Privileged)
        {
            return 2;
        }

        if (risk == Risk.Forbidden)
        {
            return 3;
        }

        return 2;
    }

    private static int Rank(Authority authority)
    {
        if (authority == Authority.Workspace)
        {
            return 0;
        }

        if (authority == Authority.OutsideWorkspace)
        {
            return 1;
        }

        if (authority == Authority.Network)
        {
            return 2;
        }

        if (authority == Authority.PrivilegedEscalation)
        {
            return 3;
        }

        return 3;
    }

    private static ToolClassification ClassifyWithTool(
        IHostToolApprovalClassifier approvalClassifier,
        ToolCall call,
        ToolHostContext context)
    {
        try
        {
            var result = approvalClassifier.ClassifyApproval(call, context);
            return new ToolClassification(
                ParseRisk(result.Risk),
                ParseAuthority(result.Authority),
                string.IsNullOrWhiteSpace(result.Summary)
                    ? "External tool"
                    : result.Summary,
                result.RequirePrompt);
        }
        catch (Exception)
        {
            return new ToolClassification(
                Risk.Privileged,
                Authority.PrivilegedEscalation,
                "External tool approval classification failed");
        }
    }

    private static Risk ParseRisk(string value) =>
        Normalize(value) switch
        {
            "read" => Risk.Read,
            "write" => Risk.Write,
            "privileged" => Risk.Privileged,
            "forbidden" => Risk.Forbidden,
            _ => Risk.Privileged
        };

    private static Authority ParseAuthority(string value) =>
        Normalize(value) switch
        {
            "workspace" => Authority.Workspace,
            "outsideworkspace" => Authority.OutsideWorkspace,
            "network" => Authority.Network,
            "privilegedescalation" => Authority.PrivilegedEscalation,
            _ => Authority.PrivilegedEscalation
        };

    private static string Normalize(string value) =>
        value.Trim().ToLowerInvariant().Replace("_", string.Empty);

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
                || string.IsNullOrWhiteSpace(path))
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
