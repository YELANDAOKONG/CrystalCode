using System.Text;
using System.Text.Json;

using Crystal.Media;
using Crystal.Multimodal;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Tools.External;

/// <summary>
/// Parses exec stdout that was declared as content JSON.
/// Image bytes are loaded only after the caller decides to attach them.
/// </summary>
internal static class ExecContentOutput
{
    public const int MaximumImages = 8;
    private const int InvalidJsonPreviewCharacters = 2_000;
    private const int ReadBufferBytes = 81920;

    internal readonly record struct PendingImage(string MimeType, string? Base64, string? Path);

    internal readonly record struct ImageLoad(
        bool Succeeded,
        IReadOnlyList<ImageContent> Images,
        string Error)
    {
        public static ImageLoad Ok(IReadOnlyList<ImageContent> images) =>
            new(true, images, string.Empty);

        public static ImageLoad Fail(string error) =>
            new(false, [], error);
    }

    public static bool TryParse(
        string stdout,
        string stderr,
        out string text,
        out IReadOnlyList<PendingImage> images,
        out string error)
    {
        ArgumentNullException.ThrowIfNull(stdout);
        ArgumentNullException.ThrowIfNull(stderr);
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
            error = WithStderr(InvalidJson(body), stderr);
            return false;
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                error = WithStderr(InvalidJson(body), stderr);
                return false;
            }

            if (!TryReadText(document.RootElement, out text, out error))
            {
                error = WithStderr(error, stderr);
                return false;
            }

            if (!TryReadImageRefs(document.RootElement, out var parsed, out error))
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

    public static async ValueTask<ImageLoad> LoadAsync(
        IReadOnlyList<PendingImage> images,
        Workspace workspace,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(images);
        ArgumentNullException.ThrowIfNull(workspace);
        var loaded = new List<ImageContent>(images.Count);
        foreach (var pending in images)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var one = pending.Base64 is not null
                ? LoadBase64(pending)
                : await LoadPathAsync(pending, workspace, cancellationToken);
            if (!one.Succeeded || one.Image is null)
            {
                return ImageLoad.Fail(one.Error);
            }

            loaded.Add(one.Image);
        }

        return ImageLoad.Ok(loaded);
    }

    private readonly record struct OneImage(bool Succeeded, ImageContent? Image, string Error)
    {
        public static OneImage Ok(ImageContent image) => new(true, image, string.Empty);

        public static OneImage Fail(string error) => new(false, null, error);
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

    private static bool TryReadImageRefs(
        JsonElement root,
        out IReadOnlyList<PendingImage> images,
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

        var list = new List<PendingImage>();
        foreach (var item in property.EnumerateArray())
        {
            if (list.Count >= MaximumImages)
            {
                error = $"A tool can return at most {MaximumImages} images.";
                return false;
            }

            if (!TryReadImageRef(item, out var image, out error))
            {
                return false;
            }

            list.Add(image);
        }

        images = list;
        return true;
    }

    private static bool TryReadImageRef(JsonElement item, out PendingImage image, out string error)
    {
        image = default;
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

        if (hasBase64)
        {
            if (base64Property.ValueKind != JsonValueKind.String
                || string.IsNullOrWhiteSpace(base64Property.GetString()))
            {
                error = base64Property.ValueKind == JsonValueKind.String
                    ? "Image base64 cannot be empty."
                    : "Image base64 must be a string.";
                return false;
            }

            image = new PendingImage(declared, base64Property.GetString(), null);
            return true;
        }

        if (pathProperty.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(pathProperty.GetString()))
        {
            error = pathProperty.ValueKind == JsonValueKind.String
                ? "Image path cannot be empty."
                : "Image path must be a string.";
            return false;
        }

        image = new PendingImage(declared, null, pathProperty.GetString());
        return true;
    }

    private static OneImage LoadBase64(PendingImage pending)
    {
        if (!TryDecode(pending.Base64, out var bytes, out var error))
        {
            return OneImage.Fail(error);
        }

        if (!TryCreateImage(pending.MimeType, bytes, out var image, out error))
        {
            return OneImage.Fail(error);
        }

        return OneImage.Ok(image);
    }

    private static async ValueTask<OneImage> LoadPathAsync(
        PendingImage pending,
        Workspace workspace,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(pending.Path))
        {
            return OneImage.Fail("Image path cannot be empty.");
        }

        if (Workspace.IsCredentialPath(pending.Path))
        {
            return OneImage.Fail("Reading credential paths is not allowed.");
        }

        if (!workspace.TryResolveExistingFile(pending.Path, out var fullPath, out var error))
        {
            return OneImage.Fail(error);
        }

        if (Workspace.IsCredentialPath(fullPath))
        {
            return OneImage.Fail("Reading credential paths is not allowed.");
        }

        if (!RegularFile.IsRegular(fullPath))
        {
            return OneImage.Fail("The image file could not be read.");
        }

        byte[] bytes;
        try
        {
            await using var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: ReadBufferBytes,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length == 0)
            {
                return OneImage.Fail("The image file is empty.");
            }

            if (stream.Length > ImageFile.MaximumBytes)
            {
                return OneImage.Fail("The image file exceeds the 20 MiB host limit.");
            }

            bytes = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(bytes, cancellationToken);
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or NotSupportedException)
        {
            return OneImage.Fail("The image file could not be read.");
        }

        if (!TryCreateImage(pending.MimeType, bytes, out var image, out error))
        {
            return OneImage.Fail(error);
        }

        return OneImage.Ok(image);
    }

    private static bool TryDecode(string? text, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;
        var compact = WithoutWhitespace(text ?? string.Empty);
        if (compact.Length == 0)
        {
            error = "Image base64 cannot be empty.";
            return false;
        }

        var maximumChars = ((ImageFile.MaximumBytes + 2) / 3) * 4;
        if (compact.Length > maximumChars)
        {
            error = "The image file exceeds the 20 MiB host limit.";
            return false;
        }

        var buffer = new byte[compact.Length];
        if (!Convert.TryFromBase64String(compact, buffer, out var written) || written == 0)
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

    private static bool TryCreateImage(
        string declared,
        byte[] bytes,
        out ImageContent image,
        out string error)
    {
        image = null!;
        error = string.Empty;
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

    private static string WithoutWhitespace(string text)
    {
        var stripped = false;
        foreach (var character in text)
        {
            if (char.IsWhiteSpace(character))
            {
                stripped = true;
                break;
            }
        }

        if (!stripped)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (!char.IsWhiteSpace(character))
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static string InvalidJson(string body)
    {
        if (body.Length == 0)
        {
            return "Tool output is not valid content JSON.";
        }

        var preview = body.Length <= InvalidJsonPreviewCharacters
            ? body
            : body[..InvalidJsonPreviewCharacters];
        return "Tool output is not valid content JSON.\n" + preview;
    }

    private static string WithStderr(string message, string stderr) =>
        stderr.Length == 0 ? message : message + "\n" + stderr;
}
