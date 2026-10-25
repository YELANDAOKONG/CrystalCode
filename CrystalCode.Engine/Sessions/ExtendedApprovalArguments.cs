namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/approval extended</c> arguments: show, on or off, a model
/// selection, or one of the thinking, tier, speed, trigger, and primary
/// subcommands.
/// </summary>
internal static class ExtendedApprovalArguments
{
    public const string Word = "extended";

    internal enum Kind
    {
        Show,
        Switch,
        Model,
        Thinking,
        Slot,
        Trigger,
        Primary
    }

    internal readonly record struct Request(
        Kind Kind,
        bool? Enabled = null,
        string? Value = null,
        ApprovalSlotArguments.Request Slot = default);

    public static bool IsExtendedCommand(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        return trimmed.Equals(Word, StringComparison.OrdinalIgnoreCase)
            || (trimmed.Length > Word.Length
                && trimmed.StartsWith(Word, StringComparison.OrdinalIgnoreCase)
                && char.IsWhiteSpace(trimmed[Word.Length]));
    }

    public static bool TryParse(string argument, out Request request, out string error)
    {
        request = default;
        error = string.Empty;
        var rest = argument.Trim()[Word.Length..].Trim();
        if (rest.Length == 0)
        {
            request = new Request(Kind.Show);
            return true;
        }

        if (TrySwitch(rest, out var enabled))
        {
            request = new Request(Kind.Switch, enabled);
            return true;
        }

        if (ApprovalThinkingArguments.IsThinkingCommand(rest))
        {
            request = new Request(Kind.Thinking, Value: ApprovalThinkingArguments.Parse(rest).Effort);
            return true;
        }

        if (ApprovalSlotArguments.TryParse(rest, out var slot))
        {
            request = new Request(Kind.Slot, Slot: slot);
            return true;
        }

        var (head, tail) = Split(rest);
        if (head.Equals("trigger", StringComparison.OrdinalIgnoreCase))
        {
            if (tail.Length == 0)
            {
                error = "Extended approval trigger is low, medium, or high.";
                return false;
            }

            request = new Request(Kind.Trigger, Value: tail);
            return true;
        }

        if (head.Equals("primary", StringComparison.OrdinalIgnoreCase))
        {
            if (!TrySwitch(tail, out var include))
            {
                error = "Extended approval primary is on or off.";
                return false;
            }

            request = new Request(Kind.Primary, include);
            return true;
        }

        if (head.Equals("on", StringComparison.OrdinalIgnoreCase)
            || head.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            error = "Extended approval is on or off, or a provider and model.";
            return false;
        }

        request = new Request(Kind.Model, Value: rest);
        return true;
    }

    private static (string Head, string Tail) Split(string text)
    {
        var index = 0;
        while (index < text.Length && !char.IsWhiteSpace(text[index]))
        {
            index++;
        }

        return (text[..index], text[index..].Trim());
    }

    private static bool TrySwitch(string text, out bool enabled)
    {
        enabled = false;
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        return text.Equals("off", StringComparison.OrdinalIgnoreCase);
    }
}
