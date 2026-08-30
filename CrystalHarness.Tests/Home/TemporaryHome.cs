using CrystalHarness.Home;

namespace CrystalHarness.Tests.Home;

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
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }
}
