using System.Globalization;

namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/trim</c>. A bare command trims to the configured count, a number
/// keeps that many images, <c>all</c> keeps none, and <c>on</c> and <c>off</c>
/// switch automatic trimming.
/// </summary>
internal static class ResourceTrimArguments
{
    public static bool TryParse(string? argument, out Request request, out string error)
    {
        request = new Request(null, null);
        error = string.Empty;
        var text = argument?.Trim() ?? string.Empty;
        if (text.Length == 0)
        {
            return true;
        }

        if (text.Equals("on", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(null, true);
            return true;
        }

        if (text.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(null, false);
            return true;
        }

        if (text.Equals("all", StringComparison.OrdinalIgnoreCase))
        {
            request = new Request(0, null);
            return true;
        }

        if (int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var keep)
            && keep > 0)
        {
            request = new Request(keep, null);
            return true;
        }

        error = "Trim takes no argument, a positive number of resources to keep, all, on, or off.";
        return false;
    }

    /// <param name="Keep">Resources to keep for a manual trim, or null when the command only switches the setting.</param>
    /// <param name="Enabled">The new automatic trimming state, or null when the command is a manual trim.</param>
    public sealed record Request(int? Keep, bool? Enabled);
}
