namespace CrystalCode.Configuration;

/// <summary>
/// Operator selection for the optional custom status line.
/// </summary>
public sealed record StatusLineSettings
{
    public static IReadOnlyList<string> DefaultFields { get; } =
        Array.AsReadOnly(
        new[]
        {
            "approval",
            "thinking",
            "model",
            "context-used",
            "session-total",
            "workspace"
        });

    public static IReadOnlyList<string> AvailableFields { get; } =
        Array.AsReadOnly(
        new[]
        {
            "approval",
            "thinking",
            "prompt-set",
            "activity",
            "model",
            "workspace",
            "context-used",
            "context-left",
            "context-tokens",
            "request-input",
            "request-output",
            "request-total",
            "session-input",
            "session-output",
            "session-total",
            "tools",
            "elapsed",
            "queued"
        });

    public StatusLineSettings(bool enabled = false, IReadOnlyList<string>? fields = null)
    {
        Enabled = enabled;
        Fields = Normalize(fields);
    }

    public bool Enabled { get; }

    public IReadOnlyList<string> Fields { get; }

    public StatusLineSettings WithEnabled(bool enabled) => new(enabled, Fields);

    public StatusLineSettings WithFields(IReadOnlyList<string> fields) => new(true, fields);

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string>? fields)
    {
        if (fields is null || fields.Count == 0)
        {
            return Array.AsReadOnly(DefaultFields.ToArray());
        }

        var normalized = new List<string>();
        foreach (var field in fields)
        {
            if (string.IsNullOrWhiteSpace(field))
            {
                throw new ArgumentException("Status line fields cannot be empty.", nameof(fields));
            }

            var value = field.Trim().ToLowerInvariant();
            if (!AvailableFields.Contains(value, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Unknown status line field  {field}", nameof(fields));
            }

            if (!normalized.Contains(value, StringComparer.Ordinal))
            {
                normalized.Add(value);
            }
        }

        return Array.AsReadOnly(normalized.ToArray());
    }
}
