namespace CrystalCode.Engine.Home;

public sealed record QuickPromptCatalog(
    string Directory,
    IReadOnlyList<QuickPrompt> Entries,
    string? Error = null);
