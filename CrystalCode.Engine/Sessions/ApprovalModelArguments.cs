namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/approval model</c> arguments. A bare approval argument stays a mode.
/// </summary>
internal static class ApprovalModelArguments
{
    internal readonly record struct Request(bool Show, bool? Enabled, string? Selection);

    public static bool IsModelCommand(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        return trimmed.Equals("model", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("model ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("model\t", StringComparison.OrdinalIgnoreCase);
    }

    public static bool TryParse(string argument, out Request request, out string error)
    {
        request = default;
        error = string.Empty;
        var rest = argument.Trim()["model".Length..].Trim();
        if (rest.Length == 0)
        {
            request = new Request(Show: true, Enabled: null, Selection: null);
            return true;
        }

        if (IsSwitch(rest, out var enabled))
        {
            request = new Request(Show: false, enabled, Selection: null);
            return true;
        }

        if (StartsWithSwitch(rest))
        {
            error = "Approval model is on or off, or a provider and model.";
            return false;
        }

        request = new Request(Show: false, Enabled: null, Selection: rest);
        return true;
    }

    private static bool IsSwitch(string text, out bool enabled)
    {
        enabled = false;
        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            enabled = true;
            return true;
        }

        return text.Equals("off", StringComparison.OrdinalIgnoreCase);
    }

    private static bool StartsWithSwitch(string text) =>
        text.StartsWith("on ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("off ", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("on\t", StringComparison.OrdinalIgnoreCase)
        || text.StartsWith("off\t", StringComparison.OrdinalIgnoreCase);
}
