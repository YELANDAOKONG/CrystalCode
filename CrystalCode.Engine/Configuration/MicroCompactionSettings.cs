using CrystalCode.Engine.Compaction;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Local removal of old successful tool results before a model round.
/// An omitted configuration is off and keeps the default cut.
/// </summary>
public sealed record MicroCompactionSettings
{
    public const int DefaultProtectTurns = ToolResultPruner.DefaultProtectTurns;

    public const int DefaultProtectTokens = ToolResultPruner.ProtectTokens;

    public const int DefaultMinimumTokens = ToolResultPruner.MinimumPruneTokens;

    public static MicroCompactionSettings Default { get; } = new(false, DefaultProtectTurns, DefaultProtectTokens, DefaultMinimumTokens);

    public MicroCompactionSettings(
        bool enabled,
        int protectTurns,
        int protectTokens,
        int minimumTokens)
    {
        if (protectTurns < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protectTurns),
                protectTurns,
                "Microcompaction protect turns must be at least 1.");
        }

        if (protectTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(protectTokens),
                protectTokens,
                "Microcompaction protect tokens cannot be negative.");
        }

        if (minimumTokens < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minimumTokens),
                minimumTokens,
                "Microcompaction minimum tokens cannot be negative.");
        }

        Enabled = enabled;
        ProtectTurns = protectTurns;
        ProtectTokens = protectTokens;
        MinimumTokens = minimumTokens;
    }

    public bool Enabled { get; }

    public int ProtectTurns { get; }

    public int ProtectTokens { get; }

    public int MinimumTokens { get; }

    public bool IsDefault =>
        !Enabled
        && ProtectTurns == DefaultProtectTurns
        && ProtectTokens == DefaultProtectTokens
        && MinimumTokens == DefaultMinimumTokens;

    public MicroCompactionSettings WithEnabled(bool enabled) =>
        new(enabled, ProtectTurns, ProtectTokens, MinimumTokens);

    /// <summary>
    /// Status value while enabled. Null while off, so the row stays hidden.
    /// </summary>
    public string? StatusText() =>
        Enabled
            ? "On  " + ProtectTurns + " turns  " + ProtectTokens + "  " + MinimumTokens
            : null;
}
