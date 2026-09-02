namespace CrystalCode.Prompts;

/// <summary>
/// Workspace and model facts captured for prompt placeholders.
/// </summary>
public sealed record PromptEnvironmentSnapshot(
    string Workspace,
    string IsGitRepo,
    string Platform,
    string Date,
    string Provider,
    string Model);
