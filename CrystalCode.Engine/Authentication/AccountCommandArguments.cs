namespace CrystalCode.Engine.Authentication;

/// <summary>
/// The words after <c>/login</c> or <c>/logout</c>: at most one provider name
/// and, for sign-in, the <c>--no-browser</c> option.
/// </summary>
public sealed record AccountCommandArguments(string Provider, bool NoBrowser, string? Error)
{
    public const string NoBrowserOption = "--no-browser";

    public static AccountCommandArguments Parse(string argument, bool allowNoBrowser)
    {
        ArgumentNullException.ThrowIfNull(argument);
        var provider = string.Empty;
        var noBrowser = false;
        foreach (var word in argument.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (word.Equals(NoBrowserOption, StringComparison.OrdinalIgnoreCase))
            {
                if (!allowNoBrowser)
                {
                    return new AccountCommandArguments(string.Empty, false, $"{NoBrowserOption} applies only to /login.");
                }

                noBrowser = true;
            }
            else if (word.StartsWith('-'))
            {
                return new AccountCommandArguments(string.Empty, false, $"Unknown option {word}.");
            }
            else if (provider.Length > 0)
            {
                return new AccountCommandArguments(string.Empty, false, "Name one account provider.");
            }
            else
            {
                provider = word;
            }
        }

        return new AccountCommandArguments(provider, noBrowser, null);
    }
}
