namespace CrystalHarness.Prompts;

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

    public string WorkSystem => Combine(Work, Instructions);

    public string PlanSystem => Combine(Plan, Instructions);

    public override string ToString() => nameof(PromptSet);

    private static string Combine(string prompt, string instructions)
    {
        if (instructions.Length == 0)
        {
            return prompt;
        }

        return prompt + "\n\n## Workspace instructions\n" + instructions;
    }
}
