namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses the <c>tier</c>, <c>speed</c>, and <c>context</c> subcommands of a
/// side model. A bare word cycles the slot; a value selects it.
/// </summary>
internal static class ApprovalSlotArguments
{
    internal readonly record struct Request(SideModelSlot Slot, string? Token)
    {
        public bool ServiceTier => Slot == SideModelSlot.ServiceTier;
    }

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
            request = new Request(SideModelSlot.ServiceTier, tier);
            return true;
        }

        if (TryRest(trimmed, "speed", out var speed))
        {
            request = new Request(SideModelSlot.Speed, speed);
            return true;
        }

        if (TryRest(trimmed, "context", out var context))
        {
            request = new Request(SideModelSlot.Context, context);
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
