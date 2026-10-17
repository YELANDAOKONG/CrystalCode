using CrystalCode.Plugins.Status;

namespace CrystalCode.Engine.Configuration;

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
            "context-window",
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
            "tier",
            "speed",
            "context-window",
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

    /// <summary>
    /// Enables the custom line with <paramref name="fields"/>. A name that is
    /// not built in must be in <paramref name="pluginFields"/>.
    /// </summary>
    public StatusLineSettings WithFields(
        IReadOnlyList<string> fields,
        IReadOnlySet<string> pluginFields)
    {
        ArgumentNullException.ThrowIfNull(pluginFields);
        return new StatusLineSettings(true, fields, pluginFields);
    }

    private StatusLineSettings(
        bool enabled,
        IReadOnlyList<string>? fields,
        IReadOnlySet<string>? pluginFields)
    {
        Enabled = enabled;
        Fields = Normalize(fields, pluginFields);
    }

    private static IReadOnlyList<string> Normalize(
        IReadOnlyList<string>? fields,
        IReadOnlySet<string>? pluginFields = null)
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
                if (!PluginStatusFieldNames.IsName(value)
                    || (pluginFields is not null && !pluginFields.Contains(value)))
                {
                    throw new ArgumentException($"Unknown status line field  {field}", nameof(fields));
                }
            }

            if (!normalized.Contains(value, StringComparer.Ordinal))
            {
                normalized.Add(value);
            }
        }

        return Array.AsReadOnly(normalized.ToArray());
    }
}
