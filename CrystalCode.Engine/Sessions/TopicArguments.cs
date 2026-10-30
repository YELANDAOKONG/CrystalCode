namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/topic</c>. A bare command shows the topic and naming settings.
/// <c>set &lt;text&gt;</c> names the session, <c>clear</c> removes the name, and
/// <c>regenerate</c> asks the topic model again. <c>on</c>, <c>off</c>,
/// <c>resume on|off</c>,
/// <c>model [provider model]</c>, <c>thinking</c>, <c>tier</c>, <c>speed</c>,
/// and <c>context</c> configure naming. Any other text is a topic.
/// </summary>
internal static class TopicArguments
{
    internal enum Action
    {
        Show,
        Set,
        Clear,
        Regenerate,
        Switch,
        Resume,
        Model,
        Thinking,
        Slot
    }

    internal readonly record struct Request(
        Action Action,
        bool Enabled = false,
        string? Value = null,
        ApprovalSlotArguments.Request Slot = default);

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
            case "on" or "off" when tail.Length == 0:
                request = new Request(Action.Switch, head.Equals("on", StringComparison.OrdinalIgnoreCase));
                return true;
            case "resume":
                if (!tail.Equals("on", StringComparison.OrdinalIgnoreCase)
                    && !tail.Equals("off", StringComparison.OrdinalIgnoreCase))
                {
                    error = "Use /topic resume on or /topic resume off.";
                    return false;
                }

                request = new Request(Action.Resume, tail.Equals("on", StringComparison.OrdinalIgnoreCase));
                return true;
            case "clear" when tail.Length == 0:
                request = new Request(Action.Clear);
                return true;
            case "regenerate" when tail.Length == 0:
                request = new Request(Action.Regenerate);
                return true;
            case "set":
                if (tail.Length == 0 || SessionTopic.Normalize(tail) is null)
                {
                    error = "Use /topic set <text>.";
                    return false;
                }

                request = new Request(Action.Set, Value: tail);
                return true;
            case "model":
                // A bare model word returns to the session model.
                request = new Request(Action.Model, Value: tail.Length == 0 || tail.Equals("session", StringComparison.OrdinalIgnoreCase) ? null : tail);
                return true;
            case "thinking":
                request = new Request(Action.Thinking, Value: tail.Length == 0 ? null : tail);
                return true;
        }

        if (ApprovalSlotArguments.TryParse(text, out var slot))
        {
            request = new Request(Action.Slot, Slot: slot);
            return true;
        }

        if (SessionTopic.Normalize(text) is null)
        {
            error = "Use /topic set <text>.";
            return false;
        }

        request = new Request(Action.Set, Value: text);
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
