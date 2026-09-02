namespace CrystalCode.Prompts;

/// <summary>
/// Per-request values for compaction user templates.
/// </summary>
public sealed record CompactionPromptContext(
    string Conversation,
    string PriorSummarySection,
    string SummaryTask,
    string OutputTemplate,
    string TodosSection);
