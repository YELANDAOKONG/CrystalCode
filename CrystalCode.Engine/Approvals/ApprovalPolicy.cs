using Crystal.Tools;
using CrystalCode.Engine.Approvals.Interfaces;
using CrystalCode.Engine.Plugins.Interfaces;
using CrystalCode.Engine.Skills;
using CrystalCode.Engine.Tools;

namespace CrystalCode.Engine.Approvals;

/// <summary>
/// Crystal <see cref="ToolInvocationPolicy"/> that applies risk, review, grants, and prompts.
/// </summary>
public sealed class ApprovalPolicy
{
    internal const string RetryGuidance =
        " Do not retry the same call. Change the approach or ask the user.";

    internal const string RejectedText = "The user declined this action." + RetryGuidance;

    internal const string ReviewFailedText =
        "An error occurred and the approval request could not be processed, so this action was not run."
        + " Do not retry the same call.";

    private readonly ApprovalMode _mode;
    private readonly Workspace _workspace;
    private readonly ToolClassifier _classifier;
    private readonly GrantStore _grants;
    private readonly IApprovalPrompt _prompt;
    private readonly IApprovalReviewer? _reviewer;
    private readonly ExtendedApprovalReviewer? _extended;
    private readonly IApprovalReviewContext? _reviewContext;
    private readonly IReadOnlySet<string> _automaticTools;

    public ApprovalPolicy(
        ApprovalMode mode,
        Workspace workspace,
        GrantStore grants,
        IApprovalPrompt prompt,
        IApprovalReviewer? reviewer = null,
        IApprovalReviewContext? reviewContext = null,
        IReadOnlyList<IApprovalClassifier>? classifiers = null,
        SkillCatalog? skills = null,
        IReadOnlySet<string>? automaticTools = null,
        Func<ToolCall, ToolClassification, ToolClassification>? advise = null,
        ExtendedApprovalReviewer? extended = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(prompt);
        _mode = mode;
        _workspace = workspace;
        _workspace.BypassAccessRestrictions = mode == ApprovalMode.Full;
        _classifier = new ToolClassifier(workspace, classifiers, skills, advise);
        _grants = grants;
        _prompt = prompt;
        _reviewer = reviewer;
        _extended = extended;
        _reviewContext = reviewContext;
        _automaticTools = automaticTools ?? new HashSet<string>(StringComparer.Ordinal);
    }

    public async ValueTask<ToolInvocationDecision> DecideAsync(
        ToolCall call,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ArgumentNullException.ThrowIfNull(call);

        if (_mode == ApprovalMode.Plan && IsSideEffect(call.Name))
        {
            return ToolInvocationDecision.Reject(
                new ToolOutput(
                    $"Plan mode does not allow {call.Name}.",
                    ToolResultStatus.Failure));
        }

        if (TargetsReadOnlyDirectory(call))
        {
            return ToolInvocationDecision.Reject(new ToolOutput(
                "Writing to a read-only session directory is not allowed. Change its access with /access.",
                ToolResultStatus.Failure));
        }

        var classification = await _classifier.ClassifyAsync(call, cancellationToken);
        if (CanPassWithoutReview(call.Name, classification))
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Policy);
            return ToolInvocationDecision.Execute;
        }

        if (classification.AllowsReusableGrant && _grants.Contains(_workspace.Root, call))
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Grant);
            return ToolInvocationDecision.Execute;
        }

        ApprovalReviewVerdict? pendingReview = null;
        ApprovalReviewVerdict? pendingExtended = null;
        if (ShouldReview())
        {
            var reviewed = await TryReviewAsync(call, classification, cancellationToken);
            if (reviewed.Decision is not null)
            {
                return reviewed.Decision;
            }

            pendingReview = reviewed.Verdict;
            pendingExtended = reviewed.Extended;
        }

        var choice = await _prompt.AskAsync(
            call,
            classification,
            pendingReview,
            pendingExtended,
            cancellationToken);
        if (!choice.IsAllow)
        {
            // Only a missing operator after a failed review is an error. A person who
            // answered deny, or an unattended run with no review failure, is a refusal.
            var reviewFailed = choice.IsUnavailable
                && (pendingReview?.IsFailure == true || pendingExtended?.IsFailure == true);
            return ToolInvocationDecision.Reject(
                new ToolOutput(reviewFailed ? ReviewFailedText : RejectedText, ToolResultStatus.Failure));
        }

        var scope = classification.AllowsReusableGrant ? choice.Scope : GrantScope.Once;
        _grants.Remember(_workspace.Root, call, scope);
        return ToolInvocationDecision.Execute;
    }

    private async ValueTask<(ToolInvocationDecision? Decision, ApprovalReviewVerdict Verdict, ApprovalReviewVerdict? Extended)> TryReviewAsync(
        ToolCall call,
        ToolClassification classification,
        CancellationToken cancellationToken)
    {
        var conversation = ReviewTranscript.Render(_reviewContext?.Conversation);
        var request = new ApprovalReviewRequest(call, classification, conversation);
        _prompt.NotifyReviewing(call, ApprovalReviewStage.Primary);
        var verdict = await _reviewer!.ReviewAsync(request, cancellationToken);
        if (verdict.IsDeny)
        {
            return (Declined("The approval reviewer", verdict), verdict, null);
        }

        // Forbidden and forced-prompt calls reach the operator whatever a
        // reviewer says, so the extended check would not change the outcome.
        if (!verdict.IsAllow
            || classification.Risk == Risk.Forbidden
            || classification.RequirePrompt)
        {
            return (null, verdict, null);
        }

        if (_extended is null || !_extended.Applies(verdict))
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Review, verdict);
            return (ToolInvocationDecision.Execute, verdict, null);
        }

        var extendedRequest = request with
        {
            PrimaryReview = _extended.IncludePrimaryReview ? verdict : null
        };
        _prompt.NotifyReviewing(call, ApprovalReviewStage.Extended);
        var extended = await _extended.Reviewer.ReviewAsync(extendedRequest, cancellationToken);
        if (extended.IsDeny)
        {
            return (Declined("The extended approval reviewer", extended), verdict, extended);
        }

        if (!extended.IsAllow)
        {
            return (null, verdict, extended);
        }

        _prompt.NotifyPassed(call, classification, ApprovalPassReason.Extended, verdict, extended);
        return (ToolInvocationDecision.Execute, verdict, extended);
    }

    private static ToolInvocationDecision Declined(string reviewer, ApprovalReviewVerdict verdict) =>
        ToolInvocationDecision.Reject(
            new ToolOutput(
                reviewer + " declined this action: " + verdict.Rationale + RetryGuidance,
                ToolResultStatus.Failure));

    private bool ShouldReview() =>
        UsesReviewer
        && _reviewer is not null
        && ReviewTranscript.HasAuthorization(_reviewContext?.Conversation);

    private bool UsesReviewer =>
        _mode == ApprovalMode.Review || _mode == ApprovalMode.Audit;

    private bool CanPassWithoutReview(string toolName, ToolClassification classification)
    {
        if (_mode == ApprovalMode.Full)
        {
            return true;
        }

        if (classification.RequirePrompt)
        {
            return false;
        }

        if (classification.Risk == Risk.Forbidden
            || classification.Risk == Risk.Privileged
            || classification.Authority != Authority.Workspace)
        {
            return false;
        }

        if (_automaticTools.Contains(toolName))
        {
            return true;
        }

        if (classification.Risk == Risk.Read)
        {
            return true;
        }

        if (classification.Risk == Risk.Write)
        {
            return toolName is WriteTool.ToolName or EditTool.ToolName or ApplyPatchTool.ToolName
                && (_mode == ApprovalMode.Edit || _mode == ApprovalMode.Review);
        }

        return false;
    }

    private bool TargetsReadOnlyDirectory(ToolCall call)
    {
        if (call.Name is WriteTool.ToolName or EditTool.ToolName)
        {
            return ToolArguments.TryReadRequiredString(call.Arguments, "path", out var path)
                && IsReadOnlyPath(path);
        }

        return call.Name == ApplyPatchTool.ToolName
            && ApplyPatchTool.TryReadPaths(call.Arguments, out var paths)
            && paths.Any(IsReadOnlyPath);
    }

    private bool IsReadOnlyPath(string path) =>
        _workspace.TryGetFullPath(path, out var fullPath, out _)
        && _workspace.IsReadOnly(fullPath);

    private static bool IsSideEffect(string toolName) =>
        toolName is EditTool.ToolName
            or WriteTool.ToolName
            or ApplyPatchTool.ToolName
            or BashTool.ToolName
            or BackgroundBashStartTool.ToolName
            or BackgroundBashStopTool.ToolName;
}
