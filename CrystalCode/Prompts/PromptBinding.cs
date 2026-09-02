namespace CrystalCode.Prompts;

/// <summary>
/// Values bound into a single prompt template substitution pass.
/// </summary>
public sealed record PromptBinding(
    PromptContext? Session = null,
    ReviewPromptContext? Review = null,
    CompactionPromptContext? Compaction = null);
