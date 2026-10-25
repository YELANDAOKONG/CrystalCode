using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Resolved prompt texts after default, home, and project overlay.
/// </summary>
public sealed record PromptSet
{
    public PromptSet(
        string work,
        string plan,
        string review,
        string instructions,
        IReadOnlyList<string>? workAttachments = null,
        IReadOnlyList<string>? planAttachments = null,
        IReadOnlyList<string>? reviewAttachments = null,
        string? topic = null,
        string? reviewUser = null,
        string? compactionSystem = null,
        string? compactionUser = null,
        string? imageSystem = null,
        string? imageUser = null,
        string? agentGeneral = null,
        string? agentWork = null,
        string? agentPlan = null,
        IReadOnlyList<string>? agentGeneralAttachments = null,
        IReadOnlyList<string>? agentWorkAttachments = null,
        IReadOnlyList<string>? agentPlanAttachments = null,
        string? reviewExtended = null,
        string? reviewExtendedUser = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(work);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(review);
        ArgumentNullException.ThrowIfNull(instructions);

        Work = work.Trim();
        Plan = plan.Trim();
        Review = review.Trim();
        Instructions = instructions.Trim();
        WorkAttachments = CopyAttachments(workAttachments);
        PlanAttachments = CopyAttachments(planAttachments);
        ReviewAttachments = CopyAttachments(reviewAttachments);
        Topic = TextOrBuiltIn(topic, TopicNamingPrompt.Text);
        ReviewUser = TextOrBuiltIn(reviewUser, ApprovalReviewPrompt.UserTemplate);
        ReviewExtended = TextOrBuiltIn(reviewExtended, ExtendedApprovalPrompt.SystemText);
        ReviewExtendedUser = TextOrBuiltIn(reviewExtendedUser, ExtendedApprovalPrompt.UserTemplate);
        CompactionSystem = TextOrBuiltIn(compactionSystem, CompactionPrompt.SystemText);
        CompactionUser = TextOrBuiltIn(compactionUser, CompactionPrompt.UserTemplate);
        ImageSystem = TextOrBuiltIn(imageSystem, ImageDescriptionPrompt.SystemText);
        ImageUser = TextOrBuiltIn(imageUser, ImageDescriptionPrompt.UserTemplate);
        AgentGeneral = TextOrBuiltIn(agentGeneral, AgentGeneralPrompt.Text);
        AgentWork = TextOrBuiltIn(agentWork, AgentWorkPrompt.Text);
        AgentPlan = TextOrBuiltIn(agentPlan, AgentPlanPrompt.Text);
        AgentGeneralAttachments = CopyAttachments(agentGeneralAttachments);
        AgentWorkAttachments = CopyAttachments(agentWorkAttachments);
        AgentPlanAttachments = CopyAttachments(agentPlanAttachments);
    }

    public string Work { get; }

    public string Plan { get; }

    public string Review { get; }

    public string Instructions { get; }

    public IReadOnlyList<string> WorkAttachments { get; }

    public IReadOnlyList<string> PlanAttachments { get; }

    public IReadOnlyList<string> ReviewAttachments { get; }

    public string Topic { get; }

    public string ReviewUser { get; }

    /// <summary>
    /// Extended approval system template. Prompt attachments do not append to it.
    /// </summary>
    public string ReviewExtended { get; }

    public string ReviewExtendedUser { get; }

    public string CompactionSystem { get; }

    public string CompactionUser { get; }

    public string ImageSystem { get; }

    public string ImageUser { get; }

    public string AgentGeneral { get; }

    public string AgentWork { get; }

    public string AgentPlan { get; }

    public IReadOnlyList<string> AgentGeneralAttachments { get; }

    public IReadOnlyList<string> AgentWorkAttachments { get; }

    public IReadOnlyList<string> AgentPlanAttachments { get; }

    public string WorkSystem =>
        ComposeWork(PromptContext.InstructionsOnly(Instructions));

    public string PlanSystem =>
        ComposePlan(PromptContext.InstructionsOnly(Instructions));

    public string ReviewSystem =>
        ComposeReview(PromptContext.InstructionsOnly(Instructions).WithMode("review"));

    public string ComposeWork(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(PromptBinder.Apply(Work, context, placeholders), WorkAttachments, context, placeholders);

    public string ComposePlan(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(PromptBinder.Apply(Plan, context, placeholders), PlanAttachments, context, placeholders);

    public string ComposeReview(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(PromptBinder.Apply(Review, context, placeholders), ReviewAttachments, context, placeholders);

    public string ComposeReviewExtended(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        PromptBinder.Apply(ReviewExtended, context, placeholders);

    public string ComposeAgentGeneral(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(
            PromptBinder.Apply(AgentGeneral, context, placeholders),
            AgentGeneralAttachments,
            context,
            placeholders);

    public string ComposeAgentWork(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(
            PromptBinder.Apply(AgentWork, context, placeholders),
            AgentWorkAttachments,
            context,
            placeholders);

    public string ComposeAgentPlan(PromptContext context, PluginPlaceholderTable? placeholders = null) =>
        AppendAttachments(
            PromptBinder.Apply(AgentPlan, context, placeholders),
            AgentPlanAttachments,
            context,
            placeholders);

    public override string ToString() => nameof(PromptSet);

    private static IReadOnlyList<string> CopyAttachments(IReadOnlyList<string>? attachments)
    {
        if (attachments is null || attachments.Count == 0)
        {
            return [];
        }

        var copy = new List<string>(attachments.Count);
        foreach (var attachment in attachments)
        {
            ArgumentNullException.ThrowIfNull(attachment);
            var trimmed = PromptFiles.TrimKeepingTrailingNewlines(attachment);
            if (trimmed.Length > 0)
            {
                copy.Add(trimmed);
            }
        }

        return copy;
    }

    private static string TextOrBuiltIn(string? text, string builtIn) =>
        string.IsNullOrWhiteSpace(text) ? builtIn : text.Trim();

    private static string AppendAttachments(
        string body,
        IReadOnlyList<string> attachments,
        PromptContext context,
        PluginPlaceholderTable? placeholders)
    {
        if (attachments.Count == 0)
        {
            return body;
        }

        var parts = new List<string> { body };
        foreach (var attachment in attachments)
        {
            var text = PromptFiles.TrimKeepingTrailingNewlines(
                PromptBinder.Apply(attachment, context, placeholders));
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        return string.Join("\n\n\n", parts);
    }
}
