namespace CrystalCode.Prompts;

internal sealed record PromptResolution(
    PromptSet Prompts,
    string PromptSet,
    IReadOnlyList<string> AvailableSets,
    PromptSource WorkSource,
    PromptSource PlanSource,
    PromptSource ReviewSource,
    IReadOnlyList<string> Notes);
