using Crystal.Tools;

using CrystalHarness.Tools;

namespace CrystalHarness.Approvals;

/// <summary>
/// Crystal <see cref="ToolInvocationPolicy"/> that applies risk, review, grants, and prompts.
/// </summary>
public sealed class ApprovalPolicy
{
    internal const string RejectedText = "The user declined this action.";

    private readonly ApprovalMode _mode;
    private readonly Workspace _workspace;
    private readonly ToolClassifier _classifier;
    private readonly GrantStore _grants;
    private readonly IApprovalPrompt _prompt;
    private readonly IApprovalReviewer? _reviewer;
    private readonly IApprovalReviewContext? _reviewContext;

    public ApprovalPolicy(
        ApprovalMode mode,
        Workspace workspace,
        GrantStore grants,
        IApprovalPrompt prompt,
        IApprovalReviewer? reviewer = null,
        IApprovalReviewContext? reviewContext = null)
    {
        ArgumentNullException.ThrowIfNull(mode);
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentNullException.ThrowIfNull(grants);
        ArgumentNullException.ThrowIfNull(prompt);
        _mode = mode;
        _workspace = workspace;
        _classifier = new ToolClassifier(workspace);
        _grants = grants;
        _prompt = prompt;
        _reviewer = reviewer;
        _reviewContext = reviewContext;
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
            return ToolInvocationDecision.Execute;
        }

        if (_grants.Contains(_workspace.Root, call))
        {
            return ToolInvocationDecision.Execute;
        }

        if (_mode == ApprovalMode.Review
            && _reviewer is not null
            && !string.IsNullOrWhiteSpace(_reviewContext?.CurrentUserRequest))
        {
            var reviewed = await ReviewAsync(call, classification, cancellationToken);
            if (reviewed is not null)
            {
                return reviewed;
            }
        }

        var choice = await _prompt.AskAsync(call, classification, cancellationToken);
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

    private async ValueTask<ToolInvocationDecision?> ReviewAsync(
        ToolCall call,
        ToolClassification classification,
        CancellationToken cancellationToken)
    {
        var request = new ApprovalReviewRequest(
            call,
            classification,
            _reviewContext?.CurrentUserRequest ?? string.Empty);
        var verdict = await _reviewer!.ReviewAsync(request, cancellationToken);
        if (verdict.IsDeny)
        {
            return ToolInvocationDecision.Reject(
                new ToolOutput(
                    "The approval reviewer declined this action: " + verdict.Rationale,
                    ToolResultStatus.Failure));
        }

        if (verdict.IsAllow && classification.Risk != Risk.Forbidden)
        {
            return ToolInvocationDecision.Execute;
        }

        return null;
    }

    private bool CanPassWithoutReview(string toolName, ToolClassification classification)
    {
        if (classification.Risk == Risk.Forbidden
            || classification.Risk == Risk.Privileged
            || classification.Authority != Authority.Workspace)
        {
            return false;
        }

        if (classification.Risk == Risk.Read)
        {
            return true;
        }

        if (toolName == BashTool.ToolName)
        {
            return _mode == ApprovalMode.Full;
        }

        if (toolName is WriteTool.ToolName or EditTool.ToolName)
        {
            return _mode == ApprovalMode.AutoEdit || _mode == ApprovalMode.Full;
        }

        return false;
    }

    private static bool IsSideEffect(string toolName) =>
        toolName is EditTool.ToolName or WriteTool.ToolName or BashTool.ToolName;
}
