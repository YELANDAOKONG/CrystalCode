namespace CrystalCode.Engine.Home;

/// <summary>
/// Exclusive claim on one saved session. The operating system holds it on an
/// open <c>&lt;id&gt;.lock</c> file, so it ends with the process even after a
/// crash. Dispose releases it. The empty file stays: deleting it while another
/// process opens it would let two holders lock different files.
/// </summary>
public sealed class SessionLock : IDisposable
{
    private readonly FileStream _stream;

    private SessionLock(string id, FileStream stream)
    {
        Id = id;
        _stream = stream;
    }

    public string Id { get; }

    /// <summary>Returns null when another holder has the session.</summary>
    internal static SessionLock? TryAcquire(string id, string path)
    {
        var options = new FileStreamOptions
        {
            Mode = FileMode.OpenOrCreate,
            Access = FileAccess.ReadWrite,
            Share = FileShare.None
        };
        if (!OperatingSystem.IsWindows())
        {
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        }

        try
        {
            return new SessionLock(id, new FileStream(path, options));
        }
        catch (IOException exception) when (IsHeldElsewhere(exception, path))
        {
            return null;
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new IOException("Session lock cannot be created  " + path, exception);
        }
    }

    // A refused exclusive open reports no distinct code on Unix, so a plain
    // I/O failure on a file that exists means another holder has it. Missing
    // paths and over-long names are real faults and surface as they are.
    private static bool IsHeldElsewhere(IOException exception, string path) =>
        exception is not (DirectoryNotFoundException or FileNotFoundException or PathTooLongException)
        && File.Exists(path);

    public void Dispose() => _stream.Dispose();
}
