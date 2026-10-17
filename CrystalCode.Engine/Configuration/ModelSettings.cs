namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Per-model limits, sampling, and thinking capability. Context size is
/// required. The current thinking gear is a host setting, not a model field.
/// Audio and video input are stored declarations; the host does not
/// accept or send them.
/// </summary>
public sealed record ModelSettings
{
    public ModelSettings(
        int contextWindow,
        double? temperature = null,
        double? topP = null,
        int? maxTokens = null,
        bool thinking = false,
        IReadOnlyList<string>? thinkingEfforts = null,
        bool imageInput = false,
        bool thinkingCanDisable = true,
        bool audioInput = false,
        bool videoInput = false,
        IReadOnlyList<int>? contextWindows = null)
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
        var windows = contextWindows is { Count: > 0 } ? contextWindows.ToArray() : [contextWindow];
        if (windows.Any(window => window <= 0 || window > contextWindow)
            || windows.Distinct().Count() != windows.Length)
        {
            throw new ArgumentException(
                "Context windows must be distinct positive token counts within contextWindow.",
                nameof(contextWindows));
        }

        Array.Sort(windows);
        ContextWindows = Array.AsReadOnly(windows);
        Temperature = temperature;
        TopP = topP;
        MaxTokens = maxTokens;
        Thinking = thinking;
        ThinkingEfforts = NormalizeEfforts(thinking, thinkingEfforts);
        ImageInput = imageInput;
        ThinkingCanDisable = thinkingCanDisable;
        AudioInput = audioInput;
        VideoInput = videoInput;
    }

    public int ContextWindow { get; }

    public IReadOnlyList<int> ContextWindows { get; }

    public double? Temperature { get; }

    public double? TopP { get; }

    public int? MaxTokens { get; }

    public bool Thinking { get; }

    public IReadOnlyList<string> ThinkingEfforts { get; }

    /// <summary>Gets whether the configured model accepts image input.</summary>
    public bool ImageInput { get; }

    /// <summary>
    /// Gets whether the host may ask this model to disable thinking.
    /// Gemini 3 models reject that request.
    /// </summary>
    public bool ThinkingCanDisable { get; }

    /// <summary>
    /// Gets whether the operator declared audio input. The host stores the
    /// declaration and does not accept or send audio.
    /// </summary>
    public bool AudioInput { get; }

    /// <summary>
    /// Gets whether the operator declared video input. The host stores the
    /// declaration and does not accept or send video.
    /// </summary>
    public bool VideoInput { get; }

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
                    "Thinking effort must be an effort token that starts with a letter "
                    + "and then uses only letters, digits, and hyphens.",
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
                    "Thinking effort must be an effort token that starts with a letter "
                    + "and then uses only letters, digits, and hyphens. "
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
