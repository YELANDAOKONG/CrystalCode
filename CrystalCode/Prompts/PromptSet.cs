namespace CrystalCode.Prompts;

/// <summary>
/// Resolved prompt texts after default, home, and project overlay.
/// </summary>
public sealed record PromptSet
{
    public PromptSet(string work, string plan, string review, string instructions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(work);
        ArgumentException.ThrowIfNullOrWhiteSpace(plan);
        ArgumentException.ThrowIfNullOrWhiteSpace(review);
        ArgumentNullException.ThrowIfNull(instructions);

        Work = work.Trim();
        Plan = plan.Trim();
        Review = review.Trim();
        Instructions = instructions.Trim();
    }

    public string Work { get; }

    public string Plan { get; }

    public string Review { get; }

    public string Instructions { get; }

    public string WorkSystem =>
        ComposeWork(PromptContext.InstructionsOnly(Instructions));

    public string PlanSystem =>
        ComposePlan(PromptContext.InstructionsOnly(Instructions));

    public string ReviewSystem =>
        ComposeReview(PromptContext.InstructionsOnly(Instructions).WithMode("review"));

    public string ComposeWork(PromptContext context) =>
        PromptBinder.Apply(Work, context);

    public string ComposePlan(PromptContext context) =>
        PromptBinder.Apply(Plan, context);

    public string ComposeReview(PromptContext context) =>
        PromptBinder.Apply(Review, context);

    public override string ToString() => nameof(PromptSet);
}
