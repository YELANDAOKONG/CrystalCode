namespace CrystalHarness.Approvals;

/// <summary>
/// Selects how side-effect tool calls are approved.
/// </summary>
public sealed record ApprovalMode
{
    public static ApprovalMode Plan { get; } = new("plan");

    public static ApprovalMode Default { get; } = new("default");

    public static ApprovalMode AutoEdit { get; } = new("autoedit");

    public static ApprovalMode Review { get; } = new("review");

    public static ApprovalMode Full { get; } = new("full");

    public ApprovalMode(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ApprovalMode Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        var mode = new ApprovalMode(value);
        if (mode.Value is "auto" or "automatic")
        {
            throw new ArgumentException(
                "Approval mode 'auto' is ambiguous. Use review "
                + "(another model checks safety and the user request) "
                + "or full (pass without review).",
                nameof(value));
        }

        if (mode.Value is "fullauto" or "yolo")
        {
            return Full;
        }

        if (mode == Plan
            || mode == Default
            || mode == AutoEdit
            || mode == Review
            || mode == Full)
        {
            return mode;
        }

        throw new ArgumentException(
            "Approval mode must be plan, default, autoedit, review, or full.",
            nameof(value));
    }

    public override string ToString() => Value;
}
