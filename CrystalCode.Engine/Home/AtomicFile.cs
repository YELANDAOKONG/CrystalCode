using System.Text;

namespace CrystalCode.Engine.Home;

/// <summary>
/// Replaces a file so a crash or power loss leaves either the previous
/// contents or the new contents, never a truncated file. The bytes go to a
/// uniquely named sibling, are flushed to the device, and then replace the
/// target in one rename. A leftover temporary file starts with a dot and
/// ends in <c>.tmp</c>, so readers that enumerate <c>*.json</c> skip it.
/// </summary>
internal static class AtomicFile
{
    private const UnixFileMode OwnerReadWrite = UnixFileMode.UserRead | UnixFileMode.UserWrite;
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    /// <param name="path">File to replace.</param>
    /// <param name="text">UTF-8 contents without a byte order mark.</param>
    /// <param name="ownerOnly">Create the file readable and writable by the owner only.</param>
    /// <param name="followLink">
    /// Write at the final target when <paramref name="path"/> is a symbolic
    /// link, as an operator-managed dotfile expects. Otherwise the link itself
    /// is replaced, so a link planted in a host-owned directory cannot
    /// redirect the write.
    /// </param>
    public static void WriteText(string path, string text, bool ownerOnly, bool followLink = false)
    {
        ArgumentNullException.ThrowIfNull(text);
        Write(path, Utf8.GetBytes(text), ownerOnly, followLink);
    }

    public static void Write(string path, ReadOnlySpan<byte> data, bool ownerOnly, bool followLink = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var fullPath = Path.GetFullPath(path);
        var target = followLink ? FinalTarget(fullPath) : fullPath;
        var directory = Path.GetDirectoryName(target)
            ?? throw new ArgumentException("The path has no parent directory.", nameof(path));
        var temporaryPath = Path.Combine(
            directory,
            $".{Path.GetFileName(target)}.{Guid.NewGuid():N}.tmp");
        var options = new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            // Set at creation so owner-only contents are never readable by
            // others between write and rename, and a replaced file keeps the
            // mode the operator gave it.
            if (ownerOnly)
            {
                options.UnixCreateMode = OwnerReadWrite;
            }
            else if (IsRegularFile(target))
            {
                options.UnixCreateMode = File.GetUnixFileMode(target);
            }
        }

        try
        {
            using (var stream = new FileStream(temporaryPath, options))
            {
                stream.Write(data);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, target, overwrite: true);
        }
        finally
        {
            File.Delete(temporaryPath);
        }
    }

    private static string FinalTarget(string path)
    {
        var info = new FileInfo(path);
        if (info.LinkTarget is null)
        {
            return path;
        }

        return info.ResolveLinkTarget(returnFinalTarget: true)?.FullName ?? path;
    }

    private static bool IsRegularFile(string path)
    {
        var info = new FileInfo(path);
        return info.Exists && info.LinkTarget is null;
    }
}
