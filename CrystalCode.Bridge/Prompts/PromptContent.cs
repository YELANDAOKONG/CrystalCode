using System.Text.Json;

using CrystalCode.Bridge.Transport;
using CrystalCode.Engine.Sessions;

namespace CrystalCode.Bridge.Prompts;

/// <summary>
/// Turns the content blocks of one <c>session/prompt</c> into the text the
/// session submits. Every advertised block is accepted: a block the engine
/// cannot use becomes a one-line mention, never an error. Mention text is
/// fixed English authored here; client values inside it are flattened to one
/// line and bounded. Images go through
/// <see cref="CodingSession.TryAttachImage"/>, so the engine checks the bytes.
/// </summary>
internal static class PromptContent
{
    private const int MaximumValueCharacters = 512;

    private const int MaximumResourceCharacters = 1_000_000;

    /// <summary>Attaches image bytes and returns the trusted marker, or the reason it failed.</summary>
    public delegate bool AttachImage(ReadOnlyMemory<byte> data, out string marker, out string error);

    public static PromptText Read(JsonElement prompt, AttachImage attach)
    {
        ArgumentNullException.ThrowIfNull(attach);
        if (prompt.ValueKind != JsonValueKind.Array)
        {
            throw JsonRpcException.InvalidParams("prompt must be an array of content blocks.");
        }

        var parts = new List<string>();
        var warnings = new List<string>();
        foreach (var block in prompt.EnumerateArray())
        {
            if (block.ValueKind != JsonValueKind.Object
                || !block.TryGetProperty("type", out var typeElement)
                || typeElement.ValueKind != JsonValueKind.String)
            {
                throw JsonRpcException.InvalidParams("Each content block needs a type.");
            }

            switch (typeElement.GetString())
            {
                case "text":
                    parts.Add(RequiredString(block, "text"));
                    break;
                case "image":
                    parts.Add(Image(RequiredString(block, "data"), attach, warnings));
                    break;
                case "resource":
                    parts.Add(Resource(block, attach, warnings));
                    break;
                case "resource_link":
                    parts.Add(Link(block));
                    break;
                case "audio":
                    throw JsonRpcException.InvalidParams("Audio input is not supported.");
                default:
                    throw JsonRpcException.InvalidParams("Unsupported content block type.");
            }
        }

        var text = string.Join("\n\n", parts.Where(static part => part.Length > 0));
        return new PromptText(text, warnings);
    }

    private static string Image(string base64, AttachImage attach, List<string> warnings)
    {
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            return NotAttached("the data is not valid base64", warnings);
        }

        return attach(bytes, out var marker, out var error)
            ? marker
            : NotAttached(error, warnings);
    }

    private static string NotAttached(string reason, List<string> warnings)
    {
        var line = OneLine(reason.TrimEnd('.'));
        warnings.Add("Image not attached: " + line + ".");
        return "[Image not attached: " + line + "]";
    }

    private static string Resource(JsonElement block, AttachImage attach, List<string> warnings)
    {
        if (!block.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
        {
            throw JsonRpcException.InvalidParams("resource needs a resource object.");
        }

        var uri = RequiredString(resource, "uri");
        var mimeType = OptionalString(resource, "mimeType");
        var heading = Heading(uri);
        if (resource.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            return Fenced(heading, mimeType, textElement.GetString()!);
        }

        var blob = RequiredString(resource, "blob");
        byte[] bytes;
        try
        {
            bytes = Convert.FromBase64String(blob);
        }
        catch (FormatException)
        {
            throw JsonRpcException.InvalidParams("resource.blob is not valid base64.");
        }

        if (mimeType is not null && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
        {
            return attach(bytes, out var marker, out var error) ? marker : NotAttached(error, warnings);
        }

        if (IsTextMime(mimeType) && TryUtf8(bytes, out var decoded))
        {
            return Fenced(heading, mimeType, decoded);
        }

        return "[Attached resource not shown: " + OneLine(heading) + " " + OneLine(mimeType ?? "unknown type")
            + ", " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " bytes, binary content is not passed to the model]";
    }

    private static string Link(JsonElement block)
    {
        var uri = RequiredString(block, "uri");
        var name = OptionalString(block, "title") ?? RequiredString(block, "name");
        var size = block.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
            ? ", " + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes"
            : string.Empty;
        if (TryLocalPath(uri, out var path))
        {
            var kind = Directory.Exists(path) ? "Linked directory" : "Linked file";
            var missing = File.Exists(path) || Directory.Exists(path) ? string.Empty : " (not found)";
            return "[" + kind + ": " + OneLine(name) + " at " + OneLine(path) + size + missing + "]";
        }

        var mimeType = OptionalString(block, "mimeType");
        var type = mimeType is null ? string.Empty : " (" + OneLine(mimeType) + ")";
        return "[Linked resource: " + OneLine(name) + " " + OneLine(uri) + type + size
            + " - Crystal Code did not open this link]";
    }

    private static string Heading(string uri) => TryLocalPath(uri, out var path) ? path : uri;

    private static string Fenced(string heading, string? mimeType, string text)
    {
        if (text.Length > MaximumResourceCharacters)
        {
            text = text[..MaximumResourceCharacters] + "\n[Resource truncated]";
        }

        var fence = "```";
        while (text.Contains(fence, StringComparison.Ordinal))
        {
            fence += "`";
        }

        var type = mimeType is null ? string.Empty : " (" + OneLine(mimeType) + ")";
        return "Attached resource: " + OneLine(heading) + type + "\n" + fence + "\n"
            + text.Replace("\r\n", "\n", StringComparison.Ordinal) + "\n" + fence;
    }

    /// <summary>A <c>file://</c> URI with no remote host and an absolute local path.</summary>
    private static bool TryLocalPath(string uri, out string path)
    {
        path = string.Empty;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)
            || parsed.Scheme != Uri.UriSchemeFile
            || !(parsed.IsLoopback || string.IsNullOrEmpty(parsed.Host)))
        {
            return false;
        }

        var local = parsed.LocalPath;
        if (!Path.IsPathFullyQualified(local))
        {
            return false;
        }

        path = Path.GetFullPath(local);
        return true;
    }

    private static bool IsTextMime(string? mimeType) =>
        mimeType is not null
        && (mimeType.StartsWith("text/", StringComparison.OrdinalIgnoreCase)
            || mimeType.EndsWith("+json", StringComparison.OrdinalIgnoreCase)
            || mimeType.EndsWith("+xml", StringComparison.OrdinalIgnoreCase)
            || mimeType is "application/json" or "application/xml" or "application/javascript"
                or "application/x-yaml" or "application/yaml" or "application/toml");

    private static bool TryUtf8(byte[] bytes, out string text)
    {
        try
        {
            text = new System.Text.UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
            return true;
        }
        catch (System.Text.DecoderFallbackException)
        {
            text = string.Empty;
            return false;
        }
    }

    /// <summary>One line, no control characters, bounded length.</summary>
    internal static string OneLine(string value)
    {
        var builder = new System.Text.StringBuilder(Math.Min(value.Length, MaximumValueCharacters));
        foreach (var character in value)
        {
            if (builder.Length >= MaximumValueCharacters)
            {
                builder.Append("...");
                break;
            }

            if (char.IsControl(character))
            {
                if (builder.Length > 0 && builder[^1] != ' ')
                {
                    builder.Append(' ');
                }

                continue;
            }

            if (character == ImageMarkerText.Prefix)
            {
                continue;
            }

            builder.Append(character);
        }

        return builder.ToString().Trim();
    }

    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()!
            : throw JsonRpcException.InvalidParams(name + " must be a string.");

    private static string? OptionalString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
