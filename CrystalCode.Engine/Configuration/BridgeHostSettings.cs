namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Operator choice for which session options <c>crystal bridge</c> offers to
/// an editor as ACP config options. A custom list applies only while
/// <see cref="Custom"/> is on; otherwise <see cref="DefaultOptions"/> apply.
/// Hidden options stay available as slash commands. <see cref="WorkspaceTrust"/>
/// is the trust check for sessions an editor opens, and it is off by default
/// because the editor chooses the directory. A command line never writes this.
/// </summary>
public sealed record BridgeHostSettings
{
    /// <summary>The word that stands for every option, now and later.</summary>
    public const string AllOptions = "all";

    /// <summary>Every option id the engine can offer, in catalog order.</summary>
    public static IReadOnlyList<string> AvailableOptions { get; } =
        Array.AsReadOnly(
        [
            "mode",
            "model",
            "thinking",
            "context",
            "tier",
            "speed",
            "approval",
            "approval_model",
            "approval_thinking",
            "approval_tier",
            "approval_speed",
            "approval_context",
            "extended_approval",
            "extended_thinking",
            "extended_tier",
            "extended_speed",
            "extended_context",
            "prompt_set",
            "agents",
            "compaction_model",
            "compaction_thinking",
            "compaction_tier",
            "compaction_speed",
            "compaction_context",
            "micro_compaction",
            "estimated_tokens"
        ]);

    /// <summary>The options offered while no custom list is on.</summary>
    public static IReadOnlyList<string> DefaultOptions { get; } =
        Array.AsReadOnly(["mode", "model", "thinking", "approval", "agents"]);

    public static BridgeHostSettings Default { get; } = new();

    public BridgeHostSettings(
        bool custom = false,
        IReadOnlyList<string>? options = null,
        bool workspaceTrust = false)
    {
        Custom = custom;
        WorkspaceTrust = workspaceTrust;
        Options = options is null ? DefaultOptions : Normalize(options);
    }

    /// <summary>Whether <see cref="Options"/> replaces the built-in default.</summary>
    public bool Custom { get; }

    /// <summary>
    /// The saved list, or <see cref="AllOptions"/> alone. It is the built-in
    /// default when none was saved.
    /// </summary>
    public IReadOnlyList<string> Options { get; }

    /// <summary>
    /// Whether <c>crystal bridge</c> asks before entering an untrusted
    /// directory. Independent of the terminal's <c>workspaceTrust</c>.
    /// </summary>
    public bool WorkspaceTrust { get; }

    public bool IsDefault => !Custom && !WorkspaceTrust && Options.SequenceEqual(DefaultOptions, StringComparer.Ordinal);

    /// <summary>The ids a front end offers: the list while custom, else the built-in default.</summary>
    public IReadOnlyList<string> Effective => Custom ? Expand(Options) : DefaultOptions;

    /// <summary>Replaces <see cref="AllOptions"/> with every id; an explicit list is returned as it is.</summary>
    public static IReadOnlyList<string> Expand(IReadOnlyList<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return options.Contains(AllOptions, StringComparer.Ordinal) ? AvailableOptions : options;
    }

    /// <summary>
    /// Parses a comma-separated list from a command line. Returns the same
    /// failure text as a saved list.
    /// </summary>
    public static bool TryParse(string? text, out IReadOnlyList<string> options, out string error)
    {
        options = [];
        error = string.Empty;
        try
        {
            options = Normalize((text ?? string.Empty).Split(',', StringSplitOptions.TrimEntries));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = exception.Message;
            return false;
        }
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> options)
    {
        if (options.Count == 0)
        {
            throw new ArgumentException("bridge.configOptions needs at least one option.", nameof(options));
        }

        var normalized = new List<string>();
        foreach (var option in options)
        {
            if (string.IsNullOrWhiteSpace(option))
            {
                throw new ArgumentException("bridge.configOptions entries cannot be empty.", nameof(options));
            }

            var value = option.Trim().ToLowerInvariant();
            if (value != AllOptions && !AvailableOptions.Contains(value, StringComparer.Ordinal))
            {
                throw new ArgumentException(
                    $"Unknown config option  {option}. Use all or: {string.Join(", ", AvailableOptions)}.",
                    nameof(options));
            }

            if (!normalized.Contains(value, StringComparer.Ordinal))
            {
                normalized.Add(value);
            }
        }

        return Array.AsReadOnly(normalized.ToArray());
    }
}
