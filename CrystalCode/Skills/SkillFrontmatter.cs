using System.Text.RegularExpressions;

namespace CrystalCode.Skills;

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
        var lines = yaml.Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd();
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
            var rawValue = line[(colon + 1)..].Trim();
            var value = TryReadBlockScalar(rawValue, lines, ref i, out var block)
                ? block
                : Unquote(rawValue);
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
            || name.Length == 0
            || description.Length is 0 or > SkillFiles.MaximumDescriptionLength)
        {
            return false;
        }

        frontmatter = new SkillFrontmatter(name, description);
        body = after.Trim();
        return true;
    }

    public override string ToString() => nameof(SkillFrontmatter);

    private static bool TryReadBlockScalar(
        string indicator,
        string[] lines,
        ref int index,
        out string value)
    {
        value = string.Empty;
        if (!IsBlockScalarIndicator(indicator, out var folded))
        {
            return false;
        }

        var parts = new List<string>();
        var i = index + 1;
        while (i < lines.Length)
        {
            var raw = lines[i].TrimEnd();
            if (raw.Length == 0)
            {
                parts.Add(string.Empty);
                i++;
                continue;
            }

            if (raw[0] is not (' ' or '\t'))
            {
                break;
            }

            parts.Add(raw.Trim());
            i++;
        }

        index = i - 1;
        value = folded ? FoldBlock(parts) : JoinLiteral(parts);
        return true;
    }

    private static bool IsBlockScalarIndicator(string value, out bool folded)
    {
        folded = false;
        if (value.Length == 0)
        {
            return false;
        }

        var marker = value[0];
        if (marker is not ('>' or '|'))
        {
            return false;
        }

        for (var i = 1; i < value.Length; i++)
        {
            var current = value[i];
            if (current is '+' or '-')
            {
                continue;
            }

            if (char.IsAsciiDigit(current))
            {
                continue;
            }

            return false;
        }

        folded = marker == '>';
        return true;
    }

    private static string FoldBlock(List<string> parts)
    {
        var pieces = new List<string>();
        foreach (var part in parts)
        {
            if (part.Length == 0)
            {
                continue;
            }

            pieces.Add(part);
        }

        return string.Join(' ', pieces);
    }

    private static string JoinLiteral(List<string> parts)
    {
        var end = parts.Count;
        while (end > 0 && parts[end - 1].Length == 0)
        {
            end--;
        }

        return string.Join('\n', parts.GetRange(0, end));
    }

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
