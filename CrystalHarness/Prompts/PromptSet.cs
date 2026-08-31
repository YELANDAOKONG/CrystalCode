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

    public string WorkSystem => Combine(Work, string.Empty, string.Empty, Instructions);

    public string PlanSystem => Combine(Plan, string.Empty, string.Empty, Instructions);

    public string ComposeWork(string environment, string skills = "") =>
        Combine(Work, environment, skills, Instructions);

    public string ComposePlan(string environment, string skills = "") =>
        Combine(Plan, environment, skills, Instructions);

    public override string ToString() => nameof(PromptSet);

    private static string Combine(string prompt, string environment, string skills, string instructions)
    {
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(skills);
        var text = prompt;
        if (environment.Trim().Length > 0)
        {
            text += "\n\n" + environment.Trim();
        }

        if (skills.Trim().Length > 0)
        {
            text += "\n\n" + skills.Trim();
        }

        if (instructions.Length > 0)
        {
            text += "\n\n## Workspace instructions\n" + instructions;
        }

        return text;
    }
}
