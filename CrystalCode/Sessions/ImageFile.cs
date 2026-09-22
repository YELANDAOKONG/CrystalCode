namespace CrystalCode.Sessions;

/// <summary>Loads supported local image files without decoding or transforming them.</summary>
public static class ImageFile
{
    public const int MaximumBytes = 20 * 1024 * 1024;

    public static ImageAttachment Load(string path, int number)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        using var stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 81920,
            FileOptions.SequentialScan);
        if (stream.Length == 0)
        {
            throw new InvalidDataException("The image file is empty.");
        }

        if (stream.Length > MaximumBytes)
        {
            throw new InvalidDataException("The image file exceeds the 20 MiB host limit.");
        }

        var data = new byte[stream.Length];
        stream.ReadExactly(data);
        var mimeType = DetectMimeType(data)
            ?? throw new InvalidDataException(
                "The file is not a supported PNG, JPEG, GIF, or WebP image.");
        return new ImageAttachment(number, mimeType, data);
    }

    public static string? DetectMimeType(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> png =
            [0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a];
        if (data.StartsWith(png))
        {
            return "image/png";
        }

        ReadOnlySpan<byte> jpeg = [0xff, 0xd8, 0xff];
        if (data.StartsWith(jpeg))
        {
            return "image/jpeg";
        }

        if (data.StartsWith("GIF87a"u8) || data.StartsWith("GIF89a"u8))
        {
            return "image/gif";
        }

        if (data.Length >= 12
            && data[..4].SequenceEqual("RIFF"u8)
            && data.Slice(8, 4).SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }
}
