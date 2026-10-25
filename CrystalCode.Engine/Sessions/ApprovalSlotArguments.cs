namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>tier</c> and <c>speed</c> reviewer subcommands. A bare word
/// cycles the slot; a token selects it.
/// </summary>
internal static class ApprovalSlotArguments
{
    internal readonly record struct Request(bool ServiceTier, string? Token);

    public static bool TryParse(string? argument, out Request request)
    {
        request = default;
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        if (TryRest(trimmed, "tier", out var tier))
        {
            request = new Request(ServiceTier: true, tier);
            return true;
        }

        if (TryRest(trimmed, "speed", out var speed))
        {
            request = new Request(ServiceTier: false, speed);
            return true;
        }

        return false;
    }

    private static bool TryRest(string text, string word, out string? rest)
    {
        rest = null;
        if (text.Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (text.Length > word.Length
            && text.StartsWith(word, StringComparison.OrdinalIgnoreCase)
            && char.IsWhiteSpace(text[word.Length]))
        {
            rest = text[word.Length..].Trim();
            return true;
        }

        return false;
    }
}
