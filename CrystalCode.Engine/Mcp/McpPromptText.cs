using System.Text;

using CrystalCode.Engine.Tools;

using ModelContextProtocol.Protocol;

namespace CrystalCode.Engine.Mcp;

/// <summary>
/// Flattens a server prompt into the single user message Crystal submits, and
/// parses the arguments a slash command carries.
/// </summary>
internal static class McpPromptText
{
    public static bool TryParseArguments(
        McpPromptEntry entry,
        string argument,
        out IReadOnlyDictionary<string, string> values,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentNullException.ThrowIfNull(argument);
        error = string.Empty;
        var parsed = new Dictionary<string, string>(StringComparer.Ordinal);
        values = parsed;
        var text = argument.Trim();
        if (entry.Arguments.Count == 0)
        {
            if (text.Length > 0)
            {
                error = $"/{entry.Command} takes no arguments.";
                return false;
            }

            return true;
        }

        // name=value pairs. A value holds spaces when quoted.
        var position = 0;
        while (position < text.Length)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
            {
                position++;
            }

            if (position >= text.Length)
            {
                break;
            }

            var equals = text.IndexOf('=', position);
            if (equals <= position)
            {
                error = UsageText(entry);
                return false;
            }

            var name = text[position..equals];
            if (name.Any(char.IsWhiteSpace) || entry.Arguments.All(item => item.Name != name))
            {
                error = $"/{entry.Command} has no argument '{name}'. {UsageText(entry)}";
                return false;
            }

            position = equals + 1;
            string value;
            if (position < text.Length && text[position] == '"')
            {
                var close = text.IndexOf('"', position + 1);
                if (close < 0)
                {
                    error = $"The value of '{name}' is missing its closing quote.";
                    return false;
                }

                value = text[(position + 1)..close];
                position = close + 1;
            }
            else
            {
                var end = position;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }

                value = text[position..end];
                position = end;
            }

            if (!parsed.TryAdd(name, value))
            {
                error = $"Argument '{name}' is repeated.";
                return false;
            }
        }

        var missing = entry.Arguments.FirstOrDefault(item => item.Required && !parsed.ContainsKey(item.Name));
        if (missing is not null)
        {
            error = $"/{entry.Command} needs '{missing.Name}'. {UsageText(entry)}";
            return false;
        }

        return true;
    }

    public static string UsageText(McpPromptEntry entry) =>
        entry.Arguments.Count == 0
            ? $"Usage: /{entry.Command}"
            : $"Usage: /{entry.Command} "
                + string.Join(" ", entry.Arguments.Select(item => item.Required ? $"{item.Name}=<value>" : $"[{item.Name}=<value>]"));

    /// <summary>
    /// Joins the prompt messages into one user message. Roles other than user
    /// are labeled, because Crystal submits a single user turn. Binary content
    /// is named but not sent.
    /// </summary>
    public static string ToUserMessage(GetPromptResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var parts = new List<string>();
        foreach (var message in result.Messages ?? [])
        {
            var text = Describe(message.Content);
            if (text.Length == 0)
            {
                continue;
            }

            parts.Add(message.Role == Role.Assistant ? "Assistant: " + text : text);
        }

        return string.Join(ToolOutputText.LineSeparator, parts).Trim();
    }

    private static string Describe(ContentBlock? block) =>
        block switch
        {
            TextContentBlock text => text.Text ?? string.Empty,
            ImageContentBlock image => $"[image content omitted: {Mime(image.MimeType)}]",
            AudioContentBlock audio => $"[audio content omitted: {Mime(audio.MimeType)}]",
            ResourceLinkBlock link => new StringBuilder("[resource link: ").Append(link.Uri).Append(']').ToString(),
            EmbeddedResourceBlock { Resource: TextResourceContents text } => text.Text ?? string.Empty,
            EmbeddedResourceBlock { Resource: { } other } => $"[binary resource omitted: {other.Uri}]",
            _ => string.Empty
        };

    private static string Mime(string? mimeType) =>
        string.IsNullOrWhiteSpace(mimeType) ? "unknown type" : mimeType;
}
