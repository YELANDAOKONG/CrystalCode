namespace CrystalHarness.Tests.Tools;

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
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
