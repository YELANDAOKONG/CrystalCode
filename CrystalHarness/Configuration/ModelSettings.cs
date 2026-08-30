namespace CrystalHarness.Configuration;

/// <summary>
/// Per-model limits, sampling, and thinking capability. Context size is
/// required. The current thinking gear is a host setting, not a model field.
/// </summary>
public sealed record ModelSettings
{
    public ModelSettings(
        int contextWindow,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        bool thinking = false,
        IReadOnlyList<string>? thinkingEfforts = null)
    {
        if (contextWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(contextWindow),
                contextWindow,
                "Context window must be positive.");
        }

        if (temperature is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(temperature),
                temperature,
                "Temperature must be between 0 and 2.");
        }

        if (topP is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(topP),
                topP,
                "Top-P must be between 0 and 1.");
        }

        if (maxTokens is <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(maxTokens),
                maxTokens,
                "Maximum token count must be positive.");
        }

        ContextWindow = contextWindow;
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        Thinking = thinking;
        ThinkingEfforts = NormalizeEfforts(thinking, thinkingEfforts);
    }

    public int ContextWindow { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? MaxTokens { get; }

    public bool Thinking { get; }

    public IReadOnlyList<string> ThinkingEfforts { get; }

    public bool AllowsEffort(string effort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(effort);
        if (!Thinking || ThinkingEfforts.Count == 0)
        {
            return false;
        }

        if (!ThinkingSelection.TryNormalizeEffort(effort, out var normalized))
        {
            return false;
        }

        foreach (var allowed in ThinkingEfforts)
        {
            if (string.Equals(allowed, normalized, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    public override string ToString() => nameof(ModelSettings);

    private static IReadOnlyList<string> NormalizeEfforts(
        bool thinking,
        IReadOnlyList<string>? thinkingEfforts)
    {
        if (thinkingEfforts is null || thinkingEfforts.Count == 0)
        {
            return [];
        }

        if (!thinking)
        {
            throw new ArgumentException(
                "Thinking efforts require thinking to be enabled.",
                nameof(thinkingEfforts));
        }

        var normalized = new List<string>(thinkingEfforts.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var effort in thinkingEfforts)
        {
            if (string.IsNullOrWhiteSpace(effort))
            {
                throw new ArgumentException(
                    "Thinking effort must be minimal, low, medium, high, or maximum.",
                    nameof(thinkingEfforts));
            }

            // none/off/default are host sentinels, not Crystal effort names.
            if (ThinkingSelection.IsDisabledAlias(effort)
                || new ThinkingSelection(effort) == ThinkingSelection.Default)
            {
                continue;
            }

            if (!ThinkingSelection.TryNormalizeEffort(effort, out var name))
            {
                throw new ArgumentException(
                    "Thinking effort must be minimal, low, medium, high, maximum, or max. "
                    + "Use none or off to disable thinking.",
                    nameof(thinkingEfforts));
            }

            if (!seen.Add(name))
            {
                throw new ArgumentException(
                    $"Thinking effort '{name}' is listed more than once.",
                    nameof(thinkingEfforts));
            }

            normalized.Add(name);
        }

        return normalized;
    }
}
