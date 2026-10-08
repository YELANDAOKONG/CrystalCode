using CrystalCode.Engine.Plugins;

namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Values bound into a single prompt template substitution pass.
/// </summary>
public sealed record PromptBinding(
    PromptContext? Session = null,
    ReviewPromptContext? Review = null,
    CompactionPromptContext? Compaction = null,
    PluginPlaceholderTable? Placeholders = null,
    ImagePromptContext? Image = null);
