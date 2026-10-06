using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Tests.Home;

internal sealed class TemporaryHome : IDisposable
{
    public TemporaryHome()
    {
        Root = Path.Combine(
            Path.GetTempPath(),
            "crystal-harness-tests",
            Guid.NewGuid().ToString("N"));
        Home = new CrystalHome(Root);
    }

    public string Root { get; }

    public CrystalHome Home { get; }

    public void Dispose()
    {
        if (!Directory.Exists(Root))
        {
            return;
        }

        try
        {
            Directory.Delete(Root, recursive: true);
        }
        catch (IOException)
        {
            // Loaded plugin assemblies stay mapped in a non-collectible
            // AssemblyLoadContext until process exit. Recursive delete then
            // fails (Windows access denied; Unix EBUSY or directory not empty).
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
