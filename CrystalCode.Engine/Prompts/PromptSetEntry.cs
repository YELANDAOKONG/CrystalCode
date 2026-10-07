namespace CrystalCode.Engine.Prompts;

/// <summary>
/// One prompt set shown in <c>/promptset</c>. Effective is the single enabled set.
/// </summary>
internal sealed record PromptSetEntry(
    string Name,
    string Title,
    string Description,
    bool Enabled,
    bool Effective);
