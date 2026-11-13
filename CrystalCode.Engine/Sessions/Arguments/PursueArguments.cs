using System.Globalization;

namespace CrystalCode.Engine.Sessions.Arguments;

/// <summary>
/// Parses <c>/pursue</c>. A bare command or <c>status</c> shows the objective.
/// <c>start &lt;objective&gt;</c>, <c>pause</c>, <c>resume</c>, and <c>clear</c>
/// run it. <c>model [provider model]</c>, <c>thinking</c>, <c>tier</c>,
/// <c>speed</c>, and <c>context</c> configure the checker, and
/// <c>limit &lt;name&gt; &lt;number|unlimited&gt;</c> sets a bound. Other text
/// is not an objective; use <c>start</c>.
/// </summary>
internal static class PursueArguments
{
    internal enum Action
    {
        Show,
        Start,
        Pause,
        Resume,
        Clear,
        Model,
        Thinking,
        Slot,
        Compaction,
        Limit
    }

    internal readonly record struct Request(
        Action Action,
        string? Value = null,
        ApprovalSlotArguments.Request Slot = default,
        int? Number = null);

    public static bool TryParse(string? argument, out Request request, out string error)
    {
        request = new Request(Action.Show);
        error = string.Empty;
        var text = argument?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return true;
        }

        var (head, tail) = Split(text);
        switch (head.ToLowerInvariant())
        {
            case "status" when tail.Length == 0:
                return true;
            case "start":
                if (tail.Length == 0)
                {
                    error = "Use /pursue start <objective>.";
                    return false;
                }

                request = new Request(Action.Start, tail);
                return true;
            case "pause" when tail.Length == 0:
                request = new Request(Action.Pause);
                return true;
            case "resume" when tail.Length == 0:
                request = new Request(Action.Resume);
                return true;
            case "clear" when tail.Length == 0:
                request = new Request(Action.Clear);
                return true;
            case "model":
                // A bare model word returns to the session model.
                request = new Request(
                    Action.Model,
                    Value: tail.Length == 0 || tail.Equals("session", StringComparison.OrdinalIgnoreCase) ? null : tail);
                return true;
            case "thinking":
                request = new Request(Action.Thinking, Value: tail.Length == 0 ? null : tail);
                return true;
            case "compaction":
                return TryParseCompaction(tail, out request, out error);
            case "limit":
                return TryParseLimit(tail, out request, out error);
            default:
                break;
        }

        if (ApprovalSlotArguments.TryParse(text, out var slot))
        {
            request = new Request(Action.Slot, Slot: slot);
            return true;
        }

        error = "Use /pursue start <objective>, status, pause, resume, clear, model, thinking, tier, speed, context, or limit.";
        return false;
    }

    private static bool TryParseCompaction(string tail, out Request request, out string error)
    {
        request = new Request(Action.Compaction);
        error = "Use /pursue compaction model [provider model], or thinking, tier, speed, or context [value].";
        var (head, rest) = Split(tail);
        switch (head.ToLowerInvariant())
        {
            case "model":
                request = new Request(
                    Action.Compaction,
                    Value: rest.Length == 0 || rest.Equals("session", StringComparison.OrdinalIgnoreCase) ? null : rest,
                    Number: 0);
                error = string.Empty;
                return true;
            case "thinking":
                request = new Request(Action.Compaction, Value: rest.Length == 0 ? null : rest, Number: 1);
                error = string.Empty;
                return true;
            default:
                if (tail.Length > 0 && ApprovalSlotArguments.TryParse(tail, out var slot))
                {
                    request = new Request(Action.Compaction, Slot: slot, Number: 2);
                    error = string.Empty;
                    return true;
                }

                return false;
        }
    }

    private static bool TryParseLimit(string tail, out Request request, out string error)
    {
        request = new Request(Action.Limit);
        error = "Use /pursue limit <continuations|stalls|model-calls|tool-calls|duration|wait> <number|unlimited>.";
        var parts = tail.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length != 2)
        {
            return false;
        }

        int? number;
        if (parts[1].Equals("unlimited", StringComparison.OrdinalIgnoreCase))
        {
            number = null;
        }
        else if (int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parsed))
        {
            number = parsed;
        }
        else
        {
            return false;
        }

        request = new Request(Action.Limit, Value: parts[0].ToLowerInvariant(), Number: number);
        error = string.Empty;
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
}
