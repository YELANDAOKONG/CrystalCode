using System.Text;
using System.Text.RegularExpressions;

namespace CrystalHarness.Tools;

internal sealed class GlobPattern
{
    private readonly Regex _regex;

    private GlobPattern(Regex regex)
    {
        _regex = regex;
    }

    public static bool TryCreate(string pattern, out GlobPattern? glob, out string error)
    {
        glob = null;
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(pattern))
        {
            error = "Glob pattern cannot be empty.";
            return false;
        }

        var normalized = pattern.Replace('\\', '/').Trim();
        if (!normalized.Contains('/') && !normalized.Contains("**", StringComparison.Ordinal))
        {
            normalized = "**/" + normalized;
        }

        try
        {
            glob = new GlobPattern(
                new Regex(
                    "^" + ToRegex(normalized) + "$",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromSeconds(1)));
            return true;
        }
        catch (ArgumentException exception)
        {
            error = "Invalid glob pattern: " + exception.Message;
            return false;
        }
    }

    public bool IsMatch(string relativePath)
    {
        var path = relativePath.Replace('\\', '/').TrimStart('/');
        try
        {
            return _regex.IsMatch(path);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static string ToRegex(string pattern)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < pattern.Length; index++)
        {
            var current = pattern[index];
            if (current == '*' && index + 1 < pattern.Length && pattern[index + 1] == '*')
            {
                if (index + 2 < pattern.Length && pattern[index + 2] == '/')
                {
                    builder.Append("(?:.*/)?");
                    index += 2;
                    continue;
                }

                builder.Append(".*");
                index++;
                continue;
            }

            switch (current)
            {
                case '*':
                    builder.Append("[^/]*");
                    break;
                case '?':
                    builder.Append("[^/]");
                    break;
                default:
                    builder.Append(Regex.Escape(current.ToString()));
                    break;
            }
        }

        return builder.ToString();
    }
}
