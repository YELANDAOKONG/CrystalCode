using System.Security.Cryptography;

using CrystalCode.Engine.Sessions;

namespace CrystalCode.Engine.Home;

/// <summary>Stores validated session images as immutable content-addressed files.</summary>
internal sealed class ImageBlobStore
{
    private readonly CrystalHome _home;

    public ImageBlobStore(CrystalHome home)
    {
        ArgumentNullException.ThrowIfNull(home);
        _home = home;
    }

    public string Store(ReadOnlySpan<byte> data, string mimeType)
    {
        if (data.IsEmpty || data.Length > ImageFile.MaximumBytes)
        {
            throw new InvalidDataException("Image data must be between 1 byte and 20 MiB.");
        }

        if (!string.Equals(ImageFile.DetectMimeType(data), mimeType, StringComparison.Ordinal))
        {
            throw new InvalidDataException("Image MIME type does not match its content.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
        var path = Path.Combine(_home.MediaDirectory, hash);
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(_home.MediaDirectory);
        }
        else
        {
            Directory.CreateDirectory(
                _home.MediaDirectory,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        }

        if (TryLoad(hash, mimeType, out _))
        {
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }

            return hash;
        }

        var temporaryPath = Path.Combine(
            _home.MediaDirectory,
            $".{hash}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            using (var stream = new FileStream(temporaryPath, options))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }

        return hash;
    }

    public bool TryLoad(string? hash, string mimeType, out byte[] data)
    {
        data = [];
        if (hash is not { Length: 64 }
            || !hash.All(static character => character is >= '0' and <= '9'
                or >= 'a' and <= 'f'))
        {
            return false;
        }

        var path = Path.Combine(_home.MediaDirectory, hash);
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                return false;
            }

            using var stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                bufferSize: 81920,
                FileOptions.SequentialScan);
            if (stream.Length is <= 0 or > ImageFile.MaximumBytes)
            {
                return false;
            }

            var bytes = new byte[(int)stream.Length];
            stream.ReadExactly(bytes);
            if (!string.Equals(
                    Convert.ToHexString(SHA256.HashData(bytes)),
                    hash,
                    StringComparison.OrdinalIgnoreCase)
                || !string.Equals(
                    ImageFile.DetectMimeType(bytes),
                    mimeType,
                    StringComparison.Ordinal))
            {
                return false;
            }

            data = bytes;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
