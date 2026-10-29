using CrystalCode.Engine.Configuration;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/describe</c> arguments. A bare command lists the resource
/// description models. <c>image</c>, <c>audio</c>, or <c>video</c> shows that
/// model; then <c>on</c>, <c>off</c>, <c>model</c>, <c>thinking</c>,
/// <c>tier</c>, <c>speed</c>, or <c>context</c> changes it.
/// </summary>
internal static class ResourceModelArguments
{
    internal enum Action
    {
        List,
        Show,
        Switch,
        Model,
        Thinking,
        Slot
    }

    internal readonly record struct Request(
        Action Action,
        ResourceKind Kind = ResourceKind.Image,
        bool Enabled = false,
        string? Value = null,
        ApprovalSlotArguments.Request Slot = default);

    public const string Usage =
        "Use /describe <image|audio|video> [on|off|model <provider model>|thinking|tier|speed|context [value]].";

    public static bool TryParse(string? argument, out Request request, out string error)
    {
        request = new Request(Action.List);
        error = string.Empty;
        var text = argument?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return true;
        }

        var (head, tail) = Split(text);
        if (!ResourceKinds.TryParse(head, out var kind))
        {
            error = Usage;
            return false;
        }

        if (tail.Length == 0)
        {
            request = new Request(Action.Show, kind);
            return true;
        }

        if (tail.Equals("on", StringComparison.OrdinalIgnoreCase)
            || tail.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(Action.Switch, kind, tail.Equals("on", StringComparison.OrdinalIgnoreCase));
            return true;
        }

        var (word, value) = Split(tail);
        if (word.Equals("model", StringComparison.OrdinalIgnoreCase))
        {
            if (value.Length == 0)
            {
                request = new Request(Action.Show, kind);
                return true;
            }

            request = new Request(Action.Model, kind, Value: value);
            return true;
        }

        if (word.Equals("thinking", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(Action.Thinking, kind, Value: value.Length == 0 ? null : value);
            return true;
        }

        if (ApprovalSlotArguments.TryParse(tail, out var slot))
        {
            request = new Request(Action.Slot, kind, Slot: slot);
            return true;
        }

        error = Usage;
        return false;
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
