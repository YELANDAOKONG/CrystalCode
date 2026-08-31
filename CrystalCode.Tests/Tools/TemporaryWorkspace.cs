namespace CrystalCode.Tests.Tools;

internal sealed class TemporaryWorkspace : IDisposable
{
    public TemporaryWorkspace()
    {
        Path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "crystal-harness-workspace",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Path))
        {
            return;
        }

        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (IOException)
        {
            // Loaded tool assemblies stay mapped in a non-collectible
            // AssemblyLoadContext until process exit. Recursive delete then
            // fails (Windows access denied; Unix EBUSY or directory not empty).
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
