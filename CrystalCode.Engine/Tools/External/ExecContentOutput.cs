using System.Text.Json;

using Crystal.Media;
using Crystal.Multimodal;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Parses exec stdout that was declared as content JSON.
/// </summary>
internal static class ExecContentOutput
{
    public const int MaximumImages = 8;

    public static bool TryRead(
        string stdout,
        string stderr,
        Workspace workspace,
        out string text,
        out IReadOnlyList<ImageContent> images,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
        ArgumentNullException.ThrowIfNull(workspace);
        text = string.Empty;
        images = [];
        error = string.Empty;
        if (stdout.Length > WorkspaceLimits.MaximumToolOutputCharacters)
        {
            error = WithStderr("Tool output exceeds the output limit.", stderr);
            return false;
        }

        var body = stdout.TrimStart('\uFEFF').Trim();
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            error = WithStderr("Tool output is not valid content JSON.", stderr);
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = WithStderr("Tool output is not valid content JSON.", stderr);
                return false;
            }

            if (!TryReadText(document.RootElement, out text, out error))
            {
                error = WithStderr(error, stderr);
                return false;
            }

            if (!TryReadImages(document.RootElement, workspace, out var parsed, out error))
            {
                error = WithStderr(error, stderr);
                return false;
            }

            images = parsed;
        }

        if (stderr.Length > 0)
        {
            text = text.Length == 0 ? stderr : text + "\n" + stderr;
        }

        return true;
    }

    private static bool TryReadText(JsonElement root, out string text, out string error)
    {
        text = string.Empty;
        error = string.Empty;
        if (!root.TryGetProperty("text", out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.String)
        {
            error = "Content text must be a string.";
            return false;
        }

        text = property.GetString() ?? string.Empty;
        return true;
    }

    private static bool TryReadImages(
        JsonElement root,
        Workspace workspace,
        out IReadOnlyList<ImageContent> images,
        out string error)
    {
        images = [];
        error = string.Empty;
        if (!root.TryGetProperty("images", out var property))
        {
            return true;
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            return true;
        }

        if (property.ValueKind != JsonValueKind.Array)
        {
            error = "Content images must be an array.";
            return false;
        }

        var list = new List<ImageContent>();
        foreach (var item in property.EnumerateArray())
        {
            if (list.Count >= MaximumImages)
            {
                error = $"A tool can return at most {MaximumImages} images.";
                return false;
            }

            if (!TryReadImage(item, workspace, out var image, out error))
            {
                return false;
            }

            list.Add(image);
        }

        images = list;
        return true;
    }

    private static bool TryReadImage(
        JsonElement item,
        Workspace workspace,
        out ImageContent image,
        out string error)
    {
        image = null!;
        error = string.Empty;
        if (item.ValueKind != JsonValueKind.Object)
        {
            error = "Each content image must be a JSON object.";
            return false;
        }

        if (!item.TryGetProperty("mimeType", out var mimeProperty)
            || mimeProperty.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(mimeProperty.GetString()))
        {
            error = "Each content image requires mimeType.";
            return false;
        }

        var declared = mimeProperty.GetString()!.Trim();
        var hasBase64 = item.TryGetProperty("base64", out var base64Property)
            && base64Property.ValueKind != JsonValueKind.Null
            && base64Property.ValueKind != JsonValueKind.Undefined;
        var hasPath = item.TryGetProperty("path", out var pathProperty)
            && pathProperty.ValueKind != JsonValueKind.Null
            && pathProperty.ValueKind != JsonValueKind.Undefined;
        if (hasBase64 == hasPath)
        {
            error = "An image must set exactly one of base64 or path.";
            return false;
        }

        byte[] bytes;
        if (hasBase64)
        {
            if (base64Property.ValueKind != JsonValueKind.String
                || !TryDecode(base64Property.GetString(), out bytes, out error))
            {
                if (error.Length == 0)
                {
                    error = "Image base64 must be a string.";
                }

                return false;
            }
        }
        else if (pathProperty.ValueKind != JsonValueKind.String
            || !TryReadPath(workspace, pathProperty.GetString(), out bytes, out error))
        {
            if (error.Length == 0)
            {
                error = "Image path must be a string.";
            }

            return false;
        }

        var detected = ImageFile.DetectMimeType(bytes);
        if (detected is null)
        {
            error = "The file is not a supported PNG, JPEG, GIF, or WebP image.";
            return false;
        }

        if (!string.Equals(detected, declared, StringComparison.OrdinalIgnoreCase))
        {
            error = "Image bytes do not match mimeType.";
            return false;
        }

        image = new ImageContent(
            new ImageMedia(new InlineMediaSource(bytes), new MediaMimeType(detected)));
        return true;
    }

    private static bool TryDecode(string? text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "Image base64 cannot be empty.";
            return false;
        }

        var maximumChars = ((ImageFile.MaximumBytes + 2) / 3) * 4;
        if (text.Length > maximumChars)
        {
            error = "The image file exceeds the 20 MiB host limit.";
            return false;
        }

        var buffer = new byte[text.Length];
        if (!Convert.TryFromBase64String(text, buffer, out var written) || written == 0)
        {
            error = "Image base64 is not valid.";
            return false;
        }

        if (written > ImageFile.MaximumBytes)
        {
            error = "The image file exceeds the 20 MiB host limit.";
            return false;
        }

        bytes = buffer[..written];
        return true;
    }

    private static bool TryReadPath(
        Workspace workspace,
        string? path,
        out byte[] bytes,
        out string error)
    {
        bytes = [];
        error = string.Empty;
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Image path cannot be empty.";
            return false;
        }

        if (Workspace.IsCredentialPath(path))
        {
            error = "Reading credential paths is not allowed.";
            return false;
        }

        if (!workspace.TryResolveExistingFile(path, out var fullPath, out error))
        {
            return false;
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            error = "Reading credential paths is not allowed.";
            return false;
        }

        try
        {
            using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            if (stream.Length == 0)
            {
                error = "The image file is empty.";
                return false;
            }

            if (stream.Length > ImageFile.MaximumBytes)
            {
                error = "The image file exceeds the 20 MiB host limit.";
                return false;
            }

            bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            error = "The image file could not be read.";
            return false;
        }
    }

    private static string WithStderr(string message, string stderr) =>
        stderr.Length == 0 ? message : message + "\n" + stderr;
}
