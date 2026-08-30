namespace CrystalHarness.Compaction;

/// <summary>
/// Whether compaction changed the transcript, found nothing to fold, or could not reduce further.
/// </summary>
public sealed record CompactionKind
{
    public static CompactionKind Applied { get; } = new("applied");

    public static CompactionKind Unchanged { get; } = new("unchanged");

    public static CompactionKind Exhausted { get; } = new("exhausted");

    public CompactionKind(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public override string ToString() => Value;
}
