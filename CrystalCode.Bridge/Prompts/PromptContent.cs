using System.Text.Json;
using System.Text.RegularExpressions;

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
/// The invisible image marker prefix is removed from client text, so only an
/// image attached here can produce a trusted marker.
/// </summary>
internal static partial class PromptContent
{
    private const int MaximumValueCharacters = 512;

    private const int MaximumResourceCharacters = 1_000_000;

    private static readonly string[] ImageExtensions = [".png", ".jpg", ".jpeg", ".gif", ".webp"];

    /// <summary>
    /// Reads one prompt. <paramref name="attach"/> attaches image bytes;
    /// <paramref name="attachFile"/> attaches a local image file through the
    /// engine's <c>/attach</c> checks. Each returns the trusted marker or the
    /// reason it failed.
    /// </summary>
    public static async Task<PromptText> ReadAsync(
        JsonElement prompt,
        Func<ReadOnlyMemory<byte>, Task<ImageAttachResult>> attach,
        Func<string, Task<ImageAttachResult>>? attachFile = null)
    {
        ArgumentNullException.ThrowIfNull(attach);
        attachFile ??= static _ => Task.FromResult(new ImageAttachResult(null, string.Empty));
        if (prompt.ValueKind != JsonValueKind.Array)
        {
            throw JsonRpcException.InvalidParams("prompt must be an array of content blocks.");
        }

        if (prompt.EnumerateArray().Any(static block => block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out var type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "audio"))
        {
            // Checked first, so an audio prompt attaches nothing.
            throw JsonRpcException.InvalidParams("Audio input is not supported.");
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
                    parts.Add(Untrusted(JsonParams.RequiredString(block, "text")));
                    break;
                case "image":
                    parts.Add(await ImageAsync(JsonParams.RequiredString(block, "data"), attach, warnings));
                    break;
                case "resource":
                    parts.Add(await ResourceAsync(block, attach, warnings));
                    break;
                case "resource_link":
                    parts.Add(await LinkAsync(block, attachFile));
                    break;
                default:
                    throw JsonRpcException.InvalidParams("Unsupported content block type.");
            }
        }

        var text = string.Join("\n\n", parts.Where(static part => part.Length > 0));
        return new PromptText(text, warnings);
    }

    private static async Task<string> ImageAsync(
        string base64,
        Func<ReadOnlyMemory<byte>, Task<ImageAttachResult>> attach,
        List<string> warnings)
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

        return await AttachAsync(bytes, attach, warnings);
    }

    private static async Task<string> AttachAsync(
        byte[] bytes,
        Func<ReadOnlyMemory<byte>, Task<ImageAttachResult>> attach,
        List<string> warnings)
    {
        var result = await attach(bytes);
        return result.Marker ?? NotAttached(result.Error, warnings);
    }

    private static string NotAttached(string reason, List<string> warnings)
    {
        var line = OneLine(reason.TrimEnd('.'));
        warnings.Add("Image not attached: " + line + ".");
        return "[Image not attached: " + line + "]";
    }

    private static async Task<string> ResourceAsync(
        JsonElement block,
        Func<ReadOnlyMemory<byte>, Task<ImageAttachResult>> attach,
        List<string> warnings)
    {
        if (!block.TryGetProperty("resource", out var resource) || resource.ValueKind != JsonValueKind.Object)
        {
            throw JsonRpcException.InvalidParams("resource needs a resource object.");
        }

        var uri = JsonParams.RequiredString(resource, "uri");
        var mimeType = JsonParams.OptionalString(resource, "mimeType");
        var heading = Heading(uri);
        if (resource.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
        {
            return Fenced(heading, mimeType, textElement.GetString()!);
        }

        var blob = JsonParams.RequiredString(resource, "blob");
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
            return await AttachAsync(bytes, attach, warnings);
        }

        if (IsTextMime(mimeType) && TryUtf8(bytes, out var decoded))
        {
            return Fenced(heading, mimeType, decoded);
        }

        return "[Attached resource not shown: " + OneLine(heading) + " " + OneLine(mimeType ?? "unknown type")
            + ", " + bytes.Length.ToString(System.Globalization.CultureInfo.InvariantCulture)
            + " bytes, binary content is not passed to the model]";
    }

    /// <summary>
    /// A local image link is attached when the engine accepts it; when it
    /// does not (outside what <c>/attach</c> resolves, too large, not a valid
    /// image, or no image input), the link becomes the file mention.
    /// </summary>
    private static async Task<string> LinkAsync(JsonElement block, Func<string, Task<ImageAttachResult>> attachFile)
    {
        var uri = JsonParams.RequiredString(block, "uri");
        if (TryLocalPath(uri, out var imagePath)
            && File.Exists(imagePath)
            && (ImageExtensions.Contains(Path.GetExtension(imagePath), StringComparer.OrdinalIgnoreCase)
                || JsonParams.OptionalString(block, "mimeType")?.StartsWith("image/", StringComparison.OrdinalIgnoreCase) == true)
            && (await attachFile(imagePath)).Marker is { } marker)
        {
            return marker;
        }

        return Link(block, uri);
    }

    private static string Link(JsonElement block, string uri)
    {
        var name = JsonParams.OptionalString(block, "title") ?? JsonParams.RequiredString(block, "name");
        var size = block.TryGetProperty("size", out var sizeElement) && sizeElement.TryGetInt64(out var bytes)
            ? ", " + bytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + " bytes"
            : string.Empty;
        if (TryLocalPath(uri, out var path))
        {
            var kind = Directory.Exists(path) ? "Linked directory" : "Linked file";
            var missing = File.Exists(path) || Directory.Exists(path) ? string.Empty : " (not found)";
            return "[" + kind + ": " + OneLine(name) + " at " + OneLine(path) + size + missing + "]";
        }

        var mimeType = JsonParams.OptionalString(block, "mimeType");
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
            + Untrusted(text.Replace("\r\n", "\n", StringComparison.Ordinal)) + "\n" + fence;
    }

    private static string Untrusted(string text) =>
        text.Replace(ImageMarkerText.Prefix.ToString(), string.Empty, StringComparison.Ordinal);

    /// <summary>
    /// Clients cannot type the invisible marker prefix, so an image attached
    /// by <c>_crystal/image/attach</c> is referenced as its plain
    /// <c>[Image #N]</c>. That plain text becomes the trusted marker only for
    /// a number <paramref name="take"/> accepts, which is a number attached
    /// that way and not yet used, so typed text cannot name another image.
    /// </summary>
    public static string TrustAttached(string text, Func<int, bool> take)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(take);
        return PlainMarker().Replace(text, match =>
            int.TryParse(match.Groups[1].ValueSpan, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number)
            && take(number)
                ? ImageMarkerText.Tag(number)
                : match.Value);
    }

    [GeneratedRegex(@"(?<!\u2063)\[Image #([0-9]{1,9})\]")]
    private static partial Regex PlainMarker();

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
}
