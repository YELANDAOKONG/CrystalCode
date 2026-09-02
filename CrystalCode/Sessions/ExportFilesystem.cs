namespace CrystalCode.Sessions;

/// <summary>
/// Ensures export directories exist and reports operator-facing errors.
/// </summary>
public static class ExportFilesystem
{
    public static bool TryEnsureDirectory(string directory, out string error)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        try
        {
            Directory.CreateDirectory(directory);
            error = string.Empty;
            return true;
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or ArgumentException
            or NotSupportedException)
        {
            error = "Export failed  " + exception.Message;
            return false;
        }
    }
}
