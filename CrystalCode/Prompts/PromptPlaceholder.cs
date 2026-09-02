namespace CrystalCode.Prompts;

/// <summary>
/// Host-resolved placeholder names for prompt templates.
/// Syntax: <c>{{name}}</c> (case-insensitive, optional surrounding whitespace).
/// </summary>
public static class PromptPlaceholder
{
    public const string Env = "env";

    public const string Skills = "skills";

    public const string Instructions = "instructions";

    public const string InstructionsSection = "instructions_section";

    public const string Workspace = "workspace";

    public const string IsGitRepo = "is_git_repo";

    public const string Platform = "platform";

    public const string Date = "date";

    public const string Provider = "provider";

    public const string Model = "model";

    public const string ModelLine = "model_line";

    public const string Mode = "mode";

    public const string ProductName = "product_name";

    public const string Conversation = "conversation";

    public const string ToolName = "tool_name";

    public const string ToolArguments = "tool_arguments";

    public const string HostRisk = "host_risk";

    public const string HostAuthority = "host_authority";

    public const string ClassificationSummary = "classification_summary";

    public const string PriorSummarySection = "prior_summary_section";

    public const string SummaryTask = "summary_task";

    public const string OutputTemplate = "output_template";

    public const string TodosSection = "todos_section";

    public static IReadOnlyList<string> All { get; } =
    [
        Env,
        Skills,
        Instructions,
        InstructionsSection,
        Workspace,
        IsGitRepo,
        Platform,
        Date,
        Provider,
        Model,
        ModelLine,
        Mode,
        ProductName,
        Conversation,
        ToolName,
        ToolArguments,
        HostRisk,
        HostAuthority,
        ClassificationSummary,
        PriorSummarySection,
        SummaryTask,
        OutputTemplate,
        TodosSection
    ];
}
