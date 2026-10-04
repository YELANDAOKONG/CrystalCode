namespace CrystalCode.Engine.Events;

/// <summary>
/// Operator display preferences that are stored in <c>config.json</c>.
/// </summary>
public sealed record SessionPreferences(
    bool EstimatedTokens,
    bool VerboseTools,
    bool VerboseCommands,
    bool VerboseApprovals,
    bool VerboseThinking,
    bool StatusLineEnabled,
    IReadOnlyList<string> StatusLineFields);
