using Crystal.Tools;
using CrystalCode.Approvals.Interfaces;
using CrystalCode.Plugins.Interfaces;
using CrystalCode.Skills;
using CrystalCode.Tools;

namespace CrystalCode.Approvals;

/// <summary>
/// Crystal <see cref="ToolInvocationPolicy"/> that applies risk, review, grants, and prompts.
/// </summary>
public sealed class ApprovalPolicy
{
    internal const string RetryGuidance =
        " Do not retry the same call. Change the approach or ask the user.";

    internal const string RejectedText = "The user declined this action." + RetryGuidance;

    private readonly ApprovalMode _mode;
    private readonly Workspace _workspace;
    private readonly ToolClassifier _classifier;
    private readonly GrantStore _grants;
    private readonly IApprovalPrompt _prompt;
    private readonly IApprovalReviewer? _reviewer;
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
        IReadOnlySet<string>? automaticTools = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(prompt);
        _mode = mode;
        _workspace = workspace;
        _classifier = new ToolClassifier(workspace, classifiers, skills);
        _grants = grants;
        _prompt = prompt;
        _reviewer = reviewer;
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

        var classification = _classifier.Classify(call);
        if (CanPassWithoutReview(call.Name, classification))
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Policy);
            return ToolInvocationDecision.Execute;
        }

        if (_grants.Contains(_workspace.Root, call))
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Grant);
            return ToolInvocationDecision.Execute;
        }

        ApprovalReviewVerdict? pendingReview = null;
        if (ShouldReview())
        {
            var reviewed = await TryReviewAsync(call, classification, cancellationToken);
            if (reviewed.Decision is not null)
            {
                return reviewed.Decision;
            }

            pendingReview = reviewed.Verdict;
        }

        var choice = await _prompt.AskAsync(
            call,
            classification,
            pendingReview,
            cancellationToken);
        if (!choice.IsAllow)
        {
            return ToolInvocationDecision.Reject(
                new ToolOutput(RejectedText, ToolResultStatus.Failure));
        }

        var scope = classification.Risk == Risk.Forbidden
            ? GrantScope.Once
            : choice.Scope;
        _grants.Remember(_workspace.Root, call, scope);
        return ToolInvocationDecision.Execute;
    }

    private async ValueTask<(ToolInvocationDecision? Decision, ApprovalReviewVerdict Verdict)> TryReviewAsync(
        ToolCall call,
        ToolClassification classification,
        CancellationToken cancellationToken)
    {
        var request = new ApprovalReviewRequest(
            call,
            classification,
            ReviewTranscript.Render(_reviewContext?.Conversation));
        _prompt.NotifyReviewing(call);
        var verdict = await _reviewer!.ReviewAsync(request, cancellationToken);
        if (verdict.IsDeny)
        {
            return (
                ToolInvocationDecision.Reject(
                    new ToolOutput(
                        "The approval reviewer declined this action: "
                        + verdict.Rationale
                        + RetryGuidance,
                        ToolResultStatus.Failure)),
                verdict);
        }

        if (verdict.IsAllow && classification.Risk != Risk.Forbidden)
        {
            _prompt.NotifyPassed(call, classification, ApprovalPassReason.Review, verdict);
            return (ToolInvocationDecision.Execute, verdict);
        }

        return (null, verdict);
    }

    private bool ShouldReview() =>
        UsesReviewer
        && _reviewer is not null
        && ReviewTranscript.HasAuthorization(_reviewContext?.Conversation);

    private bool UsesReviewer =>
        _mode == ApprovalMode.Review || _mode == ApprovalMode.Audit;

    private bool CanPassWithoutReview(string toolName, ToolClassification classification)
    {
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
            if (_mode == ApprovalMode.Full)
            {
                return true;
            }

            return toolName is WriteTool.ToolName or EditTool.ToolName
                && (_mode == ApprovalMode.Edit || _mode == ApprovalMode.Review);
        }

        if (toolName == BashTool.ToolName)
        {
            return _mode == ApprovalMode.Full;
        }

        return false;
    }

    private static bool IsSideEffect(string toolName) =>
        toolName is EditTool.ToolName or WriteTool.ToolName or BashTool.ToolName;
}
