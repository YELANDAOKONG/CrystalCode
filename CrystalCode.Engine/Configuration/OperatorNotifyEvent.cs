namespace CrystalCode.Engine.Configuration;

/// <summary>
/// A moment when the session waits for the operator. <see cref="Test"/> is
/// sent only by <c>/notify test</c> and cannot be subscribed.
/// </summary>
public sealed record OperatorNotifyEvent
{
    public static OperatorNotifyEvent Approval { get; } = new("approval");

    public static OperatorNotifyEvent Question { get; } = new("question");

    public static OperatorNotifyEvent Turn { get; } = new("turn");

    public static OperatorNotifyEvent Test { get; } = new("test");

    public static IReadOnlyList<OperatorNotifyEvent> Subscribable { get; } =
        Array.AsReadOnly(new[] { Approval, Question, Turn });

    private OperatorNotifyEvent(string value)
    {
        Value = value;
    }

    public string Value { get; }

    public static bool TryParse(string? text, out OperatorNotifyEvent value)
    {
        value = Approval;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var name = text.Trim().ToLowerInvariant();
        foreach (var candidate in Subscribable)
        {
            if (candidate.Value == name)
            {
                value = candidate;
                return true;
            }
        }

        return false;
    }

    public override string ToString() => Value;
}
