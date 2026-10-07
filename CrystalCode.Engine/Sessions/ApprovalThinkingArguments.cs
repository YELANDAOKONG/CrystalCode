namespace CrystalCode.Engine.Sessions;

/// <summary>
/// Parses <c>/approval thinking</c> arguments. A bare command cycles the gear.
/// </summary>
internal static class ApprovalThinkingArguments
{
    internal readonly record struct Request(string? Effort);

    public static bool IsThinkingCommand(string? argument)
    {
        if (string.IsNullOrWhiteSpace(argument))
        {
            return false;
        }

        var trimmed = argument.Trim();
        return trimmed.Equals("thinking", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("thinking ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("thinking\t", StringComparison.OrdinalIgnoreCase);
    }

    public static Request Parse(string argument)
    {
        var rest = argument.Trim()["thinking".Length..].Trim();
        return new Request(rest.Length == 0 ? null : rest);
    }
}
