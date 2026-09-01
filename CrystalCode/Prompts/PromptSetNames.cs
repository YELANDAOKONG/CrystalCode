using System.Text.RegularExpressions;

namespace CrystalCode.Prompts;

internal static class PromptSetNames
{
    public const string Default = "default";

    private const int MaximumLength = 64;

    private static readonly Regex Pattern = new(
        "^[a-z0-9]+(?:-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public static bool IsValid(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > MaximumLength)
        {
            return false;
        }

        try
        {
            return Pattern.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
