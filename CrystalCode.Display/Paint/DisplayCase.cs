namespace CrystalCode.Display.Paint;

/// <summary>
/// Title-case tokens for chrome and approval cards.
/// </summary>
public static class DisplayCase
{
    public static string Token(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (value.Length == 0)
        {
            return value;
        }

        return value.ToLowerInvariant() switch
        {
            "todowrite" => "TodoWrite",
            "todoread" => "TodoRead",
            "outside_workspace" => "Outside Workspace",
            "privileged_escalation" => "Privileged Escalation",
            _ => Words(value.Replace('_', ' '))
        };
    }

    private static string Words(string value)
    {
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < parts.Length; i++)
        {
            var word = parts[i];
            parts[i] = char.ToUpperInvariant(word[0]) + word[1..];
        }

        return string.Join(' ', parts);
    }
}
