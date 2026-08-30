using Crystal.Reasoning;

namespace CrystalHarness.Configuration;

/// <summary>
/// Host-selected thinking gear. Not a model capability.
/// <c>default</c> and <c>off</c> are host sentinels; other values are
/// Crystal <see cref="ReasoningEffort"/> names.
/// </summary>
public sealed record ThinkingSelection
{
    public static ThinkingSelection Default { get; } = new("default");

    public static ThinkingSelection Off { get; } = new("off");

    public ThinkingSelection(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        Value = value.Trim().ToLowerInvariant();
    }

    public string Value { get; }

    public static ThinkingSelection Parse(string value)
    {
        var selection = new ThinkingSelection(value);
        if (IsDisabledAlias(selection.Value))
        {
            return Off;
        }

        if (selection == Default)
        {
            return selection;
        }

        if (TryNormalizeEffort(selection.Value, out var effort))
        {
            return new ThinkingSelection(effort);
        }

        throw new ArgumentException(
            "Thinking effort must be default, off, none, minimal, low, medium, high, maximum, or max.",
            nameof(value));
    }

    public static bool IsDisabledAlias(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        return normalized is "off" or "none";
    }

    public static bool TryNormalizeEffort(string value, out string effort)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var normalized = value.Trim().ToLowerInvariant();
        if (normalized is "minimal" or "low" or "medium" or "high" or "maximum")
        {
            effort = normalized;
            return true;
        }

        if (normalized == "max")
        {
            effort = "maximum";
            return true;
        }

        effort = string.Empty;
        return false;
    }

    /// <summary>
    /// Maps this host choice onto the active model. Switching models never
    /// fails: unsupported thinking is omitted, and an unsupported gear
    /// falls back to the provider default without changing this value.
    /// </summary>
    public ReasoningOptions? ToReasoningOptions(ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Thinking)
        {
            return null;
        }

        if (this == Off)
        {
            return new ReasoningOptions(ReasoningMode.Disabled);
        }

        if (this == Default || !model.AllowsEffort(Value))
        {
            return new ReasoningOptions(ReasoningMode.Automatic);
        }

        return new ReasoningOptions(ReasoningMode.Enabled, new ReasoningEffort(Value));
    }

    public static IReadOnlyList<ThinkingSelection> ChoicesFor(ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(model);
        if (!model.Thinking)
        {
            return [];
        }

        var choices = new List<ThinkingSelection> { Default, Off };
        foreach (var effort in model.ThinkingEfforts)
        {
            choices.Add(new ThinkingSelection(effort));
        }

        return choices;
    }

    public static ThinkingSelection Next(ThinkingSelection current, ModelSettings model)
    {
        ArgumentNullException.ThrowIfNull(current);
        var choices = ChoicesFor(model);
        if (choices.Count == 0)
        {
            return current;
        }

        var index = -1;
        for (var i = 0; i < choices.Count; i++)
        {
            if (choices[i] == current)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return choices[0];
        }

        return choices[(index + 1) % choices.Count];
    }

    public override string ToString() => Value;
}
