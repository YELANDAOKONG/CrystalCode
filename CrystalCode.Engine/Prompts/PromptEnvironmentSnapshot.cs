namespace CrystalCode.Engine.Prompts;

/// <summary>
/// Workspace and model facts captured for prompt placeholders.
/// </summary>
public sealed record PromptEnvironmentSnapshot(
    string Workspace,
    string IsGitRepo,
    string Platform,
    string Os,
    string Architecture,
    string Date,
    string Time,
    string Provider,
    string Model,
    string SessionId,
    string Approval);
