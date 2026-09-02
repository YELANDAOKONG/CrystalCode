namespace CrystalCode.Approvals;

/// <summary>
/// Selects how side-effect tool calls are approved.
/// </summary>
public sealed record ApprovalMode
{
    public static ApprovalMode Plan { get; } = new("plan");

    public static ApprovalMode Default { get; } = new("default");

    public static ApprovalMode Edit { get; } = new("edit");

    public static ApprovalMode Review { get; } = new("review");

    public static ApprovalMode Audit { get; } = new("audit");

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
                + "(another model checks safety against the conversation) "
                + "or full (pass without review).",
                nameof(value));
        }

        if (mode.Value is "autoedit")
        {
            return Edit;
        }

        if (mode.Value is "fullreview" or "full-review")
        {
            return Audit;
        }

        if (mode.Value is "fullauto" or "yolo")
        {
            return Full;
        }

        if (mode == Plan
            || mode == Default
            || mode == Edit
            || mode == Review
            || mode == Audit
            || mode == Full)
        {
            return mode;
        }

        throw new ArgumentException(
            "Approval mode must be plan, default, edit, review, audit, or full.",
            nameof(value));
    }

    public static ApprovalMode Next(ApprovalMode current)
    {
        if (current == Default)
        {
            return Edit;
        }

        if (current == Edit)
        {
            return Review;
        }

        if (current == Review)
        {
            return Audit;
        }

        if (current == Audit)
        {
            return Full;
        }

        return Default;
    }

    public override string ToString() => Value;
}
