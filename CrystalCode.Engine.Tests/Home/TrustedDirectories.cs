using System.Text.Json;

using CrystalCode.Engine.Home;

namespace CrystalCode.Engine.Tests.Home;

internal static class TrustedDirectories
{
    public static IReadOnlyList<string> Read(string path)
    {
        var document = JsonSerializer.Deserialize<TrustedDirectoryDocument>(
                File.ReadAllText(path),
                HomeJson.Options)
            ?? new TrustedDirectoryDocument();
        document.Directories ??= [];
        return document.Directories;
    }
}
