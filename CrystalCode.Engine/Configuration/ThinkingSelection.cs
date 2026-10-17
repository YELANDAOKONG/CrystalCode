using Crystal.Reasoning;

namespace CrystalCode.Engine.Configuration;

/// <summary>
/// Host-selected thinking gear. Not a model capability and not a wire value.
/// <c>default</c> and <c>off</c> are host sentinels. Other values are the
/// effort tokens the endpoint accepts. The host stores them in lowercase and
/// does not interpret them. <c>max</c> and <c>maximum</c> stay distinct.
/// Adapters forward the stored token unchanged.
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
            "Thinking effort must be default, off, none, "
            + "or an effort token that starts with a letter and then uses only letters, digits, underscores, and hyphens.",
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
        if (IsEffortToken(normalized))
        {
            effort = normalized;
            return true;
        }

        effort = string.Empty;
        return false;
    }

    /// <summary>
    /// An endpoint token, not a host sentinel. Adapters forward it unchanged.
    /// </summary>
    private static bool IsEffortToken(string value)
    {
        if (value.Length == 0 || value is "default" or "off" or "none" || value[0] is < 'a' or > 'z')
        {
            return false;
        }

        for (var index = 1; index < value.Length; index++)
        {
            var character = value[index];
            if (character is not ((>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_'))
            {
                return false;
            }
        }

        return true;
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
            if (!model.ThinkingCanDisable)
            {
                return new ReasoningOptions(ReasoningMode.Automatic);
            }

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

        var choices = new List<ThinkingSelection> { Default };
        if (model.ThinkingCanDisable)
        {
            choices.Add(Off);
        }

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
