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
        IReadOnlyList<string>? reviewAttachments = null)
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
    }

    public string Work { get; }

    public string Plan { get; }

    public string Review { get; }

    public string Instructions { get; }

    public IReadOnlyList<string> WorkAttachments { get; }

    public IReadOnlyList<string> PlanAttachments { get; }

    public IReadOnlyList<string> ReviewAttachments { get; }

    public string WorkSystem =>
        ComposeWork(PromptContext.InstructionsOnly(Instructions));

    public string PlanSystem =>
        ComposePlan(PromptContext.InstructionsOnly(Instructions));

    public string ReviewSystem =>
        ComposeReview(PromptContext.InstructionsOnly(Instructions).WithMode("review"));

    public string ComposeWork(PromptContext context) =>
        AppendAttachments(PromptBinder.Apply(Work, context), WorkAttachments, context);

    public string ComposePlan(PromptContext context) =>
        AppendAttachments(PromptBinder.Apply(Plan, context), PlanAttachments, context);

    public string ComposeReview(PromptContext context) =>
        AppendAttachments(PromptBinder.Apply(Review, context), ReviewAttachments, context);

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
            var trimmed = attachment.Trim();
            if (trimmed.Length > 0)
            {
                copy.Add(trimmed);
            }
        }

        return copy;
    }

    private static string AppendAttachments(
        string body,
        IReadOnlyList<string> attachments,
        PromptContext context)
    {
        if (attachments.Count == 0)
        {
            return body;
        }

        var parts = new List<string> { body };
        foreach (var attachment in attachments)
        {
            var text = PromptBinder.Apply(attachment, context).Trim();
            if (text.Length > 0)
            {
                parts.Add(text);
            }
        }

        return string.Join("\n\n", parts);
    }
}
