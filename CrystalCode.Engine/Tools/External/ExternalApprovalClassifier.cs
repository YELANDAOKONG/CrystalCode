using Crystal.Tools;

using CrystalCode.Engine.Approvals;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Tools;
using CrystalCode.Tools.Interfaces;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Classifies loaded external tools. The floor is Write + Workspace and
/// declared path arguments can raise authority or become Forbidden. A
/// tool-provided classifier can raise risk or authority for the call; it
/// can never lower them, and a failure fails closed.
/// </summary>
public sealed class ExternalApprovalClassifier : IApprovalClassifier
{
    private const string ClassificationFailureSummary = "External tool approval classification failed";

    private readonly IReadOnlyDictionary<string, ExternalToolSpec> _tools;
    private readonly IReadOnlyDictionary<string, HostApprovalClassifierRunner> _approvalClassifiers;
    private readonly SessionToolHost? _host;

    public ExternalApprovalClassifier(
        IReadOnlyDictionary<string, ExternalToolSpec> tools,
        IReadOnlyDictionary<string, IHostToolApprovalClassifier>? approvalClassifiers = null,
        SessionToolHost? host = null)
    {
        ArgumentNullException.ThrowIfNull(tools);
        _tools = tools;
        _approvalClassifiers = (approvalClassifiers
            ?? new Dictionary<string, IHostToolApprovalClassifier>(StringComparer.Ordinal))
            .ToDictionary(
                pair => pair.Key,
                pair => new HostApprovalClassifierRunner(pair.Value),
                StringComparer.Ordinal);
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
        // Synchronous consumers cannot run an asynchronous pre-classifier.
        // Keep the safety floor and fail closed rather than skipping it.
        classification = _approvalClassifiers.ContainsKey(call.Name)
            ? Merge(floor, ClassificationFailed())
            : floor;
        return true;
    }

    public async ValueTask<ToolClassification?> ClassifyAsync(
        ToolCall call,
        Workspace workspace,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(call);
        ArgumentNullException.ThrowIfNull(workspace);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_tools.TryGetValue(call.Name, out var spec))
        {
            return null;
        }

        var floor = Classify(call.Arguments, spec, workspace);
        if (!_approvalClassifiers.TryGetValue(call.Name, out var runner))
        {
            return floor;
        }

        var result = await runner.ClassifyAsync(call, CreateContext(workspace), cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return Merge(floor, ParseProposal(result));
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
        var summary = floor.Summary;
        if (proposal.Summary == ClassificationFailureSummary)
        {
            summary = floor.Summary == "External tool"
                ? ClassificationFailureSummary
                : floor.Summary + "; " + ClassificationFailureSummary;
        }
        else if (risk != floor.Risk || authority != floor.Authority)
        {
            summary += $"; classified as {risk.Value} / {authority.Value}";
        }

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

    private static ToolClassification ClassificationFailed(bool requirePrompt = false) =>
        new(Risk.Privileged, Authority.PrivilegedEscalation, ClassificationFailureSummary, requirePrompt);

    private static ToolClassification ParseProposal(HostToolApprovalClassification? result)
    {
        if (result is null
            || ParseRisk(result.Risk) is not { } risk
            || ParseAuthority(result.Authority) is not { } authority)
        {
            return ClassificationFailed(result?.RequirePrompt ?? false);
        }

        return new ToolClassification(risk, authority, "External tool", result.RequirePrompt);
    }

    private static Risk? ParseRisk(string? value) =>
        Normalize(value) switch
        {
            "read" => Risk.Read,
            "write" => Risk.Write,
            "privileged" => Risk.Privileged,
            "forbidden" => Risk.Forbidden,
            _ => null
        };

    private static Authority? ParseAuthority(string? value) =>
        Normalize(value) switch
        {
            "workspace" => Authority.Workspace,
            "outsideworkspace" => Authority.OutsideWorkspace,
            "network" => Authority.Network,
            "privilegedescalation" => Authority.PrivilegedEscalation,
            _ => null
        };

    private static string? Normalize(string? value) =>
        value?.Trim().ToLowerInvariant().Replace("_", string.Empty);

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
