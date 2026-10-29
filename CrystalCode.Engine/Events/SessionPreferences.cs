using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Events;

/// <summary>
/// Operator display preferences that are stored in <c>config.json</c>.
/// </summary>
public sealed record SessionPreferences(
    bool EstimatedTokens,
    bool TodoBar,
    int TodoBarRows,
    bool VerboseTools,
    bool VerboseCommands,
    bool VerboseResults,
    bool VerboseCalls,
    bool VerboseApprovals,
    bool VerboseReview,
    bool VerboseThinking,
    bool StatusLineEnabled,
    IReadOnlyList<string> StatusLineFields,
    IReadOnlyList<StatusLineRule> StatusLineRules,
    IReadOnlyList<string> StatusLineDrop,
    string Theme,
    bool ApprovalColors);
