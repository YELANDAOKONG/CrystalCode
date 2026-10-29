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
            "mode",
            "approval",
            "thinking",
            "tier",
            "speed",
            "context-window",
            "prompt-set",
            "activity",
            "provider",
            "model",
            "session-id",
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

    public StatusLineSettings(
        bool enabled = false,
        IReadOnlyList<string>? fields = null,
        IReadOnlyList<StatusLineRule>? rules = null,
        IReadOnlyList<string>? dropOrder = null)
        : this(enabled, fields, rules, dropOrder, null)
    {
    }

    private StatusLineSettings(
        bool enabled,
        IReadOnlyList<string>? fields,
        IReadOnlyList<StatusLineRule>? rules,
        IReadOnlyList<string>? dropOrder,
        IReadOnlySet<string>? pluginFields)
    {
        Enabled = enabled;
        Fields = Normalize(fields, pluginFields);
        Rules = NormalizeRules(rules, Fields);
        DropOrder = NormalizeDropOrder(dropOrder, Fields, Rules);
    }

    public bool Enabled { get; }

    public IReadOnlyList<string> Fields { get; }

    /// <summary>Rules for the fields that have one, in field order.</summary>
    public IReadOnlyList<StatusLineRule> Rules { get; }

    /// <summary>
    /// Fields to hide first, in this order, when the line does not fit. Fields
    /// that are not listed hide last, from the end of <see cref="Fields"/>.
    /// </summary>
    public IReadOnlyList<string> DropOrder { get; }

    public StatusLineSettings WithEnabled(bool enabled) => new(enabled, Fields, Rules, DropOrder);

    public StatusLineSettings WithFields(IReadOnlyList<string> fields) => ApplyFields(fields, null);

    /// <summary>
    /// Enables the custom line with <paramref name="fields"/>. A name that is
    /// not built in must be in <paramref name="pluginFields"/>.
    /// </summary>
    public StatusLineSettings WithFields(
        IReadOnlyList<string> fields,
        IReadOnlySet<string> pluginFields)
    {
        ArgumentNullException.ThrowIfNull(pluginFields);
        return ApplyFields(fields, pluginFields);
    }

    private StatusLineSettings ApplyFields(
        IReadOnlyList<string> fields,
        IReadOnlySet<string>? pluginFields)
    {
        // Rules and drop order follow the fields that stay on the line.
        var names = Normalize(fields, pluginFields);
        return new StatusLineSettings(
            true,
            names,
            Rules.Where(rule => names.Contains(rule.Field, StringComparer.Ordinal)).ToArray(),
            DropOrder.Where(name => names.Contains(name, StringComparer.Ordinal)).ToArray(),
            pluginFields);
    }

    private static IReadOnlyList<StatusLineRule> NormalizeRules(
        IReadOnlyList<StatusLineRule>? rules,
        IReadOnlyList<string> fields)
    {
        var byField = new Dictionary<string, StatusLineRule>(StringComparer.Ordinal);
        foreach (var rule in rules ?? [])
        {
            ArgumentNullException.ThrowIfNull(rule);
            var name = rule.Field?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!fields.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Status line rule names a field that is not on the line  {rule.Field}", nameof(rules));
            }

            if (rule.Keep && rule.HideBelow is not null)
            {
                throw new ArgumentException($"Status line field cannot be both keep and hideBelow  {name}", nameof(rules));
            }

            if (rule.HideBelow is < 1)
            {
                throw new ArgumentException($"Status line hideBelow must be at least 1  {name}", nameof(rules));
            }

            if (!rule.Keep && rule.HideBelow is null)
            {
                continue;
            }

            if (!byField.TryAdd(name, rule with { Field = name }))
            {
                throw new ArgumentException($"Status line field has more than one rule  {name}", nameof(rules));
            }
        }

        return Array.AsReadOnly(fields.Where(byField.ContainsKey).Select(name => byField[name]).ToArray());
    }

    private static IReadOnlyList<string> NormalizeDropOrder(
        IReadOnlyList<string>? dropOrder,
        IReadOnlyList<string> fields,
        IReadOnlyList<StatusLineRule> rules)
    {
        var normalized = new List<string>();
        foreach (var entry in dropOrder ?? [])
        {
            var name = entry?.Trim().ToLowerInvariant() ?? string.Empty;
            if (!fields.Contains(name, StringComparer.Ordinal))
            {
                throw new ArgumentException($"Status line drop order names a field that is not on the line  {entry}", nameof(dropOrder));
            }

            if (rules.Any(rule => rule.Keep && rule.Field == name))
            {
                throw new ArgumentException($"Status line drop order names a keep field  {name}", nameof(dropOrder));
            }

            if (!normalized.Contains(name, StringComparer.Ordinal))
            {
                normalized.Add(name);
            }
        }

        return Array.AsReadOnly(normalized.ToArray());
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
