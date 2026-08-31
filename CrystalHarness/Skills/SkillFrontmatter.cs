using System.Text.RegularExpressions;

namespace CrystalHarness.Skills;

/// <summary>
/// YAML frontmatter recognized on a SKILL.md file.
/// </summary>
public sealed record SkillFrontmatter
{
    private static readonly Regex NamePattern = new(
        "^[a-z0-9]+(-[a-z0-9]+)*$",
        RegexOptions.CultureInvariant | RegexOptions.Compiled,
        TimeSpan.FromSeconds(1));

    public SkillFrontmatter(string name, string description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Name = name.Trim();
        Description = description.Trim();
    }

    public string Name { get; }

    public string Description { get; }

    public static bool IsValidName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)
            || name.Length > SkillFiles.MaximumNameLength)
        {
            return false;
        }

        try
        {
            return NamePattern.IsMatch(name);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    public static bool TryRead(string text, out SkillFrontmatter? frontmatter, out string body)
    {
        frontmatter = null;
        body = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var source = text.TrimStart('\uFEFF').Replace("\r\n", "\n").Replace('\r', '\n');
        if (!source.StartsWith("---\n", StringComparison.Ordinal)
            && source != "---")
        {
            return false;
        }

        var rest = source.Length >= 4 ? source[4..] : string.Empty;
        var close = rest.IndexOf("\n---", StringComparison.Ordinal);
        if (close < 0)
        {
            return false;
        }

        var yaml = rest[..close];
        var after = rest[(close + 4)..];
        if (after.StartsWith('\n'))
        {
            after = after[1..];
        }
        else if (after.StartsWith("---\n", StringComparison.Ordinal))
        {
            after = after[4..];
        }

        string? name = null;
        string? description = null;
        foreach (var rawLine in yaml.Split('\n'))
        {
            var line = rawLine.TrimEnd();
            if (line.Length == 0 || line[0] is '#' or ' ' or '\t')
            {
                continue;
            }

            var colon = line.IndexOf(':');
            if (colon <= 0)
            {
                continue;
            }

            var key = line[..colon].Trim();
            var value = Unquote(line[(colon + 1)..].Trim());
            if (string.Equals(key, "name", StringComparison.Ordinal))
            {
                name = value;
                continue;
            }

            if (string.Equals(key, "description", StringComparison.Ordinal))
            {
                description = value;
            }
        }

        if (name is null
            || description is null
            || !IsValidName(name)
            || description.Length is 0 or > SkillFiles.MaximumDescriptionLength)
        {
            return false;
        }

        frontmatter = new SkillFrontmatter(name, description);
        body = after.Trim();
        return true;
    }

    public override string ToString() => nameof(SkillFrontmatter);

    private static string Unquote(string value)
    {
        if (value.Length >= 2
            && ((value[0] == '"' && value[^1] == '"')
                || (value[0] == '\'' && value[^1] == '\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}
