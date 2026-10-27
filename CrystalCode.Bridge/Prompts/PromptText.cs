namespace CrystalCode.Bridge.Prompts;

/// <summary>
/// The text one prompt submits, and the warnings to show the client for
/// blocks that were turned into mentions.
/// </summary>
internal sealed record PromptText(string Text, IReadOnlyList<string> Warnings);
